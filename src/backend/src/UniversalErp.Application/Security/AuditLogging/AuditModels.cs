using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UniversalErp.Application.Security.AuditLogging;

public sealed record SecurityAuditEvent(
    Guid EventId,
    Guid TenantId,
    Guid CompanyId,
    Guid? BranchId,
    Guid ActorId,
    string ActorUsername,
    string Action, // "CREATE", "UPDATE", "DELETE", "APPROVE", "PAYROLL_CALCULATE", "TAX_DETERMINE", "JOURNAL_POST", "SUPPORT_ACCESS"
    string EntityType,
    string EntityId,
    DateTimeOffset OccurredAt,
    string? ClientIpAddress,
    string? BeforeStateJson,
    string? AfterStateJson,
    string PrevRecordHash,
    string RecordHash);

public sealed record AuditChainVerificationResult(
    bool IsChainValid,
    int TotalEventsChecked,
    int TamperedIndex,
    string? TamperedEventId,
    string? FailureReason);

public sealed class AuditLogChainService
{
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public static string ComputeRecordHash(
        string prevHash,
        Guid eventId,
        Guid tenantId,
        Guid companyId,
        Guid? branchId,
        Guid actorId,
        string action,
        string entityType,
        string entityId,
        DateTimeOffset occurredAt,
        string? beforeStateJson,
        string? afterStateJson)
    {
        var payload = $"{prevHash}|{eventId}|{tenantId}|{companyId}|{branchId}|{actorId}|{action}|{entityType}|{entityId}|{occurredAt:O}|{beforeStateJson ?? ""}|{afterStateJson ?? ""}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static SecurityAuditEvent CreateEvent(
        string prevHash,
        Guid tenantId,
        Guid companyId,
        Guid? branchId,
        Guid actorId,
        string actorUsername,
        string action,
        string entityType,
        string entityId,
        string? clientIpAddress,
        object? beforeState,
        object? afterState,
        DateTimeOffset? timestamp = null)
    {
        var eventId = Guid.NewGuid();
        var occurredAt = timestamp ?? DateTimeOffset.UtcNow;
        var beforeJson = beforeState is not null ? JsonSerializer.Serialize(beforeState) : null;
        var afterJson = afterState is not null ? JsonSerializer.Serialize(afterState) : null;

        var recordHash = ComputeRecordHash(
            prevHash,
            eventId,
            tenantId,
            companyId,
            branchId,
            actorId,
            action,
            entityType,
            entityId,
            occurredAt,
            beforeJson,
            afterJson);

        return new SecurityAuditEvent(
            EventId: eventId,
            TenantId: tenantId,
            CompanyId: companyId,
            BranchId: branchId,
            ActorId: actorId,
            ActorUsername: actorUsername,
            Action: action,
            EntityType: entityType,
            EntityId: entityId,
            OccurredAt: occurredAt,
            ClientIpAddress: clientIpAddress,
            BeforeStateJson: beforeJson,
            AfterStateJson: afterJson,
            PrevRecordHash: prevHash,
            RecordHash: recordHash);
    }

    public static AuditChainVerificationResult VerifyChain(IReadOnlyList<SecurityAuditEvent> events)
    {
        if (events == null || events.Count == 0)
        {
            return new(IsChainValid: true, TotalEventsChecked: 0, TamperedIndex: -1, TamperedEventId: null, FailureReason: null);
        }

        var expectedPrevHash = GenesisHash;

        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];

            // 1. Check link to previous hash
            if (!string.Equals(ev.PrevRecordHash, expectedPrevHash, StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    IsChainValid: false,
                    TotalEventsChecked: i + 1,
                    TamperedIndex: i,
                    TamperedEventId: ev.EventId.ToString(),
                    FailureReason: $"Chain broken at index {i}: expected PrevRecordHash '{expectedPrevHash}' but found '{ev.PrevRecordHash}'");
            }

            // 2. Re-compute hash and verify immutability
            var computed = ComputeRecordHash(
                ev.PrevRecordHash,
                ev.EventId,
                ev.TenantId,
                ev.CompanyId,
                ev.BranchId,
                ev.ActorId,
                ev.Action,
                ev.EntityType,
                ev.EntityId,
                ev.OccurredAt,
                ev.BeforeStateJson,
                ev.AfterStateJson);

            if (!string.Equals(ev.RecordHash, computed, StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    IsChainValid: false,
                    TotalEventsChecked: i + 1,
                    TamperedIndex: i,
                    TamperedEventId: ev.EventId.ToString(),
                    FailureReason: $"Tamper detected at event '{ev.EventId}' (index {i}): payload does not match cryptographic hash!");
            }

