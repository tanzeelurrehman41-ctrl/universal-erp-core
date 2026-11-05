using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Inventory;

namespace UniversalErp.Api.Development;

public sealed record ProductSyncOperation(
    Guid OperationId,
    long DeviceSequence,
    string? OperationType,
    CreateProductRequest? Payload);

public sealed record ProductSyncBatchRequest(
    Guid BatchId,
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
