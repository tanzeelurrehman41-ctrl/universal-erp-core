using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Branches;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Api.Branches;

public static class BranchEndpointMappings
{
    public static IEndpointRouteBuilder MapBranchEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment)
    {
        var group = endpoints.MapGroup("/api/v1/branches").WithTags("Branches");

        // 1. List branches
        group.MapGet("/", async (
            BranchService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetBranchesAsync(ctx.TenantId, ctx.CompanyId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        // 2. Hierarchy
        group.MapGet("/hierarchy", async (
            BranchService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var tree = await service.GetHierarchyAsync(ctx.TenantId, ctx.CompanyId, httpContext.RequestAborted);
            return Results.Ok(tree);
        });

        // 3. Get branch by id
        group.MapGet("/{id:guid}", async (
            Guid id,
            BranchService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var branch = await service.GetBranchByIdAsync(ctx.TenantId, ctx.CompanyId, id, httpContext.RequestAborted);
            return branch is null ? Results.NotFound() : Results.Ok(branch);
        });

        // 4. Create branch
        group.MapPost("/", async (
            [FromBody] CreateBranchRequest req,
            BranchService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var branch = await service.CreateBranchAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/branches/{branch.Id}", branch);
        });

        // 5. Update branch
        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateBranchRequest req,
            BranchService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var branch = await service.UpdateBranchAsync(ctx.TenantId, ctx.CompanyId, id, req, httpContext.RequestAborted);
            return branch is null ? Results.NotFound() : Results.Ok(branch);
        });

        // 6. Switch branch context
        group.MapPost("/{id:guid}/switch", async (
            Guid id,
            BranchService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var result = await service.SwitchBranchAsync(ctx.TenantId, ctx.CompanyId, id, httpContext.RequestAborted);
            return Results.Ok(result);
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