            expectedPrevHash = ev.RecordHash;
        }

        return new(IsChainValid: true, TotalEventsChecked: events.Count, TamperedIndex: -1, TamperedEventId: null, FailureReason: null);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 16: External Immutable Audit Checkpoints (S3 Object Lock / WORM / RFC 3161)
// ─────────────────────────────────────────────────────────────────────────────
public sealed record AuditChainCheckpointRecord(
    Guid CheckpointId,
    long CheckpointSequence,
    Guid FromEventId,
    Guid ToEventId,
    int EventCount,
    string HeadRecordHash,
    string MerkleRootHash,
    string ExternalStorageProvider, // "s3_object_lock", "cloudflare_r2_worm", "rfc3161_tsa", "azure_immutable_blob"
    string ExternalAnchorUri,
    string ExternalReceiptSignature,
    DateTimeOffset AnchoredAt,
    string Status = "ANCHORED");

public sealed class AuditChainCheckpointService
{
    public static string ComputeMerkleRoot(IReadOnlyList<string> hashes)
    {
        if (hashes == null || hashes.Count == 0)
            return "0000000000000000000000000000000000000000000000000000000000000000";

        var currentLevel = new List<string>(hashes);

        while (currentLevel.Count > 1)
        {
            var nextLevel = new List<string>();
            for (int i = 0; i < currentLevel.Count; i += 2)
            {
                var left = currentLevel[i];
                var right = (i + 1 < currentLevel.Count) ? currentLevel[i + 1] : left; // duplicate if odd count
                var combined = $"{left}:{right}";
                var combinedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combined))).ToLowerInvariant();
                nextLevel.Add(combinedHash);
            }
            currentLevel = nextLevel;
        }

        return currentLevel[0];
    }

    public static AuditChainCheckpointRecord CreateCheckpoint(
        IReadOnlyList<SecurityAuditEvent> batch,
        long sequence,
        string storageProvider,
        string anchorUri,
        string signingKey)
    {
        if (batch == null || batch.Count == 0)
            throw new ArgumentException("Cannot create checkpoint from empty event batch.", nameof(batch));

        // 1. Verify internal batch chain integrity first
        var verification = AuditLogChainService.VerifyChain(batch);
        if (!verification.IsChainValid)
            throw new InvalidOperationException($"Cannot anchor corrupted audit chain: {verification.FailureReason}");

        var fromId = batch[0].EventId;
        var toId = batch[^1].EventId;
        var headHash = batch[^1].RecordHash;
        var hashes = batch.Select(b => b.RecordHash).ToList();
        var merkleRoot = ComputeMerkleRoot(hashes);

        // 2. Sign checkpoint manifest
        var manifest = $"{sequence}|{fromId}|{toId}|{headHash}|{merkleRoot}|{storageProvider}|{anchorUri}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest));
        var signature = Convert.ToHexString(sigBytes).ToLowerInvariant();

        return new AuditChainCheckpointRecord(
            CheckpointId: Guid.NewGuid(),
            CheckpointSequence: sequence,
            FromEventId: fromId,
            ToEventId: toId,
            EventCount: batch.Count,
            HeadRecordHash: headHash,
            MerkleRootHash: merkleRoot,
            ExternalStorageProvider: storageProvider,
            ExternalAnchorUri: anchorUri,
            ExternalReceiptSignature: signature,
            AnchoredAt: DateTimeOffset.UtcNow,
            Status: "ANCHORED");
    }

    public static bool VerifyCheckpoint(
        AuditChainCheckpointRecord checkpoint,
        IReadOnlyList<SecurityAuditEvent> batch,
        string signingKey)
    {
        if (batch == null || batch.Count != checkpoint.EventCount) return false;
        if (batch[0].EventId != checkpoint.FromEventId) return false;
        if (batch[^1].EventId != checkpoint.ToEventId) return false;
        if (batch[^1].RecordHash != checkpoint.HeadRecordHash) return false;

        var merkle = ComputeMerkleRoot(batch.Select(b => b.RecordHash).ToList());
        if (!string.Equals(merkle, checkpoint.MerkleRootHash, StringComparison.OrdinalIgnoreCase))
            return false;

        var manifest = $"{checkpoint.CheckpointSequence}|{checkpoint.FromEventId}|{checkpoint.ToEventId}|{checkpoint.HeadRecordHash}|{checkpoint.MerkleRootHash}|{checkpoint.ExternalStorageProvider}|{checkpoint.ExternalAnchorUri}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var expectedSig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();

        return string.Equals(expectedSig, checkpoint.ExternalReceiptSignature, StringComparison.OrdinalIgnoreCase);
    }
}
