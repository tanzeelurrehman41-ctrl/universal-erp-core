namespace UniversalErp.Application.SaaS;

// ── Plan & Pricing Records ──────────────────────────────────────────────────

public sealed record PlanPricingDto(
    Guid Id,
    Guid PlanId,
    string BillingInterval,
    string CurrencyCode,
    decimal BasePrice,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil,
    bool IsActive);

public sealed record PlanFeatureDto(
    string FeatureCode,
    bool Enabled,
    long? QuantityLimit,
    string? LimitUnit);

public sealed record SubscriptionPlanDto(
    Guid Id,
    string PlanCode,
    string Name,
    string? Description,
    int TierRank,
    string Status,
    bool IsPublic,
    IReadOnlyList<PlanPricingDto> Pricing,
    IReadOnlyList<PlanFeatureDto> Features);

// ── Tenant Provisioning Records ─────────────────────────────────────────────

public sealed record TenantProvisioningRequest(
    string? TenantCode,
    string? DisplayName,
    string? LegalName,
    string? PlanCode,
    string? BillingInterval,
    string? CompanyName,
    string? CompanyCode,
    string? BranchName,
    string? BranchCode,
    string? CountryCode,
    string? BaseCurrencyCode,
    string? Timezone,
    string? AdminEmail,
    string? AdminFullName,
    string? AdminPhone);

public sealed record TenantProvisioningResult(
    Guid TenantId,
    string TenantCode,
    string DisplayName,
    Guid CompanyId,
    string CompanyCode,
    Guid BranchId,
    string BranchCode,
    Guid SubscriptionId,
    string PlanCode,
    Guid AdminUserId,
    string AdminEmail,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record CompanyBriefDto(
    Guid CompanyId,
    string CompanyCode,
    string Name,
    string CountryCode,
    string BaseCurrencyCode,
    string Status);

public sealed record BranchBriefDto(
    Guid BranchId,
    string BranchCode,
    string BranchName,
    string BranchType,
    bool IsActive);

public sealed record SubscriptionDetailDto(
    Guid SubscriptionId,
    Guid TenantId,
    Guid PlanId,
    string PlanCode,
    string PlanName,
    string Status,
    string BillingInterval,
    string CurrencyCode,
    decimal CurrentPrice,
    string OveragePolicy,
    decimal OverageRate,
    DateTimeOffset StartsAt,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset CurrentPeriodEnd,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? GraceEndsAt,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CancelledAt);

public sealed record TenantEntitlementDto(
    string FeatureCode,
    bool Enabled,
    long? QuantityLimit,
    string? LimitUnit,
    string SourceType,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil);

public sealed record AdminUserBriefDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string Status);

public sealed record TenantDetailDto(
    Guid TenantId,
    string TenantCode,
    string DisplayName,
    string? LegalName,
    string Status,
    string DefaultCountryCode,
    string DefaultCurrencyCode,
    string DefaultTimezone,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SuspendedAt,
    string? SuspensionReason,
    CompanyBriefDto Company,
    BranchBriefDto DefaultBranch,
    SubscriptionDetailDto Subscription,
    IReadOnlyList<TenantEntitlementDto> Entitlements,
    AdminUserBriefDto AdminUser);

// ── Entitlement Enforcement Records ─────────────────────────────────────────

public sealed record EntitlementCheckRequest(
    Guid TenantId,
    string FeatureCode,
    long RequestedQuantity = 1);

public sealed record EntitlementCheckResult(
    Guid TenantId,
    string FeatureCode,
    bool IsAllowed,
    string SubscriptionStatus,
    string? DenialReason,
    decimal CurrentUsage,
    decimal? QuantityLimit,
    bool IsWarningThresholdExceeded,
    string? WarningMessage,
    string OveragePolicy);

// ── Usage Records & Aggregations ────────────────────────────────────────────

public sealed record RecordUsageRequest(
    Guid TenantId,
    string MetricCode,
    decimal Quantity,
    string Unit,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    Guid? SourceEventId = null);

public sealed record UsageAggregationDto(
    Guid Id,
    Guid TenantId,
    string MetricCode,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal CurrentQuantity,
    decimal? QuantityLimit,
    bool IsExceeded,
    int WarningThresholdPct,
    bool WarningIssued,
    DateTimeOffset LastAggregatedAt);

// ── Billing & Invoices Records ──────────────────────────────────────────────

public sealed record SubscriptionInvoiceLineDto(
    Guid Id,
    int LineNumber,
    string Description,
    string LineType,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount);

public sealed record SubscriptionInvoiceDto(
    Guid Id,
    Guid TenantId,
    Guid SubscriptionId,
    string InvoiceNumber,
    string BillingReason,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    string CurrencyCode,
    string Status,
    DateTimeOffset DueDate,
    DateTimeOffset? PaidAt,
    string? HostedInvoiceUrl,
    string? PdfDownloadUrl,
    IReadOnlyList<SubscriptionInvoiceLineDto> Lines);

public sealed record ProcessPaymentRequest(
    Guid TenantId,
    Guid InvoiceId,
    decimal Amount,
    string CurrencyCode,
    string PaymentMethodRef,
    string IdempotencyKey);

public sealed record SubscriptionPaymentDto(
    Guid Id,
    Guid TenantId,
    Guid? InvoiceId,
    string PaymentReference,
    string ProviderType,
    decimal Amount,
    string CurrencyCode,
    string Status,
    string? FailureReason,
    string IdempotencyKey,
    DateTimeOffset ProcessedAt);

public sealed record SubscriptionCreditNoteDto(
    Guid Id,
    Guid TenantId,
    Guid InvoiceId,
    string CreditNoteNumber,
    decimal Amount,
    string Reason,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record UpgradeSubscriptionRequest(
    string TargetPlanCode,
    string? BillingInterval);

public sealed record UpgradeSubscriptionResult(
    Guid SubscriptionId,
    string OldPlanCode,
    string NewPlanCode,
    decimal ProrationCredit,
    decimal NewCharge,
    decimal NetPayable,
    Guid? InvoiceId,
    string Status);

// ── Lifecycle & Administration Records ──────────────────────────────────────

public sealed record TenantLifecycleTransitionRequest(
    string NewStatus,
    string? Reason);

public sealed record SupportAccessGrantRequest(
    Guid SupportUserId,
    Guid AuthorizedBy,
    string Reason,
    int DurationHours);

public sealed record SupportAccessGrantDto(
    Guid Id,
    Guid TenantId,
    Guid SupportUserId,
    Guid AuthorizedBy,
    string Reason,
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    string Status);

public sealed record SaaSPlatformAlertDto(
    Guid Id,
    Guid? TenantId,
    string AlertType,
    string Severity,
    string Title,
    string Message,
    bool IsAcknowledged,
    DateTimeOffset CreatedAt);
