using UniversalErp.Application.Abstractions;
using UniversalErp.Domain.Common;

namespace UniversalErp.Application.Purchasing;

public sealed class PurchasingService(
    IPurchasingRepository repository,
    ITenantContextAccessor tenantContext,
    SupplyChainOutboxService? outboxService = null)
{
    // ── Suppliers ──────────────────────────────────────────────────────────

    public Task<IReadOnlyList<SupplierSummary>> ListSuppliersAsync(
        CancellationToken cancellationToken) =>
        repository.ListSuppliersAsync(cancellationToken);

    public async Task<Result<SupplierSummary>> CreateSupplierAsync(
        CreateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SupplierCode))
            return Result<SupplierSummary>.Failure("INVALID_SUPPLIER_CODE");
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<SupplierSummary>.Failure("INVALID_SUPPLIER_NAME");
        if (request.PaymentTermsDays < 0)
            return Result<SupplierSummary>.Failure("INVALID_PAYMENT_TERMS");

        try
        {
            var id = await repository.CreateSupplierAsync(request, cancellationToken);
            var suppliers = await repository.ListSuppliersAsync(cancellationToken);
            var created = suppliers.FirstOrDefault(s => s.SupplierId == id);
            return created is null
                ? Result<SupplierSummary>.Failure("SUPPLIER_NOT_FOUND")
                : Result<SupplierSummary>.Success(created);
        }
        catch (Exception ex) when (ex.Message.Contains("DUPLICATE_SUPPLIER", StringComparison.OrdinalIgnoreCase)
                                || ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SupplierSummary>.Failure("DUPLICATE_SUPPLIER_CODE");
        }
    }

    // ── Purchase Orders ────────────────────────────────────────────────────

    public Task<IReadOnlyList<PurchaseOrderSummary>> ListPurchaseOrdersAsync(
        CancellationToken cancellationToken) =>
        repository.ListPurchaseOrdersAsync(cancellationToken);

    public Task<PurchaseOrderSummary?> GetPurchaseOrderAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken) =>
        repository.GetPurchaseOrderAsync(purchaseOrderId, cancellationToken);

    public async Task<Result<PurchaseOrderSummary>> CreatePurchaseOrderAsync(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PoNumber))
            return Result<PurchaseOrderSummary>.Failure("INVALID_PO_NUMBER");
        if (request.SupplierId == Guid.Empty)
            return Result<PurchaseOrderSummary>.Failure("INVALID_SUPPLIER_ID");
        if (request.WarehouseId == Guid.Empty)
            return Result<PurchaseOrderSummary>.Failure("INVALID_WAREHOUSE_ID");
        if (request.Lines is null || request.Lines.Count == 0)
            return Result<PurchaseOrderSummary>.Failure("PO_REQUIRES_LINES");
        if (request.Lines.Any(l => l.OrderedQty <= 0))
            return Result<PurchaseOrderSummary>.Failure("INVALID_LINE_QUANTITY");

        try
        {
            var createdBy = tenantContext.Current.UserId;
            var id = await repository.CreatePurchaseOrderAsync(request, createdBy, cancellationToken);
            var po = await repository.GetPurchaseOrderAsync(id, cancellationToken);
            return po is null
                ? Result<PurchaseOrderSummary>.Failure("PURCHASE_ORDER_NOT_FOUND")
                : Result<PurchaseOrderSummary>.Success(po);
        }
        catch (Exception ex) when (ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase))
        {
            return Result<PurchaseOrderSummary>.Failure("DUPLICATE_PO_NUMBER");
        }
    }

    public async Task<Result<PurchaseOrderSummary>> ConfirmPurchaseOrderAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var po = await repository.GetPurchaseOrderAsync(purchaseOrderId, cancellationToken);
        if (po is null) return Result<PurchaseOrderSummary>.Failure("PURCHASE_ORDER_NOT_FOUND");
        if (po.OrderStatus != "draft") return Result<PurchaseOrderSummary>.Failure("PO_NOT_IN_DRAFT");

        await repository.ConfirmPurchaseOrderAsync(purchaseOrderId, cancellationToken);
        var confirmed = await repository.GetPurchaseOrderAsync(purchaseOrderId, cancellationToken);
        return confirmed is null
            ? Result<PurchaseOrderSummary>.Failure("PURCHASE_ORDER_NOT_FOUND")
            : Result<PurchaseOrderSummary>.Success(confirmed);
    }

    // ── GRNs ───────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<GrnSummary>> ListGrnsAsync(
        Guid? purchaseOrderId,
        CancellationToken cancellationToken) =>
        repository.ListGrnsAsync(purchaseOrderId, cancellationToken);

    public Task<GrnSummary?> GetGrnAsync(
        Guid grnId,
        CancellationToken cancellationToken) =>
        repository.GetGrnAsync(grnId, cancellationToken);

    public async Task<Result<GrnSummary>> PostGrnAsync(
        PostGrnRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ClientTransactionId == Guid.Empty)
            return Result<GrnSummary>.Failure("INVALID_CLIENT_TRANSACTION_ID");
        if (string.IsNullOrWhiteSpace(request.GrnNumber))
            return Result<GrnSummary>.Failure("INVALID_GRN_NUMBER");
        if (request.PurchaseOrderId == Guid.Empty)
            return Result<GrnSummary>.Failure("INVALID_PURCHASE_ORDER_ID");
        if (request.Lines is null || request.Lines.Count == 0)
            return Result<GrnSummary>.Failure("GRN_REQUIRES_LINES");
        if (request.Lines.Any(l => l.ReceivedQty <= 0))
            return Result<GrnSummary>.Failure("INVALID_GRN_LINE_QUANTITY");

        // Validate PO exists and is confirmable.
        var po = await repository.GetPurchaseOrderAsync(request.PurchaseOrderId, cancellationToken);
        if (po is null) return Result<GrnSummary>.Failure("PURCHASE_ORDER_NOT_FOUND");
        if (po.OrderStatus is not ("confirmed" or "partially_received"))
            return Result<GrnSummary>.Failure("PO_NOT_CONFIRMED");

        try
        {
            var createdBy = tenantContext.Current.UserId;
            var grnId = await repository.PostGrnAsync(request, createdBy, cancellationToken);
            var grn = await repository.GetGrnAsync(grnId, cancellationToken);
            if (grn is null)
            {
                return Result<GrnSummary>.Failure("GRN_NOT_FOUND");
            }

            if (outboxService is not null)
            {
                try
                {
                    await outboxService.ProcessPendingEventsAsync(cancellationToken);
                }
                catch
                {
                    // Non-fatal: outbox event remains in erp_purchasing.outbox_event for background processing.
                }
            }

            return Result<GrnSummary>.Success(grn);
        }
        catch (Exception ex) when (ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase)
                                || ex.Message.Contains("23505", StringComparison.OrdinalIgnoreCase))
        {
            // Idempotent: same client_transaction_id already posted → return existing
            var existing = await repository.ListGrnsAsync(request.PurchaseOrderId, cancellationToken);
            var dup = existing.FirstOrDefault(g =>
                g.GrnNumber.Equals(request.GrnNumber, StringComparison.OrdinalIgnoreCase));
            return dup is null
                ? Result<GrnSummary>.Failure("DUPLICATE_GRN_TRANSACTION")
                : Result<GrnSummary>.Success(dup);
        }
    }
}
