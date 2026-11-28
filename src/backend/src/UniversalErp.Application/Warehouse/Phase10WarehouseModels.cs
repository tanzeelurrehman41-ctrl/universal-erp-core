using System;
using System.Collections.Generic;

namespace UniversalErp.Application.Warehouse;

public record WarehouseZoneRecord(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    Guid WarehouseId,
    string ZoneCode,
    string Name,
    string ZoneType,
    string StorageType,
    string DefaultPutawayStrategy,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateZoneRequest(
    string ZoneCode,
    string Name,
    string ZoneType,
    string? StorageType = "ambient",
    string? DefaultPutawayStrategy = "nearest_empty");

public record UpdateZoneRequest(
    string Name,
    string ZoneType,
    string StorageType,
    string DefaultPutawayStrategy,
    bool IsActive);

public record BinProfileRecord(
    Guid BinId,
    Guid WarehouseId,
    string Code,
    string BinType,
    Guid? ZoneId,
    string? ZoneCode,
    string? Aisle,
    string? Rack,
    string? Shelf,
    string? BinPosition,
    decimal? MaxWeightKg,
    decimal? MaxVolumeCbm,
    decimal? MaxUnits,
    bool IsPickFace,
    string StorageRestriction,
    decimal CurrentUnits,
    decimal CurrentWeightKg,
    decimal CurrentVolumeCbm,
    decimal UtilizationPercent,
    bool IsActive);

public record UpdateBinProfileRequest(
    Guid? ZoneId,
    string? Aisle,
    string? Rack,
    string? Shelf,
    string? BinPosition,
    decimal? MaxWeightKg,
    decimal? MaxVolumeCbm,
    decimal? MaxUnits,
    bool IsPickFace,
    string StorageRestriction);

public record PutawayStrategyRecord(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    Guid WarehouseId,
    Guid? ProductVariantId,
    Guid? ProductCategoryId,
    Guid? PreferredZoneId,
    Guid? PreferredBinId,
    string StrategyType,
    int Priority,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreatePutawayStrategyRequest(
    Guid? ProductVariantId,
    Guid? ProductCategoryId,
    Guid? PreferredZoneId,
    Guid? PreferredBinId,
    string StrategyType,
    int Priority = 1);

public record ReplenishmentRuleRecord(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    Guid WarehouseId,
    Guid ProductVariantId,
    Guid PickBinId,
    Guid? ReserveZoneId,
    decimal MinQuantity,
    decimal MaxQuantity,
    decimal ReorderPoint,
    decimal ReplenishIncrementQty,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateReplenishmentRuleRequest(
    Guid ProductVariantId,
    Guid PickBinId,
    Guid? ReserveZoneId,
    decimal MinQuantity,
    decimal MaxQuantity,
    decimal ReorderPoint,
    decimal ReplenishIncrementQty = 1);

public record BarcodeDefinitionRecord(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string BarcodeValue,
    string BarcodeFormat,
    string EntityType,
    Guid EntityId,
    string? Gs1Gtin,
    string? Gs1Lot,
    string? Gs1Serial,
    string? Gs1ExpiryDate,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateBarcodeRequest(
    string BarcodeValue,
    string BarcodeFormat,
    string EntityType,
    Guid EntityId,
    string? Gs1Gtin = null,
    string? Gs1Lot = null,
    string? Gs1Serial = null,
    string? Gs1ExpiryDate = null);

public record BarcodeScanRequest(
    string ScannedBarcode,
    string OperationContext,
    string? DeviceId = null,
    string? IdempotencyKey = null);

public record BarcodeScanResult(
    bool IsValid,
    string ScannedBarcode,
    string BarcodeFormat,
    string? EntityType,
    Guid? EntityId,
    string? EntityCode,
    string? Description,
    string Status,
    string? ErrorMessage,
    IReadOnlyDictionary<string, string>? Metadata = null);

public record PutawayRecommendation(
    Guid BinId,
    string BinCode,
    string? ZoneCode,
    decimal CurrentUnits,
    decimal? MaxUnits,
    decimal ProjectedUtilizationPercent,
    int Score,
    string Rationale);

public record PutawayTaskRecord(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string TaskNumber,
    Guid WarehouseId,
    string SourceType,
    Guid? SourceDocumentId,
    Guid ProductVariantId,
    Guid SourceBinId,
    Guid SuggestedBinId,
    Guid? ConfirmedBinId,
    decimal Quantity,
    string PutawayStatus,
    string? AssignedTo,
    string? ScannedBinBarcode,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt);

public record CreatePutawayTaskRequest(
    string SourceType,
    Guid? SourceDocumentId,
    Guid ProductVariantId,
    Guid SourceBinId,
    Guid? TargetBinId,
    decimal Quantity,
    string? AssignedTo = null);

public record ConfirmPutawayRequest(
    Guid ConfirmedBinId,
    string? ScannedBinBarcode = null);

public record PickingWaveRecord(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string WaveNumber,
    Guid WarehouseId,
    string WaveType,
    string Status,
    int TotalLines,
    int CompletedLines,
    int Priority,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<WavePickItemRecord>? Items = null);

public record WavePickItemRecord(
    Guid Id,
    Guid WaveId,
    string SourceDocumentType,
    Guid SourceDocumentId,
    Guid SourceLineId,
    Guid ProductVariantId,
    Guid SourceBinId,
    string? BinCode,
    string? Aisle,
    string? Rack,
    string? Shelf,
    Guid? TargetPackStationId,
    decimal RequestedQuantity,
    decimal PickedQuantity,
    string Status,
    string? PickerId,
    string? ScannedBinBarcode,
    string? ScannedProductBarcode,
    DateTimeOffset? PickedAt);

public record CreatePickingWaveRequest(
    string WaveType,
    int Priority,
    IReadOnlyList<CreateWaveItemRequest> Items);

public record CreateWaveItemRequest(
    string SourceDocumentType,
    Guid SourceDocumentId,
    Guid SourceLineId,
    Guid ProductVariantId,
    Guid SourceBinId,
    decimal Quantity);

public record ConfirmPickItemRequest(
    decimal PickedQuantity,
    string? ScannedBinBarcode = null,
    string? ScannedProductBarcode = null);

public record ShipmentPackageRecord(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string PackageNumber,
    Guid WarehouseId,
    Guid? SalesOrderId,
    Guid? SalesDispatchId,
    string PackageType,
    string? TrackingNumber,
    string? CarrierName,
    decimal? GrossWeightKg,
    decimal? NetWeightKg,
    string Status,
    Guid? StagedBinId,
    DateTimeOffset? SealedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ShipmentPackageItemRecord>? Items = null);

public record ShipmentPackageItemRecord(
    Guid Id,
    Guid PackageId,
    Guid ProductVariantId,
    decimal Quantity,
    string? LotNumber,
    string? SerialNumber);

public record CreatePackageRequest(
    Guid? SalesOrderId,
    Guid? SalesDispatchId,
    string PackageType = "carton",
    string? CarrierName = null,
    string? TrackingNumber = null,
    decimal? GrossWeightKg = null,
    decimal? NetWeightKg = null,
    IReadOnlyList<PackageItemInput>? Items = null);

public record PackageItemInput(
    Guid ProductVariantId,
    decimal Quantity,
    string? LotNumber = null,
    string? SerialNumber = null);

public record SealPackageRequest(
    decimal GrossWeightKg,
    Guid? StagedBinId = null);

public record DispatchShipmentRequest(
    string CarrierName,
    string TrackingNumber,
    string? BolNumber = null,
    string? ProofOfDeliveryNotes = null);

public record ReplenishmentTaskRecord(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string TaskNumber,
    Guid WarehouseId,
    Guid? RuleId,
    Guid ProductVariantId,
    Guid FromBinId,
    Guid ToBinId,
    decimal RequestedQuantity,
    decimal TransferredQuantity,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public record CreateReplenishmentTaskRequest(
    Guid ProductVariantId,
    Guid FromBinId,
    Guid ToBinId,
    decimal Quantity,
    Guid? RuleId = null);

public record QuarantineRecord(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string RecordNumber,
    Guid WarehouseId,
    Guid ProductVariantId,
    string SourceType,
    Guid? SourceReferenceId,
    Guid QuarantineBinId,
    decimal Quantity,
    string Reason,
    string Disposition,
    Guid? TargetDispositionBinId,
    string? InspectedBy,
    DateTimeOffset? DisposedAt,
    string? DispositionNotes,
    DateTimeOffset CreatedAt);

public record CreateQuarantineRecordRequest(
    Guid ProductVariantId,
    string SourceType,
    Guid? SourceReferenceId,
    Guid QuarantineBinId,
    decimal Quantity,
    string Reason,
    string? InspectedBy = null);

public record DisposeQuarantineRequest(
    string Disposition,
    Guid? TargetDispositionBinId = null,
    string? DispositionNotes = null);

public record WarehouseMetricsReport(
    int TotalZones,
    int TotalBins,
    int PickFacesCount,
    decimal OverallCapacityUtilization,
    int PendingPutawayCount,
    int OpenPickingWavesCount,
    int PendingReplenishmentCount,
    int QuarantineCount,
    IReadOnlyList<WarehouseZoneSummary> ZonesSummary);

public record WarehouseZoneSummary(
    Guid ZoneId,
    string ZoneCode,
    string Name,
    string ZoneType,
    int BinCount,
    decimal AverageUtilizationPercent);

public record BinStockTransferRequest(
    Guid VariantId,
    Guid SourceWarehouseId,
    Guid SourceBinId,
    Guid DestinationWarehouseId,
    Guid DestinationBinId,
    decimal Quantity,
    string? Reason = null);
