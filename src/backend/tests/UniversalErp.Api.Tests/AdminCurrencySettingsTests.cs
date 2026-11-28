using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Finance;
using UniversalErp.Application.Foundation;
using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class AdminCurrencySettingsTests
{
    private sealed class StaticTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current => context;
    }

    private sealed class InMemoryFoundationRepo(CompanySettingsResponse initial) : IFoundationRepository
    {
        public CompanySettingsResponse Settings { get; private set; } = initial;
        public readonly List<CompanyCurrencyHistoryItem> History = [];
        public readonly List<string> AuditCodes = [];

        public Task<UserProfileResponse?> GetUserProfileAsync(Guid tenantId, Guid companyId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<UserProfileResponse?>(null);

        public Task<CompanySettingsResponse?> GetCompanySettingsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<CompanySettingsResponse?>(Settings);

        public Task<CompanySettingsResponse?> UpdateCompanySettingsAsync(Guid tenantId, Guid companyId, UpdateCompanySettingsCommand command, CancellationToken ct = default) =>
            Task.FromResult<CompanySettingsResponse?>(Settings);

        public Task<CompanyCurrencySettingsResponse?> UpdateCompanyCurrencySettingsAsync(
            Guid tenantId,
            Guid companyId,
            Guid actorUserId,
            UpdateCompanyCurrencySettingsCommand command,
            CancellationToken ct = default)
        {
            var prevFunc = Settings.FunctionalCurrencyCode ?? "PKR";
            var prevPres = Settings.PresentationCurrencyCode ?? "USD";
            var prevDate = Settings.CurrencyEffectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            Settings = Settings with
            {
                CurrencyCode = command.FunctionalCurrencyCode,
                BaseCurrencyCode = command.FunctionalCurrencyCode,
                FunctionalCurrencyCode = command.FunctionalCurrencyCode,
                PresentationCurrencyCode = command.PresentationCurrencyCode,
                CurrencyEffectiveDate = command.EffectiveDate,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            History.Insert(0, new CompanyCurrencyHistoryItem(
                Guid.NewGuid(),
                prevFunc,
                command.FunctionalCurrencyCode,
                prevPres,
                command.PresentationCurrencyCode,
                prevDate,
                command.EffectiveDate,
                command.ConfirmFutureTransactions,
                command.ChangeReason,
                actorUserId,
                "finance-admin@example.com",
                "Finance Admin",
                DateTimeOffset.UtcNow));

            return Task.FromResult<CompanyCurrencySettingsResponse?>(new CompanyCurrencySettingsResponse(
                Settings.CompanyId,
                Settings.TenantId,
                Settings.CompanyCode,
                Settings.Name,
                command.FunctionalCurrencyCode,
                command.PresentationCurrencyCode,
                command.EffectiveDate,
                Settings.HasPostedTransactions,
                Settings.UpdatedAt,
                FoundationService.FunctionalCurrencyGuidanceText,
                FoundationService.PresentationCurrencyGuidanceText));
        }

        public Task<IReadOnlyList<CompanyCurrencyHistoryItem>> ListCompanyCurrencyHistoryAsync(Guid tenantId, Guid companyId, int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CompanyCurrencyHistoryItem>>(History);

        public Task<IReadOnlyList<CompanyUserSummary>> ListCompanyUsersAsync(Guid tenantId, Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CompanyUserSummary>>([]);

        public Task<CompanyUserSummary?> InviteCompanyUserAsync(Guid tenantId, Guid companyId, InviteCompanyUserCommand command, CancellationToken ct = default) =>
            Task.FromResult<CompanyUserSummary?>(null);

        public Task<IReadOnlyList<RoleDetail>> ListRolesAndPermissionsAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RoleDetail>>([]);

        public Task<IReadOnlyList<AuditEventSummary>> ListAuditEventsAsync(Guid tenantId, Guid companyId, int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuditEventSummary>>([]);

        public Task<Guid> RecordAuditEventAsync(Guid tenantId, Guid companyId, Guid actorUserId, string eventCode, string entityType, Guid? entityId, object? metadata = null, CancellationToken ct = default)
        {
            AuditCodes.Add(eventCode);
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class StubFinanceCurrencyRepo(string functionalCurrency, string presentationCurrency) : IFinanceRepository
    {
        public string? LastPostedJournalFunctionalCurrency { get; private set; }
        public CompanyCurrencyContextInfo CurrencyContext = new(functionalCurrency, presentationCurrency, new DateOnly(2026, 1, 1));

        public Task<IReadOnlyList<AccountSummary>> ListAccountsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AccountSummary>>([]);
        public Task<AccountSummary> CreateAccountAsync(Guid tenantId, Guid companyId, CreateAccountRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<FiscalYearSummary>> ListFiscalYearsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FiscalYearSummary>>([]);
        public Task<IReadOnlyList<FiscalPeriodSummary>> ListFiscalPeriodsAsync(Guid tenantId, Guid companyId, Guid? fiscalYearId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FiscalPeriodSummary>>([]);
        public Task<IReadOnlyList<JournalSummary>> ListJournalsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JournalSummary>>([]);
        public Task<JournalSummary?> GetJournalByIdAsync(Guid tenantId, Guid companyId, Guid journalId, CancellationToken cancellationToken) =>
            Task.FromResult<JournalSummary?>(null);
        public Task<JournalSummary> PostJournalAsync(Guid tenantId, Guid companyId, Guid userId, Guid idempotencyKey, string requestHash, PostJournalRequest request, CancellationToken cancellationToken)
        {
            LastPostedJournalFunctionalCurrency = request.FunctionalCurrency;
            return Task.FromResult(new JournalSummary(
                Guid.NewGuid(), request.JournalNo, request.JournalDate, request.FiscalPeriodId,
                request.Description, request.FunctionalCurrency, request.SourceModule, request.SourceType,
                request.SourceId, "posted", DateTimeOffset.UtcNow,
                request.Lines.Sum(l => l.DebitFunctional), request.Lines.Sum(l => l.CreditFunctional), []));
        }
        public Task<JournalSummary> ReverseJournalAsync(Guid tenantId, Guid companyId, Guid userId, Guid journalId, string reason, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrialBalanceRow>>([]);
        public Task<IReadOnlyList<FinancialAuditLogSummary>> ListAuditLogsAsync(Guid tenantId, Guid companyId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FinancialAuditLogSummary>>([]);
        public Task<IReadOnlyList<BankAccountSummary>> ListBankAccountsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BankAccountSummary>>([]);
        public Task<BankAccountSummary> CreateBankAccountAsync(Guid tenantId, Guid companyId, CreateBankAccountRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<BankAccountSummary?> GetBankAccountAsync(Guid tenantId, Guid companyId, Guid bankAccountId, CancellationToken cancellationToken) =>
            Task.FromResult<BankAccountSummary?>(null);
        public Task<IReadOnlyList<BankStatementSummary>> ListBankStatementsAsync(Guid tenantId, Guid companyId, Guid? bankAccountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BankStatementSummary>>([]);
        public Task<BankStatementSummary> ImportBankStatementAsync(Guid tenantId, Guid companyId, ImportBankStatementRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<BankReconciliationResult> ReconcileBankStatementAsync(Guid tenantId, Guid companyId, Guid userId, Guid statementId, string? note, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<TreasuryPaymentSummary>> ListTreasuryPaymentsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TreasuryPaymentSummary>>([]);
        public Task<TreasuryPaymentSummary> CreateTreasuryPaymentAsync(Guid tenantId, Guid companyId, Guid userId, CreateTreasuryPaymentRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<TaxCodeSummary>> ListTaxCodesAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxCodeSummary>>([]);
        public Task<TaxCodeSummary> CreateTaxCodeAsync(Guid tenantId, Guid companyId, CreateTaxCodeRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<TaxLiabilitySummary> GetTaxLiabilitySummaryAsync(Guid tenantId, Guid companyId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<TaxFilingSummary> CreateTaxFilingAsync(Guid tenantId, Guid companyId, Guid userId, CreateTaxFilingRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<TaxFilingSummary>> ListTaxFilingsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxFilingSummary>>([]);
        public Task<TaxFilingSummary> SettleTaxFilingAsync(Guid tenantId, Guid companyId, Guid userId, Guid filingId, SettleTaxFilingRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<RecurringScheduleSummary>> ListRecurringSchedulesAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RecurringScheduleSummary>>([]);
        public Task<RecurringScheduleSummary> CreateRecurringScheduleAsync(Guid tenantId, Guid companyId, CreateRecurringScheduleRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<JournalSummary> PostRecurringSchedulePeriodAsync(Guid tenantId, Guid companyId, Guid userId, Guid scheduleId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<FixedAssetSummary>> ListFixedAssetsAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FixedAssetSummary>>([]);
        public Task<FixedAssetSummary> CreateFixedAssetAsync(Guid tenantId, Guid companyId, CreateFixedAssetRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<AssetDepreciationRunSummary>> ExecuteDepreciationRunAsync(Guid tenantId, Guid companyId, Guid userId, ExecuteDepreciationRunRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<bool> SoftCloseFiscalPeriodAsync(Guid tenantId, Guid companyId, Guid userId, Guid periodId, string? note, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> CloseFiscalPeriodAsync(Guid tenantId, Guid companyId, Guid userId, Guid periodId, string? note, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> ReopenFiscalPeriodAsync(Guid tenantId, Guid companyId, Guid userId, Guid periodId, string reason, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<IReadOnlyList<PeriodCloseChecklistItemSummary>> GetPeriodCloseChecklistAsync(Guid tenantId, Guid companyId, Guid periodId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PeriodCloseChecklistItemSummary>>([]);
        public Task<bool> UpdatePeriodCloseChecklistItemAsync(Guid tenantId, Guid companyId, Guid userId, Guid itemId, bool isCompleted, string? notes, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<YearEndCloseSummary> CloseFiscalYearAsync(Guid tenantId, Guid companyId, Guid userId, YearEndCloseRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<FinanceControlDashboardSummary> GetControlDashboardAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<CurrencySummary>> ListCurrenciesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CurrencySummary>>([
                new("PKR", "Pakistani Rupee", 2, true),
                new("USD", "US Dollar", 2, true),
                new("EUR", "Euro", 2, true)
            ]);
        public Task<CompanyCurrencyContextInfo> GetCompanyCurrencyContextAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(CurrencyContext);
        public Task<IReadOnlyList<ExchangeRateSummary>> ListExchangeRatesAsync(Guid tenantId, Guid companyId, string? fromCurrency, string? toCurrency, string? rateType, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExchangeRateSummary>>([]);
        public Task<ExchangeRateSummary> CreateExchangeRateAsync(Guid tenantId, Guid companyId, Guid userId, UpsertExchangeRateRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<ExchangeRateSummary?> UpdateExchangeRateAsync(Guid tenantId, Guid companyId, Guid userId, Guid exchangeRateId, UpsertExchangeRateRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<Ias21PostedJournalLineInput>> ListPostedJournalLinesForTranslationAsync(Guid tenantId, Guid companyId, DateOnly asOfDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Ias21PostedJournalLineInput>>([]);
    }

    private static (IReadOnlyList<AccountSummary> Accounts, IReadOnlyList<Ias21PostedJournalLineInput> Lines) BuildSamplePkrBooks()
    {
        var accCash = Guid.NewGuid();
        var accInv = Guid.NewGuid();
        var accAp = Guid.NewGuid();
        var accEquity = Guid.NewGuid();
        var accRev = Guid.NewGuid();
        var accCogs = Guid.NewGuid();

        var accounts = new List<AccountSummary>
        {
            new(accCash, "1010", "Cash", "asset", "debit", "PKR", false, true, true),
            new(accInv, "1200", "Inventory Asset", "asset", "debit", "PKR", true, false, true),
            new(accAp, "2010", "Accounts Payable", "liability", "credit", "PKR", true, false, true),
            new(accEquity, "3010", "Owner Capital", "equity", "credit", "PKR", false, true, true),
            new(accRev, "4010", "Sales Revenue", "revenue", "credit", "PKR", false, true, true),
            new(accCogs, "5010", "Cost of Goods Sold", "expense", "debit", "PKR", false, true, true),
        };

        // Day 1 (2026-01-10): Owner contributes PKR 5,000 cash -> Equity PKR 5,000
        // Day 2 (2026-03-15): Purchase inventory PKR 2,250 on AP + sell 1 unit for Cash PKR 3,000, COGS PKR 1,800
        // Balances at 2026-11-26:
        // Cash = 5,000 + 3,000 = 8,000 | Inv = 2,250 - 1,800 = 450 | Total Assets = 8,450
        // AP = 2,250 | Equity = 5,000 | Revenue = 3,000 | COGS = 1,800 -> Net Income = 1,200
        // Check balance: Assets 8,450 == Liab 2,250 + Equity 5,000 + NI 1,200 = 8,450
        var j1 = Guid.NewGuid();
        var j2 = Guid.NewGuid();
        var j3 = Guid.NewGuid();

        var lines = new List<Ias21PostedJournalLineInput>
        {
            new(j1, new DateOnly(2026, 1, 10), accCash, "1010", "Cash", "asset", "debit", "PKR", 5000m, 0m),
            new(j1, new DateOnly(2026, 1, 10), accEquity, "3010", "Owner Capital", "equity", "credit", "PKR", 0m, 5000m),
            new(j2, new DateOnly(2026, 3, 15), accInv, "1200", "Inventory Asset", "asset", "debit", "PKR", 2250m, 0m),
            new(j2, new DateOnly(2026, 3, 15), accAp, "2010", "Accounts Payable", "liability", "credit", "PKR", 0m, 2250m),
            new(j3, new DateOnly(2026, 6, 20), accCash, "1010", "Cash", "asset", "debit", "PKR", 3000m, 0m),
            new(j3, new DateOnly(2026, 6, 20), accRev, "4010", "Sales Revenue", "revenue", "credit", "PKR", 0m, 3000m),
            new(j3, new DateOnly(2026, 6, 20), accCogs, "5010", "Cost of Goods Sold", "expense", "debit", "PKR", 1800m, 0m),
            new(j3, new DateOnly(2026, 6, 20), accInv, "1200", "Inventory Asset", "asset", "credit", "PKR", 0m, 1800m),
        };

        return (accounts, lines);
    }

    [Fact]
    public void Ias21Translation_MissingExchangeRates_ReturnsUnavailableAndNeverAssumesOne()
    {
        var (accounts, lines) = BuildSamplePkrBooks();
        var result = Ias21TranslationEngine.BuildPresentationStatements(
            currencyContext: new CompanyCurrencyContextInfo("PKR", "USD", new DateOnly(2026, 1, 1)),
            asOfDate: new DateOnly(2026, 11, 26),
            accounts: accounts,
            postedLines: lines,
            exchangeRates: []);

        Assert.Equal("PKR", result.FunctionalCurrencyCode);
        Assert.Equal("USD", result.PresentationCurrencyCode);
        Assert.Equal("UNAVAILABLE_MISSING_RATES", result.TranslationStatus);
        Assert.Null(result.ClosingRate);
        Assert.Null(result.TotalAssetsPresentation);
        Assert.Null(result.NetIncomePresentation);
        Assert.Null(result.TranslationDifferenceOciPresentation);
        Assert.NotEmpty(result.MissingRates);
        Assert.Contains(result.MissingRates, m => m.RateType == "closing");
        Assert.Contains(result.MissingRates, m => m.RateType == "transaction");
    }

    [Fact]
    public void Ias21Translation_AppliesClosingAndTransactionRates_AndBalancesViaOci()
    {
        var (accounts, lines) = BuildSamplePkrBooks();
        var rates = new List<Ias21ExchangeRateInput>
        {
            // Historical equity rate (Jan 2026): 1 USD = 250 PKR
            new(Guid.NewGuid(), "USD", "PKR", 250m, new DateOnly(2026, 1, 1), "transaction", "SBP", DateTimeOffset.UtcNow),
            // Transaction-date rate (June 2026): 1 USD = 270 PKR
            new(Guid.NewGuid(), "USD", "PKR", 270m, new DateOnly(2026, 6, 1), "transaction", "SBP", DateTimeOffset.UtcNow),
            // Period closing rate (Nov 2026): 1 USD = 280 PKR
            new(Guid.NewGuid(), "USD", "PKR", 280m, new DateOnly(2026, 11, 26), "closing", "SBP", DateTimeOffset.UtcNow),
        };

        var result = Ias21TranslationEngine.BuildPresentationStatements(
            currencyContext: new CompanyCurrencyContextInfo("PKR", "USD", new DateOnly(2026, 1, 1)),
            asOfDate: new DateOnly(2026, 11, 26),
            accounts: accounts,
            postedLines: lines,
            exchangeRates: rates);

        Assert.Equal("TRANSLATED", result.TranslationStatus);
        Assert.Empty(result.MissingRates);
        Assert.Equal(Math.Round(1m / 280m, 10, MidpointRounding.AwayFromZero), result.ClosingRate);

        // Functional totals preserved in PKR
        Assert.Equal(8450m, result.TotalAssetsFunctional);
        Assert.Equal(2250m, result.TotalLiabilitiesFunctional);
        Assert.Equal(5000m, result.EquityBeforeOciFunctional);
        Assert.Equal(1200m, result.NetIncomeFunctional);

        // Presentation Assets/Liab at closing 280: 8450/280 = 30.18, 2250/280 = 8.04
        Assert.Equal(30.18m, result.TotalAssetsPresentation);
        Assert.Equal(8.04m, result.TotalLiabilitiesPresentation);
        // Equity at 250: 5000/250 = 20.00
        Assert.Equal(20.00m, result.EquityBeforeOciPresentation);
        // Net income (3000 - 1800) / 270 = 4.44
        Assert.Equal(4.44m, result.NetIncomePresentation);
        // Balance sheet equation including OCI MUST balance exactly to 0 discrepancy
        Assert.True(result.IsBalanceSheetBalancedPresentation);
        Assert.Equal(0m, result.BalanceSheetDiscrepancyPresentation);
        Assert.NotNull(result.TranslationDifferenceOciPresentation);
        Assert.Equal(
            result.TotalAssetsPresentation,
            result.TotalLiabilitiesAndEquityIncludingOciPresentation);
    }

    [Fact]
    public void Ias21Translation_AverageRateOption_RequiresExplicitAverageRate()
    {
        var (accounts, lines) = BuildSamplePkrBooks();
        var withoutAverage = new List<Ias21ExchangeRateInput>
        {
            new(Guid.NewGuid(), "USD", "PKR", 250m, new DateOnly(2026, 1, 1), "transaction", "SBP", DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), "USD", "PKR", 280m, new DateOnly(2026, 11, 26), "closing", "SBP", DateTimeOffset.UtcNow),
        };

        var missingAvg = Ias21TranslationEngine.BuildPresentationStatements(
            new CompanyCurrencyContextInfo("PKR", "USD", new DateOnly(2026, 1, 1)),
            new DateOnly(2026, 11, 26),
            accounts,
            lines,
            withoutAverage,
            incomeRateMethod: Ias21TranslationEngine.IncomeRateMethodAverage);

        Assert.Equal("UNAVAILABLE_PARTIAL_RATES", missingAvg.TranslationStatus);
        Assert.Contains(missingAvg.MissingRates, m => m.RateType == "average");

        var withAverage = new List<Ias21ExchangeRateInput>(withoutAverage)
        {
            new(Guid.NewGuid(), "USD", "PKR", 265m, new DateOnly(2026, 11, 26), "average", "SBP", DateTimeOffset.UtcNow),
        };

        var translatedAvg = Ias21TranslationEngine.BuildPresentationStatements(
            new CompanyCurrencyContextInfo("PKR", "USD", new DateOnly(2026, 1, 1)),
            new DateOnly(2026, 11, 26),
            accounts,
            lines,
            withAverage,
            incomeRateMethod: Ias21TranslationEngine.IncomeRateMethodAverage);

        Assert.Equal("TRANSLATED", translatedAvg.TranslationStatus);
        Assert.Equal(Math.Round(1200m / 265m, 2, MidpointRounding.AwayFromZero), translatedAvg.NetIncomePresentation);
    }

    [Fact]
    public void InventoryValuationCalculator_ReconcilesPkrFunctional_AndTranslatesUsdAtClosingRateOrMarksUnavailable()
    {
        var layers = new List<InventoryValuationSourceLayerInput>
        {
            new(Guid.NewGuid(), null, Guid.NewGuid(), "SKU-001", Guid.NewGuid(), "MAIN-W1", Guid.NewGuid(), "PICK-01",
                "grn", Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-5), 50m, 50m, 45m, "PKR", "available")
        };
        var glBalances = new List<InventoryValuationGlCurrencyBalanceInput> { new("PKR", 1, 2250m) };

        // 1. Without closing rate -> Functional reconciled PKR 2,250, Presentation UNAVAILABLE
        var noRateReport = InventoryValuationCalculator.BuildReport("PKR", "USD", layers, [], 50m, glBalances);
        Assert.Equal("PKR", noRateReport.FunctionalCurrencyCode);
        Assert.Equal(2250m, noRateReport.FunctionalValuation);
        Assert.Equal(2250m, noRateReport.GeneralLedger1200FunctionalBalance);
        Assert.True(noRateReport.IsReconciled);
        Assert.Equal("USD", noRateReport.PresentationCurrencyCode);
        Assert.Equal("MISSING_CLOSING_EXCHANGE_RATE", noRateReport.PresentationStatus);
        Assert.Null(noRateReport.PresentationValuation);
        Assert.Null(noRateReport.GeneralLedger1200PresentationBalance);

        // 2. With transaction rate ONLY (no closing rate) -> must NOT misuse transaction rate for asset balance!
        var txRateOnly = new List<InventoryValuationExchangeRateInput>
        {
            new("USD", "PKR", DateTimeOffset.UtcNow, DateOnly.FromDateTime(DateTime.UtcNow), "transaction", 275m)
        };
        var txOnlyReport = InventoryValuationCalculator.BuildReport("PKR", "USD", layers, txRateOnly, 50m, glBalances);
        Assert.Equal("MISSING_CLOSING_EXCHANGE_RATE", txOnlyReport.PresentationStatus);
        Assert.Null(txOnlyReport.PresentationValuation);

        // 3. With explicit closing rate 278.50 -> translated cleanly
        var closingRates = new List<InventoryValuationExchangeRateInput>
        {
            new("USD", "PKR", DateTimeOffset.UtcNow, DateOnly.FromDateTime(DateTime.UtcNow), "closing", 278.5m)
        };
        var translatedReport = InventoryValuationCalculator.BuildReport("PKR", "USD", layers, closingRates, 50m, glBalances);
        Assert.Equal("TRANSLATED_CLOSING_RATE", translatedReport.PresentationStatus);
        Assert.Equal(Math.Round(2250m / 278.5m, 2, MidpointRounding.AwayFromZero), translatedReport.PresentationValuation);
        Assert.Equal(translatedReport.PresentationValuation, translatedReport.GeneralLedger1200PresentationBalance);
        Assert.Equal(Math.Round(1m / 278.5m, 10, MidpointRounding.AwayFromZero), translatedReport.PresentationAppliedExchangeRate);
    }

    [Fact]
    public async Task FoundationService_RequiresConfirmationWhenFunctionalCurrencyChanges_AndRecordsHistory()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repo = new InMemoryFoundationRepo(new CompanySettingsResponse(
            companyId, tenantId, "CLOUD-LOGISTICS", "Cloud Logistics",
            null, null, "PK", "PKR", "Asia/Karachi", 7, "active", null,
            "PKR", "PKR", "USD", new DateOnly(2026, 1, 1), true));
        var svc = new FoundationService(repo);
        var ctx = new TenantContext(tenantId, companyId, userId);

        // Changing functional currency without confirmation fails cleanly
        var blocked = await svc.UpdateCurrencySettingsAsync(ctx, new UpdateCompanyCurrencySettingsCommand(
            FunctionalCurrencyCode: "USD",
            PresentationCurrencyCode: "USD",
            EffectiveDate: new DateOnly(2026, 12, 1),
            ConfirmFutureTransactions: false,
            ChangeReason: "Should fail without confirm"));

        Assert.False(blocked.IsSuccess);
        Assert.Equal("CONFIRMATION_REQUIRED_FOR_FUTURE_TRANSACTIONS", blocked.Error);
        Assert.Empty(repo.History);

        // With confirmation -> succeeds & records audit history
        var allowed = await svc.UpdateCurrencySettingsAsync(ctx, new UpdateCompanyCurrencySettingsCommand(
            FunctionalCurrencyCode: "usd",
            PresentationCurrencyCode: "pkr",
            EffectiveDate: new DateOnly(2026, 12, 1),
            ConfirmFutureTransactions: true,
            ChangeReason: "Approved controller policy"));

        Assert.True(allowed.IsSuccess);
        Assert.Equal("USD", allowed.Value!.FunctionalCurrencyCode);
        Assert.Equal("PKR", allowed.Value.PresentationCurrencyCode);
        Assert.Single(repo.History);
        Assert.Equal("PKR", repo.History[0].PreviousFunctionalCurrency);
        Assert.Equal("USD", repo.History[0].NewFunctionalCurrency);
        Assert.Equal("Approved controller policy", repo.History[0].ChangeReason);
    }

    [Fact]
    public async Task FinanceService_DefaultsNewJournalToConfiguredFunctionalCurrency_AndValidatesRates()
    {
        var ctx = new TenantContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var repo = new StubFinanceCurrencyRepo("EUR", "USD");
        var svc = new FinanceService(repo, new StaticTenantContextAccessor(ctx));

        var acc1 = Guid.NewGuid();
        var acc2 = Guid.NewGuid();
        var periodId = Guid.NewGuid();

        var postResult = await svc.PostJournalAsync(new PostJournalRequest(
            JournalNo: "JRN-EUR-01",
            JournalDate: new DateOnly(2026, 11, 26),
            FiscalPeriodId: periodId,
            Description: "Dynamic default currency test",
            FunctionalCurrency: "", // omitted -> should resolve company's EUR functional currency
            SourceModule: "manual",
            SourceType: "general",
            SourceId: "src-1",
            Lines:
            [
                new(acc1, "Debit", "EUR", 100m, 1m, 100m, 0m),
                new(acc2, "Credit", "EUR", 100m, 1m, 0m, 100m),
            ]), Guid.NewGuid(), CancellationToken.None);

        Assert.True(postResult.IsSuccess);
        Assert.Equal("EUR", repo.LastPostedJournalFunctionalCurrency);

        var samePair = await svc.CreateExchangeRateAsync(new UpsertExchangeRateRequest("USD", "USD", 1m, new DateOnly(2026, 11, 26), "closing", "SBP", null), CancellationToken.None);
        Assert.False(samePair.IsSuccess);
        Assert.Equal("SAME_CURRENCY_PAIR_NOT_ALLOWED", samePair.Error);

        var zeroRate = await svc.CreateExchangeRateAsync(new UpsertExchangeRateRequest("USD", "PKR", 0m, new DateOnly(2026, 11, 26), "closing", "SBP", null), CancellationToken.None);
        Assert.False(zeroRate.IsSuccess);
        Assert.Equal("INVALID_EXCHANGE_RATE", zeroRate.Error);

        var badType = await svc.CreateExchangeRateAsync(new UpsertExchangeRateRequest("USD", "PKR", 278.5m, new DateOnly(2026, 11, 26), "spot_invalid", "SBP", null), CancellationToken.None);
        Assert.False(badType.IsSuccess);
        Assert.Equal("INVALID_RATE_TYPE", badType.Error);
    }

    [Fact]
    public void ApiEndpoints_RequireExplicitCurrencyAndExchangeRatePermissions_WhenNotDevelopment()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        var ctx = new TenantContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<ITenantContextAccessor>(
            builder.Services, new StaticTenantContextAccessor(ctx));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IFoundationRepository>(
            builder.Services,
            new InMemoryFoundationRepo(new CompanySettingsResponse(
                ctx.CompanyId, ctx.TenantId, "C1", "Co", null, null, "PK", "PKR", "UTC", 7, "active", null)));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<FoundationService>(builder.Services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IFinanceRepository>(
            builder.Services, new StubFinanceCurrencyRepo("PKR", "USD"));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<FinanceService>(builder.Services);
        var app = builder.Build();
        var group = app.MapGroup("/api/v1");
        UniversalErp.Api.Foundation.FoundationEndpointMappings.MapFoundationEndpoints(group, isDevelopment: false);
        UniversalErp.Api.Finance.FinanceEndpointMappings.MapFinanceEndpoints(app, isDevelopment: false);

        var endpoints = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .ToList();

        void AssertPolicy(string routePattern, string httpMethod, string expectedPolicy)
        {
            var match = endpoints.FirstOrDefault(e =>
                string.Equals(e.RoutePattern.RawText, routePattern, StringComparison.OrdinalIgnoreCase) &&
                (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.Contains(httpMethod) ?? false));
            Assert.NotNull(match);
            var auth = match!.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>();
            Assert.NotNull(auth);
            Assert.Equal(expectedPolicy, auth!.Policy);
        }

        AssertPolicy("/api/v1/foundation/currency-settings", "GET", "admin.currency_settings.view");
        AssertPolicy("/api/v1/foundation/currency-settings", "PUT", "admin.currency_settings.manage");
        AssertPolicy("/api/v1/foundation/currency-settings/history", "GET", "admin.currency_settings.view");
        AssertPolicy("/api/v1/finance/currencies", "GET", "admin.currency_settings.view");
        AssertPolicy("/api/v1/finance/exchange-rates", "GET", "admin.currency_settings.view");
        AssertPolicy("/api/v1/finance/exchange-rates", "POST", "finance.exchange_rate.manage");
        AssertPolicy("/api/v1/finance/exchange-rates/{id:guid}", "PUT", "finance.exchange_rate.manage");
        AssertPolicy("/api/v1/finance/financial-statements/presentation", "GET", "finance.reports.view");
    }

    [Fact]
    public void CurrencyConnectionGuard_RejectsMissingConfiguration_AndProtectedTargets()
    {
        var missingEx = Assert.Throws<InvalidOperationException>(() =>
            AdminCurrencySettingsPostgresTests.ResolveApprovedConnectionStrings(_ => null));
        Assert.Contains("FAIL-CLOSED", missingEx.Message, StringComparison.Ordinal);

        var partialAppOnlyEx = Assert.Throws<InvalidOperationException>(() =>
            AdminCurrencySettingsPostgresTests.ResolveApprovedConnectionStrings(k => k switch
            {
                "UNIVERSAL_ERP_TEST_CONNECTION_STRING" =>
                    "Host=127.0.0.1;Port=49386;Database=universal_erp_current_disposable_test;Username=erp_app;Password=dummy",
                _ => null
            }));
        Assert.Contains("FAIL-CLOSED", partialAppOnlyEx.Message, StringComparison.Ordinal);

        var stagingPort5433Ex = Assert.Throws<InvalidOperationException>(() =>
            AdminCurrencySettingsPostgresTests.ResolveApprovedConnectionStrings(k => k switch
            {
                "UNIVERSAL_ERP_TEST_CONNECTION_STRING" =>
                    "Host=127.0.0.1;Port=5433;Database=universal_erp_current_disposable_test;Username=erp_app;Password=dummy",
                "UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING" =>
                    "Host=127.0.0.1;Port=5433;Database=universal_erp_current_disposable_test;Username=erp_dev;Password=dummy",
                _ => null
            }));
        Assert.Contains("5433", stagingPort5433Ex.Message, StringComparison.Ordinal);

        var targetPort5433Ex = Assert.Throws<InvalidOperationException>(() =>
            AdminCurrencySettingsPostgresTests.ResolveApprovedConnectionStrings(k => k switch
            {
                "UNIVERSAL_ERP_TEST_TARGET_PORT" => "5433",
                "UNIVERSAL_ERP_TEST_TARGET_DB" => "universal_erp_current_disposable_test",
                "PGPASSWORD_ERP_APP" => "dummy",
                "PGPASSWORD_ERP_DEV" => "dummy",
                _ => null
            }));
        Assert.Contains("5433", targetPort5433Ex.Message, StringComparison.Ordinal);

        var prodPort5432Ex = Assert.Throws<InvalidOperationException>(() =>
            AdminCurrencySettingsPostgresTests.ValidateApprovedConnectionString(
                "Host=127.0.0.1;Port=5432;Database=universal_erp_current_disposable_test;Username=erp_app;Password=dummy",
                "erp_app"));
        Assert.Contains("5432", prodPort5432Ex.Message, StringComparison.Ordinal);

        var stagingDbEx = Assert.Throws<InvalidOperationException>(() =>
            AdminCurrencySettingsPostgresTests.ValidateApprovedConnectionString(
                "Host=127.0.0.1;Port=49386;Database=universal_erp_phase5_dev;Username=erp_app;Password=dummy",
                "erp_app"));
        Assert.Contains("universal_erp_phase5_dev", stagingDbEx.Message, StringComparison.Ordinal);

        var remoteHostEx = Assert.Throws<InvalidOperationException>(() =>
            AdminCurrencySettingsPostgresTests.ValidateApprovedConnectionString(
                "Host=ep-prod-db.neon.tech;Port=5434;Database=universal_erp_current_disposable_test;Username=erp_app;Password=dummy",
                "erp_app"));
        Assert.Contains("non-loopback", remoteHostEx.Message, StringComparison.OrdinalIgnoreCase);

        var roleMismatchEx = Assert.Throws<InvalidOperationException>(() =>
            AdminCurrencySettingsPostgresTests.ValidateApprovedConnectionString(
                "Host=127.0.0.1;Port=49386;Database=universal_erp_current_disposable_test;Username=erp_dev;Password=dummy",
                "erp_app"));
        Assert.Contains("erp_app", roleMismatchEx.Message, StringComparison.Ordinal);

        var valid = AdminCurrencySettingsPostgresTests.ResolveApprovedConnectionStrings(k => k switch
        {
            "UNIVERSAL_ERP_TEST_CONNECTION_STRING" =>
                "Host=127.0.0.1;Port=49386;Database=universal_erp_current_disposable_test;Username=erp_app;Password=app_pwd",
            "UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING" =>
                "Host=127.0.0.1;Port=49386;Database=universal_erp_current_disposable_test;Username=erp_dev;Password=dev_pwd",
            _ => null
        });
        Assert.Equal(49386, valid.ExpectedPort);
        Assert.False(valid.IsInternalRunner);

        // Host mode MUST reject internal container hostname & port 5432 even if UNIVERSAL_ERP_INTERNAL_CONTAINER_RUNNER=true is spoofed outside a Linux container
        var hostSpoofEx = Assert.Throws<InvalidOperationException>(() =>
            AdminCurrencySettingsPostgresTests.ResolveApprovedConnectionStrings(
                k => k switch
                {
                    "UNIVERSAL_ERP_INTERNAL_CONTAINER_RUNNER" => "true",
                    "UNIVERSAL_ERP_TEST_TARGET_HOST" => "postgres-current-test",
                    "UNIVERSAL_ERP_TEST_TARGET_PORT" => "5432",
                    "UNIVERSAL_ERP_TEST_TARGET_DB" => "universal_erp_current_disposable_verify",
                    "UNIVERSAL_ERP_TEST_CONNECTION_STRING" =>
                        "Host=postgres-current-test;Port=5432;Database=universal_erp_current_disposable_verify;Username=erp_app;Password=app_pwd",
                    "UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING" =>
                        "Host=postgres-current-test;Port=5432;Database=universal_erp_current_disposable_verify;Username=erp_dev;Password=dev_pwd",
                    _ => null
                },
                isContainerRuntime: () => false));
        Assert.Contains("5432", hostSpoofEx.Message, StringComparison.Ordinal);

        // Even inside a verified Linux container, 127.0.0.1:5432, *:5433, and non-approved service hosts MUST be rejected
        var containerLoopback5432Ex = Assert.Throws<InvalidOperationException>(() =>
            AdminCurrencySettingsPostgresTests.ResolveApprovedConnectionStrings(
                k => k switch
                {
                    "UNIVERSAL_ERP_INTERNAL_CONTAINER_RUNNER" => "true",
                    "UNIVERSAL_ERP_TEST_TARGET_HOST" => "postgres-current-test",
                    "UNIVERSAL_ERP_TEST_TARGET_PORT" => "5432",
                    "UNIVERSAL_ERP_TEST_TARGET_DB" => "universal_erp_current_disposable_verify",
                    "UNIVERSAL_ERP_TEST_CONNECTION_STRING" =>
                        "Host=127.0.0.1;Port=5432;Database=universal_erp_current_disposable_verify;Username=erp_app;Password=app_pwd",
                    "UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING" =>
                        "Host=127.0.0.1;Port=5432;Database=universal_erp_current_disposable_verify;Username=erp_dev;Password=dev_pwd",
                    _ => null
                },
                isContainerRuntime: () => true));
        Assert.Contains("postgres-current-test", containerLoopback5432Ex.Message, StringComparison.Ordinal);

        var containerWrongServiceEx = Assert.Throws<InvalidOperationException>(() =>
            AdminCurrencySettingsPostgresTests.ResolveApprovedConnectionStrings(
                k => k switch
                {
                    "UNIVERSAL_ERP_INTERNAL_CONTAINER_RUNNER" => "true",
                    "UNIVERSAL_ERP_TEST_TARGET_HOST" => "postgres-current-test",
                    "UNIVERSAL_ERP_TEST_TARGET_PORT" => "5432",
                    "UNIVERSAL_ERP_TEST_TARGET_DB" => "universal_erp_current_disposable_verify",
                    "UNIVERSAL_ERP_TEST_CONNECTION_STRING" =>
                        "Host=universal-erp-postgres;Port=5432;Database=universal_erp_current_disposable_verify;Username=erp_app;Password=app_pwd",
                    "UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING" =>
                        "Host=universal-erp-postgres;Port=5432;Database=universal_erp_current_disposable_verify;Username=erp_dev;Password=dev_pwd",
                    _ => null
                },
                isContainerRuntime: () => true));
        Assert.Contains("postgres-current-test", containerWrongServiceEx.Message, StringComparison.Ordinal);

        var containerStagingPort5433Ex = Assert.Throws<InvalidOperationException>(() =>
            AdminCurrencySettingsPostgresTests.ValidateApprovedConnectionString(
                "Host=postgres-current-test;Port=5433;Database=universal_erp_current_disposable_verify;Username=erp_app;Password=app_pwd",
                "erp_app",
                allowInternalRunner: true));
        Assert.Contains("5432", containerStagingPort5433Ex.Message, StringComparison.Ordinal);

        // Verified Linux container internal runner strictly pointing to postgres-current-test:5432 is accepted
        var validInternal = AdminCurrencySettingsPostgresTests.ResolveApprovedConnectionStrings(
            k => k switch
            {
                "UNIVERSAL_ERP_INTERNAL_CONTAINER_RUNNER" => "true",
                "UNIVERSAL_ERP_TEST_TARGET_HOST" => "postgres-current-test",
                "UNIVERSAL_ERP_TEST_TARGET_PORT" => "5432",
                "UNIVERSAL_ERP_TEST_TARGET_DB" => "universal_erp_current_disposable_verify",
                "UNIVERSAL_ERP_TEST_CONNECTION_STRING" =>
                    "Host=postgres-current-test;Port=5432;Database=universal_erp_current_disposable_verify;Username=erp_app;Password=app_pwd",
                "UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING" =>
                    "Host=postgres-current-test;Port=5432;Database=universal_erp_current_disposable_verify;Username=erp_dev;Password=dev_pwd",
                _ => null
            },
            isContainerRuntime: () => true);
        Assert.Equal(5432, validInternal.ExpectedPort);
        Assert.Equal("universal_erp_current_disposable_verify", validInternal.ExpectedDatabase);
        Assert.True(validInternal.IsInternalRunner);
    }

    private sealed class RejectingPastDateFoundationRepo(CompanySettingsResponse initial) : IFoundationRepository
    {
        public Task<UserProfileResponse?> GetUserProfileAsync(Guid tenantId, Guid companyId, Guid userId, CancellationToken ct = default) => Task.FromResult<UserProfileResponse?>(null);
        public Task<CompanySettingsResponse?> GetCompanySettingsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default) => Task.FromResult<CompanySettingsResponse?>(initial);
        public Task<CompanySettingsResponse?> UpdateCompanySettingsAsync(Guid tenantId, Guid companyId, UpdateCompanySettingsCommand command, CancellationToken ct = default) => Task.FromResult<CompanySettingsResponse?>(initial);
        public Task<CompanyCurrencySettingsResponse?> UpdateCompanyCurrencySettingsAsync(Guid tenantId, Guid companyId, Guid actorUserId, UpdateCompanyCurrencySettingsCommand command, CancellationToken ct = default) =>
            throw new InvalidOperationException("EFFECTIVE_DATE_MUST_NOT_BE_IN_THE_PAST");
        public Task<IReadOnlyList<CompanyCurrencyHistoryItem>> ListCompanyCurrencyHistoryAsync(Guid tenantId, Guid companyId, int limit = 50, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CompanyCurrencyHistoryItem>>([]);
        public Task<IReadOnlyList<CompanyUserSummary>> ListCompanyUsersAsync(Guid tenantId, Guid companyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CompanyUserSummary>>([]);
        public Task<CompanyUserSummary?> InviteCompanyUserAsync(Guid tenantId, Guid companyId, InviteCompanyUserCommand command, CancellationToken ct = default) => Task.FromResult<CompanyUserSummary?>(null);
        public Task<IReadOnlyList<RoleDetail>> ListRolesAndPermissionsAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RoleDetail>>([]);
        public Task<IReadOnlyList<AuditEventSummary>> ListAuditEventsAsync(Guid tenantId, Guid companyId, int limit = 50, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuditEventSummary>>([]);
        public Task<Guid> RecordAuditEventAsync(Guid tenantId, Guid companyId, Guid actorUserId, string eventCode, string entityType, Guid? entityId, object? metadata = null, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
    }

    [Fact]
    public async Task FoundationService_PropagatesPastEffectiveDateRejection_FromDatabaseGuard()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repo = new RejectingPastDateFoundationRepo(new CompanySettingsResponse(
            companyId, tenantId, "C1", "Co", null, null, "PK", "PKR", "UTC", 7, "active", null,
            "PKR", "PKR", "USD", new DateOnly(2026, 11, 27), true));
        var svc = new FoundationService(repo);
        var res = await svc.UpdateCurrencySettingsAsync(
            new TenantContext(tenantId, companyId, userId),
            new UpdateCompanyCurrencySettingsCommand("PKR", "EUR", new DateOnly(2026, 11, 1), false, "Past date"));

        Assert.False(res.IsSuccess);
        Assert.Equal("EFFECTIVE_DATE_MUST_NOT_BE_IN_THE_PAST", res.Error);
    }
}
