using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversalErp.Application.Abstractions;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Application.Warehouse;

public interface IWarehouseRepository
{
    Task<IReadOnlyList<WarehouseZoneRecord>> GetZonesAsync(TenantContext context, Guid warehouseId, CancellationToken ct);
    Task<Result<WarehouseZoneRecord>> CreateZoneAsync(TenantContext context, Guid warehouseId, CreateZoneRequest req, CancellationToken ct);
    Task<Result<WarehouseZoneRecord>> UpdateZoneAsync(TenantContext context, Guid zoneId, UpdateZoneRequest req, CancellationToken ct);

    Task<IReadOnlyList<BinProfileRecord>> GetBinProfilesAsync(TenantContext context, Guid warehouseId, CancellationToken ct);
    Task<Result<BinProfileRecord>> UpdateBinProfileAsync(TenantContext context, Guid binId, UpdateBinProfileRequest req, CancellationToken ct);

    Task<IReadOnlyList<PutawayStrategyRecord>> GetPutawayStrategiesAsync(TenantContext context, Guid warehouseId, CancellationToken ct);
    Task<Result<PutawayStrategyRecord>> CreatePutawayStrategyAsync(TenantContext context, Guid warehouseId, CreatePutawayStrategyRequest req, CancellationToken ct);
    Task<IReadOnlyList<PutawayRecommendation>> GetPutawayRecommendationsAsync(TenantContext context, Guid warehouseId, Guid variantId, decimal quantity, CancellationToken ct);
    Task<Result<PutawayTaskRecord>> CreatePutawayTaskAsync(TenantContext context, Guid warehouseId, CreatePutawayTaskRequest req, CancellationToken ct);
    Task<IReadOnlyList<PutawayTaskRecord>> GetPutawayTasksAsync(TenantContext context, Guid warehouseId, string? status, CancellationToken ct);
    Task<Result<PutawayTaskRecord>> ConfirmPutawayTaskAsync(TenantContext context, Guid taskId, ConfirmPutawayRequest req, CancellationToken ct);

    Task<IReadOnlyList<BarcodeDefinitionRecord>> GetBarcodesAsync(TenantContext context, string? entityType, CancellationToken ct);
    Task<Result<BarcodeDefinitionRecord>> CreateBarcodeAsync(TenantContext context, CreateBarcodeRequest req, CancellationToken ct);
    Task<BarcodeScanResult> ProcessBarcodeScanAsync(TenantContext context, BarcodeScanRequest req, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> GetScanAuditLogsAsync(TenantContext context, int limit, CancellationToken ct);

    Task<IReadOnlyList<PickingWaveRecord>> GetPickingWavesAsync(TenantContext context, Guid warehouseId, string? status, CancellationToken ct);
    Task<PickingWaveRecord?> GetPickingWaveByIdAsync(TenantContext context, Guid waveId, CancellationToken ct);
    Task<Result<PickingWaveRecord>> CreatePickingWaveAsync(TenantContext context, Guid warehouseId, CreatePickingWaveRequest req, CancellationToken ct);
    Task<Result<WavePickItemRecord>> ConfirmPickItemAsync(TenantContext context, Guid waveId, Guid itemId, ConfirmPickItemRequest req, CancellationToken ct);
    Task<Result<PickingWaveRecord>> ReleasePickingWaveAsync(TenantContext context, Guid waveId, CancellationToken ct);

    Task<IReadOnlyList<ShipmentPackageRecord>> GetShipmentPackagesAsync(TenantContext context, Guid warehouseId, string? status, CancellationToken ct);
    Task<Result<ShipmentPackageRecord>> CreateShipmentPackageAsync(TenantContext context, Guid warehouseId, CreatePackageRequest req, CancellationToken ct);
    Task<Result<ShipmentPackageRecord>> SealShipmentPackageAsync(TenantContext context, Guid packageId, SealPackageRequest req, CancellationToken ct);
    Task<Result<ShipmentPackageRecord>> DispatchShipmentAsync(TenantContext context, Guid packageId, DispatchShipmentRequest req, CancellationToken ct);

    Task<IReadOnlyList<ReplenishmentRuleRecord>> GetReplenishmentRulesAsync(TenantContext context, Guid warehouseId, CancellationToken ct);
    Task<Result<ReplenishmentRuleRecord>> CreateReplenishmentRuleAsync(TenantContext context, Guid warehouseId, CreateReplenishmentRuleRequest req, CancellationToken ct);
    Task<IReadOnlyList<ReplenishmentRecommendation>> GetReplenishmentRecommendationsAsync(TenantContext context, Guid warehouseId, CancellationToken ct);
    Task<Result<ReplenishmentTaskRecord>> CreateReplenishmentTaskAsync(TenantContext context, Guid warehouseId, CreateReplenishmentTaskRequest req, CancellationToken ct);
    Task<IReadOnlyList<ReplenishmentTaskRecord>> GetReplenishmentTasksAsync(TenantContext context, Guid warehouseId, string? status, CancellationToken ct);
    Task<Result<ReplenishmentTaskRecord>> ExecuteReplenishmentTaskAsync(TenantContext context, Guid taskId, decimal quantity, CancellationToken ct);

    Task<Result<dynamic>> ExecuteBinTransferAsync(TenantContext context, BinStockTransferRequest req, CancellationToken ct);

    Task<IReadOnlyList<QuarantineRecord>> GetQuarantineRecordsAsync(TenantContext context, Guid warehouseId, string? disposition, CancellationToken ct);
    Task<Result<QuarantineRecord>> CreateQuarantineRecordAsync(TenantContext context, Guid warehouseId, CreateQuarantineRecordRequest req, CancellationToken ct);
    Task<Result<QuarantineRecord>> DisposeQuarantineRecordAsync(TenantContext context, Guid recordId, DisposeQuarantineRequest req, CancellationToken ct);

    Task<WarehouseMetricsReport> GetWarehouseMetricsReportAsync(TenantContext context, Guid warehouseId, CancellationToken ct);
}
