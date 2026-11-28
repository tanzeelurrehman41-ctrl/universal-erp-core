using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Inventory;

namespace UniversalErp.Application.Sync;

public sealed record ProductSyncOperation(
    Guid OperationId,
    long DeviceSequence,
    string? OperationType,
    DateTimeOffset CreatedAt,
    CreateProductRequest? Payload);

public sealed record ProductSyncBatchRequest(
    Guid BatchId,
    Guid DeviceId,
    Guid GrantId,
    string GrantToken,
    IReadOnlyList<ProductSyncOperation>? Operations);

public sealed record ProductSyncOperationResult(
    Guid OperationId,
    long DeviceSequence,
    string Status,
    Product? Product,
    string? Code);

public sealed record ProductSyncBatchResponse(
    Guid BatchId,
    IReadOnlyList<ProductSyncOperationResult> Results);

public sealed class ProductSyncService(ProductCatalogService catalog)
{
    public const int MaximumBatchSize = 50;

    public static string? ValidateBatchHeader(ProductSyncBatchRequest batch)
    {
        if (batch.BatchId == Guid.Empty ||
            batch.DeviceId == Guid.Empty ||
            batch.GrantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(batch.GrantToken) ||
            batch.GrantToken.Length > 256 ||
            batch.Operations is null ||
            batch.Operations.Count is < 1 or > MaximumBatchSize)
        {
            return "INVALID_PRODUCT_SYNC_BATCH";
        }

        if (batch.Operations.Any(operation => operation is null) ||
            batch.Operations.Select(operation => operation.OperationId).Distinct().Count() != batch.Operations.Count ||
            batch.Operations.Select(operation => operation.DeviceSequence).Distinct().Count() != batch.Operations.Count)
        {
            return "DUPLICATE_OR_MISSING_PRODUCT_SYNC_IDENTITY";
        }

        return null;
    }

    public async Task<ProductSyncBatchResponse> ProcessBatchAsync(
        ProductSyncBatchRequest batch,
        CancellationToken cancellationToken = default)
    {
        var headerError = ValidateBatchHeader(batch);
        if (headerError is not null)
        {
            throw new ArgumentException(headerError, nameof(batch));
        }

        var operations = batch.Operations!;
        var results = new List<ProductSyncOperationResult>(operations.Count);
        foreach (var operation in operations)
        {
            if (operation.OperationId == Guid.Empty ||
                operation.DeviceSequence <= 0 ||
                !string.Equals(operation.OperationType, "product.create", StringComparison.Ordinal) ||
                operation.Payload is null)
            {
                results.Add(new ProductSyncOperationResult(
                    operation.OperationId,
                    operation.DeviceSequence,
                    "rejected",
                    null,
                    "INVALID_PRODUCT_SYNC_OPERATION"));
                continue;
            }

            var result = await catalog.CreateAsync(
                operation.Payload,
                operation.OperationId,
                cancellationToken);

            if (result.IsSuccess)
            {
                results.Add(new ProductSyncOperationResult(
                    operation.OperationId,
                    operation.DeviceSequence,
                    "accepted",
                    result.Value,
                    null));
                continue;
            }

            var isConflict = result.Error is "DUPLICATE_PRODUCT_CODE" or "IDEMPOTENCY_KEY_REUSED";
            results.Add(new ProductSyncOperationResult(
                operation.OperationId,
                operation.DeviceSequence,
                isConflict ? "conflict" : "rejected",
                null,
                result.Error));
        }

        return new ProductSyncBatchResponse(batch.BatchId, results);
    }
}
