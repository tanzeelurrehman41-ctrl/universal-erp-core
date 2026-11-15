using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UniversalErp.Application.Finance;

namespace UniversalErp.Api.Finance;

public static class FinanceEndpointMappings
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool isDevelopment)
    {
        var group = endpoints.MapGroup("/api/v1/finance");

        if (!isDevelopment)
        {
            group.RequireAuthorization();
        }

        // ── 1. Accounts ───────────────────────────────────────────────────────

        var listAccounts = group.MapGet("/accounts", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var accounts = await service.ListAccountsAsync(cancellationToken);
            return Results.Ok(accounts);
        });

        var createAccount = group.MapPost("/accounts", async (
            CreateAccountRequest request,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAccountAsync(request, cancellationToken);
            if (!result.IsSuccess)
            {
                var statusCode = result.Error switch
                {
                    "DUPLICATE_ACCOUNT_CODE" => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest,
                };
                return Results.Problem(statusCode: statusCode, title: result.Error);
            }

            var account = result.Value!;
            return Results.Created($"/api/v1/finance/accounts/{account.AccountId}", account);
        });

        // ── 2. Fiscal Periods & Years ─────────────────────────────────────────

        var listYears = group.MapGet("/years", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var years = await service.ListFiscalYearsAsync(cancellationToken);
            return Results.Ok(years);
        });

        var listPeriods = group.MapGet("/periods", async (
            Guid? fiscalYearId,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var periods = await service.ListFiscalPeriodsAsync(fiscalYearId, cancellationToken);
            return Results.Ok(periods);
        });

        var softClosePeriod = group.MapPost("/periods/{id:guid}/soft-close", async (
            Guid id,
            SoftCloseFiscalPeriodRequest? req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SoftCloseFiscalPeriodAsync(id, req?.Note, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        var closePeriod = group.MapPost("/periods/{id:guid}/close", async (
            Guid id,
            CloseFiscalPeriodRequest? req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CloseFiscalPeriodAsync(id, req?.Note, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        var reopenPeriod = group.MapPost("/periods/{id:guid}/reopen", async (
            Guid id,
            ReopenFiscalPeriodRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReopenFiscalPeriodAsync(id, req.Reason, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        var getChecklist = group.MapGet("/periods/{id:guid}/checklist", async (
            Guid id,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var items = await service.GetPeriodCloseChecklistAsync(id, cancellationToken);
            return Results.Ok(items);
        });

        var updateChecklist = group.MapPost("/periods/checklist/{itemId:guid}", async (
            Guid itemId,
            UpdateChecklistItemRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdatePeriodCloseChecklistItemAsync(itemId, req.IsCompleted, req.Notes, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        var closeYear = group.MapPost("/years/{id:guid}/close", async (
            Guid id,
            YearEndCloseRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var finalReq = req with { FiscalYearId = id };
            var result = await service.CloseFiscalYearAsync(finalReq, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── 3. Journals & Reversals ───────────────────────────────────────────

        var listJournals = group.MapGet("/journals", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var journals = await service.ListJournalsAsync(cancellationToken);
            return Results.Ok(journals);
        });

        var getJournal = group.MapGet("/journals/{journalId:guid}", async (
            Guid journalId,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var journal = await service.GetJournalByIdAsync(journalId, cancellationToken);
            return journal is null ? Results.NotFound() : Results.Ok(journal);
        });

        var postJournal = group.MapPost("/journals", async (
            HttpRequest httpRequest,
            PostJournalRequest request,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var keyHeader = httpRequest.Headers["Idempotency-Key"].ToString();
            Guid? idempotencyKey = Guid.TryParse(keyHeader, out var parsedKey)
                ? parsedKey
                : null;

            var result = await service.PostJournalAsync(request, idempotencyKey, cancellationToken);
            if (!result.IsSuccess)
            {
                var statusCode = result.Error switch
                {
                    "DUPLICATE_JOURNAL_NO" or "IDEMPOTENCY_KEY_REUSED" => StatusCodes.Status409Conflict,
                    "UNBALANCED_JOURNAL" => StatusCodes.Status422UnprocessableEntity,
                    _ => StatusCodes.Status400BadRequest,
                };

                return Results.Problem(
                    statusCode: statusCode,
                    title: result.Error,
                    detail: result.Error switch
                    {
                        "INVALID_IDEMPOTENCY_KEY" => "An Idempotency-Key header containing a valid UUID is required.",
                        "JOURNAL_MINIMUM_TWO_LINES_REQUIRED" => "Double-entry journals must have at least two lines.",
                        "UNBALANCED_JOURNAL" => "Total debits must strictly equal total credits.",
                        "DUPLICATE_JOURNAL_NO" => "A journal with this number already exists.",
                        "IDEMPOTENCY_KEY_REUSED" => "This Idempotency-Key was already used with different payload.",
                        _ => "Failed to post journal entry. Check the submitted fields."
                    });
            }

            var journal = result.Value!;
            return Results.Created($"/api/v1/finance/journals/{journal.JournalId}", journal);
        });

        var reverseJournal = group.MapPost("/journals/{journalId:guid}/reverse", async (
            Guid journalId,
            ReverseJournalRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReverseJournalAsync(journalId, req.Reason, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── 4. Trial Balance & Statements ─────────────────────────────────────

        var trialBalance = group.MapGet("/trial-balance", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var rows = await service.GetTrialBalanceAsync(cancellationToken);
            return Results.Ok(rows);
        });

        var financialStatements = group.MapGet("/financial-statements", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var statements = await service.GetFinancialStatementsAsync(cancellationToken);
            return Results.Ok(statements);
        });

        // ── 5. Bank Accounts ──────────────────────────────────────────────────

        var listBankAccounts = group.MapGet("/bank-accounts", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListBankAccountsAsync(cancellationToken)));

        var createBankAccount = group.MapPost("/bank-accounts", async (
            CreateBankAccountRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateBankAccountAsync(req, cancellationToken);
            return result.IsSuccess ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created) : Results.BadRequest(result.Error);
        });

        var getBankAccount = group.MapGet("/bank-accounts/{id:guid}", async (
            Guid id,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var acc = await service.GetBankAccountAsync(id, cancellationToken);
            return acc is null ? Results.NotFound() : Results.Ok(acc);
        });

        // ── 6. Bank Statements & Reconciliation ────────────────────────────────

        var importBankStatement = group.MapPost("/bank-statements/import", async (
            ImportBankStatementRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ImportBankStatementAsync(req, cancellationToken);
            return result.IsSuccess ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created) : Results.BadRequest(result.Error);
        });

        var listBankStatements = group.MapGet("/bank-statements", async (
            Guid? bankAccountId,
            FinanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListBankStatementsAsync(bankAccountId, cancellationToken)));

        var reconcileBankStatement = group.MapPost("/bank-statements/{id:guid}/reconcile", async (
            Guid id,
            ReconcileBankStatementRequest? req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReconcileBankStatementAsync(id, req?.Note, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── 7. Treasury Payments ──────────────────────────────────────────────

        var createTreasuryPayment = group.MapPost("/treasury-payments", async (
            CreateTreasuryPaymentRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateTreasuryPaymentAsync(req, cancellationToken);
            return result.IsSuccess ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created) : Results.BadRequest(result.Error);
        });

        var listTreasuryPayments = group.MapGet("/treasury-payments", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListTreasuryPaymentsAsync(cancellationToken)));

        // ── 8. Tax Codes & Tax Filings ────────────────────────────────────────

        var listTaxCodes = group.MapGet("/tax-codes", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListTaxCodesAsync(cancellationToken)));

        var createTaxCode = group.MapPost("/tax-codes", async (
            CreateTaxCodeRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateTaxCodeAsync(req, cancellationToken);
            return result.IsSuccess ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created) : Results.BadRequest(result.Error);
        });

        var getTaxSummary = group.MapGet("/tax/summary", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var from = fromDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1));
            var to = toDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            return Results.Ok(await service.GetTaxLiabilitySummaryAsync(from, to, cancellationToken));
        });

        var createTaxFiling = group.MapPost("/tax/filings", async (
            CreateTaxFilingRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateTaxFilingAsync(req, cancellationToken);
            return result.IsSuccess ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created) : Results.BadRequest(result.Error);
        });

        var listTaxFilings = group.MapGet("/tax/filings", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListTaxFilingsAsync(cancellationToken)));

        var settleTaxFiling = group.MapPost("/tax/filings/{id:guid}/settle", async (
            Guid id,
            SettleTaxFilingRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SettleTaxFilingAsync(id, req, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── 9. Accruals & Recurring Schedules ─────────────────────────────────

        var createRecurringSchedule = group.MapPost("/recurring-schedules", async (
            CreateRecurringScheduleRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateRecurringScheduleAsync(req, cancellationToken);
            return result.IsSuccess ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created) : Results.BadRequest(result.Error);
        });

        var listRecurringSchedules = group.MapGet("/recurring-schedules", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListRecurringSchedulesAsync(cancellationToken)));

        var postRecurringSchedulePeriod = group.MapPost("/recurring-schedules/{id:guid}/post-period", async (
            Guid id,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.PostRecurringSchedulePeriodAsync(id, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── 10. Fixed Assets & Depreciation ───────────────────────────────────

        var createFixedAsset = group.MapPost("/fixed-assets", async (
            CreateFixedAssetRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateFixedAssetAsync(req, cancellationToken);
            return result.IsSuccess ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created) : Results.BadRequest(result.Error);
        });

        var listFixedAssets = group.MapGet("/fixed-assets", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListFixedAssetsAsync(cancellationToken)));

        var executeDepreciationRun = group.MapPost("/fixed-assets/depreciation-runs", async (
            ExecuteDepreciationRunRequest req,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ExecuteDepreciationRunAsync(req, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── 11. Dashboards & Audit Trail ──────────────────────────────────────

        var getControlDashboard = group.MapGet("/control-dashboard", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetControlDashboardAsync(cancellationToken)));

        var listAuditLogs = group.MapGet("/audit-logs", async (
            int? limit,
            FinanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAuditLogsAsync(limit ?? 50, cancellationToken)));

        // Enforce permissions in non-dev environment
        if (!isDevelopment)
        {
            listAccounts.RequireAuthorization("finance.account.view");
            createAccount.RequireAuthorization("finance.account.manage");
            listJournals.RequireAuthorization("finance.journal.view");
            getJournal.RequireAuthorization("finance.journal.view");
            postJournal.RequireAuthorization("finance.journal.post");
            reverseJournal.RequireAuthorization("finance.journal.reverse");
            trialBalance.RequireAuthorization("finance.reports.view");
            financialStatements.RequireAuthorization("finance.reports.view");
            getControlDashboard.RequireAuthorization("finance.reports.view");
            listBankAccounts.RequireAuthorization("finance.bank.manage");
            createBankAccount.RequireAuthorization("finance.bank.manage");
            importBankStatement.RequireAuthorization("finance.bank.manage");
            reconcileBankStatement.RequireAuthorization("finance.bank.reconcile");
            createTreasuryPayment.RequireAuthorization("finance.bank.manage");
            listTaxCodes.RequireAuthorization("finance.tax.manage");
            createTaxFiling.RequireAuthorization("finance.tax.manage");
            settleTaxFiling.RequireAuthorization("finance.tax.manage");
            createRecurringSchedule.RequireAuthorization("finance.journal.post");
            postRecurringSchedulePeriod.RequireAuthorization("finance.journal.post");
            createFixedAsset.RequireAuthorization("finance.asset.manage");
            executeDepreciationRun.RequireAuthorization("finance.asset.depreciate");
            closePeriod.RequireAuthorization("finance.period.close");
            reopenPeriod.RequireAuthorization("finance.period.reopen");
            closeYear.RequireAuthorization("finance.period.close");
        }

        return endpoints;
    }
}
