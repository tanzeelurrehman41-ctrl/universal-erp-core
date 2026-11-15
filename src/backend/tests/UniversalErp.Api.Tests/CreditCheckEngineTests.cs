using UniversalErp.Application.Sales;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class CreditCheckEngineTests
{
    [Fact]
    public void Evaluate_WhenCreditLimitIsZeroAndNewOrderPositive_RequiresOverrideForCashOnlyCustomer()
    {
        var result = CreditCheckEngine.Evaluate(
            creditLimit: 0m,
            currentOutstanding: 5_000m,
            uninvoicedOrdersTotal: 2_000m,
            newOrderAmount: 1_000m);

        Assert.False(result.IsAllowed);
        Assert.True(result.RequiresOverride);
        Assert.Equal(7_000m, result.CurrentExposure);
        Assert.Equal(0m, result.CreditLimit);
        Assert.Equal(1_000m, result.ExcessAmount);
        Assert.Contains("no credit limit facility", result.Message);
    }

    [Fact]
    public void Evaluate_WhenCreditLimitIsZeroAndNewOrderZero_IsAllowed()
    {
        var result = CreditCheckEngine.Evaluate(
            creditLimit: 0m,
            currentOutstanding: 5_000m,
            uninvoicedOrdersTotal: 2_000m,
            newOrderAmount: 0m);

        Assert.True(result.IsAllowed);
        Assert.False(result.RequiresOverride);
    }

    [Fact]
    public void Evaluate_WhenExposureWithinLimit_AllowsCreditWithoutOverride()
    {
        var result = CreditCheckEngine.Evaluate(
            creditLimit: 100_000m,
            currentOutstanding: 30_000m,
            uninvoicedOrdersTotal: 20_000m,
            newOrderAmount: 25_000m);

        Assert.True(result.IsAllowed);
        Assert.False(result.RequiresOverride);
        Assert.Equal(50_000m, result.CurrentExposure);
        Assert.Equal(100_000m, result.CreditLimit);
        Assert.Equal(0m, result.ExcessAmount);
        Assert.Contains("Credit check passed", result.Message);
    }

    [Fact]
    public void Evaluate_WhenExposureExceedsLimit_RequiresOverride()
    {
        var result = CreditCheckEngine.Evaluate(
            creditLimit: 50_000m,
            currentOutstanding: 30_000m,
            uninvoicedOrdersTotal: 15_000m,
            newOrderAmount: 10_000m);

        Assert.False(result.IsAllowed);
        Assert.True(result.RequiresOverride);
        Assert.Equal(45_000m, result.CurrentExposure);
        Assert.Equal(50_000m, result.CreditLimit);
        Assert.Equal(5_000m, result.ExcessAmount);
        Assert.Contains("CREDIT_LIMIT_EXCEEDED", result.Message);
    }

    [Fact]
    public void Evaluate_WhenExposureExactlyEqualsLimit_IsAllowed()
    {
        var result = CreditCheckEngine.Evaluate(
            creditLimit: 50_000m,
            currentOutstanding: 20_000m,
            uninvoicedOrdersTotal: 10_000m,
            newOrderAmount: 20_000m);

        Assert.True(result.IsAllowed);
        Assert.False(result.RequiresOverride);
        Assert.Equal(30_000m, result.CurrentExposure);
        Assert.Equal(0m, result.ExcessAmount);
    }
}
