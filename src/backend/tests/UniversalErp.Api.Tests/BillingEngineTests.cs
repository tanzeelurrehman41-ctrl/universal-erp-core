using UniversalErp.Application.SaaS;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class BillingEngineTests
{
    private readonly BillingEngine _billing = new();

    // ── Proration Tests ─────────────────────────────────────────────────────

    [Fact]
    public void CalculateProration_MidCycleUpgrade_CalculatesCorrectCreditsAndCharges()
    {
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero); // 30 days
        var asOf = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero); // 15 days remaining

        var oldPrice = 15000.00m; // Starter
        var newPrice = 45000.00m; // Professional

        var proration = _billing.CalculateProration(oldPrice, newPrice, start, end, asOf);

        Assert.Equal(30, proration.TotalPeriodDays);
        Assert.Equal(15, proration.RemainingDays);
        Assert.Equal(7500.00m, proration.UnusedOldPlanCredit);
        Assert.Equal(22500.00m, proration.NewPlanCharge);
        Assert.Equal(15000.00m, proration.NetPayableAmount);
    }

    [Fact]
    public void CalculateProration_WhenZeroDaysRemaining_ReturnsFullOrZeroPayable()
    {
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var asOf = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero); // 0 days remaining

        var proration = _billing.CalculateProration(15000m, 45000m, start, end, asOf);

        Assert.Equal(0, proration.RemainingDays);
        Assert.Equal(0m, proration.UnusedOldPlanCredit);
        Assert.Equal(30000m, proration.NetPayableAmount);
    }

    // ── Standard Jurisdictional Tax Resolution Tests ────────────────────────

    [Theory]
    [InlineData("PUNJAB", 16.0)]
    [InlineData("SINDH", 15.0)] // Corrected standard rate under Sindh Finance Act 2024
    [InlineData("KP", 15.0)]
    [InlineData("BALOCHISTAN", 15.0)]
    [InlineData("FEDERAL", 15.0)]
    [InlineData(null, 15.0)]
    public void ResolveTaxRate_PakistaniJurisdictions_ReturnsStatutoryRate(string? province, decimal expectedRate)
    {
        var rate = _billing.ResolveTaxRate("PK", province);
        Assert.Equal(expectedRate, rate);
    }

    [Fact]
    public void ResolveTaxRate_International_ReturnsZeroPercent()
    {
        var rate = _billing.ResolveTaxRate("US", "CA");
        Assert.Equal(0m, rate);
    }

    // ── Service-Specific Statutory Classification & Place of Provision Tests ─

    [Theory]
    [InlineData("SINDH", "SRB", 15.0, "Sindh Sales Tax on Services Act, 2011, Sec 8(1) as amended by Sindh Finance Act 2024")]
    [InlineData("PUNJAB", "PRA", 16.0, "Punjab Sales Tax on Services Act, 2012, Second Schedule, Heading 9815.6000")]
    [InlineData("FEDERAL", "FBR_ICT", 15.0, "Islamabad Capital Territory (Tax on Services) Ordinance, 2001, Schedule Table, Heading 9815.6000")]
    [InlineData("KP", "KPRA", 15.0, "Khyber Pakhtunkhwa Sales Tax on Services Act, 2022, Second Schedule, Heading 9815.6000")]
    [InlineData("BALOCHISTAN", "BRA", 15.0, "Balochistan Sales Tax on Services Act, 2015, Second Schedule, Heading 9815.6000")]
    public void ResolveStatutoryTaxRule_StandardSaaSWithInputTax_ResolvesCorrectJurisdictionAndAuthority(
        string province, string expectedAuthority, decimal expectedRate, string expectedRef)
    {
        var result = _billing.ResolveStatutoryTaxRule(new BillingEngine.TaxResolutionRequest(
            CountryCode: "PK",
            ProvinceState: province,
            TariffCode: "9815.6000",
            ProviderElectedReducedRateWithoutInputTax: false));

        Assert.Equal(expectedAuthority, result.Authority);
        Assert.Equal(expectedRate, result.RatePercentage);
        Assert.Equal("standard_with_input_tax", result.TaxRegime);
        Assert.True(result.InputTaxAllowable);
        Assert.Contains(expectedRef, result.StatutoryReference);
    }

    [Theory]
    [InlineData("SINDH", "SRB", 3.0)]       // SRB 3% without input tax credit
    [InlineData("PUNJAB", "PRA", 5.0)]     // PRA 5% without input tax credit
    [InlineData("FEDERAL", "FBR_ICT", 5.0)] // ICT 5% for PSEB certified IT services
    [InlineData("KP", "KPRA", 2.0)]        // KPRA 2% without input tax credit
    [InlineData("BALOCHISTAN", "BRA", 3.0)] // BRA 3% without input tax credit
    public void ResolveStatutoryTaxRule_ConditionalReducedRates_AppliesWhenElectedWithoutInputCredit(
        string province, string expectedAuthority, decimal expectedRate)
    {
        var result = _billing.ResolveStatutoryTaxRule(new BillingEngine.TaxResolutionRequest(
            CountryCode: "PK",
            ProvinceState: province,
            TariffCode: "9815.6000",
            ProviderElectedReducedRateWithoutInputTax: true));

        Assert.Equal(expectedAuthority, result.Authority);
        Assert.Equal(expectedRate, result.RatePercentage);
        Assert.Equal("reduced_without_input_tax", result.TaxRegime);
        Assert.False(result.InputTaxAllowable);
        Assert.NotNull(result.EligibilityConditions);
    }

    [Fact]
    public void ResolveStatutoryTaxRule_IneligibleOrUnelectedReducedRate_FallsBackToStandard15PercentSindh()
    {
        // Provider does NOT elect to surrender input tax credit -> MUST NOT receive 3% reduced rate
        var result = _billing.ResolveStatutoryTaxRule(new BillingEngine.TaxResolutionRequest(
            CountryCode: "PK",
            ProvinceState: "SINDH",
            TariffCode: "9815.6000",
            ProviderElectedReducedRateWithoutInputTax: false));

        Assert.Equal(15.0m, result.RatePercentage);
        Assert.Equal("standard_with_input_tax", result.TaxRegime);
        Assert.True(result.InputTaxAllowable);
    }

    [Fact]
    public void ResolveStatutoryTaxRule_EffectiveDateChange_ResolvesHistoricalSindhRateAccurately()
    {
        // Historical period: Before July 1, 2024, Sindh rate was 13%
        var historicalDate = new DateTimeOffset(2024, 5, 15, 0, 0, 0, TimeSpan.Zero);
        var historicalResult = _billing.ResolveStatutoryTaxRule(new BillingEngine.TaxResolutionRequest(
            CountryCode: "PK",
            ProvinceState: "SINDH",
            AsOfDate: historicalDate));

        Assert.Equal(13.0m, historicalResult.RatePercentage);
        Assert.Contains("pre-2024", historicalResult.StatutoryReference);

        // Modern period: July 1, 2024 onward under Sindh Finance Act 2024, rate is 15%
        var modernDate = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
        var modernResult = _billing.ResolveStatutoryTaxRule(new BillingEngine.TaxResolutionRequest(
            CountryCode: "PK",
            ProvinceState: "SINDH",
            AsOfDate: modernDate));

        Assert.Equal(15.0m, modernResult.RatePercentage);
        Assert.Contains("Sindh Finance Act 2024", modernResult.StatutoryReference);
    }

    [Fact]
    public void ResolveStatutoryTaxRule_ExportOfServices_ReturnsZeroRated()
    {
        var exportResult = _billing.ResolveStatutoryTaxRule(new BillingEngine.TaxResolutionRequest(
            CountryCode: "PK",
            ProvinceState: "SINDH",
            IsExport: true,
            HasValidForeignRemittanceProof: true));

        Assert.Equal("EXPORT", exportResult.Jurisdiction);
        Assert.Equal(0.0m, exportResult.RatePercentage);
        Assert.Equal("export_zero_rated", exportResult.TaxRegime);
        Assert.Contains("Export Zero-Rating", exportResult.StatutoryReference);
    }

    // ── Invoice Calculation Tests (Tax-Exclusive & Tax-Inclusive) ───────────

    [Fact]
    public void CalculateInvoice_TaxExclusiveStandardPricing_ComputesAccurateTotals()
    {
        // Base: 45,000, Overage: 5,000, Discount: 2,000, Credits: 3,000, Tax: 15% (Sindh Standard)
        // Subtotal = 50,000
        // After discount = 48,000
        // Credits applied = 3,000
        // Taxable base = 45,000
        // Tax = 45,000 * 0.15 = 6,750
        // Total = 45,000 + 6,750 = 51,750
        var calc = _billing.CalculateInvoice(
            basePrice: 45000m,
            overageCharges: 5000m,
            discountAmount: 2000m,
            availableCredits: 3000m,
            taxRatePct: 15.0m,
            isTaxInclusive: false);

        Assert.Equal(50000m, calc.Subtotal);
        Assert.Equal(2000m, calc.DiscountAmount);
        Assert.Equal(3000m, calc.CreditsApplied);
        Assert.Equal(6750m, calc.TaxAmount);
        Assert.Equal(51750m, calc.TotalAmount);
        Assert.False(calc.IsTaxInclusive);
    }

    [Fact]
    public void CalculateInvoice_TaxInclusivePricing_ExtractsBaseAndTaxWithoutRoundingLeakage()
    {
        // Advertised total price: PKR 115,000 inclusive of 15% sales tax
        // Taxable base = 115,000 / 1.15 = 100,000.00
        // Tax amount = 115,000 - 100,000 = 15,000.00
        // Total = 115,000.00
        var calc = _billing.CalculateInvoice(
            basePrice: 115000m,
            overageCharges: 0m,
            discountAmount: 0m,
            availableCredits: 0m,
            taxRatePct: 15.0m,
            isTaxInclusive: true);

        Assert.Equal(100000m, calc.Subtotal - calc.TaxAmount); // Taxable base is 100,000
        Assert.Equal(15000m, calc.TaxAmount);
        Assert.Equal(115000m, calc.TotalAmount);
        Assert.True(calc.IsTaxInclusive);
    }

    [Fact]
    public void CalculateInvoice_TaxInclusiveWithUnevenFraction_RoundsToExactTotalWithoutLeakage()
    {
        // Advertised total price: PKR 100,000 inclusive of 15% sales tax
        // Base = round(100,000 / 1.15) = 86,956.52
        // Tax = 100,000 - 86,956.52 = 13,043.48
        // Total = exactly 100,000.00
        var calc = _billing.CalculateInvoice(
            basePrice: 100000m,
            overageCharges: 0m,
            discountAmount: 0m,
            availableCredits: 0m,
            taxRatePct: 15.0m,
            isTaxInclusive: true);

        Assert.Equal(13043.48m, calc.TaxAmount);
        Assert.Equal(100000.00m, calc.TotalAmount);
    }

    // ── Credit Note Calculation Tests ───────────────────────────────────────

    [Fact]
    public void CalculateCreditNote_TaxExclusive_ReversesProportionalSalesTax()
    {
        // Refund of PKR 20,000 on 15% standard rate
        // Tax reversed = 20,000 * 0.15 = 3,000
        // Total credit note = 23,000
        var credit = _billing.CalculateCreditNote(
            refundAmount: 20000m,
            taxRatePercentage: 15.0m,
            isTaxInclusive: false);

        Assert.Equal(20000m, credit.RefundSubtotal);
        Assert.Equal(3000m, credit.TaxReversed);
        Assert.Equal(23000m, credit.TotalCreditNoteAmount);
    }

    [Fact]
    public void CalculateCreditNote_TaxInclusive_ExtractsOriginalBaseAndTaxComponent()
    {
        // Refund of PKR 23,000 tax-inclusive on 15% rate
        // Subtotal = 23,000 / 1.15 = 20,000
        // Tax reversed = 3,000
        // Total credit note = 23,000
        var credit = _billing.CalculateCreditNote(
            refundAmount: 23000m,
            taxRatePercentage: 15.0m,
            isTaxInclusive: true);

        Assert.Equal(20000m, credit.RefundSubtotal);
        Assert.Equal(3000m, credit.TaxReversed);
        Assert.Equal(23000m, credit.TotalCreditNoteAmount);
    }

    // ── Invoice Number Generation ───────────────────────────────────────────

    [Fact]
    public void GenerateInvoiceNumber_ProducesStandardCompliantFormat()
    {
        var invNum = _billing.GenerateInvoiceNumber("ACME", new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero));
        Assert.StartsWith("INV-ACME-202611-", invNum);
        Assert.True(invNum.Length >= 20);
    }
}
