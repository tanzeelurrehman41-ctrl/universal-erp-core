namespace UniversalErp.Application.Tenancy;

public sealed class TenantCodeConflictException : Exception
{
}

public sealed class UserEmailConflictException : Exception
{
}

public sealed class TenantIdempotencyConflictException : Exception
{
}

public sealed class InvalidPlanException(string planCode)
    : Exception($"Subscription plan '{planCode}' is invalid or inactive.")
{
    public string PlanCode { get; } = planCode;
}

public interface ITenantOnboardingRepository
{
    Task<IReadOnlyList<SubscriptionPlanDetail>> ListSubscriptionPlansAsync(
        CancellationToken cancellationToken);

    Task<SubscriptionPlanDetail?> GetPlanByCodeAsync(
        string planCode,
        CancellationToken cancellationToken);

    Task<TenantDetail?> GetTenantByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<TenantDetail?> GetTenantByCodeAsync(
        string tenantCode,
        CancellationToken cancellationToken);

    Task<TenantDetail> RegisterTenantAsync(
        Guid idempotencyKey,
        string requestHash,
        RegisterTenantRequest request,
        CancellationToken cancellationToken);
}
