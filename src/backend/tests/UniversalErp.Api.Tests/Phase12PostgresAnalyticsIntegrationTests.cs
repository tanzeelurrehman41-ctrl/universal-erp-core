using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UniversalErp.Application.Analytics;
using UniversalErp.Application.Finance;
using UniversalErp.Infrastructure.Analytics;
using UniversalErp.Infrastructure.Finance;
using Xunit;

namespace UniversalErp.Api.Tests;

public class Phase12PostgresAnalyticsIntegrationTests
{
    private static readonly string? StagingConnectionString =
        Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
        ?? "Host=127.0.0.1;Port=5433;Database=universal_erp_phase5_dev;Username=erp_dev;Password=local_dev_change_me;Include Error Detail=true;";

    [Fact]
    public async Task Phase12_FullEnterpriseAnalyticsAndReconciliation_PostgresIntegrationTest()
    {
        if (string.IsNullOrWhiteSpace(StagingConnectionString)) return;

        var ct = CancellationToken.None;
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var fiscalYearId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();

        var dataSource = NpgsqlDataSource.Create(StagingConnectionString);
        var analyticsRepo = new PostgresAnalyticsRepository(dataSource);
        var analyticsService = new AnalyticsService(analyticsRepo);
        var financeRepo = new PostgresFinanceRepository(dataSource);

        var acc1200Id = Guid.NewGuid();
        var acc2010Id = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var deptId = Guid.NewGuid();
        var empId = Guid.NewGuid();

        // 1. Seed Tenant, Company, User, Currencies, Fiscal Period, and GL Accounts
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = $"SELECT set_config('app.tenant_id', '{tenantId}', true), set_config('app.company_id', '{companyId}', true);";
            await cmd.ExecuteNonQueryAsync(ct);

            var tcode = $"T-{tenantId.ToString("N")[..8]}";
            var ccode = $"C-{companyId.ToString("N")[..8]}";
            var uemail = $"analytics_{tenantId.ToString("N")[..6]}@example.com";

            cmd.CommandText = """
                INSERT INTO platform.tenants (id, tenant_code, display_name, status, default_country_code, default_currency_code, default_timezone)
                VALUES (@tId, @tcode, 'Phase 12 Analytics Tenant', 'active', 'PK', 'PKR', 'Asia/Karachi')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO organization.companies (tenant_id, id, company_code, name, country_code, base_currency_code, timezone, status)
                VALUES (@tId, @cId, @ccode, 'Phase 12 Analytics Corp', 'PK', 'PKR', 'Asia/Karachi', 'active')
                ON CONFLICT (tenant_id, id) DO NOTHING;

                INSERT INTO identity.users (id, email_normalized, display_name, status)
                VALUES (@uId, @uemail, 'Analytics Director', 'active')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO finance.currencies (code, name, minor_unit, is_active)
                VALUES ('PKR', 'Pakistani Rupee', 2, true)
                ON CONFLICT (code) DO NOTHING;

                INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
                VALUES (@tId, @cId, @fyid, 'FY2026', '2026-01-01', '2026-12-31', 'open');

                INSERT INTO finance.fiscal_periods (tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status)
                VALUES (@tId, @cId, @fpid, @fyid, 11, 'November 2026', '2026-11-01', '2026-11-30', 'open');

                INSERT INTO finance.accounts (
                    tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active
                ) VALUES 
                    (@tId, @cId, @acc1200, '1200', 'Merchandise Inventory (FIFO)', 'asset', 'debit', 'PKR', true, false, true),
                    (@tId, @cId, @acc2010, '2010', 'Accounts Payable', 'liability', 'credit', 'PKR', true, false, true);

                -- Seed Inventory Status, Warehouse, UOM, Bin, Product, Variant
                INSERT INTO erp_inventory.stock_status (tenant_id, status_code, description, is_reservable, is_pickable)
                VALUES (@tId, 'available', 'Available Stock', true, true)
                ON CONFLICT DO NOTHING;

                INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension, decimal_places)
                VALUES (@tId, @uomId, 'EA', 'Each', 'count', 0)
                ON CONFLICT DO NOTHING;

                INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name, is_active)
                VALUES (@tId, @cId, @whId, 'WH-MAIN', 'Main Distribution Center', true);

                INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, warehouse_id, bin_id, code, bin_type, is_active, is_pick_face, storage_restriction)
                VALUES (@tId, @cId, @whId, @binId, 'BIN-A1', 'storage', true, false, 'standard');

                INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, product_type, is_active)
                VALUES (@tId, @pId, 'PRD-MODEL-A', 'Industrial Flange Assembly', 'stock', true);

                INSERT INTO erp_inventory.product_variant (tenant_id, variant_id, product_id, sku, variant_name, base_uom_id, costing_method, is_active)
                VALUES (@tId, @vId, @pId, 'SKU-MODEL-A-01', 'Flange 50mm Standard', @uomId, 'fifo', true);

                -- Seed Model A FIFO cost layer: 50 EA @ PKR 45 = PKR 2,250
                INSERT INTO erp_inventory.inventory_cost_layer (
                    tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id,
                    status_code, source_document_type, source_document_id, source_line_number,
                    received_at, original_qty, remaining_qty, unit_cost, currency_code
                ) VALUES (
                    @tId, @cId, gen_random_uuid(), @vId, @whId, @binId,
                    'available', 'goods_received_note', gen_random_uuid(), 1,
                    NOW(), 50, 50, 45.00, 'PKR'
                );

                -- Seed HR Department and Employee
                INSERT INTO hr.departments (id, tenant_id, company_id, department_code, name, is_active)
                VALUES (@deptId, @tId, @cId, 'ENG', 'Engineering', true);

                INSERT INTO hr.employees (
                    id, tenant_id, company_id, employee_code, first_name, last_name, email,
                    department_id, employment_type, status, hire_date,
                    cnic_or_national_id, bank_account_number, bank_name, bank_iban
                ) VALUES (
                    @empId, @tId, @cId, 'EMP-1001', 'Rashid', 'Khan', 'rashid@example.com',
                    @deptId, 'full_time', 'active', '2024-01-01',
                    '42101-1234567-1', '12345678901234', 'Habib Bank Limited', 'PK36HABB0000123456789012'
                );

                INSERT INTO hr.compensation_structures (
                    id, tenant_id, company_id, employee_id, effective_date,
                    base_salary_pkr, medical_allowance_pkr, conveyance_allowance_pkr,
                    gross_salary_pkr, is_active
                ) VALUES (
                    gen_random_uuid(), @tId, @cId, @empId, '2024-01-01',
                    150000.00, 15000.00, 10000.00,
                    175000.00, true
                );
            """;
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("cId", companyId);
            cmd.Parameters.AddWithValue("uId", userId);
            cmd.Parameters.AddWithValue("tcode", tcode);
            cmd.Parameters.AddWithValue("ccode", ccode);
            cmd.Parameters.AddWithValue("uemail", uemail);
            cmd.Parameters.AddWithValue("fyid", fiscalYearId);
            cmd.Parameters.AddWithValue("fpid", fiscalPeriodId);
            cmd.Parameters.AddWithValue("acc1200", acc1200Id);
            cmd.Parameters.AddWithValue("acc2010", acc2010Id);
            cmd.Parameters.AddWithValue("uomId", uomId);
            cmd.Parameters.AddWithValue("whId", warehouseId);
            cmd.Parameters.AddWithValue("binId", binId);
            cmd.Parameters.AddWithValue("pId", productId);
            cmd.Parameters.AddWithValue("vId", variantId);
            cmd.Parameters.AddWithValue("deptId", deptId);
            cmd.Parameters.AddWithValue("empId", empId);
            await cmd.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }

