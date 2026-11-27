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

public sealed record ProductCategorySummary(
    Guid CategoryId,
    string Code,
    string Name,
    Guid? ParentCategoryId);

public sealed record CreateProductCategoryRequest(
    string? Code,
    string? Name,
    Guid? ParentCategoryId = null);

public sealed record InventoryMovementLegSummary(
    int LegNumber,
    Guid VariantId,
    string Sku,
    Guid WarehouseId,
    string WarehouseCode,
    Guid BinId,
    string BinCode,
    string StatusCode,
    decimal QuantityDelta,
    Guid BaseUomId,
    string BaseUomCode);

public sealed record InventoryMovementSummary(
    Guid MovementId,
    string MovementType,
    string? SourceDocumentType,
    Guid? SourceDocumentId,
    DateTimeOffset OccurredAt,
    string? Note,
    IReadOnlyList<InventoryMovementLegSummary> Legs);

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
    Guid UnitOfMeasureId,
    decimal? ValuedQuantity = null);

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
    Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<ProductCategorySummary>> CreateCategoryAsync(Guid tenantId, CreateProductCategoryRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryMovementSummary>> ListMovementsAsync(Guid tenantId, Guid companyId, int limit, CancellationToken cancellationToken);
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

    // Phase 5: Complete Inventory Management
    Task<IReadOnlyList<StockReservationSummary>> ListReservationsAsync(Guid tenantId, Guid companyId, Guid? variantId, Guid? warehouseId, string? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StockReservationSummary>>([]);
    Task<Result<StockReservationSummary>> CreateReservationAsync(TenantContext context, Guid transactionId, string requestHash, CreateStockReservationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<StockReservationSummary>.Failure("NOT_IMPLEMENTED"));
    Task<Result<StockReservationSummary>> ReleaseReservationAsync(TenantContext context, Guid reservationId, string? reason, CancellationToken cancellationToken) =>
        Task.FromResult(Result<StockReservationSummary>.Failure("NOT_IMPLEMENTED"));
    Task<IReadOnlyList<AvailableToPromiseSummary>> GetAtpAsync(Guid tenantId, Guid companyId, Guid? variantId, Guid? warehouseId, Guid? binId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AvailableToPromiseSummary>>([]);

    Task<IReadOnlyList<StockPickListSummary>> ListPickListsAsync(Guid tenantId, Guid companyId, Guid? warehouseId, string? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StockPickListSummary>>([]);
    Task<Result<StockPickListSummary>> CreatePickListAsync(TenantContext context, Guid transactionId, string requestHash, CreatePickListRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<StockPickListSummary>.Failure("NOT_IMPLEMENTED"));
    Task<Result<StockPickListSummary>> RecordPickLinesAsync(TenantContext context, Guid pickListId, IReadOnlyList<RecordPickLineRequest> lines, CancellationToken cancellationToken) =>
        Task.FromResult(Result<StockPickListSummary>.Failure("NOT_IMPLEMENTED"));
    Task<Result<DispatchPickListResult>> DispatchPickListAsync(TenantContext context, Guid pickListId, CancellationToken cancellationToken) =>
        Task.FromResult(Result<DispatchPickListResult>.Failure("NOT_IMPLEMENTED"));

    Task<IReadOnlyList<StockCountSessionSummary>> ListCountSessionsAsync(Guid tenantId, Guid companyId, Guid? warehouseId, string? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StockCountSessionSummary>>([]);
    Task<Result<StockCountSessionSummary>> CreateCountSessionAsync(TenantContext context, Guid transactionId, string requestHash, CreateCountSessionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<StockCountSessionSummary>.Failure("NOT_IMPLEMENTED"));
    Task<Result<StockCountSessionSummary>> RecordCountLinesAsync(TenantContext context, Guid sessionId, IReadOnlyList<RecordCountLineRequest> lines, CancellationToken cancellationToken) =>
        Task.FromResult(Result<StockCountSessionSummary>.Failure("NOT_IMPLEMENTED"));
    Task<Result<ApproveCountSessionResult>> ApproveCountSessionAsync(TenantContext context, Guid sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Result<ApproveCountSessionResult>.Failure("NOT_IMPLEMENTED"));

    Task<IReadOnlyList<StockReturnSummary>> ListStockReturnsAsync(Guid tenantId, Guid companyId, Guid? warehouseId, string? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StockReturnSummary>>([]);
    Task<Result<StockReturnSummary>> CreateStockReturnAsync(TenantContext context, Guid transactionId, string requestHash, CreateStockReturnRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<StockReturnSummary>.Failure("NOT_IMPLEMENTED"));
    Task<Result<PostStockReturnResult>> ProcessStockReturnAsync(TenantContext context, Guid returnId, CancellationToken cancellationToken) =>
        Task.FromResult(Result<PostStockReturnResult>.Failure("NOT_IMPLEMENTED"));

    Task<IReadOnlyList<BinMovementHistoryItem>> GetBinMovementHistoryAsync(Guid tenantId, Guid companyId, Guid? warehouseId, Guid? binId, Guid? variantId, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BinMovementHistoryItem>>([]);
    Task<InventoryValuationReportSummary> GetInventoryValuationReportAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(new InventoryValuationReportSummary(DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, 0, 0, true, []));
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

    public Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(CancellationToken ct) =>
        repository.ListCategoriesAsync(tenantContextAccessor.Current.TenantId, ct);

    public async Task<Result<ProductCategorySummary>> CreateCategoryAsync(
        CreateProductCategoryRequest request,
        CancellationToken ct)
    {
        var code = NormalizeCode(request.Code);
        var name = request.Name?.Trim();
        if (code is null || name is null || name.Length is < 1 or > 100)
        {
            return Result<ProductCategorySummary>.Failure("INVALID_CATEGORY");
        }

        return await repository.CreateCategoryAsync(
            tenantContextAccessor.Current.TenantId,
            new CreateProductCategoryRequest(code, name, request.ParentCategoryId),
            ct);
    }

    public Task<IReadOnlyList<InventoryMovementSummary>> ListMovementsAsync(int limit, CancellationToken ct) =>
        repository.ListMovementsAsync(
            tenantContextAccessor.Current.TenantId,
            tenantContextAccessor.Current.CompanyId,
            Math.Clamp(limit, 1, 200),
            ct);

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

    // ── Phase 5 Service Methods ─────────────────────────────────────────────────────────

    public Task<IReadOnlyList<StockReservationSummary>> ListReservationsAsync(
        Guid? variantId,
        Guid? warehouseId,
        string? status,
        CancellationToken ct)
    {
        var context = tenantContextAccessor.Current;
        return repository.ListReservationsAsync(context.TenantId, context.CompanyId, variantId, warehouseId, status?.Trim().ToLowerInvariant(), ct);
    }

    public async Task<Result<StockReservationSummary>> CreateReservationAsync(
        Guid? idempotencyKey,
        CreateStockReservationRequest request,
        CancellationToken ct)
    {
        if (idempotencyKey is null || idempotencyKey == Guid.Empty)
        {
            return Result<StockReservationSummary>.Failure("INVALID_IDEMPOTENCY_KEY");
        }
        if (request.VariantId == Guid.Empty || request.WarehouseId == Guid.Empty ||
            request.Quantity <= 0 || request.Quantity >= 1_000_000_000_000_000m ||
            decimal.Round(request.Quantity, 9) != request.Quantity ||
            (request.SourceDocumentType?.Length > 64) ||
            (request.Note?.Length > 2000))
        {
            return Result<StockReservationSummary>.Failure("INVALID_RESERVATION");
        }

        var normalized = request with
        {
            SourceDocumentType = string.IsNullOrWhiteSpace(request.SourceDocumentType) ? "sales_order" : request.SourceDocumentType.Trim().ToLowerInvariant(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            ExpiresAt = request.ExpiresAt?.ToUniversalTime(),
        };

        var canonicalPayload = JsonSerializer.Serialize(new
        {
            command = "inventory.reservation.create",
            normalized.VariantId,
            normalized.WarehouseId,
            normalized.BinId,
            normalized.Quantity,
            normalized.SourceDocumentType,
            normalized.SourceDocumentId,
            normalized.ExpiresAt,
            normalized.Note,
        });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));

        return await repository.CreateReservationAsync(
            tenantContextAccessor.Current,
            idempotencyKey.Value,
            requestHash,
            normalized,
            ct);
    }

    public async Task<Result<StockReservationSummary>> ReleaseReservationAsync(
        Guid reservationId,
        string? reason,
        CancellationToken ct)
    {
        if (reservationId == Guid.Empty)
        {
            return Result<StockReservationSummary>.Failure("INVALID_RESERVATION_ID");
        }

        return await repository.ReleaseReservationAsync(
            tenantContextAccessor.Current,
            reservationId,
            string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            ct);
    }

    public Task<IReadOnlyList<AvailableToPromiseSummary>> GetAtpAsync(
        Guid? variantId,
        Guid? warehouseId,
        Guid? binId,
        CancellationToken ct)
    {
        var context = tenantContextAccessor.Current;
        return repository.GetAtpAsync(context.TenantId, context.CompanyId, variantId, warehouseId, binId, ct);
    }

    public Task<IReadOnlyList<StockPickListSummary>> ListPickListsAsync(
        Guid? warehouseId,
        string? status,
        CancellationToken ct)
    {
        var context = tenantContextAccessor.Current;
        return repository.ListPickListsAsync(context.TenantId, context.CompanyId, warehouseId, status?.Trim().ToLowerInvariant(), ct);
    }

    public async Task<Result<StockPickListSummary>> CreatePickListAsync(
        Guid? idempotencyKey,
        CreatePickListRequest request,
        CancellationToken ct)
    {
        if (idempotencyKey is null || idempotencyKey == Guid.Empty)
        {
            return Result<StockPickListSummary>.Failure("INVALID_IDEMPOTENCY_KEY");
        }
        if (request.WarehouseId == Guid.Empty || request.Lines is null || request.Lines.Count == 0 ||
            (request.Note?.Length > 2000))
        {
            return Result<StockPickListSummary>.Failure("INVALID_PICK_LIST");
        }

        foreach (var line in request.Lines)
        {
            if (line.VariantId == Guid.Empty || line.BinId == Guid.Empty ||
                line.RequestedQty <= 0 || line.RequestedQty >= 1_000_000_000_000_000m ||
                decimal.Round(line.RequestedQty, 9) != line.RequestedQty)
            {
                return Result<StockPickListSummary>.Failure("INVALID_PICK_LINE");
            }
        }

        var normalized = request with
        {
            SourceDocumentType = string.IsNullOrWhiteSpace(request.SourceDocumentType) ? "sales_order" : request.SourceDocumentType.Trim().ToLowerInvariant(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        };

        var canonicalPayload = JsonSerializer.Serialize(new
        {
            command = "inventory.pick_list.create",
            normalized.WarehouseId,
            normalized.SourceDocumentType,
            normalized.SourceDocumentId,
            lines = normalized.Lines.Select(l => new { l.VariantId, l.BinId, l.RequestedQty, l.ReservationId, l.Note }),
            normalized.Note,
        });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));

        return await repository.CreatePickListAsync(
            tenantContextAccessor.Current,
            idempotencyKey.Value,
            requestHash,
            normalized,
            ct);
    }

    public async Task<Result<StockPickListSummary>> RecordPickLinesAsync(
        Guid pickListId,
        IReadOnlyList<RecordPickLineRequest> lines,
        CancellationToken ct)
    {
        if (pickListId == Guid.Empty || lines is null || lines.Count == 0)
        {
            return Result<StockPickListSummary>.Failure("INVALID_PICK_LINES");
        }
        foreach (var line in lines)
        {
            if (line.PickLineId == Guid.Empty || line.PickedQty < 0 ||
                line.PickedQty >= 1_000_000_000_000_000m ||
                decimal.Round(line.PickedQty, 9) != line.PickedQty)
            {
                return Result<StockPickListSummary>.Failure("INVALID_PICKED_QUANTITY");
            }
        }

        return await repository.RecordPickLinesAsync(
            tenantContextAccessor.Current,
            pickListId,
            lines,
            ct);
    }

    public async Task<Result<DispatchPickListResult>> DispatchPickListAsync(
        Guid pickListId,
        CancellationToken ct)
    {
        if (pickListId == Guid.Empty)
        {
            return Result<DispatchPickListResult>.Failure("INVALID_PICK_LIST_ID");
        }

        return await repository.DispatchPickListAsync(
            tenantContextAccessor.Current,
            pickListId,
            ct);
    }

    public Task<IReadOnlyList<StockCountSessionSummary>> ListCountSessionsAsync(
        Guid? warehouseId,
        string? status,
        CancellationToken ct)
    {
        var context = tenantContextAccessor.Current;
        return repository.ListCountSessionsAsync(context.TenantId, context.CompanyId, warehouseId, status?.Trim().ToLowerInvariant(), ct);
    }

    public async Task<Result<StockCountSessionSummary>> CreateCountSessionAsync(
        Guid? idempotencyKey,
        CreateCountSessionRequest request,
        CancellationToken ct)
    {
        if (idempotencyKey is null || idempotencyKey == Guid.Empty)
        {
            return Result<StockCountSessionSummary>.Failure("INVALID_IDEMPOTENCY_KEY");
        }
        var countType = request.CountType?.Trim().ToLowerInvariant();
        if (request.WarehouseId == Guid.Empty || countType is not ("full" or "cycle" or "spot") ||
            (request.Note?.Length > 2000))
        {
            return Result<StockCountSessionSummary>.Failure("INVALID_COUNT_SESSION");
        }

        var normalized = request with
        {
            CountType = countType,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        };

        var canonicalPayload = JsonSerializer.Serialize(new
        {
            command = "inventory.count_session.create",
            normalized.WarehouseId,
            normalized.CountType,
            normalized.Note,
        });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));

        return await repository.CreateCountSessionAsync(
            tenantContextAccessor.Current,
            idempotencyKey.Value,
            requestHash,
            normalized,
            ct);
    }

    public async Task<Result<StockCountSessionSummary>> RecordCountLinesAsync(
        Guid sessionId,
        IReadOnlyList<RecordCountLineRequest> lines,
        CancellationToken ct)
    {
        if (sessionId == Guid.Empty || lines is null || lines.Count == 0)
        {
            return Result<StockCountSessionSummary>.Failure("INVALID_COUNT_LINES");
        }
        foreach (var l in lines)
        {
            if (l.VariantId == Guid.Empty || l.BinId == Guid.Empty ||
                l.CountedQty < 0 || l.CountedQty >= 1_000_000_000_000_000m ||
                decimal.Round(l.CountedQty, 9) != l.CountedQty)
            {
                return Result<StockCountSessionSummary>.Failure("INVALID_COUNTED_QUANTITY");
            }
        }

        return await repository.RecordCountLinesAsync(
            tenantContextAccessor.Current,
            sessionId,
            lines,
            ct);
    }

    public async Task<Result<ApproveCountSessionResult>> ApproveCountSessionAsync(
        Guid sessionId,
        CancellationToken ct)
    {
        if (sessionId == Guid.Empty)
        {
            return Result<ApproveCountSessionResult>.Failure("INVALID_SESSION_ID");
        }

        return await repository.ApproveCountSessionAsync(
            tenantContextAccessor.Current,
            sessionId,
            ct);
    }

    public Task<IReadOnlyList<StockReturnSummary>> ListStockReturnsAsync(
        Guid? warehouseId,
        string? status,
        CancellationToken ct)
    {
        var context = tenantContextAccessor.Current;
        return repository.ListStockReturnsAsync(context.TenantId, context.CompanyId, warehouseId, status?.Trim().ToLowerInvariant(), ct);
    }

    public async Task<Result<StockReturnSummary>> CreateStockReturnAsync(
        Guid? idempotencyKey,
        CreateStockReturnRequest request,
        CancellationToken ct)
    {
        if (idempotencyKey is null || idempotencyKey == Guid.Empty)
        {
            return Result<StockReturnSummary>.Failure("INVALID_IDEMPOTENCY_KEY");
        }
        var returnType = request.ReturnType?.Trim().ToLowerInvariant();
        if (request.WarehouseId == Guid.Empty ||
            returnType is not ("customer_return" or "supplier_return") ||
            request.Lines is null || request.Lines.Count == 0 ||
            (request.Note?.Length > 2000))
        {
            return Result<StockReturnSummary>.Failure("INVALID_STOCK_RETURN");
        }

        foreach (var line in request.Lines)
        {
            if (line.VariantId == Guid.Empty || line.BinId == Guid.Empty ||
                line.Quantity <= 0 || line.Quantity >= 1_000_000_000_000_000m ||
                decimal.Round(line.Quantity, 9) != line.Quantity ||
                line.UnitCost < 0 || line.UnitCost >= 1_000_000_000_000_000m ||
                decimal.Round(line.UnitCost, 4) != line.UnitCost)
            {
                return Result<StockReturnSummary>.Failure("INVALID_RETURN_LINE");
            }
        }

        var normalized = request with
        {
            ReturnType = returnType,
            SourceDocumentType = string.IsNullOrWhiteSpace(request.SourceDocumentType) ? "manual" : request.SourceDocumentType.Trim().ToLowerInvariant(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        };

        var canonicalPayload = JsonSerializer.Serialize(new
        {
            command = "inventory.stock_return.create",
            normalized.ReturnType,
            normalized.SourceDocumentType,
            normalized.SourceDocumentId,
            normalized.WarehouseId,
            lines = normalized.Lines.Select(l => new { l.VariantId, l.BinId, l.Quantity, l.UnitCost, l.Disposition, l.Note }),
            normalized.Note,
        });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));

        return await repository.CreateStockReturnAsync(
            tenantContextAccessor.Current,
            idempotencyKey.Value,
            requestHash,
            normalized,
            ct);
    }

    public async Task<Result<PostStockReturnResult>> ProcessStockReturnAsync(
        Guid returnId,
        CancellationToken ct)
    {
        if (returnId == Guid.Empty)
        {
            return Result<PostStockReturnResult>.Failure("INVALID_RETURN_ID");
        }

        return await repository.ProcessStockReturnAsync(
            tenantContextAccessor.Current,
            returnId,
            ct);
    }

    public Task<IReadOnlyList<BinMovementHistoryItem>> GetBinMovementHistoryAsync(
        Guid? warehouseId,
        Guid? binId,
        Guid? variantId,
        int limit,
        CancellationToken ct)
    {
        var context = tenantContextAccessor.Current;
        return repository.GetBinMovementHistoryAsync(
            context.TenantId,
            context.CompanyId,
            warehouseId,
            binId,
            variantId,
            Math.Clamp(limit, 1, 500),
            ct);
    }

    public Task<InventoryValuationReportSummary> GetInventoryValuationReportAsync(CancellationToken ct)
    {
        var context = tenantContextAccessor.Current;
        if (context.TenantId == Guid.Empty || context.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("A valid tenant and company context is required to generate an inventory valuation report.");
        }

        return repository.GetInventoryValuationReportAsync(context.TenantId, context.CompanyId, ct);
    }

    private static string? NormalizeCode(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant();
        return !string.IsNullOrEmpty(normalized) && MasterCodePattern.IsMatch(normalized)
            ? normalized
            : null;
    }
}
