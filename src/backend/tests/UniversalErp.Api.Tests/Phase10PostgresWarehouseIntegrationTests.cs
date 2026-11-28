using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Warehouse;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Warehouse;
using Xunit;

namespace UniversalErp.Api.Tests;

public class Phase10PostgresWarehouseIntegrationTests
{
    private static readonly string? StagingConnectionString =
        Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
        ?? "Host=127.0.0.1;Port=5433;Database=universal_erp_phase5_dev;Username=erp_dev;Password=local_dev_change_me;Include Error Detail=true;";

    [Fact]
    public async Task Phase10_FullWarehouseAndLogisticsLifecycle_IntegrationTest()
    {
        if (string.IsNullOrWhiteSpace(StagingConnectionString)) return;

        var ct = CancellationToken.None;
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantContext = new TenantContext(tenantId, companyId, userId);

        var dataSource = NpgsqlDataSource.Create(StagingConnectionString);
        var repo = new PostgresWarehouseRepository(dataSource);
        var service = new WarehouseService(repo);

        // 1. Seed Foundation Data
        var warehouseId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var binRecId = Guid.NewGuid();
        var binResId = Guid.NewGuid();
        var binPickId = Guid.NewGuid();
        var binDispId = Guid.NewGuid();
        var binQuarId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = $"SELECT set_config('app.tenant_id', '{tenantId}', true), set_config('app.company_id', '{companyId}', true);";
            await cmd.ExecuteNonQueryAsync(ct);

            var layerId = Guid.NewGuid();
            var movId = Guid.NewGuid();
            var balId = Guid.NewGuid();
            var tcode = $"T-{tenantId.ToString("N")[..8]}";
            var ccode = $"C-{companyId.ToString("N")[..8]}";
            var uemail = $"wh_{tenantId.ToString("N")[..6]}@example.com";

            cmd.CommandText = """
                INSERT INTO platform.tenants (id, tenant_code, display_name, status, default_country_code, default_currency_code, default_timezone)
                VALUES (@tId, @tcode, 'Phase 10 Tenant', 'active', 'PK', 'PKR', 'Asia/Karachi')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO organization.companies (tenant_id, id, company_code, name, country_code, base_currency_code, timezone, status)
                VALUES (@tId, @cId, @ccode, 'Phase 10 Logistics Corp', 'PK', 'PKR', 'Asia/Karachi', 'active')
                ON CONFLICT (tenant_id, id) DO NOTHING;

                INSERT INTO identity.users (id, email_normalized, display_name, status)
                VALUES (@uId, @uemail, 'Warehouse Manager', 'active')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO erp_inventory.stock_status (tenant_id, status_code, description, is_reservable, is_pickable)
                VALUES
                    (@tId, 'available', 'Sellable Available Stock', true, true),
                    (@tId, 'quarantine', 'Quarantined Stock', false, false)
                ON CONFLICT (tenant_id, status_code) DO NOTHING;

                INSERT INTO erp_inventory.warehouse (tenant_id, company_id, warehouse_id, code, name, is_active)
                VALUES (@tId, @cId, @whId, 'WH-PHASE10', 'Primary Distribution Center', true)
                ON CONFLICT (tenant_id, company_id, warehouse_id) DO NOTHING;

                INSERT INTO erp_inventory.stock_bin (tenant_id, company_id, warehouse_id, bin_id, code, bin_type, is_active)
                VALUES
                    (@tId, @cId, @whId, @bRec, 'REC-01', 'receiving', true),
                    (@tId, @cId, @whId, @bRes, 'RES-01', 'storage', true),
                    (@tId, @cId, @whId, @bPick, 'PICK-01', 'storage', true),
                    (@tId, @cId, @whId, @bDisp, 'DISP-01', 'dispatch', true),
                    (@tId, @cId, @whId, @bQuar, 'QUAR-01', 'storage', true)
                ON CONFLICT (tenant_id, company_id, bin_id) DO NOTHING;

                INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension, decimal_places)
                VALUES (@tId, @uomId, 'EA', 'Each', 'count', 0)
                ON CONFLICT (tenant_id, uom_id) DO NOTHING;

                INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, product_type, is_active)
                VALUES (@tId, @prodId, 'PROD-LOG-10', 'High Velocity Logistics Item', 'stock', true)
                ON CONFLICT (tenant_id, product_id) DO NOTHING;

                INSERT INTO erp_inventory.product_variant (tenant_id, variant_id, product_id, sku, variant_name, base_uom_id, costing_method)
                VALUES (@tId, @varId, @prodId, 'VAR-WH-101', 'Standard Variant', @uomId, 'fifo')
                ON CONFLICT (tenant_id, variant_id) DO NOTHING;

                -- Initial movement for GRN
                INSERT INTO erp_inventory.inventory_movement
                    (tenant_id, company_id, movement_id, client_transaction_id, movement_type, source_document_type, source_document_id, occurred_at, created_by)
                VALUES (@tId, @cId, @movId, @movId, 'receipt', 'grn', @movId, now() - INTERVAL '2 days', @uId)
                ON CONFLICT (tenant_id, company_id, movement_id) DO NOTHING;

                -- Initial FIFO cost layer in REC-01
                INSERT INTO erp_inventory.inventory_cost_layer
                    (tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id, status_code,
                     source_document_type, source_document_id, source_line_number, source_movement_id,
                     received_at, original_qty, remaining_qty, unit_cost, currency_code)
                VALUES
                    (@tId, @cId, @layerId, @varId, @whId, @bRec, 'available',
                     'grn', @movId, 1, @movId,
                     now() - interval '2 days', 50, 50, 100.00, 'PKR')
                ON CONFLICT (tenant_id, company_id, layer_id) DO NOTHING;

                -- Initial stock balance
                INSERT INTO erp_inventory.stock_balance
                    (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, on_hand_qty, balance_id)
                VALUES (@tId, @cId, @varId, @whId, @bRec, 'available', 50, @balId)
                ON CONFLICT (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, (COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid)))
                DO UPDATE SET on_hand_qty = 50, updated_at = now();
                """;

            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("cId", companyId);
            cmd.Parameters.AddWithValue("tcode", tcode);
            cmd.Parameters.AddWithValue("ccode", ccode);
            cmd.Parameters.AddWithValue("uemail", uemail);
            cmd.Parameters.AddWithValue("uId", userId);
            cmd.Parameters.AddWithValue("whId", warehouseId);
            cmd.Parameters.AddWithValue("bRec", binRecId);
            cmd.Parameters.AddWithValue("bRes", binResId);
            cmd.Parameters.AddWithValue("bPick", binPickId);
            cmd.Parameters.AddWithValue("bDisp", binDispId);
            cmd.Parameters.AddWithValue("bQuar", binQuarId);
            cmd.Parameters.AddWithValue("uomId", uomId);
            cmd.Parameters.AddWithValue("prodId", productId);
            cmd.Parameters.AddWithValue("varId", variantId);
            cmd.Parameters.AddWithValue("movId", movId);
            cmd.Parameters.AddWithValue("layerId", layerId);
            cmd.Parameters.AddWithValue("balId", balId);
            await cmd.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }

        // 2. Create Warehouse Zones
        var zoneRec = await service.CreateZoneAsync(tenantContext, warehouseId, new CreateZoneRequest("ZONE-REC", "Receiving Dock", "receiving"));
        Assert.True(zoneRec.IsSuccess);

        var zoneRes = await service.CreateZoneAsync(tenantContext, warehouseId, new CreateZoneRequest("ZONE-RES", "Bulk Reserve High Bay", "bulk_reserve"));
        Assert.True(zoneRes.IsSuccess);

        var zonePick = await service.CreateZoneAsync(tenantContext, warehouseId, new CreateZoneRequest("ZONE-PICK", "Forward Pick Face", "picking"));
        Assert.True(zonePick.IsSuccess);

        var zones = await service.GetZonesAsync(tenantContext, warehouseId);
        Assert.True(zones.Count >= 3);

        // 3. Configure Bin Profiles
        var updPick = await service.UpdateBinProfileAsync(tenantContext, binPickId, new UpdateBinProfileRequest(
            zonePick.Value!.Id, "01", "A", "01", "01", 1000m, 5m, 100m, true, "standard"));
        Assert.True(updPick.IsSuccess);
        Assert.True(updPick.Value!.IsPickFace);

        var updRes = await service.UpdateBinProfileAsync(tenantContext, binResId, new UpdateBinProfileRequest(
            zoneRes.Value!.Id, "02", "B", "01", "01", 5000m, 20m, 500m, false, "standard"));
        Assert.True(updRes.IsSuccess);