        // 2. Post Balanced Journal Entry via Finance Repository
        // Line 1: Debit GL Account 1200 (Inventory Asset) PKR 2,250
        // Line 2: Credit GL Account 2010 (Accounts Payable) PKR 2,250
        var postJournalReq = new PostJournalRequest(
            JournalNo: "JRN-ANALYTICS-001",
            JournalDate: new DateOnly(2026, 11, 15),
            FiscalPeriodId: fiscalPeriodId,
            Description: "Initial FIFO Layer Capitalization",
            FunctionalCurrency: "PKR",
            SourceModule: "purchasing",
            SourceType: "goods_receipt",
            SourceId: "GRN-2026-001",
            Lines: [
                new(acc1200Id, "FIFO Layer 1200 Debit", "PKR", 2250.00m, 1.0m, 2250.00m, 0.00m),
                new(acc2010Id, "AP Accrual 2010 Credit", "PKR", 2250.00m, 1.0m, 0.00m, 2250.00m)
            ]
        );

        var postedJournal = await financeRepo.PostJournalAsync(
            tenantId, companyId, userId, Guid.NewGuid(), "hash-analytics-01", postJournalReq, ct
        );
        Assert.NotNull(postedJournal);
        Assert.Equal(2250.00m, postedJournal.TotalDebit);
        Assert.Equal(2250.00m, postedJournal.TotalCredit);

