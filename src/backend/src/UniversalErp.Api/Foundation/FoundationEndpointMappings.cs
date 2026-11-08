using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Foundation;

namespace UniversalErp.Api.Foundation;

public static class FoundationEndpointMappings
{
    public static RouteGroupBuilder MapFoundationEndpoints(this RouteGroupBuilder group)
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
