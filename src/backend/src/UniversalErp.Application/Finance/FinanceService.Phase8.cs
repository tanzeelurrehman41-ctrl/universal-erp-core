using UniversalErp.Domain.Common;

namespace UniversalErp.Application.Finance;

public sealed partial class FinanceService
{
    // ── 1. General Ledger Inquiry & Reversals ─────────────────────────────────

    public async Task<Result<JournalSummary>> ReverseJournalAsync(
        Guid journalId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (journalId == Guid.Empty) return Result<JournalSummary>.Failure("INVALID_JOURNAL_ID");
        if (string.IsNullOrWhiteSpace(reason)) return Result<JournalSummary>.Failure("REASON_REQUIRED");

        try
        {
            var reversed = await repository.ReverseJournalAsync(
                tenantContext.Current.TenantId,
                tenantContext.Current.CompanyId,
                tenantContext.Current.UserId,
                journalId,
                reason.Trim(),
                cancellationToken);
            return Result<JournalSummary>.Success(reversed);
        }
        catch (InvalidOperationException ex)
        {
            return Result<JournalSummary>.Failure(ex.Message);
        }
    }

    // ── 2. Bank Accounts ──────────────────────────────────────────────────────

    public Task<IReadOnlyList<BankAccountSummary>> ListBankAccountsAsync(CancellationToken cancellationToken) =>
        repository.ListBankAccountsAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            cancellationToken);

    public async Task<Result<BankAccountSummary>> CreateBankAccountAsync(
        CreateBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccountNumber)) return Result<BankAccountSummary>.Failure("INVALID_ACCOUNT_NUMBER");
        if (string.IsNullOrWhiteSpace(request.AccountName)) return Result<BankAccountSummary>.Failure("INVALID_ACCOUNT_NAME");
        if (string.IsNullOrWhiteSpace(request.BankName)) return Result<BankAccountSummary>.Failure("INVALID_BANK_NAME");
        if (request.GlAccountId == Guid.Empty) return Result<BankAccountSummary>.Failure("GL_ACCOUNT_REQUIRED");

        var summary = await repository.CreateBankAccountAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            request,
            cancellationToken);
        return Result<BankAccountSummary>.Success(summary);
    }

    public Task<BankAccountSummary?> GetBankAccountAsync(Guid bankAccountId, CancellationToken cancellationToken) =>
        repository.GetBankAccountAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            bankAccountId,
            cancellationToken);

    // ── 3. Bank Statements & Reconciliation ────────────────────────────────────

    public async Task<Result<BankStatementSummary>> ImportBankStatementAsync(
        ImportBankStatementRequest request,
        CancellationToken cancellationToken)
    {
        if (request.BankAccountId == Guid.Empty) return Result<BankStatementSummary>.Failure("BANK_ACCOUNT_REQUIRED");
        if (string.IsNullOrWhiteSpace(request.StatementNumber)) return Result<BankStatementSummary>.Failure("STATEMENT_NUMBER_REQUIRED");
        if (request.Lines.Count == 0) return Result<BankStatementSummary>.Failure("STATEMENT_REQUIRES_LINES");

        var summary = await repository.ImportBankStatementAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            request,
            cancellationToken);
        return Result<BankStatementSummary>.Success(summary);
    }

    public Task<IReadOnlyList<BankStatementSummary>> ListBankStatementsAsync(
        Guid? bankAccountId,
        CancellationToken cancellationToken) =>
        repository.ListBankStatementsAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            bankAccountId,
            cancellationToken);

    public async Task<Result<BankReconciliationResult>> ReconcileBankStatementAsync(
        Guid statementId,
        string? note,
        CancellationToken cancellationToken)
    {
        if (statementId == Guid.Empty) return Result<BankReconciliationResult>.Failure("INVALID_STATEMENT_ID");

        try
        {
            var result = await repository.ReconcileBankStatementAsync(
                tenantContext.Current.TenantId,
                tenantContext.Current.CompanyId,
                tenantContext.Current.UserId,
                statementId,
                note,
                cancellationToken);
            return Result<BankReconciliationResult>.Success(result);
        }
        catch (InvalidOperationException ex)
        {
            return Result<BankReconciliationResult>.Failure(ex.Message);
        }
    }

    // ── 4. Treasury Payments ──────────────────────────────────────────────────

    public async Task<Result<TreasuryPaymentSummary>> CreateTreasuryPaymentAsync(
        CreateTreasuryPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PaymentNumber)) return Result<TreasuryPaymentSummary>.Failure("PAYMENT_NUMBER_REQUIRED");
        if (request.Amount <= 0) return Result<TreasuryPaymentSummary>.Failure("AMOUNT_MUST_BE_POSITIVE");
        if (request.BankAccountId == Guid.Empty) return Result<TreasuryPaymentSummary>.Failure("BANK_ACCOUNT_REQUIRED");
        if (string.IsNullOrWhiteSpace(request.PayeePayerName)) return Result<TreasuryPaymentSummary>.Failure("PAYEE_NAME_REQUIRED");

        try
        {
            var summary = await repository.CreateTreasuryPaymentAsync(
                tenantContext.Current.TenantId,
                tenantContext.Current.CompanyId,
                tenantContext.Current.UserId,
                request,
                cancellationToken);
            return Result<TreasuryPaymentSummary>.Success(summary);
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreasuryPaymentSummary>.Failure(ex.Message);
        }
    }

    public Task<IReadOnlyList<TreasuryPaymentSummary>> ListTreasuryPaymentsAsync(CancellationToken cancellationToken) =>
        repository.ListTreasuryPaymentsAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            cancellationToken);

    // ── 5. Tax Codes & Tax Filings ────────────────────────────────────────────

    public Task<IReadOnlyList<TaxCodeSummary>> ListTaxCodesAsync(CancellationToken cancellationToken) =>
        repository.ListTaxCodesAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            cancellationToken);

    public async Task<Result<TaxCodeSummary>> CreateTaxCodeAsync(
        CreateTaxCodeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code)) return Result<TaxCodeSummary>.Failure("TAX_CODE_REQUIRED");
        if (string.IsNullOrWhiteSpace(request.Name)) return Result<TaxCodeSummary>.Failure("TAX_NAME_REQUIRED");
        if (request.RatePercent < 0) return Result<TaxCodeSummary>.Failure("RATE_MUST_BE_NON_NEGATIVE");
        if (request.GlPayableAccountId == Guid.Empty) return Result<TaxCodeSummary>.Failure("PAYABLE_ACCOUNT_REQUIRED");

        var summary = await repository.CreateTaxCodeAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            request,
            cancellationToken);
        return Result<TaxCodeSummary>.Success(summary);
    }

    public Task<TaxLiabilitySummary> GetTaxLiabilitySummaryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken) =>
        repository.GetTaxLiabilitySummaryAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            fromDate,
            toDate,
            cancellationToken);

    public async Task<Result<TaxFilingSummary>> CreateTaxFilingAsync(
        CreateTaxFilingRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FilingNumber)) return Result<TaxFilingSummary>.Failure("FILING_NUMBER_REQUIRED");
        if (request.PeriodEnd < request.PeriodStart) return Result<TaxFilingSummary>.Failure("INVALID_DATE_RANGE");

        var summary = await repository.CreateTaxFilingAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            tenantContext.Current.UserId,
            request,
            cancellationToken);
        return Result<TaxFilingSummary>.Success(summary);
    }

    public Task<IReadOnlyList<TaxFilingSummary>> ListTaxFilingsAsync(CancellationToken cancellationToken) =>
        repository.ListTaxFilingsAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            cancellationToken);

    public async Task<Result<TaxFilingSummary>> SettleTaxFilingAsync(
        Guid filingId,
        SettleTaxFilingRequest request,
        CancellationToken cancellationToken)
    {
        if (filingId == Guid.Empty) return Result<TaxFilingSummary>.Failure("INVALID_FILING_ID");
        if (request.BankAccountId == Guid.Empty) return Result<TaxFilingSummary>.Failure("BANK_ACCOUNT_REQUIRED");

        try
        {
            var summary = await repository.SettleTaxFilingAsync(
                tenantContext.Current.TenantId,
                tenantContext.Current.CompanyId,
                tenantContext.Current.UserId,
                filingId,
                request,
                cancellationToken);
            return Result<TaxFilingSummary>.Success(summary);
        }
        catch (InvalidOperationException ex)
        {
            return Result<TaxFilingSummary>.Failure(ex.Message);
        }
    }

    // ── 6. Accruals & Recurring Schedules ─────────────────────────────────────

    public async Task<Result<RecurringScheduleSummary>> CreateRecurringScheduleAsync(
        CreateRecurringScheduleRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ScheduleName)) return Result<RecurringScheduleSummary>.Failure("SCHEDULE_NAME_REQUIRED");
        if (request.TotalAmount <= 0 || request.PeriodAmount <= 0) return Result<RecurringScheduleSummary>.Failure("AMOUNT_MUST_BE_POSITIVE");
        if (request.DebitAccountId == Guid.Empty || request.CreditAccountId == Guid.Empty) return Result<RecurringScheduleSummary>.Failure("ACCOUNTS_REQUIRED");

        var summary = await repository.CreateRecurringScheduleAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            request,
            cancellationToken);
        return Result<RecurringScheduleSummary>.Success(summary);
    }

    public Task<IReadOnlyList<RecurringScheduleSummary>> ListRecurringSchedulesAsync(CancellationToken cancellationToken) =>
        repository.ListRecurringSchedulesAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            cancellationToken);

    public async Task<Result<JournalSummary>> PostRecurringSchedulePeriodAsync(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        if (scheduleId == Guid.Empty) return Result<JournalSummary>.Failure("INVALID_SCHEDULE_ID");

        try
        {
            var journal = await repository.PostRecurringSchedulePeriodAsync(
                tenantContext.Current.TenantId,
                tenantContext.Current.CompanyId,
                tenantContext.Current.UserId,
                scheduleId,
                cancellationToken);
            return Result<JournalSummary>.Success(journal);
        }
        catch (InvalidOperationException ex)
        {
            return Result<JournalSummary>.Failure(ex.Message);
        }
    }

    // ── 7. Fixed Assets & Depreciation ────────────────────────────────────────

    public async Task<Result<FixedAssetSummary>> CreateFixedAssetAsync(
        CreateFixedAssetRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AssetCode)) return Result<FixedAssetSummary>.Failure("ASSET_CODE_REQUIRED");
        if (string.IsNullOrWhiteSpace(request.AssetName)) return Result<FixedAssetSummary>.Failure("ASSET_NAME_REQUIRED");
        if (request.AcquisitionCost <= 0) return Result<FixedAssetSummary>.Failure("COST_MUST_BE_POSITIVE");
        if (request.UsefulLifeMonths <= 0) return Result<FixedAssetSummary>.Failure("LIFE_MUST_BE_POSITIVE");

        var summary = await repository.CreateFixedAssetAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            request,
            cancellationToken);
        return Result<FixedAssetSummary>.Success(summary);
    }

    public Task<IReadOnlyList<FixedAssetSummary>> ListFixedAssetsAsync(CancellationToken cancellationToken) =>
        repository.ListFixedAssetsAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            cancellationToken);

    public async Task<Result<IReadOnlyList<AssetDepreciationRunSummary>>> ExecuteDepreciationRunAsync(
        ExecuteDepreciationRunRequest request,
        CancellationToken cancellationToken)
    {
        if (request.FiscalPeriodId == Guid.Empty) return Result<IReadOnlyList<AssetDepreciationRunSummary>>.Failure("FISCAL_PERIOD_REQUIRED");

        var runs = await repository.ExecuteDepreciationRunAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            tenantContext.Current.UserId,
            request,
            cancellationToken);
        return Result<IReadOnlyList<AssetDepreciationRunSummary>>.Success(runs);
    }

    // ── 8. Fiscal Period Operations & Checklists ──────────────────────────────

    public async Task<Result<bool>> SoftCloseFiscalPeriodAsync(
        Guid periodId,
        string? note,
        CancellationToken cancellationToken)
    {
        var ok = await repository.SoftCloseFiscalPeriodAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            tenantContext.Current.UserId,
            periodId,
            note,
            cancellationToken);
        return ok ? Result<bool>.Success(true) : Result<bool>.Failure("PERIOD_NOT_FOUND");
    }

    public async Task<Result<bool>> CloseFiscalPeriodAsync(
        Guid periodId,
        string? note,
        CancellationToken cancellationToken)
    {
        var ok = await repository.CloseFiscalPeriodAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            tenantContext.Current.UserId,
            periodId,
            note,
            cancellationToken);
        return ok ? Result<bool>.Success(true) : Result<bool>.Failure("PERIOD_NOT_FOUND");
    }

    public async Task<Result<bool>> ReopenFiscalPeriodAsync(
        Guid periodId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result<bool>.Failure("REASON_REQUIRED");

        try
        {
            var ok = await repository.ReopenFiscalPeriodAsync(
                tenantContext.Current.TenantId,
                tenantContext.Current.CompanyId,
                tenantContext.Current.UserId,
                periodId,
                reason,
                cancellationToken);
            return ok ? Result<bool>.Success(true) : Result<bool>.Failure("PERIOD_NOT_FOUND");
        }
        catch (InvalidOperationException ex)
        {
            return Result<bool>.Failure(ex.Message);
        }
    }

    public Task<IReadOnlyList<PeriodCloseChecklistItemSummary>> GetPeriodCloseChecklistAsync(
        Guid periodId,
        CancellationToken cancellationToken) =>
        repository.GetPeriodCloseChecklistAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            periodId,
            cancellationToken);

    public async Task<Result<bool>> UpdatePeriodCloseChecklistItemAsync(
        Guid itemId,
        bool isCompleted,
        string? notes,
        CancellationToken cancellationToken)
    {
        var ok = await repository.UpdatePeriodCloseChecklistItemAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            tenantContext.Current.UserId,
            itemId,
            isCompleted,
            notes,
            cancellationToken);
        return ok ? Result<bool>.Success(true) : Result<bool>.Failure("ITEM_NOT_FOUND");
    }

    public async Task<Result<YearEndCloseSummary>> CloseFiscalYearAsync(
        YearEndCloseRequest request,
        CancellationToken cancellationToken)
    {
        if (request.FiscalYearId == Guid.Empty) return Result<YearEndCloseSummary>.Failure("FISCAL_YEAR_REQUIRED");
        if (request.RetainedEarningsAccountId == Guid.Empty) return Result<YearEndCloseSummary>.Failure("RETAINED_EARNINGS_ACCOUNT_REQUIRED");

        try
        {
            var summary = await repository.CloseFiscalYearAsync(
                tenantContext.Current.TenantId,
                tenantContext.Current.CompanyId,
                tenantContext.Current.UserId,
                request,
                cancellationToken);
            return Result<YearEndCloseSummary>.Success(summary);
        }
        catch (InvalidOperationException ex)
        {
            return Result<YearEndCloseSummary>.Failure(ex.Message);
        }
    }

    // ── 9. Audit Trail & Control Dashboards ───────────────────────────────────

    public Task<FinanceControlDashboardSummary> GetControlDashboardAsync(CancellationToken cancellationToken) =>
        repository.GetControlDashboardAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            cancellationToken);

    public Task<IReadOnlyList<FinancialAuditLogSummary>> ListAuditLogsAsync(int limit, CancellationToken cancellationToken) =>
        repository.ListAuditLogsAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            limit,
            cancellationToken);
}