        // 4. Barcodes & Scanning
        var barcodeStr = "(01)00843210987654(10)LOT-101(17)261231";
        var bcCreated = await service.CreateBarcodeAsync(tenantContext, new CreateBarcodeRequest(
            barcodeStr, "gs1_128", "product_variant", variantId, "00843210987654", "LOT-101", null, "261231"));
        Assert.True(bcCreated.IsSuccess);

        var scanResult = await service.ProcessBarcodeScanAsync(tenantContext, new BarcodeScanRequest(barcodeStr, "lookup"));
        Assert.True(scanResult.IsValid);
        Assert.Equal("product_variant", scanResult.EntityType);
        Assert.Equal(variantId, scanResult.EntityId);

        // Scan bin code
        var binScanResult = await service.ProcessBarcodeScanAsync(tenantContext, new BarcodeScanRequest("PICK-01", "lookup"));
        Assert.True(binScanResult.IsValid);
        Assert.Equal("stock_bin", binScanResult.EntityType);
        Assert.Equal(binPickId, binScanResult.EntityId);

        // 5. Putaway Strategies & Directed Putaway
        var stratCreated = await service.CreatePutawayStrategyAsync(tenantContext, warehouseId, new CreatePutawayStrategyRequest(
            variantId, null, zoneRes.Value!.Id, binResId, "fixed_bin", 1));
        Assert.True(stratCreated.IsSuccess);

        var putawayRecs = await service.GetPutawayRecommendationsAsync(tenantContext, warehouseId, variantId, 20);
        Assert.NotEmpty(putawayRecs);
        Assert.Equal(binResId, putawayRecs[0].BinId); // Recommended fixed reserve bin

        // Create and Confirm Putaway Task
        var putawayTask = await service.CreatePutawayTaskAsync(tenantContext, warehouseId, new CreatePutawayTaskRequest(
            "grn", null, variantId, binRecId, binResId, 20));
        Assert.True(putawayTask.IsSuccess);
        Assert.Equal("pending", putawayTask.Value!.PutawayStatus);

        var confirmPutaway = await service.ConfirmPutawayTaskAsync(tenantContext, putawayTask.Value.Id, new ConfirmPutawayRequest(binResId, "BIN:RES-01"));
        Assert.True(confirmPutaway.IsSuccess);
        Assert.Equal("completed", confirmPutaway.Value!.PutawayStatus);

        // Verify Model A FIFO transfer: REC-01 now has 30 EA remaining, RES-01 has 20 EA remaining @ PKR 100.00
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT remaining_qty FROM erp_inventory.inventory_cost_layer WHERE bin_id = @bRec;";
            cmd.Parameters.AddWithValue("bRec", binRecId);
            var recRem = (decimal)(await cmd.ExecuteScalarAsync(ct))!;
            Assert.Equal(30m, recRem);

            cmd.CommandText = "SELECT remaining_qty, unit_cost FROM erp_inventory.inventory_cost_layer WHERE bin_id = @bRes;";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("bRes", binResId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            Assert.True(await reader.ReadAsync(ct));
            Assert.Equal(20m, reader.GetDecimal(0));
            Assert.Equal(100.00m, reader.GetDecimal(1));
        }

        // 6. Replenishment Rule & Execution
        var replenRule = await service.CreateReplenishmentRuleAsync(tenantContext, warehouseId, new CreateReplenishmentRuleRequest(
            variantId, binPickId, zoneRes.Value.Id, MinQuantity: 10, MaxQuantity: 30, ReorderPoint: 15, ReplenishIncrementQty: 10));
        Assert.True(replenRule.IsSuccess);

        // Evaluate replenishment -> pick bin has 0 EA, so trigger needed
        var replenRecs = await service.GetReplenishmentRecommendationsAsync(tenantContext, warehouseId);
        Assert.NotEmpty(replenRecs);
        Assert.True(replenRecs[0].TriggerNeeded);
        Assert.Equal(20m, replenRecs[0].RecommendedQuantity); // Limited by 20 EA available in reserve

        // Create and execute replenishment task
        var replenTask = await service.CreateReplenishmentTaskAsync(tenantContext, warehouseId, new CreateReplenishmentTaskRequest(
            variantId, binResId, binPickId, 20, replenRule.Value!.Id));
        Assert.True(replenTask.IsSuccess);

