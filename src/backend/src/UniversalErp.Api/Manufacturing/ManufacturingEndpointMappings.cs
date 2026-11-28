using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UniversalErp.Application.Manufacturing;

namespace UniversalErp.Api.Manufacturing;

public static class ManufacturingEndpointMappings
{
    public static IEndpointRouteBuilder MapManufacturingEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool isDevelopment)
    {
        var group = endpoints.MapGroup("/api/v1/manufacturing");

        if (!isDevelopment)
        {
            group.RequireAuthorization();
        }

        // ── 1. Work Centers ───────────────────────────────────────────────────

        var listWorkCenters = group.MapGet("/work-centers", async (
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var list = await service.ListWorkCentersAsync(ct);
            return Results.Ok(list);
        });

        var createWorkCenter = group.MapPost("/work-centers", async (
            CreateWorkCenterRequest request,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.CreateWorkCenterAsync(request, ct);
            return res.IsSuccess
                ? Results.Created($"/api/v1/manufacturing/work-centers/{res.Value!.Id}", res.Value)
                : Results.BadRequest(new { error = res.Error });
        });

        var getWorkCenter = group.MapGet("/work-centers/{id:guid}", async (
            Guid id,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var wc = await service.GetWorkCenterByIdAsync(id, ct);
            return wc is null ? Results.NotFound() : Results.Ok(wc);
        });

        // ── 2. Routings ────────────────────────────────────────────────────────

        var listRoutings = group.MapGet("/routings", async (
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var list = await service.ListRoutingsAsync(ct);
            return Results.Ok(list);
        });

        var createRouting = group.MapPost("/routings", async (
            CreateRoutingRequest request,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.CreateRoutingAsync(request, ct);
            return res.IsSuccess
                ? Results.Created($"/api/v1/manufacturing/routings/{res.Value!.Id}", res.Value)
                : Results.BadRequest(new { error = res.Error });
        });

        var getRouting = group.MapGet("/routings/{id:guid}", async (
            Guid id,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var routing = await service.GetRoutingByIdAsync(id, ct);
            return routing is null ? Results.NotFound() : Results.Ok(routing);
        });

        // ── 3. BOMs & Revisions ────────────────────────────────────────────────

        var listBoms = group.MapGet("/boms", async (
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var list = await service.ListBomsAsync(ct);
            return Results.Ok(list);
        });

        var createBom = group.MapPost("/boms", async (
            CreateBomRequest request,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.CreateBomAsync(request, ct);
            return res.IsSuccess
                ? Results.Created($"/api/v1/manufacturing/boms/{res.Value!.Id}", res.Value)
                : Results.BadRequest(new { error = res.Error });
        });

        var getBom = group.MapGet("/boms/{id:guid}", async (
            Guid id,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var bom = await service.GetBomByIdAsync(id, ct);
            return bom is null ? Results.NotFound() : Results.Ok(bom);
        });

        var createBomRevision = group.MapPost("/boms/{id:guid}/revisions", async (
            Guid id,
            CreateBomRevisionRequest request,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.CreateBomRevisionAsync(id, request, ct);
            return res.IsSuccess
                ? Results.Created($"/api/v1/manufacturing/boms/{id}/revisions/{res.Value!.Id}", res.Value)
                : Results.BadRequest(new { error = res.Error });
        });

        var explodeBom = group.MapGet("/boms/explode", async (
            Guid variantId,
            decimal quantity,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.ExplodeBomAsync(variantId, quantity, ct);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        // ── 4. Substitutions ───────────────────────────────────────────────────

        var listSubstitutions = group.MapGet("/substitutions", async (
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var list = await service.ListSubstitutionsAsync(ct);
            return Results.Ok(list);
        });

        var createSubstitution = group.MapPost("/substitutions", async (
            CreateMaterialSubstitutionRequest request,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.CreateSubstitutionAsync(request, ct);
            return res.IsSuccess
                ? Results.Created($"/api/v1/manufacturing/substitutions/{res.Value!.Id}", res.Value)
                : Results.BadRequest(new { error = res.Error });
        });

        // ── 5. Work Orders ─────────────────────────────────────────────────────

        var listWorkOrders = group.MapGet("/work-orders", async (
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var list = await service.ListWorkOrdersAsync(ct);
            return Results.Ok(list);
        });

        var createWorkOrder = group.MapPost("/work-orders", async (
            CreateWorkOrderRequest request,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.CreateWorkOrderAsync(request, ct);
            return res.IsSuccess
                ? Results.Created($"/api/v1/manufacturing/work-orders/{res.Value!.Id}", res.Value)
                : Results.BadRequest(new { error = res.Error });
        });

        var getWorkOrder = group.MapGet("/work-orders/{id:guid}", async (
            Guid id,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var wo = await service.GetWorkOrderByIdAsync(id, ct);
            return wo is null ? Results.NotFound() : Results.Ok(wo);
        });

        var releaseWorkOrder = group.MapPost("/work-orders/{id:guid}/release", async (
            Guid id,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.ReleaseWorkOrderAsync(id, ct);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        var issueMaterial = group.MapPost("/work-orders/{id:guid}/issue-material", async (
            Guid id,
            IssueMaterialRequest request,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.IssueMaterialAsync(id, request, ct);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        var returnMaterial = group.MapPost("/work-orders/{id:guid}/return-material", async (
            Guid id,
            ReturnMaterialRequest request,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.ReturnMaterialAsync(id, request, ct);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        var reportProgress = group.MapPost("/work-orders/{id:guid}/report-progress", async (
            Guid id,
            ReportOperationProgressRequest request,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.ReportOperationProgressAsync(id, request, ct);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        var recordInspection = group.MapPost("/work-orders/{id:guid}/inspections", async (
            Guid id,
            CreateProductionInspectionRequest request,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.RecordProductionInspectionAsync(id, request, ct);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        var completeWorkOrder = group.MapPost("/work-orders/{id:guid}/complete", async (
            Guid id,
            CompleteWorkOrderRequest request,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var res = await service.CompleteWorkOrderAsync(id, request, ct);
            return res.IsSuccess ? Results.Ok(res.Value) : Results.BadRequest(new { error = res.Error });
        });

        var getGenealogy = group.MapGet("/work-orders/{id:guid}/genealogy", async (
            Guid id,
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var gen = await service.GetGenealogyAsync(id, ct);
            return gen is null ? Results.NotFound() : Results.Ok(gen);
        });

        var getWipVariance = group.MapGet("/reports/wip-variance", async (
            ManufacturingService service,
            CancellationToken ct) =>
        {
            var rep = await service.GetWipVarianceReportAsync(ct);
            return Results.Ok(rep);
        });

        // ── Authorization Policies in Production ──────────────────────────────
        if (!isDevelopment)
        {
            listWorkCenters.RequireAuthorization("mfg.work_center.manage");
            createWorkCenter.RequireAuthorization("mfg.work_center.manage");
            getWorkCenter.RequireAuthorization("mfg.work_center.manage");

            listRoutings.RequireAuthorization("mfg.routing.manage");
            createRouting.RequireAuthorization("mfg.routing.manage");
            getRouting.RequireAuthorization("mfg.routing.manage");

            listBoms.RequireAuthorization("mfg.bom.view");
            getBom.RequireAuthorization("mfg.bom.view");
            explodeBom.RequireAuthorization("mfg.bom.view");
            createBom.RequireAuthorization("mfg.bom.manage");
            createBomRevision.RequireAuthorization("mfg.bom.manage");

            listSubstitutions.RequireAuthorization("mfg.bom.view");
            createSubstitution.RequireAuthorization("mfg.bom.manage");

            listWorkOrders.RequireAuthorization("mfg.work_order.view");
            getWorkOrder.RequireAuthorization("mfg.work_order.view");
            createWorkOrder.RequireAuthorization("mfg.work_order.create");
            releaseWorkOrder.RequireAuthorization("mfg.work_order.approve");
            issueMaterial.RequireAuthorization("mfg.material.issue");
            returnMaterial.RequireAuthorization("mfg.material.return");
            reportProgress.RequireAuthorization("mfg.operation.report");
            recordInspection.RequireAuthorization("mfg.qc.inspect");
            completeWorkOrder.RequireAuthorization("mfg.work_order.complete");
            getGenealogy.RequireAuthorization("mfg.reports.view");
            getWipVariance.RequireAuthorization("mfg.reports.view");
        }

        return endpoints;
    }
}
