namespace UniversalErp.Application.Finance;

public sealed class AccountCodeConflictException : Exception
{
}

public sealed class JournalNoConflictException : Exception
{
}

public sealed class JournalUnbalancedException(decimal debits, decimal credits)
    : Exception($"Journal is not balanced: Debits = {debits}, Credits = {credits}.")
{
    public decimal Debits { get; } = debits;
    public decimal Credits { get; } = credits;
}

public sealed class FinanceIdempotencyConflictException : Exception
{
}

public interface IFinanceRepository
{
    // ── Phase 4 Foundation ───────────────────────────────────────────────────

    Task<IReadOnlyList<AccountSummary>> ListAccountsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken);

    Task<AccountSummary> CreateAccountAsync(
        Guid tenantId,
        Guid companyId,
        CreateAccountRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FiscalYearSummary>> ListFiscalYearsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FiscalPeriodSummary>> ListFiscalPeriodsAsync(
        Guid tenantId,
        Guid companyId,
        Guid? fiscalYearId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<JournalSummary>> ListJournalsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken);

    Task<JournalSummary?> GetJournalByIdAsync(
        Guid tenantId,
        Guid companyId,
        Guid journalId,
        CancellationToken cancellationToken);

    Task<JournalSummary> PostJournalAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid idempotencyKey,
        string requestHash,
        PostJournalRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken);

    // ── Phase 8 Finance & Treasury Management ─────────────────────────────────

    Task<JournalSummary> ReverseJournalAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid journalId,
        string reason,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BankAccountSummary>> ListBankAccountsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken);

    Task<BankAccountSummary> CreateBankAccountAsync(
        Guid tenantId,
        Guid companyId,
        CreateBankAccountRequest request,
        CancellationToken cancellationToken);

    Task<BankAccountSummary?> GetBankAccountAsync(
        Guid tenantId,
        Guid companyId,
        Guid bankAccountId,
        CancellationToken cancellationToken);

    Task<BankStatementSummary> ImportBankStatementAsync(
        Guid tenantId,
        Guid companyId,
        ImportBankStatementRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BankStatementSummary>> ListBankStatementsAsync(
        Guid tenantId,
        Guid companyId,
        Guid? bankAccountId,
        CancellationToken cancellationToken);

    Task<BankReconciliationResult> ReconcileBankStatementAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid statementId,
        string? note,
        CancellationToken cancellationToken);

    Task<TreasuryPaymentSummary> CreateTreasuryPaymentAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        CreateTreasuryPaymentRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TreasuryPaymentSummary>> ListTreasuryPaymentsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TaxCodeSummary>> ListTaxCodesAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken);

    Task<TaxCodeSummary> CreateTaxCodeAsync(
        Guid tenantId,
        Guid companyId,
        CreateTaxCodeRequest request,
        CancellationToken cancellationToken);

    Task<TaxLiabilitySummary> GetTaxLiabilitySummaryAsync(
        Guid tenantId,
        Guid companyId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken);

    Task<TaxFilingSummary> CreateTaxFilingAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        CreateTaxFilingRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TaxFilingSummary>> ListTaxFilingsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken);

    Task<TaxFilingSummary> SettleTaxFilingAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid filingId,
        SettleTaxFilingRequest request,
        CancellationToken cancellationToken);

    Task<RecurringScheduleSummary> CreateRecurringScheduleAsync(
        Guid tenantId,
        Guid companyId,
        CreateRecurringScheduleRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringScheduleSummary>> ListRecurringSchedulesAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken);

    Task<JournalSummary> PostRecurringSchedulePeriodAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid scheduleId,
        CancellationToken cancellationToken);

    Task<FixedAssetSummary> CreateFixedAssetAsync(
        Guid tenantId,
        Guid companyId,
        CreateFixedAssetRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FixedAssetSummary>> ListFixedAssetsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AssetDepreciationRunSummary>> ExecuteDepreciationRunAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        ExecuteDepreciationRunRequest request,
        CancellationToken cancellationToken);

    Task<bool> SoftCloseFiscalPeriodAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid periodId,
        string? note,
        CancellationToken cancellationToken);

    Task<bool> CloseFiscalPeriodAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid periodId,
        string? note,
        CancellationToken cancellationToken);

    Task<bool> ReopenFiscalPeriodAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid periodId,
        string reason,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PeriodCloseChecklistItemSummary>> GetPeriodCloseChecklistAsync(
        Guid tenantId,
        Guid companyId,
        Guid periodId,
        CancellationToken cancellationToken);

    Task<bool> UpdatePeriodCloseChecklistItemAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid itemId,
        bool isCompleted,
        string? notes,
        CancellationToken cancellationToken);

    Task<YearEndCloseSummary> CloseFiscalYearAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        YearEndCloseRequest request,
        CancellationToken cancellationToken);

    Task<FinanceControlDashboardSummary> GetControlDashboardAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinancialAuditLogSummary>> ListAuditLogsAsync(
        Guid tenantId,
        Guid companyId,
        int limit,
        CancellationToken cancellationToken);
}