        // ── STEP 1: Platform Reconciliation Check ──────────────────────────────────────
        // General Ledger must be balanced (Debits == Credits).
        // FIFO active layers must equal GL Account 1200 (PKR 2,250 with 0.00 variance).
        var recReport = await analyticsService.GetPlatformReconciliationReportAsync(tenantId, companyId, ct);
        Assert.NotNull(recReport);
        Assert.True(recReport.GlReconciliation.IsBalanced, "GL debits must equal credits");
        Assert.Equal(0m, recReport.GlReconciliation.Discrepancy);
        Assert.True(recReport.FifoReconciliation.IsReconciled, "FIFO layer valuation must reconcile with GL Account 1200");
        Assert.Equal(2250.00m, recReport.FifoReconciliation.FifoActiveLayersValuationPkr);
        Assert.Equal(2250.00m, recReport.FifoReconciliation.GlAccount1200ValuationPkr);
        Assert.Equal(0.00m, recReport.FifoReconciliation.VariancePkr);
        Assert.True(recReport.AllReconciled, "All platform reconciliations must pass");

        // ── STEP 2: KPI Definitions & Snapshots ─────────────────────────────────────────
        var createKpiReq = new CreateKpiDefinitionRequest(
            Code: "KPI-SALES-REV",
            Name: "Monthly Sales Revenue",
            Category: "financial",
            Unit: "PKR",
            TargetValue: 1_000_000m,
            CalculationMethod: "SUM(sales_invoices)",
            Frequency: "monthly",
            Description: "Total monthly invoiced revenue"
        );
        var createdKpi = await analyticsService.CreateKpiDefinitionAsync(tenantId, companyId, createKpiReq, ct);
        Assert.NotNull(createdKpi);
        Assert.Equal("KPI-SALES-REV", createdKpi.Code);
        Assert.Equal(1_000_000m, createdKpi.TargetValue);

        var kpiList = await analyticsService.GetKpiDefinitionsAsync(tenantId, companyId, ct: ct);
        Assert.NotEmpty(kpiList);
        Assert.Contains(kpiList, k => k.Code == "KPI-SALES-REV");

        // Record Snapshot for this KPI
        var snapshotReq = new CreateKpiSnapshotRequest(
            SnapshotDate: DateOnly.FromDateTime(DateTime.UtcNow),
            KpiCode: "KPI-SALES-REV",
            ActualValue: 1_050_000m,
            Notes: "Exceeded target by 5%"
        );
        var snapshot = await analyticsService.RecordKpiSnapshotAsync(tenantId, companyId, snapshotReq, ct);
        Assert.NotNull(snapshot);
        Assert.Equal(50_000m, snapshot.Variance);
        Assert.Equal(5.00m, snapshot.VariancePercentage);
        Assert.Equal("on_track", snapshot.Status);

        var snapshotsList = await analyticsService.GetKpiSnapshotsAsync(tenantId, companyId, "KPI-SALES-REV", ct: ct);
        Assert.NotEmpty(snapshotsList);
        Assert.Equal(1_050_000m, snapshotsList[0].ActualValue);

