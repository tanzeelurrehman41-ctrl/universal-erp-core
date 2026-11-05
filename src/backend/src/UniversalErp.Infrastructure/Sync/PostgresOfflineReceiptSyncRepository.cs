using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using UniversalErp.Application.Inventory;
using UniversalErp.Application.Sync;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Infrastructure.Sync;

public sealed class PostgresOfflineReceiptSyncRepository(NpgsqlDataSource dataSource)
    : IOfflineReceiptSyncRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromSeconds(30);

    public async Task<OfflineDeviceGrant?> RegisterDeviceAsync(
        TenantContext context,
        Guid clientDeviceId,
        string platformCode,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        var deviceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{context.TenantId:N}:{clientDeviceId:N}"))).ToLowerInvariant();
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);

        Guid deviceId;
        await using (var enroll = new NpgsqlCommand(
            "INSERT INTO identity.devices (tenant_id, device_identifier_hash, platform_code, device_name, status, enrolled_at, last_seen_at) VALUES (@tenantId, @hash, @platform, @name, 'active', now(), now()) ON CONFLICT (tenant_id, device_identifier_hash) DO NOTHING RETURNING id",
            db.Connection,
            db.Transaction))
        {
            enroll.Parameters.AddWithValue("tenantId", context.TenantId);
            enroll.Parameters.AddWithValue("hash", deviceHash);
            enroll.Parameters.AddWithValue("platform", platformCode);
            enroll.Parameters.AddWithValue("name", (object?)deviceName ?? DBNull.Value);
            var inserted = await enroll.ExecuteScalarAsync(cancellationToken);
            if (inserted is Guid createdId)
            {
                deviceId = createdId;
            }
            else
            {
                await using var existing = new NpgsqlCommand(
                    "SELECT id, status FROM identity.devices WHERE tenant_id = @tenantId AND device_identifier_hash = @hash FOR UPDATE",
                    db.Connection,
                    db.Transaction);
                existing.Parameters.AddWithValue("tenantId", context.TenantId);
                existing.Parameters.AddWithValue("hash", deviceHash);
                await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken) || reader.GetString(1) != "active")
                {
                    await reader.CloseAsync();
                    await db.Transaction.CommitAsync(cancellationToken);
                    return null;
                }
                deviceId = reader.GetGuid(0);
                await reader.CloseAsync();
                await using var touch = new NpgsqlCommand(
                    "UPDATE identity.devices SET last_seen_at = now(), device_name = COALESCE(@name, device_name) WHERE tenant_id = @tenantId AND id = @deviceId",
                    db.Connection,
                    db.Transaction);
                touch.Parameters.AddWithValue("tenantId", context.TenantId);
                touch.Parameters.AddWithValue("deviceId", deviceId);
                touch.Parameters.AddWithValue("name", (object?)deviceName ?? DBNull.Value);
                await touch.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var companyCheck = new NpgsqlCommand(
            "SELECT status FROM organization.companies WHERE tenant_id = @tenantId AND id = @companyId",
            db.Connection,
            db.Transaction))
        {
            companyCheck.Parameters.AddWithValue("tenantId", context.TenantId);
            companyCheck.Parameters.AddWithValue("companyId", context.CompanyId);
            var companyStatus = await companyCheck.ExecuteScalarAsync(cancellationToken) as string;
            if (companyStatus != "active")
            {
                await db.Transaction.CommitAsync(cancellationToken);
                return null;
            }
        }

        await using (var scopeCheck = new NpgsqlCommand(
            "SELECT scope_status FROM sync.device_company_scopes WHERE tenant_id = @tenantId AND device_id = @deviceId AND company_id = @companyId FOR UPDATE",
            db.Connection,
            db.Transaction))
        {
            scopeCheck.Parameters.AddWithValue("tenantId", context.TenantId);
            scopeCheck.Parameters.AddWithValue("deviceId", deviceId);
            scopeCheck.Parameters.AddWithValue("companyId", context.CompanyId);
            var existingScopeStatus = await scopeCheck.ExecuteScalarAsync(cancellationToken) as string;
            if (existingScopeStatus is not null && existingScopeStatus != "active")
            {
                await db.Transaction.CommitAsync(cancellationToken);
                return null;
            }

            if (existingScopeStatus is null)
            {
                await using var insertScope = new NpgsqlCommand(
                    "INSERT INTO sync.device_company_scopes (tenant_id, device_id, company_id, scope_status, all_locations, revoked_at) VALUES (@tenantId, @deviceId, @companyId, 'active', true, NULL)",
                    db.Connection,
                    db.Transaction);
                insertScope.Parameters.AddWithValue("tenantId", context.TenantId);
                insertScope.Parameters.AddWithValue("deviceId", deviceId);
                insertScope.Parameters.AddWithValue("companyId", context.CompanyId);
                await insertScope.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        var grantToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var grantHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(grantToken))).ToLowerInvariant();
        Guid grantId;
        DateTimeOffset expiresAt;
        await using (var grant = new NpgsqlCommand(
            "INSERT INTO sync.offline_grants (tenant_id, device_id, company_id, user_id, allowed_operations, allowed_location_ids, limits, expires_at, grant_signature) VALUES (@tenantId, @deviceId, @companyId, @userId, @operations, '[]'::jsonb, @limits, now() + interval '7 days', @signatureHash) RETURNING id, expires_at",
            db.Connection,
            db.Transaction))
        {
            grant.Parameters.AddWithValue("tenantId", context.TenantId);
            grant.Parameters.AddWithValue("deviceId", deviceId);
            grant.Parameters.AddWithValue("companyId", context.CompanyId);
            grant.Parameters.AddWithValue("userId", context.UserId);
            grant.Parameters.Add(new NpgsqlParameter("operations", NpgsqlDbType.Jsonb)
            {
                Value = "[\"inventory.receipt.post\"]",
            });
            grant.Parameters.Add(new NpgsqlParameter("limits", NpgsqlDbType.Jsonb)
            {
                Value = "{\"maxBatchSize\":50,\"maxReceiptQuantity\":999999999999999}",
            });
            grant.Parameters.AddWithValue("signatureHash", grantHash);
            await using var reader = await grant.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            grantId = reader.GetGuid(0);
            expiresAt = reader.GetFieldValue<DateTimeOffset>(1);
            await reader.CloseAsync();
        }

        await db.Transaction.CommitAsync(cancellationToken);
        return new OfflineDeviceGrant(deviceId, grantId, grantToken, expiresAt);
    }

    public async Task<OfflineInboxReservation> ReserveReceiptOperationAsync(
        TenantContext context,
        OfflineReceiptBatchRequest batch,
        OfflineReceiptOperation operation,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        if (!await ValidateAuthorizationAsync(db, context, batch, cancellationToken))
        {
            await db.Transaction.CommitAsync(cancellationToken);
            return new OfflineInboxReservation("unauthorized");
        }

        var claimResult = await TryClaimOrInspectAsync(db, context, batch, operation, payloadHash, DefaultLeaseDuration, cancellationToken);
        await db.Transaction.CommitAsync(cancellationToken);
        return claimResult;
    }

    public async Task CompleteReceiptOperationAsync(
        TenantContext context,
        Guid operationId,
        OfflineReceiptOperationResult result,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);
        var responsePayload = JsonSerializer.Serialize(new { receipt = result.Receipt, code = result.Code }, JsonOptions);
        await using var complete = new NpgsqlCommand(
            """
            UPDATE sync.operation_inbox
            SET outcome = @outcome,
                server_record_type = CASE WHEN @recordId IS NULL THEN NULL ELSE 'inventory_movement' END,
                server_record_id = @recordId,
                response_payload = @response::jsonb,
                processed_at = now(),
                claim_token = NULL,
                claim_expires_at = NULL
            WHERE tenant_id = @tenantId AND company_id = @companyId AND operation_id = @operationId
              AND outcome = 'deferred'
            """,
            db.Connection,
            db.Transaction);
        complete.Parameters.AddWithValue("outcome", result.Status);
        complete.Parameters.AddWithValue("recordId", (object?)result.Receipt?.MovementId ?? DBNull.Value);
        complete.Parameters.AddWithValue("response", responsePayload);
        complete.Parameters.AddWithValue("tenantId", context.TenantId);
        complete.Parameters.AddWithValue("companyId", context.CompanyId);
        complete.Parameters.AddWithValue("operationId", operationId);
        var changed = await complete.ExecuteNonQueryAsync(cancellationToken);
        if (changed != 1)
        {
            throw new InvalidOperationException($"The reserved offline operation {operationId} could not be acknowledged; expected 1 row, got {changed}.");
        }
        await db.Transaction.CommitAsync(cancellationToken);
    }

    public async Task<OfflineReceiptOperationResult> ProcessReceiptOperationAtomicAsync(
        TenantContext context,
        OfflineReceiptBatchRequest batch,
        OfflineReceiptOperation operation,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var db = await OpenScopeAsync(context.TenantId, context.CompanyId, cancellationToken);

        // 1. Authorization validation: tenant, company, device, user, grant with strict row-level shared locks
        if (!await ValidateAuthorizationAsync(db, context, batch, cancellationToken))
        {
            await db.Transaction.CommitAsync(cancellationToken);
            return new OfflineReceiptOperationResult(
                operation.OperationId, operation.DeviceSequence, "unauthorized", null, "OFFLINE_DEVICE_OR_GRANT_INVALID");
        }

        // 2. Claim / Lease Reservation (durable claim with crash recovery)
        var claim = await TryClaimOrInspectAsync(db, context, batch, operation, payloadHash, DefaultLeaseDuration, cancellationToken);
        if (claim.Disposition == "unauthorized")
        {
            await db.Transaction.CommitAsync(cancellationToken);
            return new OfflineReceiptOperationResult(
                operation.OperationId, operation.DeviceSequence, "unauthorized", null, "OFFLINE_DEVICE_OR_GRANT_INVALID");
        }
        if (claim.Disposition is "replay" or "conflict")
        {
            await db.Transaction.CommitAsync(cancellationToken);
            return claim.ExistingResult!;
        }
        if (claim.Disposition == "deferred")
        {
            await db.Transaction.CommitAsync(cancellationToken);
            return claim.ExistingResult ?? new OfflineReceiptOperationResult(
                operation.OperationId, operation.DeviceSequence, "deferred", null, "OPERATION_IN_PROGRESS");
        }

        var claimToken = claim.ClaimToken;
        var request = operation.Payload;

        // 3. Warehouse existence & active status validation
        Guid? warehouseLocationId = null;
        await using (var whCmd = new NpgsqlCommand(
            "SELECT location_id, is_active FROM erp_inventory.warehouse WHERE tenant_id = @tenantId AND company_id = @companyId AND warehouse_id = @warehouseId",
            db.Connection,
            db.Transaction))
        {
            whCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            whCmd.Parameters.AddWithValue("companyId", context.CompanyId);
            whCmd.Parameters.AddWithValue("warehouseId", request.WarehouseId);
            await using var reader = await whCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(1))
            {
                await reader.CloseAsync();
                return await RecordTerminalRejectionAsync(db, context, operation, claimToken, "WAREHOUSE_NOT_FOUND", cancellationToken);
            }
            if (!reader.IsDBNull(0))
            {
                warehouseLocationId = reader.GetGuid(0);
            }
            await reader.CloseAsync();
        }

        // 4. Bin validation: belongs to warehouse, active, storage or receiving type
        await using (var binCmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM erp_inventory.stock_bin WHERE tenant_id = @tenantId AND company_id = @companyId AND warehouse_id = @warehouseId AND bin_id = @binId AND is_active = true AND bin_type IN ('storage', 'receiving'))",
            db.Connection,
            db.Transaction))
        {
            binCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            binCmd.Parameters.AddWithValue("companyId", context.CompanyId);
            binCmd.Parameters.AddWithValue("warehouseId", request.WarehouseId);
            binCmd.Parameters.AddWithValue("binId", request.BinId);
            if (await binCmd.ExecuteScalarAsync(cancellationToken) as bool? != true)
            {
                return await RecordTerminalRejectionAsync(db, context, operation, claimToken, "RECEIPT_LOCATION_NOT_SUPPORTED", cancellationToken);
            }
        }

        // 5. Effective location restrictions enforcement:
        // A warehouse UUID is NEVER treated as interchangeable with a location UUID.
        // The effective location is strictly warehouse.location_id.
        var isLocationAuthorized = await ValidateLocationRestrictionsAsync(
            db, context, batch.DeviceId, batch.GrantId, warehouseLocationId, cancellationToken);
        if (!isLocationAuthorized)
        {
            return await RecordTerminalRejectionAsync(db, context, operation, claimToken, "LOCATION_NOT_AUTHORIZED", cancellationToken);
        }

        // 6. Variant & Unit of Measure validation
        Guid baseUomId;
        await using (var varCmd = new NpgsqlCommand(
            """
            SELECT variant.base_uom_id
            FROM erp_inventory.product_variant AS variant
            JOIN erp_inventory.product AS product
              ON product.tenant_id = variant.tenant_id AND product.product_id = variant.product_id
            WHERE variant.tenant_id = @tenantId
              AND variant.variant_id = @variantId
              AND variant.is_active = true
              AND product.is_active = true
              AND product.product_type = 'stock'
              AND variant.tracking_mode = 'none'
            """,
            db.Connection,
            db.Transaction))
        {
            varCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            varCmd.Parameters.AddWithValue("variantId", request.VariantId);
            var varResult = await varCmd.ExecuteScalarAsync(cancellationToken);
            if (varResult is not Guid foundUom)
            {
                return await RecordTerminalRejectionAsync(db, context, operation, claimToken, "VARIANT_INACTIVE", cancellationToken);
            }
            baseUomId = foundUom;
        }

        if (baseUomId != request.UnitOfMeasureId)
        {
            return await RecordTerminalRejectionAsync(db, context, operation, claimToken, "RECEIPT_UNIT_MUST_MATCH_BASE_UNIT", cancellationToken);
        }

        // 7. Command Idempotency serialization & conflict check:
        // Serialize on (tenant_id, idempotency_key) to prevent concurrent transactions from
        // racing past the pre-check and writing inventory before idempotency conflict is determined.
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('cmd_idemp:' || @tenantId::text || ':' || @key::text))",
            db.Connection,
            db.Transaction))
        {
            lockCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            lockCmd.Parameters.AddWithValue("key", operation.OperationId);
            await lockCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Check if idempotency key already exists for this tenant
        // Reject and do NOT overwrite resource_id if existing record differs in command_code, request_hash, or company_id
        await using (var checkIdemp = new NpgsqlCommand(
            """
            SELECT command_code, request_hash, resource_id, company_id
            FROM erp_inventory.command_idempotency
            WHERE tenant_id = @tenantId AND idempotency_key = @key
            FOR UPDATE
            """,
            db.Connection,
            db.Transaction))
        {
            checkIdemp.Parameters.AddWithValue("tenantId", context.TenantId);
            checkIdemp.Parameters.AddWithValue("key", operation.OperationId);
            await using var idempReader = await checkIdemp.ExecuteReaderAsync(cancellationToken);
            if (await idempReader.ReadAsync(cancellationToken))
            {
                var existingCode = idempReader.GetString(0);
                var existingHash = idempReader.GetString(1);
                var existingResourceId = idempReader.IsDBNull(2) ? (Guid?)null : idempReader.GetGuid(2);
                var existingCompany = idempReader.IsDBNull(3) ? (Guid?)null : idempReader.GetGuid(3);
                await idempReader.CloseAsync();

                if (existingCode != "inventory.receipt.post" ||
                    !string.Equals(existingHash.Trim(), payloadHash.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    (existingCompany.HasValue && existingCompany.Value != context.CompanyId))
                {
                    return await RecordTerminalConflictAsync(
                        db, context, operation, batch.DeviceId, claimToken, "COMMAND_IDEMPOTENCY_CONFLICT", null, cancellationToken);
                }

                if (existingResourceId.HasValue)
                {
                    var existingMovementId = existingResourceId.Value;
                    var duplicateSummary = new InventoryReceiptSummary(
                        operation.OperationId, existingMovementId, request.VariantId, request.WarehouseId, request.BinId,
                        request.Quantity, baseUomId, request.OccurredAt ?? operation.CreatedAt, DateTimeOffset.UtcNow);
                    var ackPayload = JsonSerializer.Serialize(new { receipt = duplicateSummary, code = (string?)null }, JsonOptions);
                    await using (var ackCmd = new NpgsqlCommand(
                        """
                        UPDATE sync.operation_inbox
                        SET outcome = 'accepted',
                            server_record_type = 'inventory_movement',
                            server_record_id = @movementId,
                            response_payload = @response::jsonb,
                            processed_at = now(),
                            claim_token = NULL,
                            claim_expires_at = NULL
                        WHERE tenant_id = @tenantId AND company_id = @companyId AND operation_id = @operationId
                          AND outcome = 'deferred'
                          AND (claim_token = @claimToken OR (@claimToken IS NULL AND claim_token IS NULL))
                        """,
                        db.Connection,
                        db.Transaction))
                    {
                        ackCmd.Parameters.AddWithValue("tenantId", context.TenantId);
                        ackCmd.Parameters.AddWithValue("companyId", context.CompanyId);
                        ackCmd.Parameters.AddWithValue("operationId", operation.OperationId);
                        ackCmd.Parameters.AddWithValue("claimToken", (object?)claimToken ?? DBNull.Value);
                        ackCmd.Parameters.AddWithValue("movementId", existingMovementId);
                        ackCmd.Parameters.AddWithValue("response", ackPayload);
                        await ackCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await db.Transaction.CommitAsync(cancellationToken);
                    return new OfflineReceiptOperationResult(operation.OperationId, operation.DeviceSequence, "accepted", duplicateSummary, null);
                }
            }
            else
            {
                await idempReader.CloseAsync();
            }
        }

        // 8. Atomic Writes: Inventory Movement, Movement Leg, Stock Balance, Outbox, Change Log, Idempotency, and Inbox Outcome
        var occurredAt = request.OccurredAt ?? operation.CreatedAt;
        var movementId = Guid.NewGuid();
        DateTimeOffset postedAt;

        await using (var movementCmd = new NpgsqlCommand(
            """
            INSERT INTO erp_inventory.inventory_movement
                (tenant_id, company_id, movement_id, client_transaction_id, movement_type, source_document_type, source_document_id, occurred_at, created_by, note)
            VALUES
                (@tenantId, @companyId, @movementId, @operationId, 'receipt', @sourceType, @sourceId, @occurredAt, @createdBy, @note)
            RETURNING posted_at
            """,
            db.Connection,
            db.Transaction))
        {
            movementCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            movementCmd.Parameters.AddWithValue("companyId", context.CompanyId);
            movementCmd.Parameters.AddWithValue("movementId", movementId);
            movementCmd.Parameters.AddWithValue("operationId", operation.OperationId);
            movementCmd.Parameters.AddWithValue("sourceType", (object?)request.SourceDocumentType ?? DBNull.Value);
            movementCmd.Parameters.AddWithValue("sourceId", (object?)request.SourceDocumentId ?? DBNull.Value);
            movementCmd.Parameters.AddWithValue("occurredAt", occurredAt);
            movementCmd.Parameters.AddWithValue("createdBy", context.UserId);
            movementCmd.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);
            var postedAtRaw = await movementCmd.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Failed to post stock receipt.");
            postedAt = postedAtRaw switch
            {
                DateTimeOffset dto => dto.ToUniversalTime(),
                DateTime dt => new DateTimeOffset(dt.ToUniversalTime()),
                _ => throw new InvalidOperationException("Invalid posting time returned by PostgreSQL."),
            };
        }

        await using (var legCmd = new NpgsqlCommand(
            """
            INSERT INTO erp_inventory.inventory_movement_leg
                (tenant_id, company_id, movement_id, leg_number, variant_id, warehouse_id, bin_id, status_code, quantity_delta, base_uom_id)
            VALUES
                (@tenantId, @companyId, @movementId, 1, @variantId, @warehouseId, @binId, 'available', @quantity, @uomId)
            """,
            db.Connection,
            db.Transaction))
        {
            legCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            legCmd.Parameters.AddWithValue("companyId", context.CompanyId);
            legCmd.Parameters.AddWithValue("movementId", movementId);
            legCmd.Parameters.AddWithValue("variantId", request.VariantId);
            legCmd.Parameters.AddWithValue("warehouseId", request.WarehouseId);
            legCmd.Parameters.AddWithValue("binId", request.BinId);
            legCmd.Parameters.AddWithValue("quantity", request.Quantity);
            legCmd.Parameters.AddWithValue("uomId", baseUomId);
            await legCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var balanceCmd = new NpgsqlCommand(
            """
            INSERT INTO erp_inventory.stock_balance
                (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id)
            VALUES
                (@tenantId, @companyId, @variantId, @warehouseId, @binId, 'available', @quantity, @balanceId)
            ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, (COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid)))
            DO UPDATE SET on_hand_qty = erp_inventory.stock_balance.on_hand_qty + EXCLUDED.on_hand_qty, updated_at = now()
            """,
            db.Connection,
            db.Transaction))
        {
            balanceCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            balanceCmd.Parameters.AddWithValue("companyId", context.CompanyId);
            balanceCmd.Parameters.AddWithValue("variantId", request.VariantId);
            balanceCmd.Parameters.AddWithValue("warehouseId", request.WarehouseId);
            balanceCmd.Parameters.AddWithValue("binId", request.BinId);
            balanceCmd.Parameters.AddWithValue("quantity", request.Quantity);
            balanceCmd.Parameters.AddWithValue("balanceId", Guid.NewGuid());
            await balanceCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var summary = new InventoryReceiptSummary(
            operation.OperationId, movementId, request.VariantId, request.WarehouseId, request.BinId,
            request.Quantity, baseUomId, occurredAt, postedAt);

        var eventPayload = JsonSerializer.Serialize(new
        {
            transactionId = operation.OperationId,
            movementId,
            request.VariantId,
            request.WarehouseId,
            request.BinId,
            request.Quantity,
            unitOfMeasureId = baseUomId,
            occurredAt,
            postedAt,
        });

        await using (var outboxCmd = new NpgsqlCommand(
            """
            INSERT INTO erp_inventory.outbox_event
                (tenant_id, company_id, event_id, aggregate_type, aggregate_id, event_type, payload, occurred_at)
            VALUES
                (@tenantId, @companyId, @eventId, 'inventory_movement', @movementId, 'inventory.receipt.posted', @payload::jsonb, @occurredAt)
            """,
            db.Connection,
            db.Transaction))
        {
            outboxCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            outboxCmd.Parameters.AddWithValue("companyId", context.CompanyId);
            outboxCmd.Parameters.AddWithValue("eventId", Guid.NewGuid());
            outboxCmd.Parameters.AddWithValue("movementId", movementId);
            outboxCmd.Parameters.AddWithValue("payload", eventPayload);
            outboxCmd.Parameters.AddWithValue("occurredAt", occurredAt);
            await outboxCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await CompanyChangeLogWriter.AppendAsync(
            db.Connection,
            db.Transaction,
            context.TenantId,
            context.CompanyId,
            "inventory_movement",
            movementId,
            1,
            "upsert",
            eventPayload,
            cancellationToken);

        await using (var idempCmd = new NpgsqlCommand(
            """
            INSERT INTO erp_inventory.command_idempotency
                (tenant_id, company_id, idempotency_key, command_code, request_hash, resource_id)
            VALUES
                (@tenantId, @companyId, @key, 'inventory.receipt.post', @hash, @resId)
            ON CONFLICT (tenant_id, idempotency_key)
            DO UPDATE SET resource_id = EXCLUDED.resource_id, company_id = EXCLUDED.company_id
            WHERE erp_inventory.command_idempotency.command_code = EXCLUDED.command_code
              AND erp_inventory.command_idempotency.request_hash = EXCLUDED.request_hash
              AND (erp_inventory.command_idempotency.company_id IS NULL OR erp_inventory.command_idempotency.company_id = EXCLUDED.company_id)
            """,
            db.Connection,
            db.Transaction))
        {
            idempCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            idempCmd.Parameters.AddWithValue("companyId", context.CompanyId);
            idempCmd.Parameters.AddWithValue("key", operation.OperationId);
            idempCmd.Parameters.AddWithValue("hash", payloadHash);
            idempCmd.Parameters.AddWithValue("resId", movementId);
            var idempAffected = await idempCmd.ExecuteNonQueryAsync(cancellationToken);
            if (idempAffected == 0)
            {
                throw new InvalidOperationException("Failed to register command idempotency due to an unmatching concurrent record. Transaction will roll back.");
            }
        }

        var responsePayload = JsonSerializer.Serialize(new { receipt = summary, code = (string?)null }, JsonOptions);
        await using (var ackCmd = new NpgsqlCommand(
            """
            UPDATE sync.operation_inbox
            SET outcome = 'accepted',
                server_record_type = 'inventory_movement',
                server_record_id = @movementId,
                response_payload = @response::jsonb,
                processed_at = now(),
                claim_token = NULL,
                claim_expires_at = NULL
            WHERE tenant_id = @tenantId AND company_id = @companyId AND operation_id = @operationId
              AND outcome = 'deferred'
              AND claim_token = @claimToken
            """,
            db.Connection,
            db.Transaction))
        {
            ackCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            ackCmd.Parameters.AddWithValue("companyId", context.CompanyId);
            ackCmd.Parameters.AddWithValue("operationId", operation.OperationId);
            ackCmd.Parameters.AddWithValue("claimToken", (object?)claimToken ?? DBNull.Value);
            ackCmd.Parameters.AddWithValue("movementId", movementId);
            ackCmd.Parameters.AddWithValue("response", responsePayload);
            var affected = await ackCmd.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1)
            {
                throw new InvalidOperationException(
                    $"Failed to acknowledge receipt operation {operation.OperationId}: expected 1 updated row, got {affected}. Claim token mismatch or outcome changed; transaction will roll back.");
            }
        }

        await db.Transaction.CommitAsync(cancellationToken);
        return new OfflineReceiptOperationResult(operation.OperationId, operation.DeviceSequence, "accepted", summary, null);
    }

    private static async Task<bool> ValidateAuthorizationAsync(
        DbScope db,
        TenantContext context,
        OfflineReceiptBatchRequest batch,
        CancellationToken cancellationToken)
    {
        var grantHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(batch.GrantToken))).ToLowerInvariant();
        await using var validate = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM identity.devices AS device
                JOIN sync.device_company_scopes AS company_scope
                  ON company_scope.tenant_id = device.tenant_id
                 AND company_scope.device_id = device.id
                 AND company_scope.company_id = @companyId
                JOIN identity.company_memberships AS membership
                  ON membership.tenant_id = company_scope.tenant_id
                 AND membership.company_id = company_scope.company_id
                 AND membership.user_id = @userId
                JOIN sync.offline_grants AS offline_grant
                  ON offline_grant.tenant_id = company_scope.tenant_id
                 AND offline_grant.device_id = company_scope.device_id
                 AND offline_grant.company_id = company_scope.company_id
                 AND offline_grant.user_id = membership.user_id
                WHERE device.tenant_id = @tenantId
                  AND device.id = @deviceId
                  AND device.status = 'active'
                  AND company_scope.scope_status = 'active'
                  AND company_scope.revoked_at IS NULL
                  AND membership.status = 'active'
                  AND offline_grant.id = @grantId
                  AND offline_grant.revoked_at IS NULL
                  AND offline_grant.expires_at > now()
                  AND offline_grant.grant_signature = @grantHash
                  AND offline_grant.allowed_operations ? 'inventory.receipt.post'
                FOR SHARE OF device, company_scope, membership, offline_grant
            )
            """,
            db.Connection,
            db.Transaction);
        validate.Parameters.AddWithValue("tenantId", context.TenantId);
        validate.Parameters.AddWithValue("deviceId", batch.DeviceId);
        validate.Parameters.AddWithValue("companyId", context.CompanyId);
        validate.Parameters.AddWithValue("grantId", batch.GrantId);
        validate.Parameters.AddWithValue("userId", context.UserId);
        validate.Parameters.AddWithValue("grantHash", grantHash);
        return await validate.ExecuteScalarAsync(cancellationToken) as bool? == true;
    }

    private static async Task<bool> ValidateLocationRestrictionsAsync(
        DbScope db,
        TenantContext context,
        Guid deviceId,
        Guid grantId,
        Guid? effectiveLocationId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT company_scope.all_locations, offline_grant.allowed_location_ids::text
            FROM sync.device_company_scopes AS company_scope
            JOIN sync.offline_grants AS offline_grant
              ON offline_grant.tenant_id = company_scope.tenant_id
             AND offline_grant.device_id = company_scope.device_id
             AND offline_grant.company_id = company_scope.company_id
            WHERE company_scope.tenant_id = @tenantId
              AND company_scope.company_id = @companyId
              AND company_scope.device_id = @deviceId
              AND offline_grant.id = @grantId
            FOR SHARE OF company_scope, offline_grant
            """,
            db.Connection,
            db.Transaction);
        cmd.Parameters.AddWithValue("tenantId", context.TenantId);
        cmd.Parameters.AddWithValue("companyId", context.CompanyId);
        cmd.Parameters.AddWithValue("deviceId", deviceId);
        cmd.Parameters.AddWithValue("grantId", grantId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.CloseAsync();
            return false;
        }

        var allLocations = reader.GetBoolean(0);
        var allowedLocJson = reader.GetString(1);
        await reader.CloseAsync();

        if (!allLocations)
        {
            if (!effectiveLocationId.HasValue)
            {
                return false;
            }

            await using var locScopeCmd = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1 FROM sync.device_location_scopes
                    WHERE tenant_id = @tenantId
                      AND company_id = @companyId
                      AND device_id = @deviceId
                      AND location_id = @effectiveLocationId
                )
                """,
                db.Connection,
                db.Transaction);
            locScopeCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            locScopeCmd.Parameters.AddWithValue("companyId", context.CompanyId);
            locScopeCmd.Parameters.AddWithValue("deviceId", deviceId);
            locScopeCmd.Parameters.AddWithValue("effectiveLocationId", effectiveLocationId.Value);
            if (await locScopeCmd.ExecuteScalarAsync(cancellationToken) as bool? != true)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(allowedLocJson) && allowedLocJson.Trim() != "[]")
        {
            if (!effectiveLocationId.HasValue)
            {
                return false;
            }

            using var doc = JsonDocument.Parse(allowedLocJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var matched = false;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.TryGetGuid(out var allowedId) && allowedId == effectiveLocationId.Value)
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched) return false;
            }
        }

        return true;
    }

    private static async Task RecordDurableConflictAsync(
        DbScope db,
        TenantContext context,
        OfflineReceiptOperation operation,
        Guid deviceId,
        string conflictCode,
        Guid? conflictingOperationId,
        CancellationToken cancellationToken)
    {
        await using var insertConflict = new NpgsqlCommand(
            """
            INSERT INTO sync.operation_conflicts
                (tenant_id, company_id, operation_id, device_id, device_sequence, conflict_code, conflicting_operation_id, created_at)
            VALUES
                (@tenantId, @companyId, @operationId, @deviceId, @sequence, @conflictCode, @conflictingOpId, now())
            ON CONFLICT (tenant_id, company_id, operation_id) DO NOTHING
            """,
            db.Connection,
            db.Transaction);
        insertConflict.Parameters.AddWithValue("tenantId", context.TenantId);
        insertConflict.Parameters.AddWithValue("companyId", context.CompanyId);
        insertConflict.Parameters.AddWithValue("operationId", operation.OperationId);
        insertConflict.Parameters.AddWithValue("deviceId", deviceId);
        insertConflict.Parameters.AddWithValue("sequence", operation.DeviceSequence);
        insertConflict.Parameters.AddWithValue("conflictCode", conflictCode);
        insertConflict.Parameters.AddWithValue("conflictingOpId", (object?)conflictingOperationId ?? DBNull.Value);
        await insertConflict.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<OfflineInboxReservation> TryClaimOrInspectAsync(
        DbScope db,
        TenantContext context,
        OfflineReceiptBatchRequest batch,
        OfflineReceiptOperation operation,
        string payloadHash,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        // 0. Serialize concurrent requests from the same device via transaction-scoped advisory lock
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext(@tenantId::text || ':' || @deviceId::text))",
            db.Connection,
            db.Transaction))
        {
            lockCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            lockCmd.Parameters.AddWithValue("deviceId", batch.DeviceId);
            await lockCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var claimToken = Guid.NewGuid();

        // 1. Check if this operation already has a recorded durable conflict
        await using (var conflictCmd = new NpgsqlCommand(
            """
            SELECT conflict_code FROM sync.operation_conflicts
            WHERE tenant_id = @tenantId AND company_id = @companyId AND operation_id = @operationId
            """,
            db.Connection,
            db.Transaction))
        {
            conflictCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            conflictCmd.Parameters.AddWithValue("companyId", context.CompanyId);
            conflictCmd.Parameters.AddWithValue("operationId", operation.OperationId);
            var existingConflictCode = await conflictCmd.ExecuteScalarAsync(cancellationToken) as string;
            if (existingConflictCode is not null)
            {
                return new OfflineInboxReservation("conflict", new OfflineReceiptOperationResult(
                    operation.OperationId, operation.DeviceSequence, "conflict", null, existingConflictCode));
            }
        }

        // 2. Check if device_sequence is already taken by another operation on this device
        await using (var seqCheck = new NpgsqlCommand(
            """
            SELECT operation_id FROM sync.operation_inbox
            WHERE tenant_id = @tenantId AND device_id = @deviceId AND device_sequence = @sequence
              AND operation_id <> @operationId
            """,
            db.Connection,
            db.Transaction))
        {
            seqCheck.Parameters.AddWithValue("tenantId", context.TenantId);
            seqCheck.Parameters.AddWithValue("deviceId", batch.DeviceId);
            seqCheck.Parameters.AddWithValue("sequence", operation.DeviceSequence);
            seqCheck.Parameters.AddWithValue("operationId", operation.OperationId);
            var conflictingOp = await seqCheck.ExecuteScalarAsync(cancellationToken);
            if (conflictingOp is Guid conflictingOpId)
            {
                await RecordDurableConflictAsync(
                    db, context, operation, batch.DeviceId, "DEVICE_SEQUENCE_REUSED", conflictingOpId, cancellationToken);

                return new OfflineInboxReservation("conflict", new OfflineReceiptOperationResult(
                    operation.OperationId, operation.DeviceSequence, "conflict", null, "DEVICE_SEQUENCE_REUSED"));
            }
        }

        // 3. Inspect existing row by operation_id under FOR UPDATE lock
        await using (var existingCmd = new NpgsqlCommand(
            """
            SELECT device_id, device_sequence, operation_type, payload_hash, outcome,
                   response_payload::text, claim_token, claim_expires_at
            FROM sync.operation_inbox
            WHERE tenant_id = @tenantId AND company_id = @companyId AND operation_id = @operationId
            FOR UPDATE
            """,
            db.Connection,
            db.Transaction))
        {
            existingCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            existingCmd.Parameters.AddWithValue("companyId", context.CompanyId);
            existingCmd.Parameters.AddWithValue("operationId", operation.OperationId);
            await using var reader = await existingCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var existingDeviceId = reader.GetGuid(0);
                var existingSequence = reader.GetInt64(1);
                var existingOpType = reader.GetString(2);
                var existingHash = reader.GetString(3);
                var existingOutcome = reader.GetString(4);
                var existingResponse = reader.GetString(5);
                var activeClaimToken = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6);
                var claimExpiresAt = reader.IsDBNull(7) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(7);
                await reader.CloseAsync();

                var matches = existingDeviceId == batch.DeviceId &&
                              existingSequence == operation.DeviceSequence &&
                              existingOpType == operation.OperationType &&
                              string.Equals(existingHash.Trim(), payloadHash.Trim(), StringComparison.OrdinalIgnoreCase);

                if (!matches)
                {
                    return new OfflineInboxReservation("conflict", new OfflineReceiptOperationResult(
                        operation.OperationId, operation.DeviceSequence, "conflict", null, "OPERATION_ID_REUSED"));
                }

                // Terminal states cannot be changed back to processing
                if (existingOutcome is "accepted" or "duplicate" or "conflict" or "rejected")
                {
                    var terminalResult = ToDuplicateResult(operation, new InboxRow(
                        existingDeviceId, existingSequence, existingOpType, existingHash, existingOutcome, existingResponse));
                    return new OfflineInboxReservation("replay", terminalResult);
                }

                // Outcome is 'deferred': Check lease status
                if (claimExpiresAt.HasValue && claimExpiresAt.Value > DateTimeOffset.UtcNow)
                {
                    // Active lease held by another worker
                    return new OfflineInboxReservation("deferred", new OfflineReceiptOperationResult(
                        operation.OperationId, operation.DeviceSequence, "deferred", null, "OPERATION_IN_PROGRESS"));
                }

                // Lease expired: Crash recovery! Re-claim the lease
                await using var reclaimCmd = new NpgsqlCommand(
                    """
                    UPDATE sync.operation_inbox
                    SET claim_token = @claimToken,
                        claimed_at = now(),
                        claim_expires_at = now() + @leaseDuration
                    WHERE tenant_id = @tenantId AND company_id = @companyId AND operation_id = @operationId
                      AND outcome = 'deferred'
                    """,
                    db.Connection,
                    db.Transaction);
                reclaimCmd.Parameters.AddWithValue("tenantId", context.TenantId);
                reclaimCmd.Parameters.AddWithValue("companyId", context.CompanyId);
                reclaimCmd.Parameters.AddWithValue("operationId", operation.OperationId);
                reclaimCmd.Parameters.AddWithValue("claimToken", claimToken);
                reclaimCmd.Parameters.AddWithValue("leaseDuration", leaseDuration);
                await reclaimCmd.ExecuteNonQueryAsync(cancellationToken);

                return new OfflineInboxReservation("process", null, claimToken);
            }
            await reader.CloseAsync();
        }

        // 4. New operation: Insert initial claim with savepoint to protect transaction health
        await using (var spCmd = new NpgsqlCommand("SAVEPOINT sp_inbox_claim", db.Connection, db.Transaction))
        {
            await spCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var insertCmd = new NpgsqlCommand(
                """
                INSERT INTO sync.operation_inbox
                    (tenant_id, company_id, operation_id, batch_id, device_id, grant_id,
                     device_sequence, operation_type, schema_version, aggregate_type,
                     client_created_at, payload_hash, outcome, claim_token, claimed_at, claim_expires_at)
                VALUES
                    (@tenantId, @companyId, @operationId, @batchId, @deviceId, @grantId,
                     @sequence, @operationType, 1, 'inventory_movement',
                     @createdAt, @payloadHash, 'deferred', @claimToken, now(), now() + @leaseDuration)
                ON CONFLICT (tenant_id, company_id, operation_id) DO NOTHING
                RETURNING operation_id
                """,
                db.Connection,
                db.Transaction);
            insertCmd.Parameters.AddWithValue("tenantId", context.TenantId);
            insertCmd.Parameters.AddWithValue("companyId", context.CompanyId);
            insertCmd.Parameters.AddWithValue("operationId", operation.OperationId);
            insertCmd.Parameters.AddWithValue("batchId", batch.BatchId);
            insertCmd.Parameters.AddWithValue("deviceId", batch.DeviceId);
            insertCmd.Parameters.AddWithValue("grantId", batch.GrantId);
            insertCmd.Parameters.AddWithValue("sequence", operation.DeviceSequence);
            insertCmd.Parameters.AddWithValue("operationType", operation.OperationType);
            insertCmd.Parameters.AddWithValue("createdAt", operation.CreatedAt);
            insertCmd.Parameters.AddWithValue("payloadHash", payloadHash);
            insertCmd.Parameters.AddWithValue("claimToken", claimToken);
            insertCmd.Parameters.AddWithValue("leaseDuration", leaseDuration);

            if (await insertCmd.ExecuteScalarAsync(cancellationToken) is Guid)
            {
                await using var relCmd = new NpgsqlCommand("RELEASE SAVEPOINT sp_inbox_claim", db.Connection, db.Transaction);
                await relCmd.ExecuteNonQueryAsync(cancellationToken);
                return new OfflineInboxReservation("process", null, claimToken);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await using var rbCmd = new NpgsqlCommand("ROLLBACK TO SAVEPOINT sp_inbox_claim", db.Connection, db.Transaction);
            await rbCmd.ExecuteNonQueryAsync(cancellationToken);

            await using var findSeq = new NpgsqlCommand(
                """
                SELECT operation_id FROM sync.operation_inbox
                WHERE tenant_id = @tenantId AND device_id = @deviceId AND device_sequence = @sequence
                """,
                db.Connection,
                db.Transaction);
            findSeq.Parameters.AddWithValue("tenantId", context.TenantId);
            findSeq.Parameters.AddWithValue("deviceId", batch.DeviceId);
            findSeq.Parameters.AddWithValue("sequence", operation.DeviceSequence);
            var conflictingOp = await findSeq.ExecuteScalarAsync(cancellationToken);
            if (conflictingOp is Guid conflictingOpId && conflictingOpId != operation.OperationId)
            {
                await RecordDurableConflictAsync(
                    db, context, operation, batch.DeviceId, "DEVICE_SEQUENCE_REUSED", conflictingOpId, cancellationToken);

                return new OfflineInboxReservation("conflict", new OfflineReceiptOperationResult(
                    operation.OperationId, operation.DeviceSequence, "conflict", null, "DEVICE_SEQUENCE_REUSED"));
            }
        }

        // 5. If INSERT ... ON CONFLICT did nothing, recursively inspect under lock
        return await TryClaimOrInspectAsync(db, context, batch, operation, payloadHash, leaseDuration, cancellationToken);
    }

    private static async Task<OfflineReceiptOperationResult> RecordTerminalOutcomeAsync(
        DbScope db,
        TenantContext context,
        OfflineReceiptOperation operation,
        Guid? claimToken,
        string outcome,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var responsePayload = JsonSerializer.Serialize(new { receipt = (InventoryReceiptSummary?)null, code = errorCode }, JsonOptions);
        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE sync.operation_inbox
            SET outcome = @outcome,
                response_payload = @response::jsonb,
                processed_at = now(),
                claim_token = NULL,
                claim_expires_at = NULL
            WHERE tenant_id = @tenantId AND company_id = @companyId AND operation_id = @operationId
              AND outcome = 'deferred'
              AND (claim_token = @claimToken OR (@claimToken IS NULL AND claim_token IS NULL))
            """,
            db.Connection,
            db.Transaction);
        updateCmd.Parameters.AddWithValue("tenantId", context.TenantId);
        updateCmd.Parameters.AddWithValue("companyId", context.CompanyId);
        updateCmd.Parameters.AddWithValue("operationId", operation.OperationId);
        updateCmd.Parameters.AddWithValue("claimToken", (object?)claimToken ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("outcome", outcome);
        updateCmd.Parameters.AddWithValue("response", responsePayload);
        var affected = await updateCmd.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Failed to record terminal {outcome} for operation {operation.OperationId}: expected 1 updated row for claim {claimToken}, got {affected}. Claim token mismatch or outcome changed; transaction will roll back.");
        }
        await db.Transaction.CommitAsync(cancellationToken);

        return new OfflineReceiptOperationResult(
            operation.OperationId, operation.DeviceSequence, outcome, null, errorCode);
    }

    private static Task<OfflineReceiptOperationResult> RecordTerminalRejectionAsync(
        DbScope db,
        TenantContext context,
        OfflineReceiptOperation operation,
        Guid? claimToken,
        string errorCode,
        CancellationToken cancellationToken)
    {
        return RecordTerminalOutcomeAsync(db, context, operation, claimToken, "rejected", errorCode, cancellationToken);
    }

    private static async Task<OfflineReceiptOperationResult> RecordTerminalConflictAsync(
        DbScope db,
        TenantContext context,
        OfflineReceiptOperation operation,
        Guid deviceId,
        Guid? claimToken,
        string conflictCode,
        Guid? conflictingOperationId,
        CancellationToken cancellationToken)
    {
        await RecordDurableConflictAsync(
            db, context, operation, deviceId, conflictCode, conflictingOperationId, cancellationToken);

        return await RecordTerminalOutcomeAsync(
            db, context, operation, claimToken, "conflict", conflictCode, cancellationToken);
    }

    private static OfflineReceiptOperationResult ToDuplicateResult(
        OfflineReceiptOperation operation,
        InboxRow row)
    {
        using var response = JsonDocument.Parse(row.ResponsePayload);
        var root = response.RootElement;
        InventoryReceiptSummary? receipt = null;
        if (root.TryGetProperty("receipt", out var receiptElement) && receiptElement.ValueKind == JsonValueKind.Object)
        {
            receipt = receiptElement.Deserialize<InventoryReceiptSummary>(JsonOptions);
        }
        var code = root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String
            ? codeElement.GetString()
            : null;
        var status = row.Outcome == "accepted" ? "duplicate" : row.Outcome;
        return new OfflineReceiptOperationResult(operation.OperationId, operation.DeviceSequence, status, receipt, code);
    }

    private async Task<DbScope> OpenScopeAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @tenantId, true), set_config('app.company_id', @companyId, true)",
                connection,
                transaction);
            scope.Parameters.AddWithValue("tenantId", tenantId.ToString());
            scope.Parameters.AddWithValue("companyId", companyId.ToString());
            await scope.ExecuteNonQueryAsync(cancellationToken);
            return new DbScope(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed record InboxRow(
        Guid DeviceId,
        long DeviceSequence,
        string OperationType,
        string PayloadHash,
        string Outcome,
        string ResponsePayload);

    private sealed class DbScope(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public NpgsqlConnection Connection { get; } = connection;
        public NpgsqlTransaction Transaction { get; } = transaction;

        public async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
