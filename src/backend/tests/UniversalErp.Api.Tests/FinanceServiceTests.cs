using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Finance;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class FinanceServiceTests
{
    private sealed class FakeTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current => context;
    }

    private sealed class FakeFinanceRepository : IFinanceRepository
    {
        public readonly List<AccountSummary> Accounts = [];
        public readonly List<JournalSummary> Journals = [];
        public bool SimulateAccountConflict = false;
        public bool SimulateJournalConflict = false;

        public Task<IReadOnlyList<AccountSummary>> ListAccountsAsync(
            Guid tenantId,
            Guid companyId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AccountSummary>>(Accounts);

        public Task<AccountSummary> CreateAccountAsync(
            Guid tenantId,
            Guid companyId,
            CreateAccountRequest request,
            CancellationToken cancellationToken)
        {
            if (SimulateAccountConflict || Accounts.Any(a => a.AccountCode == request.AccountCode))
            {
                throw new AccountCodeConflictException();
            }

            var created = new AccountSummary(
                Guid.NewGuid(),
                request.AccountCode,
                request.AccountName,
                request.AccountType,
                request.NormalBalance,
                request.CurrencyCode,
                request.IsControlAccount,
                request.AllowManualPosting,
                true);

            Accounts.Add(created);
            return Task.FromResult(created);
        }

        public Task<IReadOnlyList<FiscalYearSummary>> ListFiscalYearsAsync(
            Guid tenantId,
            Guid companyId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FiscalYearSummary>>([
                new(Guid.NewGuid(), "FY2026-27", new DateOnly(2026, 7, 1), new DateOnly(2027, 6, 30), "open")
            ]);

        public Task<IReadOnlyList<FiscalPeriodSummary>> ListFiscalPeriodsAsync(
            Guid tenantId,
            Guid companyId,
            Guid? fiscalYearId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FiscalPeriodSummary>>([
                new(Guid.NewGuid(), Guid.NewGuid(), 5, "November 2026", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "open")
            ]);

        public Task<IReadOnlyList<JournalSummary>> ListJournalsAsync(
            Guid tenantId,
            Guid companyId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JournalSummary>>(Journals);

        public Task<JournalSummary?> GetJournalByIdAsync(
            Guid tenantId,
            Guid companyId,
            Guid journalId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Journals.FirstOrDefault(j => j.JournalId == journalId));

        public Task<JournalSummary> PostJournalAsync(
            Guid tenantId,
            Guid companyId,
            Guid userId,
            Guid idempotencyKey,
            string requestHash,
            PostJournalRequest request,
            CancellationToken cancellationToken)
        {
            if (SimulateJournalConflict || Journals.Any(j => j.JournalNo == request.JournalNo))
            {
                throw new JournalNoConflictException();
            }

            var lines = request.Lines.Select((l, idx) => new JournalLineSummary(
                idx + 1,
                l.AccountId,
                "ACC-" + idx,
                "Account " + idx,
                l.Description,
                l.TransactionCurrency,
                l.TransactionAmount,
                l.DebitFunctional,
                l.CreditFunctional)).ToList();

            var journal = new JournalSummary(
                Guid.NewGuid(),
                request.JournalNo,
                request.JournalDate,
                request.FiscalPeriodId,
                request.Description,
                request.FunctionalCurrency,
                request.SourceModule,
                request.SourceType,
                request.SourceId,
                "posted",
                DateTimeOffset.UtcNow,
                request.Lines.Sum(l => l.DebitFunctional),
                request.Lines.Sum(l => l.CreditFunctional),
                lines);

            Journals.Add(journal);
            return Task.FromResult(journal);
        }

        public Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(
            Guid tenantId,
            Guid companyId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TrialBalanceRow> rows = Accounts.Select(a =>
            {
                var debits = Journals.SelectMany(j => j.Lines).Where(l => l.AccountId == a.AccountId).Sum(l => l.DebitFunctional);
                var credits = Journals.SelectMany(j => j.Lines).Where(l => l.AccountId == a.AccountId).Sum(l => l.CreditFunctional);
                var net = a.NormalBalance == "debit" ? debits - credits : credits - debits;
                return new TrialBalanceRow(a.AccountId, a.AccountCode, a.AccountName, a.AccountType, a.NormalBalance, debits, credits, net);
            }).ToList();

            return Task.FromResult(rows);
        }

        // ── Phase 8 Stubs ────────────────────────────────────────────────────────
        public Task<JournalSummary> ReverseJournalAsync(
            Guid tenantId, Guid companyId, Guid userId, Guid journalId, string reason, CancellationToken cancellationToken)
        {
            var orig = Journals.FirstOrDefault(j => j.JournalId == journalId)
                ?? throw new KeyNotFoundException("Journal not found");
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
            Task.FromResult<IReadOnlyList<BankAccountSummary>>([]);

        public Task<BankAccountSummary> CreateBankAccountAsync(Guid tenantId, Guid companyId, CreateBankAccountRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new BankAccountSummary(
                Guid.NewGuid(), request.AccountNumber, request.AccountName, request.BankName,
                request.BranchName, request.SwiftBic, request.Iban, request.CurrencyCode,
                request.AccountType, request.GlAccountId, "1020", "Main Operating Account",
                0m, true, DateTimeOffset.UtcNow));

        public Task<BankAccountSummary?> GetBankAccountAsync(Guid tenantId, Guid companyId, Guid bankAccountId, CancellationToken cancellationToken) =>
            Task.FromResult<BankAccountSummary?>(null);

        public Task<BankStatementSummary> ImportBankStatementAsync(Guid tenantId, Guid companyId, ImportBankStatementRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new BankStatementSummary(
                Guid.NewGuid(), request.BankAccountId, "Bank Account", request.StatementNumber,
                request.StatementDate, request.OpeningBalance, request.ClosingBalance, "imported",
                DateTimeOffset.UtcNow, null, null, []));

        public Task<IReadOnlyList<BankStatementSummary>> ListBankStatementsAsync(Guid tenantId, Guid companyId, Guid? bankAccountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BankStatementSummary>>([]);

        public Task<BankReconciliationResult> ReconcileBankStatementAsync(Guid tenantId, Guid companyId, Guid userId, Guid statementId, string? note, CancellationToken cancellationToken) =>
            Task.FromResult(new BankReconciliationResult(statementId, "reconciled", 0, 0, 0m, 0m, 0m, 0m, true));

        public Task<TreasuryPaymentSummary> CreateTreasuryPaymentAsync(Guid tenantId, Guid companyId, Guid userId, CreateTreasuryPaymentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new TreasuryPaymentSummary(
                Guid.NewGuid(), request.PaymentNumber, request.PaymentType, request.PaymentDate,
                request.BankAccountId, "Bank Account", request.DestinationBankAccountId, request.Amount,
                request.CurrencyCode, request.PaymentMethod, request.ReferenceNumber, request.PayeePayerName,
                Guid.NewGuid(), "posted", userId, null, DateTimeOffset.UtcNow));

        public Task<IReadOnlyList<TreasuryPaymentSummary>> ListTreasuryPaymentsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TreasuryPaymentSummary>>([]);

        public Task<IReadOnlyList<TaxCodeSummary>> ListTaxCodesAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxCodeSummary>>([]);

        public Task<TaxCodeSummary> CreateTaxCodeAsync(Guid tenantId, Guid companyId, CreateTaxCodeRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new TaxCodeSummary(
                Guid.NewGuid(), request.Code, request.Name, request.TaxType, request.RatePercent,
                request.GlPayableAccountId, "2100", request.GlReceivableAccountId, true));

        public Task<TaxLiabilitySummary> GetTaxLiabilitySummaryAsync(Guid tenantId, Guid companyId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult(new TaxLiabilitySummary(0m, 0m, 0m, 0m, 0m));

        public Task<TaxFilingSummary> CreateTaxFilingAsync(Guid tenantId, Guid companyId, Guid userId, CreateTaxFilingRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new TaxFilingSummary(
                Guid.NewGuid(), request.FilingNumber, request.PeriodStart, request.PeriodEnd, request.TaxType,
                0m, 0m, 0m, 0m, "filed", DateTimeOffset.UtcNow, null, request.Notes));

        public Task<IReadOnlyList<TaxFilingSummary>> ListTaxFilingsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxFilingSummary>>([]);

        public Task<TaxFilingSummary> SettleTaxFilingAsync(Guid tenantId, Guid companyId, Guid userId, Guid filingId, SettleTaxFilingRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new TaxFilingSummary(
                filingId, "TAX-2026-11", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "vat",
                0m, 0m, 0m, 0m, "settled", DateTimeOffset.UtcNow, Guid.NewGuid(), "Settled"));

        public Task<RecurringScheduleSummary> CreateRecurringScheduleAsync(Guid tenantId, Guid companyId, CreateRecurringScheduleRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new RecurringScheduleSummary(
                Guid.NewGuid(), request.ScheduleName, request.ScheduleType, request.Frequency,
                request.StartDate, request.EndDate, request.TotalAmount, request.PeriodAmount,
                request.DebitAccountId, "1300", "Prepaid Expense", request.CreditAccountId,
                "1020", "Bank Account", request.PeriodsCount, 0, request.StartDate,
                request.AutoReverseAccrual, "active", request.DescriptionTemplate));

        public Task<IReadOnlyList<RecurringScheduleSummary>> ListRecurringSchedulesAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RecurringScheduleSummary>>([]);

        public Task<JournalSummary> PostRecurringSchedulePeriodAsync(Guid tenantId, Guid companyId, Guid userId, Guid scheduleId, CancellationToken cancellationToken) =>
            Task.FromResult(new JournalSummary(Guid.NewGuid(), "JRN-REC-001", DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid(), "Recurring", "PKR", "recurring", "schedule", scheduleId.ToString(), "posted", DateTimeOffset.UtcNow, 100m, 100m, []));

        public Task<FixedAssetSummary> CreateFixedAssetAsync(Guid tenantId, Guid companyId, CreateFixedAssetRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new FixedAssetSummary(
                Guid.NewGuid(), request.AssetCode, request.AssetName, request.AssetCategory,
                request.AcquisitionDate, request.AcquisitionCost, request.SalvageValue,
                request.UsefulLifeMonths, request.DepreciationMethod, request.AssetGlAccountId,
                "1500", request.AccumulatedDeprAccountId, "1510", request.DepreciationExpenseAccountId,
                "6050", request.AcquisitionCost, 0m, "active"));

        public Task<IReadOnlyList<FixedAssetSummary>> ListFixedAssetsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FixedAssetSummary>>([]);

        public Task<IReadOnlyList<AssetDepreciationRunSummary>> ExecuteDepreciationRunAsync(Guid tenantId, Guid companyId, Guid userId, ExecuteDepreciationRunRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssetDepreciationRunSummary>>([]);

        public Task<bool> SoftCloseFiscalPeriodAsync(Guid tenantId, Guid companyId, Guid userId, Guid periodId, string? note, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> CloseFiscalPeriodAsync(Guid tenantId, Guid companyId, Guid userId, Guid periodId, string? note, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> ReopenFiscalPeriodAsync(Guid tenantId, Guid companyId, Guid userId, Guid periodId, string reason, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<PeriodCloseChecklistItemSummary>> GetPeriodCloseChecklistAsync(Guid tenantId, Guid companyId, Guid periodId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PeriodCloseChecklistItemSummary>>([]);

        public Task<bool> UpdatePeriodCloseChecklistItemAsync(Guid tenantId, Guid companyId, Guid userId, Guid itemId, bool isCompleted, string? notes, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<YearEndCloseSummary> CloseFiscalYearAsync(Guid tenantId, Guid companyId, Guid userId, YearEndCloseRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new YearEndCloseSummary(
                request.FiscalYearId, "FY2026", Guid.NewGuid(), "JRN-YE-001",
                0m, 0m, 0m, DateTimeOffset.UtcNow));

        public Task<FinanceControlDashboardSummary> GetControlDashboardAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceControlDashboardSummary(10, 50000m, 50000m, 0m, true, 2, 25000m, 0, 1, 10000m, 1, 1, 0m));

        public Task<IReadOnlyList<FinancialAuditLogSummary>> ListAuditLogsAsync(Guid tenantId, Guid companyId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FinancialAuditLogSummary>>([]);

        public Task<IReadOnlyList<CurrencySummary>> ListCurrenciesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CurrencySummary>>([
                new("PKR", "Pakistani Rupee", 2, true),
                new("USD", "US Dollar", 2, true)
            ]);

        public Task<CompanyCurrencyContextInfo> GetCompanyCurrencyContextAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(new CompanyCurrencyContextInfo("PKR", "USD", new DateOnly(2026, 11, 1)));

        public Task<IReadOnlyList<ExchangeRateSummary>> ListExchangeRatesAsync(Guid tenantId, Guid companyId, string? fromCurrency, string? toCurrency, string? rateType, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExchangeRateSummary>>([]);

        public Task<ExchangeRateSummary> CreateExchangeRateAsync(Guid tenantId, Guid companyId, Guid userId, UpsertExchangeRateRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ExchangeRateSummary?> UpdateExchangeRateAsync(Guid tenantId, Guid companyId, Guid userId, Guid exchangeRateId, UpsertExchangeRateRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<Ias21PostedJournalLineInput>> ListPostedJournalLinesForTranslationAsync(Guid tenantId, Guid companyId, DateOnly asOfDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Ias21PostedJournalLineInput>>([]);
    }

    [Fact]
    public async Task PostJournal_Rejects_Unbalanced_Lines()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var context = new TenantContext(tenantId, companyId, userId);
        var repo = new FakeFinanceRepository();
        var service = new FinanceService(repo, new FakeTenantContextAccessor(context));

        var req = new PostJournalRequest(
            "JRN-001",
            new DateOnly(2026, 11, 2),
            Guid.NewGuid(),
            "Unbalanced Entry",
            "PKR",
            "manual",
            "adjustment",
            "1",
            [
                new(Guid.NewGuid(), "Debit side", "PKR", 1000m, 1m, 1000m, 0m),
                new(Guid.NewGuid(), "Credit side mismatch", "PKR", 900m, 1m, 0m, 900m)
            ]);

        var result = await service.PostJournalAsync(req, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("UNBALANCED_JOURNAL", result.Error);
    }

    [Fact]
    public async Task PostJournal_Rejects_Single_Line_Journal()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var context = new TenantContext(tenantId, companyId, userId);
        var repo = new FakeFinanceRepository();
        var service = new FinanceService(repo, new FakeTenantContextAccessor(context));

        var req = new PostJournalRequest(
            "JRN-002",
            new DateOnly(2026, 11, 2),
            Guid.NewGuid(),
            "Single line",
            "PKR",
            "manual",
            "adjustment",
            "2",
            [
                new(Guid.NewGuid(), "Lone line", "PKR", 1000m, 1m, 1000m, 0m)
            ]);

        var result = await service.PostJournalAsync(req, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("JOURNAL_MINIMUM_TWO_LINES_REQUIRED", result.Error);
    }

    [Fact]
    public async Task PostJournal_Accepts_Balanced_Double_Entry_Journal()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var context = new TenantContext(tenantId, companyId, userId);
        var repo = new FakeFinanceRepository();
        var service = new FinanceService(repo, new FakeTenantContextAccessor(context));

        var cashAcc = Guid.NewGuid();
        var salesAcc = Guid.NewGuid();

        var req = new PostJournalRequest(
            "JRN-003",
            new DateOnly(2026, 11, 2),
            Guid.NewGuid(),
            "Cash Sale",
            "PKR",
            "pos",
            "sale",
            "SALE-101",
            [
                new(cashAcc, "Cash received", "PKR", 25000m, 1m, 25000m, 0m),
                new(salesAcc, "Product Sales", "PKR", 25000m, 1m, 0m, 25000m)
            ]);

        var result = await service.PostJournalAsync(req, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var journal = result.Value!;
        Assert.Equal("JRN-003", journal.JournalNo);
        Assert.Equal(25000m, journal.TotalDebit);
        Assert.Equal(25000m, journal.TotalCredit);
        Assert.Equal(2, journal.Lines.Count);
    }

    [Fact]
    public async Task CreateAccount_Validates_Account_Type_And_Code()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var context = new TenantContext(tenantId, companyId, userId);
        var repo = new FakeFinanceRepository();
        var service = new FinanceService(repo, new FakeTenantContextAccessor(context));

        // Invalid account type
        var invalidTypeReq = new CreateAccountRequest(
            "9999", "Special Account", "invalid_type", "debit", null, "PKR", false, true);
        var invalidTypeRes = await service.CreateAccountAsync(invalidTypeReq, CancellationToken.None);
        Assert.False(invalidTypeRes.IsSuccess);
        Assert.Equal("INVALID_ACCOUNT_TYPE", invalidTypeRes.Error);

        // Valid account creation
        var validReq = new CreateAccountRequest(
            "1030", "Savings Bank Account", "asset", "debit", null, "PKR", false, true);
        var validRes = await service.CreateAccountAsync(validReq, CancellationToken.None);
        Assert.True(validRes.IsSuccess);
        Assert.Equal("1030", validRes.Value!.AccountCode);

        // Duplicate code rejection
        var dupRes = await service.CreateAccountAsync(validReq, CancellationToken.None);
        Assert.False(dupRes.IsSuccess);
        Assert.Equal("DUPLICATE_ACCOUNT_CODE", dupRes.Error);
    }
}
