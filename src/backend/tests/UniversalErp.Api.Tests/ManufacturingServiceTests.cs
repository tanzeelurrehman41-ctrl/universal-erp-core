using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Manufacturing;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class ManufacturingServiceTests
{
    private sealed class DummyTenantContextAccessor(TenantContext ctx) : ITenantContextAccessor
    {
        public TenantContext Current => ctx;
    }

    private sealed class InMemoryManufacturingRepo : IManufacturingRepository
    {
        public readonly List<WorkCenterRecord> WorkCenters = new();
        public readonly List<RoutingRecord> Routings = new();
        public readonly List<BomRecord> Boms = new();
        public readonly List<MaterialSubstitutionRecord> Substitutions = new();
        public readonly List<WorkOrderRecord> WorkOrders = new();

        public Task<WorkCenterRecord> CreateWorkCenterAsync(WorkCenterRecord record, CancellationToken ct)
        {
            WorkCenters.Add(record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<WorkCenterRecord>> ListWorkCentersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorkCenterRecord>>(WorkCenters);

        public Task<WorkCenterRecord?> GetWorkCenterByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(WorkCenters.FirstOrDefault(w => w.Id == id));

        public Task<RoutingRecord> CreateRoutingAsync(RoutingRecord record, IReadOnlyList<RoutingOperationRecord> operations, CancellationToken ct)
        {
            var saved = record with { Operations = operations };
            Routings.Add(saved);
            return Task.FromResult(saved);
        }

        public Task<IReadOnlyList<RoutingRecord>> ListRoutingsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RoutingRecord>>(Routings);

        public Task<RoutingRecord?> GetRoutingByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Routings.FirstOrDefault(r => r.Id == id));

        public Task<BomRecord> CreateBomAsync(BomRecord record, BomRevisionRecord revision, IReadOnlyList<BomItemRecord> items, CancellationToken ct)
        {
            var rev = revision with { Items = items };
            var saved = record with { Revisions = new[] { rev } };
            Boms.Add(saved);
            return Task.FromResult(saved);
        }

        public Task<BomRevisionRecord> CreateBomRevisionAsync(BomRevisionRecord revision, IReadOnlyList<BomItemRecord> items, CancellationToken ct)
        {
            var rev = revision with { Items = items };
            return Task.FromResult(rev);
        }

        public Task<IReadOnlyList<BomRecord>> ListBomsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BomRecord>>(Boms);

        public Task<BomRecord?> GetBomByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Boms.FirstOrDefault(b => b.Id == id));

        public Task<BomRecord?> GetActiveBomForVariantAsync(Guid variantId, CancellationToken ct) =>
            Task.FromResult(Boms.FirstOrDefault(b => b.ProductVariantId == variantId && b.Status == "active"));

        public Task<MaterialSubstitutionRecord> CreateSubstitutionAsync(MaterialSubstitutionRecord record, CancellationToken ct)
        {
            Substitutions.Add(record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<MaterialSubstitutionRecord>> ListSubstitutionsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MaterialSubstitutionRecord>>(Substitutions);

        public Task<WorkOrderRecord> CreateWorkOrderAsync(WorkOrderRecord wo, IReadOnlyList<WorkOrderMaterialRecord> materials, IReadOnlyList<WorkOrderOperationRecord> operations, CancellationToken ct)
        {
            var saved = wo with { Materials = materials, Operations = operations };
            WorkOrders.Add(saved);
            return Task.FromResult(saved);
        }

        public Task<IReadOnlyList<WorkOrderRecord>> ListWorkOrdersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorkOrderRecord>>(WorkOrders);

        public Task<WorkOrderRecord?> GetWorkOrderByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(WorkOrders.FirstOrDefault(w => w.Id == id));

        public Task<WorkOrderRecord> ReleaseWorkOrderAsync(Guid workOrderId, CancellationToken ct)
        {
            var wo = WorkOrders.First(w => w.Id == workOrderId);
            var updated = wo with { Status = "released", ReleasedAt = DateTimeOffset.UtcNow };
            WorkOrders.Remove(wo);
            WorkOrders.Add(updated);
            return Task.FromResult(updated);
        }

        public Task<MaterialIssueResult> IssueMaterialAsync(Guid workOrderId, IssueMaterialRequest request, CancellationToken ct) =>
            Task.FromResult(new MaterialIssueResult(workOrderId, request.LineNumber, request.Quantity, request.Quantity * 100m, Guid.NewGuid(), Guid.NewGuid()));

        public Task<MaterialReturnResult> ReturnMaterialAsync(Guid workOrderId, ReturnMaterialRequest request, CancellationToken ct) =>
            Task.FromResult(new MaterialReturnResult(workOrderId, request.LineNumber, request.Quantity, request.Quantity * 100m, Guid.NewGuid(), Guid.NewGuid()));

        public Task<OperationProgressResult> ReportOperationProgressAsync(Guid workOrderId, ReportOperationProgressRequest request, CancellationToken ct) =>
            Task.FromResult(new OperationProgressResult(workOrderId, request.OperationSequence, 500m, 200m, Guid.NewGuid()));

        public Task<ProductionInspectionRecord> RecordProductionInspectionAsync(ProductionInspectionRecord inspection, CancellationToken ct) =>
            Task.FromResult(inspection);

        public Task<WorkOrderCompletionResult> CompleteWorkOrderAsync(Guid workOrderId, decimal acceptedQty, decimal scrappedQty, string? notes, CancellationToken ct) =>
            Task.FromResult(new WorkOrderCompletionResult(workOrderId, acceptedQty, scrappedQty, 150m, 1500m, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        public Task<GenealogyRecord?> GetGenealogyAsync(Guid workOrderId, CancellationToken ct) =>
            Task.FromResult<GenealogyRecord?>(null);

        public Task<WipVarianceSummaryReport> GetWipVarianceReportAsync(CancellationToken ct) =>
            Task.FromResult(new WipVarianceSummaryReport(0, 0m, 0m, 0m, 0m, Array.Empty<WorkOrderWipSummary>()));
    }

    [Fact]
    public async Task CreateWorkCenter_ValidRequest_Succeeds()
    {
        var tenantCtx = new DummyTenantContextAccessor(new TenantContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        var repo = new InMemoryManufacturingRepo();
        var service = new ManufacturingService(repo, tenantCtx);

        var req = new CreateWorkCenterRequest(
            Code: "WC-MILL-01",
            Name: "Milling Station 1",
            HourlyLaborRate: 600m,
            HourlyOverheadRate: 300m,
            DailyCapacityHours: 8m,
            EfficiencyPercent: 100m);

        var result = await service.CreateWorkCenterAsync(req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("WC-MILL-01", result.Value!.Code);
        Assert.Equal(600m, result.Value.HourlyLaborRate);
    }

    [Fact]
    public async Task CreateWorkCenter_NegativeRate_Fails()
    {
        var tenantCtx = new DummyTenantContextAccessor(new TenantContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        var repo = new InMemoryManufacturingRepo();
        var service = new ManufacturingService(repo, tenantCtx);

        var req = new CreateWorkCenterRequest(
            Code: "WC-BAD",
            Name: "Bad Station",
            HourlyLaborRate: -100m,
            HourlyOverheadRate: 300m,
            DailyCapacityHours: 8m,
            EfficiencyPercent: 100m);

        var result = await service.CreateWorkCenterAsync(req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("NEGATIVE_RATES_FORBIDDEN", result.Error);
    }

    [Fact]
    public async Task CompleteWorkOrder_OverproductionLimitExceeded_Fails()
    {
        var tenantCtx = new DummyTenantContextAccessor(new TenantContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        var repo = new InMemoryManufacturingRepo();
        var service = new ManufacturingService(repo, tenantCtx);

        var woId = Guid.NewGuid();
        var wo = new WorkOrderRecord(
            Id: woId,
            WorkOrderNumber: "WO-OVER-01",
            ProductVariantId: Guid.NewGuid(),
            BomId: null,
            BomRevisionId: null,
            RoutingId: null,
            TargetWarehouseId: Guid.NewGuid(),
            TargetBinId: Guid.NewGuid(),
            PlannedQuantity: 10m,
            CompletedQuantity: 0m,
            ScrappedQuantity: 0m,
            RejectedQuantity: 0m,
            StartDate: DateOnly.FromDateTime(DateTime.UtcNow),
            DueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            Status: "released",
            WipGlAccountId: null,
            TotalMaterialCost: 1000m,
            TotalLaborCost: 500m,
            TotalOverheadCost: 200m,
            TotalProductionCost: 1700m,
            UnitCostFinal: 0m,
            OriginDemandType: null,
            OriginDemandId: null,
            Notes: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            ReleasedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CancelledAt: null);

        repo.WorkOrders.Add(wo);

        // Attempting to complete 15 units when planned is 10 (exceeds 10% limit: 11)
        var completeReq = new CompleteWorkOrderRequest(
            AcceptedQuantity: 15m,
            ScrappedQuantity: 0m);

        var result = await service.CompleteWorkOrderAsync(woId, completeReq, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("OVERPRODUCTION_LIMIT_EXCEEDED", result.Error);
    }
}
