using UniversalErp.Application.SaaS;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class EntitlementEnforcementEngineTests
{
    private readonly EntitlementEnforcementEngine _engine = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public void EvaluateEntitlement_WhenSubscriptionSuspended_BlocksWithPreservationMessage()
    {
        var entitlement = new TenantEntitlementDto("sales.core", true, 100, "transactions", "plan", DateTimeOffset.UtcNow, null);

        var result = _engine.EvaluateEntitlement(
            _tenantId,
            subscriptionStatus: "suspended",
            overagePolicy: "block",
            overageRate: 0m,
            entitlement: entitlement,
            currentUsage: 10,
            requestedQuantity: 1);

        Assert.False(result.IsAllowed);
        Assert.Equal("suspended", result.SubscriptionStatus);
        Assert.Contains("suspended", result.DenialReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preserved", result.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateEntitlement_WhenSubscriptionExpired_BlocksWithRenewalMessage()
    {
        var entitlement = new TenantEntitlementDto("sales.core", true, 100, "transactions", "plan", DateTimeOffset.UtcNow, null);

        var result = _engine.EvaluateEntitlement(
            _tenantId,
            subscriptionStatus: "expired",
            overagePolicy: "block",
            overageRate: 0m,
            entitlement: entitlement,
            currentUsage: 10,
            requestedQuantity: 1);

        Assert.False(result.IsAllowed);
        Assert.Equal("expired", result.SubscriptionStatus);
        Assert.Contains("expired", result.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateEntitlement_WhenFeatureNotEntitledOrDisabled_BlocksAccess()
    {
        var result = _engine.EvaluateEntitlement(
            _tenantId,
            subscriptionStatus: "active",
            overagePolicy: "block",
            overageRate: 0m,
            entitlement: null,
            currentUsage: 0,
            requestedQuantity: 1);

        Assert.False(result.IsAllowed);
        Assert.Contains("not included or enabled", result.DenialReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateEntitlement_WhenFeatureUnlimited_AllowsAccess()
    {
        var entitlement = new TenantEntitlementDto("catalog.core", true, null, null, "plan", DateTimeOffset.UtcNow, null);

        var result = _engine.EvaluateEntitlement(
            _tenantId,
            subscriptionStatus: "active",
            overagePolicy: "block",
            overageRate: 0m,
            entitlement: entitlement,
            currentUsage: 99999,
            requestedQuantity: 50);

        Assert.True(result.IsAllowed);
        Assert.Null(result.QuantityLimit);
        Assert.False(result.IsWarningThresholdExceeded);
    }

    [Fact]
    public void EvaluateEntitlement_WhenUsageExceedsLimitWithHardBlock_DeniesAccess()
    {
        var entitlement = new TenantEntitlementDto("max_users", true, 10, "users", "plan", DateTimeOffset.UtcNow, null);

        var result = _engine.EvaluateEntitlement(
            _tenantId,
            subscriptionStatus: "active",
            overagePolicy: "block",
            overageRate: 0m,
            entitlement: entitlement,
            currentUsage: 10,
            requestedQuantity: 1);

        Assert.False(result.IsAllowed);
        Assert.Contains("limit exceeded", result.DenialReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.IsWarningThresholdExceeded);
    }

    [Fact]
    public void EvaluateEntitlement_WhenUsageExceedsLimitWithOverageRate_AllowsWithOverageNotice()
    {
        var entitlement = new TenantEntitlementDto("max_api_calls_per_month", true, 1000, "calls", "plan", DateTimeOffset.UtcNow, null);

        var result = _engine.EvaluateEntitlement(
            _tenantId,
            subscriptionStatus: "active",
            overagePolicy: "allow_with_rate",
            overageRate: 2.50m,
            entitlement: entitlement,
            currentUsage: 1000,
            requestedQuantity: 50);

        Assert.True(result.IsAllowed);
        Assert.True(result.IsWarningThresholdExceeded);
        Assert.Contains("Overage policy active", result.WarningMessage);
    }

    [Fact]
    public void EvaluateEntitlement_WhenNearThreshold_TriggersSoftWarning()
    {
        var entitlement = new TenantEntitlementDto("max_storage_mb", true, 1000, "megabytes", "plan", DateTimeOffset.UtcNow, null);

        // 850 / 1000 = 85% >= 80% threshold
        var result = _engine.EvaluateEntitlement(
            _tenantId,
            subscriptionStatus: "active",
            overagePolicy: "block",
            overageRate: 0m,
            entitlement: entitlement,
            currentUsage: 800,
            requestedQuantity: 50,
            warningThresholdPct: 80);

        Assert.True(result.IsAllowed);
        Assert.True(result.IsWarningThresholdExceeded);
        Assert.Contains("is at 85", result.WarningMessage);
    }
}
