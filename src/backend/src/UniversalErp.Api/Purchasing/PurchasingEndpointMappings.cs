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

        // ── Phase 6: Tolerance Policy ───────────────────────────────────────
        var getTolerance = purchasing.MapGet("/tolerance-policy", async (
            PurchasingService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetTolerancePolicyAsync(ct)));

        var updateTolerance = purchasing.MapPut("/tolerance-policy", async (
            UpdatePurchasingTolerancePolicyRequest req,
            PurchasingService svc, CancellationToken ct) =>
        {
            var result = await svc.UpdateTolerancePolicyAsync(req, ct);
            return result.IsSuccess ? Results.Ok(new { success = true }) : ToProblem(result);
        });

        // ── Phase 6: PO Approval & Cancellation ─────────────────────────────
        var approvePo = purchasing.MapPost("/purchase-orders/{id:guid}/approve", async (
            Guid id,
            ApprovePurchaseOrderRequest req,
            PurchasingService svc, CancellationToken ct) =>
        {
            var result = await svc.ApprovePurchaseOrderAsync(id, req, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        var cancelPo = purchasing.MapPost("/purchase-orders/{id:guid}/cancel", async (
            Guid id,
            CancelPurchaseOrderRequest req,
            PurchasingService svc, CancellationToken ct) =>
        {
            var result = await svc.CancelPurchaseOrderAsync(id, req, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        // ── Phase 6: Quality Inspections & Quarantine ───────────────────────
        var listInspections = purchasing.MapGet("/inspections", async (
            Guid? grnId,
            PurchasingService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListInspectionsAsync(grnId, ct)));

        var getInspection = purchasing.MapGet("/inspections/{id:guid}", async (
            Guid id,
            PurchasingService svc, CancellationToken ct) =>
        {
            var res = await svc.GetInspectionAsync(id, ct);
            return res is null ? Results.NotFound() : Results.Ok(res);
        });

        var createInspection = purchasing.MapPost("/inspections", async (
            CreatePurchaseInspectionRequest req,
            PurchasingService svc, CancellationToken ct) =>
        {
            var result = await svc.CreateInspectionAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var listQuarantineReleases = purchasing.MapGet("/quarantine-releases", async (
            Guid? grnId,
            PurchasingService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListQuarantineReleasesAsync(grnId, ct)));

        var releaseQuarantine = purchasing.MapPost("/quarantine-releases", async (
            ReleaseQuarantineStockRequest req,
            PurchasingService svc, CancellationToken ct) =>
        {
            var result = await svc.ReleaseQuarantineStockAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        // ── Phase 6: Supplier Invoices & 3-Way Matching ─────────────────────
        var listInvoices = purchasing.MapGet("/invoices", async (
            Guid? supplierId,
            PurchasingService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListSupplierInvoicesAsync(supplierId, ct)));

        var getInvoice = purchasing.MapGet("/invoices/{id:guid}", async (
            Guid id,
            PurchasingService svc, CancellationToken ct) =>
        {
            var res = await svc.GetSupplierInvoiceAsync(id, ct);
            return res is null ? Results.NotFound() : Results.Ok(res);
        });

        var createInvoice = purchasing.MapPost("/invoices", async (
            CreateSupplierInvoiceRequest req,
            PurchasingService svc, CancellationToken ct) =>
        {
            var result = await svc.CreateSupplierInvoiceAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var matchInvoice = purchasing.MapPost("/invoices/{id:guid}/match", async (
            Guid id,
            PurchasingService svc, CancellationToken ct) =>
        {
            var result = await svc.MatchSupplierInvoiceAsync(id, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        var approveInvoice = purchasing.MapPost("/invoices/{id:guid}/approve", async (
            Guid id,
            MatchAndApproveInvoiceRequest req,
            PurchasingService svc, CancellationToken ct) =>
        {
            var result = await svc.ApproveSupplierInvoiceAsync(id, req, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        // ── Phase 6: Purchase Returns & Debit Notes ─────────────────────────
        var listReturns = purchasing.MapGet("/returns", async (
            Guid? supplierId,
            PurchasingService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListPurchaseReturnsAsync(supplierId, ct)));

        var getReturn = purchasing.MapGet("/returns/{id:guid}", async (
            Guid id,
            PurchasingService svc, CancellationToken ct) =>
        {
            var res = await svc.GetPurchaseReturnAsync(id, ct);
            return res is null ? Results.NotFound() : Results.Ok(res);
        });

        var createReturn = purchasing.MapPost("/returns", async (
            CreatePurchaseReturnRequest req,
            PurchasingService svc, CancellationToken ct) =>
        {
            var result = await svc.CreatePurchaseReturnAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var approveReturn = purchasing.MapPost("/returns/{id:guid}/approve", async (
            Guid id,
            PurchasingService svc, CancellationToken ct) =>
        {
            var result = await svc.ApprovePurchaseReturnAsync(id, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        var listDebitNotes = purchasing.MapGet("/debit-notes", async (
            Guid? supplierId,
            PurchasingService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListSupplierDebitNotesAsync(supplierId, ct)));

        var createDebitNote = purchasing.MapPost("/debit-notes", async (
            CreateSupplierDebitNoteRequest req,
            PurchasingService svc, CancellationToken ct) =>
        {
            var result = await svc.CreateSupplierDebitNoteAsync(req, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : ToProblem(result);
        });

        var approveDebitNote = purchasing.MapPost("/debit-notes/{id:guid}/approve", async (
            Guid id,
            PurchasingService svc, CancellationToken ct) =>
        {
            var result = await svc.ApproveSupplierDebitNoteAsync(id, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result);
        });

        // ── Phase 6: Supplier Balances & AP Status ──────────────────────────
        var listSupplierBalances = purchasing.MapGet("/suppliers/balances", async (
            PurchasingService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListSupplierBalancesAsync(ct)));

        var getSupplierBalance = purchasing.MapGet("/suppliers/{id:guid}/balance", async (
            Guid id,
            PurchasingService svc, CancellationToken ct) =>
        {
            var res = await svc.GetSupplierBalanceAsync(id, ct);
            return res is null ? Results.NotFound() : Results.Ok(res);
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
            approvePo.RequireAuthorization("purchasing.po.approve");
            cancelPo.RequireAuthorization("purchasing.po.approve");
            listGrns.RequireAuthorization("purchasing.grn.view");
            getGrn.RequireAuthorization("purchasing.grn.view");
            postGrn.RequireAuthorization("purchasing.grn.post");
            processOutbox.RequireAuthorization("purchasing.grn.post");
            redriveOutbox.RequireAuthorization("purchasing.grn.post");

            getTolerance.RequireAuthorization("purchasing.invoice.view");
            updateTolerance.RequireAuthorization("purchasing.invoice.match");

            listInspections.RequireAuthorization("purchasing.inspection.view");
            getInspection.RequireAuthorization("purchasing.inspection.view");
            createInspection.RequireAuthorization("purchasing.inspection.record");
            listQuarantineReleases.RequireAuthorization("purchasing.inspection.view");
            releaseQuarantine.RequireAuthorization("purchasing.quarantine.release");

            listInvoices.RequireAuthorization("purchasing.invoice.view");
            getInvoice.RequireAuthorization("purchasing.invoice.view");
            createInvoice.RequireAuthorization("purchasing.invoice.create");
            matchInvoice.RequireAuthorization("purchasing.invoice.match");
            approveInvoice.RequireAuthorization("purchasing.invoice.match");

            listReturns.RequireAuthorization("purchasing.return.view");
            getReturn.RequireAuthorization("purchasing.return.view");
            createReturn.RequireAuthorization("purchasing.return.create");
            approveReturn.RequireAuthorization("purchasing.return.create");

            listDebitNotes.RequireAuthorization("purchasing.debit_note.view");
            createDebitNote.RequireAuthorization("purchasing.debit_note.create");
            approveDebitNote.RequireAuthorization("purchasing.debit_note.create");

            listSupplierBalances.RequireAuthorization("purchasing.supplier.balance");
            getSupplierBalance.RequireAuthorization("purchasing.supplier.balance");
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
            "PO_NOT_IN_DRAFT" or "PO_NOT_CONFIRMED" or "PO_NOT_IN_DRAFT_OR_PENDING" or "PO_CANNOT_BE_CANCELLED" or "INVOICE_VARIANCE_EXCEEDED" => StatusCodes.Status409Conflict,
            "INSUFFICIENT_QUARANTINE_STOCK" or "INSUFFICIENT_STOCK_FOR_RETURN" => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Problem(statusCode: status, title: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}

public sealed record RedriveOutboxRequest(Guid? EventId);
