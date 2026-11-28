using Npgsql;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Sales;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Sales;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class Phase7PostgresSalesIntegrationTests
{
    private static readonly string? StagingConnectionString =
        Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
        ?? "Host=127.0.0.1;Port=5433;Database=universal_erp_phase5_dev;Username=erp_dev;Password=local_dev_change_me;Include Error Detail=true;";

    private sealed class TestTenantContextAccessor(TenantContext ctx) : ITenantContextAccessor
    {
        public TenantContext Current => ctx;
    }

    [Fact]
    public async Task Phase7_FullSalesAndOrderToCashLifecycle_IntegrationTest()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var tenantContext = new TenantContext(
            TenantId: tenantId,
            CompanyId: companyId,
            UserId: userId);

        var dataSource = NpgsqlDataSource.Create(StagingConnectionString!);
        var accessor = new TestTenantContextAccessor(tenantContext);
        var salesRepo = new PostgresSalesRepository(dataSource, accessor);
        var salesService = new SalesService(salesRepo, accessor);
        var ct = CancellationToken.None;

        var warehouseId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var fiscalYearId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();
        var invAccountId = Guid.NewGuid();
        var arAccountId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();
        var taxAccountId = Guid.NewGuid();
        var revAccountId = Guid.NewGuid();
        var cogsAccountId = Guid.NewGuid();

        // 1. Seed Foundation & Master Data
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var tx = await conn.BeginTransactionAsync(ct);

            await using (var setCmd = conn.CreateCommand())
            {
                setCmd.Transaction = tx;
                setCmd.CommandText = $"SET LOCAL app.tenant_id = '{tenantId}'; SET LOCAL app.company_id = '{companyId}';";
                await setCmd.ExecuteNonQueryAsync(ct);
            }

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO platform.tenants (id, tenant_code, display_name, status, default_country_code, default_currency_code, default_timezone)
                VALUES (@tid, @tcode, 'Sales Test Tenant', 'active', 'PK', 'PKR', 'Asia/Karachi')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO organization.companies (tenant_id, id, company_code, name, country_code, base_currency_code, timezone, status)
                VALUES (@tid, @cid, @ccode, 'Sales Test Company', 'PK', 'PKR', 'Asia/Karachi', 'active')
                ON CONFLICT (tenant_id, id) DO NOTHING;

                INSERT INTO identity.users (id, email_normalized, display_name, status)
                VALUES (@uid, @uemail, 'Sales Director', 'active')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO erp_inventory.stock_status (tenant_id, status_code, description, is_reservable, is_pickable)
                VALUES (@tid, 'available', 'Sellable Available Stock', true, true)
                ON CONFLICT (tenant_id, status_code) DO NOTHING;

                INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name, is_active)
                VALUES (@tid, @cid, @wh, 'WH-SALES-01', 'Sales Warehouse', true)
                ON CONFLICT (tenant_id, company_id, warehouse_id) DO NOTHING;

                INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, bin_id, warehouse_id, code, bin_type, is_active)
                VALUES (@tid, @cid, @bin, @wh, 'BIN-PICK-01', 'storage', true)
                ON CONFLICT (tenant_id, company_id, bin_id) DO NOTHING;

                INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension, decimal_places)
                VALUES (@tid, @uom, 'EA', 'Each', 'count', 0)
                ON CONFLICT (tenant_id, uom_id) DO NOTHING;

                INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, product_type, is_active)
                VALUES (@tid, @pid, @pcode, 'High Velocity Widget', 'stock', true)
                ON CONFLICT (tenant_id, product_id) DO NOTHING;

                INSERT INTO erp_inventory.product_variant (tenant_id, variant_id, product_id, sku, variant_name, base_uom_id, costing_method)
                VALUES (@tid, @var, @pid, @sku, 'High Velocity Widget Standard', @uom, 'fifo')
                ON CONFLICT (tenant_id, variant_id) DO NOTHING;

                INSERT INTO erp_sales.customer
                    (tenant_id, company_id, customer_id, customer_code, name, customer_type, credit_limit, outstanding_balance, currency_code, payment_terms_days, is_active)
                VALUES
                    (@tid, @cid, @cust, @custcode, 'Apex Retail Enterprises', 'corporate', 20000.0000, 0.0000, 'PKR', 30, true)
                ON CONFLICT (tenant_id, company_id, customer_id) DO NOTHING;

                -- Seed Chart of Accounts
                INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
                VALUES (@tid, @cid, @fyid, 'FY2026-SALES', CURRENT_DATE - INTERVAL '30 days', CURRENT_DATE + INTERVAL '300 days', 'open')
                ON CONFLICT (tenant_id, company_id, code) DO NOTHING;

                INSERT INTO finance.fiscal_periods (tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status)
                VALUES (@tid, @cid, @fpid, @fyid, 1, 'Current Period', CURRENT_DATE - INTERVAL '10 days', CURRENT_DATE + INTERVAL '20 days', 'open')
                ON CONFLICT (tenant_id, company_id, fiscal_year_id, period_no) DO NOTHING;

                INSERT INTO finance.accounts (tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, is_control_account, allow_manual_posting)
                VALUES
                    (@tid, @cid, @invAcc, '1200', 'Merchandise Inventory Asset', 'asset', 'debit', true, false),
                    (@tid, @cid, @arAcc, '1100', 'Accounts Receivable Trade Debtors', 'asset', 'debit', true, false),
                    (@tid, @cid, @bankAcc, '1020', 'Bank Operating Account', 'asset', 'debit', false, true),
                    (@tid, @cid, @taxAcc, '2100', 'Sales Tax Payable', 'liability', 'credit', false, true),
                    (@tid, @cid, @revAcc, '4010', 'Sales Revenue', 'revenue', 'credit', false, true),
                    (@tid, @cid, @cogsAcc, '5010', 'Cost of Goods Sold (COGS)', 'expense', 'debit', false, true)
                ON CONFLICT (tenant_id, company_id, account_code) DO NOTHING;

                -- Seed 10 valued physical units in BIN-PICK-01
                INSERT INTO erp_inventory.inventory_cost_layer
                    (tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id, status_code, source_document_type, source_document_id, original_qty, remaining_qty, unit_cost, currency_code, received_at)
                VALUES
                    (@tid, @cid, gen_random_uuid(), @var, @wh, @bin, 'available', 'grn', gen_random_uuid(), 10.0000, 10.0000, 45.0000, 'PKR', now())
                ON CONFLICT DO NOTHING;

                INSERT INTO erp_inventory.stock_balance
                    (tenant_id, company_id, balance_id, warehouse_id, bin_id, variant_id, status_code, on_hand_qty)
                VALUES
                    (@tid, @cid, gen_random_uuid(), @wh, @bin, @var, 'available', 10.0000)
                ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid))
                DO UPDATE SET on_hand_qty = 10.0000;
            """;

            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("tcode", $"T-{tenantId.ToString()[..8]}");
            cmd.Parameters.AddWithValue("cid", companyId);
            cmd.Parameters.AddWithValue("ccode", $"C-{companyId.ToString()[..8]}");
            cmd.Parameters.AddWithValue("uid", userId);
            cmd.Parameters.AddWithValue("uemail", $"user-{userId.ToString()[..8]}@sales.test");
            cmd.Parameters.AddWithValue("wh", warehouseId);
            cmd.Parameters.AddWithValue("bin", binId);
            cmd.Parameters.AddWithValue("uom", uomId);
            cmd.Parameters.AddWithValue("pid", productId);
            cmd.Parameters.AddWithValue("pcode", $"PROD-{productId.ToString()[..8]}");
            cmd.Parameters.AddWithValue("var", variantId);
            cmd.Parameters.AddWithValue("sku", $"SKU-{variantId.ToString()[..8]}");
            cmd.Parameters.AddWithValue("cust", customerId);
            cmd.Parameters.AddWithValue("custcode", $"CUST-{customerId.ToString()[..8]}");
            cmd.Parameters.AddWithValue("fyid", fiscalYearId);
            cmd.Parameters.AddWithValue("fpid", fiscalPeriodId);
            cmd.Parameters.AddWithValue("invAcc", invAccountId);
            cmd.Parameters.AddWithValue("arAcc", arAccountId);
            cmd.Parameters.AddWithValue("bankAcc", bankAccountId);
            cmd.Parameters.AddWithValue("taxAcc", taxAccountId);
            cmd.Parameters.AddWithValue("revAcc", revAccountId);
            cmd.Parameters.AddWithValue("cogsAcc", cogsAccountId);

            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }

        // 2. Step 1: Customer Quotation Creation & Approval
        var qNo = $"QT-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var createQ = await salesService.CreateQuotationAsync(new CreateSalesQuotationRequest(
            qNo, customerId, warehouseId, Lines: [
                new CreateSalesQuotationLineRequest(variantId, binId, 5.0m, uomId, 100.0m, TaxRate: 0.10m)
            ]), ct);

        Assert.True(createQ.IsSuccess, createQ.Error);
        var quotation = createQ.Value!;
        Assert.Equal("draft", quotation.Status);
        Assert.Equal(500.0m, quotation.SubtotalAmount);
        Assert.Equal(50.0m, quotation.TaxAmount);
        Assert.Equal(550.0m, quotation.TotalAmount);

        // Approve quotation
        var approveQ = await salesService.ApproveQuotationAsync(quotation.QuotationId, new ApproveSalesQuotationRequest("Discount and pricing approved"), ct);
        Assert.True(approveQ.IsSuccess, approveQ.Error);
        Assert.Equal("approved", approveQ.Value!.Status);

        // 3. Step 2 & 3: Convert Quotation to Sales Order & Credit Check
        var soNo = $"SO-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var convertRes = await salesService.ConvertQuotationToOrderAsync(quotation.QuotationId, new ConvertQuotationToOrderRequest(soNo), ct);
        Assert.True(convertRes.IsSuccess, convertRes.Error);
        var order = convertRes.Value!;
        Assert.Equal(soNo, order.OrderNumber);
        Assert.Equal("draft", order.OrderStatus);

        // Perform Credit Check
        var creditCheck = await salesService.CheckCustomerCreditAsync(customerId, order.TotalAmount, ct);
        Assert.True(creditCheck.IsSuccess, creditCheck.Error);
        Assert.True(creditCheck.Value!.IsAllowed);
        Assert.False(creditCheck.Value.RequiresOverride);

        // Approve and Confirm Order (reserves stock via ATP)
        var approveOrder = await salesService.ApproveOrderAsync(order.OrderId, new ApproveSalesOrderRequest("Credit check passed"), ct);
        Assert.True(approveOrder.IsSuccess, approveOrder.Error);

        var confirmOrder = await salesService.ConfirmOrderAsync(order.OrderId, ct);
        Assert.True(confirmOrder.IsSuccess, confirmOrder.Error);
        Assert.Equal("confirmed", confirmOrder.Value!.OrderStatus);

        // 4. Step 4 & 5: Warehouse Dispatch & Picking Confirmation (FIFO & COGS relief)
        var dspNo = $"DSP-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var createDispatch = await salesService.CreateDispatchAsync(new CreateSalesDispatchRequest(
            dspNo, order.OrderId, Lines: [
                new CreateSalesDispatchLineRequest(1, variantId, binId, 5.0m, uomId)
            ]), ct);
        Assert.True(createDispatch.IsSuccess, createDispatch.Error);
        var dispatch = createDispatch.Value!;
        Assert.Equal("draft", dispatch.Status);

        // Confirm dispatch -> Relieves FIFO layer and posts COGS journal
        var confirmDispatch = await salesService.ConfirmDispatchAsync(dispatch.DispatchId, new ConfirmSalesDispatchRequest(MarkDelivered: true, Note: "Handed over to carrier"), ct);
        Assert.True(confirmDispatch.IsSuccess, confirmDispatch.Error);
        var confirmedDsp = confirmDispatch.Value!;
        Assert.Equal("delivered", confirmedDsp.Status);
        Assert.NotNull(confirmedDsp.GlJournalId);

        // Verify Dispatch GL Journal: Dr 5010 (PKR 225) / Cr 1200 (PKR 225)
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using (var setCmd = conn.CreateCommand())
            {
                setCmd.CommandText = $"SET LOCAL app.tenant_id = '{tenantId}'; SET LOCAL app.company_id = '{companyId}';";
                await setCmd.ExecuteNonQueryAsync(ct);
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT a.account_code, jl.debit_functional, jl.credit_functional
                FROM finance.journal_lines jl
                JOIN finance.accounts a ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
                WHERE jl.tenant_id = @tid AND jl.company_id = @cid AND jl.journal_id = @jid
                ORDER BY a.account_code;
            """;
            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("cid", companyId);
            cmd.Parameters.AddWithValue("jid", confirmedDsp.GlJournalId.Value);

            var lines = new List<(string Code, decimal Debit, decimal Credit)>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                lines.Add((reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2)));
            }

            Assert.Equal(2, lines.Count);
            // Cr 1200 PKR 225 (5 units @ PKR 45)
            Assert.Equal("1200", lines[0].Code);
            Assert.Equal(0.0m, lines[0].Debit);
            Assert.Equal(225.0m, lines[0].Credit);
            // Dr 5010 PKR 225
            Assert.Equal("5010", lines[1].Code);
            Assert.Equal(225.0m, lines[1].Debit);
            Assert.Equal(0.0m, lines[1].Credit);
        }

        // 5. Step 6: Generate Sales Invoice from Dispatch (Zero Duplicate COGS)
        var invNo = $"INV-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var clientTxId = Guid.NewGuid();
        var createInv = await salesService.CreateInvoiceFromDispatchAsync(new CreateInvoiceFromDispatchRequest(
            clientTxId, invNo, confirmedDsp.DispatchId, PaymentMethod: "credit"), ct);
        Assert.True(createInv.IsSuccess, createInv.Error);
        var invoice = createInv.Value!;
        Assert.Equal(invNo, invoice.InvoiceNumber);
        Assert.Equal(550.0m, invoice.TotalAmount);
        Assert.NotNull(invoice.GlJournalId);

        // Verify Sales Invoice GL Journal: Dr 1100 (AR PKR 550) / Cr 2100 (Tax PKR 50) & Cr 4010 (Rev PKR 500)
        // AND assert NO line for 5010 (COGS) or 1200 (Inventory Asset) exists in this invoice's journal!
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using (var setCmd = conn.CreateCommand())
            {
                setCmd.CommandText = $"SET LOCAL app.tenant_id = '{tenantId}'; SET LOCAL app.company_id = '{companyId}';";
                await setCmd.ExecuteNonQueryAsync(ct);
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT a.account_code, jl.debit_functional, jl.credit_functional
                FROM finance.journal_lines jl
                JOIN finance.accounts a ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
                WHERE jl.tenant_id = @tid AND jl.company_id = @cid AND jl.journal_id = @jid
                ORDER BY a.account_code;
            """;
            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("cid", companyId);
            cmd.Parameters.AddWithValue("jid", invoice.GlJournalId.Value);

            var invLines = new List<(string Code, decimal Debit, decimal Credit)>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                invLines.Add((reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2)));
            }

            Assert.Equal(3, invLines.Count);
            // Dr 1100 PKR 550
            Assert.Equal("1100", invLines[0].Code);
            Assert.Equal(550.0m, invLines[0].Debit);
            Assert.Equal(0.0m, invLines[0].Credit);
            // Cr 2100 PKR 50
            Assert.Equal("2100", invLines[1].Code);
            Assert.Equal(0.0m, invLines[1].Debit);
            Assert.Equal(50.0m, invLines[1].Credit);
            // Cr 4010 PKR 500
            Assert.Equal("4010", invLines[2].Code);
            Assert.Equal(0.0m, invLines[2].Debit);
            Assert.Equal(500.0m, invLines[2].Credit);

            // Assert NO duplicate COGS / stock relief
            Assert.DoesNotContain(invLines, l => l.Code is "5010" or "1200");
        }

        // Verify customer outstanding balance increased by 550
        var custAfterInv = await salesService.GetCustomerAsync(customerId, ct);
        Assert.Equal(550.0m, custAfterInv!.OutstandingBalance);

        // 6. Step 7: Customer Receipt & Payment Allocation
        var rctNo = $"RCT-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var createReceipt = await salesService.CreateReceiptAsync(new CreateCustomerReceiptRequest(
            rctNo, customerId, 350.0m, PaymentMethod: "bank_transfer", Allocations: [
                new CustomerReceiptAllocationRequest(invoice.InvoiceId, 350.0m)
            ]), ct);
        Assert.True(createReceipt.IsSuccess, createReceipt.Error);
        var receipt = createReceipt.Value!;
        Assert.Equal(350.0m, receipt.Amount);
        Assert.Equal(0.0m, receipt.UnallocatedAmount);
        Assert.NotNull(receipt.GlJournalId);

        // Verify Customer balance decreased to 200 (550 - 350)
        var custAfterReceipt = await salesService.GetCustomerAsync(customerId, ct);
        Assert.Equal(200.0m, custAfterReceipt!.OutstandingBalance);

        // 7. Step 8: Customer Sales Return & Credit Note
        var retNo = $"RET-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var createReturn = await salesService.CreateReturnAsync(new CreateSalesReturnRequest(
            retNo, customerId, InvoiceId: invoice.InvoiceId, OrderId: order.OrderId, Lines: [
                new CreateSalesReturnLineRequest(variantId, warehouseId, binId, 1.0m, 100.0m, UnitCost: 45.0m, Reason: "Customer ordered excess")
            ]), ct);
        Assert.True(createReturn.IsSuccess, createReturn.Error);
        var returnObj = createReturn.Value!;
        Assert.Equal("draft", returnObj.Status);

        // Approve return -> Restocks bin, creates new FIFO cost layer, posts Dr 1200 / Cr 5010
        var approveReturn = await salesService.ApproveReturnAsync(returnObj.ReturnId, ct);
        Assert.True(approveReturn.IsSuccess, approveReturn.Error);
        var approvedRet = approveReturn.Value!;
        Assert.Equal("approved", approvedRet.Status);
        Assert.NotNull(approvedRet.GlJournalId);

        // Issue Credit Note
        var cnNo = $"CRN-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var createCn = await salesService.CreateCreditNoteAsync(new CreateSalesCreditNoteRequest(
            cnNo, customerId, 100.0m, TaxAmount: 10.0m, ReturnId: approvedRet.ReturnId, InvoiceId: invoice.InvoiceId, Reason: "Restocked return credit"), ct);
        Assert.True(createCn.IsSuccess, createCn.Error);
        var creditNote = createCn.Value!;
        Assert.Equal("draft", creditNote.Status);

        // Approve Credit Note -> Posts Dr 4010 & 2100 / Cr 1100, reduces customer balance by 110
        var approveCn = await salesService.ApproveCreditNoteAsync(creditNote.CreditNoteId, ct);
        Assert.True(approveCn.IsSuccess, approveCn.Error);
        var approvedCn = approveCn.Value!;
        Assert.Equal("approved", approvedCn.Status);
        Assert.NotNull(approvedCn.GlJournalId);

        // Customer balance should now be 90 (200 - 110)
        var custAfterCn = await salesService.GetCustomerAsync(customerId, ct);
        Assert.Equal(90.0m, custAfterCn!.OutstandingBalance);

        // 8. Step 9 & 10: Accounts Receivable Aging & Customer Statement
        var agingList = await salesService.ListCustomerArAgingAsync(ct);
        var custAging = agingList.FirstOrDefault(a => a.CustomerId == customerId);
        Assert.NotNull(custAging);
        Assert.Equal(90.0m, custAging.NetOutstanding);
        Assert.Equal(1, custAging.OpenInvoiceCount);

        var statement = await salesService.GetCustomerStatementAsync(
            customerId,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            ct);

        Assert.NotNull(statement);
        Assert.Equal(customerId, statement.CustomerId);
        Assert.NotEmpty(statement.Items);
        Assert.Contains(statement.Items, i => i.ReferenceNumber == invNo && i.DebitAmount == 550.0m);
        Assert.Contains(statement.Items, i => i.ReferenceNumber == rctNo && i.CreditAmount == 350.0m);
        Assert.Contains(statement.Items, i => i.ReferenceNumber == cnNo && i.CreditAmount == 110.0m);
        Assert.Equal(90.0m, statement.ClosingBalance);
    }
}
