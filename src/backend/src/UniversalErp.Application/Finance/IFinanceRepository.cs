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
}
