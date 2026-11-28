namespace UniversalErp.Application.Purchasing;

// ── Request records ─────────────────────────────────────────────────────────

public sealed record CreateSupplierRequest(
    string SupplierCode,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Address,
    string CurrencyCode,
    int PaymentTermsDays);

public sealed record UpdateSupplierRequest(
    string Name,
    string? ContactName = null,
    string? Email = null,
    string? Phone = null,
    string? Address = null,
    int PaymentTermsDays = 0,
    bool IsActive = true);

public sealed record CreatePurchaseOrderRequest(
    string PoNumber,
    Guid SupplierId,
    Guid WarehouseId,
    DateOnly OrderDate,
    DateOnly? ExpectedDate,
    string? Note,
    IReadOnlyList<CreatePurchaseOrderLineRequest> Lines);

public sealed record CreatePurchaseOrderLineRequest(
    Guid VariantId,
    decimal OrderedQty,
    Guid UnitOfMeasureId,
    decimal UnitPrice);

public sealed record PostGrnRequest(
    Guid ClientTransactionId,
    string GrnNumber,
    Guid PurchaseOrderId,
    DateOnly ReceivedDate,
    string? SupplierReference,
    string? Note,
    IReadOnlyList<PostGrnLineRequest> Lines);

public sealed record PostGrnLineRequest(
    int PoLineNumber,
    Guid VariantId,
    Guid BinId,
    decimal ReceivedQty,
    Guid UnitOfMeasureId,
    decimal UnitPrice);

// ── Response records ────────────────────────────────────────────────────────

public sealed record SupplierSummary(
    Guid SupplierId,
    string SupplierCode,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string CurrencyCode,
    int PaymentTermsDays,
    bool IsActive);

public sealed record PurchaseOrderSummary(
    Guid PurchaseOrderId,
    string PoNumber,
    Guid SupplierId,
    string SupplierName,
    Guid WarehouseId,
    string WarehouseName,
    string OrderStatus,
    string CurrencyCode,
    DateOnly OrderDate,
    DateOnly? ExpectedDate,
    string? Note,
    IReadOnlyList<PurchaseOrderLineSummary> Lines);

public sealed record PurchaseOrderLineSummary(
    int LineNumber,
    Guid VariantId,
    string Sku,
    decimal OrderedQty,
    decimal ReceivedQty,
    Guid UnitOfMeasureId,
    string UomCode,
    decimal UnitPrice,
    string LineStatus);

public sealed record GrnSummary(
    Guid GrnId,
    string GrnNumber,
    Guid PurchaseOrderId,
    string PoNumber,
    Guid SupplierId,
    string SupplierName,
    Guid WarehouseId,
    string WarehouseName,
    string GrnStatus,
    DateOnly ReceivedDate,
    string? SupplierReference,
    string? Note,
    DateTimeOffset? PostedAt,
    IReadOnlyList<GrnLineSummary> Lines);

public sealed record GrnLineSummary(
    int LineNumber,
    int PoLineNumber,
    Guid VariantId,
    string Sku,
    Guid BinId,
    string BinCode,
    decimal ReceivedQty,
    string UomCode,
    decimal UnitPrice);

// ── Repository interface ────────────────────────────────────────────────────

public interface IPurchasingRepository
{
    // Suppliers
    Task<IReadOnlyList<SupplierSummary>> ListSuppliersAsync(CancellationToken cancellationToken);
    Task<Guid> CreateSupplierAsync(CreateSupplierRequest request, CancellationToken cancellationToken);
    Task<bool> UpdateSupplierAsync(Guid supplierId, UpdateSupplierRequest request, CancellationToken cancellationToken);

