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

public sealed record InventoryValuationCurrencyTotal(
    string CurrencyCode,
    decimal Amount,
    int ItemCount,
    decimal? Quantity = null);

public sealed record InventoryValuationLayerReportItem(
    Guid LayerId,
    Guid? ParentLayerId,
    Guid VariantId,
    string Sku,
    Guid WarehouseId,
    string WarehouseCode,
    Guid BinId,
    string BinCode,
    string SourceDocumentType,
    Guid? SourceDocumentId,
    DateTimeOffset ReceivedAt,
    decimal OriginalQty,
    decimal RemainingQty,
    string SourceCurrencyCode,
    decimal SourceUnitCost,
    decimal SourceExtendedValuation,
    string FunctionalCurrencyCode,
    decimal? FunctionalUnitCost,
    decimal? FunctionalExtendedValuation,
    decimal? AppliedExchangeRate,
    DateTimeOffset? ExchangeRateEffectiveAt,
    string CurrencyConversionStatus,
    string StatusCode);

public sealed record InventoryValuationReportSummary(
    DateTimeOffset GeneratedAt,
    int TotalActiveLayers,
    decimal TotalPhysicalStock,
    decimal TotalValuedStock,
    decimal TotalUnvaluedStock,
    string FunctionalCurrencyCode,
    decimal? FunctionalValuation,
    decimal? GeneralLedger1200FunctionalBalance,
    decimal? ReconciliationVarianceFunctional,
    bool IsReconciled,
    string ReconciliationStatus,
    bool IsFunctionalValuationResolved,
    bool IsGeneralLedgerCurrencyAligned,
    int MissingExchangeRateLayerCount,
    string? UnresolvedCurrencyIssue,
    IReadOnlyList<InventoryValuationCurrencyTotal> SourceValuationByCurrency,
    IReadOnlyList<InventoryValuationCurrencyTotal> GeneralLedger1200BalancesByCurrency,
    IReadOnlyList<InventoryValuationLayerReportItem> Layers,
    decimal? TotalValuation = null,
    decimal? GeneralLedger1200Balance = null,
    decimal? ReconciliationVariance = null,
    decimal? TotalValuationPkr = null,
    decimal? GeneralLedger1200BalancePkr = null,
    decimal? ReconciliationVariancePkr = null)
{
    public static InventoryValuationReportSummary Empty(string functionalCurrencyCode = "PKR")
    {
        var normalized = string.IsNullOrWhiteSpace(functionalCurrencyCode)
            ? "PKR"
            : functionalCurrencyCode.Trim().ToUpperInvariant();
        decimal? pkr = string.Equals(normalized, "PKR", StringComparison.Ordinal) ? 0m : null;
        return new InventoryValuationReportSummary(
            GeneratedAt: DateTimeOffset.UtcNow,
            TotalActiveLayers: 0,
            TotalPhysicalStock: 0m,
            TotalValuedStock: 0m,
            TotalUnvaluedStock: 0m,
            FunctionalCurrencyCode: normalized,
            FunctionalValuation: 0m,
            GeneralLedger1200FunctionalBalance: 0m,
            ReconciliationVarianceFunctional: 0m,
            IsReconciled: true,
            ReconciliationStatus: "RECONCILED",
            IsFunctionalValuationResolved: true,
            IsGeneralLedgerCurrencyAligned: true,
            MissingExchangeRateLayerCount: 0,
            UnresolvedCurrencyIssue: null,
            SourceValuationByCurrency: [],
            GeneralLedger1200BalancesByCurrency: [],
            Layers: [],
            TotalValuation: 0m,
            GeneralLedger1200Balance: 0m,
            ReconciliationVariance: 0m,
            TotalValuationPkr: pkr,
            GeneralLedger1200BalancePkr: pkr,
            ReconciliationVariancePkr: pkr);
    }
}

public sealed record InventoryValuationSourceLayerInput(
    Guid LayerId,
    Guid? ParentLayerId,
    Guid VariantId,
    string Sku,
    Guid WarehouseId,
    string WarehouseCode,
    Guid BinId,
    string BinCode,
    string SourceDocumentType,
    Guid? SourceDocumentId,
    DateTimeOffset ReceivedAt,
    decimal OriginalQty,
    decimal RemainingQty,
    decimal SourceUnitCost,
    string SourceCurrencyCode,
    string StatusCode);

public sealed record InventoryValuationExchangeRateInput(
    string FromCurrencyCode,
    string ToCurrencyCode,
    DateTimeOffset EffectiveAt,
    decimal Rate,
    DateTimeOffset CreatedAt = default);

public sealed record InventoryValuationGlCurrencyBalanceInput(
    string FunctionalCurrencyCode,
    int JournalCount,
    decimal NetBalance);

