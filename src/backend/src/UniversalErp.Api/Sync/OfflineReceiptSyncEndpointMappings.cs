using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Inventory;
using UniversalErp.Application.Sync;

namespace UniversalErp.Api.Sync;

public static class OfflineReceiptSyncEndpointMappings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapOfflineReceiptSyncEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool isDevelopment)
    {
        var sync = endpoints.MapGroup("/api/v1/sync");
        var registerDevice = sync.MapPost("/devices", async (
            RegisterOfflineDeviceRequest request,
            ITenantContextAccessor tenantContextAccessor,
            IOfflineReceiptSyncRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (request.ClientDeviceId == Guid.Empty || request.PlatformCode != "web" ||
                request.DeviceName?.Length > 160)
            {
                return Results.BadRequest(new { code = "INVALID_DEVICE_REGISTRATION" });
            }

            var grant = await repository.RegisterDeviceAsync(
                tenantContextAccessor.Current,
                request.ClientDeviceId,
                request.PlatformCode,
                string.IsNullOrWhiteSpace(request.DeviceName) ? null : request.DeviceName.Trim(),
                cancellationToken);
            return grant is null
                ? Results.Conflict(new { code = "DEVICE_REVOKED_OR_INACTIVE" })
                : Results.Ok(grant);
        });

        var postOperations = sync.MapPost("/receipts:batch", async (
            OfflineReceiptBatchRequest batch,
            ITenantContextAccessor tenantContextAccessor,
            IOfflineReceiptSyncRepository repository,
            InventoryWorkflowService inventory,
            CancellationToken cancellationToken) =>
        {
            if (batch.BatchId == Guid.Empty || batch.DeviceId == Guid.Empty || batch.GrantId == Guid.Empty ||
                string.IsNullOrWhiteSpace(batch.GrantToken) || batch.GrantToken.Length > 256 ||
                batch.Operations is null || batch.Operations.Count is < 1 or > 50)
            {
                return Results.BadRequest(new { code = "INVALID_OFFLINE_SYNC_BATCH" });
            }

            var tenantContext = tenantContextAccessor.Current;
            var results = new List<OfflineReceiptOperationResult>(batch.Operations.Count);
            foreach (var operation in batch.Operations)
            {
                if (operation.OperationId == Guid.Empty || operation.DeviceSequence <= 0 ||
                    operation.OperationType != "inventory.receipt.post" || operation.Payload is null ||
                    operation.CreatedAt == default)
                {
                    results.Add(new OfflineReceiptOperationResult(
                        operation.OperationId,
                        operation.DeviceSequence,
                        "rejected",
                        null,
                        "INVALID_OFFLINE_OPERATION"));
                    continue;
                }

                var payloadHash = ComputePayloadHash(operation);
                var result = await repository.ProcessReceiptOperationAtomicAsync(
                    tenantContext,
                    batch,
                    operation,
                    payloadHash,
                    cancellationToken);
                if (result.Status == "unauthorized")
                {
                    return Results.Json(new { code = "OFFLINE_DEVICE_OR_GRANT_INVALID" }, statusCode: StatusCodes.Status403Forbidden);
                }
                results.Add(result);
            }

            return Results.Ok(new OfflineReceiptBatchResponse(batch.BatchId, results));
        });

        if (!isDevelopment)
        {
            registerDevice.RequireAuthorization("inventory.receipt.post");
            postOperations.RequireAuthorization("inventory.receipt.post");
        }
        return endpoints;
    }

    private static string ComputePayloadHash(OfflineReceiptOperation operation)
    {
        var request = operation.Payload;
        var canonicalPayload = JsonSerializer.Serialize(new
        {
            command = "inventory.receipt.post",
            request.VariantId,
            request.WarehouseId,
            request.BinId,
            request.Quantity,
            request.UnitOfMeasureId,
            SourceDocumentType = string.IsNullOrWhiteSpace(request.SourceDocumentType)
                ? null
                : request.SourceDocumentType.Trim().ToLowerInvariant(),
            request.SourceDocumentId,
            OccurredAt = (request.OccurredAt ?? operation.CreatedAt).ToUniversalTime(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
    }
}

public sealed record RegisterOfflineDeviceRequest(Guid ClientDeviceId, string PlatformCode, string? DeviceName);