    // Purchase orders
    Task<IReadOnlyList<PurchaseOrderSummary>> ListPurchaseOrdersAsync(CancellationToken cancellationToken);
    Task<PurchaseOrderSummary?> GetPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken);
    Task<Guid> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, Guid createdBy, CancellationToken cancellationToken);
    Task ConfirmPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken);

    // GRNs
    Task<IReadOnlyList<GrnSummary>> ListGrnsAsync(Guid? purchaseOrderId, CancellationToken cancellationToken);
    Task<GrnSummary?> GetGrnAsync(Guid grnId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates and posts a GRN in a single database transaction.
    /// Also advances the PO line received quantities, updates PO status,
    /// and appends an outbox event for the inventory module to consume.
    /// Returns the GRN id; throws on duplicate client_transaction_id.
    /// </summary>
    Task<Guid> PostGrnAsync(
        PostGrnRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    // ── Phase 6 Enhancements ─────────────────────────────────────────────

    // Tolerance Policy
    Task<PurchasingTolerancePolicySummary> GetTolerancePolicyAsync(CancellationToken cancellationToken)
        => Task.FromResult(new PurchasingTolerancePolicySummary(Guid.Empty, 5.0m, 5.0m, true, DateTimeOffset.UtcNow));
    Task<bool> UpdateTolerancePolicyAsync(UpdatePurchasingTolerancePolicyRequest request, CancellationToken cancellationToken)
        => Task.FromResult(true);

    // PO Approval & Cancellation
    Task<bool> ApprovePurchaseOrderAsync(Guid purchaseOrderId, Guid approvedBy, string? note, CancellationToken cancellationToken)
        => Task.FromResult(true);
    Task<bool> CancelPurchaseOrderAsync(Guid purchaseOrderId, string reason, CancellationToken cancellationToken)
        => Task.FromResult(true);

    // Inspections & Quarantine
    Task<IReadOnlyList<PurchaseInspectionSummary>> ListInspectionsAsync(Guid? grnId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PurchaseInspectionSummary>>([]);
    Task<PurchaseInspectionSummary?> GetInspectionAsync(Guid inspectionId, CancellationToken cancellationToken)
        => Task.FromResult<PurchaseInspectionSummary?>(null);
    Task<Guid> CreateInspectionAsync(CreatePurchaseInspectionRequest request, Guid inspectorId, CancellationToken cancellationToken)
        => Task.FromResult(Guid.NewGuid());
    Task<IReadOnlyList<QuarantineReleaseSummary>> ListQuarantineReleasesAsync(Guid? grnId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<QuarantineReleaseSummary>>([]);
    Task<Guid> ReleaseQuarantineStockAsync(ReleaseQuarantineStockRequest request, Guid authorizedBy, CancellationToken cancellationToken)
        => Task.FromResult(Guid.NewGuid());

    // Invoices & 3-Way Matching
    Task<IReadOnlyList<SupplierInvoiceSummary>> ListSupplierInvoicesAsync(Guid? supplierId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SupplierInvoiceSummary>>([]);
    Task<SupplierInvoiceSummary?> GetSupplierInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken)
        => Task.FromResult<SupplierInvoiceSummary?>(null);
    Task<Guid> CreateSupplierInvoiceAsync(CreateSupplierInvoiceRequest request, Guid createdBy, CancellationToken cancellationToken)
        => Task.FromResult(Guid.NewGuid());
    Task<ThreeWayMatchEvaluation> MatchSupplierInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken)
        => Task.FromResult(new ThreeWayMatchEvaluation(false, false, 0, 0, 0, "unmatched", [], []));
    Task<bool> ApproveSupplierInvoiceAsync(Guid invoiceId, bool forceApprovalIfDisputed, string? resolutionNote, Guid approvedBy, CancellationToken cancellationToken)
        => Task.FromResult(true);

    // Returns & Debit Notes
    Task<IReadOnlyList<PurchaseReturnSummary>> ListPurchaseReturnsAsync(Guid? supplierId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PurchaseReturnSummary>>([]);
    Task<PurchaseReturnSummary?> GetPurchaseReturnAsync(Guid returnId, CancellationToken cancellationToken)
        => Task.FromResult<PurchaseReturnSummary?>(null);
    Task<Guid> CreatePurchaseReturnAsync(CreatePurchaseReturnRequest request, Guid createdBy, CancellationToken cancellationToken)
        => Task.FromResult(Guid.NewGuid());
    Task<bool> ApprovePurchaseReturnAsync(Guid returnId, Guid authorizedBy, CancellationToken cancellationToken)
        => Task.FromResult(true);
    Task<IReadOnlyList<SupplierDebitNoteSummary>> ListSupplierDebitNotesAsync(Guid? supplierId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SupplierDebitNoteSummary>>([]);
    Task<Guid> CreateSupplierDebitNoteAsync(CreateSupplierDebitNoteRequest request, Guid createdBy, CancellationToken cancellationToken)
        => Task.FromResult(Guid.NewGuid());
    Task<bool> ApproveSupplierDebitNoteAsync(Guid debitNoteId, Guid authorizedBy, CancellationToken cancellationToken)
        => Task.FromResult(true);

    // Supplier Balances & AP Status
    Task<IReadOnlyList<SupplierBalanceSummary>> ListSupplierBalancesAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SupplierBalanceSummary>>([]);
    Task<SupplierBalanceSummary?> GetSupplierBalanceAsync(Guid supplierId, CancellationToken cancellationToken)
        => Task.FromResult<SupplierBalanceSummary?>(null);
}
