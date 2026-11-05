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
