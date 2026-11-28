using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Inventory;
using UniversalErp.Application.Sync;
using UniversalErp.Domain.Inventory;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class ProductSyncServiceTests
{
    private sealed class FakeTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current => context;
    }

    private sealed class FakeProductRepository : IProductRepository
    {
        private readonly Dictionary<Guid, (string RequestHash, Product Product)> _idempotency = [];
        private readonly Dictionary<string, Product> _products = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<Product>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Product>>(_products.Values.Where(product => product.TenantId == tenantId).ToArray());

        public Task<Product?> GetByIdAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult(_products.Values.FirstOrDefault(product =>
                product.TenantId == tenantId && product.ProductId == productId));

        public Task<Product> CreateAsync(
            Guid tenantId,
            Guid companyId,
            Guid idempotencyKey,
            string requestHash,
            CreateProductRequest request,
            CancellationToken cancellationToken)
        {
            if (_idempotency.TryGetValue(idempotencyKey, out var existing))
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new IdempotencyKeyConflictException();
                }

                return Task.FromResult(existing.Product);
            }

            if (_products.ContainsKey(request.ProductCode!))
            {
                throw new ProductCodeConflictException();
            }

            var now = DateTimeOffset.UtcNow;
            var product = new Product(
                tenantId,
                Guid.NewGuid(),
                request.ProductCode!,
                request.Name!,
                request.Description,
                request.ProductType!,
                true,
                now,
                now);
            _products.Add(product.ProductCode, product);
            _idempotency.Add(idempotencyKey, (requestHash, product));
            return Task.FromResult(product);
        }
    }

    private static readonly TenantContext Context = new(
        Guid.Parse("00000000-0000-4000-8000-000000000001"),
        Guid.Parse("00000000-0000-4000-8000-000000000002"),
        Guid.Parse("00000000-0000-4000-8000-000000000003"));

    private static (ProductSyncService Service, FakeProductRepository Repository) CreateService()
    {
        var repository = new FakeProductRepository();
        var catalog = new ProductCatalogService(new FakeTenantContextAccessor(Context), repository);
        return (new ProductSyncService(catalog), repository);
    }

    [Fact]
    public async Task ProcessBatch_AcceptsProductAndReturnsCreatedRecord()
    {
        var (service, _) = CreateService();
        var operationId = Guid.NewGuid();
        var batch = new ProductSyncBatchRequest(
            Guid.NewGuid(),
            [new ProductSyncOperation(operationId, 1, "product.create",
                new CreateProductRequest("SKU-001", "Product One", "Test item", "stock"))]);

        var result = await service.ProcessBatchAsync(batch);

        var operation = Assert.Single(result.Results);
        Assert.Equal(batch.BatchId, result.BatchId);
        Assert.Equal(operationId, operation.OperationId);
        Assert.Equal("accepted", operation.Status);
        Assert.Equal("SKU-001", operation.Product?.ProductCode);
        Assert.Null(operation.Code);
    }

    [Fact]
    public async Task ProcessBatch_ReplayReturnsTheSamePersistedProduct()
    {
        var (service, _) = CreateService();
        var operationId = Guid.NewGuid();
        var operation = new ProductSyncOperation(
            operationId, 1, "product.create",
            new CreateProductRequest("SKU-002", "Product Two", null, "stock"));

        var first = await service.ProcessBatchAsync(new ProductSyncBatchRequest(Guid.NewGuid(), [operation]));
        var retry = await service.ProcessBatchAsync(new ProductSyncBatchRequest(Guid.NewGuid(), [operation]));

        Assert.Equal("accepted", Assert.Single(first.Results).Status);
        var replay = Assert.Single(retry.Results);
        Assert.Equal("accepted", replay.Status);
        Assert.Equal(first.Results[0].Product?.ProductId, replay.Product?.ProductId);
    }

    [Fact]
    public async Task ProcessBatch_ReusedOperationIdWithChangedPayloadReturnsConflict()
    {
        var (service, _) = CreateService();
        var operationId = Guid.NewGuid();
        var first = new ProductSyncOperation(
            operationId, 1, "product.create",
            new CreateProductRequest("SKU-003", "Original", null, "stock"));
        var changed = first with
        {
            Payload = new CreateProductRequest("SKU-003", "Changed", null, "stock")
        };

        await service.ProcessBatchAsync(new ProductSyncBatchRequest(Guid.NewGuid(), [first]));
        var result = await service.ProcessBatchAsync(new ProductSyncBatchRequest(Guid.NewGuid(), [changed]));

        var conflict = Assert.Single(result.Results);
        Assert.Equal("conflict", conflict.Status);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", conflict.Code);
    }

    [Fact]
    public async Task ProcessBatch_DuplicateProductCodeReturnsConflictWithoutOverwritingOriginal()
    {
        var (service, repository) = CreateService();
        var first = new ProductSyncOperation(
            Guid.NewGuid(), 1, "product.create",
            new CreateProductRequest("SKU-004", "Original", null, "stock"));
        var second = new ProductSyncOperation(
            Guid.NewGuid(), 2, "product.create",
            new CreateProductRequest("SKU-004", "Second", null, "stock"));

        await service.ProcessBatchAsync(new ProductSyncBatchRequest(Guid.NewGuid(), [first]));
        var result = await service.ProcessBatchAsync(new ProductSyncBatchRequest(Guid.NewGuid(), [second]));

        var conflict = Assert.Single(result.Results);
        Assert.Equal("conflict", conflict.Status);
        Assert.Equal("DUPLICATE_PRODUCT_CODE", conflict.Code);
        Assert.Single(await repository.ListAsync(Context.TenantId, CancellationToken.None));
    }

    [Fact]
    public async Task ProcessBatch_InvalidOperationIsRejectedWithoutStoppingOtherOperations()
    {
        var (service, _) = CreateService();
        var batch = new ProductSyncBatchRequest(
            Guid.NewGuid(),
            [
                new ProductSyncOperation(Guid.NewGuid(), 1, "unknown.command",
                    new CreateProductRequest("SKU-005", "Invalid", null, "stock")),
                new ProductSyncOperation(Guid.NewGuid(), 2, "product.create",
                    new CreateProductRequest("SKU-006", "Valid", null, "stock"))
            ]);

        var result = await service.ProcessBatchAsync(batch);

        Assert.Equal(2, result.Results.Count);
        Assert.Equal("rejected", result.Results[0].Status);
        Assert.Equal("INVALID_PRODUCT_SYNC_OPERATION", result.Results[0].Code);
        Assert.Equal("accepted", result.Results[1].Status);
    }

    [Fact]
    public void ValidateBatchHeader_RejectsDuplicateOperationIdsAndDeviceSequences()
    {
        var operationId = Guid.NewGuid();
        var operations = new[]
        {
            new ProductSyncOperation(operationId, 1, "product.create",
                new CreateProductRequest("SKU-007", "One", null, "stock")),
            new ProductSyncOperation(operationId, 2, "product.create",
                new CreateProductRequest("SKU-008", "Two", null, "stock"))
        };
        var duplicateIds = new ProductSyncBatchRequest(Guid.NewGuid(), operations);
        var duplicateSequences = new ProductSyncBatchRequest(Guid.NewGuid(),
        [
            operations[0],
            operations[1] with { OperationId = Guid.NewGuid(), DeviceSequence = 1 }
        ]);

        Assert.Equal("DUPLICATE_OR_MISSING_PRODUCT_SYNC_IDENTITY",
            ProductSyncService.ValidateBatchHeader(duplicateIds));
        Assert.Equal("DUPLICATE_OR_MISSING_PRODUCT_SYNC_IDENTITY",
            ProductSyncService.ValidateBatchHeader(duplicateSequences));
    }

    [Fact]
    public void ValidateBatchHeader_RejectsEmptyOrOversizedBatch()
    {
        Assert.Equal("INVALID_PRODUCT_SYNC_BATCH",
            ProductSyncService.ValidateBatchHeader(new ProductSyncBatchRequest(Guid.Empty, [])));

        var oversized = Enumerable.Range(1, ProductSyncService.MaximumBatchSize + 1)
            .Select(index => new ProductSyncOperation(
                Guid.NewGuid(),
                index,
                "product.create",
                new CreateProductRequest($"SKU-{index:000}", $"Product {index}", null, "stock")))
            .ToArray();

        Assert.Equal("INVALID_PRODUCT_SYNC_BATCH",
            ProductSyncService.ValidateBatchHeader(new ProductSyncBatchRequest(Guid.NewGuid(), oversized)));
    }
}
