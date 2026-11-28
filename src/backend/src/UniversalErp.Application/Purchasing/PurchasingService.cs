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

    public async Task<Result<SupplierSummary>> UpdateSupplierAsync(
        Guid supplierId,
        UpdateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<SupplierSummary>.Failure("INVALID_SUPPLIER_NAME");
        if (request.PaymentTermsDays < 0)
            return Result<SupplierSummary>.Failure("INVALID_PAYMENT_TERMS");

        var ok = await repository.UpdateSupplierAsync(supplierId, request, cancellationToken);
        if (!ok) return Result<SupplierSummary>.Failure("SUPPLIER_NOT_FOUND");

        var suppliers = await repository.ListSuppliersAsync(cancellationToken);
        var updated = suppliers.FirstOrDefault(s => s.SupplierId == supplierId);
        return updated is not null
            ? Result<SupplierSummary>.Success(updated)
            : Result<SupplierSummary>.Failure("SUPPLIER_NOT_FOUND");
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

        // Validate PO exists and is confirmable / approved.
        var po = await repository.GetPurchaseOrderAsync(request.PurchaseOrderId, cancellationToken);
        if (po is null) return Result<GrnSummary>.Failure("PURCHASE_ORDER_NOT_FOUND");
        if (po.OrderStatus is not ("confirmed" or "approved" or "partially_received"))
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

    // ── Phase 6 Enhancements ─────────────────────────────────────────────

    // Tolerance Policy
    public Task<PurchasingTolerancePolicySummary> GetTolerancePolicyAsync(CancellationToken ct) =>
        repository.GetTolerancePolicyAsync(ct);

    public async Task<Result<bool>> UpdateTolerancePolicyAsync(UpdatePurchasingTolerancePolicyRequest req, CancellationToken ct)
    {
        if (req.PriceTolerancePercent < 0 || req.QuantityTolerancePercent < 0)
            return Result<bool>.Failure("INVALID_TOLERANCE_PERCENT");
        var ok = await repository.UpdateTolerancePolicyAsync(req, ct);
        return Result<bool>.Success(ok);
    }

    // PO Approval & Cancellation
    public async Task<Result<PurchaseOrderSummary>> ApprovePurchaseOrderAsync(
        Guid purchaseOrderId, ApprovePurchaseOrderRequest req, CancellationToken ct)
    {
        var po = await repository.GetPurchaseOrderAsync(purchaseOrderId, ct);
        if (po is null) return Result<PurchaseOrderSummary>.Failure("PURCHASE_ORDER_NOT_FOUND");
        if (po.OrderStatus is not ("draft" or "pending_approval"))
            return Result<PurchaseOrderSummary>.Failure("PO_NOT_IN_DRAFT_OR_PENDING");

        var approvedBy = tenantContext.Current.UserId;
        var ok = await repository.ApprovePurchaseOrderAsync(purchaseOrderId, approvedBy, req.Note, ct);
        if (!ok) return Result<PurchaseOrderSummary>.Failure("PO_APPROVAL_FAILED");

        var updated = await repository.GetPurchaseOrderAsync(purchaseOrderId, ct);
        return updated is not null
            ? Result<PurchaseOrderSummary>.Success(updated)
            : Result<PurchaseOrderSummary>.Failure("PURCHASE_ORDER_NOT_FOUND");
    }

    public async Task<Result<PurchaseOrderSummary>> CancelPurchaseOrderAsync(
        Guid purchaseOrderId, CancelPurchaseOrderRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return Result<PurchaseOrderSummary>.Failure("CANCELLATION_REASON_REQUIRED");

        var po = await repository.GetPurchaseOrderAsync(purchaseOrderId, ct);
        if (po is null) return Result<PurchaseOrderSummary>.Failure("PURCHASE_ORDER_NOT_FOUND");
        if (po.OrderStatus is not ("draft" or "pending_approval" or "approved" or "confirmed"))
            return Result<PurchaseOrderSummary>.Failure("PO_CANNOT_BE_CANCELLED");

        var ok = await repository.CancelPurchaseOrderAsync(purchaseOrderId, req.Reason, ct);
        if (!ok) return Result<PurchaseOrderSummary>.Failure("PO_CANCELLATION_FAILED");

        var updated = await repository.GetPurchaseOrderAsync(purchaseOrderId, ct);
        return updated is not null
            ? Result<PurchaseOrderSummary>.Success(updated)
            : Result<PurchaseOrderSummary>.Failure("PURCHASE_ORDER_NOT_FOUND");
    }

    // Quality Inspections & Quarantine
    public Task<IReadOnlyList<PurchaseInspectionSummary>> ListInspectionsAsync(Guid? grnId, CancellationToken ct) =>
        repository.ListInspectionsAsync(grnId, ct);

    public Task<PurchaseInspectionSummary?> GetInspectionAsync(Guid inspectionId, CancellationToken ct) =>
        repository.GetInspectionAsync(inspectionId, ct);

    public async Task<Result<PurchaseInspectionSummary>> CreateInspectionAsync(
        CreatePurchaseInspectionRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.InspectionNumber))
            return Result<PurchaseInspectionSummary>.Failure("INVALID_INSPECTION_NUMBER");
        if (req.GrnId == Guid.Empty)
            return Result<PurchaseInspectionSummary>.Failure("INVALID_GRN_ID");
        if (req.Lines is null || req.Lines.Count == 0)
            return Result<PurchaseInspectionSummary>.Failure("INSPECTION_REQUIRES_LINES");

        try
        {
            var inspectorId = tenantContext.Current.UserId;
            var id = await repository.CreateInspectionAsync(req, inspectorId, ct);
            var insp = await repository.GetInspectionAsync(id, ct);
            return insp is not null
                ? Result<PurchaseInspectionSummary>.Success(insp)
                : Result<PurchaseInspectionSummary>.Failure("INSPECTION_NOT_FOUND");
        }
        catch (Exception ex) when (ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("23505", StringComparison.OrdinalIgnoreCase))
        {
            return Result<PurchaseInspectionSummary>.Failure("DUPLICATE_INSPECTION_NUMBER");
        }
    }

    public Task<IReadOnlyList<QuarantineReleaseSummary>> ListQuarantineReleasesAsync(Guid? grnId, CancellationToken ct) =>
        repository.ListQuarantineReleasesAsync(grnId, ct);

    public async Task<Result<QuarantineReleaseSummary>> ReleaseQuarantineStockAsync(
        ReleaseQuarantineStockRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ReleaseNumber))
            return Result<QuarantineReleaseSummary>.Failure("INVALID_RELEASE_NUMBER");
        if (req.ReleasedQty <= 0)
            return Result<QuarantineReleaseSummary>.Failure("INVALID_RELEASE_QUANTITY");
        if (req.ReleaseDestination is not ("available" or "rejected"))
            return Result<QuarantineReleaseSummary>.Failure("INVALID_RELEASE_DESTINATION");

        try
        {
            var authorizedBy = tenantContext.Current.UserId;
            var id = await repository.ReleaseQuarantineStockAsync(req, authorizedBy, ct);
            var releases = await repository.ListQuarantineReleasesAsync(req.GrnId, ct);
            var rel = releases.FirstOrDefault(r => r.ReleaseId == id);
            return rel is not null
                ? Result<QuarantineReleaseSummary>.Success(rel)
                : Result<QuarantineReleaseSummary>.Failure("RELEASE_RECORD_NOT_FOUND");
        }
        catch (Exception ex) when (ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("23505", StringComparison.OrdinalIgnoreCase))
        {
            return Result<QuarantineReleaseSummary>.Failure("DUPLICATE_RELEASE_NUMBER");
        }
        catch (Exception ex) when (ex.Message.Contains("INSUFFICIENT_QUARANTINE_STOCK", StringComparison.OrdinalIgnoreCase))
        {
            return Result<QuarantineReleaseSummary>.Failure("INSUFFICIENT_QUARANTINE_STOCK");
        }
    }

    // Invoices & 3-Way Matching
    public Task<IReadOnlyList<SupplierInvoiceSummary>> ListSupplierInvoicesAsync(Guid? supplierId, CancellationToken ct) =>
        repository.ListSupplierInvoicesAsync(supplierId, ct);

    public Task<SupplierInvoiceSummary?> GetSupplierInvoiceAsync(Guid invoiceId, CancellationToken ct) =>
        repository.GetSupplierInvoiceAsync(invoiceId, ct);

    public async Task<Result<SupplierInvoiceSummary>> CreateSupplierInvoiceAsync(
        CreateSupplierInvoiceRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.InvoiceNumber))
            return Result<SupplierInvoiceSummary>.Failure("INVALID_INVOICE_NUMBER");
        if (req.SupplierId == Guid.Empty)
            return Result<SupplierInvoiceSummary>.Failure("INVALID_SUPPLIER_ID");
        if (req.Lines is null || req.Lines.Count == 0)
            return Result<SupplierInvoiceSummary>.Failure("INVOICE_REQUIRES_LINES");

        try
        {
            var createdBy = tenantContext.Current.UserId;
            var id = await repository.CreateSupplierInvoiceAsync(req, createdBy, ct);
            var inv = await repository.GetSupplierInvoiceAsync(id, ct);
            return inv is not null
                ? Result<SupplierInvoiceSummary>.Success(inv)
                : Result<SupplierInvoiceSummary>.Failure("INVOICE_NOT_FOUND");
        }
        catch (Exception ex) when (ex.Message.Contains("DUPLICATE_INVOICE_NUMBER", StringComparison.OrdinalIgnoreCase)
                                || ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase)
                                || ex.Message.Contains("23505", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SupplierInvoiceSummary>.Failure("DUPLICATE_INVOICE_NUMBER");
        }
    }

    public async Task<Result<ThreeWayMatchEvaluation>> MatchSupplierInvoiceAsync(
        Guid invoiceId, CancellationToken ct)
    {
        var inv = await repository.GetSupplierInvoiceAsync(invoiceId, ct);
        if (inv is null) return Result<ThreeWayMatchEvaluation>.Failure("INVOICE_NOT_FOUND");

        try
        {
            var evaluation = await repository.MatchSupplierInvoiceAsync(invoiceId, ct);
            return Result<ThreeWayMatchEvaluation>.Success(evaluation);
        }
        catch (Exception ex)
        {
            return Result<ThreeWayMatchEvaluation>.Failure(ex.Message);
        }
    }

    public async Task<Result<SupplierInvoiceSummary>> ApproveSupplierInvoiceAsync(
        Guid invoiceId, MatchAndApproveInvoiceRequest req, CancellationToken ct)
    {
        var inv = await repository.GetSupplierInvoiceAsync(invoiceId, ct);
        if (inv is null) return Result<SupplierInvoiceSummary>.Failure("INVOICE_NOT_FOUND");

        try
        {
            var approvedBy = tenantContext.Current.UserId;
            var ok = await repository.ApproveSupplierInvoiceAsync(invoiceId, req.ForceApprovalIfDisputed, req.ResolutionNote, approvedBy, ct);
            if (!ok) return Result<SupplierInvoiceSummary>.Failure("INVOICE_APPROVAL_FAILED");

            var updated = await repository.GetSupplierInvoiceAsync(invoiceId, ct);
            return updated is not null
                ? Result<SupplierInvoiceSummary>.Success(updated)
                : Result<SupplierInvoiceSummary>.Failure("INVOICE_NOT_FOUND");
        }
        catch (Exception ex) when (ex.Message.Contains("INVOICE_VARIANCE_EXCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SupplierInvoiceSummary>.Failure("INVOICE_VARIANCE_EXCEEDED");
        }
        catch (Exception ex) when (ex.Message.Contains("NO_OPEN_FISCAL_PERIOD", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SupplierInvoiceSummary>.Failure("NO_OPEN_FISCAL_PERIOD");
        }
    }

    // Purchase Returns & Debit Notes
    public Task<IReadOnlyList<PurchaseReturnSummary>> ListPurchaseReturnsAsync(Guid? supplierId, CancellationToken ct) =>
        repository.ListPurchaseReturnsAsync(supplierId, ct);

    public Task<PurchaseReturnSummary?> GetPurchaseReturnAsync(Guid returnId, CancellationToken ct) =>
        repository.GetPurchaseReturnAsync(returnId, ct);

    public async Task<Result<PurchaseReturnSummary>> CreatePurchaseReturnAsync(
        CreatePurchaseReturnRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ReturnNumber))
            return Result<PurchaseReturnSummary>.Failure("INVALID_RETURN_NUMBER");
        if (req.SupplierId == Guid.Empty)
            return Result<PurchaseReturnSummary>.Failure("INVALID_SUPPLIER_ID");
        if (req.Lines is null || req.Lines.Count == 0)
            return Result<PurchaseReturnSummary>.Failure("RETURN_REQUIRES_LINES");

        try
        {
            var createdBy = tenantContext.Current.UserId;
            var id = await repository.CreatePurchaseReturnAsync(req, createdBy, ct);
            var ret = await repository.GetPurchaseReturnAsync(id, ct);
            return ret is not null
                ? Result<PurchaseReturnSummary>.Success(ret)
                : Result<PurchaseReturnSummary>.Failure("RETURN_NOT_FOUND");
        }
        catch (Exception ex) when (ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("23505", StringComparison.OrdinalIgnoreCase))
        {
            return Result<PurchaseReturnSummary>.Failure("DUPLICATE_RETURN_NUMBER");
        }
    }

    public async Task<Result<PurchaseReturnSummary>> ApprovePurchaseReturnAsync(
        Guid returnId, CancellationToken ct)
    {
        var ret = await repository.GetPurchaseReturnAsync(returnId, ct);
        if (ret is null) return Result<PurchaseReturnSummary>.Failure("RETURN_NOT_FOUND");

        try
        {
            var authorizedBy = tenantContext.Current.UserId;
            var ok = await repository.ApprovePurchaseReturnAsync(returnId, authorizedBy, ct);
            if (!ok) return Result<PurchaseReturnSummary>.Failure("RETURN_APPROVAL_FAILED");

            var updated = await repository.GetPurchaseReturnAsync(returnId, ct);
            return updated is not null
                ? Result<PurchaseReturnSummary>.Success(updated)
                : Result<PurchaseReturnSummary>.Failure("RETURN_NOT_FOUND");
        }
        catch (Exception ex) when (ex.Message.Contains("INSUFFICIENT_STOCK_FOR_RETURN", StringComparison.OrdinalIgnoreCase))
        {
            return Result<PurchaseReturnSummary>.Failure("INSUFFICIENT_STOCK_FOR_RETURN");
        }
    }

    public Task<IReadOnlyList<SupplierDebitNoteSummary>> ListSupplierDebitNotesAsync(Guid? supplierId, CancellationToken ct) =>
        repository.ListSupplierDebitNotesAsync(supplierId, ct);

    public async Task<Result<SupplierDebitNoteSummary>> CreateSupplierDebitNoteAsync(
        CreateSupplierDebitNoteRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DebitNoteNumber))
            return Result<SupplierDebitNoteSummary>.Failure("INVALID_DEBIT_NOTE_NUMBER");
        if (req.SupplierId == Guid.Empty)
            return Result<SupplierDebitNoteSummary>.Failure("INVALID_SUPPLIER_ID");
        if (req.Amount <= 0)
            return Result<SupplierDebitNoteSummary>.Failure("INVALID_DEBIT_NOTE_AMOUNT");

        try
        {
            var createdBy = tenantContext.Current.UserId;
            var id = await repository.CreateSupplierDebitNoteAsync(req, createdBy, ct);
            var notes = await repository.ListSupplierDebitNotesAsync(req.SupplierId, ct);
            var dn = notes.FirstOrDefault(n => n.DebitNoteId == id);
            return dn is not null
                ? Result<SupplierDebitNoteSummary>.Success(dn)
                : Result<SupplierDebitNoteSummary>.Failure("DEBIT_NOTE_NOT_FOUND");
        }
        catch (Exception ex) when (ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("23505", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SupplierDebitNoteSummary>.Failure("DUPLICATE_DEBIT_NOTE_NUMBER");
        }
    }

    public async Task<Result<SupplierDebitNoteSummary>> ApproveSupplierDebitNoteAsync(
        Guid debitNoteId, CancellationToken ct)
    {
        var notes = await repository.ListSupplierDebitNotesAsync(null, ct);
        var dn = notes.FirstOrDefault(n => n.DebitNoteId == debitNoteId);
        if (dn is null) return Result<SupplierDebitNoteSummary>.Failure("DEBIT_NOTE_NOT_FOUND");

        try
        {
            var authorizedBy = tenantContext.Current.UserId;
            var ok = await repository.ApproveSupplierDebitNoteAsync(debitNoteId, authorizedBy, ct);
            if (!ok) return Result<SupplierDebitNoteSummary>.Failure("DEBIT_NOTE_APPROVAL_FAILED");

            var updatedList = await repository.ListSupplierDebitNotesAsync(null, ct);
            var updated = updatedList.FirstOrDefault(n => n.DebitNoteId == debitNoteId);
            return updated is not null
                ? Result<SupplierDebitNoteSummary>.Success(updated)
                : Result<SupplierDebitNoteSummary>.Failure("DEBIT_NOTE_NOT_FOUND");
        }
        catch (Exception ex) when (ex.Message.Contains("NO_OPEN_FISCAL_PERIOD", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SupplierDebitNoteSummary>.Failure("NO_OPEN_FISCAL_PERIOD");
        }
    }

    // Supplier Balances & AP Status
    public Task<IReadOnlyList<SupplierBalanceSummary>> ListSupplierBalancesAsync(CancellationToken ct) =>
        repository.ListSupplierBalancesAsync(ct);

    public Task<SupplierBalanceSummary?> GetSupplierBalanceAsync(Guid supplierId, CancellationToken ct) =>
        repository.GetSupplierBalanceAsync(supplierId, ct);
}