/// <summary>
/// Currency-aware FIFO valuation and GL Account 1200 reconciliation calculator.
/// Never conflates unconverted foreign-currency amounts with the company's functional currency
/// and never falls back to future/current exchange rates when historical rates are absent.
/// </summary>
public static class InventoryValuationCalculator
{
    public static InventoryValuationReportSummary BuildReport(
        string companyBaseCurrencyCode,
        IReadOnlyList<InventoryValuationSourceLayerInput> sourceLayers,
        IReadOnlyList<InventoryValuationExchangeRateInput> exchangeRates,
        decimal totalPhysicalStock,
        IReadOnlyList<InventoryValuationGlCurrencyBalanceInput> gl1200BalancesByCurrency,
        DateTimeOffset? generatedAt = null)
    {
        if (string.IsNullOrWhiteSpace(companyBaseCurrencyCode))
        {
            throw new InvalidOperationException("Company base_currency_code is required for inventory valuation.");
        }

        var functionalCurrency = companyBaseCurrencyCode.Trim().ToUpperInvariant();
        var reportLayers = new List<InventoryValuationLayerReportItem>(sourceLayers.Count);
        decimal totalValuedStock = 0m;
        decimal functionalValuationSum = 0m;
        int missingRateCount = 0;

        foreach (var layer in sourceLayers)
        {
            var sourceCurrency = string.IsNullOrWhiteSpace(layer.SourceCurrencyCode)
                ? throw new InvalidOperationException($"Cost layer {layer.LayerId} is missing currency_code.")
                : layer.SourceCurrencyCode.Trim().ToUpperInvariant();
            var sourceExtended = Math.Round(layer.RemainingQty * layer.SourceUnitCost, 4, MidpointRounding.AwayFromZero);
            totalValuedStock += layer.RemainingQty;

            decimal? functionalUnitCost = null;
            decimal? functionalExtended = null;
            decimal? appliedRate = null;
            DateTimeOffset? rateEffectiveAt = null;
            string conversionStatus;

            if (string.Equals(sourceCurrency, functionalCurrency, StringComparison.Ordinal))
            {
                conversionStatus = "same_currency";
                appliedRate = 1m;
                functionalUnitCost = layer.SourceUnitCost;
                functionalExtended = sourceExtended;
                functionalValuationSum += functionalExtended.Value;
            }
            else
            {
                var matchedRate = ResolveHistoricalRate(sourceCurrency, functionalCurrency, layer.ReceivedAt, exchangeRates);
                if (matchedRate is null)
                {
                    conversionStatus = "missing_historical_rate";
                    missingRateCount++;
                }
                else
                {
                    conversionStatus = "converted_historical";
                    rateEffectiveAt = matchedRate.Value.EffectiveAt;
                    if (matchedRate.Value.IsInverse)
                    {
                        appliedRate = Math.Round(1m / matchedRate.Value.RawRate, 10, MidpointRounding.AwayFromZero);
                        functionalUnitCost = Math.Round(layer.SourceUnitCost / matchedRate.Value.RawRate, 6, MidpointRounding.AwayFromZero);
                        functionalExtended = Math.Round(sourceExtended / matchedRate.Value.RawRate, 4, MidpointRounding.AwayFromZero);
                    }
                    else
                    {
                        appliedRate = matchedRate.Value.RawRate;
                        functionalUnitCost = Math.Round(layer.SourceUnitCost * matchedRate.Value.RawRate, 6, MidpointRounding.AwayFromZero);
                        functionalExtended = Math.Round(sourceExtended * matchedRate.Value.RawRate, 4, MidpointRounding.AwayFromZero);
                    }

                    functionalValuationSum += functionalExtended.Value;
                }
            }

            reportLayers.Add(new InventoryValuationLayerReportItem(
                LayerId: layer.LayerId,
                ParentLayerId: layer.ParentLayerId,
                VariantId: layer.VariantId,
                Sku: layer.Sku,
                WarehouseId: layer.WarehouseId,
                WarehouseCode: layer.WarehouseCode,
                BinId: layer.BinId,
                BinCode: layer.BinCode,
                SourceDocumentType: layer.SourceDocumentType,
                SourceDocumentId: layer.SourceDocumentId,
                ReceivedAt: layer.ReceivedAt,
                OriginalQty: layer.OriginalQty,
                RemainingQty: layer.RemainingQty,
                SourceCurrencyCode: sourceCurrency,
                SourceUnitCost: layer.SourceUnitCost,
                SourceExtendedValuation: sourceExtended,
                FunctionalCurrencyCode: functionalCurrency,
                FunctionalUnitCost: functionalUnitCost,
                FunctionalExtendedValuation: functionalExtended,
                AppliedExchangeRate: appliedRate,
                ExchangeRateEffectiveAt: rateEffectiveAt,
                CurrencyConversionStatus: conversionStatus,
                StatusCode: layer.StatusCode));
        }

        var sourceByCurrency = reportLayers
            .GroupBy(l => l.SourceCurrencyCode, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new InventoryValuationCurrencyTotal(
                CurrencyCode: g.Key,
                Amount: Math.Round(g.Sum(x => x.SourceExtendedValuation), 4, MidpointRounding.AwayFromZero),
                ItemCount: g.Count(),
                Quantity: g.Sum(x => x.RemainingQty)))
            .ToList();

        var glByCurrency = gl1200BalancesByCurrency
            .Select(g => new InventoryValuationCurrencyTotal(
                CurrencyCode: g.FunctionalCurrencyCode.Trim().ToUpperInvariant(),
                Amount: Math.Round(g.NetBalance, 4, MidpointRounding.AwayFromZero),
                ItemCount: g.JournalCount))
            .OrderBy(g => g.CurrencyCode, StringComparer.Ordinal)
            .ToList();

        bool isFunctionalValuationResolved = missingRateCount == 0;
        decimal? functionalValuation = isFunctionalValuationResolved
            ? Math.Round(functionalValuationSum, 4, MidpointRounding.AwayFromZero)
            : null;

        var mismatchedGl = glByCurrency
            .Where(g => !string.Equals(g.CurrencyCode, functionalCurrency, StringComparison.Ordinal))
            .ToList();

        bool isGlAligned = mismatchedGl.Count == 0;
        decimal? gl1200FunctionalBalance = isGlAligned
            ? Math.Round(
                glByCurrency
                    .Where(g => string.Equals(g.CurrencyCode, functionalCurrency, StringComparison.Ordinal))
                    .Sum(g => g.Amount),
                4,
                MidpointRounding.AwayFromZero)
            : null;

        decimal? reconciliationVariance = functionalValuation.HasValue && gl1200FunctionalBalance.HasValue && isGlAligned
            ? Math.Round(functionalValuation.Value - gl1200FunctionalBalance.Value, 4, MidpointRounding.AwayFromZero)
            : null;

        bool isReconciled = isFunctionalValuationResolved
            && isGlAligned
            && reconciliationVariance.HasValue
            && reconciliationVariance.Value == 0m;

        string reconciliationStatus;
        string? unresolvedIssue = null;
        if (!isFunctionalValuationResolved && !isGlAligned)
        {
            reconciliationStatus = "UNRESOLVED_CURRENCY_MISMATCH";
            unresolvedIssue = BuildCurrencyMismatchMessage(functionalCurrency, sourceByCurrency, glByCurrency, missingRateCount);
        }
        else if (!isFunctionalValuationResolved)
        {
            reconciliationStatus = "MISSING_HISTORICAL_EXCHANGE_RATE";
            unresolvedIssue = BuildCurrencyMismatchMessage(functionalCurrency, sourceByCurrency, glByCurrency, missingRateCount);
        }
        else if (!isGlAligned)
        {
            reconciliationStatus = "GL_CURRENCY_MISMATCH";
            unresolvedIssue = BuildCurrencyMismatchMessage(functionalCurrency, sourceByCurrency, glByCurrency, missingRateCount);
        }
        else if (reconciliationVariance == 0m)
        {
            reconciliationStatus = "RECONCILED";
        }
        else
        {
            reconciliationStatus = "VARIANCE_DETECTED";
        }

        decimal? pkrValuation = string.Equals(functionalCurrency, "PKR", StringComparison.Ordinal) ? functionalValuation : null;
        decimal? pkrGl = string.Equals(functionalCurrency, "PKR", StringComparison.Ordinal) ? gl1200FunctionalBalance : null;
        decimal? pkrVariance = string.Equals(functionalCurrency, "PKR", StringComparison.Ordinal) ? reconciliationVariance : null;

        return new InventoryValuationReportSummary(
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            TotalActiveLayers: reportLayers.Count,
            TotalPhysicalStock: totalPhysicalStock,
            TotalValuedStock: totalValuedStock,
            TotalUnvaluedStock: Math.Max(0m, totalPhysicalStock - totalValuedStock),
            FunctionalCurrencyCode: functionalCurrency,
            FunctionalValuation: functionalValuation,
            GeneralLedger1200FunctionalBalance: gl1200FunctionalBalance,
            ReconciliationVarianceFunctional: reconciliationVariance,
            IsReconciled: isReconciled,
            ReconciliationStatus: reconciliationStatus,
            IsFunctionalValuationResolved: isFunctionalValuationResolved,
            IsGeneralLedgerCurrencyAligned: isGlAligned,
            MissingExchangeRateLayerCount: missingRateCount,
            UnresolvedCurrencyIssue: unresolvedIssue,
            SourceValuationByCurrency: sourceByCurrency,
            GeneralLedger1200BalancesByCurrency: glByCurrency,
            Layers: reportLayers,
            TotalValuation: functionalValuation,
            GeneralLedger1200Balance: gl1200FunctionalBalance,
            ReconciliationVariance: reconciliationVariance,
            TotalValuationPkr: pkrValuation,
            GeneralLedger1200BalancePkr: pkrGl,
            ReconciliationVariancePkr: pkrVariance);
    }

