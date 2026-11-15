using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Finance;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class Phase8FinanceServiceTests
{
    private sealed class FakeTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current => context;
    }

    private sealed class MockPhase8FinanceRepository : IFinanceRepository
    {
        public readonly List<AccountSummary> Accounts = [];
        public readonly List<JournalSummary> Journals = [];
        public readonly List<BankAccountSummary> BankAccounts = [];
        public readonly List<BankStatementSummary> Statements = [];
        public readonly List<TreasuryPaymentSummary> Payments = [];
        public readonly List<TaxCodeSummary> TaxCodes = [];
        public readonly List<TaxFilingSummary> TaxFilings = [];
        public readonly List<RecurringScheduleSummary> RecurringSchedules = [];
        public readonly List<FixedAssetSummary> FixedAssets = [];
        public readonly List<AssetDepreciationRunSummary> DepreciationRuns = [];

        public Task<IReadOnlyList<AccountSummary>> ListAccountsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AccountSummary>>(Accounts);

        public Task<AccountSummary> CreateAccountAsync(Guid tenantId, Guid companyId, CreateAccountRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<FiscalYearSummary>> ListFiscalYearsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FiscalYearSummary>>([
                new(Guid.NewGuid(), "FY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "open")
            ]);

        public Task<IReadOnlyList<FiscalPeriodSummary>> ListFiscalPeriodsAsync(Guid tenantId, Guid companyId, Guid? fiscalYearId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FiscalPeriodSummary>>([
                new(Guid.NewGuid(), Guid.NewGuid(), 11, "November 2026", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "open")
            ]);

        public Task<IReadOnlyList<JournalSummary>> ListJournalsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JournalSummary>>(Journals);

        public Task<JournalSummary?> GetJournalByIdAsync(Guid tenantId, Guid companyId, Guid journalId, CancellationToken cancellationToken) =>
            Task.FromResult(Journals.FirstOrDefault(j => j.JournalId == journalId));

        public Task<JournalSummary> PostJournalAsync(Guid tenantId, Guid companyId, Guid userId, Guid idempotencyKey, string requestHash, PostJournalRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrialBalanceRow>>([]);

        public Task<JournalSummary> ReverseJournalAsync(Guid tenantId, Guid companyId, Guid userId, Guid journalId, string reason, CancellationToken cancellationToken)
        {
            var orig = Journals.FirstOrDefault(j => j.JournalId == journalId) ?? throw new KeyNotFoundException();
            var revLines = orig.Lines.Select((l, idx) => new JournalLineSummary(
                idx + 1, l.AccountId, l.AccountCode, l.AccountName, "Reversal: " + l.Description,
                l.TransactionCurrency, l.TransactionAmount, l.CreditFunctional, l.DebitFunctional)).ToList();
            var rev = new JournalSummary(
                Guid.NewGuid(), "REV-" + orig.JournalNo, DateOnly.FromDateTime(DateTime.UtcNow),
                orig.FiscalPeriodId, "Reversal: " + reason, orig.FunctionalCurrency,
                "general_ledger", "reversal", orig.JournalId.ToString(), "posted",
                DateTimeOffset.UtcNow, orig.TotalCredit, orig.TotalDebit, revLines);
            Journals.Add(rev);
            return Task.FromResult(rev);
        }

        public Task<IReadOnlyList<BankAccountSummary>> ListBankAccountsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BankAccountSummary>>(BankAccounts);

        public Task<BankAccountSummary> CreateBankAccountAsync(Guid tenantId, Guid companyId, CreateBankAccountRequest request, CancellationToken cancellationToken)
        {
            var acc = new BankAccountSummary(
                Guid.NewGuid(), request.AccountNumber, request.AccountName, request.BankName,
                request.BranchName, request.SwiftBic, request.Iban, request.CurrencyCode,
                request.AccountType, request.GlAccountId, "1020", "Bank GL", 0m, true, DateTimeOffset.UtcNow);
            BankAccounts.Add(acc);
            return Task.FromResult(acc);
        }

        public Task<BankAccountSummary?> GetBankAccountAsync(Guid tenantId, Guid companyId, Guid bankAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(BankAccounts.FirstOrDefault(b => b.BankAccountId == bankAccountId));

        public Task<BankStatementSummary> ImportBankStatementAsync(Guid tenantId, Guid companyId, ImportBankStatementRequest request, CancellationToken cancellationToken)
        {
            var stmt = new BankStatementSummary(
                Guid.NewGuid(), request.BankAccountId, "Bank Account", request.StatementNumber,
                request.StatementDate, request.OpeningBalance, request.ClosingBalance, "imported",
                DateTimeOffset.UtcNow, null, null, []);
            Statements.Add(stmt);
            return Task.FromResult(stmt);
        }

        public Task<IReadOnlyList<BankStatementSummary>> ListBankStatementsAsync(Guid tenantId, Guid companyId, Guid? bankAccountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BankStatementSummary>>(Statements);

        public Task<BankReconciliationResult> ReconcileBankStatementAsync(Guid tenantId, Guid companyId, Guid userId, Guid statementId, string? note, CancellationToken cancellationToken) =>
            Task.FromResult(new BankReconciliationResult(statementId, "reconciled", 2, 2, 1000m, 5000m, 5000m, 0m, true));

        public Task<TreasuryPaymentSummary> CreateTreasuryPaymentAsync(Guid tenantId, Guid companyId, Guid userId, CreateTreasuryPaymentRequest request, CancellationToken cancellationToken)
        {
            var pmt = new TreasuryPaymentSummary(
                Guid.NewGuid(), request.PaymentNumber, request.PaymentType, request.PaymentDate,
                request.BankAccountId, "Bank Account", request.DestinationBankAccountId, request.Amount,
                request.CurrencyCode, request.PaymentMethod, request.ReferenceNumber, request.PayeePayerName,
                Guid.NewGuid(), "posted", userId, null, DateTimeOffset.UtcNow);
            Payments.Add(pmt);
            return Task.FromResult(pmt);
        }

        public Task<IReadOnlyList<TreasuryPaymentSummary>> ListTreasuryPaymentsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TreasuryPaymentSummary>>(Payments);

        public Task<IReadOnlyList<TaxCodeSummary>> ListTaxCodesAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxCodeSummary>>(TaxCodes);

        public Task<TaxCodeSummary> CreateTaxCodeAsync(Guid tenantId, Guid companyId, CreateTaxCodeRequest request, CancellationToken cancellationToken)
        {
            var tc = new TaxCodeSummary(
                Guid.NewGuid(), request.Code, request.Name, request.TaxType, request.RatePercent,
                request.GlPayableAccountId, "2100", request.GlReceivableAccountId, true);
            TaxCodes.Add(tc);
            return Task.FromResult(tc);
        }

        public Task<TaxLiabilitySummary> GetTaxLiabilitySummaryAsync(Guid tenantId, Guid companyId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult(new TaxLiabilitySummary(100000m, 40000m, 18000m, 7200m, 10800m));

        public Task<TaxFilingSummary> CreateTaxFilingAsync(Guid tenantId, Guid companyId, Guid userId, CreateTaxFilingRequest request, CancellationToken cancellationToken)
        {
            var filing = new TaxFilingSummary(
                Guid.NewGuid(), request.FilingNumber, request.PeriodStart, request.PeriodEnd, request.TaxType,
                100000m, 18000m, 7200m, 10800m, "filed", DateTimeOffset.UtcNow, null, request.Notes);
            TaxFilings.Add(filing);
            return Task.FromResult(filing);
        }

        public Task<IReadOnlyList<TaxFilingSummary>> ListTaxFilingsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxFilingSummary>>(TaxFilings);

        public Task<TaxFilingSummary> SettleTaxFilingAsync(Guid tenantId, Guid companyId, Guid userId, Guid filingId, SettleTaxFilingRequest request, CancellationToken cancellationToken)
        {
            var filing = TaxFilings.First(f => f.FilingId == filingId);
            var updated = filing with { Status = "settled", GlSettlementJournalId = Guid.NewGuid() };
            return Task.FromResult(updated);
        }

        public Task<RecurringScheduleSummary> CreateRecurringScheduleAsync(Guid tenantId, Guid companyId, CreateRecurringScheduleRequest request, CancellationToken cancellationToken)
        {
            var sched = new RecurringScheduleSummary(
                Guid.NewGuid(), request.ScheduleName, request.ScheduleType, request.Frequency,
                request.StartDate, request.EndDate, request.TotalAmount, request.PeriodAmount,
                request.DebitAccountId, "1300", "Prepaid Expense", request.CreditAccountId,
                "1020", "Bank Account", request.PeriodsCount, 0, request.StartDate,
                request.AutoReverseAccrual, "active", request.DescriptionTemplate);
            RecurringSchedules.Add(sched);
            return Task.FromResult(sched);
        }

        public Task<IReadOnlyList<RecurringScheduleSummary>> ListRecurringSchedulesAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RecurringScheduleSummary>>(RecurringSchedules);

        public Task<JournalSummary> PostRecurringSchedulePeriodAsync(Guid tenantId, Guid companyId, Guid userId, Guid scheduleId, CancellationToken cancellationToken) =>
            Task.FromResult(new JournalSummary(
                Guid.NewGuid(), "JRN-REC-01", DateOnly.FromDateTime(DateTime.UtcNow),
                Guid.NewGuid(), "Recurring Schedule Posting", "PKR", "recurring", "schedule",
                scheduleId.ToString(), "posted", DateTimeOffset.UtcNow, 5000m, 5000m, []));

        public Task<FixedAssetSummary> CreateFixedAssetAsync(Guid tenantId, Guid companyId, CreateFixedAssetRequest request, CancellationToken cancellationToken)
        {
            var asset = new FixedAssetSummary(
                Guid.NewGuid(), request.AssetCode, request.AssetName, request.AssetCategory,
                request.AcquisitionDate, request.AcquisitionCost, request.SalvageValue,
                request.UsefulLifeMonths, request.DepreciationMethod, request.AssetGlAccountId,
                "1500", request.AccumulatedDeprAccountId, "1510", request.DepreciationExpenseAccountId,
                "6050", request.AcquisitionCost, 0m, "active");
            FixedAssets.Add(asset);
            return Task.FromResult(asset);
        }

        public Task<IReadOnlyList<FixedAssetSummary>> ListFixedAssetsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FixedAssetSummary>>(FixedAssets);

        public Task<IReadOnlyList<AssetDepreciationRunSummary>> ExecuteDepreciationRunAsync(Guid tenantId, Guid companyId, Guid userId, ExecuteDepreciationRunRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssetDepreciationRunSummary>>([
                new(Guid.NewGuid(), "DEP-RUN-01", request.RunDate, request.FiscalPeriodId,
                    Guid.NewGuid(), "AST-01", "Delivery Van", 2500m, Guid.NewGuid(), "posted", DateTimeOffset.UtcNow)
            ]);

        public Task<bool> SoftCloseFiscalPeriodAsync(Guid tenantId, Guid companyId, Guid userId, Guid periodId, string? note, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> CloseFiscalPeriodAsync(Guid tenantId, Guid companyId, Guid userId, Guid periodId, string? note, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> ReopenFiscalPeriodAsync(Guid tenantId, Guid companyId, Guid userId, Guid periodId, string reason, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<PeriodCloseChecklistItemSummary>> GetPeriodCloseChecklistAsync(Guid tenantId, Guid companyId, Guid periodId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PeriodCloseChecklistItemSummary>>([
                new(Guid.NewGuid(), periodId, "BANK_REC", "Bank Reconciliation Complete", true, DateTimeOffset.UtcNow, Guid.NewGuid(), null)
            ]);

        public Task<bool> UpdatePeriodCloseChecklistItemAsync(Guid tenantId, Guid companyId, Guid userId, Guid itemId, bool isCompleted, string? notes, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<YearEndCloseSummary> CloseFiscalYearAsync(Guid tenantId, Guid companyId, Guid userId, YearEndCloseRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new YearEndCloseSummary(
                request.FiscalYearId, "FY2026", Guid.NewGuid(), "JRN-YE-001",
                150000m, 95000m, 55000m, DateTimeOffset.UtcNow));

        public Task<FinanceControlDashboardSummary> GetControlDashboardAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceControlDashboardSummary(12, 100000m, 100000m, 0m, true, 2, 45000m, 0, 3, 85000m, 2, 1, 10800m));

        public Task<IReadOnlyList<FinancialAuditLogSummary>> ListAuditLogsAsync(Guid tenantId, Guid companyId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FinancialAuditLogSummary>>([]);
    }

    private static (FinanceService Service, MockPhase8FinanceRepository Repo, Guid TenantId, Guid CompanyId, Guid UserId) CreateTestHarness()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var context = new TenantContext(tenantId, companyId, userId);
        var repo = new MockPhase8FinanceRepository();
        var service = new FinanceService(repo, new FakeTenantContextAccessor(context));
        return (service, repo, tenantId, companyId, userId);
    }

    [Fact]
    public async Task ReverseJournal_CreatesOpposingBalancedJournal()
    {
        var (service, repo, _, _, _) = CreateTestHarness();
        var origId = Guid.NewGuid();
        var origJournal = new JournalSummary(
            origId, "JRN-100", new DateOnly(2026, 11, 1), Guid.NewGuid(), "Original entry", "PKR",
            "general_ledger", "manual", "100", "posted", DateTimeOffset.UtcNow, 5000m, 5000m,
            [
                new(1, Guid.NewGuid(), "1020", "Bank", "Debit line", "PKR", 5000m, 5000m, 0m),
                new(2, Guid.NewGuid(), "4010", "Revenue", "Credit line", "PKR", 5000m, 0m, 5000m)
            ]);
        repo.Journals.Add(origJournal);

        var result = await service.ReverseJournalAsync(origId, "Accidental double posting", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rev = result.Value!;
        Assert.Equal("REV-JRN-100", rev.JournalNo);
        Assert.Equal(5000m, rev.TotalDebit);
        Assert.Equal(5000m, rev.TotalCredit);
    }

    [Fact]
    public async Task CreateBankAccount_ValidatesAndAddsAccount()
    {
        var (service, _, _, _, _) = CreateTestHarness();
        var req = new CreateBankAccountRequest(
            "PK99MEZN000123456789", "Main Operating HBL", "Habib Bank Limited", "Karachi Main",
            "HABBBPKA", "PK99HABB000123456789", "PKR", "checking", Guid.NewGuid());

        var result = await service.CreateBankAccountAsync(req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Main Operating HBL", result.Value!.AccountName);
        Assert.Equal("PKR", result.Value!.CurrencyCode);
    }

    [Fact]
    public async Task CreateTreasuryPayment_RecordsVendorDisbursement()
    {
        var (service, _, _, _, _) = CreateTestHarness();
        var req = new CreateTreasuryPaymentRequest(
            "PMT-VEND-001", "vendor_disbursement", new DateOnly(2026, 11, 5),
            Guid.NewGuid(), null, 25000m, "PKR", "bank_transfer", "TXN-998877",
            "Apex Steel Suppliers", Guid.NewGuid(), "Settlement of PO-2026-088");

        var result = await service.CreateTreasuryPaymentAsync(req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("vendor_disbursement", result.Value!.PaymentType);
        Assert.Equal(25000m, result.Value!.Amount);
        Assert.Equal("posted", result.Value!.Status);
    }

    [Fact]
    public async Task TaxFiling_AndSettlement_CalculatesAndUpdatesStatus()
    {
        var (service, repo, _, _, _) = CreateTestHarness();
        var fileReq = new CreateTaxFilingRequest(
            "TAX-Q3-2026", new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), "sales_tax", "Q3 Filing");

        var fileResult = await service.CreateTaxFilingAsync(fileReq, CancellationToken.None);
        Assert.True(fileResult.IsSuccess);
        var filingId = fileResult.Value!.FilingId;

        var settleReq = new SettleTaxFilingRequest(Guid.NewGuid(), "TAX-PAID-CHQ-123");
        var settleResult = await service.SettleTaxFilingAsync(filingId, settleReq, CancellationToken.None);

        Assert.True(settleResult.IsSuccess);
        Assert.Equal("settled", settleResult.Value!.Status);
        Assert.NotNull(settleResult.Value!.GlSettlementJournalId);
    }

    [Fact]
    public async Task FixedAsset_AndDepreciationRun_ExecutesSuccessfully()
    {
        var (service, _, _, _, _) = CreateTestHarness();
        var assetReq = new CreateFixedAssetRequest(
            "AST-MACH-01", "CNC Milling Machine", "Machinery", new DateOnly(2026, 1, 15),
            360000m, 12000m, 60, "straight_line",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var assetResult = await service.CreateFixedAssetAsync(assetReq, CancellationToken.None);
        Assert.True(assetResult.IsSuccess);
        Assert.Equal("AST-MACH-01", assetResult.Value!.AssetCode);

        var runReq = new ExecuteDepreciationRunRequest(new DateOnly(2026, 11, 30), Guid.NewGuid(), assetResult.Value!.AssetId);
        var runResult = await service.ExecuteDepreciationRunAsync(runReq, CancellationToken.None);

        Assert.True(runResult.IsSuccess);
        Assert.Single(runResult.Value!);
        Assert.Equal(2500m, runResult.Value![0].DepreciationAmount);
    }

    [Fact]
    public async Task FiscalPeriod_Closing_AndReopening_Workflow()
    {
        var (service, _, _, _, _) = CreateTestHarness();
        var periodId = Guid.NewGuid();

        // 1. Soft close
        var softRes = await service.SoftCloseFiscalPeriodAsync(periodId, "Audit preview", CancellationToken.None);
        Assert.True(softRes.IsSuccess);

        // 2. Hard close
        var closeRes = await service.CloseFiscalPeriodAsync(periodId, "Final books closed", CancellationToken.None);
        Assert.True(closeRes.IsSuccess);

        // 3. Reopen with reason
        var reopenRes = await service.ReopenFiscalPeriodAsync(periodId, "CFO approved adjustment for audit note 4", CancellationToken.None);
        Assert.True(reopenRes.IsSuccess);
    }

    [Fact]
    public async Task YearEndClose_TransfersNetIncomeToRetainedEarnings()
    {
        var (service, _, _, _, _) = CreateTestHarness();
        var closeReq = new YearEndCloseRequest(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 12, 31), "Close FY2026");

        var result = await service.CloseFiscalYearAsync(closeReq, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("FY2026", result.Value!.FiscalYearCode);
        Assert.Equal(55000m, result.Value!.NetIncomeTransferred);
    }
}
