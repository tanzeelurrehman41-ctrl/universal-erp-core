using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Foundation;

namespace UniversalErp.Api.Foundation;

public static class FoundationEndpointMappings
{
    public static RouteGroupBuilder MapFoundationEndpoints(this RouteGroupBuilder group, bool isDevelopment = true)
    {
        var foundation = group.MapGroup("/foundation");

        foundation.MapGet("/profile", async (
            FoundationService service,
            ITenantContextAccessor contextAccessor,
            CancellationToken ct) =>
        {
            var profile = await service.GetProfileAsync(contextAccessor.Current, ct);
            return Results.Ok(profile);
        });

        foundation.MapGet("/settings", async (
            FoundationService service,
            ITenantContextAccessor contextAccessor,
            CancellationToken ct) =>
        {
            var settings = await service.GetSettingsAsync(contextAccessor.Current, ct);
            return settings is not null ? Results.Ok(settings) : Results.NotFound();
        });

        foundation.MapPut("/settings", async (
            UpdateCompanySettingsCommand command,
            FoundationService service,
            ITenantContextAccessor contextAccessor,
            CancellationToken ct) =>
        {
            var updated = await service.UpdateSettingsAsync(contextAccessor.Current, command, ct);
            return Results.Ok(updated);
        });

        var getCurrencySettings = foundation.MapGet("/currency-settings", async (
            FoundationService service,
            ITenantContextAccessor contextAccessor,
            CancellationToken ct) =>
        {
            var settings = await service.GetCurrencySettingsAsync(contextAccessor.Current, ct);
            return settings is not null ? Results.Ok(settings) : Results.NotFound();
        });

        var updateCurrencySettings = foundation.MapPut("/currency-settings", async (
            UpdateCompanyCurrencySettingsCommand command,
            FoundationService service,
            ITenantContextAccessor contextAccessor,
            CancellationToken ct) =>
        {
            var result = await service.UpdateCurrencySettingsAsync(contextAccessor.Current, command, ct);
            if (!result.IsSuccess)
            {
                var status = result.Error switch
                {
                    "COMPANY_NOT_FOUND" => StatusCodes.Status404NotFound,
                    "CONFIRMATION_REQUIRED_FOR_FUTURE_TRANSACTIONS" => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest,
                };
                return Results.Problem(
                    statusCode: status,
                    title: result.Error,
                    detail: result.Error switch
                    {
                        "CONFIRMATION_REQUIRED_FOR_FUTURE_TRANSACTIONS" =>
                            "Explicit confirmation is required before changing functional currency for future accounting transactions.",
                        "INVALID_FUNCTIONAL_CURRENCY" => "Specify a valid active 3-letter ISO currency code for functional currency.",
                        "INVALID_PRESENTATION_CURRENCY" => "Specify a valid active 3-letter ISO currency code for presentation currency.",
                        "EFFECTIVE_DATE_REQUIRED" => "An effective date is required.",
                        "EFFECTIVE_DATE_MUST_NOT_BE_IN_THE_PAST" => "Currency effective date must not be earlier than today (CURRENT_DATE).",
                        _ => "Currency settings could not be updated."
                    },
                    extensions: new Dictionary<string, object?> { ["code"] = result.Error });
            }

            return Results.Ok(result.Value);
        });

        var listCurrencyHistory = foundation.MapGet("/currency-settings/history", async (
            int? limit,
            FoundationService service,
            ITenantContextAccessor contextAccessor,
            CancellationToken ct) =>
        {
            var items = await service.ListCurrencyHistoryAsync(contextAccessor.Current, limit ?? 50, ct);
            return Results.Ok(items);
        });

        if (!isDevelopment)
        {
            getCurrencySettings.RequireAuthorization("admin.currency_settings.view");
            updateCurrencySettings.RequireAuthorization("admin.currency_settings.manage");
            listCurrencyHistory.RequireAuthorization("admin.currency_settings.view");
        }

        foundation.MapGet("/users", async (
            FoundationService service,
            ITenantContextAccessor contextAccessor,
            CancellationToken ct) =>
        {
            var users = await service.ListUsersAsync(contextAccessor.Current, ct);
            return Results.Ok(users);
        });

        foundation.MapPost("/users", async (
            InviteCompanyUserCommand command,
            FoundationService service,
            ITenantContextAccessor contextAccessor,
            CancellationToken ct) =>
        {
            var user = await service.InviteUserAsync(contextAccessor.Current, command, ct);
            return Results.Created($"/api/v1/foundation/users/{user.UserId}", user);
        });

        foundation.MapGet("/roles", async (
            FoundationService service,
            ITenantContextAccessor contextAccessor,
            CancellationToken ct) =>
        {
            var roles = await service.ListRolesAsync(contextAccessor.Current, ct);
            return Results.Ok(roles);
        });

        foundation.MapGet("/audit-events", async (
            int? limit,
            FoundationService service,
            ITenantContextAccessor contextAccessor,
            CancellationToken ct) =>
        {
            var events = await service.ListAuditEventsAsync(contextAccessor.Current, limit ?? 50, ct);
            return Results.Ok(events);
        });

        return group;
    }
}