    private readonly record struct MatchedExchangeRate(
        decimal RawRate,
        bool IsInverse,
        DateTimeOffset EffectiveAt);

    private static MatchedExchangeRate? ResolveHistoricalRate(
        string sourceCurrency,
        string functionalCurrency,
        DateTimeOffset layerReceivedAt,
        IReadOnlyList<InventoryValuationExchangeRateInput> rates)
    {
        InventoryValuationExchangeRateInput? bestDirect = null;
        InventoryValuationExchangeRateInput? bestInverse = null;

        foreach (var r in rates)
        {
            if (r.Rate <= 0m || r.EffectiveAt > layerReceivedAt)
            {
                continue;
            }

            var from = r.FromCurrencyCode?.Trim().ToUpperInvariant();
            var to = r.ToCurrencyCode?.Trim().ToUpperInvariant();

            if (string.Equals(from, sourceCurrency, StringComparison.Ordinal) &&
                string.Equals(to, functionalCurrency, StringComparison.Ordinal))
            {
                if (bestDirect is null ||
                    r.EffectiveAt > bestDirect.EffectiveAt ||
                    (r.EffectiveAt == bestDirect.EffectiveAt && r.CreatedAt > bestDirect.CreatedAt))
                {
                    bestDirect = r;
                }
            }
            else if (string.Equals(from, functionalCurrency, StringComparison.Ordinal) &&
                     string.Equals(to, sourceCurrency, StringComparison.Ordinal))
            {
                if (bestInverse is null ||
                    r.EffectiveAt > bestInverse.EffectiveAt ||
                    (r.EffectiveAt == bestInverse.EffectiveAt && r.CreatedAt > bestInverse.CreatedAt))
                {
                    bestInverse = r;
                }
            }
        }

        if (bestDirect is null && bestInverse is null)
        {
            return null;
        }

        if (bestDirect is not null &&
            (bestInverse is null || bestDirect.EffectiveAt >= bestInverse.EffectiveAt))
        {
            return new MatchedExchangeRate(bestDirect.Rate, IsInverse: false, bestDirect.EffectiveAt);
        }

        return new MatchedExchangeRate(bestInverse!.Rate, IsInverse: true, bestInverse.EffectiveAt);
    }

