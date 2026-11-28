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

        var listCategories = inventory.MapGet("/categories", async (
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListCategoriesAsync(cancellationToken)));
        var createCategory = inventory.MapPost("/categories", async (
            CreateProductCategoryRequest request,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            ToCreated(await service.CreateCategoryAsync(request, cancellationToken)));

        var listMovements = inventory.MapGet("/movements", async (
            int? limit,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListMovementsAsync(limit ?? 50, cancellationToken)));

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

        // ── Phase 5: Stock Reservations & ATP Endpoints ─────────────────────────────────────
        var listReservations = inventory.MapGet("/reservations", async (
            Guid? variantId,
            Guid? warehouseId,
            string? status,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListReservationsAsync(variantId, warehouseId, status, cancellationToken)));

        var createReservation = inventory.MapPost("/reservations", async (
            HttpRequest httpRequest,
            CreateStockReservationRequest request,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = Guid.TryParse(httpRequest.Headers["Idempotency-Key"], out var parsed)
                ? parsed
                : (Guid?)null;
            return ToCreated(await service.CreateReservationAsync(idempotencyKey, request, cancellationToken));
        });

        var releaseReservation = inventory.MapPost("/reservations/{reservationId:guid}/release", async (
            Guid reservationId,
            ReleaseReservationBody? body,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReleaseReservationAsync(reservationId, body?.Reason, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : ToFailure(result);
        });

        var getAtp = inventory.MapGet("/atp", async (
            Guid? variantId,
            Guid? warehouseId,
            Guid? binId,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAtpAsync(variantId, warehouseId, binId, cancellationToken)));

        // ── Phase 5: Picking & Dispatch Endpoints ───────────────────────────────────────────
        var listPickLists = inventory.MapGet("/pick-lists", async (
            Guid? warehouseId,
            string? status,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListPickListsAsync(warehouseId, status, cancellationToken)));

        var createPickList = inventory.MapPost("/pick-lists", async (
            HttpRequest httpRequest,
            CreatePickListRequest request,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = Guid.TryParse(httpRequest.Headers["Idempotency-Key"], out var parsed)
                ? parsed
                : (Guid?)null;
            return ToCreated(await service.CreatePickListAsync(idempotencyKey, request, cancellationToken));
        });

        var recordPicks = inventory.MapPost("/pick-lists/{pickListId:guid}/pick", async (
            Guid pickListId,
            IReadOnlyList<RecordPickLineRequest> lines,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RecordPickLinesAsync(pickListId, lines, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : ToFailure(result);
        });

        var dispatchPickList = inventory.MapPost("/pick-lists/{pickListId:guid}/dispatch", async (
            Guid pickListId,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DispatchPickListAsync(pickListId, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : ToFailure(result);
        });

        // ── Phase 5: Stock Counts & Cycle Counting Endpoints ────────────────────────────────
        var listCountSessions = inventory.MapGet("/counts", async (
            Guid? warehouseId,
            string? status,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListCountSessionsAsync(warehouseId, status, cancellationToken)));

        var createCountSession = inventory.MapPost("/counts", async (
            HttpRequest httpRequest,
            CreateCountSessionRequest request,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = Guid.TryParse(httpRequest.Headers["Idempotency-Key"], out var parsed)
                ? parsed
                : (Guid?)null;
            return ToCreated(await service.CreateCountSessionAsync(idempotencyKey, request, cancellationToken));
        });

        var recordCountLines = inventory.MapPost("/counts/{sessionId:guid}/lines", async (
            Guid sessionId,
            IReadOnlyList<RecordCountLineRequest> lines,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RecordCountLinesAsync(sessionId, lines, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : ToFailure(result);
        });

        var approveCountSession = inventory.MapPost("/counts/{sessionId:guid}/approve", async (
            Guid sessionId,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ApproveCountSessionAsync(sessionId, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : ToFailure(result);
        });

        // ── Phase 5: Stock Returns Endpoints ────────────────────────────────────────────────
        var listStockReturns = inventory.MapGet("/returns", async (
            Guid? warehouseId,
            string? status,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListStockReturnsAsync(warehouseId, status, cancellationToken)));

        var createStockReturn = inventory.MapPost("/returns", async (
            HttpRequest httpRequest,
            CreateStockReturnRequest request,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = Guid.TryParse(httpRequest.Headers["Idempotency-Key"], out var parsed)
                ? parsed
                : (Guid?)null;
            return ToCreated(await service.CreateStockReturnAsync(idempotencyKey, request, cancellationToken));
        });

        var processStockReturn = inventory.MapPost("/returns/{returnId:guid}/process", async (
            Guid returnId,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ProcessStockReturnAsync(returnId, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : ToFailure(result);
        });

        // ── Phase 5: Movement History & Valuation Report Endpoints ──────────────────────────
        var getMovementHistory = inventory.MapGet("/movement-history", async (
            Guid? warehouseId,
            Guid? binId,
            Guid? variantId,
            int? limit,
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetBinMovementHistoryAsync(warehouseId, binId, variantId, limit ?? 100, cancellationToken)));

        var getValuationReport = inventory.MapGet("/valuation-report", async (
            InventoryWorkflowService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetInventoryValuationReportAsync(cancellationToken)));

        if (!isDevelopment)
        {
            listCategories.RequireAuthorization("inventory.product.view");
            createCategory.RequireAuthorization("inventory.product.create");
            listMovements.RequireAuthorization("inventory.availability.view");
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

            // Phase 5 Authorization
            listReservations.RequireAuthorization("inventory.reservation.view");
            createReservation.RequireAuthorization("inventory.reservation.create");
            releaseReservation.RequireAuthorization("inventory.reservation.release");
            getAtp.RequireAuthorization("inventory.reservation.view");

            listPickLists.RequireAuthorization("inventory.pick.view");
            createPickList.RequireAuthorization("inventory.pick.create");
            recordPicks.RequireAuthorization("inventory.pick.create");
            dispatchPickList.RequireAuthorization("inventory.pick.dispatch");

            listCountSessions.RequireAuthorization("inventory.count.view");
            createCountSession.RequireAuthorization("inventory.count.create");
            recordCountLines.RequireAuthorization("inventory.count.record");
            approveCountSession.RequireAuthorization("inventory.count.approve");

            listStockReturns.RequireAuthorization("inventory.returns.create");
            createStockReturn.RequireAuthorization("inventory.returns.create");
            processStockReturn.RequireAuthorization("inventory.returns.post");

            getMovementHistory.RequireAuthorization("inventory.history.view");
            getValuationReport.RequireAuthorization("inventory.valuation.report");
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
