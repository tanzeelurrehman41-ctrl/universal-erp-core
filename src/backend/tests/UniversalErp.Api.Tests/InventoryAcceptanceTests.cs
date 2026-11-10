using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class InventoryAcceptanceTests
{
    private sealed class InMemoryInventoryWorkflowRepository : IInventoryWorkflowRepository
    {
        private readonly Dictionary<string, decimal> _balances = new();
        private readonly Dictionary<Guid, (string Code, string Hash, object Result)> _idempotency = new();
        private readonly List<InventoryMovementSummary> _movements = new();

        public decimal GetBalance(Guid warehouseId, Guid binId) =>
            _balances.TryGetValue($"{warehouseId}:{binId}", out var qty) ? qty : 0m;

        public Task<IReadOnlyList<UnitOfMeasureSummary>> ListUnitsAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UnitOfMeasureSummary>>([]);

        public Task<Result<UnitOfMeasureSummary>> CreateUnitAsync(Guid tenantId, CreateUnitOfMeasureRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<UnitOfMeasureSummary>.Success(new UnitOfMeasureSummary(Guid.NewGuid(), request.Code!, request.Name!, request.Dimension!, request.DecimalPlaces)));

        public Task<IReadOnlyList<ProductVariantSummary>> ListVariantsAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductVariantSummary>>([]);

        public Task<Result<ProductVariantSummary>> CreateVariantAsync(Guid tenantId, CreateProductVariantRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ProductVariantSummary>.Success(new ProductVariantSummary(Guid.NewGuid(), request.ProductId, "PROD-1", "Prod 1", request.Sku!, request.VariantName, request.BaseUnitOfMeasureId, "EA", request.TrackingMode, request.CostingMethod, true)));

        public Task<IReadOnlyList<WarehouseSummary>> ListWarehousesAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WarehouseSummary>>([]);

        public Task<Result<WarehouseSummary>> CreateWarehouseAsync(Guid tenantId, Guid companyId, CreateWarehouseRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<WarehouseSummary>.Success(new WarehouseSummary(Guid.NewGuid(), request.Code!, request.Name!, true)));

        public Task<IReadOnlyList<StockBinSummary>> ListBinsAsync(Guid tenantId, Guid companyId, Guid? warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StockBinSummary>>([]);

        public Task<Result<StockBinSummary>> CreateBinAsync(Guid tenantId, Guid companyId, Guid warehouseId, CreateStockBinRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<StockBinSummary>.Success(new StockBinSummary(Guid.NewGuid(), warehouseId, request.Code!, request.BinType!, true)));

        public Task<IReadOnlyList<InventoryAvailabilitySummary>> ListAvailabilityAsync(Guid tenantId, Guid companyId, Guid? variantId, Guid? warehouseId, CancellationToken cancellationToken)
        {
            var list = _balances.Select(kv =>
            {
                var parts = kv.Key.Split(':');
                var wId = Guid.Parse(parts[0]);
                var bId = Guid.Parse(parts[1]);
                return new InventoryAvailabilitySummary(
                    variantId ?? Guid.NewGuid(),
                    wId,
                    bId,
                    "available",
                    kv.Value,
                    Guid.NewGuid());
            }).ToList();
            return Task.FromResult<IReadOnlyList<InventoryAvailabilitySummary>>(list);
        }

        public Task<Result<InventoryReceiptSummary>> PostReceiptAsync(
            TenantContext context, Guid transactionId, string requestHash, PostInventoryReceiptRequest request, CancellationToken cancellationToken)
        {
            if (_idempotency.TryGetValue(transactionId, out var existing))
            {
                if (existing.Code != "inventory.receipt.post" || existing.Hash != requestHash)
                    return Task.FromResult(Result<InventoryReceiptSummary>.Failure("IDEMPOTENCY_CONFLICT"));
                return Task.FromResult(Result<InventoryReceiptSummary>.Success((InventoryReceiptSummary)existing.Result));
            }

            var key = $"{request.WarehouseId}:{request.BinId}";
            _balances[key] = (_balances.TryGetValue(key, out var curr) ? curr : 0m) + request.Quantity;

            var movementId = Guid.NewGuid();
            var summary = new InventoryReceiptSummary(
                transactionId, movementId, request.VariantId, request.WarehouseId, request.BinId,
                request.Quantity, request.UnitOfMeasureId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

            _idempotency[transactionId] = ("inventory.receipt.post", requestHash, summary);
            _movements.Add(new InventoryMovementSummary(movementId, "receipt", null, null, DateTimeOffset.UtcNow, null, []));
            return Task.FromResult(Result<InventoryReceiptSummary>.Success(summary));
        }

        public Task<Result<InventoryTransferSummary>> PostTransferAsync(
            TenantContext context, Guid transactionId, string requestHash, PostInventoryTransferRequest request, CancellationToken cancellationToken)
        {
            if (_idempotency.TryGetValue(transactionId, out var existing))
            {
                if (existing.Code != "inventory.transfer.post" || existing.Hash != requestHash)
                    return Task.FromResult(Result<InventoryTransferSummary>.Failure("IDEMPOTENCY_CONFLICT"));
                return Task.FromResult(Result<InventoryTransferSummary>.Success((InventoryTransferSummary)existing.Result));
            }

            var srcKey = $"{request.SourceWarehouseId}:{request.SourceBinId}";
            var currentSource = _balances.TryGetValue(srcKey, out var curr) ? curr : 0m;
            if (currentSource < request.Quantity)
            {
                return Task.FromResult(Result<InventoryTransferSummary>.Failure("INSUFFICIENT_STOCK"));
            }

            _balances[srcKey] = currentSource - request.Quantity;
            var destKey = $"{request.DestinationWarehouseId}:{request.DestinationBinId}";
            _balances[destKey] = (_balances.TryGetValue(destKey, out var dCurr) ? dCurr : 0m) + request.Quantity;

            var movementId = Guid.NewGuid();
            var summary = new InventoryTransferSummary(
                transactionId, movementId, request.VariantId, request.SourceWarehouseId, request.SourceBinId,
                request.DestinationWarehouseId, request.DestinationBinId, request.Quantity, request.UnitOfMeasureId, DateTimeOffset.UtcNow);

            _idempotency[transactionId] = ("inventory.transfer.post", requestHash, summary);
            _movements.Add(new InventoryMovementSummary(movementId, "transfer", null, null, DateTimeOffset.UtcNow, null, []));
            return Task.FromResult(Result<InventoryTransferSummary>.Success(summary));
        }

        public Task<Result<InventoryAdjustmentSummary>> PostAdjustmentAsync(
            TenantContext context, Guid transactionId, string requestHash, PostInventoryAdjustmentRequest request, CancellationToken cancellationToken)
        {
            if (_idempotency.TryGetValue(transactionId, out var existing))
            {
                if (existing.Code != "inventory.adjustment.post" || existing.Hash != requestHash)
                    return Task.FromResult(Result<InventoryAdjustmentSummary>.Failure("IDEMPOTENCY_CONFLICT"));
                return Task.FromResult(Result<InventoryAdjustmentSummary>.Success((InventoryAdjustmentSummary)existing.Result));
            }

            var key = $"{request.WarehouseId}:{request.BinId}";
            var current = _balances.TryGetValue(key, out var curr) ? curr : 0m;
            if (current + request.QuantityDelta < 0)
            {
                return Task.FromResult(Result<InventoryAdjustmentSummary>.Failure("INSUFFICIENT_STOCK"));
            }

            _balances[key] = current + request.QuantityDelta;

            var movementId = Guid.NewGuid();
            var summary = new InventoryAdjustmentSummary(
                transactionId, movementId, request.VariantId, request.WarehouseId, request.BinId,
                request.QuantityDelta, request.UnitOfMeasureId, request.ReasonCode, DateTimeOffset.UtcNow);

            _idempotency[transactionId] = ("inventory.adjustment.post", requestHash, summary);
            _movements.Add(new InventoryMovementSummary(movementId, "adjustment", null, null, DateTimeOffset.UtcNow, null, []));
            return Task.FromResult(Result<InventoryAdjustmentSummary>.Success(summary));
        }

        public Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductCategorySummary>>([]);

        public Task<Result<ProductCategorySummary>> CreateCategoryAsync(Guid tenantId, CreateProductCategoryRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ProductCategorySummary>.Success(new ProductCategorySummary(Guid.NewGuid(), request.Code!, request.Name!, request.ParentCategoryId)));

        public Task<IReadOnlyList<InventoryMovementSummary>> ListMovementsAsync(Guid tenantId, Guid companyId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InventoryMovementSummary>>(_movements);
    }

    private static readonly TenantContext DemoContext = new(
        TenantId: Guid.Parse("22222222-2222-4222-8222-222222222222"),
        CompanyId: Guid.Parse("22222222-2222-4222-8222-222222222223"),
        UserId: Guid.Parse("3719490d-8010-4e2e-9bd7-a83f783a075b"));

    private sealed class ContextAccessor(TenantContext ctx) : ITenantContextAccessor
    {
        public TenantContext Current => ctx;
    }

    [Fact]
    public async Task Complete_Inventory_Lifecycle_Acceptance_Test()
    {
        // Setup repository and service
        var repo = new InMemoryInventoryWorkflowRepository();
        var service = new InventoryWorkflowService(new ContextAccessor(DemoContext), repo);

        var variantId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var binReceive = Guid.NewGuid();
        var binStore = Guid.NewGuid();

        // Step 1: Initial Receipt of 10 EA into RECEIVE-01
        var receiptOpId = Guid.NewGuid();
        var receiptRequest = new PostInventoryReceiptRequest(
            variantId, warehouseId, binReceive, 10m, uomId, null, null, null, "Initial demo stock receipt");

        var receiptResult = await service.PostReceiptAsync(receiptOpId, receiptRequest, CancellationToken.None);
        Assert.True(receiptResult.IsSuccess);
        Assert.Equal(10m, receiptResult.Value!.Quantity);
        Assert.Equal(10m, repo.GetBalance(warehouseId, binReceive));
        Assert.Equal(0m, repo.GetBalance(warehouseId, binStore));

        // Step 2: Idempotent Receipt Replay with same Operation ID does not duplicate balance
        var receiptReplay = await service.PostReceiptAsync(receiptOpId, receiptRequest, CancellationToken.None);
        Assert.True(receiptReplay.IsSuccess);
        Assert.Equal(receiptResult.Value.MovementId, receiptReplay.Value!.MovementId);
        Assert.Equal(10m, repo.GetBalance(warehouseId, binReceive));

        // Step 3: Transfer 4 EA from RECEIVE-01 to STORE-01
        var transferOpId = Guid.NewGuid();
        var transferRequest = new PostInventoryTransferRequest(
            variantId, warehouseId, binReceive, warehouseId, binStore, 4m, uomId, "Relocation to storage");

        var transferResult = await service.PostTransferAsync(transferOpId, transferRequest, CancellationToken.None);
        Assert.True(transferResult.IsSuccess);
        Assert.Equal(4m, transferResult.Value!.Quantity);
        Assert.Equal(6m, repo.GetBalance(warehouseId, binReceive)); // 10 - 4 = 6
        Assert.Equal(4m, repo.GetBalance(warehouseId, binStore));   // 0 + 4 = 4

        // Step 4: Transfer Overdraw is Rejected (Attempting to transfer 20 EA from RECEIVE-01)
        var overTransferRequest = new PostInventoryTransferRequest(
            variantId, warehouseId, binReceive, warehouseId, binStore, 20m, uomId, "Overdraw attempt");

        var overTransferResult = await service.PostTransferAsync(Guid.NewGuid(), overTransferRequest, CancellationToken.None);
        Assert.False(overTransferResult.IsSuccess);
        Assert.Equal("INSUFFICIENT_STOCK", overTransferResult.Error);
        Assert.Equal(6m, repo.GetBalance(warehouseId, binReceive)); // unchanged
        Assert.Equal(4m, repo.GetBalance(warehouseId, binStore));   // unchanged

        // Step 5: Transfer Idempotent Replay returns existing result without moving stock again
        var transferReplay = await service.PostTransferAsync(transferOpId, transferRequest, CancellationToken.None);
        Assert.True(transferReplay.IsSuccess);
        Assert.Equal(transferResult.Value.MovementId, transferReplay.Value!.MovementId);
        Assert.Equal(6m, repo.GetBalance(warehouseId, binReceive));
        Assert.Equal(4m, repo.GetBalance(warehouseId, binStore));

        // Step 6: Negative Adjustment of -1 EA on STORE-01 (e.g. damaged goods)
        var adjNegOpId = Guid.NewGuid();
        var adjNegRequest = new PostInventoryAdjustmentRequest(
            variantId, warehouseId, binStore, -1m, uomId, "damaged_goods", "Scissor damaged");

        var adjNegResult = await service.PostAdjustmentAsync(adjNegOpId, adjNegRequest, CancellationToken.None);
        Assert.True(adjNegResult.IsSuccess);
        Assert.Equal(-1m, adjNegResult.Value!.QuantityDelta);
        Assert.Equal(3m, repo.GetBalance(warehouseId, binStore));   // 4 - 1 = 3

        // Step 7: Positive Adjustment of +2 EA on STORE-01 (e.g. cycle count correction)
        var adjPosOpId = Guid.NewGuid();
        var adjPosRequest = new PostInventoryAdjustmentRequest(
            variantId, warehouseId, binStore, 2m, uomId, "cycle_count", "Found 2 extra units");

        var adjPosResult = await service.PostAdjustmentAsync(adjPosOpId, adjPosRequest, CancellationToken.None);
        Assert.True(adjPosResult.IsSuccess);
        Assert.Equal(2m, adjPosResult.Value!.QuantityDelta);
        Assert.Equal(5m, repo.GetBalance(warehouseId, binStore));   // 3 + 2 = 5

        // Step 8: Adjustment Over-Negative Rejection (-10 EA on STORE-01 where only 5 EA exists)
        var adjOverNeg = new PostInventoryAdjustmentRequest(
            variantId, warehouseId, binStore, -10m, uomId, "theft_or_loss", "Unrealistic loss");

        var adjOverResult = await service.PostAdjustmentAsync(Guid.NewGuid(), adjOverNeg, CancellationToken.None);
        Assert.False(adjOverResult.IsSuccess);
        Assert.Equal("INSUFFICIENT_STOCK", adjOverResult.Error);
        Assert.Equal(5m, repo.GetBalance(warehouseId, binStore));   // unchanged

        // Step 9: Final Total Inventory Availability Verification
        var availability = await service.ListAvailabilityAsync(variantId, warehouseId, CancellationToken.None);
        Assert.Equal(2, availability.Count);
        var receiveBal = availability.First(a => a.BinId == binReceive);
        var storeBal = availability.First(a => a.BinId == binStore);

        Assert.Equal(6m, receiveBal.OnHandQuantity);
        Assert.Equal(5m, storeBal.OnHandQuantity);
        Assert.Equal(11m, receiveBal.OnHandQuantity + storeBal.OnHandQuantity);
    }

    [Fact]
    public async Task Offline_Retry_And_Replay_Preserves_Idempotency_And_Balance_Integrity()
    {
        // Simulates an offline field device syncing operations through retries and potential duplicate deliveries
        var repo = new InMemoryInventoryWorkflowRepository();
        var service = new InventoryWorkflowService(new ContextAccessor(DemoContext), repo);

        var variantId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var binReceive = Guid.NewGuid();
        var binStore = Guid.NewGuid();

        // 1. Queued offline receipt (10 EA)
        var offlineReceiptOpId = Guid.NewGuid();
        var receiptReq = new PostInventoryReceiptRequest(
            variantId, warehouseId, binReceive, 10m, uomId, null, null, null, "Offline synced receipt");

        var receiptAttempt1 = await service.PostReceiptAsync(offlineReceiptOpId, receiptReq, CancellationToken.None);
        Assert.True(receiptAttempt1.IsSuccess);
        Assert.Equal(10m, repo.GetBalance(warehouseId, binReceive));

        // Network drops before client receives ACK; client retries identical batch item
        var receiptRetryAttempt2 = await service.PostReceiptAsync(offlineReceiptOpId, receiptReq, CancellationToken.None);
        Assert.True(receiptRetryAttempt2.IsSuccess);
        Assert.Equal(receiptAttempt1.Value!.MovementId, receiptRetryAttempt2.Value!.MovementId);
        Assert.Equal(10m, repo.GetBalance(warehouseId, binReceive)); // Invariant: No duplicate balance created

        // Fraudulent / corrupted payload retry with identical operation ID is blocked
        var conflictingReceiptReq = new PostInventoryReceiptRequest(
            variantId, warehouseId, binReceive, 999m, uomId, null, null, null, "Tampered payload");
        var conflictResult = await service.PostReceiptAsync(offlineReceiptOpId, conflictingReceiptReq, CancellationToken.None);
        Assert.False(conflictResult.IsSuccess);
        Assert.Equal("IDEMPOTENCY_CONFLICT", conflictResult.Error);
        Assert.Equal(10m, repo.GetBalance(warehouseId, binReceive)); // Invariant: Tampered payload ignored

        // 2. Queued offline transfer (4 EA from RECEIVE to STORE)
        var offlineTransferOpId = Guid.NewGuid();
        var transferReq = new PostInventoryTransferRequest(
            variantId, warehouseId, binReceive, warehouseId, binStore, 4m, uomId, "Offline bin transfer");

        var transferAttempt1 = await service.PostTransferAsync(offlineTransferOpId, transferReq, CancellationToken.None);
        Assert.True(transferAttempt1.IsSuccess);
        Assert.Equal(6m, repo.GetBalance(warehouseId, binReceive));
        Assert.Equal(4m, repo.GetBalance(warehouseId, binStore));

        // Network drops before transfer ACK; client retries transfer
        var transferRetryAttempt2 = await service.PostTransferAsync(offlineTransferOpId, transferReq, CancellationToken.None);
        Assert.True(transferRetryAttempt2.IsSuccess);
        Assert.Equal(transferAttempt1.Value!.MovementId, transferRetryAttempt2.Value!.MovementId);
        Assert.Equal(6m, repo.GetBalance(warehouseId, binReceive));
        Assert.Equal(4m, repo.GetBalance(warehouseId, binStore));

        // 3. Queued offline adjustment (-1 EA on STORE)
        var offlineAdjOpId = Guid.NewGuid();
        var adjReq = new PostInventoryAdjustmentRequest(
            variantId, warehouseId, binStore, -1m, uomId, "damaged_goods", "Damaged packaging in storage");

        var adjAttempt1 = await service.PostAdjustmentAsync(offlineAdjOpId, adjReq, CancellationToken.None);
        Assert.True(adjAttempt1.IsSuccess);
        Assert.Equal(3m, repo.GetBalance(warehouseId, binStore));

        // Client retries adjustment on reconnection
        var adjRetryAttempt2 = await service.PostAdjustmentAsync(offlineAdjOpId, adjReq, CancellationToken.None);
        Assert.True(adjRetryAttempt2.IsSuccess);
        Assert.Equal(adjAttempt1.Value!.MovementId, adjRetryAttempt2.Value!.MovementId);
        Assert.Equal(3m, repo.GetBalance(warehouseId, binStore));

        // Verify total movements: exactly 3 movements recorded (receipt, transfer, adjustment)
        var movements = await service.ListMovementsAsync(100, CancellationToken.None);
        Assert.Equal(3, movements.Count);
    }
}
