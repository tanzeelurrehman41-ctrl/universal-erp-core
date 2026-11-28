using UniversalErp.Application.Sync;

namespace UniversalErp.Api.Sync;

public static class ProductSyncEndpointMappings
{
    public static IEndpointRouteBuilder MapProductSyncBatchEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool isDevelopment)
    {
        var sync = endpoints.MapGroup("/api/v1/sync");
        var batch = sync.MapPost("/products:batch", async (
            ProductSyncBatchRequest request,
            ProductSyncService service,
            CancellationToken cancellationToken) =>
        {
            var validationError = ProductSyncService.ValidateBatchHeader(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new { code = validationError });
            }

            var result = await service.ProcessBatchAsync(request, cancellationToken);
            return Results.Ok(result);
        });

        if (!isDevelopment)
        {
            batch.RequireAuthorization("inventory.product.create");
        }

        return endpoints;
    }
}
