using System.Text.Json;
using Npgsql;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Purchasing;
using Xunit;

namespace UniversalErp.Api.Tests;

/// <summary>
/// PostgreSQL-backed integration tests for Purchasing GRN Outbox processing, deduplication,
/// poison-event isolation, exponential backoff, dead-letter transitions, and concurrent worker safety.
/// </summary>
public sealed class PostgresSupplyChainOutboxIntegrationTests : IAsyncDisposable
{
    private static readonly string AppConnectionString = ResolveAppConnectionString();
    private static readonly string AdminConnectionString = ResolveAdminConnectionString();

    private readonly NpgsqlDataSource _appDataSource;
    private readonly NpgsqlDataSource _adminDataSource;

    public PostgresSupplyChainOutboxIntegrationTests()
    {
        _appDataSource = NpgsqlDataSource.Create(AppConnectionString);
        _adminDataSource = NpgsqlDataSource.Create(AdminConnectionString);

        // Fail-closed runtime identity and privilege assertions
        using var appConn = _appDataSource.OpenConnection();
        using var appCmd = appConn.CreateCommand();
        appCmd.CommandText = "SELECT current_user, session_user, (SELECT rolbypassrls FROM pg_roles WHERE rolname = current_user);";
        using var appReader = appCmd.ExecuteReader();
        if (!appReader.Read() || appReader.GetString(0) != "erp_app" || appReader.GetBoolean(2))
        {
            throw new InvalidOperationException("Fail-closed: _appDataSource must connect strictly as erp_app without RLS bypass privileges.");
        }

        using var adminConn = _adminDataSource.OpenConnection();
        using var adminCmd = adminConn.CreateCommand();
        adminCmd.CommandText = "SELECT current_user;";
        var adminUser = (string?)adminCmd.ExecuteScalar();
        if (adminUser != "erp_dev")
        {
            throw new InvalidOperationException("Fail-closed: _adminDataSource must connect strictly as erp_dev for fixture setup and teardown.");
        }
    }

    private static string ResolveAppConnectionString()
    {
        var env = Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__UniversalErpTest");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        var pwd = Environment.GetEnvironmentVariable("PGPASSWORD_ERP_APP");
        if (!string.IsNullOrWhiteSpace(pwd))
        {
            return $"Host=127.0.0.1;Port=5434;Database=universal_erp_disposable_test;Username=erp_app;Password={pwd};Pooling=true;Minimum Pool Size=1;Maximum Pool Size=10";
        }

        throw new InvalidOperationException("Fail-closed: Neither UNIVERSAL_ERP_TEST_CONNECTION_STRING nor PGPASSWORD_ERP_APP environment variable is configured for erp_app test execution.");
    }

    private static string ResolveAdminConnectionString()
    {
        var env = Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__UniversalErpTestAdmin");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        var pwd = Environment.GetEnvironmentVariable("PGPASSWORD_ERP_DEV");
        if (!string.IsNullOrWhiteSpace(pwd))
        {
            return $"Host=127.0.0.1;Port=5434;Database=universal_erp_disposable_test;Username=erp_dev;Password={pwd};Pooling=true;Minimum Pool Size=1;Maximum Pool Size=10";
        }

        throw new InvalidOperationException("Fail-closed: Neither UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING nor PGPASSWORD_ERP_DEV environment variable is configured for erp_dev administrative test setup.");
    }

    public async ValueTask DisposeAsync()
    {
        await _appDataSource.DisposeAsync();
        await _adminDataSource.DisposeAsync();
    }

    private sealed record TestFixture(
        TenantContext Context,
        Guid LocationId,
        Guid WarehouseId,
        Guid BinId,
        Guid ProductId,
        Guid VariantId,
        Guid BaseUomId,
        Guid SupplierId,
        Guid PurchaseOrderId,
        Guid GrnId,
        Guid ClientTransactionId,
        Guid OutboxEventId,
        Guid FiscalYearId,
        Guid FiscalPeriodId,
        Guid Account1200Id,
        Guid Account2010Id,
        decimal ReceivedQty,
        decimal UnitPrice);

    private async Task<TestFixture> CreateFixtureAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var purchaseOrderId = Guid.NewGuid();
        var grnId = Guid.NewGuid();
        var clientTransactionId = Guid.NewGuid();
        var outboxEventId = Guid.NewGuid();
        var fiscalYearId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();
        var account1200Id = Guid.NewGuid();
        var account2010Id = Guid.NewGuid();
        const decimal receivedQty = 15m;
        const decimal unitPrice = 750m;

        await using var conn = await _adminDataSource.OpenConnectionAsync(cancellationToken);

