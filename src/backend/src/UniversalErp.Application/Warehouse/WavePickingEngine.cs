using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalErp.Application.Warehouse;

public class PickPathItem
{
    public Guid ItemId { get; set; }
    public Guid BinId { get; set; }
    public string BinCode { get; set; } = string.Empty;
    public string? Aisle { get; set; }
    public string? Rack { get; set; }
    public string? Shelf { get; set; }
    public string? BinPosition { get; set; }
    public Guid ProductVariantId { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public string Status { get; set; } = "pending";
}

public class WaveProgressSummary
{
    public int TotalLines { get; set; }
    public int CompletedLines { get; set; }
    public int ShortageLines { get; set; }
    public decimal TotalRequestedUnits { get; set; }
    public decimal TotalPickedUnits { get; set; }
    public decimal CompletionPercentage { get; set; }
    public bool IsFullyPicked => CompletedLines == TotalLines && TotalLines > 0;
}

public static class WavePickingEngine
{
    public static IReadOnlyList<PickPathItem> SequencePickPath(IReadOnlyList<PickPathItem> items)
    {
        if (items == null || items.Count <= 1)
            return items?.ToList() ?? new List<PickPathItem>();

        // Group by aisle
        var grouped = items.GroupBy(i => i.Aisle ?? "ZZ").OrderBy(g => g.Key).ToList();
        var sequenced = new List<PickPathItem>();

        for (var i = 0; i < grouped.Count; i++)
        {
            var group = grouped[i];
            var isEven = i % 2 == 0;

            var sortedInAisle = isEven
                ? group.OrderBy(x => x.Rack ?? "").ThenBy(x => x.Shelf ?? "").ThenBy(x => x.BinPosition ?? "").ThenBy(x => x.BinCode)
                : group.OrderByDescending(x => x.Rack ?? "").ThenByDescending(x => x.Shelf ?? "").ThenByDescending(x => x.BinPosition ?? "").ThenByDescending(x => x.BinCode);

            sequenced.AddRange(sortedInAisle);
        }

        return sequenced;
    }

    public static WaveProgressSummary CalculateWaveProgress(IReadOnlyList<PickPathItem> items)
    {
        if (items == null || items.Count == 0)
        {
            return new WaveProgressSummary
            {
                TotalLines = 0,
                CompletedLines = 0,
                ShortageLines = 0,
                TotalRequestedUnits = 0,
                TotalPickedUnits = 0,
                CompletionPercentage = 0
            };
        }

        var totalLines = items.Count;
        var completedLines = items.Count(i => i.Status == "picked" || i.PickedQuantity >= i.RequestedQuantity);
        var shortageLines = items.Count(i => i.Status == "shortage");
        var requestedUnits = items.Sum(i => i.RequestedQuantity);
        var pickedUnits = items.Sum(i => i.PickedQuantity);

        var pct = requestedUnits > 0
            ? Math.Round((pickedUnits / requestedUnits) * 100m, 1)
            : 0;

        return new WaveProgressSummary
        {
            TotalLines = totalLines,
            CompletedLines = completedLines,
            ShortageLines = shortageLines,
            TotalRequestedUnits = requestedUnits,
            TotalPickedUnits = pickedUnits,
            CompletionPercentage = pct
        };
    }

    public static (bool IsValid, string? ErrorMessage) ValidatePickConfirmation(
        PickPathItem item,
        decimal quantityToConfirm,
        string? scannedBinBarcode,
        string expectedBinCode,
        string? scannedProductBarcode,
        string? expectedProductSku = null)
    {
        if (quantityToConfirm <= 0)
            return (false, "Confirmed pick quantity must be greater than zero.");

        if (item.PickedQuantity + quantityToConfirm > item.RequestedQuantity)
            return (false, $"Confirmed pick quantity ({item.PickedQuantity + quantityToConfirm}) exceeds requested quantity ({item.RequestedQuantity}). Over-picking not permitted.");

        if (!string.IsNullOrWhiteSpace(scannedBinBarcode))
        {
            var parsedBin = BarcodeEngine.ParseStandardBarcode(scannedBinBarcode);
            var binIdent = string.IsNullOrEmpty(parsedBin.RawIdentifier) ? scannedBinBarcode.Trim() : parsedBin.RawIdentifier;
            if (!string.Equals(binIdent, expectedBinCode.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Bin scan mismatch: scanned '{binIdent}', but item is located in '{expectedBinCode}'.");
            }
        }

        if (!string.IsNullOrWhiteSpace(scannedProductBarcode) && !string.IsNullOrWhiteSpace(expectedProductSku))
        {
            var parsedProd = BarcodeEngine.ParseStandardBarcode(scannedProductBarcode);
            var prodIdent = string.IsNullOrEmpty(parsedProd.RawIdentifier) ? scannedProductBarcode.Trim() : parsedProd.RawIdentifier;

            if (prodIdent.StartsWith("(01)"))
            {
                var gs1 = BarcodeEngine.ParseGs1Barcode(prodIdent);
                if (gs1.Gtin != null && !string.Equals(gs1.Gtin, expectedProductSku.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"Product GTIN mismatch: scanned '{gs1.Gtin}', expected '{expectedProductSku}'.");
                }
            }
            else if (!string.Equals(prodIdent, expectedProductSku.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Product scan mismatch: scanned '{prodIdent}', expected '{expectedProductSku}'.");
            }
        }

        return (true, null);
    }
}
