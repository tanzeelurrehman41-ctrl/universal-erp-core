namespace UniversalErp.Application.Purchasing;

// ── Tolerance Policy ────────────────────────────────────────────────────────

public sealed record PurchasingTolerancePolicySummary(
    Guid PolicyId,
    decimal PriceTolerancePercent,
    decimal QuantityTolerancePercent,
    bool AutoApproveWithinTolerance,
    DateTimeOffset UpdatedAt);

public sealed record UpdatePurchasingTolerancePolicyRequest(
    decimal PriceTolerancePercent,
    decimal QuantityTolerancePercent,
    bool AutoApproveWithinTolerance);

// ── PO Enhancements ─────────────────────────────────────────────────────────

public sealed record ApprovePurchaseOrderRequest(
    string? Note);

public sealed record CancelPurchaseOrderRequest(
    string Reason);

// ── Quality Inspection ──────────────────────────────────────────────────────

public sealed record CreatePurchaseInspectionRequest(
    string InspectionNumber,
    Guid GrnId,
    string? Notes,
    IReadOnlyList<CreatePurchaseInspectionLineRequest> Lines);

public sealed record CreatePurchaseInspectionLineRequest(
    short GrnLineNumber,
    Guid VariantId,
    decimal InspectedQty,
    decimal AcceptedQty,
    decimal QuarantinedQty,
    decimal RejectedQty,
    string? RejectionReason,
    string? DispositionNotes);

public sealed record PurchaseInspectionSummary(
    Guid InspectionId,
    string InspectionNumber,
    Guid GrnId,
    string GrnNumber,
    Guid InspectorId,
    DateTimeOffset InspectedAt,
    string OverallStatus,
    string? Notes,
    IReadOnlyList<PurchaseInspectionLineSummary> Lines);

public sealed record PurchaseInspectionLineSummary(
    Guid InspectionLineId,
    short GrnLineNumber,
    Guid VariantId,
    string Sku,
    decimal InspectedQty,
    decimal AcceptedQty,
    decimal QuarantinedQty,
    decimal RejectedQty,
    string? RejectionReason,
    string? DispositionNotes);

// ── Controlled Quarantine Release ───────────────────────────────────────────

public sealed record ReleaseQuarantineStockRequest(
    string ReleaseNumber,
    Guid InspectionLineId,
    Guid GrnId,
    Guid VariantId,
    Guid SourceBinId,
    Guid TargetBinId,
    decimal ReleasedQty,
    string ReleaseDestination, // 'available' or 'rejected'
    string? Notes);

public sealed record QuarantineReleaseSummary(
    Guid ReleaseId,
    string ReleaseNumber,
    Guid InspectionLineId,
    Guid GrnId,
    Guid VariantId,
    string Sku,
    Guid SourceBinId,
    string SourceBinCode,
    Guid TargetBinId,
    string TargetBinCode,
    decimal ReleasedQty,
    string ReleaseDestination,
    Guid AuthorizedBy,
    DateTimeOffset ReleasedAt,
    string? Notes);

// ── Supplier Invoice ────────────────────────────────────────────────────────

public sealed record CreateSupplierInvoiceRequest(
    string InvoiceNumber,
    Guid SupplierId,
    Guid? PurchaseOrderId,
    Guid? GrnId,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string CurrencyCode,
    decimal TaxAmount,
    IReadOnlyList<CreateSupplierInvoiceLineRequest> Lines);

public sealed record CreateSupplierInvoiceLineRequest(
    short LineNumber,
    short? PoLineNumber,
    short? GrnLineNumber,
    Guid VariantId,
    decimal InvoicedQty,
    decimal UnitPrice);

