namespace UniversalErp.Application.Finance;

// ── Bank Accounts ─────────────────────────────────────────────────────────────

public sealed record CreateBankAccountRequest(
    string AccountNumber,
    string AccountName,
    string BankName,
    string? BranchName,
    string? SwiftBic,
    string? Iban,
    string CurrencyCode,
    string AccountType,
    Guid GlAccountId);

public sealed record BankAccountSummary(
    Guid BankAccountId,
    string AccountNumber,
    string AccountName,
    string BankName,
    string? BranchName,
    string? SwiftBic,
    string? Iban,
    string CurrencyCode,
    string AccountType,
    Guid GlAccountId,
    string GlAccountCode,
    string GlAccountName,
    decimal CurrentBalance,
    bool IsActive,
    DateTimeOffset CreatedAt);

// ── Bank Statements & Reconciliation ──────────────────────────────────────────

public sealed record ImportBankStatementLineRequest(
    int LineNo,
    DateOnly TransactionDate,
    DateOnly? ValueDate,
    string? ReferenceNumber,
    string Description,
    decimal Amount,
    decimal? BalanceAfter = null);

public sealed record ImportBankStatementRequest(
    Guid BankAccountId,
    string StatementNumber,
    DateOnly StatementDate,
    decimal OpeningBalance,
    decimal ClosingBalance,
    IReadOnlyList<ImportBankStatementLineRequest> Lines);

public sealed record BankStatementLineSummary(
    Guid LineId,
    int LineNo,
    DateOnly TransactionDate,
    DateOnly? ValueDate,
    string? ReferenceNumber,
    string Description,
    decimal Amount,
    decimal? BalanceAfter,
    string ReconciledStatus,
    Guid? MatchedJournalLineId,
    DateTimeOffset? MatchedAt);

public sealed record BankStatementSummary(
    Guid StatementId,
    Guid BankAccountId,
    string BankAccountName,
    string StatementNumber,
    DateOnly StatementDate,
    decimal OpeningBalance,
    decimal ClosingBalance,
    string Status,
    DateTimeOffset ImportedAt,
    DateTimeOffset? ReconciledAt,
    Guid? ReconciledBy,
    IReadOnlyList<BankStatementLineSummary> Lines);

public sealed record ReconcileBankStatementRequest(string? Note = null);

public sealed record BankReconciliationResult(
    Guid StatementId,
    string Status,
    int MatchedLinesCount,
    int TotalLinesCount,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal GlBookBalance,
    decimal Discrepancy,
    bool IsFullyReconciled);

// ── Treasury Payments ─────────────────────────────────────────────────────────

public sealed record CreateTreasuryPaymentRequest(
    string PaymentNumber,
    string PaymentType,
    DateOnly PaymentDate,
    Guid BankAccountId,
    Guid? DestinationBankAccountId,
    decimal Amount,
    string CurrencyCode,
    string PaymentMethod,
    string? ReferenceNumber,
    string PayeePayerName,
    Guid? TargetSubledgerId = null,
    string? Note = null);

public sealed record TreasuryPaymentSummary(
    Guid PaymentId,
    string PaymentNumber,
    string PaymentType,
    DateOnly PaymentDate,
    Guid BankAccountId,
    string BankAccountName,
    Guid? DestinationBankAccountId,
    decimal Amount,
    string CurrencyCode,
    string PaymentMethod,
    string? ReferenceNumber,
    string PayeePayerName,
    Guid? GlJournalId,
    string Status,
    Guid CreatedBy,
    Guid? ApprovedBy,
    DateTimeOffset CreatedAt);

// ── Tax Codes & Filings ───────────────────────────────────────────────────────

public sealed record CreateTaxCodeRequest(
    string Code,
    string Name,
    string TaxType,
    decimal RatePercent,
    Guid GlPayableAccountId,
    Guid? GlReceivableAccountId = null);

public sealed record TaxCodeSummary(
    Guid TaxCodeId,
    string Code,
    string Name,
    string TaxType,
    decimal RatePercent,
    Guid GlPayableAccountId,
    string GlPayableAccountCode,
    Guid? GlReceivableAccountId,
    bool IsActive);

public sealed record TaxLiabilitySummary(
    decimal TotalSalesAmount,
    decimal TotalPurchasesAmount,
    decimal TotalOutputTax,
    decimal TotalInputTax,
    decimal NetTaxPayable);

public sealed record CreateTaxFilingRequest(
    string FilingNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string TaxType,
    string? Notes = null);

public sealed record TaxFilingSummary(
    Guid FilingId,
    string FilingNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string TaxType,
    decimal TotalTaxableAmount,
    decimal OutputTaxAmount,
    decimal InputTaxAmount,
    decimal NetTaxPayable,
    string Status,
    DateTimeOffset? FiledAt,
    Guid? GlSettlementJournalId,
    string? Notes);

