using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Common;

namespace UniversalErp.Api.Inventory;

public static class InventoryEndpointMappings
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool isDevelopment)
    {
        var inventory = endpoints.MapGroup("/api/v1/inventory");

        var listUnits = inventory.MapGet("/units-of-measure", async (
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListUnitsAsync(cancellationToken)));
        var createUnit = inventory.MapPost("/units-of-measure", async (
            CreateUnitOfMeasureRequest request,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            ToCreated(await service.CreateUnitAsync(request, cancellationToken)));

        var listVariants = inventory.MapGet("/variants", async (
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListVariantsAsync(cancellationToken)));
        var createVariant = endpoints.MapPost("/api/v1/products/{productId:guid}/variants", async (
            Guid productId,
            CreateProductVariantRequest request,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            ToCreated(await service.CreateVariantAsync(request with { ProductId = productId }, cancellationToken)));

        var listWarehouses = inventory.MapGet("/warehouses", async (
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListWarehousesAsync(cancellationToken)));
        var createWarehouse = inventory.MapPost("/warehouses", async (
            CreateWarehouseRequest request,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            ToCreated(await service.CreateWarehouseAsync(request, cancellationToken)));

        var listBins = inventory.MapGet("/bins", async (
            Guid? warehouseId,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListBinsAsync(warehouseId, cancellationToken)));
        var createBin = inventory.MapPost("/warehouses/{warehouseId:guid}/bins", async (
            Guid warehouseId,
            CreateStockBinRequest request,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            ToCreated(await service.CreateBinAsync(warehouseId, request, cancellationToken)));

        var availability = inventory.MapGet("/availability", async (
            Guid? variantId,
            Guid? warehouseId,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAvailabilityAsync(variantId, warehouseId, cancellationToken)));

        var postReceipt = inventory.MapPost("/receipts", async (
            HttpRequest httpRequest,
            PostInventoryReceiptRequest request,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = Guid.TryParse(httpRequest.Headers["Idempotency-Key"], out var parsed)
                ? parsed
                : (Guid?)null;
            var result = await service.PostReceiptAsync(idempotencyKey, request, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : ToFailure(result);
        });

        var postTransfer = inventory.MapPost("/transfers", async (
            HttpRequest httpRequest,
            PostInventoryTransferRequest request,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = Guid.TryParse(httpRequest.Headers["Idempotency-Key"], out var parsed)
                ? parsed
                : (Guid?)null;
            var result = await service.PostTransferAsync(idempotencyKey, request, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : ToFailure(result);
        });

        var postAdjustment = inventory.MapPost("/adjustments", async (
            HttpRequest httpRequest,
            PostInventoryAdjustmentRequest request,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = Guid.TryParse(httpRequest.Headers["Idempotency-Key"], out var parsed)
                ? parsed
                : (Guid?)null;
            var result = await service.PostAdjustmentAsync(idempotencyKey, request, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : ToFailure(result);
        });

        if (!isDevelopment)
        {
            listUnits.RequireAuthorization("inventory.uom.view");
            createUnit.RequireAuthorization("inventory.uom.create");
            listVariants.RequireAuthorization("inventory.variant.view");
            createVariant.RequireAuthorization("inventory.variant.create");
            listWarehouses.RequireAuthorization("inventory.warehouse.view");
            createWarehouse.RequireAuthorization("inventory.warehouse.create");
            listBins.RequireAuthorization("inventory.warehouse.view");
            createBin.RequireAuthorization("inventory.warehouse.create");
            availability.RequireAuthorization("inventory.availability.view");
            postReceipt.RequireAuthorization("inventory.receipt.create", "inventory.receipt.post");
            postTransfer.RequireAuthorization("inventory.transfer.create");
            postAdjustment.RequireAuthorization("inventory.adjustment.create");
        }

        return endpoints;
    }

    private static IResult ToCreated<T>(Result<T> result) => result.IsSuccess
        ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
        : ToFailure(result);

    private static IResult ToFailure<T>(Result<T> result)
    {
        var code = result.Error ?? "INVENTORY_REQUEST_FAILED";
        var status = code.StartsWith("DUPLICATE_", StringComparison.Ordinal) ||
                     code is "IDEMPOTENCY_KEY_REUSED" or "INVENTORY_TRANSACTION_CONFLICT"
            ? StatusCodes.Status409Conflict
            : code.EndsWith("_NOT_FOUND", StringComparison.Ordinal)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
        return Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}
