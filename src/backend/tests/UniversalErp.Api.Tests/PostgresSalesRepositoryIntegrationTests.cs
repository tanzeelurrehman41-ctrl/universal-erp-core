using System.Data;
using System.Threading;
using Npgsql;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Inventory;
using UniversalErp.Application.Sales;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Inventory;
using UniversalErp.Infrastructure.Sales;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class PostgresSalesRepositoryIntegrationTests : IAsyncDisposable
{
    private static readonly string AppConnectionString = ResolveAppConnectionString();
    private static readonly string AdminConnectionString = ResolveAdminConnectionString();

    private static readonly Guid SeedTenantId = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly Guid SeedCompanyId = Guid.Parse("00000000-0000-4000-8000-000000000002");
    private static readonly Guid SeedCustomerId = Guid.Parse("00000000-0000-4000-8000-000000000301");
    private static readonly Guid SeedUserId = Guid.Parse("00000000-0000-4000-8000-000000000005");

    private readonly NpgsqlDataSource _appDataSource;
    private readonly NpgsqlDataSource _adminDataSource;

    public PostgresSalesRepositoryIntegrationTests()
    {
        _appDataSource = NpgsqlDataSource.Create(AppConnectionString);
        _adminDataSource = NpgsqlDataSource.Create(AdminConnectionString);
    }

    private static void ValidateApprovedEndpoint(string connectionString, string expectedUser)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        bool isLoopback = builder.Host == "127.0.0.1" || builder.Host == "localhost";
        int expectedPort = int.TryParse(Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_TARGET_PORT"), out var p) ? p : 5434;
        if (!isLoopback || builder.Port != expectedPort || builder.Database != "universal_erp_disposable_test" || builder.Username != expectedUser)
        {
            throw new InvalidOperationException(
                $"[FAIL-CLOSED] Connection string targets unapproved endpoint! Expected Host=127.0.0.1, Port={expectedPort}, Database=universal_erp_disposable_test, Username={expectedUser}. Got Host={builder.Host}, Port={builder.Port}, Database={builder.Database}, Username={builder.Username}. Accidental connections to production, staging, or development instances are strictly prevented.");
        }
    }

    private static string ResolveAppConnectionString()
    {
        var env = Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__UniversalErpTest");
        if (!string.IsNullOrWhiteSpace(env))
        {
            ValidateApprovedEndpoint(env, expectedUser: "erp_app");
            return env;
        }

        int targetPort = int.TryParse(Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_TARGET_PORT"), out var p) ? p : 5434;
        var pass = Environment.GetEnvironmentVariable("PGPASSWORD_ERP_APP");
        if (!string.IsNullOrWhiteSpace(pass))
        {
            var cs = $"Host=127.0.0.1;Port={targetPort};Database=universal_erp_disposable_test;Username=erp_app;Password={pass};Include Error Detail=true";
            ValidateApprovedEndpoint(cs, expectedUser: "erp_app");
            return cs;
        }
        throw new InvalidOperationException("[FAIL-CLOSED] UNIVERSAL_ERP_TEST_CONNECTION_STRING or PGPASSWORD_ERP_APP environment variable is not configured. Plaintext passwords must not be hardcoded in test source code or runsettings.");
    }

    private static string ResolveAdminConnectionString()
    {
        var env = Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__UniversalErpTestAdmin");
        if (!string.IsNullOrWhiteSpace(env))
        {
            ValidateApprovedEndpoint(env, expectedUser: "erp_dev");
            return env;
        }

        int targetPort = int.TryParse(Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_TARGET_PORT"), out var p) ? p : 5434;
        var pass = Environment.GetEnvironmentVariable("PGPASSWORD_ERP_DEV");
        if (!string.IsNullOrWhiteSpace(pass))
        {
            var cs = $"Host=127.0.0.1;Port={targetPort};Database=universal_erp_disposable_test;Username=erp_dev;Password={pass};Include Error Detail=true";
            ValidateApprovedEndpoint(cs, expectedUser: "erp_dev");
            return cs;
        }
        throw new InvalidOperationException("[FAIL-CLOSED] UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING or PGPASSWORD_ERP_DEV environment variable is not configured. Plaintext passwords must not be hardcoded in test source code or runsettings.");
    }

    public async ValueTask DisposeAsync()
    {
        await _appDataSource.DisposeAsync();
        await _adminDataSource.DisposeAsync();
    }

    private sealed class TestTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current { get; } = context;
    }

    [Fact]
    public async Task GetCustomerAsync_AuthorizedContext_ReturnsCustomer()
    {
        var context = new TenantContext(SeedTenantId, SeedCompanyId, SeedUserId);
        var accessor = new TestTenantContextAccessor(context);
        var repository = new PostgresSalesRepository(_appDataSource, accessor);

        var customer = await repository.GetCustomerAsync(SeedCustomerId, CancellationToken.None);

        Assert.NotNull(customer);
        Assert.Equal(SeedCustomerId, customer.CustomerId);
        Assert.Equal("CUST-WALKIN", customer.CustomerCode);
        Assert.Equal("Walk-in Retail Customer", customer.Name);
    }

    [Fact]
    public async Task GetCustomerAsync_ForeignTenant_ReturnsNull()
    {
        var foreignTenantId = Guid.NewGuid();
        var context = new TenantContext(foreignTenantId, SeedCompanyId, SeedUserId);
        var accessor = new TestTenantContextAccessor(context);
        var repository = new PostgresSalesRepository(_appDataSource, accessor);

        var customer = await repository.GetCustomerAsync(SeedCustomerId, CancellationToken.None);

        Assert.Null(customer);
    }

    [Fact]
    public async Task GetCustomerAsync_SameTenantWrongCompany_ReturnsNull()
    {
        var otherCompanyId = Guid.NewGuid();
        var context = new TenantContext(SeedTenantId, otherCompanyId, SeedUserId);
        var accessor = new TestTenantContextAccessor(context);
        var repository = new PostgresSalesRepository(_appDataSource, accessor);

        var customer = await repository.GetCustomerAsync(SeedCustomerId, CancellationToken.None);

        Assert.Null(customer);
    }

    [Fact]
    public async Task GetCustomerAsync_WrongTenantSameCompany_ReturnsNull()
    {
        var wrongTenantId = Guid.NewGuid();
        var context = new TenantContext(wrongTenantId, SeedCompanyId, SeedUserId);
        var accessor = new TestTenantContextAccessor(context);
        var repository = new PostgresSalesRepository(_appDataSource, accessor);

        var customer = await repository.GetCustomerAsync(SeedCustomerId, CancellationToken.None);

        Assert.Null(customer);
    }

    [Fact]
    public async Task ListCustomersAsync_AuthorizedContext_ReturnsOnlyCompanyCustomers()
    {
        var context = new TenantContext(SeedTenantId, SeedCompanyId, SeedUserId);
        var accessor = new TestTenantContextAccessor(context);
        var repository = new PostgresSalesRepository(_appDataSource, accessor);

        var customers = await repository.ListCustomersAsync(CancellationToken.None);

        Assert.NotEmpty(customers);
        Assert.Contains(customers, c => c.CustomerId == SeedCustomerId && c.CustomerCode == "CUST-WALKIN");
    }

    [Fact]
    public async Task ListCustomersAsync_ForeignTenant_ReturnsEmpty()
    {
        var foreignTenantId = Guid.NewGuid();
        var context = new TenantContext(foreignTenantId, SeedCompanyId, SeedUserId);
        var accessor = new TestTenantContextAccessor(context);
        var repository = new PostgresSalesRepository(_appDataSource, accessor);

        var customers = await repository.ListCustomersAsync(CancellationToken.None);

        Assert.Empty(customers);
    }

    [Fact]
    public async Task ListCustomersAsync_WrongCompany_ReturnsEmpty()
    {
        var unseededCompanyId = Guid.NewGuid();
        var context = new TenantContext(SeedTenantId, unseededCompanyId, SeedUserId);
        var accessor = new TestTenantContextAccessor(context);
        var repository = new PostgresSalesRepository(_appDataSource, accessor);

        var customers = await repository.ListCustomersAsync(CancellationToken.None);

        Assert.Empty(customers);
    }

    [Fact]
    public async Task CreateCustomerAsync_AuthorizedContext_InsertsAndSetsContext()
    {
        var context = new TenantContext(SeedTenantId, SeedCompanyId, SeedUserId);
        var accessor = new TestTenantContextAccessor(context);
        var repository = new PostgresSalesRepository(_appDataSource, accessor);

        var uniqueCode = $"CUST-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        var request = new CreateCustomerRequest(
            CustomerCode: uniqueCode,
            Name: "Integration Test Customer",
            CustomerType: "retail",
            ContactName: "Test Contact",
            Email: "test@example.com",
            Phone: "555-0199",
            Address: "123 Test Street",
            CreditLimit: 1000m,
            PaymentTermsDays: 30,
            CurrencyCode: "PKR");

        var createdId = await repository.CreateCustomerAsync(request, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, createdId);

        try
        {
            var fetched = await repository.GetCustomerAsync(createdId, CancellationToken.None);
            Assert.NotNull(fetched);
            Assert.Equal(createdId, fetched.CustomerId);
            Assert.Equal(uniqueCode, fetched.CustomerCode);
            Assert.Equal("Integration Test Customer", fetched.Name);
        }
        finally
        {
            await using var conn = await _adminDataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM erp_sales.customer WHERE tenant_id = @t AND company_id = @c AND customer_id = @id;";
            cmd.Parameters.AddWithValue("t", SeedTenantId);
            cmd.Parameters.AddWithValue("c", SeedCompanyId);
            cmd.Parameters.AddWithValue("id", createdId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task AssertConnectionIdentity_AppRole_SubjectToRls()
    {
        await using var connection = await _appDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_user, session_user, r.rolbypassrls FROM pg_roles r WHERE r.rolname = current_user;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("erp_app", reader.GetString(0));
        Assert.Equal("erp_app", reader.GetString(1));
        Assert.False(reader.GetBoolean(2));
    }

    [Fact]
    public async Task AssertConnectionIdentity_AdminRole_BypassesRls()
    {
        await using var connection = await _adminDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_user, session_user, r.rolbypassrls FROM pg_roles r WHERE r.rolname = current_user;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("erp_dev", reader.GetString(0));
        Assert.Equal("erp_dev", reader.GetString(1));
        Assert.True(reader.GetBoolean(2));
    }

    [Fact]
    public async Task PostInvoiceAsync_WhenGlPostingFails_RollsBackInventoryAndInvoiceAtomically()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var adminConn = await _adminDataSource.OpenConnectionAsync();
        await SeedSalesTestHierarchyAsync(
            adminConn, tenantId, companyId, userId, locationId, warehouseId, binId, uomId, productId, variantId, customerId,
            startingQty: 23m, seedFinanceAccounts: false);

        try
        {
            await SeedCostLayerAsync(adminConn, tenantId, companyId, variantId, warehouseId, binId, 23m, 45m, DateTimeOffset.UtcNow.AddDays(-1));

            var context = new TenantContext(tenantId, companyId, userId);
            var repo = new PostgresSalesRepository(_appDataSource, new TestTenantContextAccessor(context));
            var req = new PostSalesInvoiceRequest(
                ClientTransactionId: Guid.NewGuid(),
                InvoiceNumber: "POS-RB-TEST-01",
                CustomerId: customerId,
                PaymentMethod: "cash",
                Lines:
                [
                    new PostSalesInvoiceLineRequest(variantId, warehouseId, binId, 5m, uomId, UnitPrice: 75m, TaxRate: 0m)
                ]);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.PostInvoiceAsync(req, userId, CancellationToken.None));
            Assert.StartsWith("GL_POSTING_FAILED", ex.Message);

            // Assert zero partial commits across stock, FIFO layers, allocations, invoices, and movements
            await using var balCmd = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @t AND company_id = @c AND variant_id = @v;", adminConn);
            balCmd.Parameters.AddWithValue("t", tenantId);
            balCmd.Parameters.AddWithValue("c", companyId);
            balCmd.Parameters.AddWithValue("v", variantId);
            Assert.Equal(23m, (decimal)(await balCmd.ExecuteScalarAsync())!);

            await using var layerCmd = new NpgsqlCommand(
                "SELECT remaining_qty FROM erp_inventory.inventory_cost_layer WHERE tenant_id = @t AND company_id = @c AND variant_id = @v;", adminConn);
            layerCmd.Parameters.AddWithValue("t", tenantId);
            layerCmd.Parameters.AddWithValue("c", companyId);
            layerCmd.Parameters.AddWithValue("v", variantId);
            Assert.Equal(23m, (decimal)(await layerCmd.ExecuteScalarAsync())!);

            await using var allocCmd = new NpgsqlCommand(
                "SELECT count(*) FROM erp_inventory.inventory_cost_allocation WHERE tenant_id = @t AND company_id = @c;", adminConn);
            allocCmd.Parameters.AddWithValue("t", tenantId);
            allocCmd.Parameters.AddWithValue("c", companyId);
            Assert.Equal(0L, (long)(await allocCmd.ExecuteScalarAsync())!);

            await using var invCmd = new NpgsqlCommand(
                "SELECT count(*) FROM erp_sales.sales_invoice WHERE tenant_id = @t AND company_id = @c;", adminConn);
            invCmd.Parameters.AddWithValue("t", tenantId);
            invCmd.Parameters.AddWithValue("c", companyId);
            Assert.Equal(0L, (long)(await invCmd.ExecuteScalarAsync())!);

            await using var movCmd = new NpgsqlCommand(
                "SELECT count(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND company_id = @c;", adminConn);
            movCmd.Parameters.AddWithValue("t", tenantId);
            movCmd.Parameters.AddWithValue("c", companyId);
            Assert.Equal(0L, (long)(await movCmd.ExecuteScalarAsync())!);
        }
        finally
        {
            await CleanupSalesTestHierarchyAsync(adminConn, tenantId);
        }
    }

    [Fact]
    public async Task PostInvoiceAsync_MultiLayerFifo_IgnoresClientCost_PostsGl_And_PreventsDuplicateConsumption()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var adminConn = await _adminDataSource.OpenConnectionAsync();
        await SeedSalesTestHierarchyAsync(
            adminConn, tenantId, companyId, userId, locationId, warehouseId, binId, uomId, productId, variantId, customerId,
            startingQty: 14m, seedFinanceAccounts: true);

        try
        {
            var layer1Id = await SeedCostLayerAsync(adminConn, tenantId, companyId, variantId, warehouseId, binId, 4m, 40m, DateTimeOffset.UtcNow.AddDays(-2));
            var layer2Id = await SeedCostLayerAsync(adminConn, tenantId, companyId, variantId, warehouseId, binId, 10m, 50m, DateTimeOffset.UtcNow.AddDays(-1));

            var context = new TenantContext(tenantId, companyId, userId);
            var repo = new PostgresSalesRepository(_appDataSource, new TestTenantContextAccessor(context));
            var clientTxId = Guid.NewGuid();
            var req = new PostSalesInvoiceRequest(
                ClientTransactionId: clientTxId,
                InvoiceNumber: "POS-FIFO-INT-01",
                CustomerId: customerId,
                PaymentMethod: "cash",
                Lines:
                [
                    new PostSalesInvoiceLineRequest(variantId, warehouseId, binId, 5m, uomId, UnitPrice: 75m, UnitCost: 999m, TaxRate: 0m)
                ]);

            var invoiceId = await repo.PostInvoiceAsync(req, userId, CancellationToken.None);
            Assert.NotEqual(Guid.Empty, invoiceId);

            // Verify FIFO layer remaining quantities (4@40 exhausted, 1@50 consumed from layer 2 -> 9 remaining)
            await using var l1Cmd = new NpgsqlCommand("SELECT remaining_qty FROM erp_inventory.inventory_cost_layer WHERE tenant_id = @t AND company_id = @c AND layer_id = @id;", adminConn);
            l1Cmd.Parameters.AddWithValue("t", tenantId); l1Cmd.Parameters.AddWithValue("c", companyId); l1Cmd.Parameters.AddWithValue("id", layer1Id);
            Assert.Equal(0m, (decimal)(await l1Cmd.ExecuteScalarAsync())!);

            await using var l2Cmd = new NpgsqlCommand("SELECT remaining_qty FROM erp_inventory.inventory_cost_layer WHERE tenant_id = @t AND company_id = @c AND layer_id = @id;", adminConn);
            l2Cmd.Parameters.AddWithValue("t", tenantId); l2Cmd.Parameters.AddWithValue("c", companyId); l2Cmd.Parameters.AddWithValue("id", layer2Id);
            Assert.Equal(9m, (decimal)(await l2Cmd.ExecuteScalarAsync())!);

            // Verify COGS GL posting = 4*40 + 1*50 = 210 PKR
            await using var cogsCmd = new NpgsqlCommand(
                "SELECT sum(jl.debit_functional) FROM finance.journal_lines jl JOIN finance.accounts a ON a.id = jl.account_id AND a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id WHERE jl.tenant_id = @t AND jl.company_id = @c AND a.account_code = '5010';", adminConn);
            cogsCmd.Parameters.AddWithValue("t", tenantId); cogsCmd.Parameters.AddWithValue("c", companyId);
            Assert.Equal(210m, (decimal)(await cogsCmd.ExecuteScalarAsync())!);

            // Replay with identical ClientTransactionId rejects at DB unique index before touching layers again
            await Assert.ThrowsAnyAsync<Exception>(() => repo.PostInvoiceAsync(req, userId, CancellationToken.None));
            Assert.Equal(9m, (decimal)(await l2Cmd.ExecuteScalarAsync())!);
        }
        finally
        {
            await CleanupSalesTestHierarchyAsync(adminConn, tenantId);
        }
    }

    [Fact]
    public async Task PostInvoiceAsync_CrossBinValuationForbidden_ThrowsWhenBinHasUnvaluedStock()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var binPickId = Guid.NewGuid();
        var binStoreId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var adminConn = await _adminDataSource.OpenConnectionAsync();
        await SeedSalesTestHierarchyAsync(
            adminConn, tenantId, companyId, userId, locationId, warehouseId, binPickId, uomId, productId, variantId, customerId,
            startingQty: 18m, seedFinanceAccounts: true);

        // Add second bin: STORE-01
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, warehouse_id, bin_id, code, bin_type, is_active) VALUES (@t, @c, @w, @id, 'STORE-01', 'storage', true)", adminConn))
        {
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("c", companyId);
            cmd.Parameters.AddWithValue("w", warehouseId);
            cmd.Parameters.AddWithValue("id", binStoreId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Add 3 EA unvalued stock to STORE-01 (stock_balance exists, but 0 cost layers)
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.stock_balance (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id) VALUES (@t, @c, @v, @w, @b, 'available', 3, @balId)", adminConn))
        {
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("c", companyId);
            cmd.Parameters.AddWithValue("v", variantId);
            cmd.Parameters.AddWithValue("w", warehouseId);
            cmd.Parameters.AddWithValue("b", binStoreId);
            cmd.Parameters.AddWithValue("balId", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }

        // Add 18 EA valued cost layer to PICK-01
        await SeedCostLayerAsync(adminConn, tenantId, companyId, variantId, warehouseId, binPickId, 18m, 45m, DateTimeOffset.UtcNow.AddDays(-1));

        try
        {
            var context = new TenantContext(tenantId, companyId, userId);
            var repo = new PostgresSalesRepository(_appDataSource, new TestTenantContextAccessor(context));

            // Attempt checkout from STORE-01 (has 3 EA physical, but 0 layers). Must fail fail-closed with MISSING_VALUATION_DATA
            // and must NOT borrow layers from PICK-01!
            var req = new PostSalesInvoiceRequest(
                ClientTransactionId: Guid.NewGuid(),
                InvoiceNumber: "POS-UNVALUED-01",
                CustomerId: customerId,
                PaymentMethod: "cash",
                Lines:
                [
                    new PostSalesInvoiceLineRequest(variantId, warehouseId, binStoreId, 1m, uomId, UnitPrice: 75m, TaxRate: 0m)
                ]);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.PostInvoiceAsync(req, userId, CancellationToken.None));
            Assert.StartsWith("MISSING_VALUATION_DATA", ex.Message);
            Assert.Contains("unvalued stock", ex.Message);

            // Assert PICK-01 layer remained completely untouched
            await using var checkPickLayer = new NpgsqlCommand(
                "SELECT remaining_qty FROM erp_inventory.inventory_cost_layer WHERE tenant_id = @t AND bin_id = @b;", adminConn);
            checkPickLayer.Parameters.AddWithValue("t", tenantId);
            checkPickLayer.Parameters.AddWithValue("b", binPickId);
            Assert.Equal(18m, (decimal)(await checkPickLayer.ExecuteScalarAsync())!);
        }
        finally
        {
            await CleanupSalesTestHierarchyAsync(adminConn, tenantId);
        }
    }

    [Fact]
    public async Task PostTransferAsync_MovesCostLayersAcrossBins_PreservingReceiptChronologyAndUnitCost()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var binPickId = Guid.NewGuid();
        var binReceiveId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var adminConn = await _adminDataSource.OpenConnectionAsync();
        await SeedSalesTestHierarchyAsync(
            adminConn, tenantId, companyId, userId, locationId, warehouseId, binPickId, uomId, productId, variantId, customerId,
            startingQty: 10m, seedFinanceAccounts: true);

        // Add second bin: RECEIVE-01
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, warehouse_id, bin_id, code, bin_type, is_active) VALUES (@t, @c, @w, @id, 'RECEIVE-01', 'receiving', true)", adminConn))
        {
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("c", companyId);
            cmd.Parameters.AddWithValue("w", warehouseId);
            cmd.Parameters.AddWithValue("id", binReceiveId);
            await cmd.ExecuteNonQueryAsync();
        }

        var sourceLayerDate = DateTimeOffset.UtcNow.AddDays(-5);
        var sourceLayerId = await SeedCostLayerAsync(adminConn, tenantId, companyId, variantId, warehouseId, binPickId, 10m, 45m, sourceLayerDate);

        try
        {
            var context = new TenantContext(tenantId, companyId, userId);
            var inventoryRepo = new PostgresInventoryWorkflowRepository(_appDataSource);

            // Transfer 4 EA from PICK-01 to RECEIVE-01
            var transferReq = new PostInventoryTransferRequest(
                VariantId: variantId,
                SourceWarehouseId: warehouseId,
                SourceBinId: binPickId,
                DestinationWarehouseId: warehouseId,
                DestinationBinId: binReceiveId,
                Quantity: 4m,
                UnitOfMeasureId: uomId,
                Note: "Transfer 4 EA for testing FIFO layer movement");

            var transferResult = await inventoryRepo.PostTransferAsync(context, Guid.NewGuid(), "hash-01", transferReq, CancellationToken.None);
            Assert.True(transferResult.IsSuccess, transferResult.Error);

            // Assert source bin balance is decremented to 6 EA
            await using var srcBalCmd = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @t AND bin_id = @b AND status_code = 'available';", adminConn);
            srcBalCmd.Parameters.AddWithValue("t", tenantId);
            srcBalCmd.Parameters.AddWithValue("b", binPickId);
            Assert.Equal(6m, (decimal)(await srcBalCmd.ExecuteScalarAsync())!);

            // Assert destination bin balance is 4 EA
            await using var dstBalCmd = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @t AND bin_id = @b AND status_code = 'available';", adminConn);
            dstBalCmd.Parameters.AddWithValue("t", tenantId);
            dstBalCmd.Parameters.AddWithValue("b", binReceiveId);
            Assert.Equal(4m, (decimal)(await dstBalCmd.ExecuteScalarAsync())!);

            // Assert source layer remaining_qty is decremented to 6 EA
            await using var srcLayerCmd = new NpgsqlCommand(
                "SELECT remaining_qty FROM erp_inventory.inventory_cost_layer WHERE tenant_id = @t AND layer_id = @id;", adminConn);
            srcLayerCmd.Parameters.AddWithValue("t", tenantId);
            srcLayerCmd.Parameters.AddWithValue("id", sourceLayerId);
            Assert.Equal(6m, (decimal)(await srcLayerCmd.ExecuteScalarAsync())!);

            // Assert destination child layer exists with parent_layer_id, original unit_cost 45, original received_at, and remaining_qty 4
            await using var dstLayerCmd = new NpgsqlCommand(
                """
                SELECT layer_id, parent_layer_id, original_qty, remaining_qty, unit_cost, received_at
                FROM erp_inventory.inventory_cost_layer
                WHERE tenant_id = @t AND bin_id = @b AND parent_layer_id = @parent;
                """, adminConn);
            dstLayerCmd.Parameters.AddWithValue("t", tenantId);
            dstLayerCmd.Parameters.AddWithValue("b", binReceiveId);
            dstLayerCmd.Parameters.AddWithValue("parent", sourceLayerId);
            await using (var reader = await dstLayerCmd.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(sourceLayerId, reader.GetGuid(1));
                Assert.Equal(4m, reader.GetDecimal(2));
                Assert.Equal(4m, reader.GetDecimal(3));
                Assert.Equal(45m, reader.GetDecimal(4));
                Assert.Equal(sourceLayerDate.ToUnixTimeSeconds(), reader.GetFieldValue<DateTimeOffset>(5).ToUnixTimeSeconds());
            }

            // Assert inventory_cost_allocation has transfer record
            await using var allocCmd = new NpgsqlCommand(
                """
                SELECT count(*), sum(consumed_qty), sum(extended_cost)
                FROM erp_inventory.inventory_cost_allocation
                WHERE tenant_id = @t AND source_document_type = 'inventory_transfer' AND layer_id = @parent;
                """, adminConn);
            allocCmd.Parameters.AddWithValue("t", tenantId);
            allocCmd.Parameters.AddWithValue("parent", sourceLayerId);
            await using (var allocReader = await allocCmd.ExecuteReaderAsync())
            {
                Assert.True(await allocReader.ReadAsync());
                Assert.Equal(1L, allocReader.GetInt64(0));
                Assert.Equal(4m, allocReader.GetDecimal(1));
                Assert.Equal(180m, allocReader.GetDecimal(2));
            }

            // Now post a POS invoice from RECEIVE-01 for the transferred 4 EA
            var salesRepo = new PostgresSalesRepository(_appDataSource, new TestTenantContextAccessor(context));
            var invoiceReq = new PostSalesInvoiceRequest(
                ClientTransactionId: Guid.NewGuid(),
                InvoiceNumber: "POS-TRANS-01",
                CustomerId: customerId,
                PaymentMethod: "cash",
                Lines:
                [
                    new PostSalesInvoiceLineRequest(variantId, warehouseId, binReceiveId, 4m, uomId, UnitPrice: 75m, TaxRate: 0m)
                ]);

            var invoiceId = await salesRepo.PostInvoiceAsync(invoiceReq, userId, CancellationToken.None);
            Assert.NotEqual(Guid.Empty, invoiceId);

            // Assert destination child layer is now fully consumed (remaining_qty = 0)
            await using var dstConsumedCmd = new NpgsqlCommand(
                "SELECT remaining_qty FROM erp_inventory.inventory_cost_layer WHERE tenant_id = @t AND bin_id = @b AND parent_layer_id = @parent;", adminConn);
            dstConsumedCmd.Parameters.AddWithValue("t", tenantId);
            dstConsumedCmd.Parameters.AddWithValue("b", binReceiveId);
            dstConsumedCmd.Parameters.AddWithValue("parent", sourceLayerId);
            Assert.Equal(0m, (decimal)(await dstConsumedCmd.ExecuteScalarAsync())!);

            // Assert COGS recorded for 4 EA @ 45 = 180 PKR
            await using var cogsCmd = new NpgsqlCommand(
                """
                SELECT sum(jl.debit_functional)
                FROM finance.journal_lines jl
                JOIN finance.accounts a ON a.id = jl.account_id AND a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id
                WHERE jl.tenant_id = @t AND jl.company_id = @c AND a.account_code = '5010';
                """, adminConn);
            cogsCmd.Parameters.AddWithValue("t", tenantId);
            cogsCmd.Parameters.AddWithValue("c", companyId);
            Assert.Equal(180m, (decimal)(await cogsCmd.ExecuteScalarAsync())!);
        }
        finally
        {
            await CleanupSalesTestHierarchyAsync(adminConn, tenantId);
        }
    }

    [Fact]
    public async Task PostTransferAsync_UnvaluedStock_MovesPhysicalStockWithoutCreatingCostLayers()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var binStoreId = Guid.NewGuid();
        var binReceiveId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var adminConn = await _adminDataSource.OpenConnectionAsync();
        await SeedSalesTestHierarchyAsync(
            adminConn, tenantId, companyId, userId, locationId, warehouseId, binStoreId, uomId, productId, variantId, customerId,
            startingQty: 3m, seedFinanceAccounts: false);

        // Add second bin: RECEIVE-01
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, warehouse_id, bin_id, code, bin_type, is_active) VALUES (@t, @c, @w, @id, 'RECEIVE-01', 'receiving', true)", adminConn))
        {
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("c", companyId);
            cmd.Parameters.AddWithValue("w", warehouseId);
            cmd.Parameters.AddWithValue("id", binReceiveId);
            await cmd.ExecuteNonQueryAsync();
        }

        // NOTE: ZERO cost layers seeded for STORE-01 (unvalued stock)

        try
        {
            var context = new TenantContext(tenantId, companyId, userId);
            var inventoryRepo = new PostgresInventoryWorkflowRepository(_appDataSource);

            // Transfer 2 EA from STORE-01 to RECEIVE-01
            var transferReq = new PostInventoryTransferRequest(
                VariantId: variantId,
                SourceWarehouseId: warehouseId,
                SourceBinId: binStoreId,
                DestinationWarehouseId: warehouseId,
                DestinationBinId: binReceiveId,
                Quantity: 2m,
                UnitOfMeasureId: uomId,
                Note: "Transfer 2 EA unvalued stock");

            var transferResult = await inventoryRepo.PostTransferAsync(context, Guid.NewGuid(), "hash-02", transferReq, CancellationToken.None);
            Assert.True(transferResult.IsSuccess, transferResult.Error);

            // Assert source balance is decremented to 1 EA
            await using var srcBalCmd = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @t AND bin_id = @b AND status_code = 'available';", adminConn);
            srcBalCmd.Parameters.AddWithValue("t", tenantId);
            srcBalCmd.Parameters.AddWithValue("b", binStoreId);
            Assert.Equal(1m, (decimal)(await srcBalCmd.ExecuteScalarAsync())!);

            // Assert destination balance is 2 EA
            await using var dstBalCmd = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @t AND bin_id = @b AND status_code = 'available';", adminConn);
            dstBalCmd.Parameters.AddWithValue("t", tenantId);
            dstBalCmd.Parameters.AddWithValue("b", binReceiveId);
            Assert.Equal(2m, (decimal)(await dstBalCmd.ExecuteScalarAsync())!);

            // Assert ZERO cost layers exist anywhere for this tenant/variant (no layers fabricated for unvalued stock!)
            await using var layerCheckCmd = new NpgsqlCommand(
                "SELECT count(*) FROM erp_inventory.inventory_cost_layer WHERE tenant_id = @t AND variant_id = @v;", adminConn);
            layerCheckCmd.Parameters.AddWithValue("t", tenantId);
            layerCheckCmd.Parameters.AddWithValue("v", variantId);
            Assert.Equal(0L, (long)(await layerCheckCmd.ExecuteScalarAsync())!);
        }
        finally
        {
            await CleanupSalesTestHierarchyAsync(adminConn, tenantId);
        }
    }

    private static async Task<Guid> SeedCostLayerAsync(
        NpgsqlConnection conn,
        Guid tenantId,
        Guid companyId,
        Guid variantId,
        Guid warehouseId,
        Guid binId,
        decimal qty,
        decimal unitCost,
        DateTimeOffset receivedAt)
    {
        var layerId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO erp_inventory.inventory_cost_layer
                (tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id, status_code,
                 source_document_type, received_at, original_qty, remaining_qty, unit_cost, currency_code)
            VALUES
                (@t, @c, @id, @v, @w, @b, 'available', 'grn', @r, @q, @q, @cost, 'PKR');
            """, conn);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("c", companyId);
        cmd.Parameters.AddWithValue("id", layerId);
        cmd.Parameters.AddWithValue("v", variantId);
        cmd.Parameters.AddWithValue("w", warehouseId);
        cmd.Parameters.AddWithValue("b", binId);
        cmd.Parameters.AddWithValue("r", receivedAt);
        cmd.Parameters.AddWithValue("q", qty);
        cmd.Parameters.AddWithValue("cost", unitCost);
        await cmd.ExecuteNonQueryAsync();
        return layerId;
    }

    private static async Task SeedSalesTestHierarchyAsync(
        NpgsqlConnection conn,
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid locationId,
        Guid warehouseId,
        Guid binId,
        Guid uomId,
        Guid productId,
        Guid variantId,
        Guid customerId,
        decimal startingQty,
        bool seedFinanceAccounts)
    {
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO platform.tenants (id, tenant_code, display_name, status) VALUES (@id, @code, 'Sales Test Tenant', 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", tenantId);
            cmd.Parameters.AddWithValue("code", $"st-{tenantId:N}"[..20]);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO organization.companies (id, tenant_id, company_code, name, legal_name, base_currency_code, country_code, timezone, status) VALUES (@id, @t, 'MAIN', 'Main Co', 'Main Co', 'PKR', 'PK', 'Asia/Karachi', 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", companyId);
            cmd.Parameters.AddWithValue("t", tenantId);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO organization.locations (id, tenant_id, company_id, location_code, location_type_code, name, country_code, timezone, status) VALUES (@id, @t, @c, 'LOC-1', 'warehouse', 'Wh Loc', 'PK', 'Asia/Karachi', 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", locationId);
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("c", companyId);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO identity.users (id, email_normalized, display_name, status) VALUES (@id, @email, 'Sales Tester', 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", userId);
            cmd.Parameters.AddWithValue("email", $"sales-{userId:N}@test.com");
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension, decimal_places) VALUES (@t, @id, 'EA', 'Each', 'count', 0)", conn))
        {
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("id", uomId);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.stock_status (tenant_id, status_code, description, is_reservable, is_pickable) VALUES (@t, 'available', 'Available', true, true)", conn))
        {
            cmd.Parameters.AddWithValue("t", tenantId);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, product_type, is_active) VALUES (@t, @id, 'PROD-S', 'Test Stock Product', 'stock', true)", conn))
        {
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("id", productId);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.product_variant (tenant_id, variant_id, product_id, sku, variant_name, base_uom_id, tracking_mode, costing_method, is_active) VALUES (@t, @id, @p, 'SKU-S-01', 'Standard', @u, 'none', 'fifo', true)", conn))
        {
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("id", variantId);
            cmd.Parameters.AddWithValue("p", productId);
            cmd.Parameters.AddWithValue("u", uomId);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name, is_active, location_id) VALUES (@t, @c, @id, 'WH-S', 'Sales Wh', true, @loc)", conn))
        {
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("c", companyId);
            cmd.Parameters.AddWithValue("id", warehouseId);
            cmd.Parameters.AddWithValue("loc", locationId);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, warehouse_id, bin_id, code, bin_type, is_active) VALUES (@t, @c, @w, @id, 'BIN-S1', 'storage', true)", conn))
        {
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("c", companyId);
            cmd.Parameters.AddWithValue("w", warehouseId);
            cmd.Parameters.AddWithValue("id", binId);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.stock_balance (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id) VALUES (@t, @c, @v, @w, @b, 'available', @qty, @balId)", conn))
        {
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("c", companyId);
            cmd.Parameters.AddWithValue("v", variantId);
            cmd.Parameters.AddWithValue("w", warehouseId);
            cmd.Parameters.AddWithValue("b", binId);
            cmd.Parameters.AddWithValue("qty", startingQty);
            cmd.Parameters.AddWithValue("balId", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_sales.customer (tenant_id, company_id, customer_id, customer_code, name, customer_type, currency_code, is_active) VALUES (@t, @c, @id, 'CUST-S1', 'Test Sales Cust', 'retail', 'PKR', true)", conn))
        {
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("c", companyId);
            cmd.Parameters.AddWithValue("id", customerId);
            await cmd.ExecuteNonQueryAsync();
        }

        if (seedFinanceAccounts)
        {
            var fyId = Guid.NewGuid();
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status) VALUES (@t, @c, @id, 'FY-S', '2026-01-01', '2026-12-31', 'open')", conn))
            {
                cmd.Parameters.AddWithValue("t", tenantId);
                cmd.Parameters.AddWithValue("c", companyId);
                cmd.Parameters.AddWithValue("id", fyId);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO finance.fiscal_periods (tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status) VALUES (@t, @c, @id, @fy, 1, 'P1', '2026-01-01', '2026-12-31', 'open')", conn))
            {
                cmd.Parameters.AddWithValue("t", tenantId);
                cmd.Parameters.AddWithValue("c", companyId);
                cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("fy", fyId);
                await cmd.ExecuteNonQueryAsync();
            }
            var accounts = new[]
            {
                ("1010", "Cash on Hand", "asset", "debit"),
                ("1200", "Merchandise Inventory Asset", "asset", "debit"),
                ("4010", "Sales Revenue", "revenue", "credit"),
                ("5010", "Cost of Goods Sold", "expense", "debit"),
            };
            foreach (var (code, name, type, normal) in accounts)
            {
                await using var acCmd = new NpgsqlCommand(
                    "INSERT INTO finance.accounts (tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active) VALUES (@t, @c, @id, @code, @name, @type, @normal, 'PKR', true, true, true)", conn);
                acCmd.Parameters.AddWithValue("t", tenantId);
                acCmd.Parameters.AddWithValue("c", companyId);
                acCmd.Parameters.AddWithValue("id", Guid.NewGuid());
                acCmd.Parameters.AddWithValue("code", code);
                acCmd.Parameters.AddWithValue("name", name);
                acCmd.Parameters.AddWithValue("type", type);
                acCmd.Parameters.AddWithValue("normal", normal);
                await acCmd.ExecuteNonQueryAsync();
            }
        }
    }

    private static async Task CleanupSalesTestHierarchyAsync(NpgsqlConnection conn, Guid tenantId)
    {
        await using (var resetJournals = new NpgsqlCommand(
            "UPDATE finance.journals SET status = 'draft' WHERE tenant_id = @t;", conn))
        {
            resetJournals.Parameters.AddWithValue("t", tenantId);
            await resetJournals.ExecuteNonQueryAsync();
        }

        var tables = new[]
        {
            "sync.change_log",
            "sync.company_change_counters",
            "erp_sales.outbox_event",
            "erp_inventory.outbox_event",
            "erp_inventory.command_idempotency",
            "finance.journal_lines",
            "finance.journals",
            "finance.accounts",
            "finance.fiscal_periods",
            "finance.fiscal_years",
            "erp_inventory.valuation_entry",
            "erp_inventory.inventory_cost_allocation",
            "erp_inventory.inventory_cost_layer",
            "erp_inventory.inventory_movement_leg",
            "erp_inventory.inventory_movement",
            "erp_inventory.stock_balance",
            "erp_sales.sales_invoice_line",
            "erp_sales.sales_invoice",
            "erp_sales.customer",
            "erp_inventory.stock_bin",
            "erp_inventory.warehouse",
            "erp_inventory.product_variant",
            "erp_inventory.product",
            "erp_inventory.stock_status",
            "erp_inventory.unit_of_measure",
            "organization.locations",
            "organization.companies"
        };
        foreach (var table in tables)
        {
            await using var checkCmd = new NpgsqlCommand($"SELECT to_regclass('{table}') IS NOT NULL;", conn);
            var exists = (bool)(await checkCmd.ExecuteScalarAsync())!;
            if (exists)
            {
                await using var cmd = new NpgsqlCommand($"DELETE FROM {table} WHERE tenant_id = @t;", conn);
                cmd.Parameters.AddWithValue("t", tenantId);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        await using var tCmd = new NpgsqlCommand("DELETE FROM platform.tenants WHERE id = @t;", conn);
        tCmd.Parameters.AddWithValue("t", tenantId);
        await tCmd.ExecuteNonQueryAsync();
    }
}
