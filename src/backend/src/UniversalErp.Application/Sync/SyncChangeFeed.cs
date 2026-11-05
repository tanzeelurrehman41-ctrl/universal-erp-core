using System.Text.Json;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Application.Sync;

public sealed record SyncChange(
    string Cursor,
    string EntityType,
    Guid EntityId,
    long EntityVersion,
    string ChangeType,
    JsonElement Payload,
    DateTimeOffset OccurredAt);

public sealed record SyncChangePage(
    string NextCursor,
    bool HasMore,
    IReadOnlyList<SyncChange> Changes);

public interface ISyncChangeFeed
{
    Task<SyncChangePage> ReadPageAsync(
        TenantContext context,
        string entityType,
        long afterSequence,
        int pageSize,
        CancellationToken cancellationToken);
}
