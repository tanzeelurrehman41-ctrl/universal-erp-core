namespace UniversalErp.Application.Tenancy;

// ── Request records ─────────────────────────────────────────────────────────

public sealed record RegisterTenantRequest(
    string? TenantCode,
    string? DisplayName,
    string? LegalName,
    string? PlanCode,
    string? CompanyName,
    string? CompanyCode,
    string? RegistrationNumber,
    string? CountryCode,
    string? BaseCurrencyCode,
    string? Timezone,
    string? AdminEmail,
    string? AdminFullName,
    string? AdminPhone);

// ── Response and domain view records ────────────────────────────────────────

public sealed record TenantDetail(
    Guid TenantId,
    string TenantCode,
    string DisplayName,
    string? LegalName,
    string Status,
    string DefaultCountryCode,
    string DefaultCurrencyCode,
    string DefaultTimezone,
    DateTimeOffset CreatedAt,
    InitialCompanyDetail Company,
    TenantSubscriptionDetail Subscription,
    IReadOnlyList<TenantEntitlementDetail> Entitlements,
    TenantAdminUserDetail AdminUser);

public sealed record InitialCompanyDetail(
    Guid CompanyId,
    string CompanyCode,
    string Name,
    string? LegalName,
    string? RegistrationNumber,
    string CountryCode,
    string BaseCurrencyCode,
    string Timezone,
    string Status);

public sealed record TenantSubscriptionDetail(
    Guid SubscriptionId,
    string PlanCode,
    string PlanName,
    string Status,
    DateTimeOffset StartsAt,
    DateTimeOffset CurrentPeriodEnd);

public sealed record TenantEntitlementDetail(
    string FeatureCode,
    bool Enabled,
    long? QuantityLimit,
    string? LimitUnit);

public sealed record TenantAdminUserDetail(
    Guid UserId,
    string Email,
    string DisplayName,
    string Status);

public sealed record SubscriptionPlanDetail(
    Guid PlanId,
    string PlanCode,
    string Name,
    string? Description,
    string Status,
    IReadOnlyList<TenantEntitlementDetail> Features);
