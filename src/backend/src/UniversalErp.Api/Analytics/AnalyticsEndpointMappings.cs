using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Analytics;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Api.Analytics;

public static class AnalyticsEndpointMappings
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment)
    {
        var group = endpoints.MapGroup("/api/v1/analytics").WithTags("Analytics & BI");
        if (!isDevelopment)
        {
            group.RequireAuthorization();
        }

        // 1. Executive Dashboard
        group.MapGet("/dashboard", async (
            [FromQuery] DateOnly? startDate,
            [FromQuery] DateOnly? endDate,
            [FromQuery] Guid? warehouseId,
            [FromQuery] Guid? departmentId,
            [FromQuery] Guid? productId,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            bool hasSalaryView = await CheckSalaryViewPermissionAsync(httpContext, isDevelopment);
            var filter = new AnalyticsDateRangeFilter(startDate, endDate, warehouseId, departmentId, productId);
            var dash = await service.GetExecutiveDashboardAsync(ctx.TenantId, ctx.CompanyId, filter, hasSalaryView, httpContext.RequestAborted);
            return Results.Ok(dash);
        }).RequireAuthorization("analytics.dashboard.view");

        // 2. Sales & Revenue
        group.MapGet("/sales", async (
            [FromQuery] DateOnly? startDate,
            [FromQuery] DateOnly? endDate,
            [FromQuery] Guid? productId,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var filter = new AnalyticsDateRangeFilter(startDate, endDate, ProductId: productId);
            var sales = await service.GetSalesRevenueAnalyticsAsync(ctx.TenantId, ctx.CompanyId, filter, httpContext.RequestAborted);
            return Results.Ok(sales);
        }).RequireAuthorization("analytics.supply_chain.view");

        // 3. Purchasing & Suppliers
        group.MapGet("/purchasing", async (
            [FromQuery] DateOnly? startDate,
            [FromQuery] DateOnly? endDate,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var filter = new AnalyticsDateRangeFilter(startDate, endDate);
            var purch = await service.GetPurchasingSupplierAnalyticsAsync(ctx.TenantId, ctx.CompanyId, filter, httpContext.RequestAborted);
            return Results.Ok(purch);
        }).RequireAuthorization("analytics.supply_chain.view");

        // 4. Inventory Valuation & Ageing
        group.MapGet("/inventory", async (
            [FromQuery] DateOnly? startDate,
            [FromQuery] DateOnly? endDate,
            [FromQuery] Guid? warehouseId,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var filter = new AnalyticsDateRangeFilter(startDate, endDate, WarehouseId: warehouseId);
            var inv = await service.GetInventoryValuationAgeingAsync(ctx.TenantId, ctx.CompanyId, filter, httpContext.RequestAborted);
            return Results.Ok(inv);
        }).RequireAuthorization("analytics.supply_chain.view");

        // 5. Manufacturing Efficiency & WIP
        group.MapGet("/manufacturing", async (
            [FromQuery] DateOnly? startDate,
            [FromQuery] DateOnly? endDate,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var filter = new AnalyticsDateRangeFilter(startDate, endDate);
            var mfg = await service.GetManufacturingEfficiencyAsync(ctx.TenantId, ctx.CompanyId, filter, httpContext.RequestAborted);
            return Results.Ok(mfg);
        }).RequireAuthorization("analytics.supply_chain.view");

        // 6. Warehouse Logistics
        group.MapGet("/warehouse", async (
            [FromQuery] Guid? warehouseId,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var filter = new AnalyticsDateRangeFilter(WarehouseId: warehouseId);
            var wh = await service.GetWarehouseLogisticsAnalyticsAsync(ctx.TenantId, ctx.CompanyId, filter, httpContext.RequestAborted);
            return Results.Ok(wh);
        }).RequireAuthorization("analytics.supply_chain.view");

        // 7. Finance, P&L & Cash Flow
        group.MapGet("/finance", async (
            [FromQuery] DateOnly? startDate,
            [FromQuery] DateOnly? endDate,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var filter = new AnalyticsDateRangeFilter(startDate, endDate);
            var fin = await service.GetFinanceProfitabilityAnalyticsAsync(ctx.TenantId, ctx.CompanyId, filter, httpContext.RequestAborted);
            return Results.Ok(fin);
        }).RequireAuthorization("analytics.financial.view");

        // 8. Workforce Cost & Payroll
        group.MapGet("/workforce", async (
            [FromQuery] DateOnly? startDate,
            [FromQuery] DateOnly? endDate,
            [FromQuery] Guid? departmentId,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            bool hasSalaryView = await CheckSalaryViewPermissionAsync(httpContext, isDevelopment);
            var filter = new AnalyticsDateRangeFilter(startDate, endDate, DepartmentId: departmentId);
            var wf = await service.GetWorkforceCostAnalyticsAsync(ctx.TenantId, ctx.CompanyId, filter, hasSalaryView, httpContext.RequestAborted);
            return Results.Ok(wf);
        }).RequireAuthorization("analytics.workforce.view");

        // 9. Platform Reconciliation (GL & FIFO layers)
        group.MapGet("/reconciliation", async (
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var rec = await service.GetPlatformReconciliationReportAsync(ctx.TenantId, ctx.CompanyId, httpContext.RequestAborted);
            return Results.Ok(rec);
        }).RequireAuthorization("analytics.financial.view").RequireAuthorization("analytics.supply_chain.view");

        // 10. KPI Definitions
        group.MapGet("/kpis", async (
            [FromQuery] string? category,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetKpiDefinitionsAsync(ctx.TenantId, ctx.CompanyId, category, httpContext.RequestAborted);
            return Results.Ok(list);
        }).RequireAuthorization("analytics.dashboard.view");

        group.MapPost("/kpis", async (
            [FromBody] CreateKpiDefinitionRequest req,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var kpi = await service.CreateKpiDefinitionAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/analytics/kpis/{kpi.Id}", kpi);
        }).RequireAuthorization("analytics.kpi.manage");

        // 11. KPI Snapshots
        group.MapGet("/kpis/snapshots", async (
            [FromQuery] string? kpiCode,
            [FromQuery] DateOnly? fromDate,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetKpiSnapshotsAsync(ctx.TenantId, ctx.CompanyId, kpiCode, fromDate, httpContext.RequestAborted);
            return Results.Ok(list);
        }).RequireAuthorization("analytics.dashboard.view");

        group.MapPost("/kpis/snapshots", async (
            [FromBody] CreateKpiSnapshotRequest req,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var snap = await service.RecordKpiSnapshotAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/analytics/kpis/snapshots/{snap.Id}", snap);
        }).RequireAuthorization("analytics.kpi.manage");

        // 12. Data Export (CSV, TSV, JSON)
        group.MapPost("/export", async (
            [FromBody] ExportRequest req,
            AnalyticsService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var dataPermissions = GetExportDataPermissions(req.ReportType);
            if (dataPermissions.Length == 0)
            {
                return Results.BadRequest(new { code = "UNSUPPORTED_ANALYTICS_EXPORT" });
            }

            foreach (var permission in dataPermissions)
            {
                if (!await CheckAnalyticsPermissionAsync(httpContext, permission, isDevelopment))
                {
                    return Results.Forbid();
                }
            }

            bool hasSalaryView = await CheckSalaryViewPermissionAsync(httpContext, isDevelopment);
            var exp = await service.ExportAnalyticsDataAsync(ctx.TenantId, ctx.CompanyId, req, hasSalaryView, httpContext.RequestAborted);
            return Results.Ok(exp);
        }).RequireAuthorization("analytics.export");

        return endpoints;
    }

    private static string[] GetExportDataPermissions(string? reportType)
    {
        return reportType?.Trim().ToLowerInvariant() switch
        {
            "sales" or "purchasing" or "inventory_ageing" =>
                ["analytics.supply_chain.view"],
            "workforce" =>
                ["analytics.workforce.view"],
            "reconciliation" =>
                ["analytics.financial.view", "analytics.supply_chain.view"],
            _ => Array.Empty<string>(),
        };
    }

    private static async Task<bool> CheckAnalyticsPermissionAsync(
        HttpContext httpContext,
        string permissionCode,
        bool isDevelopment)
    {
        if (isDevelopment)
        {
            return true;
        }

        var authService = httpContext.RequestServices.GetService<IAuthorizationService>();
        if (authService is null)
        {
            return false;
        }

        var authResult = await authService.AuthorizeAsync(httpContext.User, permissionCode);
        return authResult.Succeeded;
    }

    private static async Task<bool> CheckSalaryViewPermissionAsync(HttpContext httpContext, bool isDevelopment)
    {
        var authService = httpContext.RequestServices.GetService<IAuthorizationService>();
        if (authService != null)
        {
            var authResult = await authService.AuthorizeAsync(httpContext.User, "payroll.salary.view");
            return authResult.Succeeded;
        }
        return isDevelopment;
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
