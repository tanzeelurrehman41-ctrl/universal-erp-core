using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Integrations;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Api.Integrations;

public static class IntegrationEndpointMappings
{
    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment)
    {
        var group = endpoints.MapGroup("/api/v1/integrations").WithTags("External Integrations Hub");

        // ── Webhooks ────────────────────────────────────────────────────────
        group.MapGet("/webhooks", async (
            IntegrationsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetWebhookSubscriptionsAsync(ctx.TenantId, ctx.CompanyId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapGet("/webhooks/{id:guid}", async (
            Guid id,
            IntegrationsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var sub = await service.GetWebhookSubscriptionByIdAsync(ctx.TenantId, id, httpContext.RequestAborted);
            return sub is null ? Results.NotFound() : Results.Ok(sub);
        });

        group.MapPost("/webhooks", async (
            [FromBody] CreateWebhookSubscriptionRequest req,
            IntegrationsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var result = await service.CreateWebhookSubscriptionAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/integrations/webhooks/{result.Subscription.Id}", result);
        });

        group.MapPost("/webhooks/{id:guid}/rotate-secret", async (
            Guid id,
            IntegrationsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var result = await service.RotateWebhookSecretAsync(ctx.TenantId, id, httpContext.RequestAborted);
            return Results.Ok(result);
        });

        group.MapDelete("/webhooks/{id:guid}", async (
            Guid id,
            IntegrationsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var deleted = await service.DeleteWebhookSubscriptionAsync(ctx.TenantId, id, httpContext.RequestAborted);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        // ── Event Deliveries ────────────────────────────────────────────────
        group.MapGet("/deliveries", async (
            [FromQuery] Guid? subscriptionId,
            [FromQuery] string? status,
            [FromQuery] int? limit,
            IntegrationsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetEventDeliveriesAsync(ctx.TenantId, subscriptionId, status, limit ?? 50, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapPost("/deliveries/enqueue", async (
            [FromBody] EnqueueEventRequest req,
            IntegrationsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var delivery = await service.EnqueueEventDeliveryAsync(ctx.TenantId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/integrations/deliveries/{delivery.Id}", delivery);
        });

        group.MapPost("/deliveries/{id:guid}/retry", async (
            Guid id,
            IntegrationsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var delivery = await service.RetryDeliveryAsync(ctx.TenantId, id, httpContext.RequestAborted);
            return Results.Ok(delivery);
        });

        // ── API Partners ────────────────────────────────────────────────────
        group.MapGet("/partners", async (
            IntegrationsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetApiPartnersAsync(ctx.TenantId, ctx.CompanyId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapPost("/partners", async (
            [FromBody] CreateApiPartnerRequest req,
            IntegrationsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var partner = await service.CreateApiPartnerAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/integrations/partners/{partner.Partner.Id}", partner);
        });

        group.MapPost("/partners/{id:guid}/revoke", async (
            Guid id,
            IntegrationsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var revoked = await service.RevokeApiPartnerAsync(ctx.TenantId, id, httpContext.RequestAborted);
            return revoked ? Results.Ok(new { message = "Partner API key revoked successfully." }) : Results.NotFound();
        });

        return endpoints;
    }

    private static TenantContext ResolveContext(HttpContext httpContext, ITenantContextAccessor accessor)
    {
        var tenantIdStr = httpContext.Request.Headers["X-Tenant-Id"].ToString();
        var companyIdStr = httpContext.Request.Headers["X-Company-Id"].ToString();
        var userIdStr = httpContext.Request.Headers["X-User-Id"].ToString();

        var tenantId = Guid.TryParse(tenantIdStr, out var tid) && tid != Guid.Empty ? tid : accessor.Current.TenantId;
        var companyId = Guid.TryParse(companyIdStr, out var cid) && cid != Guid.Empty ? cid : accessor.Current.CompanyId;
        var userId = Guid.TryParse(userIdStr, out var uid) && uid != Guid.Empty ? uid : accessor.Current.UserId;

        return new TenantContext(tenantId, companyId, userId);
    }
}
