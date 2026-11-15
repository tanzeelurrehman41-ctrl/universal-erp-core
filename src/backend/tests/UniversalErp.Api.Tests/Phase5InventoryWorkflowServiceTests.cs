using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class Phase5InventoryWorkflowServiceTests
{
    private sealed class TestTenantContextAccessor : ITenantContextAccessor
    {
        public TenantContext Current { get; } = new(
            TenantId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CompanyId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            UserId: Guid.Parse("33333333-3333-3333-3333-333333333333"));
    }

    private sealed class Phase5MockInventoryRepository : IInventoryWorkflowRepository
    {
        public Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductCategorySummary>>([]);
        public Task<Result<ProductCategorySummary>> CreateCategoryAsync(Guid tenantId, CreateProductCategoryRequest request, CancellationToken cancellationToken) => Task.FromResult(Result<ProductCategorySummary>.Failure("stub"));
        public Task<IReadOnlyList<InventoryMovementSummary>> ListMovementsAsync(Guid tenantId, Guid companyId, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InventoryMovementSummary>>([]);
        public Task<IReadOnlyList<UnitOfMeasureSummary>> ListUnitsAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UnitOfMeasureSummary>>([]);
        public Task<Result<UnitOfMeasureSummary>> CreateUnitAsync(Guid tenantId, CreateUnitOfMeasureRequest request, CancellationToken cancellationToken) => Task.FromResult(Result<UnitOfMeasureSummary>.Failure("stub"));
        public Task<IReadOnlyList<ProductVariantSummary>> ListVariantsAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductVariantSummary>>([]);
        public Task<Result<ProductVariantSummary>> CreateVariantAsync(Guid tenantId, CreateProductVariantRequest request, CancellationToken cancellationToken) => Task.FromResult(Result<ProductVariantSummary>.Failure("stub"));
        public Task<IReadOnlyList<WarehouseSummary>> ListWarehousesAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WarehouseSummary>>([]);
        public Task<Result<WarehouseSummary>> CreateWarehouseAsync(Guid tenantId, Guid companyId, CreateWarehouseRequest request, CancellationToken cancellationToken) => Task.FromResult(Result<WarehouseSummary>.Failure("stub"));
        public Task<IReadOnlyList<StockBinSummary>> ListBinsAsync(Guid tenantId, Guid companyId, Guid? warehouseId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StockBinSummary>>([]);
        public Task<Result<StockBinSummary>> CreateBinAsync(Guid tenantId, Guid companyId, Guid warehouseId, CreateStockBinRequest request, CancellationToken cancellationToken) => Task.FromResult(Result<StockBinSummary>.Failure("stub"));
        public Task<IReadOnlyList<InventoryAvailabilitySummary>> ListAvailabilityAsync(Guid tenantId, Guid companyId, Guid? variantId, Guid? warehouseId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InventoryAvailabilitySummary>>([]);
        public Task<Result<InventoryReceiptSummary>> PostReceiptAsync(TenantContext context, Guid transactionId, string requestHash, PostInventoryReceiptRequest request, CancellationToken cancellationToken) => Task.FromResult(Result<InventoryReceiptSummary>.Failure("stub"));
        public Task<Result<InventoryTransferSummary>> PostTransferAsync(TenantContext context, Guid transactionId, string requestHash, PostInventoryTransferRequest request, CancellationToken cancellationToken) => Task.FromResult(Result<InventoryTransferSummary>.Failure("stub"));
        public Task<Result<InventoryAdjustmentSummary>> PostAdjustmentAsync(TenantContext context, Guid transactionId, string requestHash, PostInventoryAdjustmentRequest request, CancellationToken cancellationToken) => Task.FromResult(Result<InventoryAdjustmentSummary>.Failure("stub"));

        public Task<IReadOnlyList<StockReservationSummary>> ListReservationsAsync(
            Guid tenantId, Guid companyId, Guid? variantId, Guid? warehouseId, string? status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StockReservationSummary>>([
                new StockReservationSummary(
                    ReservationId: Guid.NewGuid(),
                    ClientTransactionId: Guid.NewGuid(),
                    VariantId: variantId ?? Guid.NewGuid(),
                    Sku: "ITEM-A",
                    WarehouseId: warehouseId ?? Guid.NewGuid(),
                    WarehouseCode: "MAIN-W1",
                    BinId: Guid.NewGuid(),
                    BinCode: "PICK-01",
                    Quantity: 10m,
                    FulfilledQty: 0m,
                    RemainingReservedQty: 10m,
                    ReservationStatus: status ?? "active",
                    SourceDocumentType: "sales_order",
                    SourceDocumentId: null,
                    ExpiresAt: null,
                    CreatedAt: DateTimeOffset.UtcNow,
                    ReleasedAt: null,
                    Note: null)
            ]);

        public Task<Result<StockReservationSummary>> CreateReservationAsync(
            TenantContext context, Guid transactionId, string requestHash, CreateStockReservationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<StockReservationSummary>.Success(new StockReservationSummary(
                ReservationId: Guid.NewGuid(),
                ClientTransactionId: transactionId,
                VariantId: request.VariantId,
                Sku: "ITEM-A",
                WarehouseId: request.WarehouseId,
                WarehouseCode: "MAIN-W1",
                BinId: request.BinId,
                BinCode: "PICK-01",
                Quantity: request.Quantity,
                FulfilledQty: 0m,
                RemainingReservedQty: request.Quantity,
                ReservationStatus: "active",
                SourceDocumentType: request.SourceDocumentType,
                SourceDocumentId: request.SourceDocumentId,
                ExpiresAt: request.ExpiresAt,
                CreatedAt: DateTimeOffset.UtcNow,
                ReleasedAt: null,
                Note: request.Note)));

        public Task<Result<StockReservationSummary>> ReleaseReservationAsync(
            TenantContext context, Guid reservationId, string? reason, CancellationToken cancellationToken) =>
            Task.FromResult(Result<StockReservationSummary>.Success(new StockReservationSummary(
                ReservationId: reservationId,
                ClientTransactionId: Guid.NewGuid(),
                VariantId: Guid.NewGuid(),
                Sku: "ITEM-A",
                WarehouseId: Guid.NewGuid(),
                WarehouseCode: "MAIN-W1",
                BinId: Guid.NewGuid(),
                BinCode: "PICK-01",
                Quantity: 10m,
                FulfilledQty: 0m,
                RemainingReservedQty: 0m,
                ReservationStatus: "released",
                SourceDocumentType: "sales_order",
                SourceDocumentId: null,
                ExpiresAt: null,
                CreatedAt: DateTimeOffset.UtcNow.AddHours(-1),
                ReleasedAt: DateTimeOffset.UtcNow,
                Note: reason)));

        public Task<IReadOnlyList<AvailableToPromiseSummary>> GetAtpAsync(
            Guid tenantId, Guid companyId, Guid? variantId, Guid? warehouseId, Guid? binId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AvailableToPromiseSummary>>([
                new AvailableToPromiseSummary(
                    VariantId: variantId ?? Guid.NewGuid(),
                    Sku: "ITEM-A",
                    WarehouseId: warehouseId ?? Guid.NewGuid(),
                    WarehouseCode: "MAIN-W1",
                    BinId: binId,
                    BinCode: "PICK-01",
                    PhysicalOnHand: 50m,
                    ActiveReservedQty: 10m,
                    AvailableToPromiseQty: 40m,
                    ValuedQty: 47m,
                    SellableAtpQty: 40m)
            ]);

        public Task<IReadOnlyList<StockPickListSummary>> ListPickListsAsync(
            Guid tenantId, Guid companyId, Guid? warehouseId, string? status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StockPickListSummary>>([]);

        public Task<Result<StockPickListSummary>> CreatePickListAsync(
            TenantContext context, Guid transactionId, string requestHash, CreatePickListRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<StockPickListSummary>.Success(new StockPickListSummary(
                PickListId: Guid.NewGuid(),
                PickListNo: "PL-20261115-001",
                WarehouseId: request.WarehouseId,
                WarehouseCode: "MAIN-W1",
                SourceDocumentType: request.SourceDocumentType,
                SourceDocumentId: request.SourceDocumentId,
                Status: "draft",
                AssignedTo: null,
                Note: request.Note,
                CreatedAt: DateTimeOffset.UtcNow,
                DispatchedAt: null,
                Lines: request.Lines.Select(l => new StockPickLineSummary(
                    PickLineId: Guid.NewGuid(),
                    PickListId: Guid.NewGuid(),
                    ReservationId: l.ReservationId,
                    VariantId: l.VariantId,
                    Sku: "ITEM-A",
                    BinId: l.BinId,
                    BinCode: "PICK-01",
                    RequestedQty: l.RequestedQty,
                    PickedQty: 0m,
                    Status: "pending",
                    Note: l.Note)).ToList())));

        public Task<Result<StockPickListSummary>> RecordPickLinesAsync(
            TenantContext context, Guid pickListId, IReadOnlyList<RecordPickLineRequest> lines, CancellationToken cancellationToken) =>
            Task.FromResult(Result<StockPickListSummary>.Success(new StockPickListSummary(
                PickListId: pickListId,
                PickListNo: "PL-20261115-001",
                WarehouseId: Guid.NewGuid(),
                WarehouseCode: "MAIN-W1",
                SourceDocumentType: "sales_order",
                SourceDocumentId: null,
                Status: "picked",
                AssignedTo: null,
                Note: null,
                CreatedAt: DateTimeOffset.UtcNow,
                DispatchedAt: null,
                Lines: lines.Select(l => new StockPickLineSummary(
                    PickLineId: l.PickLineId,
                    PickListId: pickListId,
                    ReservationId: null,
                    VariantId: Guid.NewGuid(),
                    Sku: "ITEM-A",
                    BinId: Guid.NewGuid(),
                    BinCode: "PICK-01",
                    RequestedQty: l.PickedQty,
                    PickedQty: l.PickedQty,
                    Status: "picked",
                    Note: l.Note)).ToList())));

        public Task<Result<DispatchPickListResult>> DispatchPickListAsync(
            TenantContext context, Guid pickListId, CancellationToken cancellationToken) =>
            Task.FromResult(Result<DispatchPickListResult>.Success(new DispatchPickListResult(
                PickListId: pickListId,
                PickListNo: "PL-20261115-001",
                MovementId: Guid.NewGuid(),
                TotalDispatchedQty: 5m,
                TotalFifoCost: 225m,
                CostAllocations: [new FifoLayerAllocation(Guid.NewGuid(), 5m, 45m, 225m)],
                DispatchedAt: DateTimeOffset.UtcNow)));

        public Task<Result<StockCountSessionSummary>> CreateCountSessionAsync(
            TenantContext context, Guid transactionId, string requestHash, CreateCountSessionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<StockCountSessionSummary>.Success(new StockCountSessionSummary(
                SessionId: Guid.NewGuid(),
                CountNumber: "CNT-20261115-001",
                WarehouseId: request.WarehouseId,
                WarehouseCode: "MAIN-W1",
                CountType: request.CountType,
                Status: "draft",
                InitiatedBy: context.UserId,
                ApprovedBy: null,
                CreatedAt: DateTimeOffset.UtcNow,
                CompletedAt: null,
                ApprovedAt: null,
                Note: request.Note,
                Lines: [])));

        public Task<Result<StockCountSessionSummary>> RecordCountLinesAsync(
            TenantContext context, Guid sessionId, IReadOnlyList<RecordCountLineRequest> lines, CancellationToken cancellationToken) =>
            Task.FromResult(Result<StockCountSessionSummary>.Success(new StockCountSessionSummary(
                SessionId: sessionId,
                CountNumber: "CNT-20261115-001",
                WarehouseId: Guid.NewGuid(),
                WarehouseCode: "MAIN-W1",
                CountType: "cycle",
                Status: "completed",
                InitiatedBy: context.UserId,
                ApprovedBy: null,
                CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-30),
                CompletedAt: DateTimeOffset.UtcNow,
                ApprovedAt: null,
                Note: null,
                Lines: [])));

        public Task<Result<ApproveCountSessionResult>> ApproveCountSessionAsync(
            TenantContext context, Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ApproveCountSessionResult>.Success(new ApproveCountSessionResult(
                SessionId: sessionId,
                CountNumber: "CNT-20261115-001",
                TotalReconciledLines: 1,
                TotalNetVarianceQty: 2m,
                TotalNetVarianceCost: 90m,
                AdjustmentMovementId: Guid.NewGuid(),
                ApprovedAt: DateTimeOffset.UtcNow)));

        public Task<Result<StockReturnSummary>> CreateStockReturnAsync(
            TenantContext context, Guid transactionId, string requestHash, CreateStockReturnRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<StockReturnSummary>.Success(new StockReturnSummary(
                ReturnId: Guid.NewGuid(),
                ReturnNumber: "RET-20261115-001",
                ReturnType: request.ReturnType,
                SourceDocumentType: request.SourceDocumentType,
                SourceDocumentId: request.SourceDocumentId,
                WarehouseId: request.WarehouseId,
                WarehouseCode: "MAIN-W1",
                Status: "draft",
                CreatedBy: context.UserId,
                ApprovedBy: null,
                CreatedAt: DateTimeOffset.UtcNow,
                ProcessedAt: null,
                Note: request.Note,
                Lines: [])));

        public Task<Result<PostStockReturnResult>> ProcessStockReturnAsync(
            TenantContext context, Guid returnId, CancellationToken cancellationToken) =>
            Task.FromResult(Result<PostStockReturnResult>.Success(new PostStockReturnResult(
                ReturnId: returnId,
                ReturnNumber: "RET-20261115-001",
                MovementId: Guid.NewGuid(),
                TotalReturnedQty: 3m,
                TotalValuationImpact: 135m,
                ProcessedAt: DateTimeOffset.UtcNow)));

        public Task<IReadOnlyList<BinMovementHistoryItem>> GetBinMovementHistoryAsync(
            Guid tenantId, Guid companyId, Guid? warehouseId, Guid? binId, Guid? variantId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BinMovementHistoryItem>>([
                new BinMovementHistoryItem(
                    MovementId: Guid.NewGuid(),
                    LegNumber: 1,
                    VariantId: variantId ?? Guid.NewGuid(),
                    Sku: "ITEM-A",
                    WarehouseId: warehouseId ?? Guid.NewGuid(),
                    WarehouseCode: "MAIN-W1",
                    BinId: binId ?? Guid.NewGuid(),
                    BinCode: "PICK-01",
                    MovementType: "issue",
                    SourceDocumentType: "pick_list",
                    SourceDocumentId: null,
                    QuantityDelta: -5m,
                    StatusCode: "available",
                    OccurredAt: DateTimeOffset.UtcNow,
                    PostedAt: DateTimeOffset.UtcNow,
                    Note: "Pick dispatch")
            ]);

        public Task<InventoryValuationReportSummary> GetInventoryValuationReportAsync(
            Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(new InventoryValuationReportSummary(
                GeneratedAt: DateTimeOffset.UtcNow,
                TotalActiveLayers: 3,
                TotalPhysicalStock: 50m,
                TotalValuedStock: 50m,
                TotalUnvaluedStock: 0m,
                TotalValuationPkr: 2250m,
                GeneralLedger1200BalancePkr: 2250m,
                ReconciliationVariancePkr: 0m,
                IsReconciled: true,
                Layers: []));
    }

    [Fact]
    public async Task CreateReservation_WithNullIdempotencyKey_Fails()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());
        var req = new CreateStockReservationRequest(Guid.NewGuid(), Guid.NewGuid(), null, 5m, "sales_order", null, null, null);

        var result = await service.CreateReservationAsync(null, req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_IDEMPOTENCY_KEY", result.Error);
    }

    [Fact]
    public async Task CreateReservation_WithZeroOrNegativeQuantity_Fails()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());
        var req = new CreateStockReservationRequest(Guid.NewGuid(), Guid.NewGuid(), null, 0m, "sales_order", null, null, null);

        var result = await service.CreateReservationAsync(Guid.NewGuid(), req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_RESERVATION", result.Error);
    }

    [Fact]
    public async Task CreateReservation_WithValidRequest_Succeeds()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());
        var req = new CreateStockReservationRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, "sales_order", null, null, "Reserved for SO-100");

        var result = await service.CreateReservationAsync(Guid.NewGuid(), req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(10m, result.Value!.Quantity);
        Assert.Equal("active", result.Value.ReservationStatus);
    }

    [Fact]
    public async Task ReleaseReservation_WithEmptyId_Fails()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());

        var result = await service.ReleaseReservationAsync(Guid.Empty, "Customer cancelled", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_RESERVATION_ID", result.Error);
    }

    [Fact]
    public async Task ReleaseReservation_WithValidId_Succeeds()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());
        var resId = Guid.NewGuid();

        var result = await service.ReleaseReservationAsync(resId, "Customer cancelled order", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("released", result.Value!.ReservationStatus);
    }

    [Fact]
    public async Task GetAtp_ReturnsAtpCalculations()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());

        var atp = await service.GetAtpAsync(null, null, null, CancellationToken.None);

        Assert.NotEmpty(atp);
        Assert.Equal(40m, atp[0].AvailableToPromiseQty);
        Assert.Equal(40m, atp[0].SellableAtpQty);
    }

    [Fact]
    public async Task CreatePickList_WithEmptyLines_Fails()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());
        var req = new CreatePickListRequest(Guid.NewGuid(), "sales_order", null, [], null);

        var result = await service.CreatePickListAsync(Guid.NewGuid(), req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_PICK_LIST", result.Error);
    }

    [Fact]
    public async Task CreatePickList_WithValidLines_Succeeds()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());
        var req = new CreatePickListRequest(
            Guid.NewGuid(),
            "sales_order",
            null,
            [new CreatePickLineRequest(Guid.NewGuid(), Guid.NewGuid(), 5m, null, null)],
            "Urgent pick");

        var result = await service.CreatePickListAsync(Guid.NewGuid(), req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Lines);
        Assert.Equal(5m, result.Value.Lines[0].RequestedQty);
    }

    [Fact]
    public async Task DispatchPickList_WithValidId_SucceedsWithFifoCosts()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());
        var pickListId = Guid.NewGuid();

        var result = await service.DispatchPickListAsync(pickListId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5m, result.Value!.TotalDispatchedQty);
        Assert.Equal(225m, result.Value.TotalFifoCost);
        Assert.Single(result.Value.CostAllocations);
    }

    [Fact]
    public async Task CreateCountSession_WithInvalidCountType_Fails()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());
        var req = new CreateCountSessionRequest(Guid.NewGuid(), "invalid_type", null);

        var result = await service.CreateCountSessionAsync(Guid.NewGuid(), req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_COUNT_SESSION", result.Error);
    }

    [Fact]
    public async Task CreateCountSession_WithCycleCount_Succeeds()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());
        var req = new CreateCountSessionRequest(Guid.NewGuid(), "cycle", "Daily high-value cycle count");

        var result = await service.CreateCountSessionAsync(Guid.NewGuid(), req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("cycle", result.Value!.CountType);
        Assert.Equal("draft", result.Value.Status);
    }

    [Fact]
    public async Task CreateStockReturn_WithInvalidReturnType_Fails()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());
        var req = new CreateStockReturnRequest(
            "unknown_type",
            "invoice",
            null,
            Guid.NewGuid(),
            [new CreateStockReturnLineRequest(Guid.NewGuid(), Guid.NewGuid(), 2m, 45m, "available", null)],
            null);

        var result = await service.CreateStockReturnAsync(Guid.NewGuid(), req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_STOCK_RETURN", result.Error);
    }

    [Fact]
    public async Task CreateStockReturn_WithValidCustomerReturn_Succeeds()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());
        var req = new CreateStockReturnRequest(
            "customer_return",
            "sales_invoice",
            null,
            Guid.NewGuid(),
            [new CreateStockReturnLineRequest(Guid.NewGuid(), Guid.NewGuid(), 3m, 45m, "available", "Customer returned unopened goods")],
            "Return authorized");

        var result = await service.CreateStockReturnAsync(Guid.NewGuid(), req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("customer_return", result.Value!.ReturnType);
    }

    [Fact]
    public async Task GetInventoryValuationReport_ReturnsReconciledStatus()
    {
        var service = new InventoryWorkflowService(new TestTenantContextAccessor(), new Phase5MockInventoryRepository());

        var report = await service.GetInventoryValuationReportAsync(CancellationToken.None);

        Assert.True(report.IsReconciled);
        Assert.Equal(0m, report.ReconciliationVariancePkr);
        Assert.Equal(2250m, report.TotalValuationPkr);
        Assert.Equal(2250m, report.GeneralLedger1200BalancePkr);
    }
}
