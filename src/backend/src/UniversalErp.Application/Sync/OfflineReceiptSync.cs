using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Application.Sync;

public sealed record OfflineDeviceGrant(
    Guid DeviceId,
    Guid GrantId,
    string GrantToken,
    DateTimeOffset ExpiresAt);

public sealed record OfflineReceiptOperation(
    Guid OperationId,
    long DeviceSequence,
    string OperationType,
    DateTimeOffset CreatedAt,
    PostInventoryReceiptRequest Payload);

public sealed record OfflineReceiptOperationResult(
    Guid OperationId,
    long DeviceSequence,
    string Status,
    InventoryReceiptSummary? Receipt,
    string? Code);

public sealed record OfflineReceiptBatchRequest(
    Guid BatchId,
    Guid DeviceId,
    Guid GrantId,
    string GrantToken,
    IReadOnlyList<OfflineReceiptOperation> Operations);

public sealed record OfflineReceiptBatchResponse(
    Guid BatchId,
    IReadOnlyList<OfflineReceiptOperationResult> Results);

public sealed record OfflineInboxReservation(
    string Disposition,
    OfflineReceiptOperationResult? ExistingResult = null,
    Guid? ClaimToken = null);

public interface IOfflineReceiptSyncRepository
{
    Task<OfflineDeviceGrant?> RegisterDeviceAsync(
        TenantContext context,
        Guid clientDeviceId,
        string platformCode,
        string? deviceName,
        CancellationToken cancellationToken);

    Task<OfflineInboxReservation> ReserveReceiptOperationAsync(
        TenantContext context,
        OfflineReceiptBatchRequest batch,
        OfflineReceiptOperation operation,
        string payloadHash,
        CancellationToken cancellationToken);

    Task CompleteReceiptOperationAsync(
        TenantContext context,
        Guid operationId,
        OfflineReceiptOperationResult result,
        CancellationToken cancellationToken);

    Task<OfflineReceiptOperationResult> ProcessReceiptOperationAtomicAsync(
        TenantContext context,
        OfflineReceiptBatchRequest batch,
        OfflineReceiptOperation operation,
        string payloadHash,
        CancellationToken cancellationToken);
}
