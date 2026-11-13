using System.Text.Json;
using Npgsql;
using UniversalErp.Application.Purchasing;

namespace UniversalErp.Infrastructure.Purchasing;

public sealed class PostgresSupplyChainOutboxRepository(NpgsqlDataSource dataSource)
    : ISupplyChainOutboxRepository
{
    private sealed class GrnLinePayload
    {
        public int LineNumber { get; set; }
        public Guid VariantId { get; set; }
        public Guid BinId { get; set; }
        public decimal ReceivedQty { get; set; }
        public Guid UnitOfMeasureId { get; set; }
        public decimal UnitPrice { get; set; }
    }

    private sealed class GrnOutboxPayload
    {
        public Guid GrnId { get; set; }
        public Guid PurchaseOrderId { get; set; }
        public Guid ClientTransactionId { get; set; }
        public Guid WarehouseId { get; set; }
        public List<GrnLinePayload> Lines { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public const int MaxRetryAttempts = 5;

    public async Task<OutboxProcessResult> ProcessPendingOutboxEventsAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await SetScopeAsync(connection, tenantId, companyId, cancellationToken);

        // 1. Fetch eligible candidate event IDs (ordered by occurred_at for FIFO processing)
        const string candidateSql = """
            SELECT event_id
            FROM erp_purchasing.outbox_event
            WHERE tenant_id = @tenantId AND company_id = @companyId
              AND status = 'pending' AND published_at IS NULL
              AND (next_retry_at IS NULL OR next_retry_at <= now())
            ORDER BY occurred_at ASC
            LIMIT 50;
            """;

        var candidateIds = new List<Guid>();
        await using (var cmd = new NpgsqlCommand(candidateSql, connection))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidateIds.Add(reader.GetGuid(0));
            }
            await reader.CloseAsync();
        }

        if (candidateIds.Count == 0)
        {
            return new OutboxProcessResult(0, 0, 0, []);
        }

        var processedCount = 0;
        var movementsCreated = 0;
        var journalsPosted = 0;
        var failedCount = 0;
        var deadLetterCount = 0;
        var details = new List<string>();

        // 2. Process each event in its own isolated transaction
        foreach (var eventId in candidateIds)
        {
            await using var eventTx = await connection.BeginTransactionAsync(cancellationToken);

            Guid grnId;
            string eventType;
            string payloadRaw;
            int attemptCount;

            // Concurrency safety: Lock candidate event under FOR UPDATE SKIP LOCKED
            const string lockSql = """
                SELECT event_id, grn_id, event_type, payload, attempt_count
                FROM erp_purchasing.outbox_event
                WHERE tenant_id = @tenantId AND company_id = @companyId AND event_id = @eventId
                  AND status = 'pending' AND published_at IS NULL
                  AND (next_retry_at IS NULL OR next_retry_at <= now())
                FOR UPDATE SKIP LOCKED;
                """;

            await using (var lockCmd = new NpgsqlCommand(lockSql, connection, eventTx))
            {
                lockCmd.Parameters.AddWithValue("tenantId", tenantId);
                lockCmd.Parameters.AddWithValue("companyId", companyId);
                lockCmd.Parameters.AddWithValue("eventId", eventId);

                await using var reader = await lockCmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    // Skipped by another worker or already completed
                    await reader.CloseAsync();
                    await eventTx.RollbackAsync(cancellationToken);
                    continue;
                }

                grnId = reader.GetGuid(1);
                eventType = reader.GetString(2);
                payloadRaw = reader.GetString(3);
                attemptCount = reader.GetInt32(4);
                await reader.CloseAsync();
            }

            try
            {
                // A. Validate event type (auditable dead-letter transition, no silent publish)
                if (eventType != "grn.posted")
                {
                    throw new InvalidOperationException($"UNSUPPORTED_EVENT_TYPE: {eventType}");
                }

                var payload = JsonSerializer.Deserialize<GrnOutboxPayload>(payloadRaw, JsonOptions);
                if (payload is null || payload.Lines.Count == 0)
                {
                    throw new InvalidOperationException("EMPTY_OR_MALFORMED_PAYLOAD: GRN requires at least one line.");
                }

                // B. Fetch GRN details
                string grnNumber;
                DateOnly receivedDate;
                await using (var grnCmd = new NpgsqlCommand(
                    "SELECT grn_number, received_date FROM erp_purchasing.goods_received_note WHERE tenant_id = @tenantId AND company_id = @companyId AND grn_id = @grnId",
                    connection,
                    eventTx))
                {
                    grnCmd.Parameters.AddWithValue("tenantId", tenantId);
                    grnCmd.Parameters.AddWithValue("companyId", companyId);
                    grnCmd.Parameters.AddWithValue("grnId", grnId);
                    await using var grnReader = await grnCmd.ExecuteReaderAsync(cancellationToken);
                    if (await grnReader.ReadAsync(cancellationToken))
                    {
                        grnNumber = grnReader.GetString(0);
                        receivedDate = DateOnly.FromDateTime(grnReader.GetDateTime(1));
                    }
                    else
                    {
                        grnNumber = $"GRN-{grnId.ToString()[..8]}";
                        receivedDate = DateOnly.FromDateTime(DateTime.UtcNow);
                    }
                    await grnReader.CloseAsync();
                }

                // C. Process Inventory Movement & Balances (deduplicated)
                bool movementExists;
                await using (var chkMovCmd = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM erp_inventory.inventory_movement WHERE tenant_id = @tenantId AND company_id = @companyId AND source_document_type = 'grn' AND source_document_id = @grnId)",
                    connection,
                    eventTx))
                {
                    chkMovCmd.Parameters.AddWithValue("tenantId", tenantId);
                    chkMovCmd.Parameters.AddWithValue("companyId", companyId);
                    chkMovCmd.Parameters.AddWithValue("grnId", grnId);
                    movementExists = (bool)(await chkMovCmd.ExecuteScalarAsync(cancellationToken) ?? false);
                }

                if (!movementExists)
                {
                    var movementId = Guid.NewGuid();
                    var occurredAt = DateTimeOffset.UtcNow;
                    var note = $"GRN Receipt for {grnNumber}";

                    await using (var movCmd = new NpgsqlCommand(
                        "INSERT INTO erp_inventory.inventory_movement (tenant_id, company_id, movement_id, client_transaction_id, movement_type, source_document_type, source_document_id, occurred_at, created_by, note) VALUES (@tenantId, @companyId, @movementId, @txId, 'receipt', 'grn', @grnId, @occurredAt, @createdBy, @note)",
                        connection,
                        eventTx))
                    {
                        movCmd.Parameters.AddWithValue("tenantId", tenantId);
                        movCmd.Parameters.AddWithValue("companyId", companyId);
                        movCmd.Parameters.AddWithValue("movementId", movementId);
                        movCmd.Parameters.AddWithValue("txId", payload.ClientTransactionId);
                        movCmd.Parameters.AddWithValue("grnId", grnId);
                        movCmd.Parameters.AddWithValue("occurredAt", occurredAt);
                        movCmd.Parameters.AddWithValue("createdBy", userId);
                        movCmd.Parameters.AddWithValue("note", note);
                        await movCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    for (var i = 0; i < payload.Lines.Count; i++)
                    {
                        var line = payload.Lines[i];
                        await using var legCmd = new NpgsqlCommand(
                            "INSERT INTO erp_inventory.inventory_movement_leg (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id, bin_id, status_code, quantity_delta, base_uom_id) VALUES (@tenantId, @companyId, @movementId, @legNum, @variantId, @whId, @binId, 'available', @qty, @uomId)",
                            connection,
                            eventTx);
                        legCmd.Parameters.AddWithValue("tenantId", tenantId);
                        legCmd.Parameters.AddWithValue("companyId", companyId);
                        legCmd.Parameters.AddWithValue("movementId", movementId);
                        legCmd.Parameters.AddWithValue("legNum", i + 1);
                        legCmd.Parameters.AddWithValue("variantId", line.VariantId);
                        legCmd.Parameters.AddWithValue("whId", payload.WarehouseId);
                        legCmd.Parameters.AddWithValue("binId", line.BinId);
                        legCmd.Parameters.AddWithValue("qty", line.ReceivedQty);
                        legCmd.Parameters.AddWithValue("uomId", line.UnitOfMeasureId);
                        await legCmd.ExecuteNonQueryAsync(cancellationToken);

                        await using var balCmd = new NpgsqlCommand(
                            "INSERT INTO erp_inventory.stock_balance (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id) VALUES (@tenantId, @companyId, @variantId, @whId, @binId, 'available', @qty, @balId) ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, (COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid))) DO UPDATE SET on_hand_qty = erp_inventory.stock_balance.on_hand_qty + EXCLUDED.on_hand_qty, updated_at = now()",
                            connection,
                            eventTx);
                        balCmd.Parameters.AddWithValue("tenantId", tenantId);
                        balCmd.Parameters.AddWithValue("companyId", companyId);
                        balCmd.Parameters.AddWithValue("variantId", line.VariantId);
                        balCmd.Parameters.AddWithValue("whId", payload.WarehouseId);
                        balCmd.Parameters.AddWithValue("binId", line.BinId);
                        balCmd.Parameters.AddWithValue("qty", line.ReceivedQty);
                        balCmd.Parameters.AddWithValue("balId", Guid.NewGuid());
                        await balCmd.ExecuteNonQueryAsync(cancellationToken);

                        if (line.ReceivedQty > 0m && line.UnitPrice > 0m)
                        {
                            await using var layerCmd = new NpgsqlCommand(
                                """
                                INSERT INTO erp_inventory.inventory_cost_layer
                                    (tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id, status_code,
                                     source_document_type, source_document_id, source_line_number, source_movement_id,
                                     received_at, original_qty, remaining_qty, unit_cost, currency_code)
                                VALUES
                                    (@tenantId, @companyId, @layerId, @variantId, @whId, @binId, 'available',
                                     'grn', @grnId, @lineNum, @movementId,
                                     @receivedAt, @qty, @qty, @unitCost, 'PKR')
                                ON CONFLICT (tenant_id, company_id, source_document_type, source_document_id, source_line_number, bin_id)
                                WHERE parent_layer_id IS NULL AND source_document_id IS NOT NULL AND source_line_number IS NOT NULL
                                DO NOTHING;
                                """,
                                connection,
                                eventTx);
                            layerCmd.Parameters.AddWithValue("tenantId", tenantId);
                            layerCmd.Parameters.AddWithValue("companyId", companyId);
                            layerCmd.Parameters.AddWithValue("layerId", Guid.NewGuid());
                            layerCmd.Parameters.AddWithValue("variantId", line.VariantId);
                            layerCmd.Parameters.AddWithValue("whId", payload.WarehouseId);
                            layerCmd.Parameters.AddWithValue("binId", line.BinId);
                            layerCmd.Parameters.AddWithValue("grnId", grnId);
                            layerCmd.Parameters.AddWithValue("lineNum", i + 1);
                            layerCmd.Parameters.AddWithValue("movementId", movementId);
                            layerCmd.Parameters.AddWithValue("receivedAt", occurredAt);
                            layerCmd.Parameters.AddWithValue("qty", line.ReceivedQty);
                            layerCmd.Parameters.AddWithValue("unitCost", line.UnitPrice);
                            await layerCmd.ExecuteNonQueryAsync(cancellationToken);

                            var extendedCost = Math.Round(line.ReceivedQty * line.UnitPrice, 4, MidpointRounding.AwayFromZero);
                            await using var valCmd = new NpgsqlCommand(
                                """
                                INSERT INTO erp_inventory.valuation_entry
                                    (tenant_id, company_id, valuation_entry_id, movement_id, variant_id, entry_type,
                                     quantity, unit_cost, extended_cost, currency_code, posted_at)
                                VALUES
                                    (@tenantId, @companyId, @valId, @movementId, @variantId, 'receipt_cost',
                                     @qty, @unitCost, @extCost, 'PKR', @postedAt);
                                """,
                                connection,
                                eventTx);
                            valCmd.Parameters.AddWithValue("tenantId", tenantId);
                            valCmd.Parameters.AddWithValue("companyId", companyId);
                            valCmd.Parameters.AddWithValue("valId", Guid.NewGuid());
                            valCmd.Parameters.AddWithValue("movementId", movementId);
                            valCmd.Parameters.AddWithValue("variantId", line.VariantId);
                            valCmd.Parameters.AddWithValue("qty", line.ReceivedQty);
                            valCmd.Parameters.AddWithValue("unitCost", line.UnitPrice);
                            valCmd.Parameters.AddWithValue("extCost", extendedCost);
                            valCmd.Parameters.AddWithValue("postedAt", occurredAt);
                            await valCmd.ExecuteNonQueryAsync(cancellationToken);
                        }
                    }

                    movementsCreated++;
                }

                // D. Process General Ledger Journal Posting (deduplicated)
                var totalValuation = payload.Lines.Sum(l => l.ReceivedQty * l.UnitPrice);
                if (totalValuation > 0)
                {
                    Guid activePeriodId;
                    await using (var periodCmd = new NpgsqlCommand(
                        "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tenantId AND company_id = @companyId AND status = 'open' ORDER BY starts_on ASC LIMIT 1",
                        connection,
                        eventTx))
                    {
                        periodCmd.Parameters.AddWithValue("tenantId", tenantId);
                        periodCmd.Parameters.AddWithValue("companyId", companyId);
                        var periodResult = await periodCmd.ExecuteScalarAsync(cancellationToken);
                        if (periodResult is not Guid pId)
                        {
                            throw new InvalidOperationException("NO_OPEN_FISCAL_PERIOD: Cannot post GL journal without an open period.");
                        }
                        activePeriodId = pId;
                    }

                    Guid inventoryAccountId = Guid.Empty;
                    Guid apAccountId = Guid.Empty;
                    await using (var accCmd = new NpgsqlCommand(
                        "SELECT id, account_code FROM finance.accounts WHERE tenant_id = @tenantId AND company_id = @companyId AND account_code IN ('1200', '2010')",
                        connection,
                        eventTx))
                    {
                        accCmd.Parameters.AddWithValue("tenantId", tenantId);
                        accCmd.Parameters.AddWithValue("companyId", companyId);
                        await using var accReader = await accCmd.ExecuteReaderAsync(cancellationToken);
                        while (await accReader.ReadAsync(cancellationToken))
                        {
                            var accId = accReader.GetGuid(0);
                            var code = accReader.GetString(1);
                            if (code == "1200") inventoryAccountId = accId;
                            else if (code == "2010") apAccountId = accId;
                        }
                        await accReader.CloseAsync();
                    }

                    if (inventoryAccountId == Guid.Empty || apAccountId == Guid.Empty)
                    {
                        throw new InvalidOperationException("REQUIRED_FINANCE_ACCOUNTS_NOT_FOUND: Accounts 1200 or 2010 missing.");
                    }

                    bool journalExists;
                    await using (var chkJourCmd = new NpgsqlCommand(
                        "SELECT EXISTS (SELECT 1 FROM finance.journals WHERE tenant_id = @tenantId AND company_id = @companyId AND source_module = 'purchasing' AND source_type = 'grn' AND source_id = @grnId)",
                        connection,
                        eventTx))
                    {
                        chkJourCmd.Parameters.AddWithValue("tenantId", tenantId);
                        chkJourCmd.Parameters.AddWithValue("companyId", companyId);
                        chkJourCmd.Parameters.AddWithValue("grnId", grnId.ToString());
                        journalExists = (bool)(await chkJourCmd.ExecuteScalarAsync(cancellationToken) ?? false);
                    }

                    if (!journalExists)
                    {
                        var journalId = Guid.NewGuid();
                        var journalNo = $"JRN-GRN-{grnNumber}";
                        var idempotencyKey = $"grn-gl-{grnId}";
                        var desc = $"Automated GL posting for GRN {grnNumber}";

                        const string insertJourSql = """
                            INSERT INTO finance.journals
                                (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id, description,
                                 functional_currency, source_module, source_type, source_id, idempotency_key,
                                 status, created_by, posted_by, posted_at)
                            VALUES
                                (@id, @tenantId, @companyId, @no, @date, @periodId, @desc,
                                 'PKR', 'purchasing', 'grn', @sourceId, @key,
                                 'posted', @userId, @userId, now());
                            """;

                        await using (var jourCmd = new NpgsqlCommand(insertJourSql, connection, eventTx))
                        {
                            jourCmd.Parameters.AddWithValue("id", journalId);
                            jourCmd.Parameters.AddWithValue("tenantId", tenantId);
                            jourCmd.Parameters.AddWithValue("companyId", companyId);
                            jourCmd.Parameters.AddWithValue("no", journalNo);
                            jourCmd.Parameters.AddWithValue("date", receivedDate);
                            jourCmd.Parameters.AddWithValue("periodId", activePeriodId);
                            jourCmd.Parameters.AddWithValue("desc", desc);
                            jourCmd.Parameters.AddWithValue("sourceId", grnId.ToString());
                            jourCmd.Parameters.AddWithValue("key", idempotencyKey);
                            jourCmd.Parameters.AddWithValue("userId", userId);
                            await jourCmd.ExecuteNonQueryAsync(cancellationToken);
                        }

                        const string insertLineSql = """
                            INSERT INTO finance.journal_lines
                                (tenant_id, company_id, journal_id, line_no, account_id, description,
                                 transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
                            VALUES
                                (@tenantId, @companyId, @journalId, @lineNo, @accountId, @desc,
                                 'PKR', @amount, 1.0, @debit, @credit);
                            """;

                        // Line 1: Debit 1200
                        await using (var l1Cmd = new NpgsqlCommand(insertLineSql, connection, eventTx))
                        {
                            l1Cmd.Parameters.AddWithValue("tenantId", tenantId);
                            l1Cmd.Parameters.AddWithValue("companyId", companyId);
                            l1Cmd.Parameters.AddWithValue("journalId", journalId);
                            l1Cmd.Parameters.AddWithValue("lineNo", 1);
                            l1Cmd.Parameters.AddWithValue("accountId", inventoryAccountId);
                            l1Cmd.Parameters.AddWithValue("desc", $"Merchandise Inventory - {grnNumber}");
                            l1Cmd.Parameters.AddWithValue("amount", totalValuation);
                            l1Cmd.Parameters.AddWithValue("debit", totalValuation);
                            l1Cmd.Parameters.AddWithValue("credit", 0m);
                            await l1Cmd.ExecuteNonQueryAsync(cancellationToken);
                        }

                        // Line 2: Credit 2010
                        await using (var l2Cmd = new NpgsqlCommand(insertLineSql, connection, eventTx))
                        {
                            l2Cmd.Parameters.AddWithValue("tenantId", tenantId);
                            l2Cmd.Parameters.AddWithValue("companyId", companyId);
                            l2Cmd.Parameters.AddWithValue("journalId", journalId);
                            l2Cmd.Parameters.AddWithValue("lineNo", 2);
                            l2Cmd.Parameters.AddWithValue("accountId", apAccountId);
                            l2Cmd.Parameters.AddWithValue("desc", $"Accounts Payable (Trade Creditors) - {grnNumber}");
                            l2Cmd.Parameters.AddWithValue("amount", totalValuation);
                            l2Cmd.Parameters.AddWithValue("debit", 0m);
                            l2Cmd.Parameters.AddWithValue("credit", totalValuation);
                            await l2Cmd.ExecuteNonQueryAsync(cancellationToken);
                        }

                        journalsPosted++;
                    }
                }

                // E. Mark event published inside the same eventTx
                await using (var pubCmd = new NpgsqlCommand(
                    """
                    UPDATE erp_purchasing.outbox_event
                    SET status = 'published',
                        published_at = now(),
                        attempt_count = attempt_count + 1,
                        last_error = NULL,
                        next_retry_at = NULL
                    WHERE tenant_id = @tenantId AND company_id = @companyId AND event_id = @eventId
                    """,
                    connection,
                    eventTx))
                {
                    pubCmd.Parameters.AddWithValue("tenantId", tenantId);
                    pubCmd.Parameters.AddWithValue("companyId", companyId);
                    pubCmd.Parameters.AddWithValue("eventId", eventId);
                    await pubCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // F. Commit event transaction
                await eventTx.CommitAsync(cancellationToken);
                processedCount++;
                details.Add($"Event {eventId} (GRN {grnNumber}) committed and published successfully.");
            }
            catch (Exception ex)
            {
                // Roll back this event's business mutations cleanly
                await eventTx.RollbackAsync(cancellationToken);

                // Persist attempt count, error details, and next retry time in an independent transaction
                var isDeadLetter = await RecordFailureAsync(
                    connection, tenantId, companyId, eventId, attemptCount, ex, cancellationToken);

                failedCount++;
                if (isDeadLetter)
                {
                    deadLetterCount++;
                }
                details.Add($"Event {eventId} failed: {SanitizeError(ex)} (DeadLetter: {isDeadLetter})");
            }
        }

        return new OutboxProcessResult(
            processedCount, movementsCreated, journalsPosted, details, failedCount, deadLetterCount);
    }

    public async Task<int> RedriveDeadLetterEventsAsync(
        Guid tenantId,
        Guid companyId,
        Guid? eventId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await SetScopeAsync(connection, tenantId, companyId, cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE erp_purchasing.outbox_event
            SET status = 'pending',
                attempt_count = 0,
                next_retry_at = now(),
                last_redriven_at = now(),
                redrive_count = redrive_count + 1
            WHERE tenant_id = @tenantId AND company_id = @companyId
              AND status = 'dead_letter'
              AND (@eventId IS NULL OR event_id = @eventId)
            """,
            connection,
            tx);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("eventId", (object?)eventId ?? DBNull.Value);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return affected;
    }

    public static TimeSpan CalculateBackoff(int attemptCount)
    {
        // Exponential backoff policy:
        // Delay = Min(300s, base 2.0s * 2^(attemptCount - 1)) + (0-25% random jitter)
        const double baseSeconds = 2.0;
        const double maxCapSeconds = 300.0;
        var exponent = Math.Max(0, attemptCount - 1);
        var delaySeconds = Math.Min(maxCapSeconds, baseSeconds * Math.Pow(2, exponent));
        var jitter = delaySeconds * 0.25 * Random.Shared.NextDouble();
        return TimeSpan.FromSeconds(delaySeconds + jitter);
    }

    private static async Task<bool> RecordFailureAsync(
        NpgsqlConnection conn,
        Guid tenantId,
        Guid companyId,
        Guid eventId,
        int currentAttemptCount,
        Exception exception,
        CancellationToken ct)
    {
        try
        {
            await using var failureTx = await conn.BeginTransactionAsync(ct);
            var newAttemptCount = currentAttemptCount + 1;
            var isUnrecoverable = exception.Message.StartsWith("UNSUPPORTED_EVENT_TYPE", StringComparison.OrdinalIgnoreCase) ||
                                  exception.Message.StartsWith("EMPTY_OR_MALFORMED_PAYLOAD", StringComparison.OrdinalIgnoreCase);
            var isDeadLetter = isUnrecoverable || newAttemptCount >= MaxRetryAttempts;
            var newStatus = isDeadLetter ? "dead_letter" : "pending";
            var nextRetryAt = isDeadLetter ? (DateTimeOffset?)null : DateTimeOffset.UtcNow + CalculateBackoff(newAttemptCount);
            var sanitizedError = SanitizeError(exception);

            await using var cmd = new NpgsqlCommand(
                """
                UPDATE erp_purchasing.outbox_event
                SET status = @status,
                    attempt_count = @attemptCount,
                    last_error = @lastError,
                    next_retry_at = @nextRetry
                WHERE tenant_id = @tenantId AND company_id = @companyId AND event_id = @eventId
                """,
                conn,
                failureTx);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("eventId", eventId);
            cmd.Parameters.AddWithValue("status", newStatus);
            cmd.Parameters.AddWithValue("attemptCount", newAttemptCount);
            cmd.Parameters.AddWithValue("lastError", sanitizedError);
            cmd.Parameters.AddWithValue("nextRetry", (object?)nextRetryAt ?? DBNull.Value);

            var affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected != 1)
            {
                throw new InvalidOperationException($"Expected exactly 1 outbox event row updated, got {affected}.");
            }

            await failureTx.CommitAsync(ct);
            return isDeadLetter;
        }
        catch (Exception recordEx)
        {
            throw new InvalidOperationException(
                $"CRITICAL: Failed to persist failure state for outbox event {eventId}: {recordEx.Message}", recordEx);
        }
    }

    private static string SanitizeError(Exception ex)
    {
        var msg = ex is PostgresException pgEx
            ? $"PostgreSQL {pgEx.SqlState}: {pgEx.MessageText} (Detail: {pgEx.Detail ?? "none"})"
            : ex.Message;
        return msg.Length > 1000 ? msg[..1000] : msg;
    }

    private static async Task SetScopeAsync(
        NpgsqlConnection conn,
        Guid tenantId,
        Guid companyId,
        CancellationToken ct)
    {
        await using var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenantId, false)", conn);
        setTenant.Parameters.AddWithValue("tenantId", tenantId.ToString());
        await setTenant.ExecuteNonQueryAsync(ct);

        await using var setCompany = new NpgsqlCommand("SELECT set_config('app.company_id', @companyId, false)", conn);
        setCompany.Parameters.AddWithValue("companyId", companyId.ToString());
        await setCompany.ExecuteNonQueryAsync(ct);
    }
}
