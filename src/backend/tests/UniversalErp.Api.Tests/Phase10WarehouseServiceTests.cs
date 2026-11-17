using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Warehouse;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public class Phase10WarehouseServiceTests
{
    private sealed class MockWarehouseRepository : IWarehouseRepository
    {
        public Task<IReadOnlyList<WarehouseZoneRecord>> GetZonesAsync(TenantContext context, Guid warehouseId, CancellationToken ct) => Task.FromResult<IReadOnlyList<WarehouseZoneRecord>>(Array.Empty<WarehouseZoneRecord>());
        public Task<Result<WarehouseZoneRecord>> CreateZoneAsync(TenantContext context, Guid warehouseId, CreateZoneRequest req, CancellationToken ct) => Task.FromResult(Result<WarehouseZoneRecord>.Success(new WarehouseZoneRecord(Guid.NewGuid(), context.TenantId, context.CompanyId, warehouseId, req.ZoneCode, req.Name, req.ZoneType, req.StorageType ?? "ambient", req.DefaultPutawayStrategy ?? "nearest_empty", true, DateTimeOffset.UtcNow)));
        public Task<Result<WarehouseZoneRecord>> UpdateZoneAsync(TenantContext context, Guid zoneId, UpdateZoneRequest req, CancellationToken ct) => Task.FromResult(Result<WarehouseZoneRecord>.Success(new WarehouseZoneRecord(zoneId, context.TenantId, context.CompanyId, Guid.NewGuid(), "Z1", req.Name, req.ZoneType, req.StorageType, req.DefaultPutawayStrategy, req.IsActive, DateTimeOffset.UtcNow)));
        public Task<IReadOnlyList<BinProfileRecord>> GetBinProfilesAsync(TenantContext context, Guid warehouseId, CancellationToken ct) => Task.FromResult<IReadOnlyList<BinProfileRecord>>(Array.Empty<BinProfileRecord>());
        public Task<Result<BinProfileRecord>> UpdateBinProfileAsync(TenantContext context, Guid binId, UpdateBinProfileRequest req, CancellationToken ct) => Task.FromResult(Result<BinProfileRecord>.Success(new BinProfileRecord(binId, Guid.NewGuid(), "B1", "storage", req.ZoneId, null, req.Aisle, req.Rack, req.Shelf, req.BinPosition, req.MaxWeightKg, req.MaxVolumeCbm, req.MaxUnits, req.IsPickFace, req.StorageRestriction, 0, 0, 0, 0, true)));
        public Task<IReadOnlyList<PutawayStrategyRecord>> GetPutawayStrategiesAsync(TenantContext context, Guid warehouseId, CancellationToken ct) => Task.FromResult<IReadOnlyList<PutawayStrategyRecord>>(Array.Empty<PutawayStrategyRecord>());
        public Task<Result<PutawayStrategyRecord>> CreatePutawayStrategyAsync(TenantContext context, Guid warehouseId, CreatePutawayStrategyRequest req, CancellationToken ct) => Task.FromResult(Result<PutawayStrategyRecord>.Success(new PutawayStrategyRecord(Guid.NewGuid(), context.TenantId, context.CompanyId, warehouseId, req.ProductVariantId, req.ProductCategoryId, req.PreferredZoneId, req.PreferredBinId, req.StrategyType, req.Priority, true, DateTimeOffset.UtcNow)));
        public Task<IReadOnlyList<PutawayRecommendation>> GetPutawayRecommendationsAsync(TenantContext context, Guid warehouseId, Guid variantId, decimal quantity, CancellationToken ct) => Task.FromResult<IReadOnlyList<PutawayRecommendation>>(Array.Empty<PutawayRecommendation>());
        public Task<Result<PutawayTaskRecord>> CreatePutawayTaskAsync(TenantContext context, Guid warehouseId, CreatePutawayTaskRequest req, CancellationToken ct) => Task.FromResult(Result<PutawayTaskRecord>.Success(new PutawayTaskRecord(Guid.NewGuid(), context.TenantId, context.CompanyId, "PUT-01", warehouseId, req.SourceType, req.SourceDocumentId, req.ProductVariantId, req.SourceBinId, req.TargetBinId ?? req.SourceBinId, null, req.Quantity, "pending", req.AssignedTo, null, null, DateTimeOffset.UtcNow)));
        public Task<IReadOnlyList<PutawayTaskRecord>> GetPutawayTasksAsync(TenantContext context, Guid warehouseId, string? status, CancellationToken ct) => Task.FromResult<IReadOnlyList<PutawayTaskRecord>>(Array.Empty<PutawayTaskRecord>());
        public Task<Result<PutawayTaskRecord>> ConfirmPutawayTaskAsync(TenantContext context, Guid taskId, ConfirmPutawayRequest req, CancellationToken ct) => Task.FromResult(Result<PutawayTaskRecord>.Success(new PutawayTaskRecord(taskId, context.TenantId, context.CompanyId, "PUT-01", Guid.NewGuid(), "grn", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), req.ConfirmedBinId, 10, "completed", null, req.ScannedBinBarcode, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
        public Task<IReadOnlyList<BarcodeDefinitionRecord>> GetBarcodesAsync(TenantContext context, string? entityType, CancellationToken ct) => Task.FromResult<IReadOnlyList<BarcodeDefinitionRecord>>(Array.Empty<BarcodeDefinitionRecord>());
        public Task<Result<BarcodeDefinitionRecord>> CreateBarcodeAsync(TenantContext context, CreateBarcodeRequest req, CancellationToken ct) => Task.FromResult(Result<BarcodeDefinitionRecord>.Success(new BarcodeDefinitionRecord(Guid.NewGuid(), context.TenantId, context.CompanyId, req.BarcodeValue, req.BarcodeFormat, req.EntityType, req.EntityId, req.Gs1Gtin, req.Gs1Lot, req.Gs1Serial, req.Gs1ExpiryDate, true, DateTimeOffset.UtcNow)));
        public Task<BarcodeScanResult> ProcessBarcodeScanAsync(TenantContext context, BarcodeScanRequest req, CancellationToken ct) => Task.FromResult(new BarcodeScanResult(true, req.ScannedBarcode, "code128", "product_variant", Guid.NewGuid(), "SKU-01", "Product", "valid", null));
        public Task<IReadOnlyList<dynamic>> GetScanAuditLogsAsync(TenantContext context, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<dynamic>>(Array.Empty<dynamic>());
        public Task<IReadOnlyList<PickingWaveRecord>> GetPickingWavesAsync(TenantContext context, Guid warehouseId, string? status, CancellationToken ct) => Task.FromResult<IReadOnlyList<PickingWaveRecord>>(Array.Empty<PickingWaveRecord>());
        public Task<PickingWaveRecord?> GetPickingWaveByIdAsync(TenantContext context, Guid waveId, CancellationToken ct) => Task.FromResult<PickingWaveRecord?>(null);
        public Task<Result<PickingWaveRecord>> CreatePickingWaveAsync(TenantContext context, Guid warehouseId, CreatePickingWaveRequest req, CancellationToken ct) => Task.FromResult(Result<PickingWaveRecord>.Success(new PickingWaveRecord(Guid.NewGuid(), context.TenantId, context.CompanyId, "WAV-01", warehouseId, req.WaveType, "created", req.Items.Count, 0, req.Priority, DateTimeOffset.UtcNow, null)));
        public Task<Result<WavePickItemRecord>> ConfirmPickItemAsync(TenantContext context, Guid waveId, Guid itemId, ConfirmPickItemRequest req, CancellationToken ct) => Task.FromResult(Result<WavePickItemRecord>.Success(new WavePickItemRecord(itemId, waveId, "sales_order", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BIN-01", null, null, null, null, 10, req.PickedQuantity, "picked", null, req.ScannedBinBarcode, req.ScannedProductBarcode, DateTimeOffset.UtcNow)));
        public Task<Result<PickingWaveRecord>> ReleasePickingWaveAsync(TenantContext context, Guid waveId, CancellationToken ct) => Task.FromResult(Result<PickingWaveRecord>.Success(new PickingWaveRecord(waveId, context.TenantId, context.CompanyId, "WAV-01", Guid.NewGuid(), "sales_order", "released", 1, 0, 1, DateTimeOffset.UtcNow, null)));
        public Task<IReadOnlyList<ShipmentPackageRecord>> GetShipmentPackagesAsync(TenantContext context, Guid warehouseId, string? status, CancellationToken ct) => Task.FromResult<IReadOnlyList<ShipmentPackageRecord>>(Array.Empty<ShipmentPackageRecord>());
        public Task<Result<ShipmentPackageRecord>> CreateShipmentPackageAsync(TenantContext context, Guid warehouseId, CreatePackageRequest req, CancellationToken ct) => Task.FromResult(Result<ShipmentPackageRecord>.Success(new ShipmentPackageRecord(Guid.NewGuid(), context.TenantId, context.CompanyId, "PKG-01", warehouseId, req.SalesOrderId, req.SalesDispatchId, req.PackageType, req.TrackingNumber, req.CarrierName, req.GrossWeightKg, req.NetWeightKg, "packing", null, null, null, DateTimeOffset.UtcNow)));
        public Task<Result<ShipmentPackageRecord>> SealShipmentPackageAsync(TenantContext context, Guid packageId, SealPackageRequest req, CancellationToken ct) => Task.FromResult(Result<ShipmentPackageRecord>.Success(new ShipmentPackageRecord(packageId, context.TenantId, context.CompanyId, "PKG-01", Guid.NewGuid(), null, null, "carton", null, null, req.GrossWeightKg, null, "sealed", req.StagedBinId, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow)));
        public Task<Result<ShipmentPackageRecord>> DispatchShipmentAsync(TenantContext context, Guid packageId, DispatchShipmentRequest req, CancellationToken ct) => Task.FromResult(Result<ShipmentPackageRecord>.Success(new ShipmentPackageRecord(packageId, context.TenantId, context.CompanyId, "PKG-01", Guid.NewGuid(), null, null, "carton", req.TrackingNumber, req.CarrierName, 10, 8, "shipped", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
        public Task<IReadOnlyList<ReplenishmentRuleRecord>> GetReplenishmentRulesAsync(TenantContext context, Guid warehouseId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ReplenishmentRuleRecord>>(Array.Empty<ReplenishmentRuleRecord>());
        public Task<Result<ReplenishmentRuleRecord>> CreateReplenishmentRuleAsync(TenantContext context, Guid warehouseId, CreateReplenishmentRuleRequest req, CancellationToken ct) => Task.FromResult(Result<ReplenishmentRuleRecord>.Success(new ReplenishmentRuleRecord(Guid.NewGuid(), context.TenantId, context.CompanyId, warehouseId, req.ProductVariantId, req.PickBinId, req.ReserveZoneId, req.MinQuantity, req.MaxQuantity, req.ReorderPoint, req.ReplenishIncrementQty, true, DateTimeOffset.UtcNow)));
        public Task<IReadOnlyList<ReplenishmentRecommendation>> GetReplenishmentRecommendationsAsync(TenantContext context, Guid warehouseId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ReplenishmentRecommendation>>(Array.Empty<ReplenishmentRecommendation>());
        public Task<Result<ReplenishmentTaskRecord>> CreateReplenishmentTaskAsync(TenantContext context, Guid warehouseId, CreateReplenishmentTaskRequest req, CancellationToken ct) => Task.FromResult(Result<ReplenishmentTaskRecord>.Success(new ReplenishmentTaskRecord(Guid.NewGuid(), context.TenantId, context.CompanyId, "REP-01", warehouseId, req.RuleId, req.ProductVariantId, req.FromBinId, req.ToBinId, req.Quantity, 0, "pending", DateTimeOffset.UtcNow, null)));
        public Task<IReadOnlyList<ReplenishmentTaskRecord>> GetReplenishmentTasksAsync(TenantContext context, Guid warehouseId, string? status, CancellationToken ct) => Task.FromResult<IReadOnlyList<ReplenishmentTaskRecord>>(Array.Empty<ReplenishmentTaskRecord>());
        public Task<Result<ReplenishmentTaskRecord>> ExecuteReplenishmentTaskAsync(TenantContext context, Guid taskId, decimal quantity, CancellationToken ct) => Task.FromResult(Result<ReplenishmentTaskRecord>.Success(new ReplenishmentTaskRecord(taskId, context.TenantId, context.CompanyId, "REP-01", Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), quantity, quantity, "completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
        public Task<Result<dynamic>> ExecuteBinTransferAsync(TenantContext context, BinStockTransferRequest req, CancellationToken ct) => Task.FromResult(Result<dynamic>.Success(new { Ok = true }));
        public Task<IReadOnlyList<QuarantineRecord>> GetQuarantineRecordsAsync(TenantContext context, Guid warehouseId, string? disposition, CancellationToken ct) => Task.FromResult<IReadOnlyList<QuarantineRecord>>(Array.Empty<QuarantineRecord>());
        public Task<Result<QuarantineRecord>> CreateQuarantineRecordAsync(TenantContext context, Guid warehouseId, CreateQuarantineRecordRequest req, CancellationToken ct) => Task.FromResult(Result<QuarantineRecord>.Success(new QuarantineRecord(Guid.NewGuid(), context.TenantId, context.CompanyId, "QAR-01", warehouseId, req.ProductVariantId, req.SourceType, req.SourceReferenceId, req.QuarantineBinId, req.Quantity, req.Reason, "pending", null, req.InspectedBy, null, null, DateTimeOffset.UtcNow)));
        public Task<Result<QuarantineRecord>> DisposeQuarantineRecordAsync(TenantContext context, Guid recordId, DisposeQuarantineRequest req, CancellationToken ct) => Task.FromResult(Result<QuarantineRecord>.Success(new QuarantineRecord(recordId, context.TenantId, context.CompanyId, "QAR-01", Guid.NewGuid(), Guid.NewGuid(), "mfg_qc", null, Guid.NewGuid(), 10, "Defect", req.Disposition, req.TargetDispositionBinId, null, DateTimeOffset.UtcNow, req.DispositionNotes, DateTimeOffset.UtcNow)));
        public Task<WarehouseMetricsReport> GetWarehouseMetricsReportAsync(TenantContext context, Guid warehouseId, CancellationToken ct) => Task.FromResult(new WarehouseMetricsReport(1, 10, 2, 45.5m, 1, 1, 0, 0, Array.Empty<WarehouseZoneSummary>()));
    }

    [Fact]
    public async Task CreateZone_ValidatesRequiredFields()
    {
        var service = new WarehouseService(new MockWarehouseRepository());
        var ctx = new TenantContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var resEmptyCode = await service.CreateZoneAsync(ctx, Guid.NewGuid(), new CreateZoneRequest("", "Zone Name", "picking"));
        Assert.False(resEmptyCode.IsSuccess);
        Assert.Equal("ZONE_CODE_REQUIRED", resEmptyCode.Error);

        var resEmptyName = await service.CreateZoneAsync(ctx, Guid.NewGuid(), new CreateZoneRequest("Z1", "", "picking"));
        Assert.False(resEmptyName.IsSuccess);
        Assert.Equal("ZONE_NAME_REQUIRED", resEmptyName.Error);
    }

    [Fact]
    public async Task CreateReplenishmentRule_ValidatesMinMaxAndReorderBounds()
    {
        var service = new WarehouseService(new MockWarehouseRepository());
        var ctx = new TenantContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // Min >= Max
        var resMinMax = await service.CreateReplenishmentRuleAsync(ctx, Guid.NewGuid(), new CreateReplenishmentRuleRequest(Guid.NewGuid(), Guid.NewGuid(), null, 50, 40, 45));
        Assert.False(resMinMax.IsSuccess);
        Assert.Equal("MIN_QTY_MUST_BE_LESS_THAN_MAX", resMinMax.Error);

        // Reorder < Min
        var resReorder = await service.CreateReplenishmentRuleAsync(ctx, Guid.NewGuid(), new CreateReplenishmentRuleRequest(Guid.NewGuid(), Guid.NewGuid(), null, 20, 100, 15));
        Assert.False(resReorder.IsSuccess);
        Assert.Equal("REORDER_POINT_CANNOT_BE_LESS_THAN_MIN", resReorder.Error);
    }

    [Fact]
    public async Task ExecuteBinTransfer_ValidatesIdenticalBinsAndQuantity()
    {
        var service = new WarehouseService(new MockWarehouseRepository());
        var ctx = new TenantContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var binId = Guid.NewGuid();

        // Identical bins
        var resIdentical = await service.ExecuteBinTransferAsync(ctx, new BinStockTransferRequest(Guid.NewGuid(), Guid.NewGuid(), binId, Guid.NewGuid(), binId, 10));
        Assert.False(resIdentical.IsSuccess);
        Assert.Equal("SOURCE_AND_DEST_BIN_IDENTICAL", resIdentical.Error);

        // Non-positive quantity
        var resZeroQty = await service.ExecuteBinTransferAsync(ctx, new BinStockTransferRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0));
        Assert.False(resZeroQty.IsSuccess);
        Assert.Equal("INVALID_QUANTITY", resZeroQty.Error);
    }

    [Fact]
    public async Task GetBarcodes_AllowsNullAndFilteredEntityType()
    {
        var service = new WarehouseService(new MockWarehouseRepository());
        var ctx = new TenantContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var allBarcodes = await service.GetBarcodesAsync(ctx, null, CancellationToken.None);
        Assert.NotNull(allBarcodes);

        var filteredBarcodes = await service.GetBarcodesAsync(ctx, "product_variant", CancellationToken.None);
        Assert.NotNull(filteredBarcodes);
    }
}
