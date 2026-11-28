using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Warehouse;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Api.Warehouse;

public static class WarehouseEndpointMappings
{
    public static IEndpointRouteBuilder MapWarehouseEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment)
    {
        var group = endpoints.MapGroup("/api/v1/warehouse").WithTags("Warehouse");
        if (!isDevelopment)
        {
            group.RequireAuthorization();
        }

        // 1. Zones
        group.MapGet("/zones", async (
            [FromQuery] Guid warehouseId,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var list = await service.GetZonesAsync(ctx, warehouseId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapPost("/zones", async (
            [FromQuery] Guid warehouseId,
            [FromBody] CreateZoneRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.CreateZoneAsync(ctx, warehouseId, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Created($"/api/v1/warehouse/zones/{res.Value!.Id}", res.Value) : Results.BadRequest(new { error = res.Error });
        });

        group.MapPut("/zones/{id:guid}", async (
            Guid id,
            [FromBody] UpdateZoneRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.UpdateZoneAsync(ctx, id, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        // 2. Bin Profiles
        group.MapGet("/bins", async (
            [FromQuery] Guid warehouseId,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var list = await service.GetBinProfilesAsync(ctx, warehouseId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapPut("/bins/{id:guid}", async (
            Guid id,
            [FromBody] UpdateBinProfileRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.UpdateBinProfileAsync(ctx, id, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        // 3. Putaway Strategies & Directed Putaway
        group.MapGet("/putaway-strategies", async (
            [FromQuery] Guid warehouseId,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var list = await service.GetPutawayStrategiesAsync(ctx, warehouseId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapPost("/putaway-strategies", async (
            [FromQuery] Guid warehouseId,
            [FromBody] CreatePutawayStrategyRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.CreatePutawayStrategyAsync(ctx, warehouseId, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Created($"/api/v1/warehouse/putaway-strategies/{res.Value!.Id}", res.Value) : Results.BadRequest(new { error = res.Error });
        });

        group.MapGet("/putaway-recommendations", async (
            [FromQuery] Guid warehouseId,
            [FromQuery] Guid variantId,
            [FromQuery] decimal quantity,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var recs = await service.GetPutawayRecommendationsAsync(ctx, warehouseId, variantId, quantity, httpContext.RequestAborted);
            return Results.Ok(recs);
        });

        group.MapGet("/putaway-tasks", async (
            [FromQuery] Guid warehouseId,
            [FromQuery] string? status,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var list = await service.GetPutawayTasksAsync(ctx, warehouseId, status, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapPost("/putaway-tasks", async (
            [FromQuery] Guid warehouseId,
            [FromBody] CreatePutawayTaskRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.CreatePutawayTaskAsync(ctx, warehouseId, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Created($"/api/v1/warehouse/putaway-tasks/{res.Value!.Id}", res.Value) : Results.BadRequest(new { error = res.Error });
        });

        group.MapPost("/putaway-tasks/{id:guid}/confirm", async (
            Guid id,
            [FromBody] ConfirmPutawayRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.ConfirmPutawayTaskAsync(ctx, id, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        // 4. Barcodes & Scanning
        group.MapGet("/barcodes", async (
            [FromQuery] string? entityType,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            if (!string.IsNullOrWhiteSpace(entityType))
            {
                var trimmed = entityType.Trim();
                if (trimmed.Length > 50 || !System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[a-zA-Z0-9_-]+$"))
                {
                    return Results.BadRequest(new { error = "INVALID_ENTITY_TYPE", message = "The entityType parameter must be alphanumeric and up to 50 characters." });
                }
            }

            var ctx = ExtractContext(httpContext);
            var list = await service.GetBarcodesAsync(ctx, entityType, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapPost("/barcodes", async (
            [FromBody] CreateBarcodeRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.CreateBarcodeAsync(ctx, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Created($"/api/v1/warehouse/barcodes/{res.Value!.Id}", res.Value) : Results.BadRequest(new { error = res.Error });
        });

        group.MapPost("/scan", async (
            [FromBody] BarcodeScanRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.ProcessBarcodeScanAsync(ctx, req, httpContext.RequestAborted);
            return Results.Ok(res);
        });

        group.MapGet("/scan-logs", async (
            [FromQuery] int? limit,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var list = await service.GetScanAuditLogsAsync(ctx, limit ?? 50, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        // 5. Picking Waves
        group.MapGet("/picking-waves", async (
            [FromQuery] Guid warehouseId,
            [FromQuery] string? status,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var list = await service.GetPickingWavesAsync(ctx, warehouseId, status, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapGet("/picking-waves/{id:guid}", async (
            Guid id,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var wave = await service.GetPickingWaveByIdAsync(ctx, id, httpContext.RequestAborted);
            return wave is null ? Results.NotFound() : Results.Ok(wave);
        });

        group.MapPost("/picking-waves", async (
            [FromQuery] Guid warehouseId,
            [FromBody] CreatePickingWaveRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.CreatePickingWaveAsync(ctx, warehouseId, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Created($"/api/v1/warehouse/picking-waves/{res.Value!.Id}", res.Value) : Results.BadRequest(new { error = res.Error });
        });

        group.MapPost("/picking-waves/{waveId:guid}/items/{itemId:guid}/confirm", async (
            Guid waveId,
            Guid itemId,
            [FromBody] ConfirmPickItemRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.ConfirmPickItemAsync(ctx, waveId, itemId, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        group.MapPost("/picking-waves/{id:guid}/release", async (
            Guid id,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.ReleasePickingWaveAsync(ctx, id, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        // 6. Shipment Packages
        group.MapGet("/packages", async (
            [FromQuery] Guid warehouseId,
            [FromQuery] string? status,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var list = await service.GetShipmentPackagesAsync(ctx, warehouseId, status, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapPost("/packages", async (
            [FromQuery] Guid warehouseId,
            [FromBody] CreatePackageRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.CreateShipmentPackageAsync(ctx, warehouseId, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Created($"/api/v1/warehouse/packages/{res.Value!.Id}", res.Value) : Results.BadRequest(new { error = res.Error });
        });

        group.MapPost("/packages/{id:guid}/seal", async (
            Guid id,
            [FromBody] SealPackageRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.SealShipmentPackageAsync(ctx, id, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        group.MapPost("/packages/{id:guid}/dispatch", async (
            Guid id,
            [FromBody] DispatchShipmentRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.DispatchShipmentAsync(ctx, id, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        // 7. Replenishment
        group.MapGet("/replenishment-rules", async (
            [FromQuery] Guid warehouseId,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var list = await service.GetReplenishmentRulesAsync(ctx, warehouseId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapPost("/replenishment-rules", async (
            [FromQuery] Guid warehouseId,
            [FromBody] CreateReplenishmentRuleRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.CreateReplenishmentRuleAsync(ctx, warehouseId, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Created($"/api/v1/warehouse/replenishment-rules/{res.Value!.Id}", res.Value) : Results.BadRequest(new { error = res.Error });
        });

        group.MapGet("/replenishment-recommendations", async (
            [FromQuery] Guid warehouseId,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var list = await service.GetReplenishmentRecommendationsAsync(ctx, warehouseId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapGet("/replenishment-tasks", async (
            [FromQuery] Guid warehouseId,
            [FromQuery] string? status,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var list = await service.GetReplenishmentTasksAsync(ctx, warehouseId, status, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapPost("/replenishment-tasks", async (
            [FromQuery] Guid warehouseId,
            [FromBody] CreateReplenishmentTaskRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.CreateReplenishmentTaskAsync(ctx, warehouseId, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Created($"/api/v1/warehouse/replenishment-tasks/{res.Value!.Id}", res.Value) : Results.BadRequest(new { error = res.Error });
        });

        group.MapPost("/replenishment-tasks/{id:guid}/execute", async (
            Guid id,
            [FromQuery] decimal quantity,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.ExecuteReplenishmentTaskAsync(ctx, id, quantity, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        // 8. Bin Transfers
        group.MapPost("/transfers", async (
            [FromBody] BinStockTransferRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.ExecuteBinTransferAsync(ctx, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        // 9. Quarantine
        group.MapGet("/quarantine", async (
            [FromQuery] Guid warehouseId,
            [FromQuery] string? disposition,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var list = await service.GetQuarantineRecordsAsync(ctx, warehouseId, disposition, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        group.MapPost("/quarantine", async (
            [FromQuery] Guid warehouseId,
            [FromBody] CreateQuarantineRecordRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.CreateQuarantineRecordAsync(ctx, warehouseId, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Created($"/api/v1/warehouse/quarantine/{res.Value!.Id}", res.Value) : Results.BadRequest(new { error = res.Error });
        });

        group.MapPost("/quarantine/{id:guid}/dispose", async (
            Guid id,
            [FromBody] DisposeQuarantineRequest req,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var res = await service.DisposeQuarantineRecordAsync(ctx, id, req, httpContext.RequestAborted);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        // 10. Metrics
        group.MapGet("/metrics", async (
            [FromQuery] Guid warehouseId,
            WarehouseService service,
            HttpContext httpContext) =>
        {
            var ctx = ExtractContext(httpContext);
            var report = await service.GetWarehouseMetricsReportAsync(ctx, warehouseId, httpContext.RequestAborted);
            return Results.Ok(report);
        });

        return endpoints;
    }

    private static TenantContext ExtractContext(HttpContext httpContext)
    {
        var accessor = httpContext.RequestServices.GetService<ITenantContextAccessor>();
        if (accessor?.Current is { } current && current.TenantId != Guid.Empty)
        {
            return current;
        }

        var tenantIdStr = httpContext.Request.Headers["X-Tenant-Id"].ToString();
        var companyIdStr = httpContext.Request.Headers["X-Company-Id"].ToString();

        var tenantId = Guid.TryParse(tenantIdStr, out var tid) ? tid : Guid.Empty;
        var companyId = Guid.TryParse(companyIdStr, out var cid) ? cid : Guid.Empty;

        return new TenantContext(tenantId, companyId, Guid.Empty);
    }
}
