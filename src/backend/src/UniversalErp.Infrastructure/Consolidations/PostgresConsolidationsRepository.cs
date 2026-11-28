using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UniversalErp.Application.Consolidations;

namespace UniversalErp.Infrastructure.Consolidations;

public class PostgresConsolidationsRepository : IConsolidationsRepository
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly string? _connectionString;
    private readonly ConsolidationEliminationEngine _eliminationEngine;

    public PostgresConsolidationsRepository(NpgsqlDataSource dataSource, ConsolidationEliminationEngine eliminationEngine)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _eliminationEngine = eliminationEngine ?? throw new ArgumentNullException(nameof(eliminationEngine));
    }

    public PostgresConsolidationsRepository(string connectionString, ConsolidationEliminationEngine eliminationEngine)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _eliminationEngine = eliminationEngine ?? throw new ArgumentNullException(nameof(eliminationEngine));
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(Guid tenantId, Guid? companyId, CancellationToken ct)
    {
        var conn = _dataSource != null
            ? await _dataSource.OpenConnectionAsync(ct)
            : new NpgsqlConnection(_connectionString);

        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        const string setScopeSql = "SELECT set_config('app.tenant_id', @tenantId, false), set_config('app.company_id', @companyId, false);";
        await using var cmd = new NpgsqlCommand(setScopeSql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId.ToString());
        cmd.Parameters.AddWithValue("companyId", companyId.HasValue && companyId.Value != Guid.Empty ? companyId.Value.ToString() : "");
        await cmd.ExecuteNonQueryAsync(ct);

        return conn;
    }

    // ── 1. Intercompany Relationships ──────────────────────────────────────────

    public async Task<IReadOnlyList<IntercompanyRelationshipDto>> GetRelationshipsAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        const string sql = @"
            SELECT id, tenant_id, from_company_id, to_company_id, due_to_account_id,
                   due_from_account_id, clearing_currency_code, transfer_pricing_rule,
                   markup_percentage, is_active, created_at
            FROM consolidation.intercompany_relationships
            WHERE tenant_id = @tenantId
            ORDER BY created_at DESC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        var list = new List<IntercompanyRelationshipDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new IntercompanyRelationshipDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetGuid(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetDecimal(8),
                reader.GetBoolean(9),
                reader.GetDateTime(10)
            ));
        }

        return list;
    }

    public async Task<IntercompanyRelationshipDto?> GetRelationshipAsync(Guid tenantId, Guid fromCompanyId, Guid toCompanyId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        const string sql = @"
            SELECT id, tenant_id, from_company_id, to_company_id, due_to_account_id,
                   due_from_account_id, clearing_currency_code, transfer_pricing_rule,
                   markup_percentage, is_active, created_at
            FROM consolidation.intercompany_relationships
            WHERE tenant_id = @tenantId AND from_company_id = @fromComp AND to_company_id = @toComp;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("fromComp", fromCompanyId);
        cmd.Parameters.AddWithValue("toComp", toCompanyId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new IntercompanyRelationshipDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetGuid(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetDecimal(8),
                reader.GetBoolean(9),
                reader.GetDateTime(10)
            );
        }

        return null;
    }

    public async Task<IntercompanyRelationshipDto> CreateRelationshipAsync(Guid tenantId, CreateIntercompanyRelationshipRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        const string sql = @"
            INSERT INTO consolidation.intercompany_relationships (
                tenant_id, from_company_id, to_company_id, due_to_account_id,
                due_from_account_id, clearing_currency_code, transfer_pricing_rule,
                markup_percentage, is_active
            )
            VALUES (
                @tenantId, @fromComp, @toComp, @dueTo,
                @dueFrom, @curr, @rule, @markup, true
            )
            RETURNING id, tenant_id, from_company_id, to_company_id, due_to_account_id,
                      due_from_account_id, clearing_currency_code, transfer_pricing_rule,
                      markup_percentage, is_active, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("fromComp", request.FromCompanyId);
        cmd.Parameters.AddWithValue("toComp", request.ToCompanyId);
        cmd.Parameters.AddWithValue("dueTo", request.DueToAccountId);
        cmd.Parameters.AddWithValue("dueFrom", request.DueFromAccountId);
        cmd.Parameters.AddWithValue("curr", request.ClearingCurrencyCode);
        cmd.Parameters.AddWithValue("rule", request.TransferPricingRule);
        cmd.Parameters.AddWithValue("markup", request.MarkupPercentage);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new IntercompanyRelationshipDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetGuid(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetDecimal(8),
                reader.GetBoolean(9),
                reader.GetDateTime(10)
            );
        }

        throw new InvalidOperationException("Failed to insert intercompany relationship.");
    }

    // ── 2. Intercompany Transfers ───────────────────────────────────────────────

    public async Task<IReadOnlyList<IntercompanyTransferDto>> GetTransfersAsync(Guid tenantId, Guid? companyId = null, string? status = null, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);

        var sql = @"
            SELECT id, tenant_id, transfer_number, from_company_id, from_branch_id,
                   to_company_id, to_branch_id, status, dispatch_date, receipt_date,
                   currency_code, total_amount_pkr, from_gl_journal_id, to_gl_journal_id,
                   notes, created_at
            FROM consolidation.intercompany_transfers
            WHERE tenant_id = @tenantId";

        if (companyId.HasValue && companyId.Value != Guid.Empty)
        {
            sql += " AND (from_company_id = @companyId OR to_company_id = @companyId)";
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @status";
        }
        sql += " ORDER BY created_at DESC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        if (companyId.HasValue && companyId.Value != Guid.Empty)
            cmd.Parameters.AddWithValue("companyId", companyId.Value);
        if (!string.IsNullOrWhiteSpace(status))
            cmd.Parameters.AddWithValue("status", status);

        var transfers = new List<IntercompanyTransferDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            transfers.Add(MapTransfer(reader, new List<IntercompanyTransferLineDto>()));
        }

        return transfers;
    }

    public async Task<IntercompanyTransferDto?> GetTransferByIdAsync(Guid tenantId, Guid transferId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        const string tSql = @"
            SELECT id, tenant_id, transfer_number, from_company_id, from_branch_id,
                   to_company_id, to_branch_id, status, dispatch_date, receipt_date,
                   currency_code, total_amount_pkr, from_gl_journal_id, to_gl_journal_id,
                   notes, created_at
            FROM consolidation.intercompany_transfers
            WHERE tenant_id = @tenantId AND id = @transferId;";

        await using var tCmd = new NpgsqlCommand(tSql, conn);
        tCmd.Parameters.AddWithValue("tenantId", tenantId);
        tCmd.Parameters.AddWithValue("transferId", transferId);

        IntercompanyTransferDto? transfer = null;
        await using (var reader = await tCmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                transfer = MapTransfer(reader, new List<IntercompanyTransferLineDto>());
            }
        }

        if (transfer == null)
            return null;

        // Fetch lines
        const string lSql = @"
            SELECT id, transfer_id, product_id, quantity_dispatched, quantity_received,
                   unit_cost_pkr, transfer_price_pkr, line_total_pkr, source_layer_id,
                   source_warehouse_id, source_bin_id, dest_warehouse_id, dest_bin_id
            FROM consolidation.intercompany_transfer_lines
            WHERE tenant_id = @tenantId AND transfer_id = @transferId;";

        await using var lCmd = new NpgsqlCommand(lSql, conn);
        lCmd.Parameters.AddWithValue("tenantId", tenantId);
        lCmd.Parameters.AddWithValue("transferId", transferId);

        var lines = new List<IntercompanyTransferLineDto>();
        await using (var lReader = await lCmd.ExecuteReaderAsync(ct))
        {
            while (await lReader.ReadAsync(ct))
            {
                lines.Add(new IntercompanyTransferLineDto(
                    lReader.GetGuid(0),
                    lReader.GetGuid(1),
                    lReader.GetGuid(2),
                    lReader.GetDecimal(3),
                    lReader.GetDecimal(4),
                    lReader.GetDecimal(5),
                    lReader.GetDecimal(6),
                    lReader.GetDecimal(7),
                    lReader.IsDBNull(8) ? null : lReader.GetGuid(8),
                    lReader.IsDBNull(9) ? null : lReader.GetGuid(9),
                    lReader.IsDBNull(10) ? null : lReader.GetGuid(10),
                    lReader.IsDBNull(11) ? null : lReader.GetGuid(11),
                    lReader.IsDBNull(12) ? null : lReader.GetGuid(12)
                ));
            }
        }

        return transfer with { Lines = lines };
    }

    public async Task<IntercompanyTransferDto> CreateTransferAsync(Guid tenantId, CreateIntercompanyTransferRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // Calculate total amount
            decimal totalAmount = 0m;
            foreach (var l in request.Lines)
            {
                var price = l.TransferPricePkr ?? l.UnitCostPkr;
                totalAmount += l.Quantity * price;
            }

            const string insSql = @"
                INSERT INTO consolidation.intercompany_transfers (
                    tenant_id, transfer_number, from_company_id, from_branch_id,
                    to_company_id, to_branch_id, status, dispatch_date,
                    currency_code, total_amount_pkr, notes
                )
                VALUES (
                    @tenantId, @num, @fromComp, @fromBranch,
                    @toComp, @toBranch, 'draft', @dispDate,
                    @curr, @total, @notes
                )
                RETURNING id, tenant_id, transfer_number, from_company_id, from_branch_id,
                          to_company_id, to_branch_id, status, dispatch_date, receipt_date,
                          currency_code, total_amount_pkr, from_gl_journal_id, to_gl_journal_id,
                          notes, created_at;";

            await using var cmd = new NpgsqlCommand(insSql, conn, tx);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("num", request.TransferNumber);
            cmd.Parameters.AddWithValue("fromComp", request.FromCompanyId);
            cmd.Parameters.AddWithValue("fromBranch", (object?)request.FromBranchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("toComp", request.ToCompanyId);
            cmd.Parameters.AddWithValue("toBranch", (object?)request.ToBranchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("dispDate", request.DispatchDate);
            cmd.Parameters.AddWithValue("curr", request.CurrencyCode);
            cmd.Parameters.AddWithValue("total", totalAmount);
            cmd.Parameters.AddWithValue("notes", (object?)request.Notes ?? DBNull.Value);

            IntercompanyTransferDto created;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new InvalidOperationException("Failed to insert transfer header.");

                created = MapTransfer(reader, new List<IntercompanyTransferLineDto>());
            }

            var lineDtos = new List<IntercompanyTransferLineDto>();
            foreach (var l in request.Lines)
            {
                var price = l.TransferPricePkr ?? l.UnitCostPkr;
                var lineTotal = l.Quantity * price;

                const string lineSql = @"
                    INSERT INTO consolidation.intercompany_transfer_lines (
                        tenant_id, transfer_id, product_id, quantity_dispatched,
                        quantity_received, unit_cost_pkr, transfer_price_pkr,
                        line_total_pkr, source_layer_id, source_warehouse_id,
                        source_bin_id, dest_warehouse_id, dest_bin_id
                    )
                    VALUES (
                        @tenantId, @transferId, @prodId, @qty,
                        0, @cost, @price,
                        @total, @layerId, @srcWh,
                        @srcBin, @dstWh, @dstBin
                    )
                    RETURNING id, transfer_id, product_id, quantity_dispatched, quantity_received,
                              unit_cost_pkr, transfer_price_pkr, line_total_pkr, source_layer_id,
                              source_warehouse_id, source_bin_id, dest_warehouse_id, dest_bin_id;";

                await using var lCmd = new NpgsqlCommand(lineSql, conn, tx);
                lCmd.Parameters.AddWithValue("tenantId", tenantId);
                lCmd.Parameters.AddWithValue("transferId", created.Id);
                lCmd.Parameters.AddWithValue("prodId", l.ProductId);
                lCmd.Parameters.AddWithValue("qty", l.Quantity);
                lCmd.Parameters.AddWithValue("cost", l.UnitCostPkr);
                lCmd.Parameters.AddWithValue("price", price);
                lCmd.Parameters.AddWithValue("total", lineTotal);
                lCmd.Parameters.AddWithValue("layerId", (object?)l.SourceLayerId ?? DBNull.Value);
                lCmd.Parameters.AddWithValue("srcWh", (object?)l.SourceWarehouseId ?? DBNull.Value);
                lCmd.Parameters.AddWithValue("srcBin", (object?)l.SourceBinId ?? DBNull.Value);
                lCmd.Parameters.AddWithValue("dstWh", (object?)l.DestWarehouseId ?? DBNull.Value);
                lCmd.Parameters.AddWithValue("dstBin", (object?)l.DestBinId ?? DBNull.Value);

                await using var lReader = await lCmd.ExecuteReaderAsync(ct);
                if (await lReader.ReadAsync(ct))
                {
                    lineDtos.Add(new IntercompanyTransferLineDto(
                        lReader.GetGuid(0),
                        lReader.GetGuid(1),
                        lReader.GetGuid(2),
                        lReader.GetDecimal(3),
                        lReader.GetDecimal(4),
                        lReader.GetDecimal(5),
                        lReader.GetDecimal(6),
                        lReader.GetDecimal(7),
                        lReader.IsDBNull(8) ? null : lReader.GetGuid(8),
                        lReader.IsDBNull(9) ? null : lReader.GetGuid(9),
                        lReader.IsDBNull(10) ? null : lReader.GetGuid(10),
                        lReader.IsDBNull(11) ? null : lReader.GetGuid(11),
                        lReader.IsDBNull(12) ? null : lReader.GetGuid(12)
                    ));
                }
            }

            await tx.CommitAsync(ct);
            return created with { Lines = lineDtos };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IntercompanyTransferDto> DispatchTransferAsync(Guid tenantId, Guid transferId, Guid userId, CancellationToken ct = default)
    {
        var transfer = await GetTransferByIdAsync(tenantId, transferId, ct);
        if (transfer == null)
            throw new InvalidOperationException($"Transfer {transferId} not found.");
        if (transfer.Status != "draft")
            throw new InvalidOperationException($"Transfer cannot be dispatched because it is in status '{transfer.Status}'.");

        var rel = await GetRelationshipAsync(tenantId, transfer.FromCompanyId, transfer.ToCompanyId, ct);
        if (rel == null)
            throw new InvalidOperationException($"No active intercompany relationship found between company {transfer.FromCompanyId} and {transfer.ToCompanyId}.");

        await using var conn = await OpenConnectionAsync(tenantId, null, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // 1. Resolve open fiscal period for sender company
            var periodId = await ResolveOpenFiscalPeriodAsync(conn, tx, tenantId, transfer.FromCompanyId, ct);

            // 2. Resolve GL accounts for sender (Due-From account and Inventory Asset 1200)
            var dueFromAccountId = rel.DueFromAccountId;
            var inventoryAccountId = await ResolveAccountIdAsync(conn, tx, tenantId, transfer.FromCompanyId, "1200", ct);

            // 3. Create paired GL Journal in sender company
            var journalNo = $"IC-DISP-{transfer.TransferNumber}";
            var journalId = Guid.NewGuid();
            var totalAmount = transfer.TotalAmountPkr;

            decimal totalCost = 0m;
            foreach (var l in transfer.Lines)
            {
                totalCost += l.QuantityDispatched * l.UnitCostPkr;
            }

            const string insJrnSql = @"
                INSERT INTO finance.journals (
                    tenant_id, company_id, id, journal_no, journal_date, fiscal_period_id,
                    description, functional_currency, source_module, source_type, source_id,
                    idempotency_key, status, created_by, posted_by, posted_at
                )
                VALUES (
                    @tenantId, @companyId, @id, @no, @date, @periodId,
                    @desc, @curr, 'consolidation', 'intercompany_dispatch', @sourceId,
                    @idemKey, 'posted', @userId, @userId, now()
                );";

            await using (var jCmd = new NpgsqlCommand(insJrnSql, conn, tx))
            {
                jCmd.Parameters.AddWithValue("tenantId", tenantId);
                jCmd.Parameters.AddWithValue("companyId", transfer.FromCompanyId);
                jCmd.Parameters.AddWithValue("id", journalId);
                jCmd.Parameters.AddWithValue("no", journalNo);
                jCmd.Parameters.AddWithValue("date", transfer.DispatchDate);
                jCmd.Parameters.AddWithValue("periodId", periodId);
                jCmd.Parameters.AddWithValue("desc", $"Intercompany dispatch {transfer.TransferNumber} to company {transfer.ToCompanyId}");
                jCmd.Parameters.AddWithValue("curr", transfer.CurrencyCode);
                jCmd.Parameters.AddWithValue("sourceId", transfer.TransferNumber);
                jCmd.Parameters.AddWithValue("idemKey", $"ic-disp-{transfer.Id}");
                jCmd.Parameters.AddWithValue("userId", userId);
                await jCmd.ExecuteNonQueryAsync(ct);
            }

            // Journal Lines:
            // When transfer price differs from FIFO cost (markup/margin present), record:
            // Line 1: Dr Due-From Affiliates (Receivable) at Transfer Price
            // Line 2: Cr Intercompany Sales Revenue at Transfer Price
            // Line 3: Dr Intercompany Cost of Goods Sold at Original FIFO Cost
            // Line 4: Cr Inventory Asset at Original FIFO Cost
            // When transfer is at cost, record:
            // Line 1: Dr Due-From Affiliates at Cost
            // Line 2: Cr Inventory Asset at Cost
            if (totalAmount > totalCost)
            {
                var salesRevenueAccountId = await ResolveAccountIdAsync(conn, tx, tenantId, transfer.FromCompanyId, "4000", ct);
                var cogsAccountId = await ResolveAccountIdAsync(conn, tx, tenantId, transfer.FromCompanyId, "5000", ct);

                const string insJrnLineSql = @"
                    INSERT INTO finance.journal_lines (
                        tenant_id, company_id, journal_id, line_no, account_id,
                        description, transaction_currency, transaction_amount, exchange_rate,
                        debit_functional, credit_functional
                    )
                    VALUES
                    (@tenantId, @companyId, @journalId, 1, @dueFromAcc, @desc1, @curr, @amt, 1.0, @amt, 0),
                    (@tenantId, @companyId, @journalId, 2, @salesAcc, @desc2, @curr, @amt, 1.0, 0, @amt),
                    (@tenantId, @companyId, @journalId, 3, @cogsAcc, @desc3, @curr, @cost, 1.0, @cost, 0),
                    (@tenantId, @companyId, @journalId, 4, @invAcc, @desc4, @curr, @cost, 1.0, 0, @cost);";

                await using var jlCmd = new NpgsqlCommand(insJrnLineSql, conn, tx);
                jlCmd.Parameters.AddWithValue("tenantId", tenantId);
                jlCmd.Parameters.AddWithValue("companyId", transfer.FromCompanyId);
                jlCmd.Parameters.AddWithValue("journalId", journalId);
                jlCmd.Parameters.AddWithValue("dueFromAcc", dueFromAccountId);
                jlCmd.Parameters.AddWithValue("salesAcc", salesRevenueAccountId);
                jlCmd.Parameters.AddWithValue("cogsAcc", cogsAccountId);
                jlCmd.Parameters.AddWithValue("invAcc", inventoryAccountId);
                jlCmd.Parameters.AddWithValue("desc1", $"Due-from company {transfer.ToCompanyId} under {transfer.TransferNumber}");
                jlCmd.Parameters.AddWithValue("desc2", $"Intercompany sales revenue for {transfer.TransferNumber}");
                jlCmd.Parameters.AddWithValue("desc3", $"Cost of goods sold for {transfer.TransferNumber}");
                jlCmd.Parameters.AddWithValue("desc4", $"FIFO inventory relief at original cost for {transfer.TransferNumber}");
                jlCmd.Parameters.AddWithValue("curr", transfer.CurrencyCode);
                jlCmd.Parameters.AddWithValue("amt", totalAmount);
                jlCmd.Parameters.AddWithValue("cost", totalCost);
                await jlCmd.ExecuteNonQueryAsync(ct);
            }
            else
            {
                const string insJrnLineSql = @"
                    INSERT INTO finance.journal_lines (
                        tenant_id, company_id, journal_id, line_no, account_id,
                        description, transaction_currency, transaction_amount, exchange_rate,
                        debit_functional, credit_functional
                    )
                    VALUES
                    (@tenantId, @companyId, @journalId, 1, @dueFromAcc, @desc1, @curr, @amt, 1.0, @amt, 0),
                    (@tenantId, @companyId, @journalId, 2, @invAcc, @desc2, @curr, @amt, 1.0, 0, @amt);";

                await using var jlCmd = new NpgsqlCommand(insJrnLineSql, conn, tx);
                jlCmd.Parameters.AddWithValue("tenantId", tenantId);
                jlCmd.Parameters.AddWithValue("companyId", transfer.FromCompanyId);
                jlCmd.Parameters.AddWithValue("journalId", journalId);
                jlCmd.Parameters.AddWithValue("dueFromAcc", dueFromAccountId);
                jlCmd.Parameters.AddWithValue("invAcc", inventoryAccountId);
                jlCmd.Parameters.AddWithValue("desc1", $"Due-from company {transfer.ToCompanyId}");
                jlCmd.Parameters.AddWithValue("desc2", $"Inventory dispatched under {transfer.TransferNumber}");
                jlCmd.Parameters.AddWithValue("curr", transfer.CurrencyCode);
                jlCmd.Parameters.AddWithValue("amt", totalAmount);
                await jlCmd.ExecuteNonQueryAsync(ct);
            }

            // 4. Deduct FIFO stock layer from sender warehouse
            foreach (var line in transfer.Lines)
            {
                if (line.SourceLayerId.HasValue && line.SourceLayerId.Value != Guid.Empty)
                {
                    const string updLayerSql = @"
                        UPDATE erp_inventory.inventory_cost_layer
                        SET remaining_qty = remaining_qty - @qty,
                            updated_at = now()
                        WHERE tenant_id = @tenantId AND company_id = @companyId AND layer_id = @layerId
                          AND remaining_qty >= @qty;";

                    await using var layerCmd = new NpgsqlCommand(updLayerSql, conn, tx);
                    layerCmd.Parameters.AddWithValue("tenantId", tenantId);
                    layerCmd.Parameters.AddWithValue("companyId", transfer.FromCompanyId);
                    layerCmd.Parameters.AddWithValue("layerId", line.SourceLayerId.Value);
                    layerCmd.Parameters.AddWithValue("qty", line.QuantityDispatched);

                    var rowsAffected = await layerCmd.ExecuteNonQueryAsync(ct);
                    if (rowsAffected == 0)
                    {
                        // Layer doesn't have enough qty or doesn't exist
                        throw new InvalidOperationException($"Insufficient inventory layer remaining qty for layer {line.SourceLayerId.Value}.");
                    }
                }
            }

            // 5. Update transfer status to 'dispatched'
            const string updTransferSql = @"
                UPDATE consolidation.intercompany_transfers
                SET status = 'dispatched',
                    from_gl_journal_id = @journalId,
                    updated_at = now()
                WHERE tenant_id = @tenantId AND id = @transferId;";

            await using (var uCmd = new NpgsqlCommand(updTransferSql, conn, tx))
            {
                uCmd.Parameters.AddWithValue("tenantId", tenantId);
                uCmd.Parameters.AddWithValue("journalId", journalId);
                uCmd.Parameters.AddWithValue("transferId", transfer.Id);
                await uCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return (await GetTransferByIdAsync(tenantId, transferId, ct))!;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IntercompanyTransferDto> ReceiveTransferAsync(Guid tenantId, Guid transferId, Guid userId, ReceiveIntercompanyTransferRequest request, CancellationToken ct = default)
    {
        var transfer = await GetTransferByIdAsync(tenantId, transferId, ct);
        if (transfer == null)
            throw new InvalidOperationException($"Transfer {transferId} not found.");
        if (transfer.Status != "dispatched" && transfer.Status != "in_transit")
            throw new InvalidOperationException($"Transfer cannot be received because it is in status '{transfer.Status}'.");

        var rel = await GetRelationshipAsync(tenantId, transfer.FromCompanyId, transfer.ToCompanyId, ct);
        if (rel == null)
            throw new InvalidOperationException($"No active intercompany relationship found between {transfer.FromCompanyId} and {transfer.ToCompanyId}.");

        await using var conn = await OpenConnectionAsync(tenantId, null, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // 1. Resolve open fiscal period for receiver company
            var periodId = await ResolveOpenFiscalPeriodAsync(conn, tx, tenantId, transfer.ToCompanyId, ct);

            // 2. Resolve Due-To account and Inventory Asset account 1200 for receiver company
            var dueToAccountId = rel.DueToAccountId;
            var inventoryAccountId = await ResolveAccountIdAsync(conn, tx, tenantId, transfer.ToCompanyId, "1200", ct);

            // 3. Process lines and create target Model A FIFO inventory cost layers
            decimal receivedTotalAmount = 0m;
            foreach (var line in transfer.Lines)
            {
                var receiptItem = request.Lines?.FirstOrDefault(r => r.LineId == line.Id);
                var qtyReceived = receiptItem?.QuantityReceived ?? line.QuantityDispatched;
                var dstWarehouseId = receiptItem?.DestWarehouseId ?? line.DestWarehouseId;
                var dstBinId = receiptItem?.DestBinId ?? line.DestBinId;

                if (!dstWarehouseId.HasValue || dstWarehouseId.Value == Guid.Empty)
                    throw new ArgumentException($"Destination warehouse is required for transfer line {line.Id}.");
                if (!dstBinId.HasValue || dstBinId.Value == Guid.Empty)
                    throw new ArgumentException($"Destination bin is required for transfer line {line.Id}.");

                var lineAmount = qtyReceived * line.TransferPricePkr;
                receivedTotalAmount += lineAmount;

                // Update line received qty & destination
                const string updLineSql = @"
                    UPDATE consolidation.intercompany_transfer_lines
                    SET quantity_received = @qtyReceived,
                        dest_warehouse_id = @dstWh,
                        dest_bin_id = @dstBin
                    WHERE tenant_id = @tenantId AND id = @lineId;";

                await using (var lCmd = new NpgsqlCommand(updLineSql, conn, tx))
                {
                    lCmd.Parameters.AddWithValue("tenantId", tenantId);
                    lCmd.Parameters.AddWithValue("qtyReceived", qtyReceived);
                    lCmd.Parameters.AddWithValue("dstWh", dstWarehouseId.Value);
                    lCmd.Parameters.AddWithValue("dstBin", dstBinId.Value);
                    lCmd.Parameters.AddWithValue("lineId", line.Id);
                    await lCmd.ExecuteNonQueryAsync(ct);
                }

                // Resolve product variant for FIFO layer
                var variantId = await ResolveDefaultVariantIdAsync(conn, tx, tenantId, line.ProductId, ct);

                // Insert into erp_inventory.inventory_cost_layer
                const string insLayerSql = @"
                    INSERT INTO erp_inventory.inventory_cost_layer (
                        tenant_id, company_id, variant_id, warehouse_id, bin_id,
                        status_code, source_document_type, source_document_id,
                        source_line_number, original_qty, remaining_qty, unit_cost,
                        currency_code
                    )
                    VALUES (
                        @tenantId, @companyId, @varId, @whId, @binId,
                        'available', 'intercompany_transfer', @transferId,
                        1, @qty, @qty, @unitCost,
                        @curr
                    );";

                await using (var layerCmd = new NpgsqlCommand(insLayerSql, conn, tx))
                {
                    layerCmd.Parameters.AddWithValue("tenantId", tenantId);
                    layerCmd.Parameters.AddWithValue("companyId", transfer.ToCompanyId);
                    layerCmd.Parameters.AddWithValue("varId", variantId);
                    layerCmd.Parameters.AddWithValue("whId", dstWarehouseId.Value);
                    layerCmd.Parameters.AddWithValue("binId", dstBinId.Value);
                    layerCmd.Parameters.AddWithValue("transferId", transfer.Id);
                    layerCmd.Parameters.AddWithValue("qty", qtyReceived);
                    layerCmd.Parameters.AddWithValue("unitCost", line.TransferPricePkr);
                    layerCmd.Parameters.AddWithValue("curr", transfer.CurrencyCode);
                    await layerCmd.ExecuteNonQueryAsync(ct);
                }
            }

            // 4. Create paired GL journal in receiver company (Dr Inventory Asset 1200 / Cr Due-To Affiliates 2200)
            var journalNo = $"IC-RECV-{transfer.TransferNumber}";
            var journalId = Guid.NewGuid();

            const string insJrnSql = @"
                INSERT INTO finance.journals (
                    tenant_id, company_id, id, journal_no, journal_date, fiscal_period_id,
                    description, functional_currency, source_module, source_type, source_id,
                    idempotency_key, status, created_by, posted_by, posted_at
                )
                VALUES (
                    @tenantId, @companyId, @id, @no, @date, @periodId,
                    @desc, @curr, 'consolidation', 'intercompany_receipt', @sourceId,
                    @idemKey, 'posted', @userId, @userId, now()
                );";

            await using (var jCmd = new NpgsqlCommand(insJrnSql, conn, tx))
            {
                jCmd.Parameters.AddWithValue("tenantId", tenantId);
                jCmd.Parameters.AddWithValue("companyId", transfer.ToCompanyId);
                jCmd.Parameters.AddWithValue("id", journalId);
                jCmd.Parameters.AddWithValue("no", journalNo);
                jCmd.Parameters.AddWithValue("date", request.ReceiptDate);
                jCmd.Parameters.AddWithValue("periodId", periodId);
                jCmd.Parameters.AddWithValue("desc", $"Intercompany receipt {transfer.TransferNumber} from company {transfer.FromCompanyId}");
                jCmd.Parameters.AddWithValue("curr", transfer.CurrencyCode);
                jCmd.Parameters.AddWithValue("sourceId", transfer.TransferNumber);
                jCmd.Parameters.AddWithValue("idemKey", $"ic-recv-{transfer.Id}");
                jCmd.Parameters.AddWithValue("userId", userId);
                await jCmd.ExecuteNonQueryAsync(ct);
            }

            // Journal Lines:
            // Line 1: Dr Inventory Asset 1200
            // Line 2: Cr Due-To Affiliates (Payable)
            const string insJrnLineSql = @"
                INSERT INTO finance.journal_lines (
                    tenant_id, company_id, journal_id, line_no, account_id,
                    description, transaction_currency, transaction_amount, exchange_rate,
                    debit_functional, credit_functional
                )
                VALUES
                (@tenantId, @companyId, @journalId, 1, @invAcc, @desc1, @curr, @amt, 1.0, @amt, 0),
                (@tenantId, @companyId, @journalId, 2, @dueToAcc, @desc2, @curr, @amt, 1.0, 0, @amt);";

            await using (var jlCmd = new NpgsqlCommand(insJrnLineSql, conn, tx))
            {
                jlCmd.Parameters.AddWithValue("tenantId", tenantId);
                jlCmd.Parameters.AddWithValue("companyId", transfer.ToCompanyId);
                jlCmd.Parameters.AddWithValue("journalId", journalId);
                jlCmd.Parameters.AddWithValue("invAcc", inventoryAccountId);
                jlCmd.Parameters.AddWithValue("dueToAcc", dueToAccountId);
                jlCmd.Parameters.AddWithValue("desc1", $"Inventory received under {transfer.TransferNumber}");
                jlCmd.Parameters.AddWithValue("desc2", $"Due-to company {transfer.FromCompanyId}");
                jlCmd.Parameters.AddWithValue("curr", transfer.CurrencyCode);
                jlCmd.Parameters.AddWithValue("amt", receivedTotalAmount);
                await jlCmd.ExecuteNonQueryAsync(ct);
            }

            // 5. Update transfer status to 'received'
            const string updTransferSql = @"
                UPDATE consolidation.intercompany_transfers
                SET status = 'received',
                    receipt_date = @rDate,
                    to_gl_journal_id = @journalId,
                    updated_at = now()
                WHERE tenant_id = @tenantId AND id = @transferId;";

            await using (var uCmd = new NpgsqlCommand(updTransferSql, conn, tx))
            {
                uCmd.Parameters.AddWithValue("tenantId", tenantId);
                uCmd.Parameters.AddWithValue("rDate", request.ReceiptDate);
                uCmd.Parameters.AddWithValue("journalId", journalId);
                uCmd.Parameters.AddWithValue("transferId", transfer.Id);
                await uCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return (await GetTransferByIdAsync(tenantId, transferId, ct))!;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── 3. Financial Consolidation Runs ─────────────────────────────────────────

    public async Task<ConsolidationRunDto> ExecuteConsolidationAsync(Guid tenantId, Guid userId, ExecuteConsolidationRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            var runId = Guid.NewGuid();

            // 1. Gather intercompany Due-To / Due-From balances between participating companies
            var balancePairs = new List<ConsolidationEliminationEngine.IntercompanyBalancePair>();
            var tradingVolumes = new List<ConsolidationEliminationEngine.IntercompanyTradingVolume>();
            var heldStocks = new List<ConsolidationEliminationEngine.UnrealizedInventoryStock>();

            for (int i = 0; i < request.ParticipatingCompanyIds.Count; i++)
            {
                for (int j = 0; j < request.ParticipatingCompanyIds.Count; j++)
                {
                    if (i == j) continue;
                    var c1 = request.ParticipatingCompanyIds[i];
                    var c2 = request.ParticipatingCompanyIds[j];

                    // Check relationship
                    var rel = await GetRelationshipAsync(tenantId, c1, c2, ct);
                    if (rel != null)
                    {
                        var dueFromBal = await QueryAccountBalanceAsync(conn, tx, tenantId, c1, rel.DueFromAccountId, ct);
                        var dueToBal = await QueryAccountBalanceAsync(conn, tx, tenantId, c2, rel.DueToAccountId, ct);

                        if (dueFromBal > 0 || dueToBal > 0)
                        {
                            balancePairs.Add(new ConsolidationEliminationEngine.IntercompanyBalancePair(
                                c1, c2, dueFromBal, dueToBal
                            ));
                        }
                    }

                    // Query trading volume between c1 (seller) and c2 (buyer)
                    const string volSql = @"
                        SELECT COALESCE(SUM(total_amount_pkr), 0)
                        FROM consolidation.intercompany_transfers
                        WHERE tenant_id = @tenantId AND from_company_id = @c1 AND to_company_id = @c2
                          AND status IN ('dispatched', 'in_transit', 'received', 'reconciled');";

                    await using var vCmd = new NpgsqlCommand(volSql, conn, tx);
                    vCmd.Parameters.AddWithValue("tenantId", tenantId);
                    vCmd.Parameters.AddWithValue("c1", c1);
                    vCmd.Parameters.AddWithValue("c2", c2);
                    var vol = Convert.ToDecimal(await vCmd.ExecuteScalarAsync(ct) ?? 0m);

                    if (vol > 0)
                    {
                        tradingVolumes.Add(new ConsolidationEliminationEngine.IntercompanyTradingVolume(
                            c1, c2, vol
                        ));
                    }

                    // Query held inventory at c2 from c1
                    const string stockSql = @"
                        SELECT l.remaining_qty, tl.unit_cost_pkr, tl.transfer_price_pkr
                        FROM erp_inventory.inventory_cost_layer l
                        JOIN consolidation.intercompany_transfers t ON t.id = l.source_document_id
                        JOIN consolidation.intercompany_transfer_lines tl ON tl.transfer_id = t.id
                        WHERE l.tenant_id = @tenantId AND l.company_id = @c2
                          AND t.from_company_id = @c1 AND l.remaining_qty > 0;";

                    await using var sCmd = new NpgsqlCommand(stockSql, conn, tx);
                    sCmd.Parameters.AddWithValue("tenantId", tenantId);
                    sCmd.Parameters.AddWithValue("c1", c1);
                    sCmd.Parameters.AddWithValue("c2", c2);

                    await using var sReader = await sCmd.ExecuteReaderAsync(ct);
                    while (await sReader.ReadAsync(ct))
                    {
                        var remQty = sReader.GetDecimal(0);
                        var cost = sReader.GetDecimal(1);
                        var price = sReader.GetDecimal(2);
                        if (remQty > 0 && price > cost)
                        {
                            heldStocks.Add(new ConsolidationEliminationEngine.UnrealizedInventoryStock(
                                c1, c2, remQty, cost, price
                            ));
                        }
                    }
                }
            }

            // 2. Calculate Eliminations with Engine
            var elimResult = _eliminationEngine.CalculateEliminations(
                runId, balancePairs, tradingVolumes, heldStocks, request.EliminateUnrealizedProfit
            );

            // 3. Compute group total assets, liabilities, equity across participating companies
            decimal groupAssets = 0m;
            decimal groupLiabilities = 0m;
            decimal groupEquity = 0m;

            foreach (var compId in request.ParticipatingCompanyIds)
            {
                const string balSql = @"
                    SELECT a.account_type, COALESCE(SUM(jl.debit_functional - jl.credit_functional), 0)
                    FROM finance.accounts a
                    LEFT JOIN finance.journal_lines jl ON jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id AND jl.account_id = a.id
                    LEFT JOIN finance.journals j ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
                    WHERE a.tenant_id = @tenantId AND a.company_id = @companyId
                      AND (j.id IS NULL OR j.status = 'posted')
                    GROUP BY a.account_type;";

                await using var bCmd = new NpgsqlCommand(balSql, conn, tx);
                bCmd.Parameters.AddWithValue("tenantId", tenantId);
                bCmd.Parameters.AddWithValue("companyId", compId);

                await using var bReader = await bCmd.ExecuteReaderAsync(ct);
                while (await bReader.ReadAsync(ct))
                {
                    var type = bReader.GetString(0);
                    var net = bReader.GetDecimal(1);
                    switch (type)
                    {
                        case "asset":
                            groupAssets += net;
                            break;
                        case "liability":
                            groupLiabilities += -net; // normal balance credit
                            break;
                        case "equity":
                            groupEquity += -net;
                            break;
                    }
                }
            }

            // Adjust group totals for eliminations
            // Assets reduce by eliminated receivables & inventory profit
            groupAssets -= (elimResult.TotalIntercompanyBalanceEliminated + elimResult.TotalUnrealizedProfitEliminated);
            // Liabilities reduce by eliminated payables
            groupLiabilities -= elimResult.TotalIntercompanyBalanceEliminated;
            // Equity (or retained earnings) absorbs unrealized profit elimination
            groupEquity -= elimResult.TotalUnrealizedProfitEliminated;

            var partJson = JsonSerializer.Serialize(request.ParticipatingCompanyIds);

            // 4. Insert consolidation_runs
            const string insRunSql = @"
                INSERT INTO consolidation.consolidation_runs (
                    id, tenant_id, holding_company_id, run_code, fiscal_period_id,
                    reporting_currency_code, status, total_consolidated_assets_pkr,
                    total_consolidated_liabilities_pkr, total_consolidated_equity_pkr,
                    total_intercompany_eliminations_pkr, unrealized_profit_elimination_pkr,
                    participating_companies, created_by, approved_at, approved_by
                )
                VALUES (
                    @id, @tenantId, @holdingComp, @code, @periodId,
                    @curr, 'completed', @assets,
                    @liab, @equity,
                    @elimTotal, @unrealizedTotal,
                    @partJson::jsonb, @userId, now(), @userId
                );";

            await using (var rCmd = new NpgsqlCommand(insRunSql, conn, tx))
            {
                rCmd.Parameters.AddWithValue("id", runId);
                rCmd.Parameters.AddWithValue("tenantId", tenantId);
                rCmd.Parameters.AddWithValue("holdingComp", (object?)request.HoldingCompanyId ?? DBNull.Value);
                rCmd.Parameters.AddWithValue("code", request.RunCode);
                rCmd.Parameters.AddWithValue("periodId", request.FiscalPeriodId);
                rCmd.Parameters.AddWithValue("curr", request.ReportingCurrencyCode);
                rCmd.Parameters.AddWithValue("assets", groupAssets);
                rCmd.Parameters.AddWithValue("liab", groupLiabilities);
                rCmd.Parameters.AddWithValue("equity", groupEquity);
                rCmd.Parameters.AddWithValue("elimTotal", elimResult.TotalIntercompanyBalanceEliminated + elimResult.TotalRevenueEliminated);
                rCmd.Parameters.AddWithValue("unrealizedTotal", elimResult.TotalUnrealizedProfitEliminated);
                rCmd.Parameters.AddWithValue("partJson", partJson);
                rCmd.Parameters.AddWithValue("userId", userId);
                await rCmd.ExecuteNonQueryAsync(ct);
            }

            // 5. Insert elimination_lines
            foreach (var l in elimResult.Lines)
            {
                const string insLineSql = @"
                    INSERT INTO consolidation.elimination_lines (
                        id, tenant_id, consolidation_run_id, elimination_type,
                        from_company_id, to_company_id, account_code,
                        debit_amount_pkr, credit_amount_pkr, description
                    )
                    VALUES (
                        @id, @tenantId, @runId, @type,
                        @fromComp, @toComp, @accCode,
                        @debit, @credit, @desc
                    );";

                await using var elCmd = new NpgsqlCommand(insLineSql, conn, tx);
                elCmd.Parameters.AddWithValue("id", l.Id);
                elCmd.Parameters.AddWithValue("tenantId", tenantId);
                elCmd.Parameters.AddWithValue("runId", runId);
                elCmd.Parameters.AddWithValue("type", l.EliminationType);
                elCmd.Parameters.AddWithValue("fromComp", l.FromCompanyId);
                elCmd.Parameters.AddWithValue("toComp", l.ToCompanyId);
                elCmd.Parameters.AddWithValue("accCode", l.AccountCode);
                elCmd.Parameters.AddWithValue("debit", l.DebitAmountPkr);
                elCmd.Parameters.AddWithValue("credit", l.CreditAmountPkr);
                elCmd.Parameters.AddWithValue("desc", l.Description);
                await elCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return (await GetConsolidationRunByIdAsync(tenantId, runId, ct))!;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<ConsolidationRunDto?> GetConsolidationRunByIdAsync(Guid tenantId, Guid runId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        const string rSql = @"
            SELECT id, tenant_id, holding_company_id, run_code, fiscal_period_id,
                   reporting_currency_code, status, total_consolidated_assets_pkr,
                   total_consolidated_liabilities_pkr, total_consolidated_equity_pkr,
                   total_intercompany_eliminations_pkr, unrealized_profit_elimination_pkr,
                   elimination_journal_id, participating_companies, created_at,
                   approved_at, approved_by
            FROM consolidation.consolidation_runs
            WHERE tenant_id = @tenantId AND id = @runId;";

        await using var rCmd = new NpgsqlCommand(rSql, conn);
        rCmd.Parameters.AddWithValue("tenantId", tenantId);
        rCmd.Parameters.AddWithValue("runId", runId);

        ConsolidationRunDto? run = null;
        await using (var reader = await rCmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                var partCompaniesJson = reader.GetString(13);
                var companies = JsonSerializer.Deserialize<List<Guid>>(partCompaniesJson) ?? new List<Guid>();

                run = new ConsolidationRunDto(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetGuid(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetDecimal(7),
                    reader.GetDecimal(8),
                    reader.GetDecimal(9),
                    reader.GetDecimal(10),
                    reader.GetDecimal(11),
                    reader.IsDBNull(12) ? null : reader.GetGuid(12),
                    companies,
                    new List<EliminationLineDto>(),
                    reader.GetDateTime(14),
                    reader.IsDBNull(15) ? null : reader.GetDateTime(15),
                    reader.IsDBNull(16) ? null : reader.GetGuid(16)
                );
            }
        }

        if (run == null)
            return null;

        // Fetch elimination lines
        const string lSql = @"
            SELECT id, consolidation_run_id, elimination_type, from_company_id,
                   to_company_id, account_code, debit_amount_pkr, credit_amount_pkr, description
            FROM consolidation.elimination_lines
            WHERE tenant_id = @tenantId AND consolidation_run_id = @runId
            ORDER BY created_at ASC;";

        await using var lCmd = new NpgsqlCommand(lSql, conn);
        lCmd.Parameters.AddWithValue("tenantId", tenantId);
        lCmd.Parameters.AddWithValue("runId", runId);

        var lines = new List<EliminationLineDto>();
        await using (var lReader = await lCmd.ExecuteReaderAsync(ct))
        {
            while (await lReader.ReadAsync(ct))
            {
                lines.Add(new EliminationLineDto(
                    lReader.GetGuid(0),
                    lReader.GetGuid(1),
                    lReader.GetString(2),
                    lReader.GetGuid(3),
                    lReader.GetGuid(4),
                    lReader.GetString(5),
                    lReader.GetDecimal(6),
                    lReader.GetDecimal(7),
                    lReader.GetString(8)
                ));
            }
        }

        return run with { EliminationLines = lines };
    }

    public async Task<IReadOnlyList<ConsolidationRunDto>> GetConsolidationRunsAsync(Guid tenantId, Guid? fiscalPeriodId = null, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        var sql = @"
            SELECT id, tenant_id, holding_company_id, run_code, fiscal_period_id,
                   reporting_currency_code, status, total_consolidated_assets_pkr,
                   total_consolidated_liabilities_pkr, total_consolidated_equity_pkr,
                   total_intercompany_eliminations_pkr, unrealized_profit_elimination_pkr,
                   elimination_journal_id, participating_companies, created_at,
                   approved_at, approved_by
            FROM consolidation.consolidation_runs
            WHERE tenant_id = @tenantId";

        if (fiscalPeriodId.HasValue && fiscalPeriodId.Value != Guid.Empty)
        {
            sql += " AND fiscal_period_id = @periodId";
        }
        sql += " ORDER BY created_at DESC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        if (fiscalPeriodId.HasValue && fiscalPeriodId.Value != Guid.Empty)
            cmd.Parameters.AddWithValue("periodId", fiscalPeriodId.Value);

        var list = new List<ConsolidationRunDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var partCompaniesJson = reader.GetString(13);
            var companies = JsonSerializer.Deserialize<List<Guid>>(partCompaniesJson) ?? new List<Guid>();

            list.Add(new ConsolidationRunDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetDecimal(11),
                reader.IsDBNull(12) ? null : reader.GetGuid(12),
                companies,
                new List<EliminationLineDto>(),
                reader.GetDateTime(14),
                reader.IsDBNull(15) ? null : reader.GetDateTime(15),
                reader.IsDBNull(16) ? null : reader.GetGuid(16)
            ));
        }

        return list;
    }

    public async Task<ConsolidatedStatementDto> GetConsolidatedStatementAsync(Guid tenantId, Guid runId, string statementType, CancellationToken ct = default)
    {
        var run = await GetConsolidationRunByIdAsync(tenantId, runId, ct);
        if (run == null)
            throw new InvalidOperationException($"Consolidation run {runId} not found.");

        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        // Account range filter: Balance Sheet = 1000-3999, P&L = 4000-5999
        var isBalanceSheet = string.Equals(statementType, "balance_sheet", StringComparison.OrdinalIgnoreCase);
        var minCode = isBalanceSheet ? "1000" : "4000";
        var maxCode = isBalanceSheet ? "3999" : "5999";

        // Query accounts and trial balances per company
        const string accSql = @"
            SELECT a.company_id, a.account_code, a.account_name, a.account_type,
                   COALESCE(SUM(jl.debit_functional - jl.credit_functional), 0) as net_balance
            FROM finance.accounts a
            LEFT JOIN finance.journal_lines jl ON jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id AND jl.account_id = a.id
            LEFT JOIN finance.journals j ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
            WHERE a.tenant_id = @tenantId AND a.account_code >= @minCode AND a.account_code <= @maxCode
              AND (j.id IS NULL OR j.status = 'posted')
            GROUP BY a.company_id, a.account_code, a.account_name, a.account_type
            ORDER BY a.account_code ASC;";

        await using var accCmd = new NpgsqlCommand(accSql, conn);
        accCmd.Parameters.AddWithValue("tenantId", tenantId);
        accCmd.Parameters.AddWithValue("minCode", minCode);
        accCmd.Parameters.AddWithValue("maxCode", maxCode);

        var accountsMap = new Dictionary<string, (string Name, string Type, Dictionary<Guid, decimal> Balances)>();

        await using (var reader = await accCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var compId = reader.GetGuid(0);
                var code = reader.GetString(1);
                var name = reader.GetString(2);
                var type = reader.GetString(3);
                var net = reader.GetDecimal(4);

                if (!accountsMap.TryGetValue(code, out var entry))
                {
                    entry = (name, type, new Dictionary<Guid, decimal>());
                    accountsMap[code] = entry;
                }
                entry.Balances[compId] = net;
            }
        }

        // Add accounts mentioned in elimination lines that may not have GL postings
        foreach (var elim in run.EliminationLines)
        {
            if (string.Compare(elim.AccountCode, minCode) >= 0 && string.Compare(elim.AccountCode, maxCode) <= 0)
            {
                if (!accountsMap.ContainsKey(elim.AccountCode))
                {
                    accountsMap[elim.AccountCode] = (elim.Description, isBalanceSheet ? "asset" : "revenue", new Dictionary<Guid, decimal>());
                }
            }
        }

        if (isBalanceSheet)
        {
            var pnlDebits = run.EliminationLines.Where(l => string.Compare(l.AccountCode, "4000") >= 0).Sum(l => l.DebitAmountPkr);
            var pnlCredits = run.EliminationLines.Where(l => string.Compare(l.AccountCode, "4000") >= 0).Sum(l => l.CreditAmountPkr);
            var pnlNet = pnlDebits - pnlCredits;
            if (pnlNet != 0)
            {
                accountsMap["3999"] = ("Consolidated Retained Earnings Impact", "equity", new Dictionary<Guid, decimal>());
            }
        }
        else
        {
            var bsDebits = run.EliminationLines.Where(l => string.Compare(l.AccountCode, "4000") < 0).Sum(l => l.DebitAmountPkr);
            var bsCredits = run.EliminationLines.Where(l => string.Compare(l.AccountCode, "4000") < 0).Sum(l => l.CreditAmountPkr);
            var bsNet = bsCredits - bsDebits;
            if (bsNet != 0)
            {
                accountsMap["5999"] = ("Consolidated Balance Sheet Impact", "revenue", new Dictionary<Guid, decimal>());
            }
        }

        var lines = new List<ConsolidatedStatementLineDto>();
        decimal totalElimDebits = 0m;
        decimal totalElimCredits = 0m;

        foreach (var kvp in accountsMap.OrderBy(k => k.Key))
        {
            var code = kvp.Key;
            var (name, type, companyBals) = kvp.Value;

            decimal elimDebits = 0m;
            decimal elimCredits = 0m;

            if (code == "3999")
            {
                var pnlDebits = run.EliminationLines.Where(l => string.Compare(l.AccountCode, "4000") >= 0).Sum(l => l.DebitAmountPkr);
                var pnlCredits = run.EliminationLines.Where(l => string.Compare(l.AccountCode, "4000") >= 0).Sum(l => l.CreditAmountPkr);
                var pnlNet = pnlDebits - pnlCredits;
                if (pnlNet > 0) elimDebits = pnlNet;
                else elimCredits = -pnlNet;
            }
            else if (code == "5999")
            {
                var bsDebits = run.EliminationLines.Where(l => string.Compare(l.AccountCode, "4000") < 0).Sum(l => l.DebitAmountPkr);
                var bsCredits = run.EliminationLines.Where(l => string.Compare(l.AccountCode, "4000") < 0).Sum(l => l.CreditAmountPkr);
                var bsNet = bsCredits - bsDebits;
                if (bsNet > 0) elimCredits = bsNet;
                else elimDebits = -bsNet;
            }
            else
            {
                elimDebits = run.EliminationLines.Where(l => l.AccountCode == code).Sum(l => l.DebitAmountPkr);
                elimCredits = run.EliminationLines.Where(l => l.AccountCode == code).Sum(l => l.CreditAmountPkr);
            }

            totalElimDebits += elimDebits;
            totalElimCredits += elimCredits;

            var subtotal = companyBals.Values.Sum();
            // In financial reporting: For Debit normal accounts (Asset, Expense), Elim Debit increases, Elim Credit decreases.
            // For Credit normal accounts (Liability, Equity, Revenue), Elim Debit decreases, Elim Credit increases.
            decimal consolidated;
            if (type is "asset" or "expense")
            {
                consolidated = subtotal + (elimDebits - elimCredits);
            }
            else
            {
                // Net is stored as Dr - Cr, so credit accounts are negative. Add (elimDebits - elimCredits) reduces the credit.
                consolidated = subtotal + (elimDebits - elimCredits);
            }

            lines.Add(new ConsolidatedStatementLineDto(
                code,
                name,
                type,
                companyBals,
                elimDebits,
                elimCredits,
                consolidated
            ));
        }

        var netElim = totalElimDebits - totalElimCredits;
        var isBalanced = Math.Abs(netElim) < 0.0001m;

        return new ConsolidatedStatementDto(
            run.RunCode,
            run.FiscalPeriodId,
            statementType,
            lines,
            totalElimDebits,
            totalElimCredits,
            netElim,
            isBalanced
        );
    }

    // ── Helper methods ──────────────────────────────────────────────────────────

    private static IntercompanyTransferDto MapTransfer(NpgsqlDataReader r, IReadOnlyList<IntercompanyTransferLineDto> lines)
    {
        return new IntercompanyTransferDto(
            r.GetGuid(0),
            r.GetGuid(1),
            r.GetString(2),
            r.GetGuid(3),
            r.IsDBNull(4) ? null : r.GetGuid(4),
            r.GetGuid(5),
            r.IsDBNull(6) ? null : r.GetGuid(6),
            r.GetString(7),
            DateOnly.FromDateTime(r.GetDateTime(8)),
            r.IsDBNull(9) ? null : DateOnly.FromDateTime(r.GetDateTime(9)),
            r.GetString(10),
            r.GetDecimal(11),
            r.IsDBNull(12) ? null : r.GetGuid(12),
            r.IsDBNull(13) ? null : r.GetGuid(13),
            r.IsDBNull(14) ? null : r.GetString(14),
            lines,
            r.GetDateTime(15)
        );
    }

    private static async Task<Guid> ResolveOpenFiscalPeriodAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenantId, Guid companyId, CancellationToken ct)
    {
        const string sql = @"
            SELECT id FROM finance.fiscal_periods
            WHERE tenant_id = @tenantId AND company_id = @companyId AND status = 'open'
            ORDER BY starts_on ASC LIMIT 1;";

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var res = await cmd.ExecuteScalarAsync(ct);
        if (res is Guid g) return g;

        // If no open period, create or pick first period
        const string anySql = @"
            SELECT id FROM finance.fiscal_periods
            WHERE tenant_id = @tenantId AND company_id = @companyId
            LIMIT 1;";
        await using var anyCmd = new NpgsqlCommand(anySql, conn, tx);
        anyCmd.Parameters.AddWithValue("tenantId", tenantId);
        anyCmd.Parameters.AddWithValue("companyId", companyId);
        var anyRes = await anyCmd.ExecuteScalarAsync(ct);
        if (anyRes is Guid ag) return ag;

        throw new InvalidOperationException($"No fiscal period found for company {companyId}.");
    }

    private static async Task<Guid> ResolveAccountIdAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenantId, Guid companyId, string accountCode, CancellationToken ct)
    {
        const string sql = @"
            SELECT id FROM finance.accounts
            WHERE tenant_id = @tenantId AND company_id = @companyId AND account_code = @code
            LIMIT 1;";

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("code", accountCode);

        var res = await cmd.ExecuteScalarAsync(ct);
        if (res is Guid g) return g;

        throw new InvalidOperationException($"Account {accountCode} not found for company {companyId}.");
    }

    private static async Task<decimal> QueryAccountBalanceAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenantId, Guid companyId, Guid accountId, CancellationToken ct)
    {
        const string sql = @"
            SELECT COALESCE(SUM(jl.debit_functional - jl.credit_functional), 0)
            FROM finance.journal_lines jl
            JOIN finance.journals j ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
            WHERE jl.tenant_id = @tenantId AND jl.company_id = @companyId AND jl.account_id = @accId
              AND j.status = 'posted';";

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("accId", accountId);

        var bal = Convert.ToDecimal(await cmd.ExecuteScalarAsync(ct) ?? 0m);
        return Math.Abs(bal);
    }

    private static async Task<Guid> ResolveDefaultVariantIdAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenantId, Guid productId, CancellationToken ct)
    {
        const string sql = @"
            SELECT variant_id FROM erp_inventory.product_variant
            WHERE tenant_id = @tenantId AND product_id = @prodId
            LIMIT 1;";

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("prodId", productId);

        var res = await cmd.ExecuteScalarAsync(ct);
        if (res is Guid g) return g;

        // Resolve or create a default UOM
        var uomId = Guid.NewGuid();
        const string uomSql = @"
            SELECT uom_id FROM erp_inventory.unit_of_measure
            WHERE tenant_id = @tenantId
            LIMIT 1;";
        await using (var uomCmd = new NpgsqlCommand(uomSql, conn, tx))
        {
            uomCmd.Parameters.AddWithValue("tenantId", tenantId);
            var uomRes = await uomCmd.ExecuteScalarAsync(ct);
            if (uomRes is Guid ug)
            {
                uomId = ug;
            }
            else
            {
                const string insUomSql = @"
                    INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension, decimal_places)
                    VALUES (@tenantId, @uomId, 'EA', 'Each', 'count', 0)
                    ON CONFLICT (tenant_id, code) DO UPDATE SET name = EXCLUDED.name
                    RETURNING uom_id;";
                await using var insUomCmd = new NpgsqlCommand(insUomSql, conn, tx);
                insUomCmd.Parameters.AddWithValue("tenantId", tenantId);
                insUomCmd.Parameters.AddWithValue("uomId", uomId);
                uomId = (Guid)(await insUomCmd.ExecuteScalarAsync(ct))!;
            }
        }

        // Create default variant if none exists
        var newVarId = Guid.NewGuid();
        const string insVarSql = @"
            INSERT INTO erp_inventory.product_variant (
                tenant_id, product_id, variant_id, sku, variant_name, base_uom_id, is_active
            )
            VALUES (
                @tenantId, @prodId, @varId, 'DEF-' || substr(@varId::text, 1, 8), 'Standard', @uomId, true
            )
            RETURNING variant_id;";

        await using var insCmd = new NpgsqlCommand(insVarSql, conn, tx);
        insCmd.Parameters.AddWithValue("tenantId", tenantId);
        insCmd.Parameters.AddWithValue("prodId", productId);
        insCmd.Parameters.AddWithValue("varId", newVarId);
        insCmd.Parameters.AddWithValue("uomId", uomId);

        return (Guid)(await insCmd.ExecuteScalarAsync(ct))!;
    }
}
