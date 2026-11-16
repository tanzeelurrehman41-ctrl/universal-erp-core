using UniversalErp.Application.SaaS;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class BillingEngineTests
{
    private readonly BillingEngine _billing = new();

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

    [Theory]
    [InlineData("PUNJAB", 16.0)]
    [InlineData("SINDH", 13.0)]
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

    [Fact]
    public void CalculateInvoice_WithDiscountsCreditsAndTax_ComputesCorrectTotals()
    {
        // Base: 45,000, Overage: 5,000, Discount: 2,000, Credits: 3,000, Tax: 16% (Punjab)
        // Subtotal = 50,000
        // After discount = 48,000
        // Credits applied = 3,000
        // Taxable base = 45,000
        // Tax = 45,000 * 0.16 = 7,200
        // Total = 45,000 + 7,200 = 52,200
        var calc = _billing.CalculateInvoice(
            basePrice: 45000m,
            overageCharges: 5000m,
            discountAmount: 2000m,
            availableCredits: 3000m,
            taxRatePct: 16.0m);

        Assert.Equal(50000m, calc.Subtotal);
        Assert.Equal(2000m, calc.DiscountAmount);
        Assert.Equal(3000m, calc.CreditsApplied);
        Assert.Equal(7200m, calc.TaxAmount);
        Assert.Equal(52200m, calc.TotalAmount);
    }

    [Fact]
    public void GenerateInvoiceNumber_ProducesStandardCompliantFormat()
    {
        var invNum = _billing.GenerateInvoiceNumber("ACME", new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero));
        Assert.StartsWith("INV-ACME-202611-", invNum);
        Assert.True(invNum.Length >= 20);
    }
}
