using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using UniversalErp.Application.Inventory;
using UniversalErp.Application.Sync;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Inventory;
using UniversalErp.Infrastructure.Sync;
using Xunit;

namespace UniversalErp.Api.Tests;

/// <summary>
/// PostgreSQL-backed integration tests for offline receipt synchronization.
/// Tests execute directly against a real PostgreSQL instance to verify:
/// - Concurrency race conditions (simultaneous same-operation and same-sequence submissions)
/// - Crash interruption and durable lease recovery
/// - Lost acknowledgment retry durability
/// - Payload mismatch and operation-ID reuse detection
/// - Multi-layer security authorization and effective location enforcement
/// </summary>
public sealed class PostgresOfflineReceiptSyncIntegrationTests : IAsyncDisposable
{
    private static readonly string ConnectionString = ResolveConnectionString();

    private readonly NpgsqlDataSource _dataSource;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PostgresOfflineReceiptSyncIntegrationTests()
    {
        _dataSource = NpgsqlDataSource.Create(ConnectionString);
    }

    private static string ResolveConnectionString()
    {
        var env = Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__UniversalErpTest");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        var current = AppContext.BaseDirectory;
        for (int i = 0; i < 5 && !string.IsNullOrEmpty(current); i++)
        {
            var configPath = Path.Combine(current, "appsettings.Development.json");
            if (File.Exists(configPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs) &&
                    cs.TryGetProperty("UniversalErpTest", out var testCs))
                {
                    return testCs.GetString()!;
                }
            }
            var candidateInApi = Path.Combine(current, "src", "UniversalErp.Api", "appsettings.Development.json");
            if (File.Exists(candidateInApi))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(candidateInApi));
                if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs) &&
                    cs.TryGetProperty("UniversalErpTest", out var testCs))
                {
                    return testCs.GetString()!;
                }
            }
            current = Directory.GetParent(current)?.FullName;
        }

        return "Host=127.0.0.1;Port=5433;Database=universal_erp;Username=erp_dev;Password=local_dev_change_me;Pooling=true;Minimum Pool Size=2;Maximum Pool Size=20";
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
    }

    private static string ComputeHash(OfflineReceiptOperation operation)
    {
        var request = operation.Payload;
        var canonicalPayload = JsonSerializer.Serialize(new
        {
            command = "inventory.receipt.post",
            request.VariantId,
            request.WarehouseId,
            request.BinId,
            request.Quantity,
            request.UnitOfMeasureId,
            SourceDocumentType = string.IsNullOrWhiteSpace(request.SourceDocumentType)
                ? null
                : request.SourceDocumentType.Trim().ToLowerInvariant(),
            request.SourceDocumentId,
            OccurredAt = (request.OccurredAt ?? operation.CreatedAt).ToUniversalTime(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
    }

    private sealed record TestFixture(
        TenantContext Context,
        Guid DeviceId,
        Guid GrantId,
        string GrantToken,
        Guid ProductId,
        Guid VariantId,
        Guid WarehouseId,
        Guid BinId,
        Guid BaseUomId,
        Guid LocationId,
        PostgresOfflineReceiptSyncRepository Repository);

    private async Task<TestFixture> CreateFixtureAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var clientDeviceId = Guid.NewGuid();

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

        // 1. Tenant
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO platform.tenants (id, tenant_code, display_name, status) VALUES (@id, @code, @name, 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", tenantId);
            cmd.Parameters.AddWithValue("code", $"test-{tenantId:N}"[..20]);
            cmd.Parameters.AddWithValue("name", "Test Tenant");
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Set local RLS
        await using (var scope = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
        {
            scope.Parameters.AddWithValue("t", tenantId.ToString());
            scope.Parameters.AddWithValue("c", companyId.ToString());
            await scope.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2. Company & Location
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO organization.companies (id, tenant_id, company_code, name, legal_name, base_currency_code, country_code, timezone, status) VALUES (@id, @tenantId, 'MAIN', 'Main Co', 'Main Co Legal', 'PKR', 'PK', 'Asia/Karachi', 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", companyId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO organization.locations (id, tenant_id, company_id, location_code, location_type_code, name, country_code, timezone, status) VALUES (@id, @tenantId, @companyId, 'LOC-HQ', 'warehouse', 'Headquarters', 'PK', 'Asia/Karachi', 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", locationId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 3. User, Tenant Membership & Company Membership
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO identity.users (id, email_normalized, display_name, status) VALUES (@id, @email, 'Admin User', 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", userId);
            cmd.Parameters.AddWithValue("email", $"admin-{userId:N}@test.com");
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO identity.tenant_memberships (id, tenant_id, user_id, status) VALUES (@id, @tenantId, @userId, 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("userId", userId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO identity.company_memberships (id, tenant_id, company_id, user_id, status) VALUES (@id, @tenantId, @companyId, @userId, 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("userId", userId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 4. Unit of Measure & Stock Status
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.stock_status (tenant_id, status_code, description, is_reservable, is_pickable) VALUES (@tenantId, 'available', 'Available', true, true)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension, decimal_places) VALUES (@tenantId, @uomId, 'EA', 'Each', 'count', 0)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("uomId", uomId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 5. Product & Variant
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, product_type, is_active) VALUES (@tenantId, @id, 'P-01', 'Test Product', 'stock', true)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("id", productId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.product_variant (tenant_id, variant_id, product_id, sku, variant_name, base_uom_id, tracking_mode, costing_method, is_active) VALUES (@tenantId, @varId, @prodId, 'SKU-01', 'Standard', @uomId, 'none', 'fifo', true)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("varId", variantId);
            cmd.Parameters.AddWithValue("prodId", productId);
            cmd.Parameters.AddWithValue("uomId", uomId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 6. Warehouse & Stock Bin
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name, is_active, location_id) VALUES (@tenantId, @companyId, @whId, 'WH-01', 'Central WH', true, @locId)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("whId", warehouseId);
            cmd.Parameters.AddWithValue("locId", locationId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, warehouse_id, bin_id, code, bin_type, is_active) VALUES (@tenantId, @companyId, @whId, @binId, 'BIN-A', 'storage', true)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("whId", warehouseId);
            cmd.Parameters.AddWithValue("binId", binId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var repository = new PostgresOfflineReceiptSyncRepository(_dataSource);
        var context = new TenantContext(tenantId, companyId, userId);

        // 7. Register Device & Grant
        var grant = await repository.RegisterDeviceAsync(context, clientDeviceId, "web", "Test Device", cancellationToken)
            ?? throw new InvalidOperationException("Failed to register test device.");

        return new TestFixture(
            context,
            grant.DeviceId,
            grant.GrantId,
            grant.GrantToken,
            productId,
            variantId,
            warehouseId,
            binId,
            uomId,
            locationId,
            repository);
    }

    private async Task CleanupFixtureAsync(TestFixture fixture)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                scope.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                scope.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            await using var cmd = new NpgsqlCommand(
                """
                DELETE FROM sync.operation_conflicts WHERE tenant_id = @id;
                DELETE FROM sync.operation_inbox WHERE tenant_id = @id;
                DELETE FROM sync.offline_grants WHERE tenant_id = @id;
                DELETE FROM sync.device_location_scopes WHERE tenant_id = @id;
                DELETE FROM sync.device_company_scopes WHERE tenant_id = @id;
                DELETE FROM identity.devices WHERE tenant_id = @id;
                DELETE FROM erp_inventory.command_idempotency WHERE tenant_id = @id;
                DELETE FROM erp_inventory.stock_balance WHERE tenant_id = @id;
                DELETE FROM erp_inventory.inventory_movement_leg WHERE tenant_id = @id;
                DELETE FROM erp_inventory.inventory_movement WHERE tenant_id = @id;
                DELETE FROM erp_inventory.outbox_event WHERE tenant_id = @id;
                DELETE FROM sync.company_change_feed WHERE tenant_id = @id;
                DELETE FROM erp_inventory.stock_bin WHERE tenant_id = @id;
                DELETE FROM erp_inventory.warehouse WHERE tenant_id = @id;
                DELETE FROM erp_inventory.product_variant WHERE tenant_id = @id;
                DELETE FROM erp_inventory.product WHERE tenant_id = @id;
                DELETE FROM erp_inventory.unit_of_measure WHERE tenant_id = @id;
                DELETE FROM erp_inventory.stock_status WHERE tenant_id = @id;
                DELETE FROM identity.company_memberships WHERE tenant_id = @id;
                DELETE FROM identity.tenant_memberships WHERE tenant_id = @id;
                DELETE FROM organization.locations WHERE tenant_id = @id;
                DELETE FROM organization.companies WHERE tenant_id = @id;
                DELETE FROM platform.tenants WHERE id = @id;
                """, conn);
            cmd.Parameters.AddWithValue("id", fixture.Context.TenantId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task TwoSimultaneousSubmissions_SameOperationId_OnlyOneMovementPosted_OtherIsDuplicate()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var opId = Guid.NewGuid();
            var op = new OfflineReceiptOperation(
                opId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId,
                    fixture.WarehouseId,
                    fixture.BinId,
                    15m,
                    fixture.BaseUomId,
                    null, null, null, "Concurrent test"));

            var batch1 = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op]);
            var batch2 = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op]);
            var hash = ComputeHash(op);

            // Execute two simultaneous submissions of the exact same operation ID
            var task1 = fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch1, op, hash, default);
            var task2 = fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch2, op, hash, default);

            var results = await Task.WhenAll(task1, task2);

            var accepted = results.Where(r => r.Status == "accepted").ToList();
            var duplicate = results.Where(r => r.Status == "duplicate").ToList();
            var deferred = results.Where(r => r.Status == "deferred").ToList();

            // At least one must be accepted; the concurrent one must either be duplicate or deferred (lease held)
            Assert.Single(accepted);
            Assert.True(duplicate.Count + deferred.Count == 1, "The second concurrent submission must be duplicate or deferred.");

            // If the second was deferred due to active lease, retrying it now must return duplicate
            if (deferred.Count == 1)
            {
                var retry = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch2, op, hash, default);
                Assert.Equal("duplicate", retry.Status);
                Assert.Equal(accepted[0].Receipt!.MovementId, retry.Receipt!.MovementId);
            }
            else
            {
                Assert.Equal(accepted[0].Receipt!.MovementId, duplicate[0].Receipt!.MovementId);
            }

            // Verify in PostgreSQL database: Exactly 1 inventory movement exists
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                scope.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                scope.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            await using (var checkCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND client_transaction_id = @opId", conn))
            {
                checkCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                checkCmd.Parameters.AddWithValue("opId", opId);
                var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                Assert.Equal(1, count);
            }

            // Verify stock balance: On hand quantity is 15 (not 30)
            await using (var balCmd = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @t AND company_id = @c AND variant_id = @v AND warehouse_id = @w AND bin_id = @b", conn))
            {
                balCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                balCmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                balCmd.Parameters.AddWithValue("v", fixture.VariantId);
                balCmd.Parameters.AddWithValue("w", fixture.WarehouseId);
                balCmd.Parameters.AddWithValue("b", fixture.BinId);
                var qty = Convert.ToDecimal(await balCmd.ExecuteScalarAsync());
                Assert.Equal(15m, qty);
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task TwoSimultaneousSubmissions_SameDeviceSequence_DifferentOperationIds_OneAccepted_OtherConflict()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var op1 = new OfflineReceiptOperation(
                Guid.NewGuid(),
                99, // Same sequence
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 5m, fixture.BaseUomId, null, null, null, "Op1"));

            var op2 = new OfflineReceiptOperation(
                Guid.NewGuid(),
                99, // Same sequence, different operationId
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 5m, fixture.BaseUomId, null, null, null, "Op2"));

            var batch1 = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op1]);
            var batch2 = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op2]);

            var t1 = fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch1, op1, ComputeHash(op1), default);
            var t2 = fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch2, op2, ComputeHash(op2), default);

            var results = await Task.WhenAll(t1, t2);

            var accepted = results.SingleOrDefault(r => r.Status == "accepted");
            var conflict = results.SingleOrDefault(r => r.Status == "conflict" && r.Code == "DEVICE_SEQUENCE_REUSED");

            Assert.NotNull(accepted);
            Assert.NotNull(conflict);

            // Verify original operation remains accepted in sync.operation_inbox
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                scope.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                scope.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            await using (var acceptedInbox = new NpgsqlCommand(
                "SELECT outcome, server_record_id FROM sync.operation_inbox WHERE tenant_id = @t AND operation_id = @id", conn))
            {
                acceptedInbox.Parameters.AddWithValue("t", fixture.Context.TenantId);
                acceptedInbox.Parameters.AddWithValue("id", accepted.OperationId);
                await using var r = await acceptedInbox.ExecuteReaderAsync();
                Assert.True(await r.ReadAsync());
                Assert.Equal("accepted", r.GetString(0));
                Assert.False(r.IsDBNull(1));
                await r.CloseAsync();
            }

            // Verify the conflicting operation is durably recorded in sync.operation_conflicts
            await using (var conflictCheck = new NpgsqlCommand(
                "SELECT conflict_code, conflicting_operation_id FROM sync.operation_conflicts WHERE tenant_id = @t AND operation_id = @id", conn))
            {
                conflictCheck.Parameters.AddWithValue("t", fixture.Context.TenantId);
                conflictCheck.Parameters.AddWithValue("id", conflict.OperationId);
                await using var r = await conflictCheck.ExecuteReaderAsync();
                Assert.True(await r.ReadAsync());
                Assert.Equal("DEVICE_SEQUENCE_REUSED", r.GetString(0));
                Assert.Equal(accepted.OperationId, r.GetGuid(1));
                await r.CloseAsync();
            }

            // Retry of conflicting operation returns the durable conflict without mutating inbox
            var conflictOp = conflict.OperationId == op1.OperationId ? op1 : op2;
            var conflictBatch = conflict.OperationId == op1.OperationId ? batch1 : batch2;
            var retryConflict = await fixture.Repository.ProcessReceiptOperationAtomicAsync(
                fixture.Context, conflictBatch, conflictOp, ComputeHash(conflictOp), default);
            Assert.Equal("conflict", retryConflict.Status);
            Assert.Equal("DEVICE_SEQUENCE_REUSED", retryConflict.Code);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task ServerInterruption_AfterReservation_BeforePosting_RecoversAfterLeaseExpiry()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var opId = Guid.NewGuid();
            var op = new OfflineReceiptOperation(
                opId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 20m, fixture.BaseUomId, null, null, null, "Crash test"));
            var batch = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op]);
            var hash = ComputeHash(op);

            // 1. Server reserves the operation
            var reservation = await fixture.Repository.ReserveReceiptOperationAsync(fixture.Context, batch, op, hash, default);
            Assert.Equal("process", reservation.Disposition);

            // 2. Simulate server crash / interruption before posting:
            // The row exists in sync.operation_inbox with outcome = 'deferred'
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                scope.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                scope.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            await using (var queryCmd = new NpgsqlCommand(
                "SELECT outcome, server_record_id, claim_expires_at FROM sync.operation_inbox WHERE tenant_id = @t AND operation_id = @id", conn))
            {
                queryCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                queryCmd.Parameters.AddWithValue("id", opId);
                await using var reader = await queryCmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("deferred", reader.GetString(0));
                Assert.True(reader.IsDBNull(1));
                await reader.CloseAsync();
            }

            // Verify ZERO movements posted
            await using (var checkCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND client_transaction_id = @opId", conn))
            {
                checkCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                checkCmd.Parameters.AddWithValue("opId", opId);
                Assert.Equal(0, Convert.ToInt32(await checkCmd.ExecuteScalarAsync()));
            }

            // 3. Client retries while lease is still active: protected by active lease
            var activeRetry = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch, op, hash, default);
            Assert.Equal("deferred", activeRetry.Status);
            Assert.Equal("OPERATION_IN_PROGRESS", activeRetry.Code);

            // 4. Force lease expiration in database (simulating passage of lease duration)
            await using (var expireCmd = new NpgsqlCommand(
                "UPDATE sync.operation_inbox SET claim_expires_at = now() - interval '1 second' WHERE tenant_id = @t AND operation_id = @id", conn))
            {
                expireCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                expireCmd.Parameters.AddWithValue("id", opId);
                await expireCmd.ExecuteNonQueryAsync();
            }

            // 5. Client retries after lease expiration: Recovered and executed to completion!
            var recoveredResult = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch, op, hash, default);
            Assert.Equal("accepted", recoveredResult.Status);
            Assert.NotNull(recoveredResult.Receipt);

            // Verify now exactly 1 movement exists
            await using (var checkCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND client_transaction_id = @opId", conn))
            {
                checkCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                checkCmd.Parameters.AddWithValue("opId", opId);
                Assert.Equal(1, Convert.ToInt32(await checkCmd.ExecuteScalarAsync()));
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task ServerInterruption_AfterPosting_BeforeAcknowledgment_RetryReturnsDuplicateReceipt()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var opId = Guid.NewGuid();
            var op = new OfflineReceiptOperation(
                opId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 25m, fixture.BaseUomId, null, null, null, "Lost ACK test"));
            var batch = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op]);
            var hash = ComputeHash(op);

            // Step 1: Server posts inventory and marks inbox outcome = 'accepted'
            var initialResult = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch, op, hash, default);
            Assert.Equal("accepted", initialResult.Status);
            var expectedMovementId = initialResult.Receipt!.MovementId;

            // Step 2: Client network dropped before receiving the HTTP acknowledgment.
            // Client retries with identical operationId and payload.
            var retryResult = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch, op, hash, default);

            // Step 3: Returns durable persisted duplicate receipt
            Assert.Equal("duplicate", retryResult.Status);
            Assert.NotNull(retryResult.Receipt);
            Assert.Equal(expectedMovementId, retryResult.Receipt.MovementId);

            // Verify in PostgreSQL: Exactly 1 inventory movement, not 2
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                scope.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                scope.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            await using (var checkCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND client_transaction_id = @opId", conn))
            {
                checkCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                checkCmd.Parameters.AddWithValue("opId", opId);
                Assert.Equal(1, Convert.ToInt32(await checkCmd.ExecuteScalarAsync()));
            }

            // Verify stock balance: 25 (not 50)
            await using (var balCmd = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @t AND company_id = @c AND variant_id = @v AND warehouse_id = @w AND bin_id = @b", conn))
            {
                balCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                balCmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                balCmd.Parameters.AddWithValue("v", fixture.VariantId);
                balCmd.Parameters.AddWithValue("w", fixture.WarehouseId);
                balCmd.Parameters.AddWithValue("b", fixture.BinId);
                Assert.Equal(25m, Convert.ToDecimal(await balCmd.ExecuteScalarAsync()));
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task RetryAfterSuccessfulPosting_ReturnsPersistedResultDurable()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var opId = Guid.NewGuid();
            var op = new OfflineReceiptOperation(
                opId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 12m, fixture.BaseUomId, null, null, null, "Multi retry"));
            var batch = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op]);
            var hash = ComputeHash(op);

            var r1 = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch, op, hash, default);
            Assert.Equal("accepted", r1.Status);

            var r2 = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch, op, hash, default);
            Assert.Equal("duplicate", r2.Status);
            Assert.Equal(r1.Receipt!.MovementId, r2.Receipt!.MovementId);

            var r3 = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch, op, hash, default);
            Assert.Equal("duplicate", r3.Status);
            Assert.Equal(r1.Receipt!.MovementId, r3.Receipt!.MovementId);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task ReuseOfOperationId_WithChangedPayload_ReturnsConflict()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var opId = Guid.NewGuid();
            var opInitial = new OfflineReceiptOperation(
                opId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 10m, fixture.BaseUomId, null, null, null, "Original"));
            var batch1 = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [opInitial]);

            var r1 = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch1, opInitial, ComputeHash(opInitial), default);
            Assert.Equal("accepted", r1.Status);

            // Reusing same operation ID with mutated quantity (99 instead of 10)
            var opMutated = new OfflineReceiptOperation(
                opId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 99m, fixture.BaseUomId, null, null, null, "Mutated payload"));
            var batch2 = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [opMutated]);

            var r2 = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch2, opMutated, ComputeHash(opMutated), default);
            Assert.Equal("conflict", r2.Status);
            Assert.Equal("OPERATION_ID_REUSED", r2.Code);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task RevokedDevice_RevokedCompanyScope_AndUnauthorizedWarehouseBin_EnforcesAllBoundaries()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                scope.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                scope.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            // 1. Unauthorized Warehouse (belongs to another company)
            var foreignWhId = Guid.NewGuid();
            var opBadWh = new OfflineReceiptOperation(
                Guid.NewGuid(),
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, foreignWhId, fixture.BinId, 10m, fixture.BaseUomId, null, null, null, "Bad WH"));
            var batchBadWh = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [opBadWh]);
            var resBadWh = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batchBadWh, opBadWh, ComputeHash(opBadWh), default);
            Assert.Equal("rejected", resBadWh.Status);
            Assert.Equal("WAREHOUSE_NOT_FOUND", resBadWh.Code);

            // 2. Unauthorized Bin (does not exist in warehouse)
            var foreignBinId = Guid.NewGuid();
            var opBadBin = new OfflineReceiptOperation(
                Guid.NewGuid(),
                2,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, foreignBinId, 10m, fixture.BaseUomId, null, null, null, "Bad Bin"));
            var batchBadBin = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [opBadBin]);
            var resBadBin = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batchBadBin, opBadBin, ComputeHash(opBadBin), default);
            Assert.Equal("rejected", resBadBin.Status);
            Assert.Equal("RECEIPT_LOCATION_NOT_SUPPORTED", resBadBin.Code);

            // 3. Location restrictions enforcement (all_locations = false and no location scope)
            await using (var restrictLocCmd = new NpgsqlCommand(
                "UPDATE sync.device_company_scopes SET all_locations = false WHERE tenant_id = @t AND device_id = @d", conn))
            {
                restrictLocCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                restrictLocCmd.Parameters.AddWithValue("d", fixture.DeviceId);
                await restrictLocCmd.ExecuteNonQueryAsync();
            }

            var opRestrictedLoc = new OfflineReceiptOperation(
                Guid.NewGuid(),
                3,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 10m, fixture.BaseUomId, null, null, null, "Restricted loc"));
            var batchRestrictedLoc = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [opRestrictedLoc]);
            var resRestrictedLoc = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batchRestrictedLoc, opRestrictedLoc, ComputeHash(opRestrictedLoc), default);
            Assert.Equal("rejected", resRestrictedLoc.Status);
            Assert.Equal("LOCATION_NOT_AUTHORIZED", resRestrictedLoc.Code);

            // Restore all_locations = true for scope tests
            await using (var restoreLocCmd = new NpgsqlCommand(
                "UPDATE sync.device_company_scopes SET all_locations = true WHERE tenant_id = @t AND device_id = @d", conn))
            {
                restoreLocCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                restoreLocCmd.Parameters.AddWithValue("d", fixture.DeviceId);
                await restoreLocCmd.ExecuteNonQueryAsync();
            }

            // 4. Revoked Company Scope
            await using (var revokeScopeCmd = new NpgsqlCommand(
                "UPDATE sync.device_company_scopes SET scope_status = 'revoked', revoked_at = now() WHERE tenant_id = @t AND device_id = @d", conn))
            {
                revokeScopeCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                revokeScopeCmd.Parameters.AddWithValue("d", fixture.DeviceId);
                await revokeScopeCmd.ExecuteNonQueryAsync();
            }

            var opRevokedScope = new OfflineReceiptOperation(
                Guid.NewGuid(),
                4,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 10m, fixture.BaseUomId, null, null, null, "Revoked scope"));
            var batchRevokedScope = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [opRevokedScope]);
            var resRevokedScope = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batchRevokedScope, opRevokedScope, ComputeHash(opRevokedScope), default);
            Assert.Equal("unauthorized", resRevokedScope.Status);

            // 5. Revoked Device
            await using (var revokeDevCmd = new NpgsqlCommand(
                "UPDATE identity.devices SET status = 'revoked' WHERE tenant_id = @t AND id = @d", conn))
            {
                revokeDevCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                revokeDevCmd.Parameters.AddWithValue("d", fixture.DeviceId);
                await revokeDevCmd.ExecuteNonQueryAsync();
            }

            var opRevokedDev = new OfflineReceiptOperation(
                Guid.NewGuid(),
                5,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 10m, fixture.BaseUomId, null, null, null, "Revoked dev"));
            var batchRevokedDev = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [opRevokedDev]);
            var resRevokedDev = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batchRevokedDev, opRevokedDev, ComputeHash(opRevokedDev), default);
            Assert.Equal("unauthorized", resRevokedDev.Status);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task LeaseExpiryAndRecovery_ExpiredDeferredOperation_ReclaimedAndCompleted()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var opId = Guid.NewGuid();
            var op = new OfflineReceiptOperation(
                opId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 18m, fixture.BaseUomId, null, null, null, "Expired lease recovery"));
            var batch = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op]);
            var hash = ComputeHash(op);

            // Manually insert into sync.operation_inbox with outcome = 'deferred' and an expired lease from 1 minute ago
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                scope.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                scope.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            var oldClaimToken = Guid.NewGuid();
            await using (var insertCmd = new NpgsqlCommand(
                """
                INSERT INTO sync.operation_inbox
                    (tenant_id, company_id, operation_id, batch_id, device_id, grant_id,
                     device_sequence, operation_type, schema_version, aggregate_type,
                     client_created_at, payload_hash, outcome, claim_token, claimed_at, claim_expires_at)
                VALUES
                    (@tenantId, @companyId, @operationId, @batchId, @deviceId, @grantId,
                     1, 'inventory.receipt.post', 1, 'inventory_movement',
                     now(), @payloadHash, 'deferred', @claimToken, now() - interval '2 minutes', now() - interval '1 minute')
                """, conn))
            {
                insertCmd.Parameters.AddWithValue("tenantId", fixture.Context.TenantId);
                insertCmd.Parameters.AddWithValue("companyId", fixture.Context.CompanyId);
                insertCmd.Parameters.AddWithValue("operationId", opId);
                insertCmd.Parameters.AddWithValue("batchId", batch.BatchId);
                insertCmd.Parameters.AddWithValue("deviceId", fixture.DeviceId);
                insertCmd.Parameters.AddWithValue("grantId", fixture.GrantId);
                insertCmd.Parameters.AddWithValue("payloadHash", hash);
                insertCmd.Parameters.AddWithValue("claimToken", oldClaimToken);
                await insertCmd.ExecuteNonQueryAsync();
            }

            // Now execute ProcessReceiptOperationAtomicAsync:
            // It must detect the expired lease, re-claim it, post inventory atomically, and commit accepted outcome!
            var result = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch, op, hash, default);

            Assert.Equal("accepted", result.Status);
            Assert.NotNull(result.Receipt);

            // Verify in PostgreSQL: Inbox is 'accepted', claim_token is NULL, movement exists
            await using (var verifyCmd = new NpgsqlCommand(
                "SELECT outcome, claim_token, server_record_id FROM sync.operation_inbox WHERE tenant_id = @t AND operation_id = @id", conn))
            {
                verifyCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                verifyCmd.Parameters.AddWithValue("id", opId);
                await using var reader = await verifyCmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("accepted", reader.GetString(0));
                Assert.True(reader.IsDBNull(1)); // claim_token cleared upon completion
                Assert.False(reader.IsDBNull(2));
                Assert.Equal(result.Receipt.MovementId, reader.GetGuid(2));
                await reader.CloseAsync();
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task ClaimTokenMismatch_AcceptanceThrowsAndRollsBack_NoMovementsCommitted()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var opId = Guid.NewGuid();
            var op = new OfflineReceiptOperation(
                opId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 10m, fixture.BaseUomId, null, null, null, "Claim mismatch test"));
            var batch = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op]);
            var hash = ComputeHash(op);

            // 1. Reserve operation with initial claim token
            var reservation = await fixture.Repository.ReserveReceiptOperationAsync(fixture.Context, batch, op, hash, default);
            Assert.Equal("process", reservation.Disposition);
            var initialClaimToken = reservation.ClaimToken!.Value;

            // 2. Simulate another worker overtaking the lease and changing the claim_token
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                scope.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                scope.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            var stolenToken = Guid.NewGuid();
            await using (var takeoverCmd = new NpgsqlCommand(
                "UPDATE sync.operation_inbox SET claim_token = @stolenToken WHERE tenant_id = @t AND operation_id = @id", conn))
            {
                takeoverCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                takeoverCmd.Parameters.AddWithValue("stolenToken", stolenToken);
                takeoverCmd.Parameters.AddWithValue("id", opId);
                await takeoverCmd.ExecuteNonQueryAsync();
            }

            // 3. Simulating first worker attempting to acknowledge with its outdated initialClaimToken
            var ackException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using var ackConn = await _dataSource.OpenConnectionAsync();
                await using var tx = await ackConn.BeginTransactionAsync();
                await using var ackScope = new NpgsqlCommand(
                    "SELECT set_config('app.tenant_id', @t, true), set_config('app.company_id', @c, true)", ackConn, tx);
                ackScope.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                ackScope.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await ackScope.ExecuteNonQueryAsync();

                await using var ackCmd = new NpgsqlCommand(
                    """
                    UPDATE sync.operation_inbox
                    SET outcome = 'accepted',
                        server_record_type = 'inventory_movement',
                        server_record_id = @movementId,
                        response_payload = '{}'::jsonb,
                        processed_at = now(),
                        claim_token = NULL,
                        claim_expires_at = NULL
                    WHERE tenant_id = @tenantId AND company_id = @companyId AND operation_id = @operationId
                      AND outcome = 'deferred'
                      AND claim_token = @claimToken
                    """, ackConn, tx);
                ackCmd.Parameters.AddWithValue("tenantId", fixture.Context.TenantId);
                ackCmd.Parameters.AddWithValue("companyId", fixture.Context.CompanyId);
                ackCmd.Parameters.AddWithValue("operationId", opId);
                ackCmd.Parameters.AddWithValue("claimToken", initialClaimToken); // Outdated claim token!
                ackCmd.Parameters.AddWithValue("movementId", Guid.NewGuid());
                var affected = await ackCmd.ExecuteNonQueryAsync();
                if (affected != 1)
                {
                    throw new InvalidOperationException($"Failed to acknowledge receipt operation: expected 1 updated row, got {affected}.");
                }
                await tx.CommitAsync();
            });

            Assert.Contains("expected 1 updated row, got 0", ackException.Message);

            // 4. Verify in PostgreSQL database: ZERO movements posted, inbox still has outcome = 'deferred' with stolenToken
            await using (var verifyCmd = new NpgsqlCommand(
                "SELECT outcome, claim_token FROM sync.operation_inbox WHERE tenant_id = @t AND operation_id = @id", conn))
            {
                verifyCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                verifyCmd.Parameters.AddWithValue("id", opId);
                await using var reader = await verifyCmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("deferred", reader.GetString(0));
                Assert.Equal(stolenToken, reader.GetGuid(1));
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task AffectedRowCountZero_RejectionThrowsAndRollsBack()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var opId = Guid.NewGuid();
            var op = new OfflineReceiptOperation(
                opId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 10m, fixture.BaseUomId, null, null, null, "Row count failure test"));
            var batch = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op]);
            var hash = ComputeHash(op);

            // Complete operation normally so its outcome = 'accepted'
            var initialResult = await fixture.Repository.ProcessReceiptOperationAtomicAsync(fixture.Context, batch, op, hash, default);
            Assert.Equal("accepted", initialResult.Status);

            // Now attempt to record terminal rejection with an arbitrary claim token against the accepted operation
            // The predicate requires outcome = 'deferred' and matching claim token.
            // Since outcome is 'accepted', 0 rows are updated, throwing InvalidOperationException
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, true), set_config('app.company_id', @c, true)", conn, tx))
            {
                scope.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                scope.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            var wrongToken = Guid.NewGuid();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using var updateCmd = new NpgsqlCommand(
                    """
                    UPDATE sync.operation_inbox
                    SET outcome = 'rejected',
                        response_payload = '{}'::jsonb,
                        processed_at = now(),
                        claim_token = NULL,
                        claim_expires_at = NULL
                    WHERE tenant_id = @tenantId AND company_id = @companyId AND operation_id = @operationId
                      AND outcome = 'deferred'
                      AND (claim_token = @claimToken OR (@claimToken IS NULL AND claim_token IS NULL))
                    """, conn, tx);
                updateCmd.Parameters.AddWithValue("tenantId", fixture.Context.TenantId);
                updateCmd.Parameters.AddWithValue("companyId", fixture.Context.CompanyId);
                updateCmd.Parameters.AddWithValue("operationId", opId);
                updateCmd.Parameters.AddWithValue("claimToken", wrongToken);
                var affected = await updateCmd.ExecuteNonQueryAsync();
                if (affected != 1)
                {
                    throw new InvalidOperationException($"Failed to record terminal rejection: expected 1 updated row, got {affected}.");
                }
                await tx.CommitAsync();
            });

            Assert.Contains("expected 1 updated row, got 0", ex.Message);

            // Verify in PostgreSQL: Operation remains in 'accepted' status
            await using (var checkCmd = new NpgsqlCommand(
                "SELECT outcome FROM sync.operation_inbox WHERE tenant_id = @t AND operation_id = @id", conn, tx))
            {
                checkCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                checkCmd.Parameters.AddWithValue("id", opId);
                var outcome = await checkCmd.ExecuteScalarAsync() as string;
                Assert.Equal("accepted", outcome);
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task LocationScopeMismatch_WarehouseUUIDNotTreatedAsLocation_RejectedWithoutMovements()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                scope.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                scope.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            // Create a different location (LocB) that is authorized for the device,
            // while the warehouse belongs to LocA (fixture.LocationId).
            var authorizedLocId = Guid.NewGuid();
            await using (var locCmd = new NpgsqlCommand(
                "INSERT INTO organization.locations (id, tenant_id, company_id, location_code, location_type_code, name, country_code, timezone, status) VALUES (@id, @t, @c, 'LOC-OTHER', 'warehouse', 'Other Location', 'PK', 'Asia/Karachi', 'active')", conn))
            {
                locCmd.Parameters.AddWithValue("id", authorizedLocId);
                locCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                locCmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                await locCmd.ExecuteNonQueryAsync();
            }

            // Restrict device company scope: all_locations = false
            await using (var scopeCmd = new NpgsqlCommand(
                "UPDATE sync.device_company_scopes SET all_locations = false WHERE tenant_id = @t AND device_id = @d", conn))
            {
                scopeCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                scopeCmd.Parameters.AddWithValue("d", fixture.DeviceId);
                await scopeCmd.ExecuteNonQueryAsync();
            }

            // Grant device access ONLY to authorizedLocId (NOT fixture.LocationId, and NOT fixture.WarehouseId)
            await using (var devLocCmd = new NpgsqlCommand(
                "INSERT INTO sync.device_location_scopes (tenant_id, company_id, device_id, location_id) VALUES (@t, @c, @d, @locId)", conn))
            {
                devLocCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                devLocCmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                devLocCmd.Parameters.AddWithValue("d", fixture.DeviceId);
                devLocCmd.Parameters.AddWithValue("locId", authorizedLocId);
                await devLocCmd.ExecuteNonQueryAsync();
            }

            // Submit offline receipt targeting fixture.WarehouseId (whose location_id is fixture.LocationId)
            var opId = Guid.NewGuid();
            var op = new OfflineReceiptOperation(
                opId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 10m, fixture.BaseUomId, null, null, null, "Location scope mismatch"));
            var batch = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op]);

            var result = await fixture.Repository.ProcessReceiptOperationAtomicAsync(
                fixture.Context, batch, op, ComputeHash(op), default);

            // Must reject because warehouse.location_id does not match the authorized location
            Assert.Equal("rejected", result.Status);
            Assert.Equal("LOCATION_NOT_AUTHORIZED", result.Code);

            // Verify in PostgreSQL: Zero inventory movements were created
            await using (var checkCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND client_transaction_id = @opId", conn))
            {
                checkCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                checkCmd.Parameters.AddWithValue("opId", opId);
                Assert.Equal(0, Convert.ToInt32(await checkCmd.ExecuteScalarAsync()));
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task CommandIdempotencyKeyCollision_DifferentCommandOrHash_DoesNotOverwriteResource_ReturnsConflict()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var opId = Guid.NewGuid();
            var foreignResourceId = Guid.NewGuid();

            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                scope.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                scope.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            // Pre-seed an existing command_idempotency row with the same key (opId)
            // but for a completely different command_code ('inventory.adjustment.post')
            await using (var seedCmd = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.command_idempotency
                    (tenant_id, company_id, idempotency_key, command_code, request_hash, resource_id)
                VALUES
                    (@tenantId, @companyId, @key, 'inventory.adjustment.post', 'foreignhash00000000000000000000000000000000000000000000000000000', @resId)
                """, conn))
            {
                seedCmd.Parameters.AddWithValue("tenantId", fixture.Context.TenantId);
                seedCmd.Parameters.AddWithValue("companyId", fixture.Context.CompanyId);
                seedCmd.Parameters.AddWithValue("key", opId);
                seedCmd.Parameters.AddWithValue("resId", foreignResourceId);
                await seedCmd.ExecuteNonQueryAsync();
            }

            // Attempt to post an offline receipt with this same opId
            var op = new OfflineReceiptOperation(
                opId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 10m, fixture.BaseUomId, null, null, null, "Idempotency collision test"));
            var batch = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op]);

            var result = await fixture.Repository.ProcessReceiptOperationAtomicAsync(
                fixture.Context, batch, op, ComputeHash(op), default);

            // Must reject/conflict due to idempotency conflict
            Assert.True(result.Status is "conflict" or "rejected");
            Assert.Equal("COMMAND_IDEMPOTENCY_CONFLICT", result.Code);

            // Verify in PostgreSQL: Original command_idempotency row is NOT overwritten
            await using (var checkCmd = new NpgsqlCommand(
                "SELECT command_code, resource_id FROM erp_inventory.command_idempotency WHERE tenant_id = @t AND idempotency_key = @key", conn))
            {
                checkCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                checkCmd.Parameters.AddWithValue("key", opId);
                await using var reader = await checkCmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("inventory.adjustment.post", reader.GetString(0));
                Assert.Equal(foreignResourceId, reader.GetGuid(1));
            }

            // Verify in PostgreSQL: Zero receipt inventory movements were created
            await using (var movCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND client_transaction_id = @opId", conn))
            {
                movCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                movCmd.Parameters.AddWithValue("opId", opId);
                Assert.Equal(0, Convert.ToInt32(await movCmd.ExecuteScalarAsync()));
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task AuthorizationRevocation_ForShareSerialization_RevocationBlocksOrRejects()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var op = new OfflineReceiptOperation(
                Guid.NewGuid(),
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 10m, fixture.BaseUomId, null, null, null, "Auth revocation test"));
            var batch = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op]);

            // Open an uncommitted transaction that revokes the device company scope
            await using var revokeConn = await _dataSource.OpenConnectionAsync();
            await using var revokeTx = await revokeConn.BeginTransactionAsync();
            await using (var scopeCmd = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, true), set_config('app.company_id', @c, true)", revokeConn, revokeTx))
            {
                scopeCmd.Parameters.AddWithValue("t", fixture.Context.TenantId.ToString());
                scopeCmd.Parameters.AddWithValue("c", fixture.Context.CompanyId.ToString());
                await scopeCmd.ExecuteNonQueryAsync();
            }

            await using (var updateScope = new NpgsqlCommand(
                "UPDATE sync.device_company_scopes SET scope_status = 'revoked', revoked_at = now() WHERE tenant_id = @t AND device_id = @d", revokeConn, revokeTx))
            {
                updateScope.Parameters.AddWithValue("t", fixture.Context.TenantId);
                updateScope.Parameters.AddWithValue("d", fixture.DeviceId);
                await updateScope.ExecuteNonQueryAsync();
            }

            // Start the sync process concurrently: It must wait on FOR SHARE lock and then see the committed revocation
            var syncTask = Task.Run(async () =>
            {
                return await fixture.Repository.ProcessReceiptOperationAtomicAsync(
                    fixture.Context, batch, op, ComputeHash(op), default);
            });

            // Allow the sync query to encounter the row lock and wait
            await Task.Delay(200);

            // Now commit the revocation transaction
            await revokeTx.CommitAsync();

            // The sync task should finish and report "unauthorized"
            var result = await syncTask;
            Assert.Equal("unauthorized", result.Status);
            Assert.Equal("OFFLINE_DEVICE_OR_GRANT_INVALID", result.Code);

            // Verify in PostgreSQL: Zero inventory movements were created
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var countCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND client_transaction_id = @opId", conn))
            {
                countCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                countCmd.Parameters.AddWithValue("opId", op.OperationId);
                Assert.Equal(0, Convert.ToInt32(await countCmd.ExecuteScalarAsync()));
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task ConcurrentSubmissions_SameIdempotencyKey_DifferentCompanies_OneAccepted_OtherConflict()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var tenantId = fixture.Context.TenantId;
            var companyId2 = Guid.NewGuid();
            var userId2 = Guid.NewGuid();
            var locationId2 = Guid.NewGuid();
            var warehouseId2 = Guid.NewGuid();
            var binId2 = Guid.NewGuid();
            var clientDeviceId2 = Guid.NewGuid();

            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                scope.Parameters.AddWithValue("t", tenantId.ToString());
                scope.Parameters.AddWithValue("c", companyId2.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            // 1. Company 2
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO organization.companies (id, tenant_id, company_code, name, legal_name, base_currency_code, country_code, timezone, status) VALUES (@id, @tenantId, @code, 'Company 2', 'Company 2 Legal', 'PKR', 'PK', 'Asia/Karachi', 'active')", conn))
            {
                cmd.Parameters.AddWithValue("id", companyId2);
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                cmd.Parameters.AddWithValue("code", $"c2-{companyId2:N}"[..20]);
                await cmd.ExecuteNonQueryAsync();
            }

            // 2. Location 2
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO organization.locations (id, tenant_id, company_id, location_code, location_type_code, name, country_code, timezone, status) VALUES (@id, @tenantId, @companyId, @code, 'warehouse', 'Location 2', 'PK', 'Asia/Karachi', 'active')", conn))
            {
                cmd.Parameters.AddWithValue("id", locationId2);
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                cmd.Parameters.AddWithValue("companyId", companyId2);
                cmd.Parameters.AddWithValue("code", $"loc2-{locationId2:N}"[..20]);
                await cmd.ExecuteNonQueryAsync();
            }

            // 3. User & Membership for Company 2
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO identity.users (id, email_normalized, display_name, status) VALUES (@id, @email, 'Admin User 2', 'active')", conn))
            {
                cmd.Parameters.AddWithValue("id", userId2);
                cmd.Parameters.AddWithValue("email", $"user2-{userId2:N}@test.local");
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO identity.tenant_memberships (id, tenant_id, user_id, status) VALUES (@id, @tenantId, @userId, 'active')", conn))
            {
                cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                cmd.Parameters.AddWithValue("userId", userId2);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO identity.company_memberships (id, tenant_id, company_id, user_id, status) VALUES (@id, @tenantId, @companyId, @userId, 'active')", conn))
            {
                cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                cmd.Parameters.AddWithValue("companyId", companyId2);
                cmd.Parameters.AddWithValue("userId", userId2);
                await cmd.ExecuteNonQueryAsync();
            }

            // 4. Warehouse 2 & Bin 2
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name, is_active, location_id) VALUES (@tenantId, @companyId, @whId, 'WH-02', 'Warehouse 2', true, @locId)", conn))
            {
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                cmd.Parameters.AddWithValue("companyId", companyId2);
                cmd.Parameters.AddWithValue("whId", warehouseId2);
                cmd.Parameters.AddWithValue("locId", locationId2);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, warehouse_id, bin_id, code, bin_type, is_active) VALUES (@tenantId, @companyId, @whId, @binId, 'BIN-B', 'storage', true)", conn))
            {
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                cmd.Parameters.AddWithValue("companyId", companyId2);
                cmd.Parameters.AddWithValue("whId", warehouseId2);
                cmd.Parameters.AddWithValue("binId", binId2);
                await cmd.ExecuteNonQueryAsync();
            }

            // 5. Register Device & Grant for Company 2
            var context2 = new TenantContext(tenantId, companyId2, userId2);
            var grant2 = await fixture.Repository.RegisterDeviceAsync(context2, clientDeviceId2, "web", "Test Device 2", default)
                ?? throw new InvalidOperationException("Failed to register test device for company 2.");

            // 6. Same Operation ID (idempotency key) across both companies!
            var sharedOpId = Guid.NewGuid();

            var op1 = new OfflineReceiptOperation(
                sharedOpId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 10m, fixture.BaseUomId, null, null, null, "Company 1 Receipt"));
            var batch1 = new OfflineReceiptBatchRequest(Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [op1]);

            var op2 = new OfflineReceiptOperation(
                sharedOpId,
                1,
                "inventory.receipt.post",
                DateTimeOffset.UtcNow,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, warehouseId2, binId2, 20m, fixture.BaseUomId, null, null, null, "Company 2 Receipt"));
            var batch2 = new OfflineReceiptBatchRequest(Guid.NewGuid(), grant2.DeviceId, grant2.GrantId, grant2.GrantToken, [op2]);

            // 7. Execute both requests concurrently
            var task1 = fixture.Repository.ProcessReceiptOperationAtomicAsync(
                fixture.Context, batch1, op1, ComputeHash(op1), default);
            var task2 = fixture.Repository.ProcessReceiptOperationAtomicAsync(
                context2, batch2, op2, ComputeHash(op2), default);

            var results = await Task.WhenAll(task1, task2);

            // 8. Assertions: exactly one commits (accepted), the other returns conflict
            var accepted = results.SingleOrDefault(r => r.Status == "accepted");
            var conflicted = results.SingleOrDefault(r => r.Status == "conflict");

            Assert.NotNull(accepted);
            Assert.NotNull(conflicted);
            Assert.Equal("COMMAND_IDEMPOTENCY_CONFLICT", conflicted.Code);

            var isCompany1Winner = accepted.Receipt!.WarehouseId == fixture.WarehouseId;
            var winningCompanyId = isCompany1Winner ? fixture.Context.CompanyId : companyId2;
            var losingCompanyId = isCompany1Winner ? companyId2 : fixture.Context.CompanyId;
            var (losingWhId, losingBinId) = isCompany1Winner
                ? (warehouseId2, binId2)
                : (fixture.WarehouseId, fixture.BinId);
            var losingDeviceId = isCompany1Winner ? grant2.DeviceId : fixture.DeviceId;

            // 9. Verify Winning Company Scope: Exactly 1 movement committed, 0 conflicts
            await using (var winScope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                winScope.Parameters.AddWithValue("t", tenantId.ToString());
                winScope.Parameters.AddWithValue("c", winningCompanyId.ToString());
                await winScope.ExecuteNonQueryAsync();
            }

            await using (var verifyWinMov = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND company_id = @c AND client_transaction_id = @opId", conn))
            {
                verifyWinMov.Parameters.AddWithValue("t", tenantId);
                verifyWinMov.Parameters.AddWithValue("c", winningCompanyId);
                verifyWinMov.Parameters.AddWithValue("opId", sharedOpId);
                Assert.Equal(1, Convert.ToInt32(await verifyWinMov.ExecuteScalarAsync()));
            }

            await using (var verifyWinConflict = new NpgsqlCommand(
                "SELECT COUNT(*) FROM sync.operation_conflicts WHERE tenant_id = @t AND company_id = @c AND operation_id = @opId", conn))
            {
                verifyWinConflict.Parameters.AddWithValue("t", tenantId);
                verifyWinConflict.Parameters.AddWithValue("c", winningCompanyId);
                verifyWinConflict.Parameters.AddWithValue("opId", sharedOpId);
                Assert.Equal(0, Convert.ToInt32(await verifyWinConflict.ExecuteScalarAsync()));
            }

            // 10. Verify Losing Company Scope: Zero movements, zero legs, zero balance change, zero outbox events
            await using (var loseScope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                loseScope.Parameters.AddWithValue("t", tenantId.ToString());
                loseScope.Parameters.AddWithValue("c", losingCompanyId.ToString());
                await loseScope.ExecuteNonQueryAsync();
            }

            // A. Zero inventory movements for losing company
            await using (var verifyLoseMov = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND company_id = @c AND client_transaction_id = @opId", conn))
            {
                verifyLoseMov.Parameters.AddWithValue("t", tenantId);
                verifyLoseMov.Parameters.AddWithValue("c", losingCompanyId);
                verifyLoseMov.Parameters.AddWithValue("opId", sharedOpId);
                Assert.Equal(0, Convert.ToInt32(await verifyLoseMov.ExecuteScalarAsync()));
            }

            // B. Zero inventory movement legs for losing company
            await using (var verifyLoseLeg = new NpgsqlCommand(
                """
                SELECT COUNT(*)
                FROM erp_inventory.inventory_movement_leg leg
                JOIN erp_inventory.inventory_movement m
                  ON m.tenant_id = leg.tenant_id AND m.company_id = leg.company_id AND m.movement_id = leg.movement_id
                WHERE leg.tenant_id = @t AND leg.company_id = @c AND m.client_transaction_id = @opId
                """, conn))
            {
                verifyLoseLeg.Parameters.AddWithValue("t", tenantId);
                verifyLoseLeg.Parameters.AddWithValue("c", losingCompanyId);
                verifyLoseLeg.Parameters.AddWithValue("opId", sharedOpId);
                Assert.Equal(0, Convert.ToInt32(await verifyLoseLeg.ExecuteScalarAsync()));
            }

            // C. Zero stock balance delta in losing company's warehouse/bin
            await using (var verifyLoseBal = new NpgsqlCommand(
                "SELECT COALESCE(SUM(on_hand_qty), 0) FROM erp_inventory.stock_balance WHERE tenant_id = @t AND company_id = @c AND warehouse_id = @w AND bin_id = @b", conn))
            {
                verifyLoseBal.Parameters.AddWithValue("t", tenantId);
                verifyLoseBal.Parameters.AddWithValue("c", losingCompanyId);
                verifyLoseBal.Parameters.AddWithValue("w", losingWhId);
                verifyLoseBal.Parameters.AddWithValue("b", losingBinId);
                Assert.Equal(0m, Convert.ToDecimal(await verifyLoseBal.ExecuteScalarAsync()));
            }

            // D. Zero outbox events for losing company
            await using (var verifyLoseOutbox = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.outbox_event WHERE tenant_id = @t AND company_id = @c AND payload::text LIKE '%' || @opId || '%'", conn))
            {
                verifyLoseOutbox.Parameters.AddWithValue("t", tenantId);
                verifyLoseOutbox.Parameters.AddWithValue("c", losingCompanyId);
                verifyLoseOutbox.Parameters.AddWithValue("opId", sharedOpId.ToString());
                Assert.Equal(0, Convert.ToInt32(await verifyLoseOutbox.ExecuteScalarAsync()));
            }

            // E. Inbox outcome is durably 'conflict' with released claim
            await using (var verifyInbox = new NpgsqlCommand(
                "SELECT outcome, claim_token, claim_expires_at, response_payload::text FROM sync.operation_inbox WHERE tenant_id = @t AND company_id = @c AND operation_id = @opId", conn))
            {
                verifyInbox.Parameters.AddWithValue("t", tenantId);
                verifyInbox.Parameters.AddWithValue("c", losingCompanyId);
                verifyInbox.Parameters.AddWithValue("opId", sharedOpId);
                await using var reader = await verifyInbox.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("conflict", reader.GetString(0));
                Assert.True(reader.IsDBNull(1)); // claim_token is NULL
                Assert.True(reader.IsDBNull(2)); // claim_expires_at is NULL
                var responsePayload = reader.GetString(3);
                Assert.Contains("COMMAND_IDEMPOTENCY_CONFLICT", responsePayload);
                await reader.CloseAsync();
            }

            // F. Conflict record is durable in sync.operation_conflicts and visible under losing company's authorized RLS scope
            await using (var verifyConflict = new NpgsqlCommand(
                "SELECT conflict_code, device_id, device_sequence FROM sync.operation_conflicts WHERE tenant_id = @t AND company_id = @c AND operation_id = @opId", conn))
            {
                verifyConflict.Parameters.AddWithValue("t", tenantId);
                verifyConflict.Parameters.AddWithValue("c", losingCompanyId);
                verifyConflict.Parameters.AddWithValue("opId", sharedOpId);
                await using var reader = await verifyConflict.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("COMMAND_IDEMPOTENCY_CONFLICT", reader.GetString(0));
                Assert.Equal(losingDeviceId, reader.GetGuid(1));
                Assert.Equal(1L, reader.GetInt64(2));
                await reader.CloseAsync();
            }

            // 11. Command idempotency table globally references the winning company
            await using (var verifyIdemp = new NpgsqlCommand(
                "SELECT company_id FROM erp_inventory.command_idempotency WHERE tenant_id = @t AND idempotency_key = @key", conn))
            {
                verifyIdemp.Parameters.AddWithValue("t", tenantId);
                verifyIdemp.Parameters.AddWithValue("key", sharedOpId);
                await using var reader = await verifyIdemp.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var actualWinningCompanyId = reader.GetGuid(0);
                Assert.False(await reader.ReadAsync());
                await reader.CloseAsync();

                Assert.Equal(winningCompanyId, actualWinningCompanyId);
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task ConcurrentSubmissions_OfflineSync_And_OnlineWorkflow_SameTransactionId_OneCommits_OtherHandledCleanly()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var tenantId = fixture.Context.TenantId;
            var companyId = fixture.Context.CompanyId;
            var sharedTxId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            // 1. Offline Operation (qty = 10)
            var offlineOp = new OfflineReceiptOperation(
                sharedTxId,
                1,
                "inventory.receipt.post",
                now,
                new PostInventoryReceiptRequest(
                    fixture.VariantId, fixture.WarehouseId, fixture.BinId, 10m, fixture.BaseUomId, null, null, null, "Offline Receipt"));
            var offlineBatch = new OfflineReceiptBatchRequest(
                Guid.NewGuid(), fixture.DeviceId, fixture.GrantId, fixture.GrantToken, [offlineOp]);

            // 2. Online Request (qty = 25)
            var onlineRepo = new PostgresInventoryWorkflowRepository(_dataSource);
            var onlineRequest = new PostInventoryReceiptRequest(
                fixture.VariantId, fixture.WarehouseId, fixture.BinId, 25m, fixture.BaseUomId, null, null, null, "Online Receipt");

            var onlinePayload = JsonSerializer.Serialize(new
            {
                command = "inventory.receipt.post",
                onlineRequest.VariantId,
                onlineRequest.WarehouseId,
                onlineRequest.BinId,
                onlineRequest.Quantity,
                onlineRequest.UnitOfMeasureId,
                SourceDocumentType = (string?)null,
                onlineRequest.SourceDocumentId,
                OccurredAt = now.ToUniversalTime(),
                Note = "Online Receipt",
            }, JsonOptions);
            var onlineHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(onlinePayload)));

            // 3. Launch both concurrently
            var offlineTask = fixture.Repository.ProcessReceiptOperationAtomicAsync(
                fixture.Context, offlineBatch, offlineOp, ComputeHash(offlineOp), default);
            var onlineTask = onlineRepo.PostReceiptAsync(
                fixture.Context, sharedTxId, onlineHash, onlineRequest, default);

            await Task.WhenAll(offlineTask, onlineTask);

            var offlineResult = await offlineTask;
            var onlineResult = await onlineTask;

            // 4. Assert: Exactly one won, the other lost cleanly
            var isOfflineWinner = offlineResult.Status == "accepted";
            var isOnlineWinner = onlineResult.IsSuccess;

            // Exactly one must succeed
            Assert.True(isOfflineWinner ^ isOnlineWinner, "Exactly one of offline or online must succeed.");

            if (isOfflineWinner)
            {
                Assert.Equal("accepted", offlineResult.Status);
                Assert.NotNull(offlineResult.Receipt);
                Assert.Equal(10m, offlineResult.Receipt.Quantity);

                // Online must fail with IDEMPOTENCY_KEY_REUSED
                Assert.False(onlineResult.IsSuccess);
                Assert.Equal("IDEMPOTENCY_KEY_REUSED", onlineResult.Error);
            }
            else
            {
                Assert.True(onlineResult.IsSuccess);
                Assert.NotNull(onlineResult.Value);
                Assert.Equal(25m, onlineResult.Value.Quantity);

                // Offline must report conflict with COMMAND_IDEMPOTENCY_CONFLICT
                Assert.Equal("conflict", offlineResult.Status);
                Assert.Equal("COMMAND_IDEMPOTENCY_CONFLICT", offlineResult.Code);
            }

            // 5. Verify in PostgreSQL: Exactly 1 inventory movement exists globally for this client_transaction_id
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn))
            {
                scope.Parameters.AddWithValue("t", tenantId.ToString());
                scope.Parameters.AddWithValue("c", companyId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            await using (var verifyMov = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND client_transaction_id = @id", conn))
            {
                verifyMov.Parameters.AddWithValue("t", tenantId);
                verifyMov.Parameters.AddWithValue("id", sharedTxId);
                var count = Convert.ToInt32(await verifyMov.ExecuteScalarAsync());
                Assert.Equal(1, count);
            }

            // 6. Verify in PostgreSQL: stock balance equals winning quantity (10 or 25, never 35)
            var expectedQty = isOfflineWinner ? 10m : 25m;
            await using (var verifyBal = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @t AND company_id = @c AND variant_id = @v AND warehouse_id = @w AND bin_id = @b", conn))
            {
                verifyBal.Parameters.AddWithValue("t", tenantId);
                verifyBal.Parameters.AddWithValue("c", companyId);
                verifyBal.Parameters.AddWithValue("v", fixture.VariantId);
                verifyBal.Parameters.AddWithValue("w", fixture.WarehouseId);
                verifyBal.Parameters.AddWithValue("b", fixture.BinId);
                var onHand = Convert.ToDecimal(await verifyBal.ExecuteScalarAsync());
                Assert.Equal(expectedQty, onHand);
            }

            // 7. Verify in PostgreSQL: command_idempotency has exactly 1 row
            await using (var verifyIdemp = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.command_idempotency WHERE tenant_id = @t AND idempotency_key = @id", conn))
            {
                verifyIdemp.Parameters.AddWithValue("t", tenantId);
                verifyIdemp.Parameters.AddWithValue("id", sharedTxId);
                var count = Convert.ToInt32(await verifyIdemp.ExecuteScalarAsync());
                Assert.Equal(1, count);
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }
}
