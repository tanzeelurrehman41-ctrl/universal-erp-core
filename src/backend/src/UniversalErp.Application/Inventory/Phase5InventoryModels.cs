namespace UniversalErp.Application.Inventory;

// ── Stock Reservation DTOs ──────────────────────────────────────────────────────────

public sealed record CreateStockReservationRequest(
    Guid VariantId,
    Guid WarehouseId,
    Guid? BinId,
    decimal Quantity,
    string SourceDocumentType,
    Guid? SourceDocumentId,
    DateTimeOffset? ExpiresAt,
    string? Note);

public sealed record ReleaseReservationBody(string? Reason);

public sealed record StockReservationSummary(
    Guid ReservationId,
    Guid ClientTransactionId,
    Guid VariantId,
    string Sku,
    Guid WarehouseId,
    string WarehouseCode,
    Guid? BinId,
    string? BinCode,
    decimal Quantity,
    decimal FulfilledQty,
    decimal RemainingReservedQty,
    string ReservationStatus,
    string SourceDocumentType,
    Guid? SourceDocumentId,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReleasedAt,
    string? Note);

public sealed record AvailableToPromiseSummary(
    Guid VariantId,
    string Sku,
    Guid WarehouseId,
    string WarehouseCode,
    Guid? BinId,
    string? BinCode,
    decimal PhysicalOnHand,
    decimal ActiveReservedQty,
    decimal AvailableToPromiseQty,
    decimal ValuedQty,
    decimal SellableAtpQty);

// ── Inventory Picking & Dispatch DTOs ───────────────────────────────────────────────

public sealed record CreatePickListRequest(
    Guid WarehouseId,
    string SourceDocumentType,
    Guid? SourceDocumentId,
    IReadOnlyList<CreatePickLineRequest> Lines,
    string? Note);

public sealed record CreatePickLineRequest(
    Guid VariantId,
    Guid BinId,
    decimal RequestedQty,
    Guid? ReservationId,
    string? Note);

public sealed record RecordPickLineRequest(
    Guid PickLineId,
    decimal PickedQty,
    string? Note);

public sealed record StockPickLineSummary(
    Guid PickLineId,
    Guid PickListId,
    Guid? ReservationId,
    Guid VariantId,
    string Sku,
    Guid BinId,
    string BinCode,
    decimal RequestedQty,
    decimal PickedQty,
    string Status,
    string? Note);

public sealed record StockPickListSummary(
    Guid PickListId,
    string PickListNo,
    Guid WarehouseId,
    string WarehouseCode,
    string SourceDocumentType,
    Guid? SourceDocumentId,
    string Status,
    Guid? AssignedTo,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DispatchedAt,
    IReadOnlyList<StockPickLineSummary> Lines);

public sealed record DispatchPickListResult(
    Guid PickListId,
    string PickListNo,
    Guid MovementId,
    decimal TotalDispatchedQty,
    decimal TotalFifoCost,
    IReadOnlyList<FifoLayerAllocation> CostAllocations,
    DateTimeOffset DispatchedAt);

// ── Stock Counts & Cycle Counting DTOs ──────────────────────────────────────────────

public sealed record CreateCountSessionRequest(
    Guid WarehouseId,
    string CountType,
    string? Note);

public sealed record RecordCountLineRequest(
    Guid VariantId,
    Guid BinId,
    decimal CountedQty,
    string? Note);

public sealed record StockCountLineSummary(
    Guid LineId,
    Guid SessionId,
    Guid VariantId,
    string Sku,
    Guid BinId,
    string BinCode,
    decimal SnapshotQty,
    decimal? CountedQty,
    decimal? VarianceQty,
    decimal UnitCost,
    decimal? VarianceExtendedCost,
    bool IsReconciled,
    Guid? ReconciledMovementId,
    Guid? CountedBy,
    DateTimeOffset? CountedAt,
    string? Note);

public sealed record StockCountSessionSummary(
    Guid SessionId,
    string CountNumber,
    Guid WarehouseId,
    string WarehouseCode,
    string CountType,
    string Status,
    Guid InitiatedBy,
    Guid? ApprovedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ApprovedAt,
    string? Note,
    IReadOnlyList<StockCountLineSummary> Lines);

public sealed record ApproveCountSessionResult(
    Guid SessionId,
    string CountNumber,
    int TotalReconciledLines,
    decimal TotalNetVarianceQty,
    decimal TotalNetVarianceCost,
    Guid? AdjustmentMovementId,
    DateTimeOffset ApprovedAt);

// ── Stock Returns DTOs ──────────────────────────────────────────────────────────────

public sealed record CreateStockReturnRequest(
    string ReturnType,
    string SourceDocumentType,
    Guid? SourceDocumentId,
    Guid WarehouseId,
    IReadOnlyList<CreateStockReturnLineRequest> Lines,
    string? Note);

public sealed record CreateStockReturnLineRequest(
    Guid VariantId,
    Guid BinId,
    decimal Quantity,
    decimal UnitCost,
    string Disposition,
    string? Note);

public sealed record StockReturnLineSummary(
    Guid LineId,
    Guid ReturnId,
    Guid VariantId,
    string Sku,
    Guid BinId,
    string BinCode,
    decimal Quantity,
    decimal UnitCost,
    decimal ExtendedCost,
    string Disposition,
    Guid? MovementId,
    string? Note);

public sealed record StockReturnSummary(
    Guid ReturnId,
    string ReturnNumber,
    string ReturnType,
    string SourceDocumentType,
    Guid? SourceDocumentId,
    Guid WarehouseId,
    string WarehouseCode,
    string Status,
    Guid CreatedBy,
    Guid? ApprovedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    string? Note,
    IReadOnlyList<StockReturnLineSummary> Lines);

public sealed record PostStockReturnResult(
    Guid ReturnId,
    string ReturnNumber,
    Guid MovementId,
    decimal TotalReturnedQty,
    decimal TotalValuationImpact,
    DateTimeOffset ProcessedAt);

// ── Inventory History & Valuation Report DTOs ───────────────────────────────────────

public sealed record BinMovementHistoryItem(
    Guid MovementId,
    int LegNumber,
    Guid VariantId,
    string Sku,
    Guid WarehouseId,
    string WarehouseCode,
    Guid BinId,
    string BinCode,
    string MovementType,
    string? SourceDocumentType,
    Guid? SourceDocumentId,
    decimal QuantityDelta,
    string StatusCode,
    DateTimeOffset OccurredAt,
    DateTimeOffset PostedAt,
    string? Note);

public sealed record InventoryValuationLayerReportItem(
    Guid LayerId,
    Guid? ParentLayerId,
    Guid VariantId,
    string Sku,
    Guid WarehouseId,
    string WarehouseCode,
    Guid BinId,
    string BinCode,
    string SourceDocumentType,
    Guid? SourceDocumentId,
    DateTimeOffset ReceivedAt,
    decimal OriginalQty,
    decimal RemainingQty,
    decimal UnitCost,
    decimal ExtendedValuation,
    string CurrencyCode,
    string StatusCode);

public sealed record InventoryValuationReportSummary(
    DateTimeOffset GeneratedAt,
    int TotalActiveLayers,
    decimal TotalPhysicalStock,
    decimal TotalValuedStock,
    decimal TotalUnvaluedStock,
    decimal TotalValuationPkr,
    decimal GeneralLedger1200BalancePkr,
    decimal ReconciliationVariancePkr,
    bool IsReconciled,
    IReadOnlyList<InventoryValuationLayerReportItem> Layers);
