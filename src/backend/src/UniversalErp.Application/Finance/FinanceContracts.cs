namespace UniversalErp.Application.Finance;

// ── Request Records ─────────────────────────────────────────────────────────

public sealed record CreateAccountRequest(
    string AccountCode,
    string AccountName,
    string AccountType,
    string NormalBalance,
    Guid? ParentAccountId,
    string? CurrencyCode,
    bool IsControlAccount,
    bool AllowManualPosting);

public sealed record PostJournalRequest(
    string JournalNo,
    DateOnly JournalDate,
    Guid FiscalPeriodId,
    string Description,
    string FunctionalCurrency,
    string SourceModule,
    string SourceType,
    string SourceId,
    IReadOnlyList<PostJournalLineRequest> Lines);

public sealed record PostJournalLineRequest(
    Guid AccountId,
    string? Description,
    string TransactionCurrency,
    decimal TransactionAmount,
    decimal ExchangeRate,
    decimal DebitFunctional,
    decimal CreditFunctional);

// ── Summary & View Records ──────────────────────────────────────────────────

public sealed record AccountSummary(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    string NormalBalance,
    string? CurrencyCode,
    bool IsControlAccount,
    bool AllowManualPosting,
    bool IsActive);

public sealed record FiscalYearSummary(
    Guid YearId,
    string Code,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string Status);

public sealed record FiscalPeriodSummary(
    Guid PeriodId,
    Guid FiscalYearId,
    int PeriodNo,
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string Status);

public sealed record JournalLineSummary(
    int LineNo,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string? Description,
    string TransactionCurrency,
    decimal TransactionAmount,
    decimal DebitFunctional,
    decimal CreditFunctional);

public sealed record JournalSummary(
    Guid JournalId,
    string JournalNo,
    DateOnly JournalDate,
    Guid FiscalPeriodId,
    string Description,
    string FunctionalCurrency,
    string SourceModule,
    string SourceType,
    string SourceId,
    string Status,
    DateTimeOffset? PostedAt,
    decimal TotalDebit,
    decimal TotalCredit,
    IReadOnlyList<JournalLineSummary> Lines);

public sealed record TrialBalanceRow(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    string NormalBalance,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal NetBalance);

public sealed record FinancialStatementLine(
    string AccountCode,
    string AccountName,
    string AccountType,
    decimal Balance);

public sealed record FinancialStatementsSummary(
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    decimal TotalRevenue,
    decimal TotalExpenses,
    decimal NetIncome,
    IReadOnlyList<FinancialStatementLine> BalanceSheetLines,
    IReadOnlyList<FinancialStatementLine> IncomeStatementLines);

public sealed record CurrencySummary(
    string Code,
    string Name,
    int MinorUnit,
    bool IsActive);

public sealed record ExchangeRateSummary(
    Guid ExchangeRateId,
    Guid? CompanyId,
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateOnly EffectiveDate,
    DateTimeOffset EffectiveAt,
    string RateType,
    string Source,
    string? Reference,
    Guid? CreatedByUserId,
    string? CreatedByEmail,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    Guid? UpdatedByUserId,
    string? UpdatedByEmail,
    DateTimeOffset? UpdatedAt);

public sealed record UpsertExchangeRateRequest(
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateOnly EffectiveDate,
    string RateType,
    string? Source,
    string? Reference);

public sealed record CompanyCurrencyContextInfo(
    string FunctionalCurrencyCode,
    string PresentationCurrencyCode,
    DateOnly CurrencyEffectiveDate);

public sealed record Ias21PostedJournalLineInput(
    Guid JournalId,
    DateOnly JournalDate,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    string NormalBalance,
    string JournalFunctionalCurrency,
    decimal DebitFunctional,
    decimal CreditFunctional);

public sealed record Ias21ExchangeRateInput(
    Guid ExchangeRateId,
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateOnly EffectiveDate,
    string RateType,
    string Source,
    DateTimeOffset CreatedAt);

public sealed record MissingExchangeRateDiagnostic(
    string RequiredFor,
    string CurrencyPair,
    string RateType,
    DateOnly RequiredDate,
    string Reason);

public sealed record PresentationStatementLine(
    string AccountCode,
    string AccountName,
    string AccountType,
    decimal FunctionalBalance,
    decimal? PresentationBalance,
    decimal? AppliedRate,
    DateOnly? RateEffectiveDate,
    string RateTypeUsed,
    string TranslationStatus);

public sealed record PresentationFinancialStatementsResponse(
    DateOnly AsOfDate,
    string FunctionalCurrencyCode,
    string PresentationCurrencyCode,
    bool SameCurrency,
    string TranslationMethod,
    string TranslationStatus,
    decimal? ClosingRate,
    string? ClosingRateDirection,
    DateOnly? ClosingRateEffectiveDate,
    string? ClosingRateSource,
    decimal? TotalAssetsFunctional,
    decimal? TotalAssetsPresentation,
    decimal? TotalLiabilitiesFunctional,
    decimal? TotalLiabilitiesPresentation,
    decimal? EquityBeforeOciFunctional,
    decimal? EquityBeforeOciPresentation,
    decimal? TotalRevenueFunctional,
    decimal? TotalRevenuePresentation,
    decimal? TotalCostOfSalesFunctional,
    decimal? TotalCostOfSalesPresentation,
    decimal? TotalOperatingExpensesFunctional,
    decimal? TotalOperatingExpensesPresentation,
    decimal? TotalExpensesFunctional,
    decimal? TotalExpensesPresentation,
    decimal? NetIncomeFunctional,
    decimal? NetIncomePresentation,
    decimal? TranslationDifferenceOciPresentation,
    decimal? TotalLiabilitiesAndEquityIncludingOciPresentation,
    bool? IsBalanceSheetBalancedPresentation,
    decimal? BalanceSheetDiscrepancyPresentation,
    IReadOnlyList<PresentationStatementLine> BalanceSheetLines,
    IReadOnlyList<PresentationStatementLine> IncomeStatementLines,
    IReadOnlyList<MissingExchangeRateDiagnostic> MissingRates);
