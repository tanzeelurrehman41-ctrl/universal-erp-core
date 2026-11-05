using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UniversalErp.Application.Abstractions;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Application.Inventory;

public sealed record UnitOfMeasureSummary(
    Guid UnitOfMeasureId,
    string Code,
    string Name,
    string Dimension,
    short DecimalPlaces);

public sealed record ProductVariantSummary(
    Guid VariantId,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string Sku,
    string? VariantName,
    Guid BaseUnitOfMeasureId,
    string BaseUnitCode,
    string TrackingMode,
    string CostingMethod,
    bool IsActive);

public sealed record WarehouseSummary(
    Guid WarehouseId,
    string Code,
    string Name,
    bool IsActive);

public sealed record StockBinSummary(
    Guid BinId,
    Guid WarehouseId,
    string Code,
    string BinType,
    bool IsActive);

public sealed record InventoryAvailabilitySummary(
    Guid VariantId,
    Guid WarehouseId,
    Guid BinId,
    string StockStatus,
    decimal OnHandQuantity,
    Guid UnitOfMeasureId);

public sealed record InventoryReceiptSummary(
    Guid TransactionId,
    Guid MovementId,
    Guid VariantId,
    Guid WarehouseId,
    Guid BinId,
    decimal Quantity,
    Guid UnitOfMeasureId,
    DateTimeOffset OccurredAt,
    DateTimeOffset PostedAt);

public sealed record CreateUnitOfMeasureRequest(
    string? Code,
    string? Name,
    string? Dimension,
    short DecimalPlaces = 3);

public sealed record CreateProductVariantRequest(
    Guid ProductId,
    string? Sku,
    string? VariantName,
    Guid BaseUnitOfMeasureId,
    string TrackingMode = "none",
    string CostingMethod = "fifo");

public sealed record CreateWarehouseRequest(string? Code, string? Name);

public sealed record CreateStockBinRequest(string? Code, string? BinType = "storage");

public sealed record PostInventoryReceiptRequest(
    Guid VariantId,
    Guid WarehouseId,
    Guid BinId,
    decimal Quantity,
    Guid UnitOfMeasureId,
    string? SourceDocumentType,
    Guid? SourceDocumentId,
    DateTimeOffset? OccurredAt,
    string? Note);

public sealed record PostInventoryTransferRequest(
    Guid VariantId,
    Guid SourceWarehouseId,
    Guid SourceBinId,
    Guid DestinationWarehouseId,
    Guid DestinationBinId,
    decimal Quantity,
    Guid UnitOfMeasureId,
    string? Note);

public sealed record InventoryTransferSummary(
    Guid TransactionId,
    Guid MovementId,
    Guid VariantId,
    Guid SourceWarehouseId,
    Guid SourceBinId,
    Guid DestinationWarehouseId,
    Guid DestinationBinId,
    decimal Quantity,
    Guid UnitOfMeasureId,
    DateTimeOffset PostedAt);

public sealed record PostInventoryAdjustmentRequest(
    Guid VariantId,
    Guid WarehouseId,
    Guid BinId,
    decimal QuantityDelta,
    Guid UnitOfMeasureId,
    string ReasonCode,
    string? Note);

public sealed record InventoryAdjustmentSummary(
    Guid TransactionId,
    Guid MovementId,
    Guid VariantId,
    Guid WarehouseId,
    Guid BinId,
    decimal QuantityDelta,
    Guid UnitOfMeasureId,
    string ReasonCode,
    DateTimeOffset PostedAt);