    private static string BuildCurrencyMismatchMessage(
        string functionalCurrency,
        IReadOnlyList<InventoryValuationCurrencyTotal> sourceByCurrency,
        IReadOnlyList<InventoryValuationCurrencyTotal> glByCurrency,
        int missingRateCount)
    {
        var parts = new List<string>();
        if (missingRateCount > 0)
        {
            var sourceDesc = string.Join(", ", sourceByCurrency.Select(s => $"{s.CurrencyCode} {s.Amount:N2}"));
            parts.Add($"{missingRateCount} active FIFO layer(s) ({sourceDesc}) lack a historically applicable exchange rate to company functional currency {functionalCurrency}.");
        }

        var mismatchedGl = glByCurrency
            .Where(g => !string.Equals(g.CurrencyCode, functionalCurrency, StringComparison.Ordinal))
            .ToList();
        if (mismatchedGl.Count > 0)
        {
            var glDesc = string.Join(", ", mismatchedGl.Select(g => $"{g.CurrencyCode} {g.Amount:N2} ({g.ItemCount} journal(s))"));
            parts.Add($"GL Account 1200 contains posted journal(s) denominated in non-functional currency ({glDesc}) rather than {functionalCurrency}.");
        }

        return string.Join(" ", parts);
    }
}

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
    Task<InventoryValuationReportSummary> GetInventoryValuationReportAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken);
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

    /// <summary>
    /// Returns active FIFO layer valuation reconciled against physical available stock and GL Account 1200.
    /// </summary>
    public Task<InventoryValuationReportSummary> GetInventoryValuationReportAsync(CancellationToken ct)
    {
        var context = tenantContextAccessor.Current;
        if (context.TenantId == Guid.Empty || context.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("A valid tenant and company context is required to generate an inventory valuation report.");
        }

        return repository.GetInventoryValuationReportAsync(context.TenantId, context.CompanyId, ct);
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
