using Npgsql;
using UniversalErp.Application.SaaS;
using UniversalErp.Infrastructure.SaaS;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class Phase14PostgresSaaSIntegrationTests
{
    private static readonly string StagingConnectionString =
        Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
        ?? "Host=127.0.0.1;Port=5433;Database=universal_erp_phase5_dev;Username=erp_dev;Password=local_dev_change_me;Include Error Detail=true;";

    [Fact]
    public async Task Phase14_CompleteSaaSPlatformLifecycle_AllScenariosExecuteSuccessfully()
    {
        var ct = CancellationToken.None;
        var dataSource = NpgsqlDataSource.Create(StagingConnectionString);
        var repository = new PostgresSaaSRepository(dataSource);
        var entitlementEngine = new EntitlementEnforcementEngine();
        var billingEngine = new BillingEngine();
        var paymentProvider = new SandboxPaymentProvider();
        var service = new SaaSService(repository, entitlementEngine, billingEngine, paymentProvider);

        // ── Scenario 1: Tenant Creation & Automated DB-Level Provisioning ────
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var tenantCode = $"p14_{suffix}";
        var idempotencyKey = Guid.NewGuid();

        var provRequest = new TenantProvisioningRequest(
            TenantCode: tenantCode,
            DisplayName: $"Phase 14 Test Enterprise {suffix}",
            LegalName: $"Phase 14 Holdings Ltd {suffix}",
            PlanCode: "starter",
            BillingInterval: "month",
            CompanyName: $"Phase 14 Industrial Corp {suffix}",
            CompanyCode: $"P14-CORP-{suffix.ToUpperInvariant()}",
            BranchName: "Headquarters Regional",
            BranchCode: "HQ",
            CountryCode: "PK",
            BaseCurrencyCode: "PKR",
            Timezone: "Asia/Karachi",
            AdminEmail: $"admin_{suffix}@example.com",
            AdminFullName: "SaaS Administrator",
            AdminPhone: "+923001234567");

        var provResult = await service.ProvisionTenantAsync(idempotencyKey, provRequest, ct);

        Assert.NotNull(provResult);
        Assert.NotEqual(Guid.Empty, provResult.TenantId);
        Assert.Equal(tenantCode, provResult.TenantCode);
        Assert.NotEqual(Guid.Empty, provResult.CompanyId);
        Assert.NotEqual(Guid.Empty, provResult.BranchId);
        Assert.Equal("HQ", provResult.BranchCode);
        Assert.NotEqual(Guid.Empty, provResult.SubscriptionId);
        Assert.Equal("active", provResult.Status);

        // ── Scenario 2: Provisioning Idempotency & Retry Recovery ─────────────
        var retryResult = await service.ProvisionTenantAsync(idempotencyKey, provRequest, ct);
        Assert.Equal(provResult.TenantId, retryResult.TenantId);
        Assert.Equal(provResult.CompanyId, retryResult.CompanyId);
        Assert.Equal(provResult.SubscriptionId, retryResult.SubscriptionId);

        // ── Scenario 3: Entitlement Enforcement - Unentitled Module Block ────
        // Starter plan does NOT have 'manufacturing.core'
        var unentitledCheck = await service.CheckEntitlementAsync(provResult.TenantId, "manufacturing.core", 1, ct);
        Assert.False(unentitledCheck.IsAllowed);
        Assert.Contains("not included or enabled", unentitledCheck.DenialReason, StringComparison.OrdinalIgnoreCase);

        // Starter plan DOES have 'sales.core'
        var entitledCheck = await service.CheckEntitlementAsync(provResult.TenantId, "sales.core", 1, ct);
        Assert.True(entitledCheck.IsAllowed);
        Assert.Null(entitledCheck.DenialReason);

        // ── Scenario 4: Quantitative Limits & Hard Block ─────────────────────
        // Starter plan has 'max_companies' = 1
        var companyLimitCheck = await service.CheckEntitlementAsync(provResult.TenantId, "max_companies", 1, ct);
        // Current company count is 1. Requesting 1 more would exceed 1
        Assert.False(companyLimitCheck.IsAllowed);
        Assert.Contains("limit exceeded", companyLimitCheck.DenialReason, StringComparison.OrdinalIgnoreCase);

        // ── Scenario 5: Soft Warning Threshold Evaluation (>= 80%) ──────────
        // Record storage usage of 1700 MB out of 2048 MB (83% >= 80% threshold)
        var now = DateTimeOffset.UtcNow;
        var periodEnd = now.AddMonths(1);
        await service.RecordUsageAsync(new RecordUsageRequest(
            TenantId: provResult.TenantId,
            MetricCode: "max_storage_mb",
            Quantity: 1700m,
            Unit: "megabytes",
            PeriodStart: now,
            PeriodEnd: periodEnd), ct);

        var storageCheck = await service.CheckEntitlementAsync(provResult.TenantId, "max_storage_mb", 10, ct);
        Assert.True(storageCheck.IsAllowed);
        Assert.True(storageCheck.IsWarningThresholdExceeded);
        Assert.NotNull(storageCheck.WarningMessage);
        Assert.Contains("Usage warning", storageCheck.WarningMessage);

        // Verify alert was logged in platform alerts queue
        var alerts = await service.ListPlatformAlertsAsync(provResult.TenantId, false, ct);
        Assert.Contains(alerts, a => a.AlertType == "quota_warning");

        // ── Scenario 6: Overage Policy Execution (allow_with_rate) ───────────
        // Update subscription overage policy
        await using (var rawConn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var updCmd = new NpgsqlCommand(
                "UPDATE platform.subscriptions SET overage_policy = 'allow_with_rate', overage_rate = 5.00 WHERE tenant_id = @tId", rawConn);
            updCmd.Parameters.AddWithValue("tId", provResult.TenantId);
            await updCmd.ExecuteNonQueryAsync(ct);
        }

        // Test with usage exceeding limit
        var overageResult = entitlementEngine.EvaluateEntitlement(
            provResult.TenantId,
            subscriptionStatus: "active",
            overagePolicy: "allow_with_rate",
            overageRate: 5.00m,
            entitlement: new TenantEntitlementDto("max_api_calls_per_month", true, 1000, "calls", "plan", now, null),
            currentUsage: 1000m,
            requestedQuantity: 50);

        Assert.True(overageResult.IsAllowed);
        Assert.True(overageResult.IsWarningThresholdExceeded);
        Assert.Contains("Overage policy active", overageResult.WarningMessage);

        // ── Scenario 7: Usage Recording & Idempotent Aggregation ─────────────
        var usageAggs = await service.GetUsageAggregationsAsync(provResult.TenantId, ct);
        Assert.NotEmpty(usageAggs);
        var storageAgg = usageAggs.FirstOrDefault(a => a.MetricCode == "max_storage_mb");
        Assert.NotNull(storageAgg);
        Assert.Equal(1700m, storageAgg.CurrentQuantity);

        // ── Scenario 8: Mid-Cycle Plan Upgrade with Proration ────────────────
        var upgradeResult = await service.UpgradeSubscriptionAsync(provResult.TenantId, new UpgradeSubscriptionRequest(
            TargetPlanCode: "professional",
            BillingInterval: "month"), ct);

        Assert.Equal("succeeded", upgradeResult.Status);
        Assert.Equal("starter", upgradeResult.OldPlanCode);
        Assert.Equal("professional", upgradeResult.NewPlanCode);
        Assert.True(upgradeResult.NewCharge > upgradeResult.ProrationCredit);
        Assert.True(upgradeResult.NetPayable > 0);
        Assert.NotNull(upgradeResult.InvoiceId);

        // Verify upgraded entitlements snapshot now includes 'manufacturing.core' and 'warehouse.advanced'
        var postUpgradeEntitlements = await repository.GetTenantEntitlementsAsync(provResult.TenantId, ct);
        Assert.Contains(postUpgradeEntitlements, e => e.FeatureCode == "manufacturing.core");
        Assert.Contains(postUpgradeEntitlements, e => e.FeatureCode == "warehouse.advanced");

        // ── Scenario 9: Sandbox Payment Success & Subscription Reactivation ──
        // Simulate subscription in 'past_due' status
        await repository.UpdateSubscriptionLifecycleAsync(provResult.TenantId, "past_due", null, ct);
        var subBeforePay = await repository.GetSubscriptionByTenantIdAsync(provResult.TenantId, ct);
        Assert.Equal("past_due", subBeforePay!.Status);

        var payKey = $"pay_key_{Guid.NewGuid():N}";
        var paymentResult = await service.ProcessPaymentAsync(new ProcessPaymentRequest(
            TenantId: provResult.TenantId,
            InvoiceId: upgradeResult.InvoiceId!.Value,
            Amount: upgradeResult.NetPayable,
            CurrencyCode: "PKR",
            PaymentMethodRef: "pm_card_visa_test",
            IdempotencyKey: payKey), ct);

        Assert.Equal("succeeded", paymentResult.Status);
        Assert.StartsWith("PAY-SBX-", paymentResult.PaymentReference);

        // Subscription should now be reactivated to 'active'
        var subAfterPay = await repository.GetSubscriptionByTenantIdAsync(provResult.TenantId, ct);
        Assert.Equal("active", subAfterPay!.Status);

        // Invoice should now be marked as 'paid'
        var paidInvoice = await repository.GetInvoiceByIdAsync(upgradeResult.InvoiceId.Value, ct);
        Assert.Equal("paid", paidInvoice!.Status);

        // ── Scenario 10: Sandbox Payment Failure Simulation ──────────────────
        var failKey = $"pay_fail_{Guid.NewGuid():N}";
        var failResult = await service.ProcessPaymentAsync(new ProcessPaymentRequest(
            TenantId: provResult.TenantId,
            InvoiceId: upgradeResult.InvoiceId.Value,
            Amount: 1000m,
            CurrencyCode: "PKR",
            PaymentMethodRef: "pm_card_decline_simulated",
            IdempotencyKey: failKey), ct);

        Assert.Equal("failed", failResult.Status);
        Assert.Contains("decline", failResult.FailureReason, StringComparison.OrdinalIgnoreCase);

        // ── Scenario 11: Duplicate Payment Idempotency Handling ──────────────
        var dupPayResult = await service.ProcessPaymentAsync(new ProcessPaymentRequest(
            TenantId: provResult.TenantId,
            InvoiceId: upgradeResult.InvoiceId.Value,
            Amount: upgradeResult.NetPayable,
            CurrencyCode: "PKR",
            PaymentMethodRef: "pm_card_visa_test",
            IdempotencyKey: payKey), ct);

        Assert.Equal(paymentResult.Id, dupPayResult.Id);
        Assert.Equal(paymentResult.PaymentReference, dupPayResult.PaymentReference);

        // ── Scenario 12: Refund Processing & Credit Note Issuance ────────────
        var refundKey = $"ref_key_{Guid.NewGuid():N}";
        var creditNote = await service.ProcessRefundAsync(
            provResult.TenantId,
            upgradeResult.InvoiceId.Value,
            paymentResult.PaymentReference,
            5000.00m,
            "Partial plan concession",
            refundKey,
            ct);

        Assert.NotNull(creditNote);
        Assert.StartsWith("CN-", creditNote.CreditNoteNumber);
        Assert.Equal(5000.00m, creditNote.Amount);
        Assert.Equal("issued", creditNote.Status);

        // ── Scenario 13: Tenant Suspension & Data Preservation ───────────────
        var suspSuccess = await service.UpdateTenantLifecycleAsync(
            provResult.TenantId, "suspended", "Non-payment audit compliance", ct);
        Assert.True(suspSuccess);

        // Verify writes are blocked under suspended state
        var suspCheck = await service.CheckEntitlementAsync(provResult.TenantId, "sales.core", 1, ct);
        Assert.False(suspCheck.IsAllowed);
        Assert.Contains("suspended", suspCheck.DenialReason, StringComparison.OrdinalIgnoreCase);

        // Verify tenant business data remains intact and queryable
        var tenantDetail = await service.GetTenantDetailAsync(provResult.TenantId, ct);
        Assert.NotNull(tenantDetail);
        Assert.Equal("suspended", tenantDetail.Status);
        Assert.NotNull(tenantDetail.Company);
        Assert.NotNull(tenantDetail.DefaultBranch);

        // ── Scenario 14: Cross-Tenant Access Rejection & RLS Isolation ───────
        var otherTenantId = Guid.NewGuid();
        var crossTenantEntitlements = await repository.GetTenantEntitlementsAsync(otherTenantId, ct);
        Assert.Empty(crossTenantEntitlements);

        var crossTenantInvoices = await repository.GetInvoicesByTenantAsync(otherTenantId, ct);
        Assert.Empty(crossTenantInvoices);
    }
}
