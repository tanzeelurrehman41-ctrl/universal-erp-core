using UniversalErp.Application.Purchasing;
using UniversalErp.Domain.Common;

namespace UniversalErp.Api.Purchasing;

public static class PurchasingEndpointMappings
{
    public static IEndpointRouteBuilder MapPurchasingEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool isDevelopment)
    {
        var purchasing = endpoints.MapGroup("/api/v1/purchasing");

        // ── Suppliers ───────────────────────────────────────────────────────
        var listSuppliers = purchasing.MapGet("/suppliers", async (
            PurchasingService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListSuppliersAsync(ct)));

        var createSupplier = purchasing.MapPost("/suppliers", async (
            CreateSupplierRequest req,
            PurchasingService svc,
            CancellationToken ct) =>
        {
            var result = await svc.CreateSupplierAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var updateSupplier = purchasing.MapPut("/suppliers/{id:guid}", async (
            Guid id,
            UpdateSupplierRequest req,
            PurchasingService svc,
            CancellationToken ct) =>
        {
            var result = await svc.UpdateSupplierAsync(id, req, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        // ── Purchase Orders ─────────────────────────────────────────────────
        var listPurchaseOrders = purchasing.MapGet("/purchase-orders", async (
            PurchasingService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListPurchaseOrdersAsync(ct)));

        var getPurchaseOrder = purchasing.MapGet("/purchase-orders/{id:guid}", async (
            Guid id,
            PurchasingService svc,
            CancellationToken ct) =>
        {
            var po = await svc.GetPurchaseOrderAsync(id, ct);
            return po is null ? Results.NotFound() : Results.Ok(po);
        });

        var createPurchaseOrder = purchasing.MapPost("/purchase-orders", async (
            CreatePurchaseOrderRequest req,
            PurchasingService svc,
            CancellationToken ct) =>
        {
            var result = await svc.CreatePurchaseOrderAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var confirmPurchaseOrder = purchasing.MapPost("/purchase-orders/{id:guid}/confirm", async (
            Guid id,
            PurchasingService svc,
            CancellationToken ct) =>
        {
            var result = await svc.ConfirmPurchaseOrderAsync(id, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        // ── GRNs ────────────────────────────────────────────────────────────
        var listGrns = purchasing.MapGet("/grns", async (
            Guid? purchaseOrderId,
            PurchasingService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListGrnsAsync(purchaseOrderId, ct)));

        var getGrn = purchasing.MapGet("/grns/{id:guid}", async (
            Guid id,
            PurchasingService svc,
            CancellationToken ct) =>
        {
            var grn = await svc.GetGrnAsync(id, ct);
            return grn is null ? Results.NotFound() : Results.Ok(grn);
        });

        var postGrn = purchasing.MapPost("/grns", async (
            PostGrnRequest req,
            PurchasingService svc,
            CancellationToken ct) =>
        {
            var result = await svc.PostGrnAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var processOutbox = purchasing.MapPost("/outbox/process", async (
            SupplyChainOutboxService svc,
            CancellationToken ct) =>
        {
            var result = await svc.ProcessPendingEventsAsync(ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : ToProblem(result);
        });

        var redriveOutbox = purchasing.MapPost("/outbox/redrive", async (
            RedriveOutboxRequest? req,
            SupplyChainOutboxService svc,
            CancellationToken ct) =>
        {
            var result = await svc.RedriveDeadLetterEventsAsync(req?.EventId, ct);
            return result.IsSuccess
                ? Results.Ok(new { redrivenCount = result.Value })
                : ToProblem(result);
        });

        if (!isDevelopment)
        {
            listSuppliers.RequireAuthorization("purchasing.supplier.view");
            createSupplier.RequireAuthorization("purchasing.supplier.create");
            updateSupplier.RequireAuthorization("purchasing.supplier.create");
            listPurchaseOrders.RequireAuthorization("purchasing.po.view");
            getPurchaseOrder.RequireAuthorization("purchasing.po.view");
            createPurchaseOrder.RequireAuthorization("purchasing.po.create");
            confirmPurchaseOrder.RequireAuthorization("purchasing.po.confirm");
            listGrns.RequireAuthorization("purchasing.grn.view");
            getGrn.RequireAuthorization("purchasing.grn.view");
            postGrn.RequireAuthorization("purchasing.grn.post");
            processOutbox.RequireAuthorization("purchasing.grn.post");
            redriveOutbox.RequireAuthorization("purchasing.grn.post");
        }

        return endpoints;
    }

    private static IResult ToProblem<T>(Result<T> result)
    {
        var code = result.Error ?? "PURCHASING_REQUEST_FAILED";
        var status = code switch
        {
            _ when code.StartsWith("DUPLICATE_", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
            _ when code.EndsWith("_NOT_FOUND", StringComparison.Ordinal)   => StatusCodes.Status404NotFound,
            "PO_NOT_IN_DRAFT" or "PO_NOT_CONFIRMED"                        => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Problem(statusCode: status, title: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}

public sealed record RedriveOutboxRequest(Guid? EventId);
