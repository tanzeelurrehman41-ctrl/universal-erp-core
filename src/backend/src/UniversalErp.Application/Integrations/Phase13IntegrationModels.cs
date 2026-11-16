using System;
using System.Collections.Generic;

namespace UniversalErp.Application.Integrations;

public record WebhookSubscriptionDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string EventType,
    string TargetUrl,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record CreateWebhookSubscriptionRequest(
    string EventType,
    string TargetUrl,
    string? Secret = null
);

public record WebhookSubscriptionCreateResult(
    WebhookSubscriptionDto Subscription,
    string PlaintextSecret
);

public record RotateWebhookSecretResult(
    Guid SubscriptionId,
    string NewPlaintextSecret,
    string SecretHash,
    DateTimeOffset RotatedAt
);

public record OutboundEventDeliveryDto(
    Guid Id,
    Guid SubscriptionId,
    string EventType,
    string EventPayloadJson,
    string DeliveryStatus,
    int AttemptCount,
    int? LastResponseCode,
    string? LastError,
    DateTimeOffset? NextRetryAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset CreatedAt
);

public record EnqueueEventRequest(
    Guid SubscriptionId,
    string EventType,
    string EventPayloadJson
);

public record ApiPartnerDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string PartnerName,
    IReadOnlyList<string> Permissions,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record CreateApiPartnerRequest(
    string PartnerName,
    IReadOnlyList<string> Permissions
);

public record CreateApiPartnerResult(
    ApiPartnerDto Partner,
    string PlaintextApiKey
);
