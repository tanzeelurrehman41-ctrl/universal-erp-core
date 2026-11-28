using Npgsql;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Manufacturing;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Manufacturing;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class Phase9PostgresManufacturingIntegrationTests
{
    private static readonly string? StagingConnectionString =
        Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
        ?? "Host=127.0.0.1;Port=5433;Database=universal_erp_phase5_dev;Username=erp_dev;Password=local_dev_change_me;Include Error Detail=true;";

    private sealed class TestTenantContextAccessor(TenantContext ctx) : ITenantContextAccessor
    {
        public TenantContext Current => ctx;
    }

    [Fact]
    public async Task Phase9_FullManufacturingLifecycle_IntegrationTest()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var tenantContext = new TenantContext(
            TenantId: tenantId,
            CompanyId: companyId,
            UserId: userId);

        var dataSource = NpgsqlDataSource.Create(StagingConnectionString!);
        var accessor = new TestTenantContextAccessor(tenantContext);
        var repo = new PostgresManufacturingRepository(dataSource, accessor);
        var service = new ManufacturingService(repo, accessor);
        var ct = CancellationToken.None;

        var warehouseId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var quarBinId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var rawProductId = Guid.NewGuid();
        var rawVariantId = Guid.NewGuid();
        var fgProductId = Guid.NewGuid();
        var fgVariantId = Guid.NewGuid();
        var rawLayerId = Guid.NewGuid();

        // 1. Seed Foundation Data
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var tx = await conn.BeginTransactionAsync(ct);

            await using (var setCmd = conn.CreateCommand())
            {
                setCmd.Transaction = tx;
                setCmd.CommandText = $"SET LOCAL app.tenant_id = '{tenantId}'; SET LOCAL app.company_id = '{companyId}';";
                await setCmd.ExecuteNonQueryAsync(ct);
            }

            const string seedSql = """
                INSERT INTO platform.tenants (id, tenant_code, display_name, status, default_country_code, default_currency_code, default_timezone)
                VALUES (@tid, @tcode, 'Mfg Test Tenant', 'active', 'PK', 'PKR', 'Asia/Karachi')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO organization.companies (tenant_id, id, company_code, name, country_code, base_currency_code, timezone, status)
                VALUES (@tid, @cid, @ccode, 'Mfg Test Company', 'PK', 'PKR', 'Asia/Karachi', 'active')
                ON CONFLICT (tenant_id, id) DO NOTHING;

                INSERT INTO identity.users (id, email_normalized, display_name, status)
                VALUES (@uid, @uemail, 'Mfg Tester', 'active')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO erp_inventory.stock_status (tenant_id, status_code, description, is_reservable, is_pickable)
                VALUES
                    (@tid, 'available', 'Sellable Available Stock', true, true),
                    (@tid, 'quarantine', 'Quarantined Stock', false, false)
                ON CONFLICT (tenant_id, status_code) DO NOTHING;

                INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name, is_active)
                VALUES (@tid, @cid, @wh, 'WH-MFG-01', 'Manufacturing Warehouse', true)
                ON CONFLICT (tenant_id, company_id, warehouse_id) DO NOTHING;

                INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, bin_id, warehouse_id, code, bin_type, is_active)
                VALUES
                    (@tid, @cid, @bin, @wh, 'BIN-MFG-01', 'storage', true),
                    (@tid, @cid, @qbin, @wh, 'BIN-QUAR-01', 'storage', true)
                ON CONFLICT (tenant_id, company_id, bin_id) DO NOTHING;

                INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension, decimal_places)
                VALUES (@tid, @uom, 'EA', 'Each', 'count', 0)
                ON CONFLICT (tenant_id, uom_id) DO NOTHING;

                -- Raw Material Product & Variant
                INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, product_type, is_active)
                VALUES (@tid, @rawPid, 'PRD-RAW-01', 'Raw Material Steel Rod', 'stock', true)
                ON CONFLICT (tenant_id, product_id) DO NOTHING;

                INSERT INTO erp_inventory.product_variant (tenant_id, variant_id, product_id, sku, variant_name, base_uom_id, costing_method)
                VALUES (@tid, @rawVid, @rawPid, 'SKU-RAW-01', 'Steel Rod 10mm', @uom, 'fifo')
                ON CONFLICT (tenant_id, variant_id) DO NOTHING;

                -- Finished Good Product & Variant
                INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, product_type, is_active)
                VALUES (@tid, @fgPid, 'PRD-FG-01', 'Hydraulic Pump Assembly', 'stock', true)
                ON CONFLICT (tenant_id, product_id) DO NOTHING;

                INSERT INTO erp_inventory.product_variant (tenant_id, variant_id, product_id, sku, variant_name, base_uom_id, costing_method)
                VALUES (@tid, @fgVid, @fgPid, 'SKU-FG-01', 'Hydraulic Pump Standard', @uom, 'fifo')
                ON CONFLICT (tenant_id, variant_id) DO NOTHING;

                -- Initial FIFO cost layer for Raw Material: 30 EA @ 100 PKR in BIN-MFG-01
                INSERT INTO erp_inventory.inventory_cost_layer
                    (tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id, status_code, source_document_type, received_at, original_qty, remaining_qty, unit_cost, currency_code)
                VALUES
                    (@tid, @cid, @layerId, @rawVid, @wh, @bin, 'available', 'opening_balance', now() - INTERVAL '1 day', 30, 30, 100.00, 'PKR')
                ON CONFLICT (tenant_id, company_id, layer_id) DO NOTHING;

                -- Seed Accounts
                INSERT INTO finance.accounts (tenant_id, company_id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active)
                VALUES
                    (@tid, @cid, '1200', 'Raw Material Inventory', 'asset', 'debit', 'PKR', true, false, true),
                    (@tid, @cid, '1210', 'Finished Goods Inventory', 'asset', 'debit', 'PKR', true, false, true),
                    (@tid, @cid, '1250', 'Work-in-Process (WIP) Inventory', 'asset', 'debit', 'PKR', true, false, true),
                    (@tid, @cid, '2050', 'Direct Labor Clearing', 'liability', 'credit', 'PKR', false, true, true),
                    (@tid, @cid, '6060', 'Applied Manufacturing Overhead', 'expense', 'credit', 'PKR', false, true, true),
                    (@tid, @cid, '5020', 'Scrap & Production Loss', 'expense', 'debit', 'PKR', false, true, true),
                    (@tid, @cid, '5030', 'Manufacturing Cost Variance', 'expense', 'debit', 'PKR', false, true, true)
                ON CONFLICT (tenant_id, company_id, account_code) DO NOTHING;
                """;

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = seedSql;
            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("cid", companyId);
            cmd.Parameters.AddWithValue("tcode", $"T-{tenantId.ToString("N")[..8]}");
            cmd.Parameters.AddWithValue("ccode", $"C-{companyId.ToString("N")[..8]}");
            cmd.Parameters.AddWithValue("uid", userId);
            cmd.Parameters.AddWithValue("uemail", $"mfg_{tenantId.ToString("N")[..6]}@example.com");
            cmd.Parameters.AddWithValue("wh", warehouseId);
            cmd.Parameters.AddWithValue("bin", binId);
            cmd.Parameters.AddWithValue("qbin", quarBinId);
            cmd.Parameters.AddWithValue("uom", uomId);
            cmd.Parameters.AddWithValue("rawPid", rawProductId);
            cmd.Parameters.AddWithValue("rawVid", rawVariantId);
            cmd.Parameters.AddWithValue("fgPid", fgProductId);
            cmd.Parameters.AddWithValue("fgVid", fgVariantId);
            cmd.Parameters.AddWithValue("layerId", rawLayerId);
            await cmd.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }

        // 2. Create Work Center
        var wcResult = await service.CreateWorkCenterAsync(new CreateWorkCenterRequest(
            Code: "WC-ASSY-01",
            Name: "Assembly Station 1",
            HourlyLaborRate: 500m,
            HourlyOverheadRate: 200m,
            DailyCapacityHours: 8m,
            EfficiencyPercent: 100m), ct);
        Assert.True(wcResult.IsSuccess);
        var workCenter = wcResult.Value!;

        // 3. Create Routing
        var routingResult = await service.CreateRoutingAsync(new CreateRoutingRequest(
            RoutingNumber: "ROUT-PUMP-01",
            Name: "Pump Assembly Routing",
            ProductVariantId: fgVariantId,
            Description: "Standard routing for hydraulic pump",
            Operations: new[]
            {
                new CreateRoutingOperationRequest(
                    OperationSequence: 10,
                    OperationName: "Component Machining",
                    WorkCenterId: workCenter.Id,
                    SetupTimeMinutes: 30m,
                    RunTimeMinutesPerUnit: 15m),
                new CreateRoutingOperationRequest(
                    OperationSequence: 20,
                    OperationName: "Final Assembly & Testing",
                    WorkCenterId: workCenter.Id,
                    SetupTimeMinutes: 30m,
                    RunTimeMinutesPerUnit: 20m)
            }), ct);
        Assert.True(routingResult.IsSuccess);
        var routing = routingResult.Value!;

        // 4. Create BOM: 2 EA Raw Material per Pump, 0% scrap
        var bomResult = await service.CreateBomAsync(new CreateBomRequest(
            BomNumber: "BOM-PUMP-01",
            ProductVariantId: fgVariantId,
            Name: "Pump Bill of Materials",
            BaseQuantity: 1m,
            UomId: uomId,
            YieldPercent: 100m,
            RevisionCode: "A",
            EffectiveFrom: DateOnly.FromDateTime(DateTime.UtcNow),
            ChangeReason: "Initial release",
            Items: new[]
            {
                new CreateBomItemRequest(
                    LineNumber: 1,
                    ComponentVariantId: rawVariantId,
                    ComponentType: "raw_material",
                    QuantityPer: 2m,
                    ScrapPercentage: 0m)
            }), ct);
        Assert.True(bomResult.IsSuccess);
        var bom = bomResult.Value!;

        // 5. Create Work Order for 10 Pumps
        var woResult = await service.CreateWorkOrderAsync(new CreateWorkOrderRequest(
            WorkOrderNumber: "WO-2026-001",
            ProductVariantId: fgVariantId,
            BomId: bom.Id,
            BomRevisionId: null,
            RoutingId: routing.Id,
            TargetWarehouseId: warehouseId,
            TargetBinId: binId,
            PlannedQuantity: 10m,
            StartDate: DateOnly.FromDateTime(DateTime.UtcNow),
            DueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))), ct);
        Assert.True(woResult.IsSuccess);
        var wo = woResult.Value!;
        Assert.Equal("planned", wo.Status);
        Assert.Single(wo.Materials!);
        Assert.Equal(20m, wo.Materials![0].RequiredQuantity); // 10 * 2 = 20 EA

        // 6. Release Work Order -> Stock reservation created
        var releaseResult = await service.ReleaseWorkOrderAsync(wo.Id, ct);
        Assert.True(releaseResult.IsSuccess);
        var releasedWo = releaseResult.Value!;
        Assert.Equal("released", releasedWo.Status);
        Assert.NotNull(releasedWo.Materials![0].ReservationId);

        // 7. Issue Material: 20 EA from BIN-MFG-01
        var issueResult = await service.IssueMaterialAsync(wo.Id, new IssueMaterialRequest(
            LineNumber: 1,
            ComponentVariantId: rawVariantId,
            Quantity: 20m,
            WarehouseId: warehouseId,
            BinId: binId), ct);
        Assert.True(issueResult.IsSuccess);
        var issue = issueResult.Value!;
        Assert.Equal(20m, issue.IssuedQuantity);
        Assert.Equal(2000m, issue.ConsumedCost); // 20 * 100 PKR = 2,000 PKR

        // Verify remaining qty on raw material layer is now 10 EA (30 - 20)
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT remaining_qty FROM erp_inventory.inventory_cost_layer WHERE layer_id = @lid";
            cmd.Parameters.AddWithValue("lid", rawLayerId);
            var rem = (decimal)(await cmd.ExecuteScalarAsync(ct))!;
            Assert.Equal(10m, rem);
        }

        // 8. Report Operation Progress: 60 min setup + 120 min run = 3 hours
        // Labor: 3 * 500 = 1,500 PKR, Overhead: 3 * 200 = 600 PKR -> Total: 2,100 PKR
        var progResult = await service.ReportOperationProgressAsync(wo.Id, new ReportOperationProgressRequest(
            OperationSequence: 10,
            ActualSetupMinutes: 60m,
            ActualRunMinutes: 120m,
            CompletedQuantity: 10m,
            ScrappedQuantity: 0m), ct);
        Assert.True(progResult.IsSuccess);
        Assert.Equal(1500m, progResult.Value!.AllocatedLaborCost);
        Assert.Equal(600m, progResult.Value.AllocatedOverheadCost);

        // 9. Record QC Inspection: 9 Accepted, 1 Rejected/Quarantined
        var qcResult = await service.RecordProductionInspectionAsync(wo.Id, new CreateProductionInspectionRequest(
            InspectionNumber: "QC-2026-001",
            InspectionType: "finished_goods",
            InspectedQuantity: 10m,
            AcceptedQuantity: 9m,
            RejectedQuantity: 1m,
            QuarantineBinId: quarBinId,
            Disposition: "quarantine",
            Notes: "1 pump with minor seal defect quarantined"), ct);
        Assert.True(qcResult.IsSuccess);

        // 10. Complete Work Order: 9 Accepted, 1 Scrapped
        // Total costs: 2,000 (material) + 2,100 (labor/overhead) = 4,100 PKR
        // Total units: 10 -> Unit cost = 410 PKR/unit
        // FG valuation: 9 * 410 = 3,690 PKR
        // Scrap expense: 1 * 410 = 410 PKR
        var compResult = await service.CompleteWorkOrderAsync(wo.Id, new CompleteWorkOrderRequest(
            AcceptedQuantity: 9m,
            ScrappedQuantity: 1m,
            Notes: "Finished goods completion with 9 accepted"), ct);
        Assert.True(compResult.IsSuccess);
        var comp = compResult.Value!;
        Assert.Equal(9m, comp.AcceptedQuantity);
        Assert.Equal(1m, comp.ScrappedQuantity);
        Assert.Equal(410.0000m, comp.UnitCostFinal);
        Assert.Equal(4100m, comp.TotalProductionCost);

        // 11. Verify Finished Goods FIFO layer was created with 9 EA and 410 PKR unit cost
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT remaining_qty, unit_cost FROM erp_inventory.inventory_cost_layer WHERE layer_id = @lid";
            cmd.Parameters.AddWithValue("lid", comp.FinishedGoodsLayerId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            Assert.True(await reader.ReadAsync(ct));
            Assert.Equal(9m, reader.GetDecimal(0));
            Assert.Equal(410.0000m, reader.GetDecimal(1));
        }

        // 12. Verify Genealogy Traceability
        var genealogy = await service.GetGenealogyAsync(wo.Id, ct);
        Assert.NotNull(genealogy);
        Assert.Equal(wo.Id, genealogy.WorkOrderId);
        Assert.Single(genealogy.ConsumedComponents);
        var compNode = genealogy.ConsumedComponents[0];
        Assert.Equal(rawVariantId, compNode.ComponentVariantId);
        Assert.Equal(20m, compNode.ConsumedQuantity);
        Assert.Equal(2000m, compNode.ConsumedCost);
        Assert.Single(compNode.LayerSources);
        Assert.Equal(rawLayerId, compNode.LayerSources[0].LayerId);

        // 13. Verify WIP & Variance Report
        var wipReport = await service.GetWipVarianceReportAsync(ct);
        Assert.NotNull(wipReport);
    }
}
