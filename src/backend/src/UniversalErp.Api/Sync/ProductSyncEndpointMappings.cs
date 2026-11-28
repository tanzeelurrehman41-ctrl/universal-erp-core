using UniversalErp.Application.Abstractions;
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
            ITenantContextAccessor tenantContextAccessor,
            IOfflineReceiptSyncRepository deviceRepository,
            CancellationToken cancellationToken) =>
        {
            var validationError = ProductSyncService.ValidateBatchHeader(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new { code = validationError });
            }

            var grantIsValid = await deviceRepository.ValidateDeviceGrantAsync(
                tenantContextAccessor.Current,
                request.DeviceId,
                request.GrantId,
                request.GrantToken,
                "inventory.product.create",
                cancellationToken);
            if (!grantIsValid)
            {
                return Results.Json(new { code = "OFFLINE_DEVICE_OR_GRANT_INVALID" }, statusCode: StatusCodes.Status403Forbidden);
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
