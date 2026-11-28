using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using UniversalErp.Application.Inventory;
using UniversalErp.Application.Sync;
using UniversalErp.Domain.Inventory;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Sync;

namespace UniversalErp.Infrastructure.Inventory;

public sealed class PostgresProductRepository(NpgsqlDataSource dataSource) : IProductRepository
{
    private static readonly JsonSerializerOptions SyncJsonOptions = new(JsonSerializerDefaults.Web);

    private const string ProductColumns = """
        tenant_id, product_id, product_code, name, description,
        product_type, is_active, created_at, updated_at
        """;

    public async Task<IReadOnlyList<Product>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenantId, null, cancellationToken);

        await using var command = new NpgsqlCommand(
            $"SELECT {ProductColumns} FROM erp_inventory.product WHERE tenant_id = @tenantId ORDER BY product_code",
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);

        var products = new List<Product>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(ReadProduct(reader));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return products;
    }

    public async Task<Product?> GetByIdAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenantId, null, cancellationToken);

        await using var command = new NpgsqlCommand(
            $"SELECT {ProductColumns} FROM erp_inventory.product WHERE tenant_id = @tenantId AND product_id = @productId",
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("productId", productId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var product = await reader.ReadAsync(cancellationToken) ? ReadProduct(reader) : null;
        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return product;
    }

    public async Task<Product> CreateAsync(
        Guid tenantId,
        Guid companyId,
        Guid idempotencyKey,
        string requestHash,
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        await using (var reserveKey = new NpgsqlCommand(
            """
            INSERT INTO erp_inventory.command_idempotency
                (tenant_id, idempotency_key, command_code, request_hash)
            VALUES (@tenantId, @idempotencyKey, 'product.create', @requestHash)
            ON CONFLICT (tenant_id, idempotency_key) DO NOTHING
            RETURNING idempotency_key
            """,
            connection,
            transaction))
        {
            reserveKey.Parameters.AddWithValue("tenantId", tenantId);
            reserveKey.Parameters.AddWithValue("idempotencyKey", idempotencyKey);
            reserveKey.Parameters.AddWithValue("requestHash", requestHash);

            var reservedKey = await reserveKey.ExecuteScalarAsync(cancellationToken);
            if (reservedKey is null or DBNull)
            {
                return await ReplayExistingAsync(
                    connection,
                    transaction,
                    tenantId,
                    idempotencyKey,
                    requestHash,
                    cancellationToken);
            }
        }

        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO erp_inventory.product
                (tenant_id, product_id, product_code, name, description, product_type)
            VALUES
                (@tenantId, @productId, @productCode, @name, @description, @productType)
            RETURNING {ProductColumns}
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("productId", Guid.NewGuid());
        command.Parameters.AddWithValue("productCode", request.ProductCode!);
        command.Parameters.AddWithValue("name", request.Name!);
        command.Parameters.AddWithValue("description", (object?)request.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("productType", request.ProductType!);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("PostgreSQL did not return the created product.");
            }

            var product = ReadProduct(reader);
            await reader.CloseAsync();

            var changePayload = JsonSerializer.Serialize(new
            {
                tenantId = product.TenantId,
                productId = product.ProductId,
                productCode = product.ProductCode,
                name = product.Name,
                description = product.Description,
                productType = product.ProductType,
                isActive = product.IsActive,
                createdAt = product.CreatedAt,
                updatedAt = product.UpdatedAt,
            });
            await CompanyChangeLogWriter.AppendTenantWideAsync(
                connection,
                transaction,
                tenantId,
                companyId,
                "product",
                product.ProductId,
                1,
                "upsert",
                changePayload,
                cancellationToken);

            await using var saveResult = new NpgsqlCommand(
                "UPDATE erp_inventory.command_idempotency SET resource_id = @productId WHERE tenant_id = @tenantId AND idempotency_key = @idempotencyKey",
                connection,
                transaction);
            saveResult.Parameters.AddWithValue("tenantId", tenantId);
            saveResult.Parameters.AddWithValue("idempotencyKey", idempotencyKey);
            saveResult.Parameters.AddWithValue("productId", product.ProductId);
            await saveResult.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return product;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ProductCodeConflictException();
        }
    }

    public async Task<ProductSyncOperationResult> CreateOfflineAsync(
        TenantContext context,
        ProductSyncBatchRequest batch,
        ProductSyncOperation operation,
        CreateProductRequest? normalizedRequest,
        string requestHash,
        string? validationError,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, context.TenantId, context.CompanyId, cancellationToken);

        if (!await ValidateProductGrantAsync(connection, transaction, context, batch, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new ProductSyncOperationResult(
                operation.OperationId, operation.DeviceSequence, "unauthorized", null, "OFFLINE_DEVICE_OR_GRANT_INVALID");
        }

        await using (var operationLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('offline_product_operation:' || @tenantId::text || ':' || @companyId::text || ':' || @operationId::text))",
            connection,
            transaction))
        {
            operationLock.Parameters.AddWithValue("tenantId", context.TenantId);
            operationLock.Parameters.AddWithValue("companyId", context.CompanyId);
            operationLock.Parameters.AddWithValue("operationId", operation.OperationId);
            await operationLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var existingInbox = new NpgsqlCommand(
            """
            SELECT device_id, device_sequence, operation_type, payload_hash, outcome, response_payload::text
            FROM sync.operation_inbox
            WHERE tenant_id = @tenantId AND company_id = @companyId AND operation_id = @operationId
            FOR UPDATE
            """,
            connection,
            transaction))
        {
            existingInbox.Parameters.AddWithValue("tenantId", context.TenantId);
            existingInbox.Parameters.AddWithValue("companyId", context.CompanyId);
            existingInbox.Parameters.AddWithValue("operationId", operation.OperationId);
            await using var reader = await existingInbox.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var existingDeviceId = reader.GetGuid(0);
                var existingSequence = reader.GetInt64(1);
                var existingOperationType = reader.GetString(2);
                var existingHash = reader.GetString(3);
                var existingOutcome = reader.GetString(4);
                var responseJson = reader.GetString(5);
                await reader.CloseAsync();

                var matches = existingDeviceId == batch.DeviceId &&
                              existingSequence == operation.DeviceSequence &&
                              existingOperationType == (operation.OperationType ?? string.Empty) &&
                              string.Equals(existingHash, requestHash, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    await RecordProductSyncConflictAsync(
                        connection, transaction, context, operation, batch.DeviceId,
                        "OPERATION_ID_REUSED", null, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new ProductSyncOperationResult(
                        operation.OperationId, operation.DeviceSequence, "conflict", null, "OPERATION_ID_REUSED");
                }

                if (existingOutcome is "accepted" or "duplicate" or "conflict" or "rejected")
                {
                    var replay = JsonSerializer.Deserialize<ProductSyncOperationResult>(responseJson, SyncJsonOptions);
                    await transaction.CommitAsync(cancellationToken);
                    return replay ?? new ProductSyncOperationResult(
                        operation.OperationId, operation.DeviceSequence, "rejected", null, "OFFLINE_SYNC_RECEIPT_INVALID");
                }

                await transaction.CommitAsync(cancellationToken);
                return new ProductSyncOperationResult(
                    operation.OperationId, operation.DeviceSequence, "deferred", null, "OPERATION_IN_PROGRESS");
            }
            await reader.CloseAsync();
        }

        await using (var sequenceLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('offline_device_sequence:' || @tenantId::text || ':' || @deviceId::text || ':' || @sequence::text))",
            connection,
            transaction))
        {
            sequenceLock.Parameters.AddWithValue("tenantId", context.TenantId);
            sequenceLock.Parameters.AddWithValue("deviceId", batch.DeviceId);
            sequenceLock.Parameters.AddWithValue("sequence", operation.DeviceSequence);
            await sequenceLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var sequenceCheck = new NpgsqlCommand(
            """
            SELECT operation_id
            FROM sync.operation_inbox
            WHERE tenant_id = @tenantId AND device_id = @deviceId AND device_sequence = @sequence
            FOR UPDATE
            """,
            connection,
            transaction))
        {
            sequenceCheck.Parameters.AddWithValue("tenantId", context.TenantId);
            sequenceCheck.Parameters.AddWithValue("deviceId", batch.DeviceId);
            sequenceCheck.Parameters.AddWithValue("sequence", operation.DeviceSequence);
            var conflictingOperation = await sequenceCheck.ExecuteScalarAsync(cancellationToken);
            if (conflictingOperation is Guid conflictingId && conflictingId != operation.OperationId)
            {
                await RecordProductSyncConflictAsync(
                    connection, transaction, context, operation, batch.DeviceId,
                    "DEVICE_SEQUENCE_REUSED", conflictingId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new ProductSyncOperationResult(
                    operation.OperationId, operation.DeviceSequence, "conflict", null, "DEVICE_SEQUENCE_REUSED");
            }
        }

        await using (var reserveInbox = new NpgsqlCommand(
            """
            INSERT INTO sync.operation_inbox
                (tenant_id, company_id, operation_id, batch_id, device_id, grant_id,
                 device_sequence, operation_type, schema_version, aggregate_type,
                 client_created_at, payload_hash, outcome)
            VALUES
                (@tenantId, @companyId, @operationId, @batchId, @deviceId, @grantId,
                 @sequence, @operationType, 1, 'product',
                 @createdAt, @payloadHash, 'deferred')
            """,
            connection,
            transaction))
        {
            reserveInbox.Parameters.AddWithValue("tenantId", context.TenantId);
            reserveInbox.Parameters.AddWithValue("companyId", context.CompanyId);
            reserveInbox.Parameters.AddWithValue("operationId", operation.OperationId);
            reserveInbox.Parameters.AddWithValue("batchId", batch.BatchId);
            reserveInbox.Parameters.AddWithValue("deviceId", batch.DeviceId);
            reserveInbox.Parameters.AddWithValue("grantId", batch.GrantId);
            reserveInbox.Parameters.AddWithValue("sequence", operation.DeviceSequence);
            reserveInbox.Parameters.AddWithValue("operationType", operation.OperationType ?? string.Empty);
            reserveInbox.Parameters.AddWithValue("createdAt", operation.CreatedAt);
            reserveInbox.Parameters.AddWithValue("payloadHash", requestHash);
            await reserveInbox.ExecuteNonQueryAsync(cancellationToken);
        }

        if (validationError is not null || normalizedRequest is null)
        {
            var rejected = new ProductSyncOperationResult(
                operation.OperationId, operation.DeviceSequence, "rejected", null,
                validationError ?? "INVALID_PRODUCT_SYNC_OPERATION");
            await FinishProductInboxAsync(connection, transaction, context, rejected, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return rejected;
        }

        await using (var savepoint = new NpgsqlCommand("SAVEPOINT sp_offline_product_create", connection, transaction))
        {
            await savepoint.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var idempotencyLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('cmd_idemp:' || @tenantId::text || ':' || @key::text))",
            connection,
            transaction))
        {
            idempotencyLock.Parameters.AddWithValue("tenantId", context.TenantId);
            idempotencyLock.Parameters.AddWithValue("key", operation.OperationId);
            await idempotencyLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var existingKey = new NpgsqlCommand(
            """
            SELECT command_code, request_hash, resource_id, company_id
            FROM erp_inventory.command_idempotency
            WHERE tenant_id = @tenantId AND idempotency_key = @key
            FOR UPDATE
            """,
            connection,
            transaction))
        {
            existingKey.Parameters.AddWithValue("tenantId", context.TenantId);
            existingKey.Parameters.AddWithValue("key", operation.OperationId);
            await using var reader = await existingKey.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var commandCode = reader.GetString(0);
                var existingHash = reader.GetString(1).Trim();
                var resourceId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
                var existingCompanyId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
                await reader.CloseAsync();

                if (commandCode != "product.create" ||
                    !string.Equals(existingHash, requestHash, StringComparison.OrdinalIgnoreCase) ||
                    existingCompanyId != context.CompanyId)
                {
                    var conflict = new ProductSyncOperationResult(
                        operation.OperationId, operation.DeviceSequence, "conflict", null, "IDEMPOTENCY_KEY_REUSED");
                    await FinishProductInboxAsync(connection, transaction, context, conflict, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return conflict;
                }

                if (resourceId is null)
                {
                    var incomplete = new ProductSyncOperationResult(
                        operation.OperationId, operation.DeviceSequence, "rejected", null, "IDEMPOTENCY_RESULT_MISSING");
                    await FinishProductInboxAsync(connection, transaction, context, incomplete, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return incomplete;
                }

                var replayProduct = await ReadProductByIdAsync(
                    connection, transaction, context.TenantId, resourceId.Value, cancellationToken);
                if (replayProduct is null)
                {
                    var missing = new ProductSyncOperationResult(
                        operation.OperationId, operation.DeviceSequence, "rejected", null, "PRODUCT_RESULT_NOT_FOUND");
                    await FinishProductInboxAsync(connection, transaction, context, missing, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return missing;
                }

                var duplicate = new ProductSyncOperationResult(
                    operation.OperationId, operation.DeviceSequence, "accepted", replayProduct, null);
                await FinishProductInboxAsync(connection, transaction, context, duplicate, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return duplicate;
            }
            await reader.CloseAsync();
        }

        try
        {
            await using (var reserveKey = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.command_idempotency
                    (tenant_id, company_id, idempotency_key, command_code, request_hash)
                VALUES (@tenantId, @companyId, @idempotencyKey, 'product.create', @requestHash)
                """,
                connection,
                transaction))
            {
                reserveKey.Parameters.AddWithValue("tenantId", context.TenantId);
                reserveKey.Parameters.AddWithValue("companyId", context.CompanyId);
                reserveKey.Parameters.AddWithValue("idempotencyKey", operation.OperationId);
                reserveKey.Parameters.AddWithValue("requestHash", requestHash);
                await reserveKey.ExecuteNonQueryAsync(cancellationToken);
            }

            Product product;
            await using (var command = new NpgsqlCommand(
                $"""
                INSERT INTO erp_inventory.product
                    (tenant_id, product_id, product_code, name, description, product_type)
                VALUES
                    (@tenantId, @productId, @productCode, @name, @description, @productType)
                RETURNING {ProductColumns}
                """,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("tenantId", context.TenantId);
                command.Parameters.AddWithValue("productId", Guid.NewGuid());
                command.Parameters.AddWithValue("productCode", normalizedRequest.ProductCode!);
                command.Parameters.AddWithValue("name", normalizedRequest.Name!);
                command.Parameters.AddWithValue("description", (object?)normalizedRequest.Description ?? DBNull.Value);
                command.Parameters.AddWithValue("productType", normalizedRequest.ProductType!);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("PostgreSQL did not return the offline-created product.");
                }
                product = ReadProduct(reader);
                await reader.CloseAsync();
            }

            var changePayload = JsonSerializer.Serialize(new
            {
                tenantId = product.TenantId,
                productId = product.ProductId,
                productCode = product.ProductCode,
                name = product.Name,
                description = product.Description,
                productType = product.ProductType,
                isActive = product.IsActive,
                createdAt = product.CreatedAt,
                updatedAt = product.UpdatedAt,
            });
            await CompanyChangeLogWriter.AppendTenantWideAsync(
                connection,
                transaction,
                context.TenantId,
                context.CompanyId,
                "product",
                product.ProductId,
                1,
                "upsert",
                changePayload,
                cancellationToken);

            await SetTenantScopeAsync(connection, transaction, context.TenantId, context.CompanyId, cancellationToken);
            await using (var saveResult = new NpgsqlCommand(
                """
                UPDATE erp_inventory.command_idempotency
                SET resource_id = @productId
                WHERE tenant_id = @tenantId AND company_id = @companyId AND idempotency_key = @idempotencyKey
                """,
                connection,
                transaction))
            {
                saveResult.Parameters.AddWithValue("tenantId", context.TenantId);
                saveResult.Parameters.AddWithValue("companyId", context.CompanyId);
                saveResult.Parameters.AddWithValue("productId", product.ProductId);
                saveResult.Parameters.AddWithValue("idempotencyKey", operation.OperationId);
                await saveResult.ExecuteNonQueryAsync(cancellationToken);
            }

            var accepted = new ProductSyncOperationResult(
                operation.OperationId, operation.DeviceSequence, "accepted", product, null);
            await FinishProductInboxAsync(connection, transaction, context, accepted, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return accepted;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await using (var rollbackSavepoint = new NpgsqlCommand(
                "ROLLBACK TO SAVEPOINT sp_offline_product_create", connection, transaction))
            {
                await rollbackSavepoint.ExecuteNonQueryAsync(cancellationToken);
            }

            var conflictCode = "DUPLICATE_PRODUCT_CODE";
            var conflict = new ProductSyncOperationResult(
                operation.OperationId, operation.DeviceSequence, "conflict", null, conflictCode);
            await FinishProductInboxAsync(connection, transaction, context, conflict, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return conflict;
        }
    }

    private static async Task<bool> ValidateProductGrantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TenantContext context,
        ProductSyncBatchRequest batch,
        CancellationToken cancellationToken)
    {
        var grantHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(batch.GrantToken))).ToLowerInvariant();
        await using var command = new NpgsqlCommand(
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
                  AND offline_grant.allowed_operations ? 'inventory.product.create'
                FOR SHARE OF device, company_scope, membership, offline_grant
            )
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", context.TenantId);
        command.Parameters.AddWithValue("deviceId", batch.DeviceId);
        command.Parameters.AddWithValue("companyId", context.CompanyId);
        command.Parameters.AddWithValue("grantId", batch.GrantId);
        command.Parameters.AddWithValue("userId", context.UserId);
        command.Parameters.AddWithValue("grantHash", grantHash);
        return await command.ExecuteScalarAsync(cancellationToken) as bool? == true;
    }

    private static async Task<Product?> ReadProductByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {ProductColumns} FROM erp_inventory.product WHERE tenant_id = @tenantId AND product_id = @productId",
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("productId", productId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var product = await reader.ReadAsync(cancellationToken) ? ReadProduct(reader) : null;
        await reader.CloseAsync();
        return product;
    }

    private static async Task FinishProductInboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TenantContext context,
        ProductSyncOperationResult result,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.Serialize(result, SyncJsonOptions);
        await using var command = new NpgsqlCommand(
            """
            UPDATE sync.operation_inbox
            SET outcome = @outcome,
                server_record_type = CASE WHEN @recordId IS NULL THEN NULL ELSE 'product' END,
                server_record_id = @recordId,
                response_payload = @response::jsonb,
                processed_at = now()
            WHERE tenant_id = @tenantId AND company_id = @companyId
              AND operation_id = @operationId AND outcome = 'deferred'
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("outcome", result.Status);
        command.Parameters.AddWithValue("recordId", (object?)result.Product?.ProductId ?? DBNull.Value);
        command.Parameters.AddWithValue("response", response);
        command.Parameters.AddWithValue("tenantId", context.TenantId);
        command.Parameters.AddWithValue("companyId", context.CompanyId);
        command.Parameters.AddWithValue("operationId", result.OperationId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Could not persist product sync receipt for operation {result.OperationId}; expected one inbox row, got {affected}.");
        }
    }

    private static async Task RecordProductSyncConflictAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TenantContext context,
        ProductSyncOperation operation,
        Guid deviceId,
        string conflictCode,
        Guid? conflictingOperationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO sync.operation_conflicts
                (tenant_id, company_id, operation_id, device_id, device_sequence, conflict_code, conflicting_operation_id)
            VALUES
                (@tenantId, @companyId, @operationId, @deviceId, @sequence, @conflictCode, @conflictingOperationId)
            ON CONFLICT (tenant_id, company_id, operation_id) DO NOTHING
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", context.TenantId);
        command.Parameters.AddWithValue("companyId", context.CompanyId);
        command.Parameters.AddWithValue("operationId", operation.OperationId);
        command.Parameters.AddWithValue("deviceId", deviceId);
        command.Parameters.AddWithValue("sequence", operation.DeviceSequence);
        command.Parameters.AddWithValue("conflictCode", conflictCode);
        command.Parameters.AddWithValue("conflictingOperationId", (object?)conflictingOperationId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Product> ReplayExistingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        await using var findKey = new NpgsqlCommand(
            "SELECT command_code, request_hash, resource_id FROM erp_inventory.command_idempotency WHERE tenant_id = @tenantId AND idempotency_key = @idempotencyKey FOR UPDATE",
            connection,
            transaction);
        findKey.Parameters.AddWithValue("tenantId", tenantId);
        findKey.Parameters.AddWithValue("idempotencyKey", idempotencyKey);

        await using var reader = await findKey.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The idempotency record disappeared during the transaction.");
        }

        var commandCode = reader.GetString(0);
        var savedHash = reader.GetString(1).Trim();
        var resourceId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
        await reader.CloseAsync();

        if (commandCode != "product.create" || !string.Equals(savedHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyKeyConflictException();
        }
        if (resourceId is null)
        {
            throw new InvalidOperationException("The original product command has no result.");
        }

        await using var findProduct = new NpgsqlCommand(
            $"SELECT {ProductColumns} FROM erp_inventory.product WHERE tenant_id = @tenantId AND product_id = @productId",
            connection,
            transaction);
        findProduct.Parameters.AddWithValue("tenantId", tenantId);
        findProduct.Parameters.AddWithValue("productId", resourceId.Value);

        await using var productReader = await findProduct.ExecuteReaderAsync(cancellationToken);
        if (!await productReader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The product created by the original command no longer exists.");
        }

        var product = ReadProduct(productReader);
        await productReader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return product;
    }

    private static async Task SetTenantScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid? companyId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenantId, true), set_config('app.company_id', @companyId, true)",
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId.ToString());
        command.Parameters.AddWithValue("companyId", companyId?.ToString() ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Product ReadProduct(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetString(5),
        reader.GetBoolean(6),
        reader.GetFieldValue<DateTimeOffset>(7),
        reader.GetFieldValue<DateTimeOffset>(8));
}
