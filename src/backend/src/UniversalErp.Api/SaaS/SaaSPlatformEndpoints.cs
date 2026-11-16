using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UniversalErp.Application.SaaS;

namespace UniversalErp.Api.SaaS;

public static class SaaSPlatformEndpoints
{
    public static IEndpointRouteBuilder MapSaaSEndpoints(this IEndpointRouteBuilder app, bool isDevelopment)
    {
        var group = app.MapGroup("/api/v1/saas")
            .WithTags("SaaS Platform & Tenant Management");

        // ── Plans & Pricing ─────────────────────────────────────────────────

        group.MapGet("/plans", async (SaaSService service, CancellationToken ct) =>
        {
            var plans = await service.ListPlansAsync(ct);
            return Results.Ok(plans);
        });

        group.MapGet("/plans/{code}", async (string code, SaaSService service, CancellationToken ct) =>
        {
            var plan = await service.GetPlanByCodeAsync(code, ct);
            return plan != null ? Results.Ok(plan) : Results.NotFound(new { error = $"Plan '{code}' not found." });
        });

        // ── Tenant Provisioning & Lifecycle ─────────────────────────────────

        group.MapPost("/tenants/provision", async (
            HttpContext context,
            TenantProvisioningRequest request,
            SaaSService service,
            CancellationToken ct) =>
        {
            var idempHeader = context.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
            var idempotencyKey = Guid.TryParse(idempHeader, out var parsedKey) ? parsedKey : Guid.NewGuid();

            try
            {
                var result = await service.ProvisionTenantAsync(idempotencyKey, request, ct);
                return Results.Created($"/api/v1/saas/tenants/{result.TenantId}", result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        group.MapGet("/tenants", async (
            string? status,
            int? limit,
            int? offset,
            SaaSService service,
            CancellationToken ct) =>
        {
            var tenants = await service.ListTenantsAsync(status, limit ?? 50, offset ?? 0, ct);
            return Results.Ok(tenants);
        });

        group.MapGet("/tenants/{id:guid}", async (Guid id, SaaSService service, CancellationToken ct) =>
        {
            var tenant = await service.GetTenantDetailAsync(id, ct);
            return tenant != null ? Results.Ok(tenant) : Results.NotFound(new { error = $"Tenant {id} not found." });
        });

        group.MapPost("/tenants/{id:guid}/lifecycle", async (
            Guid id,
            TenantLifecycleTransitionRequest request,
            SaaSService service,
            CancellationToken ct) =>
        {
            var success = await service.UpdateTenantLifecycleAsync(id, request.NewStatus, request.Reason, ct);
            return success ? Results.Ok(new { message = $"Tenant status updated to {request.NewStatus}." })
                           : Results.NotFound(new { error = "Tenant not found or status update failed." });
        });

        // ── Entitlements & Metering ─────────────────────────────────────────

        group.MapPost("/tenants/{id:guid}/entitlements/check", async (
            Guid id,
            EntitlementCheckRequest request,
            SaaSService service,
            CancellationToken ct) =>
        {
            var result = await service.CheckEntitlementAsync(id, request.FeatureCode, request.RequestedQuantity, ct);
            return Results.Ok(result);
        });

        group.MapPost("/tenants/{id:guid}/usage", async (
            Guid id,
            RecordUsageRequest request,
            SaaSService service,
            CancellationToken ct) =>
        {
            var req = request with { TenantId = id };
            await service.RecordUsageAsync(req, ct);
            return Results.Accepted();
        });

        group.MapGet("/tenants/{id:guid}/usage", async (
            Guid id,
            SaaSService service,
            CancellationToken ct) =>
        {
            var usage = await service.GetUsageAggregationsAsync(id, ct);
            return Results.Ok(usage);
        });

        // ── Subscriptions & Invoicing ───────────────────────────────────────

        group.MapPost("/tenants/{id:guid}/subscriptions/upgrade", async (
            Guid id,
            UpgradeSubscriptionRequest request,
            SaaSService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.UpgradeSubscriptionAsync(id, request, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapGet("/tenants/{id:guid}/invoices", async (
            Guid id,
            SaaSService service,
            CancellationToken ct) =>
        {
            var invoices = await service.GetInvoicesAsync(id, ct);
            return Results.Ok(invoices);
        });

        group.MapPost("/tenants/{id:guid}/payments", async (
            Guid id,
            ProcessPaymentRequest request,
            SaaSService service,
            CancellationToken ct) =>
        {
            var req = request with { TenantId = id };
            var result = await service.ProcessPaymentAsync(req, ct);
            return Results.Ok(result);
        });

        group.MapPost("/tenants/{id:guid}/refunds", async (
            Guid id,
            RefundRequest request,
            SaaSService service,
            CancellationToken ct) =>
        {
            var result = await service.ProcessRefundAsync(
                id, request.InvoiceId, request.PaymentReference, request.Amount, request.Reason, request.IdempotencyKey, ct);
            return Results.Ok(result);
        });

        // ── Support Access & Alerts ─────────────────────────────────────────

        group.MapPost("/tenants/{id:guid}/support-access", async (
            Guid id,
            SupportAccessGrantRequest request,
            SaaSService service,
            CancellationToken ct) =>
        {
            var grant = await service.GrantSupportAccessAsync(request, id, ct);
            return Results.Created($"/api/v1/saas/support-access/{grant.Id}", grant);
        });

        group.MapPost("/support-access/{grantId:guid}/revoke", async (
            Guid grantId,
            SaaSService service,
            CancellationToken ct) =>
        {
            var success = await service.RevokeSupportAccessAsync(grantId, ct);
            return success ? Results.Ok(new { message = "Support access revoked." })
                           : Results.NotFound(new { error = "Grant not found or already revoked." });
        });

        group.MapGet("/alerts", async (
            Guid? tenantId,
            bool? unacknowledgedOnly,
            SaaSService service,
            CancellationToken ct) =>
        {
            var alerts = await service.ListPlatformAlertsAsync(tenantId, unacknowledgedOnly ?? false, ct);
            return Results.Ok(alerts);
        });

        return app;
    }

    public sealed record RefundRequest(
        Guid InvoiceId,
        string PaymentReference,
        decimal Amount,
        string Reason,
        string IdempotencyKey);
}
