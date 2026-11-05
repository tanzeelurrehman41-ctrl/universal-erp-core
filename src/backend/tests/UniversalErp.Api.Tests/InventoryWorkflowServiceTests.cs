using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class InventoryWorkflowServiceTests
{
    private sealed class FakeTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current => context;
    }

    private sealed class FakeInventoryRepository : IInventoryWorkflowRepository
    {
        public Task<IReadOnlyList<UnitOfMeasureSummary>> ListUnitsAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UnitOfMeasureSummary>>([]);

        public Task<Result<UnitOfMeasureSummary>> CreateUnitAsync(Guid tenantId, CreateUnitOfMeasureRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<UnitOfMeasureSummary>.Success(new UnitOfMeasureSummary(Guid.NewGuid(), request.Code!, request.Name!, request.Dimension!, request.DecimalPlaces)));

        public Task<IReadOnlyList<ProductVariantSummary>> ListVariantsAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductVariantSummary>>([]);

        public Task<Result<ProductVariantSummary>> CreateVariantAsync(Guid tenantId, CreateProductVariantRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ProductVariantSummary>.Success(new ProductVariantSummary(Guid.NewGuid(), request.ProductId, "PROD-1", "Prod 1", request.Sku!, request.VariantName, request.BaseUnitOfMeasureId, "PCS", request.TrackingMode, request.CostingMethod, true)));

        public Task<IReadOnlyList<WarehouseSummary>> ListWarehousesAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WarehouseSummary>>([]);

        public Task<Result<WarehouseSummary>> CreateWarehouseAsync(Guid tenantId, Guid companyId, CreateWarehouseRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<WarehouseSummary>.Success(new WarehouseSummary(Guid.NewGuid(), request.Code!, request.Name!, true)));

        public Task<IReadOnlyList<StockBinSummary>> ListBinsAsync(Guid tenantId, Guid companyId, Guid? warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StockBinSummary>>([]);

        public Task<Result<StockBinSummary>> CreateBinAsync(Guid tenantId, Guid companyId, Guid warehouseId, CreateStockBinRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<StockBinSummary>.Success(new StockBinSummary(Guid.NewGuid(), warehouseId, request.Code!, request.BinType!, true)));

        public Task<IReadOnlyList<InventoryAvailabilitySummary>> ListAvailabilityAsync(Guid tenantId, Guid companyId, Guid? variantId, Guid? warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InventoryAvailabilitySummary>>([]);

        public Task<Result<InventoryReceiptSummary>> PostReceiptAsync(TenantContext context, Guid transactionId, string requestHash, PostInventoryReceiptRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<InventoryReceiptSummary>.Success(new InventoryReceiptSummary(transactionId, Guid.NewGuid(), request.VariantId, request.WarehouseId, request.BinId, request.Quantity, request.UnitOfMeasureId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));

        public Task<Result<InventoryTransferSummary>> PostTransferAsync(TenantContext context, Guid transactionId, string requestHash, PostInventoryTransferRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<InventoryTransferSummary>.Success(new InventoryTransferSummary(transactionId, Guid.NewGuid(), request.VariantId, request.SourceWarehouseId, request.SourceBinId, request.DestinationWarehouseId, request.DestinationBinId, request.Quantity, request.UnitOfMeasureId, DateTimeOffset.UtcNow)));

        public Task<Result<InventoryAdjustmentSummary>> PostAdjustmentAsync(TenantContext context, Guid transactionId, string requestHash, PostInventoryAdjustmentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<InventoryAdjustmentSummary>.Success(new InventoryAdjustmentSummary(transactionId, Guid.NewGuid(), request.VariantId, request.WarehouseId, request.BinId, request.QuantityDelta, request.UnitOfMeasureId, request.ReasonCode, DateTimeOffset.UtcNow)));
    }

    private static readonly TenantContext TestContext = new(
        TenantId: Guid.Parse("00000000-0000-4000-8000-000000000001"),
        CompanyId: Guid.Parse("00000000-0000-4000-8000-000000000002"),
        UserId: Guid.Parse("00000000-0000-4000-8000-000000000003"));

    [Fact]
    public async Task PostTransfer_Rejects_Missing_Idempotency_Key()
    {
        var service = new InventoryWorkflowService(new FakeTenantContextAccessor(TestContext), new FakeInventoryRepository());
        var request = new PostInventoryTransferRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            10m, Guid.NewGuid(), "Inter-warehouse transfer");

        var result = await service.PostTransferAsync(null, request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_IDEMPOTENCY_KEY", result.Error);
    }

    [Fact]
    public async Task PostTransfer_Rejects_Identical_Source_And_Destination_Bin()
    {
        var service = new InventoryWorkflowService(new FakeTenantContextAccessor(TestContext), new FakeInventoryRepository());
        var sameBinId = Guid.NewGuid();
        var request = new PostInventoryTransferRequest(
            Guid.NewGuid(), Guid.NewGuid(), sameBinId, Guid.NewGuid(), sameBinId,
            10m, Guid.NewGuid(), "Transfer to same bin");

        var result = await service.PostTransferAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SOURCE_AND_DESTINATION_BIN_MUST_DIFFER", result.Error);
    }

    [Fact]
    public async Task PostTransfer_Rejects_Zero_Or_Negative_Quantity()
    {
        var service = new InventoryWorkflowService(new FakeTenantContextAccessor(TestContext), new FakeInventoryRepository());
        var request = new PostInventoryTransferRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            -5m, Guid.NewGuid(), "Negative transfer");

        var result = await service.PostTransferAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_TRANSFER", result.Error);
    }

    [Fact]
    public async Task PostTransfer_Succeeds_With_Valid_Parameters()
    {
        var service = new InventoryWorkflowService(new FakeTenantContextAccessor(TestContext), new FakeInventoryRepository());
        var request = new PostInventoryTransferRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            15m, Guid.NewGuid(), "Valid transfer between bins");

        var result = await service.PostTransferAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(15m, result.Value!.Quantity);
    }

    [Fact]
    public async Task PostAdjustment_Rejects_Zero_Quantity_Delta()
    {
        var service = new InventoryWorkflowService(new FakeTenantContextAccessor(TestContext), new FakeInventoryRepository());
        var request = new PostInventoryAdjustmentRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            0m, Guid.NewGuid(), "cycle_count", "Zero adjustment");

        var result = await service.PostAdjustmentAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_ADJUSTMENT", result.Error);
    }

    [Fact]
    public async Task PostAdjustment_Rejects_Missing_Reason_Code()
    {
        var service = new InventoryWorkflowService(new FakeTenantContextAccessor(TestContext), new FakeInventoryRepository());
        var request = new PostInventoryAdjustmentRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            -2m, Guid.NewGuid(), "", "Missing reason");

        var result = await service.PostAdjustmentAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_ADJUSTMENT", result.Error);
    }

    [Fact]
    public async Task PostAdjustment_Succeeds_For_Valid_Positive_Or_Negative_Delta()
    {
        var service = new InventoryWorkflowService(new FakeTenantContextAccessor(TestContext), new FakeInventoryRepository());
        var request = new PostInventoryAdjustmentRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            -3.5m, Guid.NewGuid(), "damaged_goods", "Damaged in transit");

        var result = await service.PostAdjustmentAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(-3.5m, result.Value!.QuantityDelta);
        Assert.Equal("damaged_goods", result.Value!.ReasonCode);
    }
}
