namespace UniversalErp.Application.SaaS;

public sealed class SaaSService(
    ISaaSRepository repository,
    EntitlementEnforcementEngine entitlementEngine,
    BillingEngine billingEngine,
    IPaymentProvider paymentProvider)
{
    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
        repository.ListPlansAsync(cancellationToken);

    public Task<SubscriptionPlanDto?> GetPlanByCodeAsync(string planCode, CancellationToken cancellationToken) =>
        repository.GetPlanByCodeAsync(planCode, cancellationToken);

    public Task<TenantProvisioningResult> ProvisionTenantAsync(
        Guid idempotencyKey,
        TenantProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantCode))
            throw new ArgumentException("Tenant code is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new ArgumentException("Tenant display name is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.AdminEmail))
            throw new ArgumentException("Admin email is required.", nameof(request));

        return repository.ProvisionTenantAsync(idempotencyKey, request, cancellationToken);
    }

    public Task<TenantDetailDto?> GetTenantDetailAsync(Guid tenantId, CancellationToken cancellationToken) =>
        repository.GetTenantDetailAsync(tenantId, cancellationToken);

    public Task<IReadOnlyList<TenantDetailDto>> ListTenantsAsync(string? status, int limit, int offset, CancellationToken cancellationToken) =>
        repository.ListTenantsAsync(status, limit, offset, cancellationToken);

    public async Task<bool> UpdateTenantLifecycleAsync(
        Guid tenantId,
        string newStatus,
        string? reason,
        CancellationToken cancellationToken)
    {
        var success = await repository.UpdateTenantLifecycleAsync(tenantId, newStatus, reason, cancellationToken);
        if (success && string.Equals(newStatus, "suspended", StringComparison.OrdinalIgnoreCase))
        {
            await repository.UpdateSubscriptionLifecycleAsync(tenantId, "suspended", null, cancellationToken);
            await repository.CreatePlatformAlertAsync(new SaaSPlatformAlertDto(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                AlertType: "lifecycle_changed",
                Severity: "warning",
                Title: "Tenant Suspended",
                Message: $"Tenant {tenantId} was suspended. Reason: {reason ?? "Unspecified"}. Business records preserved.",
                IsAcknowledged: false,
                CreatedAt: DateTimeOffset.UtcNow), cancellationToken);
        }
        else if (success && string.Equals(newStatus, "active", StringComparison.OrdinalIgnoreCase))
        {
            await repository.UpdateSubscriptionLifecycleAsync(tenantId, "active", null, cancellationToken);
        }

        return success;
    }

    public async Task<EntitlementCheckResult> CheckEntitlementAsync(
        Guid tenantId,
        string featureCode,
        long requestedQuantity = 1,
        CancellationToken cancellationToken = default)
    {
        var subscription = await repository.GetSubscriptionByTenantIdAsync(tenantId, cancellationToken);
        if (subscription is null)
        {
            return new EntitlementCheckResult(
                tenantId,
                featureCode,
                IsAllowed: false,
                SubscriptionStatus: "none",
                DenialReason: "No active subscription found for tenant.",
                CurrentUsage: 0,
                QuantityLimit: 0,
                IsWarningThresholdExceeded: false,
                WarningMessage: null,
                OveragePolicy: "block");
        }

        var entitlement = await repository.GetEntitlementByFeatureAsync(tenantId, featureCode, cancellationToken);

        // Fetch current usage from aggregations if applicable
        var aggregations = await repository.GetUsageAggregationsAsync(tenantId, cancellationToken);
        var matchingAgg = aggregations.FirstOrDefault(a => MatchesMetricCode(a.MetricCode, featureCode));
        var currentUsage = matchingAgg?.CurrentQuantity ?? 0m;

        var result = entitlementEngine.EvaluateEntitlement(
            tenantId,
            subscription.Status,
            subscription.OveragePolicy,
            subscription.OverageRate,
            entitlement,
            currentUsage,
            requestedQuantity);

        if (result.IsWarningThresholdExceeded && !string.IsNullOrWhiteSpace(result.WarningMessage))
        {
            await repository.CreatePlatformAlertAsync(new SaaSPlatformAlertDto(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                AlertType: result.IsAllowed ? "quota_warning" : "quota_exceeded",
                Severity: result.IsAllowed ? "warning" : "error",
                Title: $"Quota Alert: {featureCode}",
                Message: result.WarningMessage,
                IsAcknowledged: false,
                CreatedAt: DateTimeOffset.UtcNow), cancellationToken);
        }

        return result;
    }

    public async Task RecordUsageAsync(
        RecordUsageRequest request,
        CancellationToken cancellationToken)
    {
        await repository.RecordUsageAsync(request, cancellationToken);
        await repository.AggregateUsageMetricAsync(
            request.TenantId,
            request.MetricCode,
            request.PeriodStart,
            request.PeriodEnd,
            cancellationToken);
    }

    public Task<IReadOnlyList<UsageAggregationDto>> GetUsageAggregationsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        repository.GetUsageAggregationsAsync(tenantId, cancellationToken);

    public async Task<UpgradeSubscriptionResult> UpgradeSubscriptionAsync(
        Guid tenantId,
        UpgradeSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var subscription = await repository.GetSubscriptionByTenantIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"No subscription found for tenant {tenantId}.");

        var targetPlan = await repository.GetPlanByCodeAsync(request.TargetPlanCode, cancellationToken)
            ?? throw new ArgumentException($"Target plan '{request.TargetPlanCode}' not found.");

        var interval = request.BillingInterval ?? subscription.BillingInterval;
        var pricing = targetPlan.Pricing.FirstOrDefault(p => string.Equals(p.BillingInterval, interval, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No pricing configured for plan '{request.TargetPlanCode}' interval '{interval}'.");

        var asOf = DateTimeOffset.UtcNow;
        var proration = billingEngine.CalculateProration(
            subscription.CurrentPrice,
            pricing.BasePrice,
            subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd,
            asOf);

        var result = await repository.UpgradeSubscriptionAsync(
            tenantId,
            request.TargetPlanCode,
            interval,
            proration.UnusedOldPlanCredit,
            proration.NewPlanCharge,
            proration.NetPayableAmount,
            cancellationToken);

        // If net payable is greater than zero, create upgrade invoice
        if (proration.NetPayableAmount > 0)
        {
            var invoiceNumber = billingEngine.GenerateInvoiceNumber($"UPG-{tenantId.ToString()[..4]}", asOf);
            var taxRate = billingEngine.ResolveTaxRate("PK", "FEDERAL");
            var calc = billingEngine.CalculateInvoice(proration.NetPayableAmount, 0, 0, 0, taxRate);

            var invoice = new SubscriptionInvoiceDto(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                SubscriptionId: subscription.SubscriptionId,
                InvoiceNumber: invoiceNumber,
                BillingReason: "plan_upgrade",
                PeriodStart: asOf,
                PeriodEnd: subscription.CurrentPeriodEnd,
                Subtotal: calc.Subtotal,
                DiscountAmount: calc.DiscountAmount,
                TaxAmount: calc.TaxAmount,
                TotalAmount: calc.TotalAmount,
                PaidAmount: 0.00m,
                CurrencyCode: pricing.CurrencyCode,
                Status: "open",
                DueDate: asOf.AddDays(7),
                PaidAt: null,
                HostedInvoiceUrl: $"/invoices/{invoiceNumber}",
                PdfDownloadUrl: $"/invoices/{invoiceNumber}.pdf",
                Lines: new List<SubscriptionInvoiceLineDto>
                {
                    new(Guid.NewGuid(), 1, $"Plan Upgrade: {subscription.PlanCode} to {targetPlan.PlanCode} (Prorated)", "proration", 1, proration.NetPayableAmount, proration.NetPayableAmount)
                });

            var createdInvoice = await repository.CreateInvoiceAsync(invoice, cancellationToken);
            return result with { InvoiceId = createdInvoice.Id };
        }

        return result;
    }

    public async Task<SubscriptionPaymentDto> ProcessPaymentAsync(
        ProcessPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var invoice = await repository.GetInvoiceByIdAsync(request.InvoiceId, cancellationToken)
            ?? throw new InvalidOperationException($"Invoice {request.InvoiceId} does not exist.");

        var paymentExec = await paymentProvider.ProcessPaymentAsync(
            request.TenantId,
            request.InvoiceId,
            request.Amount,
            request.CurrencyCode,
            request.PaymentMethodRef,
            request.IdempotencyKey,
            cancellationToken);

        var paymentDto = new SubscriptionPaymentDto(
            Id: Guid.NewGuid(),
            TenantId: request.TenantId,
            InvoiceId: request.InvoiceId,
            PaymentReference: paymentExec.PaymentReference,
            ProviderType: paymentProvider.ProviderName,
            Amount: paymentExec.AmountCharged > 0 ? paymentExec.AmountCharged : request.Amount,
            CurrencyCode: request.CurrencyCode,
            Status: paymentExec.Status,
            FailureReason: paymentExec.FailureReason,
            IdempotencyKey: request.IdempotencyKey,
            ProcessedAt: paymentExec.ProcessedAt);

        var recorded = await repository.RecordPaymentAsync(paymentDto, cancellationToken);

        if (paymentExec.Success)
        {
            // Reactivate subscription if it was in past due or grace
            var sub = await repository.GetSubscriptionByTenantIdAsync(request.TenantId, cancellationToken);
            if (sub is not null && (sub.Status is "past_due" or "grace" or "grace_period"))
            {
                await repository.UpdateSubscriptionLifecycleAsync(request.TenantId, "active", null, cancellationToken);
            }
        }
        else
        {
            await repository.CreatePlatformAlertAsync(new SaaSPlatformAlertDto(
                Id: Guid.NewGuid(),
                TenantId: request.TenantId,
                AlertType: "payment_failed",
                Severity: "error",
                Title: "Subscription Payment Failed",
                Message: $"Payment failed for invoice {invoice.InvoiceNumber}: {paymentExec.FailureReason}",
                IsAcknowledged: false,
                CreatedAt: DateTimeOffset.UtcNow), cancellationToken);
        }

        return recorded;
    }

    public async Task<SubscriptionCreditNoteDto> ProcessRefundAsync(
        Guid tenantId,
        Guid invoiceId,
        string paymentReference,
        decimal amount,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var refundExec = await paymentProvider.ProcessRefundAsync(
            tenantId,
            paymentReference,
            amount,
            reason,
            idempotencyKey,
            cancellationToken);

        if (!refundExec.Success)
        {
            throw new InvalidOperationException($"Refund processing failed: {refundExec.FailureReason}");
        }

        var creditNote = new SubscriptionCreditNoteDto(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            InvoiceId: invoiceId,
            CreditNoteNumber: $"CN-{tenantId.ToString()[..4].ToUpperInvariant()}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Amount: amount,
            Reason: reason,
            Status: "issued",
            CreatedAt: DateTimeOffset.UtcNow);

        return await repository.IssueCreditNoteAsync(creditNote, cancellationToken);
    }

    public Task<IReadOnlyList<SubscriptionInvoiceDto>> GetInvoicesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        repository.GetInvoicesByTenantAsync(tenantId, cancellationToken);

    public Task<SupportAccessGrantDto> GrantSupportAccessAsync(
        SupportAccessGrantRequest request,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        repository.GrantSupportAccessAsync(request, tenantId, cancellationToken);

    public Task<bool> RevokeSupportAccessAsync(Guid grantId, CancellationToken cancellationToken) =>
        repository.RevokeSupportAccessAsync(grantId, cancellationToken);

    public Task<IReadOnlyList<SupportAccessGrantDto>> ListSupportAccessGrantsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        repository.ListSupportAccessGrantsAsync(tenantId, cancellationToken);

    public Task<IReadOnlyList<SaaSPlatformAlertDto>> ListPlatformAlertsAsync(Guid? tenantId, bool unacknowledgedOnly, CancellationToken cancellationToken) =>
        repository.ListPlatformAlertsAsync(tenantId, unacknowledgedOnly, cancellationToken);

    public Task<IReadOnlyList<BillingTaxRuleDto>> ListTaxRulesAsync(string? countryCode, string? provinceState, CancellationToken cancellationToken) =>
        repository.ListTaxRulesAsync(countryCode, provinceState, cancellationToken);

    private static bool MatchesMetricCode(string metricCode, string featureCode)
    {
        if (string.Equals(metricCode, featureCode, StringComparison.OrdinalIgnoreCase))
            return true;

        var cleanMetric = metricCode.StartsWith("max_") ? metricCode[4..] : metricCode;
        var cleanFeature = featureCode.StartsWith("max_") ? featureCode[4..] : featureCode;

        if (string.Equals(cleanMetric, cleanFeature, StringComparison.OrdinalIgnoreCase))
            return true;

        if (cleanFeature.Contains("transaction") && cleanMetric.Contains("transaction")) return true;
        if (cleanFeature.Contains("storage") && cleanMetric.Contains("storage")) return true;
        if (cleanFeature.Contains("api_call") && cleanMetric.Contains("api_call")) return true;
        if (cleanFeature.Contains("compan") && cleanMetric.Contains("compan")) return true;
        if (cleanFeature.Contains("user") && cleanMetric.Contains("user")) return true;
        if (cleanFeature.Contains("branch") && cleanMetric.Contains("branch")) return true;
        if (cleanFeature.Contains("location") && cleanMetric.Contains("location")) return true;
        if (cleanFeature.Contains("integration") && cleanMetric.Contains("integration")) return true;

        return false;
    }
}
