using System.Data;
using System.Threading;
using Npgsql;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Sales;
using UniversalErp.Domain.Tenancy;
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
        if (!isLoopback || builder.Port != 5434 || builder.Database != "universal_erp_disposable_test" || builder.Username != expectedUser)
        {
            throw new InvalidOperationException(
                $"[FAIL-CLOSED] Connection string targets unapproved endpoint! Expected Host=127.0.0.1, Port=5434, Database=universal_erp_disposable_test, Username={expectedUser}. Got Host={builder.Host}, Port={builder.Port}, Database={builder.Database}, Username={builder.Username}. Accidental connections to production, staging, or development instances are strictly prevented.");
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
        var pass = Environment.GetEnvironmentVariable("PGPASSWORD_ERP_APP");
        if (!string.IsNullOrWhiteSpace(pass))
        {
            var cs = $"Host=127.0.0.1;Port=5434;Database=universal_erp_disposable_test;Username=erp_app;Password={pass};Include Error Detail=true";
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
        var pass = Environment.GetEnvironmentVariable("PGPASSWORD_ERP_DEV");
        if (!string.IsNullOrWhiteSpace(pass))
        {
            var cs = $"Host=127.0.0.1;Port=5434;Database=universal_erp_disposable_test;Username=erp_dev;Password={pass};Include Error Detail=true";
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
}