public sealed record SupplierInvoiceSummary(
    Guid InvoiceId,
    string InvoiceNumber,
    Guid SupplierId,
    string SupplierName,
    Guid? PurchaseOrderId,
    string? PoNumber,
    Guid? GrnId,
    string? GrnNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string CurrencyCode,
    decimal Subtotal,
    decimal TaxAmount,
    decimal TotalAmount,
    string MatchingStatus,
    string Status,
    string PaymentStatus,
    decimal PaidAmount,
    decimal VarianceAmount,
    string? VarianceReason,
    Guid? GlJournalId,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    IReadOnlyList<SupplierInvoiceLineSummary> Lines);

public sealed record SupplierInvoiceLineSummary(
    Guid InvoiceLineId,
    short LineNumber,
    short? PoLineNumber,
    short? GrnLineNumber,
    Guid VariantId,
    string Sku,
    decimal InvoicedQty,
    decimal UnitPrice,
    decimal LineTotal,
    decimal MatchedQty,
    decimal PriceVariance,
    decimal QtyVariance,
    string MatchingStatus);

public sealed record MatchAndApproveInvoiceRequest(
    bool ForceApprovalIfDisputed,
    string? ResolutionNote);

// ── Purchase Returns & Debit Notes ──────────────────────────────────────────

public sealed record CreatePurchaseReturnRequest(
    string ReturnNumber,
    Guid SupplierId,
    Guid? PurchaseOrderId,
    Guid? GrnId,
    Guid? SupplierInvoiceId,
    string Reason,
    IReadOnlyList<CreatePurchaseReturnLineRequest> Lines);

public sealed record CreatePurchaseReturnLineRequest(
    Guid VariantId,
    Guid WarehouseId,
    Guid BinId,
    decimal ReturnQty,
    decimal UnitCost,
    string FromStockStatus, // 'available' or 'quarantine'
    string? RestockReason);

public sealed record PurchaseReturnSummary(
    Guid ReturnId,
    string ReturnNumber,
    Guid SupplierId,
    string SupplierName,
    Guid? PurchaseOrderId,
    string? PoNumber,
    Guid? GrnId,
    string? GrnNumber,
    Guid? SupplierInvoiceId,
    string? InvoiceNumber,
    string Status,
    string Reason,
    Guid? AuthorizedBy,
    DateTimeOffset? AuthorizedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PurchaseReturnLineSummary> Lines);

public sealed record PurchaseReturnLineSummary(
    Guid ReturnLineId,
    short LineNumber,
    Guid VariantId,
    string Sku,
    Guid WarehouseId,
    string WarehouseName,
    Guid BinId,
    string BinCode,
    decimal ReturnQty,
    decimal UnitCost,
    decimal TotalCost,
    string FromStockStatus,
    string? RestockReason);

public sealed record CreateSupplierDebitNoteRequest(
    string DebitNoteNumber,
    Guid SupplierId,
    Guid? PurchaseReturnId,
    Guid? SupplierInvoiceId,
    DateOnly DebitNoteDate,
    decimal Amount,
    string? Notes);

public sealed record SupplierDebitNoteSummary(
    Guid DebitNoteId,
    string DebitNoteNumber,
    Guid SupplierId,
    string SupplierName,
    Guid? PurchaseReturnId,
    string? ReturnNumber,
    Guid? SupplierInvoiceId,
    string? InvoiceNumber,
    DateOnly DebitNoteDate,
    string CurrencyCode,
    decimal Amount,
    string Status,
    Guid? GlJournalId,
    Guid? AuthorizedBy,
    DateTimeOffset? AuthorizedAt,
    string? Notes);

// ── Supplier Balances & AP Status ───────────────────────────────────────────

public sealed record SupplierBalanceSummary(
    Guid SupplierId,
    string SupplierCode,
    string SupplierName,
    string CurrencyCode,
    decimal TotalInvoiced,
    decimal TotalDebitNotes,
    decimal NetPayable,
    decimal PaymentsAllocated,
    decimal OutstandingBalance,
    string PaymentStatus,
    int ActiveInvoiceCount,
    int OpenDebitNotesCount);
