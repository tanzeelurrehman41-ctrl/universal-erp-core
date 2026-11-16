namespace UniversalErp.Application.SaaS;

public sealed class BillingEngine
{
    public sealed record ProrationResult(
        decimal UnusedOldPlanCredit,
        decimal NewPlanCharge,
        decimal NetPayableAmount,
        int RemainingDays,
        int TotalPeriodDays);

    public sealed record InvoiceCalculationResult(
        decimal Subtotal,
        decimal DiscountAmount,
        decimal CreditsApplied,
        decimal TaxRatePercentage,
        decimal TaxAmount,
        decimal TotalAmount);

    public ProrationResult CalculateProration(
        decimal oldBasePrice,
        decimal newBasePrice,
        DateTimeOffset currentPeriodStart,
        DateTimeOffset currentPeriodEnd,
        DateTimeOffset asOfDate)
    {
        var totalDays = Math.Max(1, (int)Math.Ceiling((currentPeriodEnd - currentPeriodStart).TotalDays));
        var remainingDays = Math.Max(0, (int)Math.Ceiling((currentPeriodEnd - asOfDate).TotalDays));

        if (remainingDays <= 0 || oldBasePrice <= 0 && newBasePrice <= 0)
        {
            return new ProrationResult(
                UnusedOldPlanCredit: 0.00m,
                NewPlanCharge: 0.00m,
                NetPayableAmount: Math.Max(0, newBasePrice - oldBasePrice),
                RemainingDays: 0,
                TotalPeriodDays: totalDays);
        }

        var fraction = (decimal)remainingDays / totalDays;
        var unusedCredit = Math.Round(oldBasePrice * fraction, 2);
        var newCharge = Math.Round(newBasePrice * fraction, 2);
        var netPayable = Math.Max(0.00m, newCharge - unusedCredit);

        return new ProrationResult(
            UnusedOldPlanCredit: unusedCredit,
            NewPlanCharge: newCharge,
            NetPayableAmount: netPayable,
            RemainingDays: remainingDays,
            TotalPeriodDays: totalDays);
    }

    public decimal ResolveTaxRate(string countryCode, string? provinceState)
    {
        if (!string.Equals(countryCode, "PK", StringComparison.OrdinalIgnoreCase))
        {
            return 0.00m; // International or export zero-rate
        }

        var province = provinceState?.ToUpperInvariant().Trim() ?? "FEDERAL";
        return province switch
        {
            "PUNJAB" => 16.0000m,      // Punjab Revenue Authority (PRA)
            "SINDH" => 13.0000m,       // Sindh Revenue Board (SRB)
            "KP" or "KPK" or "KHYBER_PAKHTUNKHWA" => 15.0000m, // KPRA
            "BALOCHISTAN" => 15.0000m, // BRA
            _ => 15.0000m              // ICT Federal Sales Tax on Services
        };
    }

    public InvoiceCalculationResult CalculateInvoice(
        decimal basePrice,
        decimal overageCharges,
        decimal discountAmount,
        decimal availableCredits,
        decimal taxRatePct)
    {
        var subtotal = Math.Max(0.00m, basePrice + overageCharges);
        var totalDiscount = Math.Min(subtotal, Math.Max(0.00m, discountAmount));
        var afterDiscount = subtotal - totalDiscount;

        var creditsApplied = Math.Min(afterDiscount, Math.Max(0.00m, availableCredits));
        var taxableBase = Math.Max(0.00m, afterDiscount - creditsApplied);

        var taxAmount = Math.Round(taxableBase * (taxRatePct / 100.0m), 2);
        var total = taxableBase + taxAmount;

        return new InvoiceCalculationResult(
            Subtotal: subtotal,
            DiscountAmount: totalDiscount,
            CreditsApplied: creditsApplied,
            TaxRatePercentage: taxRatePct,
            TaxAmount: taxAmount,
            TotalAmount: total);
    }

    public string GenerateInvoiceNumber(string tenantCode, DateTimeOffset asOfDate)
    {
        var datePart = asOfDate.ToString("yyyyMM");
        var randomSuffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return $"INV-{tenantCode.ToUpperInvariant()}-{datePart}-{randomSuffix}";
    }
}