        // 1. Tenant
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO platform.tenants (id, tenant_code, display_name, status) VALUES (@id, @code, @name, 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", tenantId);
            cmd.Parameters.AddWithValue("code", $"test-{tenantId:N}"[..20]);
            cmd.Parameters.AddWithValue("name", "Test Outbox Tenant");
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Set RLS scope
        await SetRlsAsync(conn, tenantId, companyId, cancellationToken);

        // 2. Company & Location
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO organization.companies (id, tenant_id, company_code, name, legal_name, base_currency_code, country_code, timezone, status) VALUES (@id, @tenantId, 'MAIN', 'Main Co', 'Main Co Legal', 'PKR', 'PK', 'Asia/Karachi', 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", companyId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO organization.locations (id, tenant_id, company_id, location_code, location_type_code, name, country_code, timezone, status) VALUES (@id, @tenantId, @companyId, 'LOC-WH1', 'warehouse', 'Main Warehouse Loc', 'PK', 'Asia/Karachi', 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", locationId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 3. User & Memberships
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO identity.users (id, email_normalized, display_name, status) VALUES (@id, @email, 'Outbox Tester', 'active')", conn))
        {
            cmd.Parameters.AddWithValue("id", userId);
            cmd.Parameters.AddWithValue("email", $"outbox-{userId:N}@test.com");
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
            "INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension, decimal_places) VALUES (@tenantId, @id, 'EA', 'Each', 'count', 0)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("id", uomId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.stock_status (tenant_id, status_code, description, is_reservable, is_pickable) VALUES (@tenantId, 'available', 'Available', true, true)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 5. Product & Variant
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, product_type, is_active) VALUES (@tenantId, @id, 'PROD-GRN', 'GRN Test Product', 'stock', true)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("id", productId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.product_variant (tenant_id, variant_id, product_id, sku, variant_name, base_uom_id, tracking_mode, costing_method, is_active) VALUES (@tenantId, @id, @productId, 'SKU-GRN-01', 'Standard', @uomId, 'none', 'fifo', true)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("id", variantId);
            cmd.Parameters.AddWithValue("productId", productId);
            cmd.Parameters.AddWithValue("uomId", uomId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 6. Warehouse & Stock Bin
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name, is_active, location_id) VALUES (@tenantId, @companyId, @id, 'WH-GRN', 'GRN Warehouse', true, @locationId)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("id", warehouseId);
            cmd.Parameters.AddWithValue("locationId", locationId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, warehouse_id, bin_id, code, bin_type, is_active) VALUES (@tenantId, @companyId, @warehouseId, @id, 'BIN-GRN-A', 'storage', true)", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("warehouseId", warehouseId);
            cmd.Parameters.AddWithValue("id", binId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 7. Finance: Fiscal Year, Fiscal Period, Accounts 1200 & 2010
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
            VALUES (@tenantId, @companyId, @id, 'FY-GRN-2026', '2026-01-01', '2026-12-31', 'open')
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("id", fiscalYearId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO finance.fiscal_periods (tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status)
            VALUES (@tenantId, @companyId, @id, @yearId, 1, 'Period 1 2026', '2026-01-01', '2026-01-31', 'open')
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("id", fiscalPeriodId);
            cmd.Parameters.AddWithValue("yearId", fiscalYearId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO finance.accounts (tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active)
            VALUES (@tenantId, @companyId, @id, '1200', 'Merchandise Inventory Asset', 'asset', 'debit', 'PKR', true, true, true)
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("id", account1200Id);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO finance.accounts (tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active)
            VALUES (@tenantId, @companyId, @id, '2010', 'Accounts Payable Trade Creditors', 'liability', 'credit', 'PKR', true, true, true)
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("id", account2010Id);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 8. Purchasing: Supplier, Purchase Order & Lines
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO erp_purchasing.supplier (tenant_id, company_id, supplier_id, supplier_code, name, currency_code, payment_terms_days, is_active)
            VALUES (@tenantId, @companyId, @id, 'SUP-GRN-01', 'GRN Test Supplier Ltd', 'PKR', 30, true)
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("id", supplierId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO erp_purchasing.purchase_order (tenant_id, company_id, purchase_order_id, po_number, supplier_id, warehouse_id, order_status, currency_code, order_date, created_by)
            VALUES (@tenantId, @companyId, @id, 'PO-GRN-001', @supplierId, @warehouseId, 'confirmed', 'PKR', CURRENT_DATE, @userId)
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("id", purchaseOrderId);
            cmd.Parameters.AddWithValue("supplierId", supplierId);
            cmd.Parameters.AddWithValue("warehouseId", warehouseId);
            cmd.Parameters.AddWithValue("userId", userId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO erp_purchasing.purchase_order_line (tenant_id, company_id, purchase_order_id, line_number, variant_id, ordered_qty, received_qty, unit_of_measure_id, unit_price, line_status)
            VALUES (@tenantId, @companyId, @poId, 1, @variantId, 20, 0, @uomId, @price, 'open')
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("poId", purchaseOrderId);
            cmd.Parameters.AddWithValue("variantId", variantId);
            cmd.Parameters.AddWithValue("uomId", uomId);
            cmd.Parameters.AddWithValue("price", unitPrice);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 9. Goods Received Note & Line
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO erp_purchasing.goods_received_note (tenant_id, company_id, grn_id, client_transaction_id, grn_number, purchase_order_id, supplier_id, warehouse_id, grn_status, received_date, created_by, posted_at)
            VALUES (@tenantId, @companyId, @id, @txId, 'GRN-2026-TEST-001', @poId, @supplierId, @warehouseId, 'posted', CURRENT_DATE, @userId, now())
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("id", grnId);
            cmd.Parameters.AddWithValue("txId", clientTransactionId);
            cmd.Parameters.AddWithValue("poId", purchaseOrderId);
            cmd.Parameters.AddWithValue("supplierId", supplierId);
            cmd.Parameters.AddWithValue("warehouseId", warehouseId);
            cmd.Parameters.AddWithValue("userId", userId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO erp_purchasing.goods_received_note_line (tenant_id, company_id, grn_id, line_number, po_line_number, purchase_order_id, variant_id, bin_id, received_qty, unit_of_measure_id, unit_price)
            VALUES (@tenantId, @companyId, @grnId, 1, 1, @poId, @variantId, @binId, @qty, @uomId, @price)
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("grnId", grnId);
            cmd.Parameters.AddWithValue("poId", purchaseOrderId);
            cmd.Parameters.AddWithValue("variantId", variantId);
            cmd.Parameters.AddWithValue("binId", binId);
            cmd.Parameters.AddWithValue("qty", receivedQty);
            cmd.Parameters.AddWithValue("uomId", uomId);
            cmd.Parameters.AddWithValue("price", unitPrice);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 10. Outbox Event (pending)
        var payloadJson = JsonSerializer.Serialize(new
        {
            grnId,
            purchaseOrderId,
            clientTransactionId,
            warehouseId,
            lines = new[]
            {
                new
                {
                    lineNumber = 1,
                    variantId,
                    binId,
                    receivedQty,
                    unitOfMeasureId = uomId,
                    unitPrice,
                }
            }
        });

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO erp_purchasing.outbox_event (tenant_id, company_id, event_id, grn_id, event_type, payload, occurred_at, published_at, attempt_count, status)
            VALUES (@tenantId, @companyId, @eventId, @grnId, 'grn.posted', @payload::jsonb, now(), NULL, 0, 'pending')
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("eventId", outboxEventId);
            cmd.Parameters.AddWithValue("grnId", grnId);
            cmd.Parameters.AddWithValue("payload", payloadJson);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        return new TestFixture(
            new TenantContext(tenantId, companyId, userId),
            locationId,
            warehouseId,
            binId,
            productId,
            variantId,
            uomId,
            supplierId,
            purchaseOrderId,
            grnId,
            clientTransactionId,
            outboxEventId,
            fiscalYearId,
            fiscalPeriodId,
            account1200Id,
            account2010Id,
            receivedQty,
            unitPrice);
    }

    private static async Task SetRlsAsync(NpgsqlConnection conn, TenantContext ctx, CancellationToken ct = default)
    {
        await SetRlsAsync(conn, ctx.TenantId, ctx.CompanyId, ct);
    }

    private static async Task SetRlsAsync(NpgsqlConnection conn, Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        await using var scope = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @t, false), set_config('app.company_id', @c, false)", conn);
        scope.Parameters.AddWithValue("t", tenantId.ToString());
        scope.Parameters.AddWithValue("c", companyId.ToString());
        await scope.ExecuteNonQueryAsync(ct);
    }

    private async Task CleanupFixtureAsync(TestFixture fixture)
    {
        try
        {
            await using var conn = await _adminDataSource.OpenConnectionAsync();
            var t = fixture.Context.TenantId;
            var c = fixture.Context.CompanyId;

            await SetRlsAsync(conn, t, c);

            await using var cmd = new NpgsqlCommand(
                """
                DELETE FROM finance.journal_lines WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM finance.journals WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM erp_purchasing.outbox_event WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM erp_purchasing.goods_received_note_line WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM erp_purchasing.goods_received_note WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM erp_purchasing.purchase_order_line WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM erp_purchasing.purchase_order WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM erp_purchasing.supplier WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM finance.accounts WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM finance.fiscal_periods WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM finance.fiscal_years WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM erp_inventory.stock_balance WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM erp_inventory.inventory_movement_leg WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM erp_inventory.stock_bin WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM erp_inventory.warehouse WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM erp_inventory.product_variant WHERE tenant_id = @t;
                DELETE FROM erp_inventory.product WHERE tenant_id = @t;
                DELETE FROM erp_inventory.unit_of_measure WHERE tenant_id = @t;
                DELETE FROM erp_inventory.stock_status WHERE tenant_id = @t;
                DELETE FROM identity.company_memberships WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM identity.tenant_memberships WHERE tenant_id = @t;
                DELETE FROM identity.users WHERE id = @uid;
                DELETE FROM organization.locations WHERE tenant_id = @t AND company_id = @c;
                DELETE FROM organization.companies WHERE tenant_id = @t AND id = @c;
                DELETE FROM platform.tenants WHERE id = @t;
                """, conn);
            cmd.Parameters.AddWithValue("t", t);
            cmd.Parameters.AddWithValue("c", c);
            cmd.Parameters.AddWithValue("uid", fixture.Context.UserId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task GrnOutbox_InitialProcessing_And_RetryAfterReset_DeduplicatesMovementAndStock_AndMarksPublished()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var repo = new PostgresSupplyChainOutboxRepository(_appDataSource);

            // --- Step 1: Initial Processing ---
            var firstResult = await repo.ProcessPendingOutboxEventsAsync(
                fixture.Context.TenantId, fixture.Context.CompanyId, fixture.Context.UserId, CancellationToken.None);

            Assert.Equal(1, firstResult.ProcessedEvents);
            Assert.Equal(1, firstResult.StockMovementsCreated);
            Assert.Equal(1, firstResult.JournalsPosted);

            await using var conn = await _adminDataSource.OpenConnectionAsync();
            await SetRlsAsync(conn, fixture.Context);

            // 1. Exactly 1 inventory movement exists for this GRN
            Guid movementId;
            await using (var cmd = new NpgsqlCommand(
                "SELECT movement_id FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND company_id = @c AND source_document_type = 'grn' AND source_document_id = @grnId", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("grnId", fixture.GrnId);
                var idRaw = await cmd.ExecuteScalarAsync();
                Assert.NotNull(idRaw);
                movementId = (Guid)idRaw;
            }

            // 2. Exactly 1 movement leg exists with the expected quantity
            await using (var cmd = new NpgsqlCommand(
                "SELECT quantity_delta FROM erp_inventory.inventory_movement_leg WHERE tenant_id = @t AND company_id = @c AND movement_id = @m", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("m", movementId);
                var qty = Convert.ToDecimal(await cmd.ExecuteScalarAsync());
                Assert.Equal(fixture.ReceivedQty, qty);
            }

            // 3. Stock balance reflects exactly the received quantity
            await using (var cmd = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @t AND company_id = @c AND variant_id = @v AND warehouse_id = @w AND bin_id = @b", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("v", fixture.VariantId);
                cmd.Parameters.AddWithValue("w", fixture.WarehouseId);
                cmd.Parameters.AddWithValue("b", fixture.BinId);
                var onHand = Convert.ToDecimal(await cmd.ExecuteScalarAsync());
                Assert.Equal(fixture.ReceivedQty, onHand);
            }

            // 4. Exactly 1 GL Journal exists for this GRN
            Guid journalId;
            await using (var cmd = new NpgsqlCommand(
                "SELECT id FROM finance.journals WHERE tenant_id = @t AND company_id = @c AND source_module = 'purchasing' AND source_type = 'grn' AND source_id = @grnId", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("grnId", fixture.GrnId.ToString());
                var jId = await cmd.ExecuteScalarAsync();
                Assert.NotNull(jId);
                journalId = (Guid)jId;
            }

            // 5. Outbox event is marked published with status = 'published' and attempt_count = 1
            await using (var cmd = new NpgsqlCommand(
                "SELECT status, published_at, attempt_count FROM erp_purchasing.outbox_event WHERE tenant_id = @t AND company_id = @c AND event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("e", fixture.OutboxEventId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("published", reader.GetString(0));
                Assert.False(reader.IsDBNull(1)); // published_at is set
                Assert.Equal(1, reader.GetInt32(2)); // attempt_count == 1
                await reader.CloseAsync();
            }

            // --- Step 2: Reset published_at to NULL and status to 'pending' to simulate a retry ---
            await using (var resetCmd = new NpgsqlCommand(
                "UPDATE erp_purchasing.outbox_event SET published_at = NULL, status = 'pending' WHERE tenant_id = @t AND company_id = @c AND event_id = @e", conn))
            {
                resetCmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                resetCmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                resetCmd.Parameters.AddWithValue("e", fixture.OutboxEventId);
                var resetCount = await resetCmd.ExecuteNonQueryAsync();
                Assert.Equal(1, resetCount);
            }

            // --- Step 3: Process It Again (Simulate Retry) ---
            var retryResult = await repo.ProcessPendingOutboxEventsAsync(
                fixture.Context.TenantId, fixture.Context.CompanyId, fixture.Context.UserId, CancellationToken.None);

            Assert.Equal(1, retryResult.ProcessedEvents);
            Assert.Equal(0, retryResult.StockMovementsCreated);
            Assert.Equal(0, retryResult.JournalsPosted);

            // --- Step 4: Assert Persisted Database State Directly ---
            // A. Exactly 1 inventory movement remains for this GRN (no duplicates)
            await using (var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND company_id = @c AND source_document_type = 'grn' AND source_document_id = @grnId", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("grnId", fixture.GrnId);
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(1, count);
            }

            // B. Stock balance has NOT been incremented again (remains receivedQty, not 2x)
            await using (var cmd = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @t AND company_id = @c AND variant_id = @v AND warehouse_id = @w AND bin_id = @b", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("v", fixture.VariantId);
                cmd.Parameters.AddWithValue("w", fixture.WarehouseId);
                cmd.Parameters.AddWithValue("b", fixture.BinId);
                var onHand = Convert.ToDecimal(await cmd.ExecuteScalarAsync());
                Assert.Equal(fixture.ReceivedQty, onHand);
            }

            // C. Outbox event is marked published again with attempt_count = 2
            await using (var cmd = new NpgsqlCommand(
                "SELECT status, published_at, attempt_count FROM erp_purchasing.outbox_event WHERE tenant_id = @t AND company_id = @c AND event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("e", fixture.OutboxEventId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("published", reader.GetString(0));
                Assert.False(reader.IsDBNull(1));
                Assert.Equal(2, reader.GetInt32(2));
                await reader.CloseAsync();
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task PoisonEvent_FollowedByHealthyEvent_HealthyEventCommits_AndPoisonEventDoesNotBlockQueue()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using var conn = await _adminDataSource.OpenConnectionAsync();
            await SetRlsAsync(conn, fixture.Context);

            // Create Event 1 (Poison): points to nonexistent bin UUID -> foreign key violation
            var poisonEventId = Guid.NewGuid();
            var invalidBinId = Guid.NewGuid();
            var poisonPayload = JsonSerializer.Serialize(new
            {
                grnId = fixture.GrnId,
                purchaseOrderId = fixture.PurchaseOrderId,
                clientTransactionId = Guid.NewGuid(),
                warehouseId = fixture.WarehouseId,
                lines = new[]
                {
                    new
                    {
                        lineNumber = 1,
                        variantId = fixture.VariantId,
                        binId = invalidBinId, // Nonexistent bin!
                        receivedQty = 5m,
                        unitOfMeasureId = fixture.BaseUomId,
                        unitPrice = 100m,
                    }
                }
            });

            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO erp_purchasing.outbox_event (tenant_id, company_id, event_id, grn_id, event_type, payload, occurred_at, published_at, attempt_count, status)
                VALUES (@tenantId, @companyId, @eventId, @grnId, 'grn.posted', @payload::jsonb, now() - interval '10 seconds', NULL, 0, 'pending')
                """, conn))
            {
                cmd.Parameters.AddWithValue("tenantId", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("companyId", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("eventId", poisonEventId);
                cmd.Parameters.AddWithValue("grnId", fixture.GrnId);
                cmd.Parameters.AddWithValue("payload", poisonPayload);
                await cmd.ExecuteNonQueryAsync();
            }

            // Event 2 is the fixture's healthy event (already seeded with valid bin)
            var repo = new PostgresSupplyChainOutboxRepository(_appDataSource);
            var result = await repo.ProcessPendingOutboxEventsAsync(
                fixture.Context.TenantId, fixture.Context.CompanyId, fixture.Context.UserId, CancellationToken.None);

            // Assert: 1 processed (healthy), 1 failed (poison)
            Assert.Equal(1, result.ProcessedEvents);
            Assert.Equal(1, result.FailedEvents);
            Assert.Equal(1, result.StockMovementsCreated);

            // Assert Event 1 (Poison): Failed cleanly, rolled back, attempt_count = 1, published_at IS NULL
            await using (var cmd = new NpgsqlCommand(
                "SELECT status, attempt_count, published_at, last_error FROM erp_purchasing.outbox_event WHERE tenant_id = @t AND company_id = @c AND event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("e", poisonEventId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("pending", reader.GetString(0));
                Assert.Equal(1, reader.GetInt32(1));
                Assert.True(reader.IsDBNull(2)); // published_at is NULL
                Assert.NotNull(reader.GetString(3)); // last_error is recorded
                await reader.CloseAsync();
            }

            // Assert Event 2 (Healthy): Succeeded and committed!
            await using (var cmd = new NpgsqlCommand(
                "SELECT status, published_at FROM erp_purchasing.outbox_event WHERE tenant_id = @t AND company_id = @c AND event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("e", fixture.OutboxEventId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("published", reader.GetString(0));
                Assert.False(reader.IsDBNull(1));
                await reader.CloseAsync();
            }

            // Assert Stock Balance is exactly 15m (from healthy event, 0 from poison event)
            await using (var cmd = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @t AND company_id = @c AND variant_id = @v AND warehouse_id = @w AND bin_id = @b", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("v", fixture.VariantId);
                cmd.Parameters.AddWithValue("w", fixture.WarehouseId);
                cmd.Parameters.AddWithValue("b", fixture.BinId);
                var onHand = Convert.ToDecimal(await cmd.ExecuteScalarAsync());
                Assert.Equal(fixture.ReceivedQty, onHand);
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task PoisonEvent_RetryCountAndLastError_PersistsDurablyWithoutThrowingUnhandled()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using var conn = await _adminDataSource.OpenConnectionAsync();
            await SetRlsAsync(conn, fixture.Context);

            // Poison the event by referencing a nonexistent variant
            var badVariantId = Guid.NewGuid();
            var payload = JsonSerializer.Serialize(new
            {
                grnId = fixture.GrnId,
                purchaseOrderId = fixture.PurchaseOrderId,
                clientTransactionId = Guid.NewGuid(),
                warehouseId = fixture.WarehouseId,
                lines = new[]
                {
                    new
                    {
                        lineNumber = 1,
                        variantId = badVariantId,
                        binId = fixture.BinId,
                        receivedQty = 10m,
                        unitOfMeasureId = fixture.BaseUomId,
                        unitPrice = 50m,
                    }
                }
            });

            await using (var cmd = new NpgsqlCommand(
                "UPDATE erp_purchasing.outbox_event SET payload = @p::jsonb WHERE event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("p", payload);
                cmd.Parameters.AddWithValue("e", fixture.OutboxEventId);
                await cmd.ExecuteNonQueryAsync();
            }

            var repo = new PostgresSupplyChainOutboxRepository(_appDataSource);
            var result = await repo.ProcessPendingOutboxEventsAsync(
                fixture.Context.TenantId, fixture.Context.CompanyId, fixture.Context.UserId, CancellationToken.None);

            Assert.Equal(0, result.ProcessedEvents);
            Assert.Equal(1, result.FailedEvents);

            // Verify in PostgreSQL: attempt_count = 1, last_error != null, next_retry_at is set in the future
            await using (var cmd = new NpgsqlCommand(
                "SELECT status, attempt_count, last_error, next_retry_at, published_at FROM erp_purchasing.outbox_event WHERE event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("e", fixture.OutboxEventId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("pending", reader.GetString(0));
                Assert.Equal(1, reader.GetInt32(1));
                Assert.Contains("PostgreSQL", reader.GetString(2)); // sanitized error details
                Assert.False(reader.IsDBNull(3)); // next_retry_at is populated
                Assert.True(reader.GetFieldValue<DateTimeOffset>(3) > DateTimeOffset.UtcNow); // exponential backoff in future
                Assert.True(reader.IsDBNull(4)); // published_at IS NULL
                await reader.CloseAsync();
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task PoisonEvent_ReachingMaxAttempts_TransitionsToDeadLetterState()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using var conn = await _adminDataSource.OpenConnectionAsync();
            await SetRlsAsync(conn, fixture.Context);

            // Poison payload & seed attempt_count = 4 (one attempt away from MaxRetryAttempts=5)
            var badVariantId = Guid.NewGuid();
            var payload = JsonSerializer.Serialize(new
            {
                grnId = fixture.GrnId,
                purchaseOrderId = fixture.PurchaseOrderId,
                clientTransactionId = Guid.NewGuid(),
                warehouseId = fixture.WarehouseId,
                lines = new[]
                {
                    new
                    {
                        lineNumber = 1,
                        variantId = badVariantId,
                        binId = fixture.BinId,
                        receivedQty = 10m,
                        unitOfMeasureId = fixture.BaseUomId,
                        unitPrice = 50m,
                    }
                }
            });

            await using (var cmd = new NpgsqlCommand(
                "UPDATE erp_purchasing.outbox_event SET payload = @p::jsonb, attempt_count = 4 WHERE event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("p", payload);
                cmd.Parameters.AddWithValue("e", fixture.OutboxEventId);
                await cmd.ExecuteNonQueryAsync();
            }

            var repo = new PostgresSupplyChainOutboxRepository(_appDataSource);
            var result = await repo.ProcessPendingOutboxEventsAsync(
                fixture.Context.TenantId, fixture.Context.CompanyId, fixture.Context.UserId, CancellationToken.None);

            Assert.Equal(0, result.ProcessedEvents);
            Assert.Equal(1, result.FailedEvents);
            Assert.Equal(1, result.DeadLetterEvents);

            // Assert transition to 'dead_letter' with attempt_count = 5 and published_at IS NULL
            await using (var cmd = new NpgsqlCommand(
                "SELECT status, attempt_count, published_at, next_retry_at FROM erp_purchasing.outbox_event WHERE event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("e", fixture.OutboxEventId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("dead_letter", reader.GetString(0));
                Assert.Equal(5, reader.GetInt32(1));
                Assert.True(reader.IsDBNull(2)); // published_at remains NULL
                Assert.True(reader.IsDBNull(3)); // next_retry_at is NULL (no automatic retry)
                await reader.CloseAsync();
            }

            // Run process again: Dead-letter event must be excluded from selection!
            var secondResult = await repo.ProcessPendingOutboxEventsAsync(
                fixture.Context.TenantId, fixture.Context.CompanyId, fixture.Context.UserId, CancellationToken.None);

            Assert.Equal(0, secondResult.ProcessedEvents);
            Assert.Equal(0, secondResult.FailedEvents);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task DeadLetterEvent_WhenRedrivenAndFixed_SuccessfullyProcesses()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using var conn = await _adminDataSource.OpenConnectionAsync();
            await SetRlsAsync(conn, fixture.Context);

            // Mark event as dead_letter directly
            await using (var cmd = new NpgsqlCommand(
                "UPDATE erp_purchasing.outbox_event SET status = 'dead_letter', attempt_count = 5, last_error = 'SIMULATED_FAILURE' WHERE event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("e", fixture.OutboxEventId);
                await cmd.ExecuteNonQueryAsync();
            }

            var repo = new PostgresSupplyChainOutboxRepository(_appDataSource);

            // Redrive dead letter event
            var redrivenCount = await repo.RedriveDeadLetterEventsAsync(
                fixture.Context.TenantId, fixture.Context.CompanyId, fixture.OutboxEventId, CancellationToken.None);

            Assert.Equal(1, redrivenCount);

            // Assert redrive audit state in database
            await using (var cmd = new NpgsqlCommand(
                "SELECT status, attempt_count, redrive_count, last_redriven_at FROM erp_purchasing.outbox_event WHERE event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("e", fixture.OutboxEventId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("pending", reader.GetString(0));
                Assert.Equal(0, reader.GetInt32(1)); // attempt_count reset to 0
                Assert.Equal(1, reader.GetInt32(2)); // redrive_count incremented to 1
                Assert.False(reader.IsDBNull(3)); // last_redriven_at recorded
                await reader.CloseAsync();
            }

            // Process it now: should succeed cleanly!
            var result = await repo.ProcessPendingOutboxEventsAsync(
                fixture.Context.TenantId, fixture.Context.CompanyId, fixture.Context.UserId, CancellationToken.None);

            Assert.Equal(1, result.ProcessedEvents);
            Assert.Equal(1, result.StockMovementsCreated);

            // Assert status is now 'published'
            await using (var cmd = new NpgsqlCommand(
                "SELECT status, published_at FROM erp_purchasing.outbox_event WHERE event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("e", fixture.OutboxEventId);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("published", reader.GetString(0));
                Assert.False(reader.IsDBNull(1));
                await reader.CloseAsync();
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task ConcurrentWorkers_ProcessingPendingEvents_DoNotDoubleProcessOrCollide()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using var conn = await _adminDataSource.OpenConnectionAsync();
            await SetRlsAsync(conn, fixture.Context);

            // Seed a second healthy event
            var eventId2 = Guid.NewGuid();
            var grnId2 = Guid.NewGuid();
            var clientTxId2 = Guid.NewGuid();

            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO erp_purchasing.goods_received_note (tenant_id, company_id, grn_id, client_transaction_id, grn_number, purchase_order_id, supplier_id, warehouse_id, grn_status, received_date, created_by, posted_at)
                VALUES (@tenantId, @companyId, @id, @txId, 'GRN-2026-TEST-002', @poId, @supplierId, @warehouseId, 'posted', CURRENT_DATE, @userId, now())
                """, conn))
            {
                cmd.Parameters.AddWithValue("tenantId", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("companyId", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("id", grnId2);
                cmd.Parameters.AddWithValue("txId", clientTxId2);
                cmd.Parameters.AddWithValue("poId", fixture.PurchaseOrderId);
                cmd.Parameters.AddWithValue("supplierId", fixture.SupplierId);
                cmd.Parameters.AddWithValue("warehouseId", fixture.WarehouseId);
                cmd.Parameters.AddWithValue("userId", fixture.Context.UserId);
                await cmd.ExecuteNonQueryAsync();
            }

            var payload2 = JsonSerializer.Serialize(new
            {
                grnId = grnId2,
                purchaseOrderId = fixture.PurchaseOrderId,
                clientTransactionId = clientTxId2,
                warehouseId = fixture.WarehouseId,
                lines = new[]
                {
                    new
                    {
                        lineNumber = 1,
                        variantId = fixture.VariantId,
                        binId = fixture.BinId,
                        receivedQty = 10m,
                        unitOfMeasureId = fixture.BaseUomId,
                        unitPrice = 500m,
                    }
                }
            });

            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO erp_purchasing.outbox_event (tenant_id, company_id, event_id, grn_id, event_type, payload, occurred_at, published_at, attempt_count, status)
                VALUES (@tenantId, @companyId, @eventId, @grnId, 'grn.posted', @payload::jsonb, now(), NULL, 0, 'pending')
                """, conn))
            {
                cmd.Parameters.AddWithValue("tenantId", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("companyId", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("eventId", eventId2);
                cmd.Parameters.AddWithValue("grnId", grnId2);
                cmd.Parameters.AddWithValue("payload", payload2);
                await cmd.ExecuteNonQueryAsync();
            }

            // Launch two workers concurrently
            var repo1 = new PostgresSupplyChainOutboxRepository(_appDataSource);
            var repo2 = new PostgresSupplyChainOutboxRepository(_appDataSource);

            var task1 = repo1.ProcessPendingOutboxEventsAsync(
                fixture.Context.TenantId, fixture.Context.CompanyId, fixture.Context.UserId, CancellationToken.None);
            var task2 = repo2.ProcessPendingOutboxEventsAsync(
                fixture.Context.TenantId, fixture.Context.CompanyId, fixture.Context.UserId, CancellationToken.None);

            await Task.WhenAll(task1, task2);

            var r1 = await task1;
            var r2 = await task2;

            // Assert: Exactly 2 events were processed across the two workers
            Assert.Equal(2, r1.ProcessedEvents + r2.ProcessedEvents);
            Assert.Equal(2, r1.StockMovementsCreated + r2.StockMovementsCreated);

            // Direct SQL: Both events have status = 'published'
            await using (var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_purchasing.outbox_event WHERE tenant_id = @t AND company_id = @c AND status = 'published'", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                var publishedCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(2, publishedCount);
            }

            // Direct SQL: Total inventory movements created for these GRNs is exactly 2
            await using (var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM erp_inventory.inventory_movement WHERE tenant_id = @t AND company_id = @c AND source_document_type = 'grn'", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                var movCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(2, movCount);
            }

            // Direct SQL: Stock balance equals receivedQty (15) + event 2 (10) = 25m
            await using (var cmd = new NpgsqlCommand(
                "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @t AND company_id = @c AND variant_id = @v AND warehouse_id = @w AND bin_id = @b", conn))
            {
                cmd.Parameters.AddWithValue("t", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("c", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("v", fixture.VariantId);
                cmd.Parameters.AddWithValue("w", fixture.WarehouseId);
                cmd.Parameters.AddWithValue("b", fixture.BinId);
                var onHand = Convert.ToDecimal(await cmd.ExecuteScalarAsync());
                Assert.Equal(25m, onHand);
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task UnsupportedEventTypeOrEmptyLines_TransitionsDirectlyToDeadLetter_WithoutSilentPublish()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using var conn = await _adminDataSource.OpenConnectionAsync();
            await SetRlsAsync(conn, fixture.Context);

            // Event A: Unsupported event type
            var eventIdA = Guid.NewGuid();
            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO erp_purchasing.outbox_event (tenant_id, company_id, event_id, grn_id, event_type, payload, occurred_at, published_at, attempt_count, status)
                VALUES (@tenantId, @companyId, @eventId, @grnId, 'unsupported.dummy.event', '{}'::jsonb, now(), NULL, 0, 'pending')
                """, conn))
            {
                cmd.Parameters.AddWithValue("tenantId", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("companyId", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("eventId", eventIdA);
                cmd.Parameters.AddWithValue("grnId", fixture.GrnId);
                await cmd.ExecuteNonQueryAsync();
            }

            // Event B: Empty lines payload
            var eventIdB = Guid.NewGuid();
            var emptyPayload = JsonSerializer.Serialize(new
            {
                grnId = fixture.GrnId,
                purchaseOrderId = fixture.PurchaseOrderId,
                clientTransactionId = Guid.NewGuid(),
                warehouseId = fixture.WarehouseId,
                lines = Array.Empty<object>()
            });

            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO erp_purchasing.outbox_event (tenant_id, company_id, event_id, grn_id, event_type, payload, occurred_at, published_at, attempt_count, status)
                VALUES (@tenantId, @companyId, @eventId, @grnId, 'grn.posted', @payload::jsonb, now(), NULL, 0, 'pending')
                """, conn))
            {
                cmd.Parameters.AddWithValue("tenantId", fixture.Context.TenantId);
                cmd.Parameters.AddWithValue("companyId", fixture.Context.CompanyId);
                cmd.Parameters.AddWithValue("eventId", eventIdB);
                cmd.Parameters.AddWithValue("grnId", fixture.GrnId);
                cmd.Parameters.AddWithValue("payload", emptyPayload);
                await cmd.ExecuteNonQueryAsync();
            }

            var repo = new PostgresSupplyChainOutboxRepository(_appDataSource);
            var result = await repo.ProcessPendingOutboxEventsAsync(
                fixture.Context.TenantId, fixture.Context.CompanyId, fixture.Context.UserId, CancellationToken.None);

            // Assert: Healthy fixture event processed (1), while both A and B transitioned to dead letter
            Assert.Equal(1, result.ProcessedEvents);
            Assert.Equal(2, result.FailedEvents);
            Assert.Equal(2, result.DeadLetterEvents);

            // Direct SQL for Event A
            await using (var cmd = new NpgsqlCommand(
                "SELECT status, published_at, last_error FROM erp_purchasing.outbox_event WHERE event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("e", eventIdA);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("dead_letter", reader.GetString(0));
                Assert.True(reader.IsDBNull(1)); // NEVER published!
                Assert.Contains("UNSUPPORTED_EVENT_TYPE", reader.GetString(2));
                await reader.CloseAsync();
            }

            // Direct SQL for Event B
            await using (var cmd = new NpgsqlCommand(
                "SELECT status, published_at, last_error FROM erp_purchasing.outbox_event WHERE event_id = @e", conn))
            {
                cmd.Parameters.AddWithValue("e", eventIdB);
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("dead_letter", reader.GetString(0));
                Assert.True(reader.IsDBNull(1)); // NEVER published!
                Assert.Contains("EMPTY_OR_MALFORMED_PAYLOAD", reader.GetString(2));
                await reader.CloseAsync();
            }
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }
}