        var executedReplen = await service.ExecuteReplenishmentTaskAsync(tenantContext, replenTask.Value.Id, 20);
        Assert.True(executedReplen.IsSuccess);
        Assert.Equal("completed", executedReplen.Value!.Status);

        // Verify pick bin PICK-01 now holds 20 EA layer at PKR 100.00
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT on_hand_qty FROM erp_inventory.stock_balance WHERE bin_id = @bPick;";
            cmd.Parameters.AddWithValue("bPick", binPickId);
            var pickStock = (decimal)(await cmd.ExecuteScalarAsync(ct))!;
            Assert.Equal(20m, pickStock);
        }

        // 7. Picking Waves
        var wave = await service.CreatePickingWaveAsync(tenantContext, warehouseId, new CreatePickingWaveRequest(
            "sales_order", 1, new[]
            {
                new CreateWaveItemRequest("sales_order", Guid.NewGuid(), Guid.NewGuid(), variantId, binPickId, 5)
            }));
        Assert.True(wave.IsSuccess);

        var releasedWave = await service.ReleasePickingWaveAsync(tenantContext, wave.Value!.Id);
        Assert.True(releasedWave.IsSuccess);
        Assert.Equal("released", releasedWave.Value!.Status);

        var waveDetails = await service.GetPickingWaveByIdAsync(tenantContext, wave.Value.Id);
        Assert.NotNull(waveDetails);
        Assert.Single(waveDetails.Items!);

        var confirmedItem = await service.ConfirmPickItemAsync(tenantContext, wave.Value.Id, waveDetails.Items![0].Id, new ConfirmPickItemRequest(
            5, "BIN:PICK-01", barcodeStr));
        Assert.True(confirmedItem.IsSuccess);
        Assert.Equal("picked", confirmedItem.Value!.Status);

        var pickedWave = await service.GetPickingWaveByIdAsync(tenantContext, wave.Value.Id);
        Assert.Equal("picked", pickedWave!.Status);

        // 8. Cartonization, Packing & Dispatch
        var package = await service.CreateShipmentPackageAsync(tenantContext, warehouseId, new CreatePackageRequest(
            null, null, "carton", "TCS Express", null, null, null,
            new[] { new PackageItemInput(variantId, 5, "LOT-101") }));
        Assert.True(package.IsSuccess);
        Assert.Equal("packing", package.Value!.Status);

        var sealedPkg = await service.SealShipmentPackageAsync(tenantContext, package.Value.Id, new SealPackageRequest(5.5m, binDispId));
        Assert.True(sealedPkg.IsSuccess);
        Assert.Equal("sealed", sealedPkg.Value!.Status);

        var dispatchedPkg = await service.DispatchShipmentAsync(tenantContext, package.Value.Id, new DispatchShipmentRequest(
            "TCS Express", "TCS-TRACK-9988", "BOL-10023", "Signed by recipient"));
        Assert.True(dispatchedPkg.IsSuccess);
        Assert.Equal("shipped", dispatchedPkg.Value!.Status);
        Assert.Equal("TCS-TRACK-9988", dispatchedPkg.Value!.TrackingNumber);

        // 9. Quarantine & Disposition
        var quarRecord = await service.CreateQuarantineRecordAsync(tenantContext, warehouseId, new CreateQuarantineRecordRequest(
            variantId, "customer_return", null, binQuarId, 3, "Packaging torn, needs inspection"));
        Assert.True(quarRecord.IsSuccess);
        Assert.Equal("pending", quarRecord.Value!.Disposition);

        var disposedQuar = await service.DisposeQuarantineRecordAsync(tenantContext, quarRecord.Value.Id, new DisposeQuarantineRequest(
            "released_to_stock", binRecId, "Inspected and repackaged"));
        Assert.True(disposedQuar.IsSuccess);
        Assert.Equal("released_to_stock", disposedQuar.Value!.Disposition);

        // 10. Warehouse Metrics
        var metrics = await service.GetWarehouseMetricsReportAsync(tenantContext, warehouseId);
        Assert.True(metrics.TotalZones >= 3);
        Assert.True(metrics.TotalBins >= 5);
        Assert.True(metrics.PickFacesCount >= 1);
    }
}
