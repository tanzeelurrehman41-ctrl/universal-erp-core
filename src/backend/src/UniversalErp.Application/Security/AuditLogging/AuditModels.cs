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
