namespace UniversalErp.Application.Manufacturing;

public interface IManufacturingRepository
{
    // Work Centers
    Task<WorkCenterRecord> CreateWorkCenterAsync(WorkCenterRecord record, CancellationToken ct);
    Task<IReadOnlyList<WorkCenterRecord>> ListWorkCentersAsync(CancellationToken ct);
    Task<WorkCenterRecord?> GetWorkCenterByIdAsync(Guid id, CancellationToken ct);

    // Routings
    Task<RoutingRecord> CreateRoutingAsync(RoutingRecord record, IReadOnlyList<RoutingOperationRecord> operations, CancellationToken ct);
    Task<IReadOnlyList<RoutingRecord>> ListRoutingsAsync(CancellationToken ct);
    Task<RoutingRecord?> GetRoutingByIdAsync(Guid id, CancellationToken ct);

    // BOMs
    Task<BomRecord> CreateBomAsync(BomRecord record, BomRevisionRecord revision, IReadOnlyList<BomItemRecord> items, CancellationToken ct);
    Task<BomRevisionRecord> CreateBomRevisionAsync(BomRevisionRecord revision, IReadOnlyList<BomItemRecord> items, CancellationToken ct);
    Task<IReadOnlyList<BomRecord>> ListBomsAsync(CancellationToken ct);
    Task<BomRecord?> GetBomByIdAsync(Guid id, CancellationToken ct);
    Task<BomRecord?> GetActiveBomForVariantAsync(Guid variantId, CancellationToken ct);

    // Material Substitutions
    Task<MaterialSubstitutionRecord> CreateSubstitutionAsync(MaterialSubstitutionRecord record, CancellationToken ct);
    Task<IReadOnlyList<MaterialSubstitutionRecord>> ListSubstitutionsAsync(CancellationToken ct);

    // Work Orders
    Task<WorkOrderRecord> CreateWorkOrderAsync(WorkOrderRecord wo, IReadOnlyList<WorkOrderMaterialRecord> materials, IReadOnlyList<WorkOrderOperationRecord> operations, CancellationToken ct);
    Task<IReadOnlyList<WorkOrderRecord>> ListWorkOrdersAsync(CancellationToken ct);
    Task<WorkOrderRecord?> GetWorkOrderByIdAsync(Guid id, CancellationToken ct);
    Task<WorkOrderRecord> ReleaseWorkOrderAsync(Guid workOrderId, CancellationToken ct);
    Task<MaterialIssueResult> IssueMaterialAsync(Guid workOrderId, IssueMaterialRequest request, CancellationToken ct);
    Task<MaterialReturnResult> ReturnMaterialAsync(Guid workOrderId, ReturnMaterialRequest request, CancellationToken ct);
    Task<OperationProgressResult> ReportOperationProgressAsync(Guid workOrderId, ReportOperationProgressRequest request, CancellationToken ct);
    Task<ProductionInspectionRecord> RecordProductionInspectionAsync(ProductionInspectionRecord inspection, CancellationToken ct);
    Task<WorkOrderCompletionResult> CompleteWorkOrderAsync(Guid workOrderId, decimal acceptedQty, decimal scrappedQty, string? notes, CancellationToken ct);

    // Traceability & Reporting
    Task<GenealogyRecord?> GetGenealogyAsync(Guid workOrderId, CancellationToken ct);
    Task<WipVarianceSummaryReport> GetWipVarianceReportAsync(CancellationToken ct);
}
