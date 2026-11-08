using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniversalErp.Application.Abstractions;
using UniversalErp.Domain.Common;

namespace UniversalErp.Application.Finance;

public sealed class FinanceService(
    IFinanceRepository repository,
    ITenantContextAccessor tenantContext)
{
    private static readonly HashSet<string> ValidAccountTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "asset", "liability", "equity", "revenue", "expense"
    };

    private static readonly HashSet<string> ValidNormalBalances = new(StringComparer.OrdinalIgnoreCase)
    {
        "debit", "credit"
    };

    // ── Accounts ─────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<AccountSummary>> ListAccountsAsync(CancellationToken cancellationToken) =>
        repository.ListAccountsAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            cancellationToken);

    public async Task<Result<AccountSummary>> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccountCode) || request.AccountCode.Length > 32)
        {
            return Result<AccountSummary>.Failure("INVALID_ACCOUNT_CODE");
        }

        if (string.IsNullOrWhiteSpace(request.AccountName) || request.AccountName.Length > 200)
        {
            return Result<AccountSummary>.Failure("INVALID_ACCOUNT_NAME");
        }

        var accountType = request.AccountType.Trim().ToLowerInvariant();
        if (!ValidAccountTypes.Contains(accountType))
        {
            return Result<AccountSummary>.Failure("INVALID_ACCOUNT_TYPE");
        }

        var normalBalance = request.NormalBalance.Trim().ToLowerInvariant();
        if (!ValidNormalBalances.Contains(normalBalance))
        {
            return Result<AccountSummary>.Failure("INVALID_NORMAL_BALANCE");
        }

        var normalized = new CreateAccountRequest(
            request.AccountCode.Trim().ToUpperInvariant(),
            request.AccountName.Trim(),
            accountType,
            normalBalance,
            request.ParentAccountId,
            string.IsNullOrWhiteSpace(request.CurrencyCode) ? null : request.CurrencyCode.Trim().ToUpperInvariant(),
            request.IsControlAccount,
            request.AllowManualPosting);

        try
        {
            var created = await repository.CreateAccountAsync(
                tenantContext.Current.TenantId,
                tenantContext.Current.CompanyId,
                normalized,
                cancellationToken);

            return Result<AccountSummary>.Success(created);
        }
        catch (AccountCodeConflictException)
        {
            return Result<AccountSummary>.Failure("DUPLICATE_ACCOUNT_CODE");
        }
    }

    // ── Fiscal Periods ───────────────────────────────────────────────────────

    public Task<IReadOnlyList<FiscalYearSummary>> ListFiscalYearsAsync(CancellationToken cancellationToken) =>
        repository.ListFiscalYearsAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            cancellationToken);

    public Task<IReadOnlyList<FiscalPeriodSummary>> ListFiscalPeriodsAsync(
        Guid? fiscalYearId,
        CancellationToken cancellationToken) =>
        repository.ListFiscalPeriodsAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            fiscalYearId,
            cancellationToken);

    // ── Journals ─────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<JournalSummary>> ListJournalsAsync(CancellationToken cancellationToken) =>
        repository.ListJournalsAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            cancellationToken);

    public Task<JournalSummary?> GetJournalByIdAsync(
        Guid journalId,
        CancellationToken cancellationToken) =>
        repository.GetJournalByIdAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            journalId,
            cancellationToken);

    public async Task<Result<JournalSummary>> PostJournalAsync(
        PostJournalRequest request,
        Guid? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is null || idempotencyKey == Guid.Empty)
        {
            return Result<JournalSummary>.Failure("INVALID_IDEMPOTENCY_KEY");
        }

        if (string.IsNullOrWhiteSpace(request.JournalNo) || request.JournalNo.Length > 64)
        {
            return Result<JournalSummary>.Failure("INVALID_JOURNAL_NO");
        }

        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 500)
        {
            return Result<JournalSummary>.Failure("INVALID_JOURNAL_DESCRIPTION");
        }

        if (request.FiscalPeriodId == Guid.Empty)
        {
            return Result<JournalSummary>.Failure("INVALID_FISCAL_PERIOD");
        }

        if (request.Lines is null || request.Lines.Count < 2)
        {
            return Result<JournalSummary>.Failure("JOURNAL_MINIMUM_TWO_LINES_REQUIRED");
        }

        decimal totalDebit = 0;
        decimal totalCredit = 0;

        foreach (var line in request.Lines)
        {
            if (line.AccountId == Guid.Empty)
            {
                return Result<JournalSummary>.Failure("INVALID_LINE_ACCOUNT");
            }

            if (line.TransactionAmount <= 0)
            {
                return Result<JournalSummary>.Failure("INVALID_LINE_AMOUNT");
            }

            if ((line.DebitFunctional > 0 && line.CreditFunctional > 0) ||
                (line.DebitFunctional <= 0 && line.CreditFunctional <= 0))
            {
                return Result<JournalSummary>.Failure("LINE_MUST_BE_EITHER_DEBIT_OR_CREDIT");
            }

            totalDebit += line.DebitFunctional;
            totalCredit += line.CreditFunctional;
        }

        if (Math.Abs(totalDebit - totalCredit) > 0.0001m)
        {
            return Result<JournalSummary>.Failure("UNBALANCED_JOURNAL");
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(request.FunctionalCurrency)
            ? "PKR"
            : request.FunctionalCurrency.Trim().ToUpperInvariant();

        var normalized = new PostJournalRequest(
            request.JournalNo.Trim().ToUpperInvariant(),
            request.JournalDate,
            request.FiscalPeriodId,
            request.Description.Trim(),
            normalizedCurrency,
            string.IsNullOrWhiteSpace(request.SourceModule) ? "manual" : request.SourceModule.Trim(),
            string.IsNullOrWhiteSpace(request.SourceType) ? "adjustment" : request.SourceType.Trim(),
            string.IsNullOrWhiteSpace(request.SourceId) ? idempotencyKey.Value.ToString() : request.SourceId.Trim(),
            request.Lines);

        var canonicalPayload = JsonSerializer.Serialize(new
        {
            journalNo = normalized.JournalNo,
            journalDate = normalized.JournalDate.ToString("yyyy-MM-dd"),
            fiscalPeriodId = normalized.FiscalPeriodId,
            description = normalized.Description,
            functionalCurrency = normalized.FunctionalCurrency,
            totalDebit,
            totalCredit,
            lines = normalized.Lines.Select(l => new
            {
                l.AccountId,
                l.DebitFunctional,
                l.CreditFunctional,
                l.TransactionAmount
            })
        });

        var requestHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));

        try
        {
            var journal = await repository.PostJournalAsync(
                tenantContext.Current.TenantId,
                tenantContext.Current.CompanyId,
                tenantContext.Current.UserId,
                idempotencyKey.Value,
                requestHash,
                normalized,
                cancellationToken);

            return Result<JournalSummary>.Success(journal);
        }
        catch (JournalNoConflictException)
        {
            return Result<JournalSummary>.Failure("DUPLICATE_JOURNAL_NO");
        }
        catch (FinanceIdempotencyConflictException)
        {
            return Result<JournalSummary>.Failure("IDEMPOTENCY_KEY_REUSED");
        }
        catch (JournalUnbalancedException)
        {
            return Result<JournalSummary>.Failure("UNBALANCED_JOURNAL");
        }
    }

    // ── Trial Balance & Financial Statements ─────────────────────────────────

    public Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(CancellationToken cancellationToken) =>
        repository.GetTrialBalanceAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            cancellationToken);

    public async Task<FinancialStatementsSummary> GetFinancialStatementsAsync(CancellationToken cancellationToken)
    {
        var trialBalance = await GetTrialBalanceAsync(cancellationToken);

        var balanceSheetLines = new List<FinancialStatementLine>();
        var incomeStatementLines = new List<FinancialStatementLine>();

        decimal totalAssets = 0;
        decimal totalLiabilities = 0;
        decimal totalEquity = 0;
        decimal totalRevenue = 0;
        decimal totalExpenses = 0;

        foreach (var row in trialBalance)
        {
            var line = new FinancialStatementLine(row.AccountCode, row.AccountName, row.AccountType, row.NetBalance);
            switch (row.AccountType.ToLowerInvariant())
            {
                case "asset":
                    totalAssets += row.NetBalance;
                    balanceSheetLines.Add(line);
                    break;
                case "liability":
                    totalLiabilities += row.NetBalance;
                    balanceSheetLines.Add(line);
                    break;
                case "equity":
                    totalEquity += row.NetBalance;
                    balanceSheetLines.Add(line);
                    break;
                case "revenue":
                    totalRevenue += row.NetBalance;
                    incomeStatementLines.Add(line);
                    break;
                case "expense":
                    totalExpenses += row.NetBalance;
                    incomeStatementLines.Add(line);
                    break;
            }
        }

        var netIncome = totalRevenue - totalExpenses;

        return new FinancialStatementsSummary(
            totalAssets,
            totalLiabilities,
            totalEquity,
            totalRevenue,
            totalExpenses,
            netIncome,
            balanceSheetLines,
            incomeStatementLines);
    }
}
