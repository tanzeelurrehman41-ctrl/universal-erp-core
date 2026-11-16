using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalErp.Application.Integrations;

public interface IIntegrationsRepository
{
    Task<IReadOnlyList<WebhookSubscriptionDto>> GetWebhookSubscriptionsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);
    Task<WebhookSubscriptionDto?> GetWebhookSubscriptionByIdAsync(Guid tenantId, Guid subscriptionId, CancellationToken ct = default);
    Task<WebhookSubscriptionCreateResult> CreateWebhookSubscriptionAsync(Guid tenantId, Guid companyId, CreateWebhookSubscriptionRequest request, CancellationToken ct = default);
    Task<RotateWebhookSecretResult> RotateWebhookSecretAsync(Guid tenantId, Guid subscriptionId, CancellationToken ct = default);
    Task<bool> DeleteWebhookSubscriptionAsync(Guid tenantId, Guid subscriptionId, CancellationToken ct = default);

    Task<OutboundEventDeliveryDto> EnqueueEventDeliveryAsync(Guid tenantId, EnqueueEventRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<OutboundEventDeliveryDto>> GetEventDeliveriesAsync(Guid tenantId, Guid? subscriptionId = null, string? status = null, int limit = 50, CancellationToken ct = default);
    Task<OutboundEventDeliveryDto> RecordDeliveryAttemptAsync(Guid tenantId, Guid deliveryId, int statusCode, string? error, bool succeeded, CancellationToken ct = default);
    Task<OutboundEventDeliveryDto> RetryDeliveryAsync(Guid tenantId, Guid deliveryId, CancellationToken ct = default);

    Task<IReadOnlyList<ApiPartnerDto>> GetApiPartnersAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);
    Task<CreateApiPartnerResult> CreateApiPartnerAsync(Guid tenantId, Guid companyId, CreateApiPartnerRequest request, CancellationToken ct = default);
    Task<bool> RevokeApiPartnerAsync(Guid tenantId, Guid partnerId, CancellationToken ct = default);
}
