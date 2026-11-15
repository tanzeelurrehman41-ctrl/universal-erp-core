using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversalErp.Application.Abstractions;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Application.Warehouse;

public class WarehouseService
{
    private readonly IWarehouseRepository _repo;

    public WarehouseService(IWarehouseRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public Task<IReadOnlyList<WarehouseZoneRecord>> GetZonesAsync(TenantContext context, Guid warehouseId, CancellationToken ct = default)
        => _repo.GetZonesAsync(context, warehouseId, ct);

    public async Task<Result<WarehouseZoneRecord>> CreateZoneAsync(TenantContext context, Guid warehouseId, CreateZoneRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.ZoneCode))
            return Result<WarehouseZoneRecord>.Failure("ZONE_CODE_REQUIRED");
        if (string.IsNullOrWhiteSpace(req.Name))
            return Result<WarehouseZoneRecord>.Failure("ZONE_NAME_REQUIRED");

        return await _repo.CreateZoneAsync(context, warehouseId, req, ct);
    }

    public Task<Result<WarehouseZoneRecord>> UpdateZoneAsync(TenantContext context, Guid zoneId, UpdateZoneRequest req, CancellationToken ct = default)
        => _repo.UpdateZoneAsync(context, zoneId, req, ct);

    public Task<IReadOnlyList<BinProfileRecord>> GetBinProfilesAsync(TenantContext context, Guid warehouseId, CancellationToken ct = default)
        => _repo.GetBinProfilesAsync(context, warehouseId, ct);

    public Task<Result<BinProfileRecord>> UpdateBinProfileAsync(TenantContext context, Guid binId, UpdateBinProfileRequest req, CancellationToken ct = default)
        => _repo.UpdateBinProfileAsync(context, binId, req, ct);

    public Task<IReadOnlyList<PutawayStrategyRecord>> GetPutawayStrategiesAsync(TenantContext context, Guid warehouseId, CancellationToken ct = default)
        => _repo.GetPutawayStrategiesAsync(context, warehouseId, ct);

    public Task<Result<PutawayStrategyRecord>> CreatePutawayStrategyAsync(TenantContext context, Guid warehouseId, CreatePutawayStrategyRequest req, CancellationToken ct = default)
        => _repo.CreatePutawayStrategyAsync(context, warehouseId, req, ct);

    public Task<IReadOnlyList<PutawayRecommendation>> GetPutawayRecommendationsAsync(TenantContext context, Guid warehouseId, Guid variantId, decimal quantity, CancellationToken ct = default)
        => _repo.GetPutawayRecommendationsAsync(context, warehouseId, variantId, quantity, ct);

    public async Task<Result<PutawayTaskRecord>> CreatePutawayTaskAsync(TenantContext context, Guid warehouseId, CreatePutawayTaskRequest req, CancellationToken ct = default)
    {
        if (req.Quantity <= 0)
            return Result<PutawayTaskRecord>.Failure("INVALID_QUANTITY");
        return await _repo.CreatePutawayTaskAsync(context, warehouseId, req, ct);
    }

    public Task<IReadOnlyList<PutawayTaskRecord>> GetPutawayTasksAsync(TenantContext context, Guid warehouseId, string? status = null, CancellationToken ct = default)
        => _repo.GetPutawayTasksAsync(context, warehouseId, status, ct);

    public Task<Result<PutawayTaskRecord>> ConfirmPutawayTaskAsync(TenantContext context, Guid taskId, ConfirmPutawayRequest req, CancellationToken ct = default)
        => _repo.ConfirmPutawayTaskAsync(context, taskId, req, ct);

    public Task<IReadOnlyList<BarcodeDefinitionRecord>> GetBarcodesAsync(TenantContext context, string? entityType = null, CancellationToken ct = default)
        => _repo.GetBarcodesAsync(context, entityType, ct);

    public async Task<Result<BarcodeDefinitionRecord>> CreateBarcodeAsync(TenantContext context, CreateBarcodeRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.BarcodeValue))
            return Result<BarcodeDefinitionRecord>.Failure("BARCODE_VALUE_REQUIRED");
        return await _repo.CreateBarcodeAsync(context, req, ct);
    }

    public async Task<BarcodeScanResult> ProcessBarcodeScanAsync(TenantContext context, BarcodeScanRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.ScannedBarcode))
            return new BarcodeScanResult(false, string.Empty, "unknown", null, null, null, null, "invalid", "Barcode is empty.");
        return await _repo.ProcessBarcodeScanAsync(context, req, ct);
    }

    public Task<IReadOnlyList<dynamic>> GetScanAuditLogsAsync(TenantContext context, int limit = 50, CancellationToken ct = default)
        => _repo.GetScanAuditLogsAsync(context, limit, ct);

    public Task<IReadOnlyList<PickingWaveRecord>> GetPickingWavesAsync(TenantContext context, Guid warehouseId, string? status = null, CancellationToken ct = default)
        => _repo.GetPickingWavesAsync(context, warehouseId, status, ct);

    public Task<PickingWaveRecord?> GetPickingWaveByIdAsync(TenantContext context, Guid waveId, CancellationToken ct = default)
        => _repo.GetPickingWaveByIdAsync(context, waveId, ct);

    public async Task<Result<PickingWaveRecord>> CreatePickingWaveAsync(TenantContext context, Guid warehouseId, CreatePickingWaveRequest req, CancellationToken ct = default)
    {
        if (req.Items == null || req.Items.Count == 0)
            return Result<PickingWaveRecord>.Failure("WAVE_ITEMS_REQUIRED");
        return await _repo.CreatePickingWaveAsync(context, warehouseId, req, ct);
    }

    public Task<Result<WavePickItemRecord>> ConfirmPickItemAsync(TenantContext context, Guid waveId, Guid itemId, ConfirmPickItemRequest req, CancellationToken ct = default)
        => _repo.ConfirmPickItemAsync(context, waveId, itemId, req, ct);

    public Task<Result<PickingWaveRecord>> ReleasePickingWaveAsync(TenantContext context, Guid waveId, CancellationToken ct = default)
        => _repo.ReleasePickingWaveAsync(context, waveId, ct);

    public Task<IReadOnlyList<ShipmentPackageRecord>> GetShipmentPackagesAsync(TenantContext context, Guid warehouseId, string? status = null, CancellationToken ct = default)
        => _repo.GetShipmentPackagesAsync(context, warehouseId, status, ct);

    public Task<Result<ShipmentPackageRecord>> CreateShipmentPackageAsync(TenantContext context, Guid warehouseId, CreatePackageRequest req, CancellationToken ct = default)
        => _repo.CreateShipmentPackageAsync(context, warehouseId, req, ct);

    public Task<Result<ShipmentPackageRecord>> SealShipmentPackageAsync(TenantContext context, Guid packageId, SealPackageRequest req, CancellationToken ct = default)
        => _repo.SealShipmentPackageAsync(context, packageId, req, ct);

    public Task<Result<ShipmentPackageRecord>> DispatchShipmentAsync(TenantContext context, Guid packageId, DispatchShipmentRequest req, CancellationToken ct = default)
        => _repo.DispatchShipmentAsync(context, packageId, req, ct);

    public Task<IReadOnlyList<ReplenishmentRuleRecord>> GetReplenishmentRulesAsync(TenantContext context, Guid warehouseId, CancellationToken ct = default)
        => _repo.GetReplenishmentRulesAsync(context, warehouseId, ct);

    public async Task<Result<ReplenishmentRuleRecord>> CreateReplenishmentRuleAsync(TenantContext context, Guid warehouseId, CreateReplenishmentRuleRequest req, CancellationToken ct = default)
    {
        if (req.MinQuantity >= req.MaxQuantity)
            return Result<ReplenishmentRuleRecord>.Failure("MIN_QTY_MUST_BE_LESS_THAN_MAX");
        if (req.ReorderPoint < req.MinQuantity)
            return Result<ReplenishmentRuleRecord>.Failure("REORDER_POINT_CANNOT_BE_LESS_THAN_MIN");
        return await _repo.CreateReplenishmentRuleAsync(context, warehouseId, req, ct);
    }

    public Task<IReadOnlyList<ReplenishmentRecommendation>> GetReplenishmentRecommendationsAsync(TenantContext context, Guid warehouseId, CancellationToken ct = default)
        => _repo.GetReplenishmentRecommendationsAsync(context, warehouseId, ct);

    public Task<Result<ReplenishmentTaskRecord>> CreateReplenishmentTaskAsync(TenantContext context, Guid warehouseId, CreateReplenishmentTaskRequest req, CancellationToken ct = default)
        => _repo.CreateReplenishmentTaskAsync(context, warehouseId, req, ct);

    public Task<IReadOnlyList<ReplenishmentTaskRecord>> GetReplenishmentTasksAsync(TenantContext context, Guid warehouseId, string? status = null, CancellationToken ct = default)
        => _repo.GetReplenishmentTasksAsync(context, warehouseId, status, ct);

    public Task<Result<ReplenishmentTaskRecord>> ExecuteReplenishmentTaskAsync(TenantContext context, Guid taskId, decimal quantity, CancellationToken ct = default)
        => _repo.ExecuteReplenishmentTaskAsync(context, taskId, quantity, ct);

    public async Task<Result<dynamic>> ExecuteBinTransferAsync(TenantContext context, BinStockTransferRequest req, CancellationToken ct = default)
    {
        if (req.Quantity <= 0)
            return Result<dynamic>.Failure("INVALID_QUANTITY");
        if (req.SourceBinId == req.DestinationBinId)
            return Result<dynamic>.Failure("SOURCE_AND_DEST_BIN_IDENTICAL");
        return await _repo.ExecuteBinTransferAsync(context, req, ct);
    }

    public Task<IReadOnlyList<QuarantineRecord>> GetQuarantineRecordsAsync(TenantContext context, Guid warehouseId, string? disposition = null, CancellationToken ct = default)
        => _repo.GetQuarantineRecordsAsync(context, warehouseId, disposition, ct);

    public Task<Result<QuarantineRecord>> CreateQuarantineRecordAsync(TenantContext context, Guid warehouseId, CreateQuarantineRecordRequest req, CancellationToken ct = default)
        => _repo.CreateQuarantineRecordAsync(context, warehouseId, req, ct);

    public Task<Result<QuarantineRecord>> DisposeQuarantineRecordAsync(TenantContext context, Guid recordId, DisposeQuarantineRequest req, CancellationToken ct = default)
        => _repo.DisposeQuarantineRecordAsync(context, recordId, req, ct);

    public Task<WarehouseMetricsReport> GetWarehouseMetricsReportAsync(TenantContext context, Guid warehouseId, CancellationToken ct = default)
        => _repo.GetWarehouseMetricsReportAsync(context, warehouseId, ct);
}