public sealed record SettleTaxFilingRequest(
    Guid BankAccountId,
    string? PaymentReference = null);

// ── Accruals & Recurring Schedules ────────────────────────────────────────────

public sealed record CreateRecurringScheduleRequest(
    string ScheduleName,
    string ScheduleType,
    string Frequency,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal TotalAmount,
    decimal PeriodAmount,
    Guid DebitAccountId,
    Guid CreditAccountId,
    int PeriodsCount,
    bool AutoReverseAccrual = false,
    string? DescriptionTemplate = null);

public sealed record RecurringScheduleSummary(
    Guid ScheduleId,
    string ScheduleName,
    string ScheduleType,
    string Frequency,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal TotalAmount,
    decimal PeriodAmount,
    Guid DebitAccountId,
    string DebitAccountCode,
    string DebitAccountName,
    Guid CreditAccountId,
    string CreditAccountCode,
    string CreditAccountName,
    int PeriodsCount,
    int PostedPeriodsCount,
    DateOnly NextRunDate,
    bool AutoReverseAccrual,
    string Status,
    string? DescriptionTemplate);

// ── Fixed Assets & Depreciation ───────────────────────────────────────────────

public sealed record CreateFixedAssetRequest(
    string AssetCode,
    string AssetName,
    string AssetCategory,
    DateOnly AcquisitionDate,
    decimal AcquisitionCost,
    decimal SalvageValue,
    int UsefulLifeMonths,
    string DepreciationMethod,
    Guid AssetGlAccountId,
    Guid AccumulatedDeprAccountId,
    Guid DepreciationExpenseAccountId);

public sealed record FixedAssetSummary(
    Guid AssetId,
    string AssetCode,
    string AssetName,
    string AssetCategory,
    DateOnly AcquisitionDate,
    decimal AcquisitionCost,
    decimal SalvageValue,
    int UsefulLifeMonths,
    string DepreciationMethod,
    Guid AssetGlAccountId,
    string AssetGlAccountCode,
    Guid AccumulatedDeprAccountId,
    string AccumulatedDeprAccountCode,
    Guid DepreciationExpenseAccountId,
    string DepreciationExpenseAccountCode,
    decimal CurrentBookValue,
    decimal AccumulatedDepreciation,
    string Status);

public sealed record ExecuteDepreciationRunRequest(
    DateOnly RunDate,
    Guid FiscalPeriodId,
    Guid? SpecificAssetId = null);

public sealed record AssetDepreciationRunSummary(
    Guid RunId,
    string RunNumber,
    DateOnly RunDate,
    Guid FiscalPeriodId,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    decimal DepreciationAmount,
    Guid? GlJournalId,
    string Status,
    DateTimeOffset CreatedAt);

// ── Fiscal Period Closing & Reopening ─────────────────────────────────────────

public sealed record SoftCloseFiscalPeriodRequest(string? Note = null);

public sealed record CloseFiscalPeriodRequest(string? Note = null);

public sealed record ReopenFiscalPeriodRequest(string Reason);

public sealed record PeriodCloseChecklistItemSummary(
    Guid ItemId,
    Guid FiscalPeriodId,
    string TaskCode,
    string TaskName,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    Guid? CompletedBy,
    string? Notes);

public sealed record UpdateChecklistItemRequest(
    bool IsCompleted,
    string? Notes = null);

public sealed record YearEndCloseRequest(
    Guid FiscalYearId,
    Guid RetainedEarningsAccountId,
    DateOnly ClosingDate,
    string? Note = null);

public sealed record YearEndCloseSummary(
    Guid FiscalYearId,
    string FiscalYearCode,
    Guid ClosingJournalId,
    string ClosingJournalNo,
    decimal TotalRevenueClosed,
    decimal TotalExpenseClosed,
    decimal NetIncomeTransferred,
    DateTimeOffset ClosedAt);

// ── Financial Audit & Controls ────────────────────────────────────────────────

public sealed record ReverseJournalRequest(string Reason);

public sealed record FinancialAuditLogSummary(
    Guid LogId,
    string EventType,
    string EntityName,
    string EntityId,
    Guid ActorUserId,
    string Description,
    string? MetadataJson,
    DateTimeOffset CreatedAt);

public sealed record FinanceControlDashboardSummary(
    int TotalAccounts,
    decimal TotalGlDebits,
    decimal TotalGlCredits,
    decimal GlNetVariance,
    bool IsGlBalanced,
    int ActiveBankAccountsCount,
    decimal TotalCashAndBankBalances,
    int UnreconciledStatementsCount,
    int ActiveFixedAssetsCount,
    decimal TotalFixedAssetBookValue,
    int ActiveRecurringSchedulesCount,
    int OpenFiscalPeriodsCount,
    decimal EstimatedNetTaxPayable);
