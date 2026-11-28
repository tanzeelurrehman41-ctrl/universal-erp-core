using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using UniversalErp.Api.Security;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Finance;
using UniversalErp.Application.Foundation;
using UniversalErp.Application.Inventory;
using UniversalErp.Application.Purchasing;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Finance;
using UniversalErp.Infrastructure.Foundation;
using UniversalErp.Infrastructure.Inventory;
using UniversalErp.Infrastructure.Purchasing;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class AdminCurrencySettingsPostgresTests : IAsyncDisposable
{
    internal const string ApprovedDisposableDatabase = "universal_erp_current_disposable_test";
    internal const string ApprovedInternalServiceHost = "postgres-current-test";

    private readonly NpgsqlDataSource _appDataSource;
    private readonly NpgsqlDataSource _adminDataSource;
    private readonly string _appConnectionString;
    private readonly int _expectedPort;
    private readonly string _expectedDatabase;
    private readonly bool _isInternalRunner;

    public AdminCurrencySettingsPostgresTests()
    {
        var (appConnStr, adminConnStr, port, db, isInternalRunner) = ResolveApprovedConnectionStrings();
        _appConnectionString = appConnStr;
        _expectedPort = port;
        _expectedDatabase = db;
        _isInternalRunner = isInternalRunner;
        _appDataSource = NpgsqlDataSource.Create(appConnStr);
        _adminDataSource = NpgsqlDataSource.Create(adminConnStr);
    }

    public async ValueTask DisposeAsync()
    {
        await _appDataSource.DisposeAsync();
        await _adminDataSource.DisposeAsync();
    }

    private sealed class StaticTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current => context;
    }

    internal static bool IsApprovedCurrentDisposableDatabase(string? database) =>
        database is "universal_erp_current_disposable_test"
            or "universal_erp_current_disposable_verify"
            or "universal_erp_current_disposable_verify_047"
            or "universal_erp_current_disposable_verify_047b"
            or "universal_erp_current_disposable_verify_048";

    internal static bool IsVerifiedLinuxContainerRuntime() =>
        OperatingSystem.IsLinux() &&
        (string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
         || File.Exists("/.dockerenv"));

    private static bool IsInternalRunnerRequestedAndValid(Func<string, string?> getEnv, Func<bool> isContainerRuntime)
    {
        if (!isContainerRuntime())
        {
            return false;
        }

        return string.Equals(getEnv("UNIVERSAL_ERP_INTERNAL_CONTAINER_RUNNER")?.Trim(), "true", StringComparison.Ordinal)
            && string.Equals(getEnv("UNIVERSAL_ERP_TEST_TARGET_HOST")?.Trim(), ApprovedInternalServiceHost, StringComparison.Ordinal)
            && string.Equals(getEnv("UNIVERSAL_ERP_TEST_TARGET_PORT")?.Trim(), "5432", StringComparison.Ordinal)
            && IsApprovedCurrentDisposableDatabase(getEnv("UNIVERSAL_ERP_TEST_TARGET_DB")?.Trim());
    }

    internal static (string AppConnectionString, string AdminConnectionString, int ExpectedPort, string ExpectedDatabase, bool IsInternalRunner) ResolveApprovedConnectionStrings() =>
        ResolveApprovedConnectionStrings(Environment.GetEnvironmentVariable, IsVerifiedLinuxContainerRuntime);

    internal static (string AppConnectionString, string AdminConnectionString, int ExpectedPort, string ExpectedDatabase, bool IsInternalRunner) ResolveApprovedConnectionStrings(
        Func<string, string?> getEnv) =>
        ResolveApprovedConnectionStrings(getEnv, () => false);

    internal static (string AppConnectionString, string AdminConnectionString, int ExpectedPort, string ExpectedDatabase, bool IsInternalRunner) ResolveApprovedConnectionStrings(
        Func<string, string?> getEnv,
        Func<bool> isContainerRuntime)
    {
        ArgumentNullException.ThrowIfNull(getEnv);
        ArgumentNullException.ThrowIfNull(isContainerRuntime);

        var allowInternalRunner = IsInternalRunnerRequestedAndValid(getEnv, isContainerRuntime);
        var targetPortRaw = getEnv("UNIVERSAL_ERP_TEST_TARGET_PORT");
        int? explicitTargetPort = null;
        if (!string.IsNullOrWhiteSpace(targetPortRaw))
        {
            if (!int.TryParse(targetPortRaw.Trim(), out var parsedPort) ||
                parsedPort <= 0 ||
                parsedPort == 5433 ||
                (parsedPort == 5432 && !allowInternalRunner))
            {
                throw new InvalidOperationException(
                    $"[FAIL-CLOSED] UNIVERSAL_ERP_TEST_TARGET_PORT='{targetPortRaw}' is invalid or targets protected port 5432 (production) / 5433 (staging).");
            }
            explicitTargetPort = parsedPort;
        }

        var targetDbRaw = getEnv("UNIVERSAL_ERP_TEST_TARGET_DB")?.Trim();
        if (!string.IsNullOrWhiteSpace(targetDbRaw) && !IsApprovedCurrentDisposableDatabase(targetDbRaw))
        {
            throw new InvalidOperationException(
                $"[FAIL-CLOSED] UNIVERSAL_ERP_TEST_TARGET_DB='{targetDbRaw}' is rejected. Only approved current-schema disposable databases are allowed.");
        }

        var expectedHost = allowInternalRunner ? ApprovedInternalServiceHost : "127.0.0.1";
        var effectiveDb = !string.IsNullOrWhiteSpace(targetDbRaw) ? targetDbRaw : ApprovedDisposableDatabase;

        var appEnv = getEnv("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
            ?? getEnv("ConnectionStrings__UniversalErpTest");
        string appConnStr;
        if (!string.IsNullOrWhiteSpace(appEnv))
        {
            appConnStr = appEnv.Trim();
        }
        else
        {
            var appPwd = getEnv("PGPASSWORD_ERP_APP");
            if (explicitTargetPort is null || string.IsNullOrWhiteSpace(targetDbRaw) || string.IsNullOrWhiteSpace(appPwd))
            {
                throw new InvalidOperationException(
                    $"[FAIL-CLOSED] Explicit disposable test connection required. Configure UNIVERSAL_ERP_TEST_CONNECTION_STRING or set UNIVERSAL_ERP_TEST_TARGET_PORT, UNIVERSAL_ERP_TEST_TARGET_DB, and PGPASSWORD_ERP_APP.");
            }
            appConnStr = $"Host={expectedHost};Port={explicitTargetPort.Value};Database={effectiveDb};Username=erp_app;Password={appPwd};Include Error Detail=true";
        }

        var adminEnv = getEnv("UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING")
            ?? getEnv("ConnectionStrings__UniversalErpTestAdmin");
        string adminConnStr;
        if (!string.IsNullOrWhiteSpace(adminEnv))
        {
            adminConnStr = adminEnv.Trim();
        }
        else
        {
            var adminPwd = getEnv("PGPASSWORD_ERP_DEV");
            if (explicitTargetPort is null || string.IsNullOrWhiteSpace(targetDbRaw) || string.IsNullOrWhiteSpace(adminPwd))
            {
                throw new InvalidOperationException(
                    $"[FAIL-CLOSED] Explicit disposable admin connection required. Configure UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING or set UNIVERSAL_ERP_TEST_TARGET_PORT, UNIVERSAL_ERP_TEST_TARGET_DB, and PGPASSWORD_ERP_DEV.");
            }
            adminConnStr = $"Host={expectedHost};Port={explicitTargetPort.Value};Database={effectiveDb};Username=erp_dev;Password={adminPwd};Include Error Detail=true";
        }

        var appBuilder = ValidateApprovedConnectionString(
            appConnStr,
            expectedUser: "erp_app",
            expectedPort: explicitTargetPort,
            expectedDatabase: targetDbRaw,
            allowInternalRunner: allowInternalRunner);
        var adminBuilder = ValidateApprovedConnectionString(
            adminConnStr,
            expectedUser: "erp_dev",
            expectedPort: explicitTargetPort,
            expectedDatabase: targetDbRaw,
            allowInternalRunner: allowInternalRunner);

        if (appBuilder.Port != adminBuilder.Port ||
            !string.Equals(appBuilder.Host, adminBuilder.Host, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(appBuilder.Database, adminBuilder.Database, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"[FAIL-CLOSED] App connection ({appBuilder.Host}:{appBuilder.Port}/{appBuilder.Database}) and Admin connection ({adminBuilder.Host}:{adminBuilder.Port}/{adminBuilder.Database}) must target the exact same disposable container endpoint and database.");
        }

        return (appConnStr, adminConnStr, appBuilder.Port, appBuilder.Database!, allowInternalRunner);
    }

    internal static NpgsqlConnectionStringBuilder ValidateApprovedConnectionString(
        string? connectionString,
        string expectedUser,
        int? expectedPort = null,
        string? expectedDatabase = null,
        bool allowInternalRunner = false)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("[FAIL-CLOSED] Empty PostgreSQL test connection string is rejected.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var host = builder.Host?.Trim();
        if (allowInternalRunner)
        {
            if (!string.Equals(host, ApprovedInternalServiceHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"[FAIL-CLOSED] Verified internal runner mode strictly requires Host='{ApprovedInternalServiceHost}', got '{builder.Host}'. Loopback/staging/external hosts remain rejected.");
            }
            if (builder.Port != 5432)
            {
                throw new InvalidOperationException(
                    $"[FAIL-CLOSED] Verified internal runner mode strictly requires internal Port=5432, got {builder.Port}.");
            }
        }
        else
        {
            if (!string.Equals(host, "127.0.0.1", StringComparison.Ordinal) &&
                !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"[FAIL-CLOSED] Refusing non-loopback host '{builder.Host}'. Only 127.0.0.1 or localhost host disposable targets are permitted outside verified internal runner mode.");
            }

            if (builder.Port <= 0 || builder.Port is 5432 or 5433)
            {
                throw new InvalidOperationException(
                    $"[FAIL-CLOSED] Refusing PostgreSQL port {builder.Port}. Production (5432), staging (5433), and unspecified ports are forbidden on host connections.");
            }
        }

        if (expectedPort.HasValue && builder.Port != expectedPort.Value)
        {
            throw new InvalidOperationException(
                $"[FAIL-CLOSED] Port mismatch: expected disposable port {expectedPort.Value}, got {builder.Port}.");
        }

        var db = builder.Database?.Trim();
        if (!IsApprovedCurrentDisposableDatabase(db) ||
            (!string.IsNullOrWhiteSpace(expectedDatabase) && !string.Equals(db, expectedDatabase.Trim(), StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"[FAIL-CLOSED] Refusing target database '{builder.Database}'. Expected an approved current-schema disposable database.");
        }

        if (!string.Equals(builder.Username?.Trim(), expectedUser, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"[FAIL-CLOSED] Role mismatch: expected '{expectedUser}', got '{builder.Username}'.");
        }

        return builder;
    }

    private async Task AssertLiveTargetIdentityAsync(CancellationToken ct)
    {
        if (_isInternalRunner && !IsVerifiedLinuxContainerRuntime())
        {
            throw new InvalidOperationException("[FAIL-CLOSED] Internal runner mode attempted outside a verified Linux container runtime.");
        }

        await using (var adminConn = await _adminDataSource.OpenConnectionAsync(ct))
        await using (var cmd = new NpgsqlCommand(
            "SELECT current_database(), current_user, COALESCE(inet_server_port(), 0), COALESCE(host(inet_server_addr()), '');", adminConn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                throw new InvalidOperationException("[FAIL-CLOSED] Failed reading live admin PostgreSQL session identity.");
            }
            var db = reader.GetString(0);
            var user = reader.GetString(1);
            var serverPort = reader.GetInt32(2);
            var serverAddr = reader.GetString(3);
            var validInternal = _isInternalRunner && serverPort == 5432 && serverAddr != "127.0.0.1" && serverAddr != "::1";
            var validHost = !_isInternalRunner && serverPort is not (5432 or 5433) && (serverPort == 0 || serverPort == _expectedPort);
            if (!string.Equals(db, _expectedDatabase, StringComparison.Ordinal) ||
                !IsApprovedCurrentDisposableDatabase(db) ||
                !string.Equals(user, "erp_dev", StringComparison.Ordinal) ||
                (!validInternal && !validHost))
            {
                throw new InvalidOperationException(
                    $"[FAIL-CLOSED] Live admin target mismatch! db='{db}', user='{user}', server={serverAddr}:{serverPort}, internal={_isInternalRunner}, expected={_expectedDatabase}/erp_dev:{_expectedPort}.");
            }
        }

        await using (var appConn = await _appDataSource.OpenConnectionAsync(ct))
        await using (var cmd = new NpgsqlCommand(
            "SELECT current_database(), current_user, COALESCE(inet_server_port(), 0), COALESCE(host(inet_server_addr()), ''), (SELECT rolbypassrls FROM pg_roles WHERE rolname = current_user);",
            appConn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                throw new InvalidOperationException("[FAIL-CLOSED] Failed reading live application PostgreSQL session identity.");
            }
            var db = reader.GetString(0);
            var user = reader.GetString(1);
            var serverPort = reader.GetInt32(2);
            var serverAddr = reader.GetString(3);
            var bypassRls = reader.GetBoolean(4);
            var validInternal = _isInternalRunner && serverPort == 5432 && serverAddr != "127.0.0.1" && serverAddr != "::1";
            var validHost = !_isInternalRunner && serverPort is not (5432 or 5433) && (serverPort == 0 || serverPort == _expectedPort);
            if (!string.Equals(db, _expectedDatabase, StringComparison.Ordinal) ||
                !IsApprovedCurrentDisposableDatabase(db) ||
                !string.Equals(user, "erp_app", StringComparison.Ordinal) ||
                bypassRls ||
                (!validInternal && !validHost))
            {
                throw new InvalidOperationException(
                    $"[FAIL-CLOSED] Live app target/privilege mismatch! db='{db}', user='{user}', bypassrls={bypassRls}, server={serverAddr}:{serverPort}, internal={_isInternalRunner}.");
            }
        }
    }

    [Fact]
    public async Task EndToEnd_CurrencySettings_Ias21Translation_And_RlsIsolation_WorkInPostgreSql()
    {
        var ct = CancellationToken.None;
        var liveValidated = false;

        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var alphaCompanyId = Guid.NewGuid();
        var betaCompanyId = Guid.NewGuid();
        var gammaCompanyId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var viewerUserId = Guid.NewGuid();
        var gammaUserId = Guid.NewGuid();

        var fyId = Guid.NewGuid();
        var fpId = Guid.NewGuid();
        var cashGlId = Guid.NewGuid();
        var invGlId = Guid.NewGuid();
        var apGlId = Guid.NewGuid();
        var eqGlId = Guid.NewGuid();
        var revGlId = Guid.NewGuid();
        var expGlId = Guid.NewGuid();

        var uomId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var layerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var histInvJournalId = Guid.NewGuid();

        var betaUomId = Guid.NewGuid();
        var betaWhId = Guid.NewGuid();
        var betaSupplierId = Guid.NewGuid();

        try
        {
            await AssertLiveTargetIdentityAsync(ct);
            liveValidated = true;

            await using (var seedConn = await _adminDataSource.OpenConnectionAsync(ct))
        {
            await using var tx = await seedConn.BeginTransactionAsync(ct);

            await using (var setScope = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @tid, true), set_config('app.company_id', @cid, true);",
                seedConn, tx))
            {
                setScope.Parameters.AddWithValue("tid", tenantId.ToString());
                setScope.Parameters.AddWithValue("cid", alphaCompanyId.ToString());
                await setScope.ExecuteNonQueryAsync(ct);
            }

            // Tenants, Companies (Alpha = PKR/USD, Beta = USD/PKR), Users, Roles & Permissions
            var adminMembershipId = Guid.NewGuid();
            var viewerMembershipId = Guid.NewGuid();
            var adminRoleId = Guid.NewGuid();
            var viewerRoleId = Guid.NewGuid();

            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO platform.tenants (id, tenant_code, display_name, status, default_country_code, default_currency_code, default_timezone)
                VALUES
                    (@tid, @tcode, 'Currency E2E Tenant', 'active', 'PK', 'PKR', 'Asia/Karachi'),
                    (@otid, @otcode, 'Other Tenant', 'active', 'PK', 'PKR', 'Asia/Karachi');

                INSERT INTO organization.companies
                    (id, tenant_id, company_code, name, country_code, base_currency_code, functional_currency_code, presentation_currency_code, currency_effective_date, timezone, status)
                VALUES
                    (@cidAlpha, @tid, 'ALPHA-PKR', 'Alpha Logistics PKR', 'PK', 'PKR', 'PKR', 'USD', '2026-01-01', 'Asia/Karachi', 'active'),
                    (@cidBeta, @tid, 'BETA-USD', 'Beta Operations USD', 'US', 'USD', 'USD', 'PKR', '2026-01-01', 'UTC', 'active');

                INSERT INTO identity.users (id, email_normalized, display_name, status)
                VALUES
                    (@adminId, @adminEmail, 'Currency Admin', 'active'),
                    (@viewerId, @viewerEmail, 'Inventory Viewer', 'active');

                INSERT INTO identity.tenant_memberships (id, tenant_id, user_id, status)
                VALUES
                    (@adminMemId, @tid, @adminId, 'active'),
                    (@viewerMemId, @tid, @viewerId, 'active');

                INSERT INTO identity.company_memberships (tenant_id, company_id, user_id, status)
                VALUES
                    (@tid, @cidAlpha, @adminId, 'active'),
                    (@tid, @cidBeta, @adminId, 'active'),
                    (@tid, @cidAlpha, @viewerId, 'active');

                INSERT INTO "authorization".roles (id, tenant_id, role_code, name, is_system_role)
                VALUES
                    (@adminRoleId, @tid, 'tenant_admin', 'Tenant Administrator', true),
                    (@viewerRoleId, @tid, 'DEMO_INVENTORY_VIEWER', 'Inventory Viewer', false);

                INSERT INTO "authorization".membership_role_assignments (tenant_id, membership_id, role_id, assigned_by_user_id)
                VALUES
                    (@tid, @adminMemId, @adminRoleId, @adminId),
                    (@tid, @viewerMemId, @viewerRoleId, @adminId);

                INSERT INTO "authorization".role_permissions (tenant_id, role_id, permission_code, scope_type)
                SELECT @tid, @adminRoleId, p.permission_code, 'company'
                FROM "authorization".permissions p
                WHERE p.permission_code IN ('admin.currency_settings.view', 'admin.currency_settings.manage', 'finance.exchange_rate.manage')
                ON CONFLICT DO NOTHING;

                INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
                VALUES (@tid, @cidAlpha, @fyId, 'FY2026', '2026-01-01', '2026-12-31', 'open');

                INSERT INTO finance.fiscal_periods (tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status)
                VALUES (@tid, @cidAlpha, @fpId, @fyId, 11, 'November 2026', '2026-11-01', '2026-11-30', 'open');

                INSERT INTO finance.accounts (tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active)
                VALUES
                    (@tid, @cidAlpha, @cashGlId, '1010', 'Cash', 'asset', 'debit', 'PKR', false, true, true),
                    (@tid, @cidAlpha, @invGlId, '1200', 'Inventory Asset', 'asset', 'debit', 'PKR', true, false, true),
                    (@tid, @cidAlpha, @apGlId, '2010', 'Accounts Payable', 'liability', 'credit', 'PKR', true, false, true),
                    (@tid, @cidAlpha, @eqGlId, '3010', 'Owner Equity', 'equity', 'credit', 'PKR', false, true, true),
                    (@tid, @cidAlpha, @revGlId, '4010', 'Sales Revenue', 'revenue', 'credit', 'PKR', false, true, true),
                    (@tid, @cidAlpha, @expGlId, '6010', 'Operating Expense', 'expense', 'debit', 'PKR', false, true, true);
                """,
                seedConn, tx))
            {
                cmd.Parameters.AddWithValue("tid", tenantId);
                cmd.Parameters.AddWithValue("otid", otherTenantId);
                cmd.Parameters.AddWithValue("tcode", $"T-{tenantId:N}"[..16]);
                cmd.Parameters.AddWithValue("otcode", $"OT-{otherTenantId:N}"[..16]);
                cmd.Parameters.AddWithValue("cidAlpha", alphaCompanyId);
                cmd.Parameters.AddWithValue("cidBeta", betaCompanyId);
                cmd.Parameters.AddWithValue("adminId", adminUserId);
                cmd.Parameters.AddWithValue("viewerId", viewerUserId);
                cmd.Parameters.AddWithValue("adminEmail", $"admin-{adminUserId:N}@example.test");
                cmd.Parameters.AddWithValue("viewerEmail", $"viewer-{viewerUserId:N}@example.test");
                cmd.Parameters.AddWithValue("adminMemId", adminMembershipId);
                cmd.Parameters.AddWithValue("viewerMemId", viewerMembershipId);
                cmd.Parameters.AddWithValue("adminRoleId", adminRoleId);
                cmd.Parameters.AddWithValue("viewerRoleId", viewerRoleId);
                cmd.Parameters.AddWithValue("fyId", fyId);
                cmd.Parameters.AddWithValue("fpId", fpId);
                cmd.Parameters.AddWithValue("cashGlId", cashGlId);
                cmd.Parameters.AddWithValue("invGlId", invGlId);
                cmd.Parameters.AddWithValue("apGlId", apGlId);
                cmd.Parameters.AddWithValue("eqGlId", eqGlId);
                cmd.Parameters.AddWithValue("revGlId", revGlId);
                cmd.Parameters.AddWithValue("expGlId", expGlId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Seed Gamma in otherTenantId
            await using (var setOther = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @tid, true), set_config('app.company_id', @cid, true);",
                seedConn, tx))
            {
                setOther.Parameters.AddWithValue("tid", otherTenantId.ToString());
                setOther.Parameters.AddWithValue("cid", gammaCompanyId.ToString());
                await setOther.ExecuteNonQueryAsync(ct);
            }

            var gammaMembershipId = Guid.NewGuid();
            var gammaRoleId = Guid.NewGuid();

            await using (var gammaCmd = new NpgsqlCommand(
                """
                INSERT INTO organization.companies
                    (id, tenant_id, company_code, name, country_code, base_currency_code, functional_currency_code, presentation_currency_code, currency_effective_date, timezone, status)
                VALUES
                    (@cidGamma, @otid, 'GAMMA-PKR', 'Gamma Other Tenant', 'PK', 'PKR', 'PKR', 'USD', '2026-01-01', 'Asia/Karachi', 'active');

                INSERT INTO identity.users (id, email_normalized, display_name, status)
                VALUES (@gammaId, @gammaEmail, 'Gamma Admin', 'active');

                INSERT INTO identity.tenant_memberships (id, tenant_id, user_id, status)
                VALUES (@gammaMemId, @otid, @gammaId, 'active');

                INSERT INTO identity.company_memberships (tenant_id, company_id, user_id, status)
                VALUES (@otid, @cidGamma, @gammaId, 'active');

                INSERT INTO "authorization".roles (id, tenant_id, role_code, name, is_system_role)
                VALUES (@gammaRoleId, @otid, 'tenant_admin', 'Tenant Administrator', true);

                INSERT INTO "authorization".membership_role_assignments (tenant_id, membership_id, role_id, assigned_by_user_id)
                VALUES (@otid, @gammaMemId, @gammaRoleId, @gammaId);

                INSERT INTO "authorization".role_permissions (tenant_id, role_id, permission_code, scope_type)
                SELECT @otid, @gammaRoleId, p.permission_code, 'company'
                FROM "authorization".permissions p
                WHERE p.permission_code IN ('admin.currency_settings.view', 'admin.currency_settings.manage', 'finance.exchange_rate.manage')
                ON CONFLICT DO NOTHING;
                """,
                seedConn, tx))
            {
                gammaCmd.Parameters.AddWithValue("cidGamma", gammaCompanyId);
                gammaCmd.Parameters.AddWithValue("otid", otherTenantId);
                gammaCmd.Parameters.AddWithValue("gammaId", gammaUserId);
                gammaCmd.Parameters.AddWithValue("gammaEmail", $"gamma-{gammaUserId:N}@example.test");
                gammaCmd.Parameters.AddWithValue("gammaMemId", gammaMembershipId);
                gammaCmd.Parameters.AddWithValue("gammaRoleId", gammaRoleId);
                await gammaCmd.ExecuteNonQueryAsync(ct);
            }

            // Switch back to Alpha to seed 50 EA / PKR 2,250 FIFO layer + historical PKR journals
            await using (var setAlpha = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @tid, true), set_config('app.company_id', @cid, true);",
                seedConn, tx))
            {
                setAlpha.Parameters.AddWithValue("tid", tenantId.ToString());
                setAlpha.Parameters.AddWithValue("cid", alphaCompanyId.ToString());
                await setAlpha.ExecuteNonQueryAsync(ct);
            }

            await using (var invCmd = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.stock_status (tenant_id, status_code, description, is_reservable, is_pickable)
                VALUES (@tid, 'available', 'Available', true, true)
                ON CONFLICT DO NOTHING;

                INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension)
                VALUES (@tid, @uomId, 'EA', 'Each', 'count');

                INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, product_type)
                VALUES (@tid, @prodId, 'SKU-50EA', 'Legacy Batch Item', 'stock');

                INSERT INTO erp_inventory.product_variant (tenant_id, variant_id, product_id, sku, variant_name, base_uom_id)
                VALUES (@tid, @varId, @prodId, 'SKU-50EA-V1', 'Legacy Batch Item', @uomId);

                INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name, is_active)
                VALUES (@tid, @cidAlpha, @whId, 'MAIN-W1', 'Main Warehouse', true);

                INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, bin_id, warehouse_id, code, bin_type, is_active)
                VALUES (@tid, @cidAlpha, @binId, @whId, 'PICK-01', 'storage', true);

                INSERT INTO erp_inventory.stock_balance (tenant_id, company_id, balance_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty)
                VALUES (@tid, @cidAlpha, @balId, @varId, @whId, @binId, 'available', 50);

                INSERT INTO erp_inventory.inventory_cost_layer
                    (tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id, status_code,
                     source_document_type, received_at, original_qty, remaining_qty, unit_cost, currency_code)
                VALUES
                    (@tid, @cidAlpha, @layerId, @varId, @whId, @binId, 'available',
                     'opening_balance', '2026-03-15T10:00:00Z', 50, 50, 45, 'PKR');
                """,
                seedConn, tx))
            {
                invCmd.Parameters.AddWithValue("tid", tenantId);
                invCmd.Parameters.AddWithValue("cidAlpha", alphaCompanyId);
                invCmd.Parameters.AddWithValue("uomId", uomId);
                invCmd.Parameters.AddWithValue("prodId", prodId);
                invCmd.Parameters.AddWithValue("varId", variantId);
                invCmd.Parameters.AddWithValue("whId", warehouseId);
                invCmd.Parameters.AddWithValue("binId", binId);
                invCmd.Parameters.AddWithValue("balId", Guid.NewGuid());
                invCmd.Parameters.AddWithValue("layerId", layerId);
                await invCmd.ExecuteNonQueryAsync(ct);
            }

            // Historical PKR journals: Equity Jan 10 (5000), Inventory/AP Mar 15 (2250), Cash Sale/Rent Jun 20 (3000/1000)
            var j1 = Guid.NewGuid();
            var j2 = histInvJournalId;
            var j3 = Guid.NewGuid();
            await using (var jCmd = new NpgsqlCommand(
                """
                INSERT INTO finance.journals
                    (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id, description, functional_currency, source_module, source_type, source_id, idempotency_key, status, created_by, posted_by, posted_at)
                VALUES
                    (@j1, @tid, @cidAlpha, 'JRN-HIST-EQ', '2026-01-10', @fpId, 'Equity Injection', 'PKR', 'manual', 'general', 'hist-1', 'hist-eq-1', 'posted', @adminId, @adminId, '2026-01-10T10:00:00Z'),
                    (@j2, @tid, @cidAlpha, 'JRN-HIST-INV', '2026-03-15', @fpId, 'Inventory 50 EA @ 45', 'PKR', 'purchasing', 'grn', 'hist-2', 'hist-inv-2', 'posted', @adminId, @adminId, '2026-03-15T10:00:00Z'),
                    (@j3, @tid, @cidAlpha, 'JRN-HIST-PNL', '2026-06-20', @fpId, 'Service Sale & Expense', 'PKR', 'manual', 'general', 'hist-3', 'hist-pnl-3', 'posted', @adminId, @adminId, '2026-06-20T10:00:00Z');

                INSERT INTO finance.journal_lines
                    (tenant_id, company_id, journal_id, line_no, account_id, description, transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
                VALUES
                    (@tid, @cidAlpha, @j1, 1, @cashGlId, 'Cash from Owner', 'PKR', 5000, 1.0, 5000, 0),
                    (@tid, @cidAlpha, @j1, 2, @eqGlId, 'Owner Capital', 'PKR', 5000, 1.0, 0, 5000),
                    (@tid, @cidAlpha, @j2, 1, @invGlId, 'Inventory 50 EA', 'PKR', 2250, 1.0, 2250, 0),
                    (@tid, @cidAlpha, @j2, 2, @apGlId, 'Payable', 'PKR', 2250, 1.0, 0, 2250),
                    (@tid, @cidAlpha, @j3, 1, @cashGlId, 'Service Cash Inflow', 'PKR', 2000, 1.0, 2000, 0),
                    (@tid, @cidAlpha, @j3, 2, @expGlId, 'Rent Expense', 'PKR', 1000, 1.0, 1000, 0),
                    (@tid, @cidAlpha, @j3, 3, @revGlId, 'Service Revenue', 'PKR', 3000, 1.0, 0, 3000);
                """,
                seedConn, tx))
            {
                jCmd.Parameters.AddWithValue("tid", tenantId);
                jCmd.Parameters.AddWithValue("cidAlpha", alphaCompanyId);
                jCmd.Parameters.AddWithValue("fpId", fpId);
                jCmd.Parameters.AddWithValue("adminId", adminUserId);
                jCmd.Parameters.AddWithValue("j1", j1);
                jCmd.Parameters.AddWithValue("j2", j2);
                jCmd.Parameters.AddWithValue("j3", j3);
                jCmd.Parameters.AddWithValue("cashGlId", cashGlId);
                jCmd.Parameters.AddWithValue("invGlId", invGlId);
                jCmd.Parameters.AddWithValue("apGlId", apGlId);
                jCmd.Parameters.AddWithValue("eqGlId", eqGlId);
                jCmd.Parameters.AddWithValue("revGlId", revGlId);
                jCmd.Parameters.AddWithValue("expGlId", expGlId);
                await jCmd.ExecuteNonQueryAsync(ct);
            }

            // Beta Warehouse + Supplier + Accounts for dynamic default PO/Journal test
            await using (var setBeta = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', @tid, true), set_config('app.company_id', @cid, true);",
                seedConn, tx))
            {
                setBeta.Parameters.AddWithValue("tid", tenantId.ToString());
                setBeta.Parameters.AddWithValue("cid", betaCompanyId.ToString());
                await setBeta.ExecuteNonQueryAsync(ct);
            }

            await using (var betaCmd = new NpgsqlCommand(
                """
                INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name, is_active)
                VALUES (@tid, @cidBeta, @whId, 'US-WH1', 'US Warehouse', true);

                INSERT INTO erp_purchasing.supplier (tenant_id, company_id, supplier_id, supplier_code, name, is_active)
                VALUES (@tid, @cidBeta, @betaSupId, 'SUP-BETA', 'US Vendor Corp', true);
                """,
                seedConn, tx))
            {
                betaCmd.Parameters.AddWithValue("tid", tenantId);
                betaCmd.Parameters.AddWithValue("cidBeta", betaCompanyId);
                betaCmd.Parameters.AddWithValue("whId", betaWhId);
                betaCmd.Parameters.AddWithValue("betaSupId", betaSupplierId);
                await betaCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }

        // Unused helper placeholders eliminated cleanly
        _ = betaUomId;
        _ = supplierId;

        var alphaCtx = new TenantContext(tenantId, alphaCompanyId, adminUserId);
        var betaCtx = new TenantContext(tenantId, betaCompanyId, adminUserId);
        var gammaCtx = new TenantContext(otherTenantId, gammaCompanyId, gammaUserId);

        var foundationRepo = new PostgresFoundationRepository(_appDataSource);
        var foundationSvc = new FoundationService(foundationRepo);
        var financeRepo = new PostgresFinanceRepository(_appDataSource);

        // 1. Verify Admin Currency Settings, Authorized Company Settings Update, & DB Permissions
        var curSettings = await foundationSvc.GetCurrencySettingsAsync(alphaCtx, ct);
        Assert.NotNull(curSettings);
        Assert.Equal("PKR", curSettings!.FunctionalCurrencyCode);
        Assert.Equal("USD", curSettings.PresentationCurrencyCode);
        Assert.True(curSettings.HasPostedTransactions);

        var alphaSettingsBefore = await foundationSvc.GetSettingsAsync(alphaCtx, ct);
        Assert.NotNull(alphaSettingsBefore);
        var updatedAlphaSettings = await foundationSvc.UpdateSettingsAsync(
            alphaCtx,
            new UpdateCompanySettingsCommand("Alpha Logistics PKR", "Alpha Legal Pvt Ltd", "REG-ALPHA-01", "Asia/Karachi"),
            ct);
        Assert.Equal("Alpha Legal Pvt Ltd", updatedAlphaSettings.LegalName);
        Assert.Equal("REG-ALPHA-01", updatedAlphaSettings.RegistrationNumber);
        var alphaSettingsAfter = await foundationSvc.GetSettingsAsync(alphaCtx, ct);
        Assert.NotNull(alphaSettingsAfter);
        Assert.Equal("Alpha Legal Pvt Ltd", alphaSettingsAfter!.LegalName);

        await using (var permConn = await _appDataSource.OpenConnectionAsync(ct))
        {
            async Task<bool> HasPerm(Guid uid, string code)
            {
                await using var pCmd = new NpgsqlCommand(
                    "SELECT identity.user_has_permission(@t, @c, @u, @p);", permConn);
                pCmd.Parameters.AddWithValue("t", tenantId);
                pCmd.Parameters.AddWithValue("c", alphaCompanyId);
                pCmd.Parameters.AddWithValue("u", uid);
                pCmd.Parameters.AddWithValue("p", code);
                return (bool)(await pCmd.ExecuteScalarAsync(ct) ?? false);
            }

            Assert.True(await HasPerm(adminUserId, "admin.currency_settings.view"));
            Assert.True(await HasPerm(adminUserId, "admin.currency_settings.manage"));
            Assert.True(await HasPerm(adminUserId, "finance.exchange_rate.manage"));
            Assert.False(await HasPerm(viewerUserId, "admin.currency_settings.view"));
            Assert.False(await HasPerm(viewerUserId, "admin.currency_settings.manage"));
            Assert.False(await HasPerm(viewerUserId, "finance.exchange_rate.manage"));

            // Verify ASP.NET Core DatabasePermissionAuthorizationHandler enforcement
            var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", adminUserId.ToString())], "Bearer"));
            var viewerPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", viewerUserId.ToString())], "Bearer"));

            var adminHandler = new DatabasePermissionAuthorizationHandler(_appDataSource, new StaticTenantContextAccessor(alphaCtx));
            var adminReq = new DatabasePermissionRequirement("admin.currency_settings.manage");
            var adminAuthCtx = new AuthorizationHandlerContext([adminReq], adminPrincipal, null);
            await adminHandler.HandleAsync(adminAuthCtx);
            Assert.True(adminAuthCtx.HasSucceeded);

            var viewerHandler = new DatabasePermissionAuthorizationHandler(
                _appDataSource,
                new StaticTenantContextAccessor(new TenantContext(tenantId, alphaCompanyId, viewerUserId)));
            var viewerReq = new DatabasePermissionRequirement("finance.exchange_rate.manage");
            var viewerAuthCtx = new AuthorizationHandlerContext([viewerReq], viewerPrincipal, null);
            await viewerHandler.HandleAsync(viewerAuthCtx);
            Assert.False(viewerAuthCtx.HasSucceeded);

            // Verify erp_app least-privilege matrix & Forced RLS policies in PostgreSQL
            await using var grantCmd = new NpgsqlCommand(
                """
                SELECT privilege_type
                FROM information_schema.role_table_grants
                WHERE grantee = 'erp_app' AND table_schema = 'organization' AND table_name = 'company_currency_history';
                """, permConn);
            var privs = new List<string>();
            await using (var gr = await grantCmd.ExecuteReaderAsync(ct))
            {
                while (await gr.ReadAsync(ct)) privs.Add(gr.GetString(0));
            }
            Assert.Contains("SELECT", privs);
            Assert.Contains("INSERT", privs);
            Assert.DoesNotContain("UPDATE", privs);
            Assert.DoesNotContain("DELETE", privs);
        }

        // 2. Without exchange rates -> Functional PKR 2,250 reconciled; USD Presentation Unavailable
        var invRepoAlpha = new PostgresInventoryWorkflowRepository(_appDataSource);
        var invSvcAlpha = new InventoryWorkflowService(new StaticTenantContextAccessor(alphaCtx), invRepoAlpha);
        var valNoRates = await invSvcAlpha.GetInventoryValuationReportAsync(ct);
        Assert.Equal("PKR", valNoRates.FunctionalCurrencyCode);
        Assert.Equal(50m, valNoRates.TotalPhysicalStock);
        Assert.Equal(2250m, valNoRates.FunctionalValuation);
        Assert.Equal(2250m, valNoRates.GeneralLedger1200FunctionalBalance);
        Assert.True(valNoRates.IsReconciled);
        Assert.Equal("USD", valNoRates.PresentationCurrencyCode);
        Assert.Equal("MISSING_CLOSING_EXCHANGE_RATE", valNoRates.PresentationStatus);
        Assert.Null(valNoRates.PresentationValuation);

        var asOfToday = DateOnly.FromDateTime(DateTime.UtcNow);
        var alphaFinanceSvc = new FinanceService(financeRepo, new StaticTenantContextAccessor(alphaCtx));
        var stmtNoRates = await alphaFinanceSvc.GetPresentationFinancialStatementsAsync(asOfToday, "transaction", ct);
        Assert.StartsWith("UNAVAILABLE", stmtNoRates.TranslationStatus);
        Assert.NotEmpty(stmtNoRates.MissingRates);

        // 3. Add Exchange Rates + Verify Duplicate & Inverse Ambiguity Rejection
        var rEq = await alphaFinanceSvc.CreateExchangeRateAsync(
            new UpsertExchangeRateRequest("USD", "PKR", 250m, new DateOnly(2026, 1, 1), "transaction", "SBP", "EQ-JAN"), ct);
        Assert.True(rEq.IsSuccess);

        var rPnl = await alphaFinanceSvc.CreateExchangeRateAsync(
            new UpsertExchangeRateRequest("USD", "PKR", 270m, new DateOnly(2026, 6, 1), "transaction", "SBP", "PNL-JUN"), ct);
        Assert.True(rPnl.IsSuccess);

        var rClose = await alphaFinanceSvc.CreateExchangeRateAsync(
            new UpsertExchangeRateRequest("USD", "PKR", 280m, asOfToday, "closing", "SBP", "CLS-NOV"), ct);
        Assert.True(rClose.IsSuccess);

        var dupAttempt = await alphaFinanceSvc.CreateExchangeRateAsync(
            new UpsertExchangeRateRequest("USD", "PKR", 281m, asOfToday, "closing", "SBP", "DUP"), ct);
        Assert.False(dupAttempt.IsSuccess);
        Assert.Equal("DUPLICATE_EXCHANGE_RATE", dupAttempt.Error);

        var invAttempt = await alphaFinanceSvc.CreateExchangeRateAsync(
            new UpsertExchangeRateRequest("PKR", "USD", 0.00357m, asOfToday, "closing", "SBP", "INV"), ct);
        Assert.False(invAttempt.IsSuccess);
        Assert.Equal("AMBIGUOUS_INVERSE_EXCHANGE_RATE", invAttempt.Error);

        // Update closing rate (next day so CLS-NOV=280 remains effective asOfToday) & verify audit log
        var updClose = await alphaFinanceSvc.CreateExchangeRateAsync(
            new UpsertExchangeRateRequest("USD", "PKR", 278.5m, asOfToday.AddDays(1), "closing", "SBP", "ADJ"), ct);
        Assert.True(updClose.IsSuccess);
        var updatedRate = await alphaFinanceSvc.UpdateExchangeRateAsync(
            updClose.Value!.ExchangeRateId,
            new UpsertExchangeRateRequest("USD", "PKR", 280m, asOfToday.AddDays(1), "closing", "SBP-REV", "ADJ-REV"), ct);
        Assert.True(updatedRate.IsSuccess);
        var auditLogs = await financeRepo.ListAuditLogsAsync(tenantId, alphaCompanyId, 20, ct);
        Assert.Contains(auditLogs, l => l.EventType == "EXCHANGE_RATE_UPDATED");

        // 4. With Closing & Transaction Rates -> Inventory Valuation & IAS 21 Statements balance cleanly in USD
        var valTranslated = await invSvcAlpha.GetInventoryValuationReportAsync(ct);
        Assert.Equal(2250m, valTranslated.FunctionalValuation);
        Assert.Equal("TRANSLATED_CLOSING_RATE", valTranslated.PresentationStatus);
        Assert.Equal(Math.Round(2250m / 280m, 2, MidpointRounding.AwayFromZero), valTranslated.PresentationValuation);
        Assert.Equal(valTranslated.PresentationValuation, valTranslated.GeneralLedger1200PresentationBalance);

        var stmtTranslated = await alphaFinanceSvc.GetPresentationFinancialStatementsAsync(asOfToday, "transaction", ct);
        Assert.Equal("TRANSLATED", stmtTranslated.TranslationStatus);
        Assert.Equal(Math.Round(1m / 280m, 10, MidpointRounding.AwayFromZero), stmtTranslated.ClosingRate);
        Assert.True(stmtTranslated.IsBalanceSheetBalancedPresentation);
        Assert.Equal(0m, stmtTranslated.BalanceSheetDiscrepancyPresentation);
        Assert.NotNull(stmtTranslated.TranslationDifferenceOciPresentation);

        // 5. Cross-Company (Beta) & Cross-Tenant (Gamma) Isolation on Exchange Rates & Currency History
        var betaFinanceSvc = new FinanceService(financeRepo, new StaticTenantContextAccessor(betaCtx));
        var gammaFinanceSvc = new FinanceService(financeRepo, new StaticTenantContextAccessor(gammaCtx));
        Assert.Empty(await betaFinanceSvc.ListExchangeRatesAsync(null, null, null, ct));
        Assert.Empty(await gammaFinanceSvc.ListExchangeRatesAsync(null, null, null, ct));

        // 6. Prospective Functional Currency Update Preserves Historical PKR Records & Drives Future Transactions
        var noConfirm = await foundationSvc.UpdateCurrencySettingsAsync(alphaCtx, new UpdateCompanyCurrencySettingsCommand(
            "USD", "PKR", new DateOnly(2026, 12, 1), false, "No confirm test"), ct);
        Assert.False(noConfirm.IsSuccess);
        Assert.Equal("CONFIRMATION_REQUIRED_FOR_FUTURE_TRANSACTIONS", noConfirm.Error);

        var confirmedChange = await foundationSvc.UpdateCurrencySettingsAsync(alphaCtx, new UpdateCompanyCurrencySettingsCommand(
            "USD", "PKR", new DateOnly(2026, 12, 1), true, "Prospective USD switch"), ct);
        Assert.True(confirmedChange.IsSuccess);

        // Seed real currency history records in Beta and Gamma for non-vacuous cross-company / cross-tenant isolation
        var betaPresChange = await foundationSvc.UpdateCurrencySettingsAsync(betaCtx, new UpdateCompanyCurrencySettingsCommand(
            "USD", "EUR", new DateOnly(2026, 12, 1), false, "Beta reporting switch"), ct);
        Assert.True(betaPresChange.IsSuccess);

        var gammaPresChange = await foundationSvc.UpdateCurrencySettingsAsync(gammaCtx, new UpdateCompanyCurrencySettingsCommand(
            "PKR", "GBP", new DateOnly(2026, 12, 1), false, "Gamma reporting switch"), ct);
        Assert.True(gammaPresChange.IsSuccess);

        var history = await foundationSvc.ListCurrencyHistoryAsync(alphaCtx, 20, ct);
        Assert.Single(history);
        Assert.Equal("PKR", history[0].PreviousFunctionalCurrency);
        Assert.Equal("USD", history[0].NewFunctionalCurrency);
        Assert.Equal("Prospective USD switch", history[0].ChangeReason);

        var betaHistory = await foundationSvc.ListCurrencyHistoryAsync(betaCtx, 20, ct);
        Assert.Single(betaHistory);
        Assert.Equal("EUR", betaHistory[0].NewPresentationCurrency);
        Assert.Equal("Beta reporting switch", betaHistory[0].ChangeReason);

        var gammaHistory = await foundationSvc.ListCurrencyHistoryAsync(gammaCtx, 20, ct);
        Assert.Single(gammaHistory);
        Assert.Equal("GBP", gammaHistory[0].NewPresentationCurrency);
        Assert.Equal("Gamma reporting switch", gammaHistory[0].ChangeReason);

        await VerifyHardenedRlsAndPoolIsolationAsync(
            tenantId,
            otherTenantId,
            alphaCompanyId,
            betaCompanyId,
            gammaCompanyId,
            adminUserId,
            viewerUserId,
            gammaUserId,
            asOfToday,
            foundationSvc,
            ct);

        // Verify historical layer & historical journal still store PKR untouched under erp_app + FORCE RLS
        await using (var chkConn = await _appDataSource.OpenConnectionAsync(ct))
        {
            await using var chkTx = await chkConn.BeginTransactionAsync(ct);
            await using (var s = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true), set_config('app.company_id', @c, true);", chkConn, chkTx))
            {
                s.Parameters.AddWithValue("t", tenantId.ToString());
                s.Parameters.AddWithValue("c", alphaCompanyId.ToString());
                await s.ExecuteNonQueryAsync(ct);
            }

            await using var lCmd = new NpgsqlCommand("SELECT currency_code, remaining_qty * unit_cost FROM erp_inventory.inventory_cost_layer WHERE layer_id = @lid;", chkConn, chkTx);
            lCmd.Parameters.AddWithValue("lid", layerId);
            await using var lr = await lCmd.ExecuteReaderAsync(ct);
            Assert.True(await lr.ReadAsync(ct));
            Assert.Equal("PKR", lr.GetString(0));
            Assert.Equal(2250m, lr.GetDecimal(1));
            await lr.CloseAsync();

            await using var jCmd = new NpgsqlCommand("SELECT functional_currency FROM finance.journals WHERE id = @jid;", chkConn, chkTx);
            jCmd.Parameters.AddWithValue("jid", histInvJournalId);
            Assert.Equal("PKR", (string)(await jCmd.ExecuteScalarAsync(ct))!);

            // Cross-tenant RLS probe under NOBYPASSRLS erp_app: Gamma context must see zero Alpha layers
            await using (var sGamma = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true), set_config('app.company_id', @c, true);", chkConn, chkTx))
            {
                sGamma.Parameters.AddWithValue("t", otherTenantId.ToString());
                sGamma.Parameters.AddWithValue("c", gammaCompanyId.ToString());
                await sGamma.ExecuteNonQueryAsync(ct);
            }
            await using var lGammaCmd = new NpgsqlCommand("SELECT layer_id FROM erp_inventory.inventory_cost_layer WHERE layer_id = @lid;", chkConn, chkTx);
            lGammaCmd.Parameters.AddWithValue("lid", layerId);
            Assert.Null(await lGammaCmd.ExecuteScalarAsync(ct));
        }

        // Verify NEW transactions in Beta (Functional = USD) default dynamically to USD instead of 'PKR'
        var poRepo = new PostgresPurchasingRepository(_appDataSource, new StaticTenantContextAccessor(betaCtx));
        var poId = await poRepo.CreatePurchaseOrderAsync(
            new CreatePurchaseOrderRequest(
                PoNumber: $"PO-USD-{Guid.NewGuid():N}"[..14],
                SupplierId: betaSupplierId,
                WarehouseId: betaWhId,
                OrderDate: new DateOnly(2026, 11, 26),
                ExpectedDate: null,
                Note: "Dynamic USD default check",
                Lines: []),
            adminUserId,
            ct);
        var createdPo = await poRepo.GetPurchaseOrderAsync(poId, ct);
        Assert.NotNull(createdPo);
        Assert.Equal("USD", createdPo!.CurrencyCode);

        // Verify NEW journal in Alpha (after prospective change to USD) defaults to USD
        var newJrnRes = await alphaFinanceSvc.PostJournalAsync(
            new PostJournalRequest(
                JournalNo: $"JRN-NEW-{Guid.NewGuid():N}"[..14],
                JournalDate: new DateOnly(2026, 12, 2),
                FiscalPeriodId: fpId,
                Description: "Prospective USD posting",
                FunctionalCurrency: "",
                SourceModule: "manual",
                SourceType: "general",
                SourceId: "prospective-1",
                Lines:
                [
                    new(cashGlId, "Cash USD", "USD", 10m, 1m, 10m, 0m),
                    new(eqGlId, "Equity USD", "USD", 10m, 1m, 0m, 10m),
                ]),
            Guid.NewGuid(),
            ct);
        Assert.True(newJrnRes.IsSuccess);
        Assert.Equal("USD", newJrnRes.Value!.FunctionalCurrency);
        }
        finally
        {
            if (liveValidated)
            {
                await CleanupFixturesAsync(tenantId, otherTenantId, adminUserId, viewerUserId, gammaUserId);
            }
        }
    }

    private async Task VerifyHardenedRlsAndPoolIsolationAsync(
        Guid tenantId,
        Guid otherTenantId,
        Guid alphaCompanyId,
        Guid betaCompanyId,
        Guid gammaCompanyId,
        Guid adminUserId,
        Guid viewerUserId,
        Guid gammaUserId,
        DateOnly asOfToday,
        FoundationService foundationSvc,
        CancellationToken ct)
    {
        async Task<T> RunInAppScopeAsync<T>(Guid? scopeTenantId, Guid? scopeCompanyId, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action)
        {
            await using var conn = await _appDataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            if (scopeTenantId.HasValue || scopeCompanyId.HasValue)
            {
                await using var scopeCmd = new NpgsqlCommand(
                    "SELECT set_config('app.tenant_id', @tid, true), set_config('app.company_id', @cid, true);",
                    conn, tx);
                scopeCmd.Parameters.AddWithValue("tid", scopeTenantId?.ToString() ?? string.Empty);
                scopeCmd.Parameters.AddWithValue("cid", scopeCompanyId?.ToString() ?? string.Empty);
                await scopeCmd.ExecuteNonQueryAsync(ct);
            }
            return await action(conn, tx);
        }

        async Task<PostgresException> AssertPrivilegeFailureAsync(
            Guid? scopeTenantId,
            Guid? scopeCompanyId,
            Func<NpgsqlConnection, NpgsqlTransaction, Task> action)
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => RunInAppScopeAsync<object?>(
                scopeTenantId,
                scopeCompanyId,
                async (conn, tx) =>
                {
                    await action(conn, tx);
                    return null;
                }));
            Assert.Equal("42501", ex.SqlState);
            return ex;
        }

        static async Task<object?> QueryGetCompanySettingsScalarAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tid, Guid cid)
        {
            await using var cmd = new NpgsqlCommand("SELECT identity.get_company_settings(@tid, @cid);", conn, tx);
            cmd.Parameters.AddWithValue("tid", tid);
            cmd.Parameters.AddWithValue("cid", cid);
            return await cmd.ExecuteScalarAsync();
        }

        static async Task<long> QueryHistoryCountAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tid, Guid cid)
        {
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM identity.list_company_currency_history(@tid, @cid, 50);", conn, tx);
            cmd.Parameters.AddWithValue("tid", tid);
            cmd.Parameters.AddWithValue("cid", cid);
            return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        }

        static async Task ExecuteUpdateCompanySettingsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tid, Guid cid, string name)
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT identity.update_company_settings(@tid, @cid, @name, NULL, NULL, NULL);",
                conn, tx);
            cmd.Parameters.AddWithValue("tid", tid);
            cmd.Parameters.AddWithValue("cid", cid);
            cmd.Parameters.AddWithValue("name", name);
            await cmd.ExecuteNonQueryAsync();
        }

        static async Task ExecuteUpdateCurrencySettingsAsync(
            NpgsqlConnection conn,
            NpgsqlTransaction tx,
            Guid tid,
            Guid cid,
            Guid actorId,
            string func,
            string pres,
            DateOnly effectiveDate,
            bool confirm,
            string reason)
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT identity.update_company_currency_settings(@tid, @cid, @aid, @func, @pres, @eff, @conf, @reason);",
                conn, tx);
            cmd.Parameters.AddWithValue("tid", tid);
            cmd.Parameters.AddWithValue("cid", cid);
            cmd.Parameters.AddWithValue("aid", actorId);
            cmd.Parameters.AddWithValue("func", func);
            cmd.Parameters.AddWithValue("pres", pres);
            cmd.Parameters.AddWithValue("eff", effectiveDate);
            cmd.Parameters.AddWithValue("conf", confirm);
            cmd.Parameters.AddWithValue("reason", reason);
            await cmd.ExecuteNonQueryAsync();
        }

        // 1. Unscoped erp_app calls MUST fail closed
        Assert.Equal(DBNull.Value, await RunInAppScopeAsync(null, null, (c, t) => QueryGetCompanySettingsScalarAsync(c, t, tenantId, alphaCompanyId)));
        Assert.Equal(0L, await RunInAppScopeAsync(null, null, (c, t) => QueryHistoryCountAsync(c, t, tenantId, alphaCompanyId)));
        await AssertPrivilegeFailureAsync(null, null, (c, t) => ExecuteUpdateCompanySettingsAsync(c, t, tenantId, alphaCompanyId, "Unscoped"));
        await AssertPrivilegeFailureAsync(null, null, (c, t) => ExecuteUpdateCurrencySettingsAsync(c, t, tenantId, alphaCompanyId, adminUserId, "USD", "CAD", asOfToday, false, "Unscoped"));

        // 2. Tenant-only scoped erp_app calls (missing app.company_id) MUST fail closed
        Assert.Equal(DBNull.Value, await RunInAppScopeAsync(tenantId, null, (c, t) => QueryGetCompanySettingsScalarAsync(c, t, tenantId, alphaCompanyId)));
        Assert.Equal(0L, await RunInAppScopeAsync(tenantId, null, (c, t) => QueryHistoryCountAsync(c, t, tenantId, alphaCompanyId)));
        await AssertPrivilegeFailureAsync(tenantId, null, (c, t) => ExecuteUpdateCompanySettingsAsync(c, t, tenantId, alphaCompanyId, "MissingCompanyScope"));
        await AssertPrivilegeFailureAsync(tenantId, null, (c, t) => ExecuteUpdateCurrencySettingsAsync(c, t, tenantId, alphaCompanyId, adminUserId, "USD", "CAD", asOfToday, false, "MissingCompanyScope"));

        // 3. Cross-company (Alpha -> Beta) and cross-tenant (Alpha -> Gamma) reads MUST be denied
        Assert.Equal(DBNull.Value, await RunInAppScopeAsync(tenantId, alphaCompanyId, (c, t) => QueryGetCompanySettingsScalarAsync(c, t, tenantId, betaCompanyId)));
        Assert.Equal(DBNull.Value, await RunInAppScopeAsync(tenantId, alphaCompanyId, (c, t) => QueryGetCompanySettingsScalarAsync(c, t, otherTenantId, gammaCompanyId)));
        Assert.Equal(0L, await RunInAppScopeAsync(tenantId, alphaCompanyId, (c, t) => QueryHistoryCountAsync(c, t, tenantId, betaCompanyId)));
        Assert.Equal(0L, await RunInAppScopeAsync(tenantId, alphaCompanyId, (c, t) => QueryHistoryCountAsync(c, t, otherTenantId, gammaCompanyId)));

        // 4. Mismatched-context writes (Alpha -> Beta / Gamma) MUST fail with 42501
        await AssertPrivilegeFailureAsync(tenantId, alphaCompanyId, (c, t) => ExecuteUpdateCompanySettingsAsync(c, t, tenantId, betaCompanyId, "CrossCompanyWrite"));
        await AssertPrivilegeFailureAsync(tenantId, alphaCompanyId, (c, t) => ExecuteUpdateCompanySettingsAsync(c, t, otherTenantId, gammaCompanyId, "CrossTenantWrite"));
        await AssertPrivilegeFailureAsync(tenantId, alphaCompanyId, (c, t) => ExecuteUpdateCurrencySettingsAsync(c, t, tenantId, betaCompanyId, adminUserId, "USD", "CAD", asOfToday, false, "CrossCompanyCurrency"));
        await AssertPrivilegeFailureAsync(tenantId, alphaCompanyId, (c, t) => ExecuteUpdateCurrencySettingsAsync(c, t, otherTenantId, gammaCompanyId, gammaUserId, "PKR", "CAD", asOfToday, false, "CrossTenantCurrency"));

        // 5. Unauthorized actor (Gamma actor on Alpha) & viewer without 'admin.currency_settings.manage' MUST fail with 42501
        await AssertPrivilegeFailureAsync(tenantId, alphaCompanyId, (c, t) => ExecuteUpdateCurrencySettingsAsync(c, t, tenantId, alphaCompanyId, gammaUserId, "USD", "CAD", asOfToday, false, "ForeignActor"));
        await AssertPrivilegeFailureAsync(tenantId, alphaCompanyId, (c, t) => ExecuteUpdateCurrencySettingsAsync(c, t, tenantId, alphaCompanyId, viewerUserId, "USD", "CAD", asOfToday, false, "ViewerActor"));

        var svcViewerEx = await Assert.ThrowsAsync<PostgresException>(() =>
            foundationSvc.UpdateCurrencySettingsAsync(
                new TenantContext(tenantId, alphaCompanyId, viewerUserId),
                new UpdateCompanyCurrencySettingsCommand("USD", "CAD", asOfToday, false, "ServiceViewerAttempt"),
                ct));
        Assert.Equal("42501", svcViewerEx.SqlState);

        // 5a. FP-2a: Past effective dates MUST be rejected with ERRCODE 22023 (Migration 048) and surfaced cleanly via FoundationService
        var yesterday = asOfToday.AddDays(-1);
        var pastDateEx = await Assert.ThrowsAsync<PostgresException>(() =>
            RunInAppScopeAsync<object?>(tenantId, alphaCompanyId, async (conn, tx) =>
            {
                await ExecuteUpdateCurrencySettingsAsync(conn, tx, tenantId, alphaCompanyId, adminUserId, "USD", "USD", yesterday, true, "PastDateProbe");
                return null;
            }));
        Assert.Equal("22023", pastDateEx.SqlState);
        Assert.Contains("EFFECTIVE_DATE_MUST_NOT_BE_IN_THE_PAST", pastDateEx.Message);

        var pastDateSvcResult = await foundationSvc.UpdateCurrencySettingsAsync(
            new TenantContext(tenantId, alphaCompanyId, adminUserId),
            new UpdateCompanyCurrencySettingsCommand("USD", "CAD", yesterday, false, "PastDateSvcProbe"),
            ct);
        Assert.False(pastDateSvcResult.IsSuccess);
        Assert.Equal("EFFECTIVE_DATE_MUST_NOT_BE_IN_THE_PAST", pastDateSvcResult.Error);

        // 5b. FP-6a: A tenant-only role assignment (dedicated role with scope_type = 'tenant' ONLY, without scope_type = 'company')
        //     MUST NOT grant company-level currency management or view/rate authority in user_has_permission OR update_company_currency_settings.
        var tenantOnlyUserId = Guid.NewGuid();
        var tenantOnlyMembershipId = Guid.NewGuid();
        var tenantOnlyRoleId = Guid.NewGuid();
        try
        {
            await using (var toConn = await _adminDataSource.OpenConnectionAsync(ct))
            {
                await using var toTx = await toConn.BeginTransactionAsync(ct);
                await using (var setTid = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tid, true), set_config('app.company_id', @cid, true);", toConn, toTx))
                {
                    setTid.Parameters.AddWithValue("tid", tenantId.ToString());
                    setTid.Parameters.AddWithValue("cid", alphaCompanyId.ToString());
                    await setTid.ExecuteNonQueryAsync(ct);
                }
                await using var toUserCmd = new NpgsqlCommand("""
                    INSERT INTO identity.users (id, email_normalized, display_name, status)
                    VALUES (@uid, @email, 'TenantOnly Currency User', 'active');

                    INSERT INTO identity.tenant_memberships (id, tenant_id, user_id, status)
                    VALUES (@memId, @tid, @uid, 'active');

                    INSERT INTO identity.company_memberships (tenant_id, company_id, user_id, status)
                    VALUES (@tid, @cid, @uid, 'active');

                    INSERT INTO "authorization".roles (id, tenant_id, role_code, name, is_system_role)
                    VALUES (@roleId, @tid, @roleCode, 'Tenant-Only Currency Role', false);

                    INSERT INTO "authorization".membership_role_assignments (tenant_id, membership_id, role_id, assigned_by_user_id)
                    VALUES (@tid, @memId, @roleId, @uid);

                    INSERT INTO "authorization".role_permissions (tenant_id, role_id, permission_code, scope_type)
                    SELECT @tid, @roleId, p.permission_code, 'tenant'
                    FROM "authorization".permissions p
                    WHERE p.permission_code IN ('admin.currency_settings.view', 'admin.currency_settings.manage', 'finance.exchange_rate.manage');
                    """, toConn, toTx);
                toUserCmd.Parameters.AddWithValue("uid", tenantOnlyUserId);
                toUserCmd.Parameters.AddWithValue("email", $"tenantonly-{tenantOnlyUserId:N}@example.test");
                toUserCmd.Parameters.AddWithValue("tid", tenantId);
                toUserCmd.Parameters.AddWithValue("cid", alphaCompanyId);
                toUserCmd.Parameters.AddWithValue("memId", tenantOnlyMembershipId);
                toUserCmd.Parameters.AddWithValue("roleId", tenantOnlyRoleId);
                toUserCmd.Parameters.AddWithValue("roleCode", $"TENANT_ONLY_{tenantOnlyRoleId:N}"[..24]);
                await toUserCmd.ExecuteNonQueryAsync(ct);
                await toTx.CommitAsync(ct);
            }

            // DB routine user_has_permission MUST return FALSE for all three currency permissions at scope_type='tenant' only
            await using (var toPermConn = await _appDataSource.OpenConnectionAsync(ct))
            {
                foreach (var perm in new[] { "admin.currency_settings.view", "admin.currency_settings.manage", "finance.exchange_rate.manage" })
                {
                    await using var pCmd = new NpgsqlCommand("SELECT identity.user_has_permission(@t, @c, @u, @p);", toPermConn);
                    pCmd.Parameters.AddWithValue("t", tenantId);
                    pCmd.Parameters.AddWithValue("c", alphaCompanyId);
                    pCmd.Parameters.AddWithValue("u", tenantOnlyUserId);
                    pCmd.Parameters.AddWithValue("p", perm);
                    Assert.False((bool)(await pCmd.ExecuteScalarAsync(ct) ?? true));
                }
            }

            // ASP.NET Core DatabasePermissionAuthorizationHandler MUST also deny tenantOnlyUserId
            var tenantOnlyPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", tenantOnlyUserId.ToString())], "Bearer"));
            var tenantOnlyHandler = new DatabasePermissionAuthorizationHandler(
                _appDataSource,
                new StaticTenantContextAccessor(new TenantContext(tenantId, alphaCompanyId, tenantOnlyUserId)));
            var tenantOnlyAuthCtx = new AuthorizationHandlerContext(
                [new DatabasePermissionRequirement("admin.currency_settings.manage")],
                tenantOnlyPrincipal,
                null);
            await tenantOnlyHandler.HandleAsync(tenantOnlyAuthCtx);
            Assert.False(tenantOnlyAuthCtx.HasSucceeded);

            // tenantOnlyUserId MUST also fail inside identity.update_company_currency_settings with 42501
            var tenantScopeEx = await AssertPrivilegeFailureAsync(tenantId, alphaCompanyId, (c, t) =>
                ExecuteUpdateCurrencySettingsAsync(c, t, tenantId, alphaCompanyId, tenantOnlyUserId, "USD", "USD", asOfToday, true, "TenantScopeOnlyProbe"));
            Assert.Contains("MISSING_PERMISSION_ADMIN_CURRENCY_SETTINGS_MANAGE", tenantScopeEx.Message);
        }
        finally
        {
            await using var cleanConn = await _adminDataSource.OpenConnectionAsync(CancellationToken.None);
            await using var cleanTx = await cleanConn.BeginTransactionAsync(CancellationToken.None);
            await using var cleanCmd = new NpgsqlCommand("""
                DELETE FROM "authorization".role_permissions WHERE tenant_id = @tid AND role_id = @roleId;
                DELETE FROM "authorization".membership_role_assignments WHERE tenant_id = @tid AND membership_id = @memId;
                DELETE FROM "authorization".roles WHERE tenant_id = @tid AND id = @roleId;
                DELETE FROM identity.company_memberships WHERE tenant_id = @tid AND user_id = @uid;
                DELETE FROM identity.tenant_memberships WHERE id = @memId;
                DELETE FROM identity.users WHERE id = @uid;
                """, cleanConn, cleanTx);
            cleanCmd.Parameters.AddWithValue("tid", tenantId);
            cleanCmd.Parameters.AddWithValue("uid", tenantOnlyUserId);
            cleanCmd.Parameters.AddWithValue("memId", tenantOnlyMembershipId);
            cleanCmd.Parameters.AddWithValue("roleId", tenantOnlyRoleId);
            await cleanCmd.ExecuteNonQueryAsync(CancellationToken.None);
            await cleanTx.CommitAsync(CancellationToken.None);
        }

        // Verify Beta and Gamma currency settings remained completely intact after all rejected attempts
        var betaAfterDenial = await foundationSvc.GetCurrencySettingsAsync(new TenantContext(tenantId, betaCompanyId, adminUserId), ct);
        Assert.NotNull(betaAfterDenial);
        Assert.Equal("EUR", betaAfterDenial!.PresentationCurrencyCode);

        var gammaAfterDenial = await foundationSvc.GetCurrencySettingsAsync(new TenantContext(otherTenantId, gammaCompanyId, gammaUserId), ct);
        Assert.NotNull(gammaAfterDenial);
        Assert.Equal("GBP", gammaAfterDenial!.PresentationCurrencyCode);

        // 6. Constrained single-connection pool (MaxPoolSize = 1): prove zero GUC leakage across commits & rolled-back errors
        var poolBuilder = new NpgsqlConnectionStringBuilder(_appConnectionString)
        {
            Pooling = true,
            MinPoolSize = 1,
            MaxPoolSize = 1
        };
        await using var constrainedPool = NpgsqlDataSource.Create(poolBuilder.ConnectionString);
        var pooledRepo = new PostgresFoundationRepository(constrainedPool);

        var pooledAlpha = await pooledRepo.GetCompanySettingsAsync(tenantId, alphaCompanyId, ct);
        Assert.NotNull(pooledAlpha);
        var pooledUpdated = await pooledRepo.UpdateCompanySettingsAsync(
            tenantId,
            alphaCompanyId,
            new UpdateCompanySettingsCommand("Alpha Logistics PKR", "Alpha Legal Pvt Ltd", "REG-ALPHA-02", "Asia/Karachi"),
            ct);
        Assert.NotNull(pooledUpdated);
        Assert.Equal("REG-ALPHA-02", pooledUpdated!.RegistrationNumber);
        var pooledHistory = await pooledRepo.ListCompanyCurrencyHistoryAsync(tenantId, alphaCompanyId, 10, ct);
        Assert.Single(pooledHistory);

        // Trigger rollback on invalid currency in same single-connection pool
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pooledRepo.UpdateCompanyCurrencySettingsAsync(
                tenantId,
                alphaCompanyId,
                adminUserId,
                new UpdateCompanyCurrencySettingsCommand("ZZZ", "USD", asOfToday, false, "RollbackProbe"),
                ct));

        // Immediately inspect the reused pooled connection with NO set_config: GUCs MUST be cleared
        await using (var rawConn = await constrainedPool.OpenConnectionAsync(ct))
        {
            await using var gucCmd = new NpgsqlCommand(
                "SELECT NULLIF(current_setting('app.tenant_id', true), ''), NULLIF(current_setting('app.company_id', true), ''), identity.get_company_settings(@tid, @cid);",
                rawConn);
            gucCmd.Parameters.AddWithValue("tid", tenantId);
            gucCmd.Parameters.AddWithValue("cid", alphaCompanyId);
            await using var r = await gucCmd.ExecuteReaderAsync(ct);
            Assert.True(await r.ReadAsync(ct));
            Assert.True(r.IsDBNull(0), "Pooled connection leaked app.tenant_id across requests!");
            Assert.True(r.IsDBNull(1), "Pooled connection leaked app.company_id across requests!");
            Assert.True(r.IsDBNull(2), "Unscoped pooled read returned company settings!");
        }

        // Subsequent request on same constrained pool resolves Gamma accurately
        var pooledGamma = await pooledRepo.GetCompanySettingsAsync(otherTenantId, gammaCompanyId, ct);
        Assert.NotNull(pooledGamma);
        Assert.Equal(gammaCompanyId, pooledGamma!.CompanyId);
        Assert.Equal("GBP", pooledGamma.PresentationCurrencyCode);
    }

    private async Task CleanupFixturesAsync(Guid tenantId, Guid otherTenantId, Guid adminUserId, Guid viewerUserId, Guid gammaUserId)
    {
        await using var conn = await _adminDataSource.OpenConnectionAsync(CancellationToken.None);
        await using var tx = await conn.BeginTransactionAsync(CancellationToken.None);

        var tenantIds = new[] { tenantId, otherTenantId };
        var userIds = new[] { adminUserId, viewerUserId, gammaUserId };

        await using (var resetJournals = new NpgsqlCommand(
            "UPDATE finance.journals SET status = 'draft' WHERE tenant_id = ANY(@tids);", conn, tx))
        {
            resetJournals.Parameters.AddWithValue("tids", tenantIds);
            await resetJournals.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var tenantScopedTables = new[]
        {
            "finance.financial_audit_logs",
            "finance.exchange_rates",
            "finance.journal_lines",
            "finance.journals",
            "finance.accounts",
            "finance.fiscal_periods",
            "finance.fiscal_years",
            "erp_purchasing.purchase_order_line",
            "erp_purchasing.purchase_order",
            "erp_purchasing.supplier",
            "erp_inventory.valuation_entry",
            "erp_inventory.inventory_cost_allocation",
            "erp_inventory.inventory_cost_layer",
            "erp_inventory.stock_balance",
            "erp_inventory.stock_bin",
            "erp_inventory.warehouse",
            "erp_inventory.product_variant",
            "erp_inventory.product",
            "erp_inventory.stock_status",
            "erp_inventory.unit_of_measure",
            "organization.company_currency_history",
            "\"authorization\".role_permissions",
            "\"authorization\".membership_role_assignments",
            "\"authorization\".roles",
            "identity.company_memberships",
            "identity.tenant_memberships",
            "audit.audit_events",
            "organization.companies"
        };

        foreach (var table in tenantScopedTables)
        {
            await using var cmd = new NpgsqlCommand($"DELETE FROM {table} WHERE tenant_id = ANY(@tids);", conn, tx);
            cmd.Parameters.AddWithValue("tids", tenantIds);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using (var userCmd = new NpgsqlCommand("DELETE FROM identity.users WHERE id = ANY(@uids);", conn, tx))
        {
            userCmd.Parameters.AddWithValue("uids", userIds);
            await userCmd.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using (var tenantCmd = new NpgsqlCommand("DELETE FROM platform.tenants WHERE id = ANY(@tids);", conn, tx))
        {
            tenantCmd.Parameters.AddWithValue("tids", tenantIds);
            await tenantCmd.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await tx.CommitAsync(CancellationToken.None);
    }
}
