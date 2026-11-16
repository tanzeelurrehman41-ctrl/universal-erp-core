using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalErp.Application.Integrations;

public class IntegrationsService
{
    private readonly IIntegrationsRepository _repository;
    private readonly WebhookSigningEngine _signingEngine;

    public IntegrationsService(IIntegrationsRepository repository, WebhookSigningEngine signingEngine)
    {
        _repository = repository;
        _signingEngine = signingEngine;
    }

    public Task<IReadOnlyList<WebhookSubscriptionDto>> GetWebhookSubscriptionsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        return _repository.GetWebhookSubscriptionsAsync(tenantId, companyId, ct);
    }

    public Task<WebhookSubscriptionDto?> GetWebhookSubscriptionByIdAsync(Guid tenantId, Guid subscriptionId, CancellationToken ct = default)
    {
        return _repository.GetWebhookSubscriptionByIdAsync(tenantId, subscriptionId, ct);
    }

    public Task<WebhookSubscriptionCreateResult> CreateWebhookSubscriptionAsync(Guid tenantId, Guid companyId, CreateWebhookSubscriptionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.EventType))
            throw new ArgumentException("EventType is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TargetUrl) || !Uri.TryCreate(request.TargetUrl, UriKind.Absolute, out _))
            throw new ArgumentException("Valid absolute TargetUrl is required.", nameof(request));

        return _repository.CreateWebhookSubscriptionAsync(tenantId, companyId, request, ct);
    }

    public Task<RotateWebhookSecretResult> RotateWebhookSecretAsync(Guid tenantId, Guid subscriptionId, CancellationToken ct = default)
    {
        return _repository.RotateWebhookSecretAsync(tenantId, subscriptionId, ct);
    }

    public Task<bool> DeleteWebhookSubscriptionAsync(Guid tenantId, Guid subscriptionId, CancellationToken ct = default)
    {
        return _repository.DeleteWebhookSubscriptionAsync(tenantId, subscriptionId, ct);
    }

    public Task<OutboundEventDeliveryDto> EnqueueEventDeliveryAsync(Guid tenantId, EnqueueEventRequest request, CancellationToken ct = default)
    {
        return _repository.EnqueueEventDeliveryAsync(tenantId, request, ct);
    }

    public Task<IReadOnlyList<OutboundEventDeliveryDto>> GetEventDeliveriesAsync(Guid tenantId, Guid? subscriptionId = null, string? status = null, int limit = 50, CancellationToken ct = default)
    {
        return _repository.GetEventDeliveriesAsync(tenantId, subscriptionId, status, limit, ct);
    }

    public Task<OutboundEventDeliveryDto> RecordDeliveryAttemptAsync(Guid tenantId, Guid deliveryId, int statusCode, string? error, bool succeeded, CancellationToken ct = default)
    {
        return _repository.RecordDeliveryAttemptAsync(tenantId, deliveryId, statusCode, error, succeeded, ct);
    }

    public async Task<(string SignatureHeader, string TimestampHeader)> SignOutboundDeliveryAsync(Guid tenantId, Guid subscriptionId, string payload, CancellationToken ct = default)
    {
        var secret = await _repository.GetDecryptedSecretAsync(tenantId, subscriptionId, ct);
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException($"Cannot sign outbound delivery: webhook subscription {subscriptionId} not found, inactive, or revoked.");

        return _signingEngine.ComputeHeaders(payload, secret, DateTimeOffset.UtcNow);
    }

    public Task<OutboundEventDeliveryDto> RetryDeliveryAsync(Guid tenantId, Guid deliveryId, CancellationToken ct = default)
    {
        return _repository.RetryDeliveryAsync(tenantId, deliveryId, ct);
    }

    public Task<IReadOnlyList<ApiPartnerDto>> GetApiPartnersAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        return _repository.GetApiPartnersAsync(tenantId, companyId, ct);
    }

    public Task<CreateApiPartnerResult> CreateApiPartnerAsync(Guid tenantId, Guid companyId, CreateApiPartnerRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.PartnerName))
            throw new ArgumentException("PartnerName is required.", nameof(request));

        return _repository.CreateApiPartnerAsync(tenantId, companyId, request, ct);
    }

    public Task<bool> RevokeApiPartnerAsync(Guid tenantId, Guid partnerId, CancellationToken ct = default)
    {
        return _repository.RevokeApiPartnerAsync(tenantId, partnerId, ct);
    }
}
