namespace UniversalErp.Application.Inventory;

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
    DateOnly EffectiveDate,
    string RateType,
    decimal Rate,
    DateTimeOffset CreatedAt = default);

public sealed record InventoryValuationGlCurrencyBalanceInput(
    string FunctionalCurrencyCode,
    int JournalCount,
    decimal NetBalance);

/// <summary>
/// Currency-aware FIFO valuation, GL Account 1200 reconciliation, and IAS 21
/// presentation-currency calculator. Never conflates unconverted foreign-currency
/// amounts with the company's functional currency and never fabricates missing rates.
/// </summary>
public static class InventoryValuationCalculator
{
    public static InventoryValuationReportSummary BuildReport(
        string companyFunctionalCurrencyCode,
        string companyPresentationCurrencyCode,
        IReadOnlyList<InventoryValuationSourceLayerInput> sourceLayers,
        IReadOnlyList<InventoryValuationExchangeRateInput> exchangeRates,
        decimal totalPhysicalStock,
        IReadOnlyList<InventoryValuationGlCurrencyBalanceInput> gl1200BalancesByCurrency,
        DateTimeOffset? generatedAt = null,
        DateOnly? asOfDate = null)
    {
        if (string.IsNullOrWhiteSpace(companyFunctionalCurrencyCode))
        {
            throw new InvalidOperationException("Company functional_currency_code is required for inventory valuation.");
        }

        var functionalCurrency = companyFunctionalCurrencyCode.Trim().ToUpperInvariant();
        var presentationCurrency = string.IsNullOrWhiteSpace(companyPresentationCurrencyCode)
            ? functionalCurrency
            : companyPresentationCurrencyCode.Trim().ToUpperInvariant();
        var effectiveTimestamp = generatedAt ?? DateTimeOffset.UtcNow;
        var effectiveDate = asOfDate ?? DateOnly.FromDateTime(effectiveTimestamp.UtcDateTime);

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
                UnitCost: layer.SourceUnitCost,
                ExtendedValuation: sourceExtended,
                CurrencyCode: sourceCurrency,
                StatusCode: layer.StatusCode,
                SourceCurrencyCode: sourceCurrency,
                SourceUnitCost: layer.SourceUnitCost,
                SourceExtendedValuation: sourceExtended,
                FunctionalCurrencyCode: functionalCurrency,
                FunctionalUnitCost: functionalUnitCost,
                FunctionalExtendedValuation: functionalExtended,
                AppliedExchangeRate: appliedRate,
                ExchangeRateEffectiveAt: rateEffectiveAt,
                CurrencyConversionStatus: conversionStatus));
        }

        var sourceByCurrency = reportLayers
            .GroupBy(l => l.SourceCurrencyCode ?? l.CurrencyCode, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new InventoryValuationCurrencyTotal(
                CurrencyCode: g.Key,
                Amount: Math.Round(g.Sum(x => x.SourceExtendedValuation ?? x.ExtendedValuation), 4, MidpointRounding.AwayFromZero),
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

        // IAS 21 Presentation Translation (Assets translate at Closing Rate as of reporting date)
        decimal? presentationValuation = null;
        decimal? presentationGlBalance = null;
        decimal? presentationRate = null;
        DateOnly? presentationRateEffectiveDate = null;
        string? presentationRateType = null;
        string presentationStatus;
        string? presentationIssue = null;

        if (string.Equals(functionalCurrency, presentationCurrency, StringComparison.Ordinal))
        {
            presentationValuation = functionalValuation;
            presentationGlBalance = gl1200FunctionalBalance;
            presentationRate = 1m;
            presentationRateEffectiveDate = effectiveDate;
            presentationRateType = "same_currency";
            presentationStatus = isFunctionalValuationResolved && isGlAligned ? "SAME_CURRENCY" : "UNRESOLVED_FUNCTIONAL_CURRENCY";
            presentationIssue = unresolvedIssue;
        }
        else if (!isFunctionalValuationResolved || !isGlAligned)
        {
            presentationStatus = "UNRESOLVED_FUNCTIONAL_CURRENCY";
            presentationIssue = unresolvedIssue;
        }
        else
        {
            var closingRate = ResolveClosingRate(functionalCurrency, presentationCurrency, effectiveDate, exchangeRates);
            if (closingRate is null)
            {
                presentationStatus = "MISSING_CLOSING_EXCHANGE_RATE";
                presentationIssue = $"Missing closing exchange rate for {functionalCurrency}/{presentationCurrency} on or before {effectiveDate:yyyy-MM-dd}. {presentationCurrency} presentation figures are unavailable.";
            }
            else
            {
                presentationRateType = "closing";
                presentationRateEffectiveDate = closingRate.Value.EffectiveDate;
                presentationRate = closingRate.Value.IsInverse
                    ? Math.Round(1m / closingRate.Value.RawRate, 10, MidpointRounding.AwayFromZero)
                    : closingRate.Value.RawRate;

                presentationValuation = closingRate.Value.IsInverse
                    ? Math.Round(functionalValuation!.Value / closingRate.Value.RawRate, 2, MidpointRounding.AwayFromZero)
                    : Math.Round(functionalValuation!.Value * closingRate.Value.RawRate, 2, MidpointRounding.AwayFromZero);

                presentationGlBalance = closingRate.Value.IsInverse
                    ? Math.Round(gl1200FunctionalBalance!.Value / closingRate.Value.RawRate, 2, MidpointRounding.AwayFromZero)
                    : Math.Round(gl1200FunctionalBalance!.Value * closingRate.Value.RawRate, 2, MidpointRounding.AwayFromZero);

                presentationStatus = "TRANSLATED_CLOSING_RATE";
            }
        }

        decimal? pkrValuation = string.Equals(functionalCurrency, "PKR", StringComparison.Ordinal) ? functionalValuation : null;
        decimal? pkrGl = string.Equals(functionalCurrency, "PKR", StringComparison.Ordinal) ? gl1200FunctionalBalance : null;
        decimal? pkrVariance = string.Equals(functionalCurrency, "PKR", StringComparison.Ordinal) ? reconciliationVariance : null;

        return new InventoryValuationReportSummary(
            GeneratedAt: effectiveTimestamp,
            TotalActiveLayers: reportLayers.Count,
            TotalPhysicalStock: totalPhysicalStock,
            TotalValuedStock: totalValuedStock,
            TotalUnvaluedStock: Math.Max(0m, totalPhysicalStock - totalValuedStock),
            TotalValuationPkr: pkrValuation ?? 0m,
            GeneralLedger1200BalancePkr: pkrGl ?? 0m,
            ReconciliationVariancePkr: pkrVariance ?? 0m,
            IsReconciled: isReconciled,
            Layers: reportLayers,
            FunctionalCurrencyCode: functionalCurrency,
            FunctionalValuation: functionalValuation,
            GeneralLedger1200FunctionalBalance: gl1200FunctionalBalance,
            ReconciliationVarianceFunctional: reconciliationVariance,
            ReconciliationStatus: reconciliationStatus,
            IsFunctionalValuationResolved: isFunctionalValuationResolved,
            IsGeneralLedgerCurrencyAligned: isGlAligned,
            MissingExchangeRateLayerCount: missingRateCount,
            UnresolvedCurrencyIssue: unresolvedIssue,
            SourceValuationByCurrency: sourceByCurrency,
            GeneralLedger1200BalancesByCurrency: glByCurrency,
            TotalValuation: functionalValuation,
            GeneralLedger1200Balance: gl1200FunctionalBalance,
            ReconciliationVariance: reconciliationVariance,
            PresentationCurrencyCode: presentationCurrency,
            PresentationValuation: presentationValuation,
            GeneralLedger1200PresentationBalance: presentationGlBalance,
            PresentationAppliedExchangeRate: presentationRate,
            PresentationExchangeRateEffectiveDate: presentationRateEffectiveDate,
            PresentationExchangeRateType: presentationRateType,
            PresentationStatus: presentationStatus,
            PresentationIssue: presentationIssue);
    }

    private readonly record struct MatchedRate(
        decimal RawRate,
        bool IsInverse,
        DateTimeOffset EffectiveAt,
        DateOnly EffectiveDate);

    private static MatchedRate? ResolveHistoricalRate(
        string sourceCurrency,
        string functionalCurrency,
        DateTimeOffset layerReceivedAt,
        IReadOnlyList<InventoryValuationExchangeRateInput> rates)
    {
        var receivedDate = DateOnly.FromDateTime(layerReceivedAt.UtcDateTime);
        InventoryValuationExchangeRateInput? bestDirect = null;
        InventoryValuationExchangeRateInput? bestInverse = null;

        foreach (var r in rates)
        {
            if (r.Rate <= 0m ||
                string.Equals(r.RateType, "closing", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.RateType, "average", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (r.EffectiveAt > layerReceivedAt && r.EffectiveDate > receivedDate)
            {
                continue;
            }
            if (r.EffectiveDate > receivedDate)
            {
                continue;
            }

            var from = r.FromCurrencyCode?.Trim().ToUpperInvariant();
            var to = r.ToCurrencyCode?.Trim().ToUpperInvariant();

            if (string.Equals(from, sourceCurrency, StringComparison.Ordinal) &&
                string.Equals(to, functionalCurrency, StringComparison.Ordinal))
            {
                if (bestDirect is null || r.EffectiveAt > bestDirect.EffectiveAt ||
                    (r.EffectiveAt == bestDirect.EffectiveAt && r.CreatedAt > bestDirect.CreatedAt))
                {
                    bestDirect = r;
                }
            }
            else if (string.Equals(from, functionalCurrency, StringComparison.Ordinal) &&
                     string.Equals(to, sourceCurrency, StringComparison.Ordinal))
            {
                if (bestInverse is null || r.EffectiveAt > bestInverse.EffectiveAt ||
                    (r.EffectiveAt == bestInverse.EffectiveAt && r.CreatedAt > bestInverse.CreatedAt))
                {
                    bestInverse = r;
                }
            }
        }

        if (bestDirect is null && bestInverse is null) return null;

        if (bestDirect is not null && (bestInverse is null || bestDirect.EffectiveAt >= bestInverse.EffectiveAt))
        {
            return new MatchedRate(bestDirect.Rate, IsInverse: false, bestDirect.EffectiveAt, bestDirect.EffectiveDate);
        }

        return new MatchedRate(bestInverse!.Rate, IsInverse: true, bestInverse.EffectiveAt, bestInverse.EffectiveDate);
    }

    private static MatchedRate? ResolveClosingRate(
        string functionalCurrency,
        string presentationCurrency,
        DateOnly asOfDate,
        IReadOnlyList<InventoryValuationExchangeRateInput> rates)
    {
        InventoryValuationExchangeRateInput? bestDirect = null;
        InventoryValuationExchangeRateInput? bestInverse = null;

        foreach (var r in rates)
        {
            if (r.Rate <= 0m ||
                !string.Equals(r.RateType, "closing", StringComparison.OrdinalIgnoreCase) ||
                r.EffectiveDate > asOfDate)
            {
                continue;
            }

            var from = r.FromCurrencyCode?.Trim().ToUpperInvariant();
            var to = r.ToCurrencyCode?.Trim().ToUpperInvariant();

            if (string.Equals(from, functionalCurrency, StringComparison.Ordinal) &&
                string.Equals(to, presentationCurrency, StringComparison.Ordinal))
            {
                if (bestDirect is null || r.EffectiveDate > bestDirect.EffectiveDate ||
                    (r.EffectiveDate == bestDirect.EffectiveDate && r.CreatedAt > bestDirect.CreatedAt))
                {
                    bestDirect = r;
                }
            }
            else if (string.Equals(from, presentationCurrency, StringComparison.Ordinal) &&
                     string.Equals(to, functionalCurrency, StringComparison.Ordinal))
            {
                if (bestInverse is null || r.EffectiveDate > bestInverse.EffectiveDate ||
                    (r.EffectiveDate == bestInverse.EffectiveDate && r.CreatedAt > bestInverse.CreatedAt))
                {
                    bestInverse = r;
                }
            }
        }

        if (bestDirect is null && bestInverse is null) return null;

        if (bestDirect is not null && (bestInverse is null || bestDirect.EffectiveDate >= bestInverse.EffectiveDate))
        {
            return new MatchedRate(bestDirect.Rate, IsInverse: false, bestDirect.EffectiveAt, bestDirect.EffectiveDate);
        }

        return new MatchedRate(bestInverse!.Rate, IsInverse: true, bestInverse.EffectiveAt, bestInverse.EffectiveDate);
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
