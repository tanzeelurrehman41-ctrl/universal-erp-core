using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Sync;

namespace UniversalErp.Api.Sync;

public static class SyncEndpointMappings
{
    public static IEndpointRouteBuilder MapSyncChangeEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool isDevelopment)
    {
        var sync = endpoints.MapGroup("/api/v1/sync/changes");

        var products = sync.MapGet("/products", async (
            string? after,
            int? limit,
            ITenantContextAccessor tenantContextAccessor,
            ISyncChangeFeed changeFeed,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadPage(after, limit, out var afterSequence, out var pageSize))
            {
                return Results.BadRequest(new { code = "INVALID_SYNC_CURSOR_OR_LIMIT" });
            }

            var page = await changeFeed.ReadPageAsync(
                tenantContextAccessor.Current,
                "product",
                afterSequence,
                pageSize,
                cancellationToken);
            return Results.Ok(page);
        });

        var inventory = sync.MapGet("/inventory", async (
            string? after,
            int? limit,
            ITenantContextAccessor tenantContextAccessor,
            ISyncChangeFeed changeFeed,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadPage(after, limit, out var afterSequence, out var pageSize))
            {
                return Results.BadRequest(new { code = "INVALID_SYNC_CURSOR_OR_LIMIT" });
            }

            var page = await changeFeed.ReadPageAsync(
                tenantContextAccessor.Current,
                "inventory_movement",
                afterSequence,
                pageSize,
                cancellationToken);
            return Results.Ok(page);
        });

        if (!isDevelopment)
        {
            products.RequireAuthorization("inventory.product.view");
            inventory.RequireAuthorization("inventory.availability.view");
        }

        return endpoints;
    }

    private static bool TryReadPage(string? after, int? requestedLimit, out long afterSequence, out int pageSize)
    {
        afterSequence = 0;
        pageSize = requestedLimit ?? 100;
        if (pageSize is < 1 or > 500) return false;
        return string.IsNullOrEmpty(after) ||
            (long.TryParse(after, NumberStyles.None, CultureInfo.InvariantCulture, out afterSequence) && afterSequence >= 0);
    }
}
