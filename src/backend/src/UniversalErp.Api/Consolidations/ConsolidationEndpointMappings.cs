using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Consolidations;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Api.Consolidations;

public static class ConsolidationEndpointMappings
{
    public static IEndpointRouteBuilder MapConsolidationEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment)
    {
        var icGroup = endpoints.MapGroup("/api/v1/intercompany").WithTags("Intercompany Operations");
        var consGroup = endpoints.MapGroup("/api/v1/consolidations").WithTags("Financial Consolidation");
        if (!isDevelopment)
        {
            icGroup.RequireAuthorization();
            consGroup.RequireAuthorization();
        }

        // ── Intercompany Relationships ───────────────────────────────────────
        icGroup.MapGet("/relationships", async (
            ConsolidationService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetRelationshipsAsync(ctx.TenantId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        icGroup.MapPost("/relationships", async (
            [FromBody] CreateIntercompanyRelationshipRequest req,
            ConsolidationService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var rel = await service.CreateRelationshipAsync(ctx.TenantId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/intercompany/relationships/{rel.Id}", rel);
        });

        // ── Intercompany Stock Transfers ─────────────────────────────────────
        icGroup.MapGet("/transfers", async (
            [FromQuery] Guid? companyId,
            [FromQuery] string? status,
            ConsolidationService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var comp = companyId ?? ctx.CompanyId;
            var list = await service.GetTransfersAsync(ctx.TenantId, comp, status, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        icGroup.MapGet("/transfers/{id:guid}", async (
            Guid id,
            ConsolidationService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var transfer = await service.GetTransferByIdAsync(ctx.TenantId, id, httpContext.RequestAborted);
            return transfer is null ? Results.NotFound() : Results.Ok(transfer);
        });

        icGroup.MapPost("/transfers", async (
            [FromBody] CreateIntercompanyTransferRequest req,
            ConsolidationService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var transfer = await service.CreateTransferAsync(ctx.TenantId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/intercompany/transfers/{transfer.Id}", transfer);
        });

        icGroup.MapPost("/transfers/{id:guid}/dispatch", async (
            Guid id,
            ConsolidationService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var transfer = await service.DispatchTransferAsync(ctx.TenantId, id, ctx.UserId, httpContext.RequestAborted);
            return Results.Ok(transfer);
        });

        icGroup.MapPost("/transfers/{id:guid}/receive", async (
            Guid id,
            [FromBody] ReceiveIntercompanyTransferRequest req,
            ConsolidationService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var transfer = await service.ReceiveTransferAsync(ctx.TenantId, id, ctx.UserId, req, httpContext.RequestAborted);
            return Results.Ok(transfer);
        });

        // ── Group Financial Consolidation ────────────────────────────────────
        consGroup.MapGet("/runs", async (
            [FromQuery] Guid? fiscalPeriodId,
            ConsolidationService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetConsolidationRunsAsync(ctx.TenantId, fiscalPeriodId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        consGroup.MapGet("/runs/{id:guid}", async (
            Guid id,
            ConsolidationService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var run = await service.GetConsolidationRunByIdAsync(ctx.TenantId, id, httpContext.RequestAborted);
            return run is null ? Results.NotFound() : Results.Ok(run);
        });

        consGroup.MapPost("/runs", async (
            [FromBody] ExecuteConsolidationRequest req,
            ConsolidationService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var run = await service.ExecuteConsolidationAsync(ctx.TenantId, ctx.UserId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/consolidations/runs/{run.Id}", run);
        });

        consGroup.MapGet("/runs/{id:guid}/statements", async (
            Guid id,
            [FromQuery] string? type,
            ConsolidationService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var statementType = string.IsNullOrWhiteSpace(type) ? "balance_sheet" : type;
            var statement = await service.GetConsolidatedStatementAsync(ctx.TenantId, id, statementType, httpContext.RequestAborted);
            return Results.Ok(statement);
        });

        return endpoints;
    }

    private static TenantContext ResolveContext(HttpContext httpContext, ITenantContextAccessor accessor)
    {
        if (accessor?.Current is { } current && current.TenantId != Guid.Empty)
        {
            return current;
        }

        var tenantIdStr = httpContext.Request.Headers["X-Tenant-Id"].ToString();
        var companyIdStr = httpContext.Request.Headers["X-Company-Id"].ToString();

        var tenantId = Guid.TryParse(tenantIdStr, out var tid) && tid != Guid.Empty ? tid : Guid.Empty;
        var companyId = Guid.TryParse(companyIdStr, out var cid) && cid != Guid.Empty ? cid : Guid.Empty;

        return new TenantContext(tenantId, companyId, Guid.Empty);
    }
}
