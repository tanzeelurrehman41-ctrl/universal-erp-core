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

        // ── Accounts ─────────────────────────────────────────────────────────

        group.MapGet("/accounts", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var accounts = await service.ListAccountsAsync(cancellationToken);
            return Results.Ok(accounts);
        });

        group.MapPost("/accounts", async (
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

        // ── Fiscal Periods ───────────────────────────────────────────────────

        group.MapGet("/years", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var years = await service.ListFiscalYearsAsync(cancellationToken);
            return Results.Ok(years);
        });

        group.MapGet("/periods", async (
            Guid? fiscalYearId,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var periods = await service.ListFiscalPeriodsAsync(fiscalYearId, cancellationToken);
            return Results.Ok(periods);
        });

        // ── Journals ─────────────────────────────────────────────────────────

        group.MapGet("/journals", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var journals = await service.ListJournalsAsync(cancellationToken);
            return Results.Ok(journals);
        });

        group.MapGet("/journals/{journalId:guid}", async (
            Guid journalId,
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var journal = await service.GetJournalByIdAsync(journalId, cancellationToken);
            return journal is null ? Results.NotFound() : Results.Ok(journal);
        });

        group.MapPost("/journals", async (
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

        // ── Trial Balance & Statements ───────────────────────────────────────

        group.MapGet("/trial-balance", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var rows = await service.GetTrialBalanceAsync(cancellationToken);
            return Results.Ok(rows);
        });

        group.MapGet("/financial-statements", async (
            FinanceService service,
            CancellationToken cancellationToken) =>
        {
            var statements = await service.GetFinancialStatementsAsync(cancellationToken);
            return Results.Ok(statements);
        });

        return endpoints;
    }
}