public interface IInventoryWorkflowRepository
{
    Task<IReadOnlyList<UnitOfMeasureSummary>> ListUnitsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<UnitOfMeasureSummary>> CreateUnitAsync(Guid tenantId, CreateUnitOfMeasureRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductVariantSummary>> ListVariantsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<ProductVariantSummary>> CreateVariantAsync(Guid tenantId, CreateProductVariantRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<WarehouseSummary>> ListWarehousesAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken);
    Task<Result<WarehouseSummary>> CreateWarehouseAsync(Guid tenantId, Guid companyId, CreateWarehouseRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<StockBinSummary>> ListBinsAsync(Guid tenantId, Guid companyId, Guid? warehouseId, CancellationToken cancellationToken);
    Task<Result<StockBinSummary>> CreateBinAsync(Guid tenantId, Guid companyId, Guid warehouseId, CreateStockBinRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryAvailabilitySummary>> ListAvailabilityAsync(Guid tenantId, Guid companyId, Guid? variantId, Guid? warehouseId, CancellationToken cancellationToken);
    Task<Result<InventoryReceiptSummary>> PostReceiptAsync(TenantContext context, Guid transactionId, string requestHash, PostInventoryReceiptRequest request, CancellationToken cancellationToken);
    Task<Result<InventoryTransferSummary>> PostTransferAsync(TenantContext context, Guid transactionId, string requestHash, PostInventoryTransferRequest request, CancellationToken cancellationToken);
    Task<Result<InventoryAdjustmentSummary>> PostAdjustmentAsync(TenantContext context, Guid transactionId, string requestHash, PostInventoryAdjustmentRequest request, CancellationToken cancellationToken);
}

public sealed class InventoryWorkflowService(
    ITenantContextAccessor tenantContextAccessor,
    IInventoryWorkflowRepository repository)
{
    private static readonly Regex MasterCodePattern = new(
        @"\A[A-Z0-9][A-Z0-9._/-]{0,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> Dimensions = new(StringComparer.Ordinal)
    {
        "count", "length", "mass", "volume", "time", "area", "other",
    };
    private static readonly HashSet<string> TrackingModes = new(StringComparer.Ordinal)
    {
        "none", "lot", "serial", "lot_serial",
    };
    private static readonly HashSet<string> CostingMethods = new(StringComparer.Ordinal)
    {
        "fifo", "moving_average", "specific_identification",
    };
    private static readonly HashSet<string> BinTypes = new(StringComparer.Ordinal)
    {
        "storage", "receiving", "dispatch", "production", "transit", "virtual",
    };

    public Task<IReadOnlyList<UnitOfMeasureSummary>> ListUnitsAsync(CancellationToken ct) =>
        repository.ListUnitsAsync(tenantContextAccessor.Current.TenantId, ct);

    public async Task<Result<UnitOfMeasureSummary>> CreateUnitAsync(
        CreateUnitOfMeasureRequest request,
        CancellationToken ct)
    {
        var code = NormalizeCode(request.Code);
        var name = request.Name?.Trim();
        var dimension = request.Dimension?.Trim().ToLowerInvariant();
        if (code is null || name is null || name.Length is < 1 or > 100 ||
            dimension is null || !Dimensions.Contains(dimension) ||
            request.DecimalPlaces is < 0 or > 9)
        {
            return Result<UnitOfMeasureSummary>.Failure("INVALID_UNIT_OF_MEASURE");
        }

        return await repository.CreateUnitAsync(
            tenantContextAccessor.Current.TenantId,
            new CreateUnitOfMeasureRequest(code, name, dimension, request.DecimalPlaces),
            ct);
    }

    public Task<IReadOnlyList<ProductVariantSummary>> ListVariantsAsync(CancellationToken ct) =>
        repository.ListVariantsAsync(tenantContextAccessor.Current.TenantId, ct);

    public async Task<Result<ProductVariantSummary>> CreateVariantAsync(
        CreateProductVariantRequest request,
        CancellationToken ct)
    {
        var sku = NormalizeCode(request.Sku);
        var variantName = string.IsNullOrWhiteSpace(request.VariantName) ? null : request.VariantName.Trim();
        var trackingMode = request.TrackingMode?.Trim().ToLowerInvariant();
        var costingMethod = request.CostingMethod?.Trim().ToLowerInvariant();
        if (request.ProductId == Guid.Empty || request.BaseUnitOfMeasureId == Guid.Empty ||
            sku is null || (variantName?.Length > 200) ||
            trackingMode is null || costingMethod is null ||
            !TrackingModes.Contains(trackingMode) || !CostingMethods.Contains(costingMethod))
        {
            return Result<ProductVariantSummary>.Failure("INVALID_PRODUCT_VARIANT");
        }

        return await repository.CreateVariantAsync(
            tenantContextAccessor.Current.TenantId,
            request with { Sku = sku, VariantName = variantName, TrackingMode = trackingMode, CostingMethod = costingMethod },
            ct);
    }

    public Task<IReadOnlyList<WarehouseSummary>> ListWarehousesAsync(CancellationToken ct)
    {
        var context = tenantContextAccessor.Current;
        return repository.ListWarehousesAsync(context.TenantId, context.CompanyId, ct);
    }

    public async Task<Result<WarehouseSummary>> CreateWarehouseAsync(
        CreateWarehouseRequest request,
        CancellationToken ct)
    {
        var code = NormalizeCode(request.Code);
        var name = request.Name?.Trim();
        if (code is null || name is null || name.Length is < 1 or > 160)
        {
            return Result<WarehouseSummary>.Failure("INVALID_WAREHOUSE");
        }

        var context = tenantContextAccessor.Current;
        return await repository.CreateWarehouseAsync(
            context.TenantId,
            context.CompanyId,
            new CreateWarehouseRequest(code, name),
            ct);
    }

    public Task<IReadOnlyList<StockBinSummary>> ListBinsAsync(Guid? warehouseId, CancellationToken ct)
    {
        var context = tenantContextAccessor.Current;
        return repository.ListBinsAsync(context.TenantId, context.CompanyId, warehouseId, ct);
    }

    public async Task<Result<StockBinSummary>> CreateBinAsync(
        Guid warehouseId,
        CreateStockBinRequest request,
        CancellationToken ct)
    {
        var code = NormalizeCode(request.Code);
        var binType = request.BinType?.Trim().ToLowerInvariant();
        if (warehouseId == Guid.Empty || code is null || binType is null || !BinTypes.Contains(binType))
        {
            return Result<StockBinSummary>.Failure("INVALID_STOCK_BIN");
        }

        var context = tenantContextAccessor.Current;
        return await repository.CreateBinAsync(
            context.TenantId,
            context.CompanyId,
            warehouseId,
            request with { Code = code, BinType = binType },
            ct);
    }

    public Task<IReadOnlyList<InventoryAvailabilitySummary>> ListAvailabilityAsync(
        Guid? variantId,
        Guid? warehouseId,
        CancellationToken ct)
    {
        var context = tenantContextAccessor.Current;
        return repository.ListAvailabilityAsync(context.TenantId, context.CompanyId, variantId, warehouseId, ct);
    }

    public async Task<Result<InventoryReceiptSummary>> PostReceiptAsync(
        Guid? idempotencyKey,
        PostInventoryReceiptRequest request,
        CancellationToken ct)
    {
        if (idempotencyKey is null || idempotencyKey == Guid.Empty)
        {
            return Result<InventoryReceiptSummary>.Failure("INVALID_IDEMPOTENCY_KEY");
        }
        if (request.VariantId == Guid.Empty || request.WarehouseId == Guid.Empty ||
            request.BinId == Guid.Empty || request.UnitOfMeasureId == Guid.Empty ||
            request.Quantity <= 0 || request.Quantity >= 1_000_000_000_000_000m ||
            decimal.Round(request.Quantity, 9) != request.Quantity ||
            (request.SourceDocumentType?.Length > 64) ||
            (request.Note?.Length > 2000))
        {
            return Result<InventoryReceiptSummary>.Failure("INVALID_RECEIPT");
        }

        var normalized = request with
        {
            SourceDocumentType = string.IsNullOrWhiteSpace(request.SourceDocumentType)
                ? null
                : request.SourceDocumentType.Trim().ToLowerInvariant(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            OccurredAt = request.OccurredAt?.ToUniversalTime(),
        };
        if ((normalized.SourceDocumentType is null) != (normalized.SourceDocumentId is null) ||
            normalized.SourceDocumentId == Guid.Empty)
        {
            return Result<InventoryReceiptSummary>.Failure("INVALID_SOURCE_DOCUMENT");
        }

        var canonicalPayload = JsonSerializer.Serialize(new
        {
            command = "inventory.receipt.post",
            normalized.VariantId,
            normalized.WarehouseId,
            normalized.BinId,
            normalized.Quantity,
            normalized.UnitOfMeasureId,
            normalized.SourceDocumentType,
            normalized.SourceDocumentId,
            normalized.OccurredAt,
            normalized.Note,
        });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
        return await repository.PostReceiptAsync(
            tenantContextAccessor.Current,
            idempotencyKey.Value,
            requestHash,
            normalized,
            ct);
    }

    public async Task<Result<InventoryTransferSummary>> PostTransferAsync(
        Guid? idempotencyKey,
        PostInventoryTransferRequest request,
        CancellationToken ct)
    {
        if (idempotencyKey is null || idempotencyKey == Guid.Empty)
        {
            return Result<InventoryTransferSummary>.Failure("INVALID_IDEMPOTENCY_KEY");
        }
        if (request.VariantId == Guid.Empty ||
            request.SourceWarehouseId == Guid.Empty || request.SourceBinId == Guid.Empty ||
            request.DestinationWarehouseId == Guid.Empty || request.DestinationBinId == Guid.Empty ||
            request.UnitOfMeasureId == Guid.Empty ||
            request.Quantity <= 0 || request.Quantity >= 1_000_000_000_000_000m ||
            decimal.Round(request.Quantity, 9) != request.Quantity ||
            (request.Note?.Length > 2000))
        {
            return Result<InventoryTransferSummary>.Failure("INVALID_TRANSFER");
        }
        if (request.SourceBinId == request.DestinationBinId)
        {
            return Result<InventoryTransferSummary>.Failure("SOURCE_AND_DESTINATION_BIN_MUST_DIFFER");
        }

        var normalized = request with
        {
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        };

        var canonicalPayload = JsonSerializer.Serialize(new
        {
            command = "inventory.transfer.post",
            normalized.VariantId,
            normalized.SourceWarehouseId,
            normalized.SourceBinId,
            normalized.DestinationWarehouseId,
            normalized.DestinationBinId,
            normalized.Quantity,
            normalized.UnitOfMeasureId,
            normalized.Note,
        });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
        return await repository.PostTransferAsync(
            tenantContextAccessor.Current,
            idempotencyKey.Value,
            requestHash,
            normalized,
            ct);
    }

    public async Task<Result<InventoryAdjustmentSummary>> PostAdjustmentAsync(
        Guid? idempotencyKey,
        PostInventoryAdjustmentRequest request,
        CancellationToken ct)
    {
        if (idempotencyKey is null || idempotencyKey == Guid.Empty)
        {
            return Result<InventoryAdjustmentSummary>.Failure("INVALID_IDEMPOTENCY_KEY");
        }
        if (request.VariantId == Guid.Empty ||
            request.WarehouseId == Guid.Empty || request.BinId == Guid.Empty ||
            request.UnitOfMeasureId == Guid.Empty ||
            request.QuantityDelta == 0 ||
            Math.Abs(request.QuantityDelta) >= 1_000_000_000_000_000m ||
            decimal.Round(request.QuantityDelta, 9) != request.QuantityDelta ||
            string.IsNullOrWhiteSpace(request.ReasonCode) ||
            (request.Note?.Length > 2000))
        {
            return Result<InventoryAdjustmentSummary>.Failure("INVALID_ADJUSTMENT");
        }

        var normalized = request with
        {
            ReasonCode = request.ReasonCode.Trim().ToLowerInvariant(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        };

        var canonicalPayload = JsonSerializer.Serialize(new
        {
            command = "inventory.adjustment.post",
            normalized.VariantId,
            normalized.WarehouseId,
            normalized.BinId,
            normalized.QuantityDelta,
            normalized.UnitOfMeasureId,
            normalized.ReasonCode,
            normalized.Note,
        });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
        return await repository.PostAdjustmentAsync(
            tenantContextAccessor.Current,
            idempotencyKey.Value,
            requestHash,
            normalized,
            ct);
    }

    private static string? NormalizeCode(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant();
        return !string.IsNullOrEmpty(normalized) && MasterCodePattern.IsMatch(normalized)
            ? normalized
            : null;
    }
}
