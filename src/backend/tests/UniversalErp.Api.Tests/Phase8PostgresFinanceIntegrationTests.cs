using Npgsql;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Finance;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Finance;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class Phase8PostgresFinanceIntegrationTests
{
    private static readonly string? StagingConnectionString =
        Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
        ?? "Host=127.0.0.1;Port=5433;Database=universal_erp_phase5_dev;Username=erp_dev;Password=local_dev_change_me;Include Error Detail=true;";

    private sealed class TestTenantContextAccessor(TenantContext ctx) : ITenantContextAccessor
    {
        public TenantContext Current => ctx;
    }

    [Fact]
    public async Task Phase8_FullFinanceAndTreasuryLifecycle_IntegrationTest()
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
        var financeRepo = new PostgresFinanceRepository(dataSource);
        var financeService = new FinanceService(financeRepo, accessor);
        var ct = CancellationToken.None;

        var fiscalYearId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();
        var bankGlId = Guid.NewGuid();
        var apGlId = Guid.NewGuid();
        var arGlId = Guid.NewGuid();
        var taxPayableGlId = Guid.NewGuid();
        var revenueGlId = Guid.NewGuid();
        var cogsGlId = Guid.NewGuid();
        var retainedEarningsGlId = Guid.NewGuid();
        var assetGlId = Guid.NewGuid();
        var accumDeprGlId = Guid.NewGuid();
        var deprExpenseGlId = Guid.NewGuid();
        var prepaidExpenseGlId = Guid.NewGuid();

        // 1. Seed Organization, Fiscal Periods, and Chart of Accounts
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
                VALUES (@tid, @tcode, 'Finance Test Tenant', 'active', 'PK', 'PKR', 'Asia/Karachi')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO organization.companies (tenant_id, id, company_code, name, country_code, base_currency_code, timezone, status)
                VALUES (@tid, @cid, @ccode, 'Finance Test Company', 'PK', 'PKR', 'Asia/Karachi', 'active')
                ON CONFLICT (tenant_id, id) DO NOTHING;

                INSERT INTO identity.users (id, email_normalized, display_name, status)
                VALUES (@uid, @uemail, 'Finance Manager', 'active')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
                VALUES (@tid, @cid, @fyid, 'FY2026', '2026-01-01', '2026-12-31', 'open');

                INSERT INTO finance.fiscal_periods (tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status)
                VALUES (@tid, @cid, @fpid, @fyid, 11, 'November 2026', '2026-11-01', '2026-11-30', 'open');

                INSERT INTO finance.accounts (tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active)
                VALUES
                    (@tid, @cid, @bankGlId, '1020', 'Bank Operating Account', 'asset', 'debit', 'PKR', false, true, true),
                    (@tid, @cid, @apGlId, '2010', 'Accounts Payable', 'liability', 'credit', 'PKR', true, false, true),
                    (@tid, @cid, @arGlId, '1100', 'Accounts Receivable', 'asset', 'debit', 'PKR', true, false, true),
                    (@tid, @cid, @taxPayableGlId, '2100', 'Sales Tax Payable', 'liability', 'credit', 'PKR', false, true, true),
                    (@tid, @cid, @revenueGlId, '4010', 'Sales Revenue', 'revenue', 'credit', 'PKR', false, true, true),
                    (@tid, @cid, @cogsGlId, '5010', 'Cost of Goods Sold', 'expense', 'debit', 'PKR', false, true, true),
                    (@tid, @cid, @retainedEarningsGlId, '3020', 'Retained Earnings', 'equity', 'credit', 'PKR', false, true, true),
                    (@tid, @cid, @assetGlId, '1500', 'Machinery & Equipment', 'asset', 'debit', 'PKR', false, true, true),
                    (@tid, @cid, @accumDeprGlId, '1510', 'Accumulated Depreciation', 'asset', 'credit', 'PKR', false, true, true),
                    (@tid, @cid, @deprExpenseGlId, '6050', 'Depreciation Expense', 'expense', 'debit', 'PKR', false, true, true),
                    (@tid, @cid, @prepaidExpenseGlId, '1300', 'Prepaid Insurance', 'asset', 'debit', 'PKR', false, true, true);
            """;
            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("cid", companyId);
            cmd.Parameters.AddWithValue("uid", userId);
            cmd.Parameters.AddWithValue("uemail", "fin_" + userId.ToString("N")[..8] + "@erp.local");
            cmd.Parameters.AddWithValue("tcode", "T-FIN-" + tenantId.ToString("N")[..8]);
            cmd.Parameters.AddWithValue("ccode", "C-FIN-" + companyId.ToString("N")[..8]);
            cmd.Parameters.AddWithValue("fyid", fiscalYearId);
            cmd.Parameters.AddWithValue("fpid", fiscalPeriodId);
            cmd.Parameters.AddWithValue("bankGlId", bankGlId);
            cmd.Parameters.AddWithValue("apGlId", apGlId);
            cmd.Parameters.AddWithValue("arGlId", arGlId);
            cmd.Parameters.AddWithValue("taxPayableGlId", taxPayableGlId);
            cmd.Parameters.AddWithValue("revenueGlId", revenueGlId);
            cmd.Parameters.AddWithValue("cogsGlId", cogsGlId);
            cmd.Parameters.AddWithValue("retainedEarningsGlId", retainedEarningsGlId);
            cmd.Parameters.AddWithValue("assetGlId", assetGlId);
            cmd.Parameters.AddWithValue("accumDeprGlId", accumDeprGlId);
            cmd.Parameters.AddWithValue("deprExpenseGlId", deprExpenseGlId);
            cmd.Parameters.AddWithValue("prepaidExpenseGlId", prepaidExpenseGlId);

            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }

        // 2. Bank Account Creation
        var createBankReq = new CreateBankAccountRequest(
            AccountNumber: "PK88MEZN00011223344",
            AccountName: "Meezan Corporate Account",
            BankName: "Meezan Bank Ltd",
            BranchName: "Clifton Branch",
            SwiftBic: "MEZNPKKA",
            Iban: "PK88MEZN00011223344",
            CurrencyCode: "PKR",
            AccountType: "checking",
            GlAccountId: bankGlId);

        var bankAcc = await financeRepo.CreateBankAccountAsync(tenantId, companyId, createBankReq, ct);
        Assert.NotNull(bankAcc);
        Assert.Equal("Meezan Corporate Account", bankAcc.AccountName);

        // 3. Post a GL Journal to have transactions for reconciliation
        var journalReq = new PostJournalRequest(
            JournalNo: "JRN-FIN-TEST-001",
            JournalDate: new DateOnly(2026, 11, 10),
            FiscalPeriodId: fiscalPeriodId,
            Description: "Customer Payment Received",
            FunctionalCurrency: "PKR",
            SourceModule: "treasury",
            SourceType: "collection",
            SourceId: "CUST-PAY-01",
            Lines: [
                new(bankGlId, "Bank Debit", "PKR", 50000m, 1m, 50000m, 0m),
                new(revenueGlId, "Revenue Credit", "PKR", 50000m, 1m, 0m, 50000m)
            ]);

        var postedJrn = await financeRepo.PostJournalAsync(tenantId, companyId, userId, Guid.NewGuid(), "hash-01", journalReq, ct);
        Assert.NotNull(postedJrn);
        Assert.Equal(50000m, postedJrn.TotalDebit);

        // 4. Import Bank Statement & Reconcile
        var importStmtReq = new ImportBankStatementRequest(
            BankAccountId: bankAcc.BankAccountId,
            StatementNumber: "STMT-2026-11-001",
            StatementDate: new DateOnly(2026, 11, 15),
            OpeningBalance: 0m,
            ClosingBalance: 50000m,
            Lines: [
                new(1, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10), "CUST-PAY-01", "Online Transfer", 50000m, 50000m)
            ]);

        var stmt = await financeRepo.ImportBankStatementAsync(tenantId, companyId, importStmtReq, ct);
        Assert.NotNull(stmt);
        Assert.Single(stmt.Lines);

        var reconResult = await financeRepo.ReconcileBankStatementAsync(tenantId, companyId, userId, stmt.StatementId, "Auto reconciled", ct);
        Assert.NotNull(reconResult);
        Assert.Equal(1, reconResult.MatchedLinesCount);
        Assert.True(reconResult.IsFullyReconciled);

        // 5. Reverse a Journal
        var reversalJrn = await financeRepo.ReverseJournalAsync(tenantId, companyId, userId, postedJrn.JournalId, "Test journal reversal", ct);
        Assert.NotNull(reversalJrn);
        Assert.StartsWith("REV-", reversalJrn.JournalNo);
        Assert.Equal(50000m, reversalJrn.TotalDebit);
        Assert.Equal(50000m, reversalJrn.TotalCredit);

        // 6. Treasury Payment (Vendor Disbursement)
        var pmtReq = new CreateTreasuryPaymentRequest(
            PaymentNumber: "PMT-FIN-001",
            PaymentType: "vendor_disbursement",
            PaymentDate: new DateOnly(2026, 11, 12),
            BankAccountId: bankAcc.BankAccountId,
            DestinationBankAccountId: null,
            Amount: 15000m,
            CurrencyCode: "PKR",
            PaymentMethod: "wire",
            ReferenceNumber: "VEND-DISB-99",
            PayeePayerName: "National Logistics Cell",
            TargetSubledgerId: apGlId,
            Note: "Freight settlement");

        var pmt = await financeRepo.CreateTreasuryPaymentAsync(tenantId, companyId, userId, pmtReq, ct);
        Assert.NotNull(pmt);
        Assert.Equal("cleared", pmt.Status);
        Assert.NotNull(pmt.GlJournalId);

        // 7. Tax Code, Filing, and Settlement
        var taxCodeReq = new CreateTaxCodeRequest("GST-18", "Standard GST 18%", "vat", 18m, taxPayableGlId);
        var taxCode = await financeRepo.CreateTaxCodeAsync(tenantId, companyId, taxCodeReq, ct);
        Assert.NotNull(taxCode);
        Assert.Equal(18m, taxCode.RatePercent);

        var taxFilingReq = new CreateTaxFilingRequest("TAX-FILING-NOV-2026", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "vat", "Monthly return");
        var filing = await financeRepo.CreateTaxFilingAsync(tenantId, companyId, userId, taxFilingReq, ct);
        Assert.NotNull(filing);
        Assert.Equal("filed", filing.Status);

        // Seed positive net tax payable to test settlement GL journal posting
        await using (var updConn = await dataSource.OpenConnectionAsync(ct))
        await using (var updCmd = updConn.CreateCommand())
        {
            updCmd.CommandText = "UPDATE finance.tax_filings SET net_tax_payable = 5000 WHERE id = @id;";
            updCmd.Parameters.AddWithValue("id", filing.FilingId);
            await updCmd.ExecuteNonQueryAsync(ct);
        }

        var settleTaxReq = new SettleTaxFilingRequest(bankAcc.BankAccountId, "TAX-CHQ-1001");
        var settledFiling = await financeRepo.SettleTaxFilingAsync(tenantId, companyId, userId, filing.FilingId, settleTaxReq, ct);
        Assert.Equal("settled", settledFiling.Status);
        Assert.NotNull(settledFiling.GlSettlementJournalId);

        // 8. Recurring Schedule
        var recurringReq = new CreateRecurringScheduleRequest(
            ScheduleName: "Monthly Office Rent Accrual",
            ScheduleType: "accrual",
            Frequency: "monthly",
            StartDate: new DateOnly(2026, 11, 1),
            EndDate: new DateOnly(2027, 10, 31),
            TotalAmount: 120000m,
            PeriodAmount: 10000m,
            DebitAccountId: prepaidExpenseGlId,
            CreditAccountId: bankGlId,
            PeriodsCount: 12,
            AutoReverseAccrual: true,
            DescriptionTemplate: "Office Rent Month {period}");

        var sched = await financeRepo.CreateRecurringScheduleAsync(tenantId, companyId, recurringReq, ct);
        Assert.NotNull(sched);
        Assert.Equal(10000m, sched.PeriodAmount);

        var recJrn = await financeRepo.PostRecurringSchedulePeriodAsync(tenantId, companyId, userId, sched.ScheduleId, ct);
        Assert.NotNull(recJrn);
        Assert.Equal(10000m, recJrn.TotalDebit);

        // 9. Fixed Asset & Depreciation Run
        var assetReq = new CreateFixedAssetRequest(
            AssetCode: "AST-FORKLIFT-01",
            AssetName: "Toyota 3-Ton Forklift",
            AssetCategory: "vehicles",
            AcquisitionDate: new DateOnly(2026, 1, 1),
            AcquisitionCost: 240000m,
            SalvageValue: 24000m,
            UsefulLifeMonths: 48,
            DepreciationMethod: "straight_line",
            AssetGlAccountId: assetGlId,
            AccumulatedDeprAccountId: accumDeprGlId,
            DepreciationExpenseAccountId: deprExpenseGlId);

        var asset = await financeRepo.CreateFixedAssetAsync(tenantId, companyId, assetReq, ct);
        Assert.NotNull(asset);
        Assert.Equal(240000m, asset.CurrentBookValue);

        var deprRunReq = new ExecuteDepreciationRunRequest(new DateOnly(2026, 11, 30), fiscalPeriodId, asset.AssetId);
        var deprRuns = await financeRepo.ExecuteDepreciationRunAsync(tenantId, companyId, userId, deprRunReq, ct);
        Assert.NotEmpty(deprRuns);
        // Monthly straight-line: (240000 - 24000) / 48 = 4500
        Assert.Equal(4500m, deprRuns[0].DepreciationAmount);

        // 10. Fiscal Period Closing and Reopening Workflow
        var checklist = await financeRepo.GetPeriodCloseChecklistAsync(tenantId, companyId, fiscalPeriodId, ct);
        Assert.NotEmpty(checklist);

        await financeRepo.UpdatePeriodCloseChecklistItemAsync(tenantId, companyId, userId, checklist[0].ItemId, true, "Done", ct);

        var softClosed = await financeRepo.SoftCloseFiscalPeriodAsync(tenantId, companyId, userId, fiscalPeriodId, "Month-end cutover", ct);
        Assert.True(softClosed);

        var hardClosed = await financeRepo.CloseFiscalPeriodAsync(tenantId, companyId, userId, fiscalPeriodId, "Final books closed", ct);
        Assert.True(hardClosed);

        var reopened = await financeRepo.ReopenFiscalPeriodAsync(tenantId, companyId, userId, fiscalPeriodId, "CFO approved post-audit adjustment", ct);
        Assert.True(reopened);

        // 11. Control Dashboard & Audit Logs
        var dashboard = await financeRepo.GetControlDashboardAsync(tenantId, companyId, ct);
        Assert.NotNull(dashboard);
        Assert.True(dashboard.TotalAccounts >= 11);

        var auditLogs = await financeRepo.ListAuditLogsAsync(tenantId, companyId, 50, ct);
        Assert.NotEmpty(auditLogs);
    }
}
