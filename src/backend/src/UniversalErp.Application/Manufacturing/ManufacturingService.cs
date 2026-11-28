using UniversalErp.Application.Abstractions;
using UniversalErp.Domain.Common;

namespace UniversalErp.Application.Manufacturing;

public sealed class ManufacturingService(
    IManufacturingRepository repository,
    ITenantContextAccessor tenantContext)
{
    // ── Work Centers ──────────────────────────────────────────────────────────

    public Task<IReadOnlyList<WorkCenterRecord>> ListWorkCentersAsync(CancellationToken ct) =>
        repository.ListWorkCentersAsync(ct);

    public Task<WorkCenterRecord?> GetWorkCenterByIdAsync(Guid id, CancellationToken ct) =>
        repository.GetWorkCenterByIdAsync(id, ct);

    public async Task<Result<WorkCenterRecord>> CreateWorkCenterAsync(
        CreateWorkCenterRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 50)
            return Result<WorkCenterRecord>.Failure("INVALID_WORK_CENTER_CODE");

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
            return Result<WorkCenterRecord>.Failure("INVALID_WORK_CENTER_NAME");

        if (request.HourlyLaborRate < 0m || request.HourlyOverheadRate < 0m)
            return Result<WorkCenterRecord>.Failure("NEGATIVE_RATES_FORBIDDEN");

        if (request.DailyCapacityHours <= 0m || request.DailyCapacityHours > 24m)
            return Result<WorkCenterRecord>.Failure("INVALID_DAILY_CAPACITY");

        if (request.EfficiencyPercent <= 0m || request.EfficiencyPercent > 200m)
            return Result<WorkCenterRecord>.Failure("INVALID_EFFICIENCY_PERCENT");

        var record = new WorkCenterRecord(
            Id: Guid.NewGuid(),
            Code: request.Code.Trim().ToUpperInvariant(),
            Name: request.Name.Trim(),
            HourlyLaborRate: request.HourlyLaborRate,
            HourlyOverheadRate: request.HourlyOverheadRate,
            DailyCapacityHours: request.DailyCapacityHours,
            EfficiencyPercent: request.EfficiencyPercent,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var created = await repository.CreateWorkCenterAsync(record, ct);
        return Result<WorkCenterRecord>.Success(created);
    }

    // ── Routings & Operations ──────────────────────────────────────────────────

    public Task<IReadOnlyList<RoutingRecord>> ListRoutingsAsync(CancellationToken ct) =>
        repository.ListRoutingsAsync(ct);

    public Task<RoutingRecord?> GetRoutingByIdAsync(Guid id, CancellationToken ct) =>
        repository.GetRoutingByIdAsync(id, ct);

    public async Task<Result<RoutingRecord>> CreateRoutingAsync(
        CreateRoutingRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RoutingNumber) || request.RoutingNumber.Length > 50)
            return Result<RoutingRecord>.Failure("INVALID_ROUTING_NUMBER");

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
            return Result<RoutingRecord>.Failure("INVALID_ROUTING_NAME");

        if (request.ProductVariantId == Guid.Empty)
            return Result<RoutingRecord>.Failure("INVALID_PRODUCT_VARIANT");

        var routingId = Guid.NewGuid();
        var routingRecord = new RoutingRecord(
            Id: routingId,
            RoutingNumber: request.RoutingNumber.Trim().ToUpperInvariant(),
            Name: request.Name.Trim(),
            ProductVariantId: request.ProductVariantId,
            IsActive: true,
            Description: request.Description?.Trim(),
            CreatedAt: DateTimeOffset.UtcNow);

        var ops = new List<RoutingOperationRecord>();
        if (request.Operations != null)
        {
            foreach (var op in request.Operations)
            {
                if (op.OperationSequence <= 0)
                    return Result<RoutingRecord>.Failure("INVALID_OPERATION_SEQUENCE");
                if (string.IsNullOrWhiteSpace(op.OperationName))
                    return Result<RoutingRecord>.Failure("INVALID_OPERATION_NAME");
                if (op.WorkCenterId == Guid.Empty)
                    return Result<RoutingRecord>.Failure("INVALID_WORK_CENTER");

                ops.Add(new RoutingOperationRecord(
                    Id: Guid.NewGuid(),
                    RoutingId: routingId,
                    OperationSequence: op.OperationSequence,
                    OperationName: op.OperationName.Trim(),
                    WorkCenterId: op.WorkCenterId,
                    SetupTimeMinutes: Math.Max(0m, op.SetupTimeMinutes),
                    RunTimeMinutesPerUnit: Math.Max(0m, op.RunTimeMinutesPerUnit),
                    Description: op.Description?.Trim(),
                    CreatedAt: DateTimeOffset.UtcNow));
            }
        }

        var created = await repository.CreateRoutingAsync(routingRecord, ops, ct);
        return Result<RoutingRecord>.Success(created);
    }

    // ── BOMs & Revisions ───────────────────────────────────────────────────────

    public Task<IReadOnlyList<BomRecord>> ListBomsAsync(CancellationToken ct) =>
        repository.ListBomsAsync(ct);

    public Task<BomRecord?> GetBomByIdAsync(Guid id, CancellationToken ct) =>
        repository.GetBomByIdAsync(id, ct);

    public async Task<Result<BomRecord>> CreateBomAsync(
        CreateBomRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BomNumber) || request.BomNumber.Length > 50)
            return Result<BomRecord>.Failure("INVALID_BOM_NUMBER");

        if (request.ProductVariantId == Guid.Empty)
            return Result<BomRecord>.Failure("INVALID_PRODUCT_VARIANT");

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
            return Result<BomRecord>.Failure("INVALID_BOM_NAME");

        if (request.BaseQuantity <= 0m)
            return Result<BomRecord>.Failure("INVALID_BASE_QUANTITY");

        if (request.YieldPercent <= 0m || request.YieldPercent > 100m)
            return Result<BomRecord>.Failure("INVALID_YIELD_PERCENT");

        if (string.IsNullOrWhiteSpace(request.RevisionCode))
            return Result<BomRecord>.Failure("INVALID_REVISION_CODE");

        var bomId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();

        var bomRecord = new BomRecord(
            Id: bomId,
            BomNumber: request.BomNumber.Trim().ToUpperInvariant(),
            ProductVariantId: request.ProductVariantId,
            Name: request.Name.Trim(),
            BaseQuantity: request.BaseQuantity,
            UomId: request.UomId,
            YieldPercent: request.YieldPercent,
            CurrentRevision: request.RevisionCode.Trim().ToUpperInvariant(),
            Status: "active",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var revisionRecord = new BomRevisionRecord(
            Id: revisionId,
            BomId: bomId,
            RevisionCode: request.RevisionCode.Trim().ToUpperInvariant(),
            EffectiveFrom: request.EffectiveFrom,
            EffectiveTo: null,
            Status: "active",
            ChangeReason: request.ChangeReason?.Trim(),
            ApprovedBy: tenantContext.Current.UserId,
            ApprovedAt: DateTimeOffset.UtcNow,
            CreatedAt: DateTimeOffset.UtcNow);

        var items = new List<BomItemRecord>();
        if (request.Items != null)
        {
            foreach (var item in request.Items)
            {
                if (item.LineNumber <= 0)
                    return Result<BomRecord>.Failure("INVALID_BOM_LINE_NUMBER");
                if (item.ComponentVariantId == Guid.Empty)
                    return Result<BomRecord>.Failure("INVALID_COMPONENT_VARIANT");
                if (item.QuantityPer <= 0m)
                    return Result<BomRecord>.Failure("INVALID_QUANTITY_PER");
                if (item.ScrapPercentage < 0m || item.ScrapPercentage >= 100m)
                    return Result<BomRecord>.Failure("INVALID_SCRAP_PERCENTAGE");

                items.Add(new BomItemRecord(
                    Id: Guid.NewGuid(),
                    BomRevisionId: revisionId,
                    LineNumber: item.LineNumber,
                    ComponentVariantId: item.ComponentVariantId,
                    ComponentType: string.IsNullOrWhiteSpace(item.ComponentType) ? "raw_material" : item.ComponentType.Trim().ToLowerInvariant(),
                    QuantityPer: item.QuantityPer,
                    ScrapPercentage: item.ScrapPercentage,
                    Notes: item.Notes?.Trim(),
                    CreatedAt: DateTimeOffset.UtcNow));
            }
        }

        var created = await repository.CreateBomAsync(bomRecord, revisionRecord, items, ct);
        return Result<BomRecord>.Success(created);
    }

    public async Task<Result<BomRevisionRecord>> CreateBomRevisionAsync(
        Guid bomId,
        CreateBomRevisionRequest request,
        CancellationToken ct)
    {
        var bom = await repository.GetBomByIdAsync(bomId, ct);
        if (bom == null)
            return Result<BomRevisionRecord>.Failure("BOM_NOT_FOUND");

        if (string.IsNullOrWhiteSpace(request.RevisionCode))
            return Result<BomRevisionRecord>.Failure("INVALID_REVISION_CODE");

        var revisionId = Guid.NewGuid();
        var revisionRecord = new BomRevisionRecord(
            Id: revisionId,
            BomId: bomId,
            RevisionCode: request.RevisionCode.Trim().ToUpperInvariant(),
            EffectiveFrom: request.EffectiveFrom,
            EffectiveTo: request.EffectiveTo,
            Status: "active",
            ChangeReason: request.ChangeReason?.Trim(),
            ApprovedBy: tenantContext.Current.UserId,
            ApprovedAt: DateTimeOffset.UtcNow,
            CreatedAt: DateTimeOffset.UtcNow);

        var items = new List<BomItemRecord>();
        if (request.Items != null)
        {
            foreach (var item in request.Items)
            {
                items.Add(new BomItemRecord(
                    Id: Guid.NewGuid(),
                    BomRevisionId: revisionId,
                    LineNumber: item.LineNumber,
                    ComponentVariantId: item.ComponentVariantId,
                    ComponentType: string.IsNullOrWhiteSpace(item.ComponentType) ? "raw_material" : item.ComponentType.Trim().ToLowerInvariant(),
                    QuantityPer: item.QuantityPer,
                    ScrapPercentage: item.ScrapPercentage,
                    Notes: item.Notes?.Trim(),
                    CreatedAt: DateTimeOffset.UtcNow));
            }
        }

        var created = await repository.CreateBomRevisionAsync(revisionRecord, items, ct);
        return Result<BomRevisionRecord>.Success(created);
    }

    public async Task<Result<IReadOnlyList<ExplodedComponentLine>>> ExplodeBomAsync(
        Guid productVariantId,
        decimal orderQuantity,
        CancellationToken ct)
    {
        if (orderQuantity <= 0m)
            return Result<IReadOnlyList<ExplodedComponentLine>>.Failure("INVALID_ORDER_QUANTITY");

        var boms = await repository.ListBomsAsync(ct);
        var bomDict = new Dictionary<Guid, BomExplosionEngine.BomDefinition>();

        foreach (var b in boms)
        {
            var fullBom = await repository.GetBomByIdAsync(b.Id, ct);
            if (fullBom?.Revisions != null && fullBom.Revisions.Count > 0)
            {
                var activeRev = fullBom.Revisions.FirstOrDefault(r => r.Status == "active") ?? fullBom.Revisions[0];
                var items = activeRev.Items?.Select(i => new BomExplosionEngine.BomItemDefinition(
                    ComponentVariantId: i.ComponentVariantId,
                    ComponentType: i.ComponentType,
                    QuantityPer: i.QuantityPer,
                    ScrapPercentage: i.ScrapPercentage,
                    Notes: i.Notes)).ToList() ?? new List<BomExplosionEngine.BomItemDefinition>();

                bomDict[b.ProductVariantId] = new BomExplosionEngine.BomDefinition(
                    BomId: b.Id,
                    ProductVariantId: b.ProductVariantId,
                    BaseQuantity: b.BaseQuantity,
                    YieldPercent: b.YieldPercent,
                    Items: items);
            }
        }

        var subs = await repository.ListSubstitutionsAsync(ct);
        var subDict = subs.Where(s => s.IsActive).ToDictionary(s => s.OriginalVariantId, s => s.SubstituteVariantId);

        try
        {
            var exploded = BomExplosionEngine.Explode(productVariantId, orderQuantity, bomDict, subDict);
            return Result<IReadOnlyList<ExplodedComponentLine>>.Success(exploded);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ExplodedComponentLine>>.Failure(ex.Message);
        }
    }

    // ── Material Substitutions ─────────────────────────────────────────────────

    public Task<IReadOnlyList<MaterialSubstitutionRecord>> ListSubstitutionsAsync(CancellationToken ct) =>
        repository.ListSubstitutionsAsync(ct);

    public async Task<Result<MaterialSubstitutionRecord>> CreateSubstitutionAsync(
        CreateMaterialSubstitutionRequest request,
        CancellationToken ct)
    {
        if (request.OriginalVariantId == Guid.Empty || request.SubstituteVariantId == Guid.Empty)
            return Result<MaterialSubstitutionRecord>.Failure("INVALID_VARIANT_IDS");

        if (request.OriginalVariantId == request.SubstituteVariantId)
            return Result<MaterialSubstitutionRecord>.Failure("SAME_VARIANT_SUBSTITUTION_FORBIDDEN");

        if (request.ConversionFactor <= 0m)
            return Result<MaterialSubstitutionRecord>.Failure("INVALID_CONVERSION_FACTOR");

        var record = new MaterialSubstitutionRecord(
            Id: Guid.NewGuid(),
            OriginalVariantId: request.OriginalVariantId,
            SubstituteVariantId: request.SubstituteVariantId,
            ConversionFactor: request.ConversionFactor,
            Priority: request.Priority <= 0 ? 1 : request.Priority,
            IsActive: true,
            Notes: request.Notes?.Trim(),
            CreatedAt: DateTimeOffset.UtcNow);

        var created = await repository.CreateSubstitutionAsync(record, ct);
        return Result<MaterialSubstitutionRecord>.Success(created);
    }

    // ── Work Orders ────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<WorkOrderRecord>> ListWorkOrdersAsync(CancellationToken ct) =>
        repository.ListWorkOrdersAsync(ct);

    public Task<WorkOrderRecord?> GetWorkOrderByIdAsync(Guid id, CancellationToken ct) =>
        repository.GetWorkOrderByIdAsync(id, ct);

    public async Task<Result<WorkOrderRecord>> CreateWorkOrderAsync(
        CreateWorkOrderRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.WorkOrderNumber) || request.WorkOrderNumber.Length > 50)
            return Result<WorkOrderRecord>.Failure("INVALID_WORK_ORDER_NUMBER");

        if (request.ProductVariantId == Guid.Empty)
            return Result<WorkOrderRecord>.Failure("INVALID_PRODUCT_VARIANT");

        if (request.TargetWarehouseId == Guid.Empty || request.TargetBinId == Guid.Empty)
            return Result<WorkOrderRecord>.Failure("TARGET_WAREHOUSE_BIN_REQUIRED");

        if (request.PlannedQuantity <= 0m)
            return Result<WorkOrderRecord>.Failure("INVALID_PLANNED_QUANTITY");

        if (request.DueDate < request.StartDate)
            return Result<WorkOrderRecord>.Failure("DUE_DATE_BEFORE_START_DATE");

        // Resolve BOM
        var bomId = request.BomId;
        Guid? bomRevId = request.BomRevisionId;
        BomRecord? activeBom = null;

        if (bomId.HasValue)
        {
            activeBom = await repository.GetBomByIdAsync(bomId.Value, ct);
        }
        else
        {
            activeBom = await repository.GetActiveBomForVariantAsync(request.ProductVariantId, ct);
            if (activeBom != null)
            {
                bomId = activeBom.Id;
            }
        }

        if (activeBom?.Revisions != null && activeBom.Revisions.Count > 0)
        {
            var rev = activeBom.Revisions.FirstOrDefault(r => r.Status == "active") ?? activeBom.Revisions[0];
            bomRevId = rev.Id;
        }

        // Explode BOM items to generate WorkOrderMaterialRecords
        var materials = new List<WorkOrderMaterialRecord>();
        var operations = new List<WorkOrderOperationRecord>();

        var workOrderId = Guid.NewGuid();

        if (activeBom != null)
        {
            var explosionResult = await ExplodeBomAsync(request.ProductVariantId, request.PlannedQuantity, ct);
            if (explosionResult.IsSuccess && explosionResult.Value != null)
            {
                var line = 1;
                foreach (var exp in explosionResult.Value)
                {
                    materials.Add(new WorkOrderMaterialRecord(
                        Id: Guid.NewGuid(),
                        WorkOrderId: workOrderId,
                        LineNumber: line++,
                        ComponentVariantId: exp.ComponentVariantId,
                        RequiredQuantity: exp.GrossRequiredQuantity,
                        ReservedQuantity: 0m,
                        IssuedQuantity: 0m,
                        ReturnedQuantity: 0m,
                        ReservationId: null,
                        ConsumedCost: 0m,
                        Status: "pending",
                        CreatedAt: DateTimeOffset.UtcNow));
                }
            }
        }

        // Resolve Routing operations
        if (request.RoutingId.HasValue)
        {
            var routing = await repository.GetRoutingByIdAsync(request.RoutingId.Value, ct);
            if (routing?.Operations != null)
            {
                foreach (var op in routing.Operations)
                {
                    operations.Add(new WorkOrderOperationRecord(
                        Id: Guid.NewGuid(),
                        WorkOrderId: workOrderId,
                        OperationSequence: op.OperationSequence,
                        OperationName: op.OperationName,
                        WorkCenterId: op.WorkCenterId,
                        SetupTimeMinutes: op.SetupTimeMinutes,
                        RunTimeMinutesPerUnit: op.RunTimeMinutesPerUnit,
                        ActualSetupMinutes: 0m,
                        ActualRunMinutes: 0m,
                        CompletedQuantity: 0m,
                        ScrappedQuantity: 0m,
                        AllocatedLaborCost: 0m,
                        AllocatedOverheadCost: 0m,
                        Status: "pending",
                        CompletedAt: null,
                        Notes: op.Description));
                }
            }
        }

        var wo = new WorkOrderRecord(
            Id: workOrderId,
            WorkOrderNumber: request.WorkOrderNumber.Trim().ToUpperInvariant(),
            ProductVariantId: request.ProductVariantId,
            BomId: bomId,
            BomRevisionId: bomRevId,
            RoutingId: request.RoutingId,
            TargetWarehouseId: request.TargetWarehouseId,
            TargetBinId: request.TargetBinId,
            PlannedQuantity: request.PlannedQuantity,
            CompletedQuantity: 0m,
            ScrappedQuantity: 0m,
            RejectedQuantity: 0m,
            StartDate: request.StartDate,
            DueDate: request.DueDate,
            Status: "planned",
            WipGlAccountId: null,
            TotalMaterialCost: 0m,
            TotalLaborCost: 0m,
            TotalOverheadCost: 0m,
            TotalProductionCost: 0m,
            UnitCostFinal: 0m,
            OriginDemandType: request.OriginDemandType?.Trim(),
            OriginDemandId: request.OriginDemandId,
            Notes: request.Notes?.Trim(),
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            ReleasedAt: null,
            CompletedAt: null,
            CancelledAt: null);

        var created = await repository.CreateWorkOrderAsync(wo, materials, operations, ct);
        return Result<WorkOrderRecord>.Success(created);
    }

    public async Task<Result<WorkOrderRecord>> ReleaseWorkOrderAsync(
        Guid workOrderId,
        CancellationToken ct)
    {
        var wo = await repository.GetWorkOrderByIdAsync(workOrderId, ct);
        if (wo == null)
            return Result<WorkOrderRecord>.Failure("WORK_ORDER_NOT_FOUND");

        if (wo.Status != "planned" && wo.Status != "draft")
            return Result<WorkOrderRecord>.Failure($"INVALID_STATUS_FOR_RELEASE: Current status is {wo.Status}");

        var released = await repository.ReleaseWorkOrderAsync(workOrderId, ct);
        return Result<WorkOrderRecord>.Success(released);
    }

    public async Task<Result<MaterialIssueResult>> IssueMaterialAsync(
        Guid workOrderId,
        IssueMaterialRequest request,
        CancellationToken ct)
    {
        var wo = await repository.GetWorkOrderByIdAsync(workOrderId, ct);
        if (wo == null)
            return Result<MaterialIssueResult>.Failure("WORK_ORDER_NOT_FOUND");

        if (wo.Status != "released" && wo.Status != "in_progress" && wo.Status != "partially_completed")
            return Result<MaterialIssueResult>.Failure($"INVALID_STATUS_FOR_ISSUE: Work order status is {wo.Status}");

        if (request.Quantity <= 0m)
            return Result<MaterialIssueResult>.Failure("INVALID_ISSUE_QUANTITY");

        if (request.WarehouseId == Guid.Empty || request.BinId == Guid.Empty)
            return Result<MaterialIssueResult>.Failure("WAREHOUSE_AND_BIN_REQUIRED");

        try
        {
            var result = await repository.IssueMaterialAsync(workOrderId, request, ct);
            return Result<MaterialIssueResult>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<MaterialIssueResult>.Failure(ex.Message);
        }
    }

    public async Task<Result<MaterialReturnResult>> ReturnMaterialAsync(
        Guid workOrderId,
        ReturnMaterialRequest request,
        CancellationToken ct)
    {
        var wo = await repository.GetWorkOrderByIdAsync(workOrderId, ct);
        if (wo == null)
            return Result<MaterialReturnResult>.Failure("WORK_ORDER_NOT_FOUND");

        if (request.Quantity <= 0m)
            return Result<MaterialReturnResult>.Failure("INVALID_RETURN_QUANTITY");

        try
        {
            var result = await repository.ReturnMaterialAsync(workOrderId, request, ct);
            return Result<MaterialReturnResult>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<MaterialReturnResult>.Failure(ex.Message);
        }
    }

    public async Task<Result<OperationProgressResult>> ReportOperationProgressAsync(
        Guid workOrderId,
        ReportOperationProgressRequest request,
        CancellationToken ct)
    {
        var wo = await repository.GetWorkOrderByIdAsync(workOrderId, ct);
        if (wo == null)
            return Result<OperationProgressResult>.Failure("WORK_ORDER_NOT_FOUND");

        if (wo.Status != "released" && wo.Status != "in_progress" && wo.Status != "partially_completed")
            return Result<OperationProgressResult>.Failure($"INVALID_STATUS_FOR_PROGRESS: Work order status is {wo.Status}");

        try
        {
            var result = await repository.ReportOperationProgressAsync(workOrderId, request, ct);
            return Result<OperationProgressResult>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<OperationProgressResult>.Failure(ex.Message);
        }
    }

    public async Task<Result<ProductionInspectionRecord>> RecordProductionInspectionAsync(
        Guid workOrderId,
        CreateProductionInspectionRequest request,
        CancellationToken ct)
    {
        var wo = await repository.GetWorkOrderByIdAsync(workOrderId, ct);
        if (wo == null)
            return Result<ProductionInspectionRecord>.Failure("WORK_ORDER_NOT_FOUND");

        if (string.IsNullOrWhiteSpace(request.InspectionNumber))
            return Result<ProductionInspectionRecord>.Failure("INVALID_INSPECTION_NUMBER");

        if (request.InspectedQuantity <= 0m)
            return Result<ProductionInspectionRecord>.Failure("INVALID_INSPECTED_QUANTITY");

        if (request.AcceptedQuantity + request.RejectedQuantity != request.InspectedQuantity)
            return Result<ProductionInspectionRecord>.Failure("QUANTITY_MISMATCH: Accepted + Rejected must equal Inspected quantity.");

        var inspection = new ProductionInspectionRecord(
            Id: Guid.NewGuid(),
            WorkOrderId: workOrderId,
            InspectionNumber: request.InspectionNumber.Trim().ToUpperInvariant(),
            InspectionType: request.InspectionType.Trim().ToLowerInvariant(),
            InspectedQuantity: request.InspectedQuantity,
            AcceptedQuantity: request.AcceptedQuantity,
            RejectedQuantity: request.RejectedQuantity,
            QuarantineBinId: request.QuarantineBinId,
            Disposition: request.Disposition.Trim().ToLowerInvariant(),
            InspectorUserId: tenantContext.Current.UserId,
            InspectionDate: DateTimeOffset.UtcNow,
            Notes: request.Notes?.Trim(),
            CreatedAt: DateTimeOffset.UtcNow);

        var created = await repository.RecordProductionInspectionAsync(inspection, ct);
        return Result<ProductionInspectionRecord>.Success(created);
    }

    public async Task<Result<WorkOrderCompletionResult>> CompleteWorkOrderAsync(
        Guid workOrderId,
        CompleteWorkOrderRequest request,
        CancellationToken ct)
    {
        var wo = await repository.GetWorkOrderByIdAsync(workOrderId, ct);
        if (wo == null)
            return Result<WorkOrderCompletionResult>.Failure("WORK_ORDER_NOT_FOUND");

        if (wo.Status != "released" && wo.Status != "in_progress" && wo.Status != "partially_completed")
            return Result<WorkOrderCompletionResult>.Failure($"INVALID_STATUS_FOR_COMPLETION: Work order status is {wo.Status}");

        if (request.AcceptedQuantity < 0m || request.ScrappedQuantity < 0m)
            return Result<WorkOrderCompletionResult>.Failure("NEGATIVE_QUANTITY_FORBIDDEN");

        var totalFinishing = request.AcceptedQuantity + request.ScrappedQuantity;
        if (totalFinishing <= 0m)
            return Result<WorkOrderCompletionResult>.Failure("ZERO_COMPLETION_QUANTITY");

        // Overproduction protection: Total completed cannot exceed planned quantity by more than 10% without override
        var totalResultingCompleted = wo.CompletedQuantity + request.AcceptedQuantity;
        if (totalResultingCompleted > wo.PlannedQuantity * 1.10m)
            return Result<WorkOrderCompletionResult>.Failure("OVERPRODUCTION_LIMIT_EXCEEDED: Cannot exceed planned quantity by more than 10%.");

        try
        {
            var result = await repository.CompleteWorkOrderAsync(
                workOrderId,
                request.AcceptedQuantity,
                request.ScrappedQuantity,
                request.Notes,
                ct);
            return Result<WorkOrderCompletionResult>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<WorkOrderCompletionResult>.Failure(ex.Message);
        }
    }

    // ── Traceability & Reporting ───────────────────────────────────────────────

    public Task<GenealogyRecord?> GetGenealogyAsync(Guid workOrderId, CancellationToken ct) =>
        repository.GetGenealogyAsync(workOrderId, ct);

    public Task<WipVarianceSummaryReport> GetWipVarianceReportAsync(CancellationToken ct) =>
        repository.GetWipVarianceReportAsync(ct);
}
