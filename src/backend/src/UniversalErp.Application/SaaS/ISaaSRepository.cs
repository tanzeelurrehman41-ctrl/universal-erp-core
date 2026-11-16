namespace UniversalErp.Application.SaaS;

public interface ISaaSRepository
{
    // Plans & Pricing
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionPlanDto?> GetPlanByCodeAsync(string planCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlanPricingDto>> ListPricingAsync(CancellationToken cancellationToken);

    // Tenant Provisioning & Lifecycle
    Task<TenantProvisioningResult> ProvisionTenantAsync(
        Guid idempotencyKey,
        TenantProvisioningRequest request,
        CancellationToken cancellationToken);

    Task<TenantDetailDto?> GetTenantDetailAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<TenantDetailDto?> GetTenantByCodeAsync(string tenantCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<TenantDetailDto>> ListTenantsAsync(string? status, int limit, int offset, CancellationToken cancellationToken);
    Task<bool> UpdateTenantLifecycleAsync(Guid tenantId, string newStatus, string? reason, CancellationToken cancellationToken);

    // Entitlements & Metering
    Task<IReadOnlyList<TenantEntitlementDto>> GetTenantEntitlementsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<TenantEntitlementDto?> GetEntitlementByFeatureAsync(Guid tenantId, string featureCode, CancellationToken cancellationToken);
    Task RecordUsageAsync(RecordUsageRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<UsageAggregationDto>> GetUsageAggregationsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<UsageAggregationDto> AggregateUsageMetricAsync(
        Guid tenantId,
        string metricCode,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken);

    // Subscriptions
    Task<SubscriptionDetailDto?> GetSubscriptionByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<UpgradeSubscriptionResult> UpgradeSubscriptionAsync(
        Guid tenantId,
        string targetPlanCode,
        string? billingInterval,
        decimal prorationCredit,
        decimal newCharge,
        decimal netPayable,
        CancellationToken cancellationToken);
    Task<bool> UpdateSubscriptionLifecycleAsync(
        Guid tenantId,
        string status,
        DateTimeOffset? graceEndsAt,
        CancellationToken cancellationToken);

    // Invoices & Billing
    Task<SubscriptionInvoiceDto> CreateInvoiceAsync(SubscriptionInvoiceDto invoice, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionInvoiceDto>> GetInvoicesByTenantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<SubscriptionInvoiceDto?> GetInvoiceByIdAsync(Guid invoiceId, CancellationToken cancellationToken);
    Task<SubscriptionPaymentDto> RecordPaymentAsync(SubscriptionPaymentDto payment, CancellationToken cancellationToken);
    Task<SubscriptionCreditNoteDto> IssueCreditNoteAsync(SubscriptionCreditNoteDto creditNote, CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingTaxRuleDto>> ListTaxRulesAsync(string? countryCode, string? provinceState, CancellationToken cancellationToken);

    // Support Access & System Alerts
    Task<SupportAccessGrantDto> GrantSupportAccessAsync(SupportAccessGrantRequest request, Guid tenantId, CancellationToken cancellationToken);
    Task<bool> RevokeSupportAccessAsync(Guid grantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SupportAccessGrantDto>> ListSupportAccessGrantsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task CreatePlatformAlertAsync(SaaSPlatformAlertDto alert, CancellationToken cancellationToken);
    Task<IReadOnlyList<SaaSPlatformAlertDto>> ListPlatformAlertsAsync(Guid? tenantId, bool unacknowledgedOnly, CancellationToken cancellationToken);
}
