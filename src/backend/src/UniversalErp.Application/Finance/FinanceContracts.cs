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