        // ── STEP 3: Executive Dashboard ───────────────────────────────────────────────
        var filter = new AnalyticsDateRangeFilter();
        var dash = await analyticsService.GetExecutiveDashboardAsync(tenantId, companyId, filter, includeConfidentialSalary: false, ct: ct);
        Assert.NotNull(dash);
        Assert.True(dash.Financials.IsGeneralLedgerBalanced);
        Assert.True(dash.SupplyChain.IsFifoReconciled);
        Assert.Equal(2250.00m, dash.SupplyChain.FifoValuationPkr);

        // ── STEP 4: Inventory Valuation & Ageing Analytics ─────────────────────────────
        var invAnalytics = await analyticsService.GetInventoryValuationAgeingAsync(tenantId, companyId, filter, ct);
        Assert.NotNull(invAnalytics);
        Assert.Equal(2250.00m, invAnalytics.TotalFifoLayerValuationPkr);
        Assert.True(invAnalytics.IsReconciled);
        Assert.Equal(0.00m, invAnalytics.ValuationVariancePkr);

        // ── STEP 5: Workforce Cost Analytics & Security Gate ───────────────────────────
        // (A) Without confidential salary access: Gross and Net are 0, IsSalaryMasked is true
        var maskedWf = await analyticsService.GetWorkforceCostAnalyticsAsync(tenantId, companyId, filter, includeConfidentialSalary: false, ct);
        Assert.NotNull(maskedWf);
        Assert.True(maskedWf.IsSalaryMasked, "Must be flagged as salary masked");
        Assert.Equal(0m, maskedWf.MonthlyGrossPayrollPkr);
        Assert.Equal(0m, maskedWf.MonthlyNetPayrollPkr);
        Assert.True(maskedWf.TotalActiveHeadcount >= 1);

        // (B) With confidential salary access: Gross salary is computed
        var unmaskedWf = await analyticsService.GetWorkforceCostAnalyticsAsync(tenantId, companyId, filter, includeConfidentialSalary: true, ct);
        Assert.NotNull(unmaskedWf);
        Assert.False(unmaskedWf.IsSalaryMasked, "Must not be flagged as masked when permission granted");
        Assert.True(unmaskedWf.MonthlyGrossPayrollPkr >= 175_000m);

        // ── STEP 6: Multi-format Export Generator ──────────────────────────────────────
        // CSV Export
        var csvExportReq = new ExportRequest("reconciliation", "csv");
        var csvResp = await analyticsService.ExportAnalyticsDataAsync(tenantId, companyId, csvExportReq, false, ct);
        Assert.NotNull(csvResp);
        Assert.EndsWith(".csv", csvResp.FileName);
        Assert.Equal("text/csv", csvResp.ContentType);
        Assert.Equal(2, csvResp.RowCount);

        // TSV Export
        var tsvExportReq = new ExportRequest("reconciliation", "tsv");
        var tsvResp = await analyticsService.ExportAnalyticsDataAsync(tenantId, companyId, tsvExportReq, false, ct);
        Assert.NotNull(tsvResp);
        Assert.EndsWith(".tsv", tsvResp.FileName);
        Assert.Equal("text/tab-separated-values", tsvResp.ContentType);
        Assert.Equal(2, tsvResp.RowCount);

        // ── STEP 7: Tenant RLS Isolation Check ─────────────────────────────────────────
        var tenant2Id = Guid.NewGuid();
        var company2Id = Guid.NewGuid();
        var tenant2Kpis = await analyticsService.GetKpiDefinitionsAsync(tenant2Id, company2Id, ct: ct);
        Assert.Empty(tenant2Kpis); // Tenant 2 cannot see Tenant 1's KPIs

        var tenant2Rec = await analyticsService.GetPlatformReconciliationReportAsync(tenant2Id, company2Id, ct);
        Assert.Equal(0m, tenant2Rec.FifoReconciliation.FifoActiveLayersValuationPkr);
        Assert.Equal(0m, tenant2Rec.GlReconciliation.TotalDebits);
    }
}
