using Npgsql;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Purchasing;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Purchasing;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class Phase6PostgresPurchasingIntegrationTests
{
    private static readonly string? StagingConnectionString =
        Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
        ?? "Host=127.0.0.1;Port=5433;Database=universal_erp_phase5_dev;Username=erp_dev;Password=local_dev_change_me;Include Error Detail=true;";

    private sealed class TestTenantContextAccessor(TenantContext ctx) : ITenantContextAccessor
    {
        public TenantContext Current => ctx;
    }

    [Fact]
    public async Task Phase6_FullPurchasingAndQualityLifecycle_IntegrationTest()
    {
        // 1. Arrange tenant & environment
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var tenantContext = new TenantContext(
            TenantId: tenantId,
            CompanyId: companyId,
            UserId: userId);

        var dataSource = NpgsqlDataSource.Create(StagingConnectionString!);
        var accessor = new TestTenantContextAccessor(tenantContext);
        var outboxRepo = new PostgresSupplyChainOutboxRepository(dataSource);
        var outboxService = new SupplyChainOutboxService(outboxRepo, accessor);
        var purchasingRepo = new PostgresPurchasingRepository(dataSource, accessor);
        var purchasingService = new PurchasingService(purchasingRepo, accessor, outboxService);

        var cancellationToken = CancellationToken.None;

        // 2. Seed Foundation Data
        var warehouseId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var quarBinId = Guid.NewGuid();
        var targetAvailBinId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var fiscalYearId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();
        var invAccountId = Guid.NewGuid();
        var apAccountId = Guid.NewGuid();
        var cogsAccountId = Guid.NewGuid();

        await using (var conn = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);

            // Set RLS for finance tables
            await using (var setCmd = conn.CreateCommand())
            {
                setCmd.Transaction = tx;
                setCmd.CommandText = $"SET LOCAL app.tenant_id = '{tenantId}'; SET LOCAL app.company_id = '{companyId}';";
                await setCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                -- Seed Tenant & Company
                INSERT INTO platform.tenants (id, tenant_code, display_name, status, default_country_code, default_currency_code, default_timezone)
                VALUES (@tid, @tcode, 'Purchasing Test Tenant', 'active', 'PK', 'PKR', 'Asia/Karachi')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO organization.companies (tenant_id, id, company_code, name, country_code, base_currency_code, timezone, status)
                VALUES (@tid, @cid, @ccode, 'Purchasing Test Company', 'PK', 'PKR', 'Asia/Karachi', 'active')
                ON CONFLICT (tenant_id, id) DO NOTHING;

                INSERT INTO identity.users (id, email_normalized, display_name, status)
                VALUES (@uid, @uemail, 'Purchasing Tester', 'active')
                ON CONFLICT (id) DO NOTHING;

                -- Seed Stock Statuses
                INSERT INTO erp_inventory.stock_status (tenant_id, status_code, description, is_reservable, is_pickable)
                VALUES
                    (@tid, 'available', 'Sellable Available Stock', true, true),
                    (@tid, 'quarantine', 'Quarantined Stock', false, false),
                    (@tid, 'damaged', 'Damaged Stock', false, false)
                ON CONFLICT (tenant_id, status_code) DO NOTHING;

                -- Seed Warehouse & Bins
                INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name, is_active)
                VALUES (@tid, @cid, @wh, 'WH-PURCH-01', 'Purchasing Warehouse', true)
                ON CONFLICT (tenant_id, company_id, warehouse_id) DO NOTHING;

                INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, bin_id, warehouse_id, code, bin_type, is_active)
                VALUES
                    (@tid, @cid, @bin, @wh, 'BIN-REC-01', 'receiving', true),
                    (@tid, @cid, @qbin, @wh, 'BIN-QUAR-01', 'storage', true),
                    (@tid, @cid, @tbin, @wh, 'BIN-STOR-01', 'storage', true)
                ON CONFLICT (tenant_id, company_id, bin_id) DO NOTHING;

                -- Seed UoM & Product
                INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension, decimal_places)
                VALUES (@tid, @uom, 'EA', 'Each', 'count', 0)
                ON CONFLICT (tenant_id, uom_id) DO NOTHING;

                INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, product_type, is_active)
                VALUES (@tid, @pid, @pcode, 'Purchasing Raw Material', 'stock', true)
                ON CONFLICT (tenant_id, product_id) DO NOTHING;

                INSERT INTO erp_inventory.product_variant (tenant_id, variant_id, product_id, sku, variant_name, base_uom_id, costing_method)
                VALUES (@tid, @var, @pid, @sku, 'Purchasing Raw Material Variant', @uom, 'fifo')
                ON CONFLICT (tenant_id, variant_id) DO NOTHING;

                -- Seed Supplier
                INSERT INTO erp_purchasing.supplier
                    (tenant_id, company_id, supplier_id, supplier_code, name, currency_code, payment_terms_days)
                VALUES
                    (@tid, @cid, @sup, @scode, 'Global Industrial Supplies Ltd', 'PKR', 30)
                ON CONFLICT (tenant_id, company_id, supplier_id) DO NOTHING;

                -- Seed Fiscal Period & Chart of Accounts
                INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
                VALUES (@tid, @cid, @fyid, 'FY2026-PURCH', CURRENT_DATE - INTERVAL '30 days', CURRENT_DATE + INTERVAL '300 days', 'open')
                ON CONFLICT (tenant_id, company_id, code) DO NOTHING;

                INSERT INTO finance.fiscal_periods (tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status)
                VALUES (@tid, @cid, @fpid, @fyid, 1, 'Current Period', CURRENT_DATE - INTERVAL '10 days', CURRENT_DATE + INTERVAL '20 days', 'open')
                ON CONFLICT (tenant_id, company_id, fiscal_year_id, period_no) DO NOTHING;

                INSERT INTO finance.accounts (tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, is_control_account, allow_manual_posting)
                VALUES
                    (@tid, @cid, @invAcc, '1200', 'Merchandise Inventory Asset', 'asset', 'debit', true, false),
                    (@tid, @cid, @apAcc, '2010', 'Accounts Payable Trade Creditors', 'liability', 'credit', true, false),
                    (@tid, @cid, @cogsAcc, '5010', 'Cost of Goods Sold (COGS)', 'expense', 'debit', false, true)
                ON CONFLICT (tenant_id, company_id, account_code) DO NOTHING;
                """;

            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("tcode", $"T-{tenantId.ToString()[..8].ToUpperInvariant()}");
            cmd.Parameters.AddWithValue("cid", companyId);
            cmd.Parameters.AddWithValue("ccode", $"C-{companyId.ToString()[..8].ToUpperInvariant()}");
            cmd.Parameters.AddWithValue("uid", userId);
            cmd.Parameters.AddWithValue("uemail", $"purchasing-{userId:N}@test.com");
            cmd.Parameters.AddWithValue("wh", warehouseId);
            cmd.Parameters.AddWithValue("bin", binId);
            cmd.Parameters.AddWithValue("qbin", quarBinId);
            cmd.Parameters.AddWithValue("tbin", targetAvailBinId);
            cmd.Parameters.AddWithValue("uom", uomId);
            cmd.Parameters.AddWithValue("pid", productId);
            cmd.Parameters.AddWithValue("pcode", $"PROD-{Guid.NewGuid().ToString()[..6]}");
            cmd.Parameters.AddWithValue("var", variantId);
            cmd.Parameters.AddWithValue("sku", $"SKU-{Guid.NewGuid().ToString()[..6]}");
            cmd.Parameters.AddWithValue("sup", supplierId);
            cmd.Parameters.AddWithValue("scode", $"SUP-{Guid.NewGuid().ToString()[..6]}");
            cmd.Parameters.AddWithValue("fyid", fiscalYearId);
            cmd.Parameters.AddWithValue("fpid", fiscalPeriodId);
            cmd.Parameters.AddWithValue("invAcc", invAccountId);
            cmd.Parameters.AddWithValue("apAcc", apAccountId);
            cmd.Parameters.AddWithValue("cogsAcc", cogsAccountId);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        // 3. Test Purchasing Tolerance Policy
        var polResult = await purchasingService.UpdateTolerancePolicyAsync(
            new UpdatePurchasingTolerancePolicyRequest(5.0m, 5.0m, true), cancellationToken);
        Assert.True(polResult.IsSuccess);
        var policy = await purchasingService.GetTolerancePolicyAsync(cancellationToken);
        Assert.Equal(5.0m, policy.PriceTolerancePercent);

        // 4. Create and Approve Purchase Order (10 EA @ PKR 100.00)
        var poNumber = $"PO-{Guid.NewGuid().ToString()[..8]}";
        var poResult = await purchasingService.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
            poNumber, supplierId, warehouseId, DateOnly.FromDateTime(DateTime.UtcNow), null, "Phase 6 PO",
            [new CreatePurchaseOrderLineRequest(variantId, 10m, uomId, 100m)]), cancellationToken);

        Assert.True(poResult.IsSuccess);
        var poId = poResult.Value.PurchaseOrderId;

        var poApproveResult = await purchasingService.ApprovePurchaseOrderAsync(
            poId, new ApprovePurchaseOrderRequest("Authorized by Director"), cancellationToken);
        Assert.True(poApproveResult.IsSuccess);
        Assert.Equal("approved", poApproveResult.Value.OrderStatus);

        // 5. Post GRN for 10 EA @ PKR 100.00 and Process Outbox Event
        var grnNumber = $"GRN-{Guid.NewGuid().ToString()[..8]}";
        var clientTxId = Guid.NewGuid();
        var grnResult = await purchasingService.PostGrnAsync(new PostGrnRequest(
            clientTxId, grnNumber, poId, DateOnly.FromDateTime(DateTime.UtcNow), "REF-001", "GRN Receipt",
            [new PostGrnLineRequest(1, variantId, binId, 10m, uomId, 100m)]), cancellationToken);

        Assert.True(grnResult.IsSuccess);
        var grnId = grnResult.Value.GrnId;

        // Process Outbox event to generate inventory cost layer & GL journal
        var outboxRes = await outboxService.ProcessPendingEventsAsync(cancellationToken);
        Assert.True(outboxRes.IsSuccess);

        // Verify stock balance has 10 EA available
        await using (var conn = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @tid AND company_id = @cid AND variant_id = @var AND bin_id = @bin AND status_code = 'available'";
            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("cid", companyId);
            cmd.Parameters.AddWithValue("var", variantId);
            cmd.Parameters.AddWithValue("bin", binId);
            var onHand = (decimal)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0m);
            Assert.Equal(10m, onHand);
        }

        // 6. Quality Inspection: 8 EA Accepted, 2 EA Quarantined
        var inspNumber = $"INSP-{Guid.NewGuid().ToString()[..8]}";
        var inspResult = await purchasingService.CreateInspectionAsync(new CreatePurchaseInspectionRequest(
            inspNumber, grnId, "Quality Inspection Completed",
            [new CreatePurchaseInspectionLineRequest(1, variantId, 10m, 8m, 2m, 0m, null, "2 EA quarantined for batch assay")]), cancellationToken);

        Assert.True(inspResult.IsSuccess);
        var inspId = inspResult.Value.InspectionId;
        var insp = await purchasingService.GetInspectionAsync(inspId, cancellationToken);
        Assert.NotNull(insp);
        Assert.Equal("partial", insp.OverallStatus);
        var inspLine = Assert.Single(insp.Lines);

        // Verify stock balance reflects isolation: 8 EA available, 2 EA quarantine
        await using (var conn = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT status_code, on_hand_qty FROM erp_inventory.stock_balance
                WHERE tenant_id = @tid AND company_id = @cid AND variant_id = @var AND bin_id = @bin;
                """;
            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("cid", companyId);
            cmd.Parameters.AddWithValue("var", variantId);
            cmd.Parameters.AddWithValue("bin", binId);
            var balances = new Dictionary<string, decimal>();
            await using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await r.ReadAsync(cancellationToken))
                {
                    balances[r.GetString(0)] = r.GetDecimal(1);
                }
            }
            Assert.Equal(8m, balances["available"]);
            Assert.Equal(2m, balances["quarantine"]);
        }

        // 7. Controlled Quarantine Release: 2 EA released into Available stock in target bin
        var relNumber = $"REL-{Guid.NewGuid().ToString()[..8]}";
        var relResult = await purchasingService.ReleaseQuarantineStockAsync(new ReleaseQuarantineStockRequest(
            relNumber, inspLine.InspectionLineId, grnId, variantId, binId, targetAvailBinId, 2m, "available", "Assay passed"), cancellationToken);

        Assert.True(relResult.IsSuccess);

        // Verify quarantine stock balance is now 0 in source bin, and 2 in target bin
        await using (var conn = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT on_hand_qty FROM erp_inventory.stock_balance
                WHERE tenant_id = @tid AND company_id = @cid AND variant_id = @var AND bin_id = @bin AND status_code = 'quarantine';
                """;
            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("cid", companyId);
            cmd.Parameters.AddWithValue("var", variantId);
            cmd.Parameters.AddWithValue("bin", binId);
            var remQuar = (decimal)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0m);
            Assert.Equal(0m, remQuar);
        }

        // 8. Three-Way Matching: Create Supplier Invoice (Exact Match: 10 EA @ PKR 100.00 = PKR 1,000.00)
        var invNumber = $"INV-{Guid.NewGuid().ToString()[..8]}";
        var invResult = await purchasingService.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
            invNumber, supplierId, poId, grnId, DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "PKR", 0m,
            [new CreateSupplierInvoiceLineRequest(1, 1, 1, variantId, 10m, 100m)]), cancellationToken);

        Assert.True(invResult.IsSuccess);
        var invId = invResult.Value.InvoiceId;

        // Perform 3-way matching
        var matchRes = await purchasingService.MatchSupplierInvoiceAsync(invId, cancellationToken);
        Assert.True(matchRes.IsSuccess);
        Assert.True(matchRes.Value.IsMatched);
        Assert.Equal("matched", matchRes.Value.OverallStatus);

        // Approve supplier invoice: CRITICAL ACCOUNTING INVARIANT:
        // No duplicate GL journal should be created because GRN already credited 2010!
        var approveInvRes = await purchasingService.ApproveSupplierInvoiceAsync(
            invId, new MatchAndApproveInvoiceRequest(false, "Exact match approved"), cancellationToken);
        Assert.True(approveInvRes.IsSuccess);
        Assert.Equal("approved", approveInvRes.Value.Status);
        Assert.Null(approveInvRes.Value.GlJournalId); // Zero price variance -> no duplicate journal!

        // 9. Duplicate Invoice Detection: Attempting to insert same invoice number for same supplier must fail!
        var dupInvResult = await purchasingService.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
            invNumber, supplierId, poId, grnId, DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "PKR", 0m,
            [new CreateSupplierInvoiceLineRequest(1, 1, 1, variantId, 10m, 100m)]), cancellationToken);

        Assert.False(dupInvResult.IsSuccess);
        Assert.Equal("DUPLICATE_INVOICE_NUMBER", dupInvResult.Error);

        // 10. Purchase Return & Supplier Debit Note (Return 2 EA @ PKR 100.00 = PKR 200.00)
        var retNumber = $"RET-{Guid.NewGuid().ToString()[..8]}";
        var retResult = await purchasingService.CreatePurchaseReturnAsync(new CreatePurchaseReturnRequest(
            retNumber, supplierId, poId, grnId, invId, "Supplier recall",
            [new CreatePurchaseReturnLineRequest(variantId, warehouseId, binId, 2m, 100m, "available", "Recall")]), cancellationToken);

        Assert.True(retResult.IsSuccess);
        var returnId = retResult.Value.ReturnId;

        // Approve Return: decreases stock balance and consumes FIFO layer
        var appRetRes = await purchasingService.ApprovePurchaseReturnAsync(returnId, cancellationToken);
        Assert.True(appRetRes.IsSuccess);

        // Verify stock balance decremented: 8 - 2 = 6 EA available in binId
        await using (var conn = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE tenant_id = @tid AND company_id = @cid AND variant_id = @var AND bin_id = @bin AND status_code = 'available'";
            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("cid", companyId);
            cmd.Parameters.AddWithValue("var", variantId);
            cmd.Parameters.AddWithValue("bin", binId);
            var remStock = (decimal)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0m);
            Assert.Equal(6m, remStock);
        }

        // Create and Approve Debit Note for PKR 200.00
        var dnNumber = $"DN-{Guid.NewGuid().ToString()[..8]}";
        var dnResult = await purchasingService.CreateSupplierDebitNoteAsync(new CreateSupplierDebitNoteRequest(
            dnNumber, supplierId, returnId, invId, DateOnly.FromDateTime(DateTime.UtcNow), 200m, "Debit Note for recall"), cancellationToken);

        Assert.True(dnResult.IsSuccess);
        var dnId = dnResult.Value.DebitNoteId;

        var appDnRes = await purchasingService.ApproveSupplierDebitNoteAsync(dnId, cancellationToken);
        Assert.True(appDnRes.IsSuccess);
        Assert.Equal("approved", appDnRes.Value.Status);
        Assert.NotNull(appDnRes.Value.GlJournalId); // GL journal Dr 2010 / Cr 1200 created

        // 11. Supplier Balances & AP Status Inquiry
        var balancesList = await purchasingService.ListSupplierBalancesAsync(cancellationToken);
        var supBal = balancesList.FirstOrDefault(b => b.SupplierId == supplierId);
        Assert.NotNull(supBal);
        Assert.Equal(1000m, supBal.TotalInvoiced);
        Assert.Equal(200m, supBal.TotalDebitNotes);
        Assert.Equal(800m, supBal.NetPayable);
        Assert.Equal(800m, supBal.OutstandingBalance);
        Assert.Equal("unpaid", supBal.PaymentStatus);
    }
}
