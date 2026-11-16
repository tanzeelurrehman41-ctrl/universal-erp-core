namespace UniversalErp.Application.SaaS;

public sealed class EntitlementEnforcementEngine
{
    public EntitlementCheckResult EvaluateEntitlement(
        Guid tenantId,
        string subscriptionStatus,
        string overagePolicy,
        decimal overageRate,
        TenantEntitlementDto? entitlement,
        decimal currentUsage,
        long requestedQuantity = 1,
        int warningThresholdPct = 80)
    {
        var normalizedStatus = subscriptionStatus.ToLowerInvariant().Trim();

        // 1. Subscription Lifecycle Check
        if (normalizedStatus is "suspended")
        {
            return new EntitlementCheckResult(
                tenantId,
                entitlement?.FeatureCode ?? "unknown",
                IsAllowed: false,
                SubscriptionStatus: subscriptionStatus,
                DenialReason: "Tenant subscription is currently suspended. Write operations are blocked; historical data remains preserved.",
                CurrentUsage: currentUsage,
                QuantityLimit: entitlement?.QuantityLimit,
                IsWarningThresholdExceeded: true,
                WarningMessage: "Subscription suspended. Please update payment method to reactivate services.",
                OveragePolicy: overagePolicy);
        }

        if (normalizedStatus is "expired")
        {
            return new EntitlementCheckResult(
                tenantId,
                entitlement?.FeatureCode ?? "unknown",
                IsAllowed: false,
                SubscriptionStatus: subscriptionStatus,
                DenialReason: "Subscription has expired. Access is limited to read-only data exports.",
                CurrentUsage: currentUsage,
                QuantityLimit: entitlement?.QuantityLimit,
                IsWarningThresholdExceeded: true,
                WarningMessage: "Subscription expired. Contact support to renew.",
                OveragePolicy: overagePolicy);
        }

        // 2. Feature Enabled Check
        if (entitlement is null || !entitlement.Enabled)
        {
            var code = entitlement?.FeatureCode ?? "unentitled_feature";
            return new EntitlementCheckResult(
                tenantId,
                code,
                IsAllowed: false,
                SubscriptionStatus: subscriptionStatus,
                DenialReason: $"Feature '{code}' is not included or enabled in current subscription plan.",
                CurrentUsage: currentUsage,
                QuantityLimit: 0,
                IsWarningThresholdExceeded: false,
                WarningMessage: null,
                OveragePolicy: overagePolicy);
        }

        // 3. Quantitative Limit Check
        var limit = entitlement.QuantityLimit;
        if (limit is null)
        {
            // Unlimited feature
            return new EntitlementCheckResult(
                tenantId,
                entitlement.FeatureCode,
                IsAllowed: true,
                SubscriptionStatus: subscriptionStatus,
                DenialReason: null,
                CurrentUsage: currentUsage,
                QuantityLimit: null,
                IsWarningThresholdExceeded: false,
                WarningMessage: null,
                OveragePolicy: overagePolicy);
        }

        var projectedUsage = currentUsage + requestedQuantity;
        var limitVal = (decimal)limit.Value;

        if (projectedUsage > limitVal)
        {
            if (string.Equals(overagePolicy, "allow_with_rate", StringComparison.OrdinalIgnoreCase))
            {
                var overageQty = projectedUsage - limitVal;
                return new EntitlementCheckResult(
                    tenantId,
                    entitlement.FeatureCode,
                    IsAllowed: true,
                    SubscriptionStatus: subscriptionStatus,
                    DenialReason: null,
                    CurrentUsage: currentUsage,
                    QuantityLimit: limitVal,
                    IsWarningThresholdExceeded: true,
                    WarningMessage: $"Overage policy active: {overageQty} units exceed plan limit at PKR {overageRate:N2} per unit.",
                    OveragePolicy: overagePolicy);
            }

            if (string.Equals(overagePolicy, "soft_warning", StringComparison.OrdinalIgnoreCase))
            {
                return new EntitlementCheckResult(
                    tenantId,
                    entitlement.FeatureCode,
                    IsAllowed: true,
                    SubscriptionStatus: subscriptionStatus,
                    DenialReason: null,
                    CurrentUsage: currentUsage,
                    QuantityLimit: limitVal,
                    IsWarningThresholdExceeded: true,
                    WarningMessage: $"Soft warning: Usage {projectedUsage} exceeds plan limit of {limitVal}.",
                    OveragePolicy: overagePolicy);
            }

            // Hard block
            return new EntitlementCheckResult(
                tenantId,
                entitlement.FeatureCode,
                IsAllowed: false,
                SubscriptionStatus: subscriptionStatus,
                DenialReason: $"Entitlement limit exceeded for '{entitlement.FeatureCode}'. Limit: {limitVal}, Current: {currentUsage}, Requested: {requestedQuantity}.",
                CurrentUsage: currentUsage,
                QuantityLimit: limitVal,
                IsWarningThresholdExceeded: true,
                WarningMessage: $"Quota full. Upgrade subscription plan to increase {entitlement.FeatureCode} limit.",
                OveragePolicy: overagePolicy);
        }

        // 4. Soft Warning Threshold Check (e.g. >= 80% of limit)
        var thresholdRatio = warningThresholdPct / 100.0m;
        var isNearLimit = limitVal > 0 && (projectedUsage / limitVal) >= thresholdRatio;

        string? softWarning = null;
        if (isNearLimit)
        {
            var pct = Math.Round((projectedUsage / limitVal) * 100, 1);
            softWarning = $"Usage warning: '{entitlement.FeatureCode}' is at {pct}% of plan limit ({projectedUsage}/{limitVal}).";
        }

        return new EntitlementCheckResult(
            tenantId,
            entitlement.FeatureCode,
            IsAllowed: true,
            SubscriptionStatus: subscriptionStatus,
            DenialReason: null,
            CurrentUsage: currentUsage,
            QuantityLimit: limitVal,
            IsWarningThresholdExceeded: isNearLimit,
            WarningMessage: softWarning,
            OveragePolicy: overagePolicy);
    }
}
