namespace UniversalErp.Application.Purchasing;

public enum ThreeWayMatchLineStatus
{
    ExactMatch,
    WithinTolerance,
    QuantityVarianceExceeded,
    PriceVarianceExceeded,
    Disputed,
    Unmatched
}

public sealed record ThreeWayMatchLineResult(
    int LineNumber,
    Guid VariantId,
    decimal OrderedQty,
    decimal ReceivedQty,
    decimal InvoicedQty,
    decimal ExpectedUnitPrice,
    decimal InvoicedUnitPrice,
    decimal QuantityVariance,
    decimal PriceVariance,
    decimal ExtendedPriceVariance,
    ThreeWayMatchLineStatus Status,
    string? Notes);

public sealed record ThreeWayMatchEvaluation(
    bool IsMatched,
    bool RequiresManualApproval,
    decimal TotalInvoicedAmount,
    decimal TotalExpectedAmount,
    decimal TotalNetVariance,
    string OverallStatus,
    IReadOnlyList<ThreeWayMatchLineResult> LineResults,
    IReadOnlyList<string> Discrepancies);

public static class ThreeWayMatchingEngine
{
    public static ThreeWayMatchEvaluation Evaluate(
        PurchaseOrderSummary? po,
        GrnSummary? grn,
        IReadOnlyList<CreateSupplierInvoiceLineRequest> invoiceLines,
        decimal priceTolerancePercent = 5.0m,
        decimal quantityTolerancePercent = 5.0m,
        bool autoApproveWithinTolerance = true)
    {
        var discrepancies = new List<string>();
        var lineResults = new List<ThreeWayMatchLineResult>();

        if (po is null && grn is null)
        {
            discrepancies.Add("No associated Purchase Order or Goods Received Note found for 3-way matching.");
            return new ThreeWayMatchEvaluation(
                IsMatched: false,
                RequiresManualApproval: true,
                TotalInvoicedAmount: invoiceLines.Sum(l => l.InvoicedQty * l.UnitPrice),
                TotalExpectedAmount: 0m,
                TotalNetVariance: invoiceLines.Sum(l => l.InvoicedQty * l.UnitPrice),
                OverallStatus: "unmatched",
                LineResults: [],
                Discrepancies: discrepancies);
        }

        var totalInvoiced = 0m;
        var totalExpected = 0m;
        var hasExceededTolerance = false;
        var hasVariance = false;

        foreach (var invLine in invoiceLines)
        {
            totalInvoiced += invLine.InvoicedQty * invLine.UnitPrice;

            // Find matching PO line and GRN line
            PurchaseOrderLineSummary? poLine = null;
            if (po is not null && invLine.PoLineNumber.HasValue)
            {
                poLine = po.Lines.FirstOrDefault(l => l.LineNumber == invLine.PoLineNumber.Value);
            }
            poLine ??= po?.Lines.FirstOrDefault(l => l.VariantId == invLine.VariantId);

            GrnLineSummary? grnLine = null;
            if (grn is not null && invLine.GrnLineNumber.HasValue)
            {
                grnLine = grn.Lines.FirstOrDefault(l => l.LineNumber == invLine.GrnLineNumber.Value);
            }
            grnLine ??= grn?.Lines.FirstOrDefault(l => l.VariantId == invLine.VariantId);

            var orderedQty = poLine?.OrderedQty ?? 0m;
            var receivedQty = grnLine?.ReceivedQty ?? poLine?.ReceivedQty ?? 0m;
            var expectedPrice = grnLine?.UnitPrice ?? poLine?.UnitPrice ?? 0m;

            var expectedLineTotal = (receivedQty > 0m ? receivedQty : orderedQty) * expectedPrice;
            totalExpected += expectedLineTotal;

            var qtyVariance = invLine.InvoicedQty - receivedQty;
            var priceVariance = invLine.UnitPrice - expectedPrice;
            var extPriceVariance = (invLine.UnitPrice - expectedPrice) * invLine.InvoicedQty;

            ThreeWayMatchLineStatus lineStatus;
            string? note = null;

            if (receivedQty == 0m && orderedQty == 0m)
            {
                lineStatus = ThreeWayMatchLineStatus.Unmatched;
                note = $"No receipt found for variant {invLine.VariantId}.";
                discrepancies.Add($"Line {invLine.LineNumber}: No receipt found.");
                hasExceededTolerance = true;
            }
            else if (qtyVariance == 0m && priceVariance == 0m)
            {
                lineStatus = ThreeWayMatchLineStatus.ExactMatch;
            }
            else
            {
                hasVariance = true;

                // Check quantity tolerance
                var qtyToleranceQty = receivedQty > 0m
                    ? (receivedQty * (quantityTolerancePercent / 100m))
                    : 0m;
                var isQtyWithin = Math.Abs(qtyVariance) <= qtyToleranceQty;

                // Check price tolerance
                var priceToleranceAmount = expectedPrice > 0m
                    ? (expectedPrice * (priceTolerancePercent / 100m))
                    : 0m;
                var isPriceWithin = Math.Abs(priceVariance) <= priceToleranceAmount;

                if (!isQtyWithin)
                {
                    lineStatus = ThreeWayMatchLineStatus.QuantityVarianceExceeded;
                    note = $"Quantity variance {qtyVariance:+0.##;-0.##} exceeds allowed tolerance of {quantityTolerancePercent}%.";
                    discrepancies.Add($"Line {invLine.LineNumber}: Invoiced {invLine.InvoicedQty} vs Received {receivedQty}.");
                    hasExceededTolerance = true;
                }
                else if (!isPriceWithin)
                {
                    lineStatus = ThreeWayMatchLineStatus.PriceVarianceExceeded;
                    note = $"Unit price variance {priceVariance:+0.##;-0.##} exceeds allowed tolerance of {priceTolerancePercent}%.";
                    discrepancies.Add($"Line {invLine.LineNumber}: Invoiced price {invLine.UnitPrice:F2} vs Expected {expectedPrice:F2}.");
                    hasExceededTolerance = true;
                }
                else
                {
                    lineStatus = ThreeWayMatchLineStatus.WithinTolerance;
                    note = $"Variance is within configured tolerance bounds (Qty {quantityTolerancePercent}%, Price {priceTolerancePercent}%).";
                }
            }

            lineResults.Add(new ThreeWayMatchLineResult(
                LineNumber: invLine.LineNumber,
                VariantId: invLine.VariantId,
                OrderedQty: orderedQty,
                ReceivedQty: receivedQty,
                InvoicedQty: invLine.InvoicedQty,
                ExpectedUnitPrice: expectedPrice,
                InvoicedUnitPrice: invLine.UnitPrice,
                QuantityVariance: qtyVariance,
                PriceVariance: priceVariance,
                ExtendedPriceVariance: extPriceVariance,
                Status: lineStatus,
                Notes: note));
        }

        var totalNetVariance = totalInvoiced - totalExpected;
        var isMatched = !hasExceededTolerance && (!hasVariance || autoApproveWithinTolerance);
        var overallStatus = hasExceededTolerance
            ? "variance_exceeded"
            : (hasVariance ? "matched_with_variance" : "matched");

        return new ThreeWayMatchEvaluation(
            IsMatched: isMatched,
            RequiresManualApproval: hasExceededTolerance || (hasVariance && !autoApproveWithinTolerance),
            TotalInvoicedAmount: totalInvoiced,
            TotalExpectedAmount: totalExpected,
            TotalNetVariance: totalNetVariance,
            OverallStatus: overallStatus,
            LineResults: lineResults,
            Discrepancies: discrepancies);
    }
}
