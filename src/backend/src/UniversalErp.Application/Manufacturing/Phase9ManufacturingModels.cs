namespace UniversalErp.Application.Manufacturing;

public sealed record WorkCenterRecord(
    Guid Id,
    string Code,
    string Name,
    decimal HourlyLaborRate,
    decimal HourlyOverheadRate,
    decimal DailyCapacityHours,
    decimal EfficiencyPercent,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateWorkCenterRequest(
    string Code,
    string Name,
    decimal HourlyLaborRate,
    decimal HourlyOverheadRate,
    decimal DailyCapacityHours,
    decimal EfficiencyPercent);

public sealed record RoutingRecord(
    Guid Id,
    string RoutingNumber,
    string Name,
    Guid ProductVariantId,
    bool IsActive,
    string? Description,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RoutingOperationRecord>? Operations = null);

public sealed record RoutingOperationRecord(
    Guid Id,
    Guid RoutingId,
    int OperationSequence,
    string OperationName,
    Guid WorkCenterId,
    decimal SetupTimeMinutes,
    decimal RunTimeMinutesPerUnit,
    string? Description,
    DateTimeOffset CreatedAt);

public sealed record CreateRoutingRequest(
    string RoutingNumber,
    string Name,
    Guid ProductVariantId,
    string? Description,
    IReadOnlyList<CreateRoutingOperationRequest>? Operations = null);

public sealed record CreateRoutingOperationRequest(
    int OperationSequence,
    string OperationName,
    Guid WorkCenterId,
    decimal SetupTimeMinutes,
    decimal RunTimeMinutesPerUnit,
    string? Description = null);

public sealed record BomRecord(
    Guid Id,
    string BomNumber,
    Guid ProductVariantId,
    string Name,
    decimal BaseQuantity,
    Guid? UomId,
    decimal YieldPercent,
    string CurrentRevision,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<BomRevisionRecord>? Revisions = null);

public sealed record BomRevisionRecord(
    Guid Id,
    Guid BomId,
    string RevisionCode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    string? ChangeReason,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<BomItemRecord>? Items = null);

public sealed record BomItemRecord(
    Guid Id,
    Guid BomRevisionId,
    int LineNumber,
    Guid ComponentVariantId,
    string ComponentType,
    decimal QuantityPer,
    decimal ScrapPercentage,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record CreateBomRequest(
    string BomNumber,
    Guid ProductVariantId,
    string Name,
    decimal BaseQuantity,
    Guid? UomId,
    decimal YieldPercent,
    string RevisionCode,
    DateOnly EffectiveFrom,
    string? ChangeReason = null,
    IReadOnlyList<CreateBomItemRequest>? Items = null);

public sealed record CreateBomRevisionRequest(
    string RevisionCode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    string? ChangeReason = null,
    IReadOnlyList<CreateBomItemRequest>? Items = null);

public sealed record CreateBomItemRequest(
    int LineNumber,
    Guid ComponentVariantId,
    string ComponentType,
    decimal QuantityPer,
    decimal ScrapPercentage = 0m,
    string? Notes = null);

public sealed record MaterialSubstitutionRecord(
    Guid Id,
    Guid OriginalVariantId,
    Guid SubstituteVariantId,
    decimal ConversionFactor,
    int Priority,
    bool IsActive,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record CreateMaterialSubstitutionRequest(
    Guid OriginalVariantId,
    Guid SubstituteVariantId,
    decimal ConversionFactor,
    int Priority,
    string? Notes = null);

public sealed record WorkOrderRecord(
    Guid Id,
    string WorkOrderNumber,
    Guid ProductVariantId,
    Guid? BomId,
    Guid? BomRevisionId,
    Guid? RoutingId,
    Guid TargetWarehouseId,
    Guid TargetBinId,
    decimal PlannedQuantity,
    decimal CompletedQuantity,
    decimal ScrappedQuantity,
    decimal RejectedQuantity,
    DateOnly StartDate,
    DateOnly DueDate,
    string Status,
    Guid? WipGlAccountId,
    decimal TotalMaterialCost,
    decimal TotalLaborCost,
    decimal TotalOverheadCost,
    decimal TotalProductionCost,
    decimal UnitCostFinal,
    string? OriginDemandType,
    Guid? OriginDemandId,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ReleasedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<WorkOrderMaterialRecord>? Materials = null,
    IReadOnlyList<WorkOrderOperationRecord>? Operations = null);

public sealed record WorkOrderMaterialRecord(
    Guid Id,
    Guid WorkOrderId,
    int LineNumber,
    Guid ComponentVariantId,
    decimal RequiredQuantity,
    decimal ReservedQuantity,
    decimal IssuedQuantity,
    decimal ReturnedQuantity,
    Guid? ReservationId,
    decimal ConsumedCost,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record WorkOrderOperationRecord(
    Guid Id,
    Guid WorkOrderId,
    int OperationSequence,
    string OperationName,
    Guid WorkCenterId,
    decimal SetupTimeMinutes,
    decimal RunTimeMinutesPerUnit,
    decimal ActualSetupMinutes,
    decimal ActualRunMinutes,
    decimal CompletedQuantity,
    decimal ScrappedQuantity,
    decimal AllocatedLaborCost,
    decimal AllocatedOverheadCost,
    string Status,
    DateTimeOffset? CompletedAt,
    string? Notes);

public sealed record CreateWorkOrderRequest(
    string WorkOrderNumber,
    Guid ProductVariantId,
    Guid? BomId,
    Guid? BomRevisionId,
    Guid? RoutingId,
    Guid TargetWarehouseId,
    Guid TargetBinId,
    decimal PlannedQuantity,
    DateOnly StartDate,
    DateOnly DueDate,
    string? OriginDemandType = null,
    Guid? OriginDemandId = null,
    string? Notes = null);

public sealed record ReleaseWorkOrderRequest(
    Guid WarehouseId,
    Guid BinId);

public sealed record IssueMaterialRequest(
    int LineNumber,
    Guid ComponentVariantId,
    decimal Quantity,
    Guid WarehouseId,
    Guid BinId,
    string? Notes = null);

public sealed record MaterialIssueResult(
    Guid WorkOrderId,
    int LineNumber,
    decimal IssuedQuantity,
    decimal ConsumedCost,
    Guid MovementId,
    Guid JournalId);

public sealed record ReturnMaterialRequest(
    int LineNumber,
    decimal Quantity,
    Guid TargetWarehouseId,
    Guid TargetBinId,
    string? Reason = null);

public sealed record MaterialReturnResult(
    Guid WorkOrderId,
    int LineNumber,
    decimal ReturnedQuantity,
    decimal CreditedCost,
    Guid MovementId,
    Guid JournalId);

public sealed record ReportOperationProgressRequest(
    int OperationSequence,
    decimal ActualSetupMinutes,
    decimal ActualRunMinutes,
    decimal CompletedQuantity,
    decimal ScrappedQuantity,
    string? Notes = null);

public sealed record OperationProgressResult(
    Guid WorkOrderId,
    int OperationSequence,
    decimal AllocatedLaborCost,
    decimal AllocatedOverheadCost,
    Guid? JournalId);

public sealed record CreateProductionInspectionRequest(
    string InspectionNumber,
    string InspectionType,
    decimal InspectedQuantity,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    Guid? QuarantineBinId,
    string Disposition,
    string? Notes = null);

public sealed record ProductionInspectionRecord(
    Guid Id,
    Guid WorkOrderId,
    string InspectionNumber,
    string InspectionType,
    decimal InspectedQuantity,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    Guid? QuarantineBinId,
    string Disposition,
    Guid InspectorUserId,
    DateTimeOffset InspectionDate,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record CompleteWorkOrderRequest(
    decimal AcceptedQuantity,
    decimal ScrappedQuantity,
    string? Notes = null);

public sealed record WorkOrderCompletionResult(
    Guid WorkOrderId,
    decimal AcceptedQuantity,
    decimal ScrappedQuantity,
    decimal UnitCostFinal,
    decimal TotalProductionCost,
    Guid FinishedGoodsLayerId,
    Guid MovementId,
    Guid JournalId);

public sealed record GenealogyRecord(
    Guid WorkOrderId,
    string WorkOrderNumber,
    Guid ProductVariantId,
    decimal CompletedQuantity,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<GenealogyComponentNode> ConsumedComponents);

public sealed record GenealogyComponentNode(
    Guid ComponentVariantId,
    decimal ConsumedQuantity,
    decimal ConsumedCost,
    IReadOnlyList<GenealogyLayerSource> LayerSources);

public sealed record GenealogyLayerSource(
    Guid LayerId,
    Guid MovementId,
    decimal ConsumedQty,
    decimal UnitCost,
    decimal ExtendedCost,
    DateTimeOffset ReceivedAt,
    string SourceDocumentType,
    Guid? SourceDocumentId);

public sealed record WipVarianceSummaryReport(
    int TotalActiveWorkOrders,
    decimal TotalAccumulatedWip,
    decimal TotalMaterialCost,
    decimal TotalLaborCost,
    decimal TotalOverheadCost,
    IReadOnlyList<WorkOrderWipSummary> WorkOrders);

public sealed record WorkOrderWipSummary(
    Guid WorkOrderId,
    string WorkOrderNumber,
    string Status,
    decimal PlannedQuantity,
    decimal CompletedQuantity,
    decimal MaterialCost,
    decimal LaborCost,
    decimal OverheadCost,
    decimal TotalWip);
