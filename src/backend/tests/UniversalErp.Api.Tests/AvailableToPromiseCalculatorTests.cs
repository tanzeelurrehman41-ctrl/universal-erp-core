using UniversalErp.Application.Inventory;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class AvailableToPromiseCalculatorTests
{
    [Fact]
    public void Calculate_WithStandardStock_ComputesAtpAndSellableAtpCorrectly()
    {
        var slice = new AtpStockSlice(
            VariantId: Guid.NewGuid(),
            WarehouseId: Guid.NewGuid(),
            BinId: Guid.NewGuid(),
            PhysicalOnHand: 50m,
            ActiveReservedQty: 10m,
            ValuedQty: 45m);

        var result = AvailableToPromiseCalculator.Calculate(slice);

        Assert.Equal(50m, result.PhysicalOnHand);
        Assert.Equal(10m, result.ActiveReservedQty);
        Assert.Equal(40m, result.AvailableToPromiseQty); // 50 - 10
        Assert.Equal(40m, result.SellableAtpQty); // min(45, 40)
    }

    [Fact]
    public void Calculate_WithUnvaluedStock_CapsSellableAtpToValuedStock()
    {
        // 50 physical, 10 reserved => ATP = 40.
        // But only 30 units are valued (20 units unvalued/unauthorized).
        // Sellable ATP must never exceed valued stock (30).
        var slice = new AtpStockSlice(
            VariantId: Guid.NewGuid(),
            WarehouseId: Guid.NewGuid(),
            BinId: Guid.NewGuid(),
            PhysicalOnHand: 50m,
            ActiveReservedQty: 10m,
            ValuedQty: 30m);

        var result = AvailableToPromiseCalculator.Calculate(slice);

        Assert.Equal(40m, result.AvailableToPromiseQty);
        Assert.Equal(30m, result.SellableAtpQty);
    }

    [Fact]
    public void Calculate_WhenOverReserved_ReturnsZeroAtpAndSellableAtp()
    {
        var slice = new AtpStockSlice(
            VariantId: Guid.NewGuid(),
            WarehouseId: Guid.NewGuid(),
            BinId: Guid.NewGuid(),
            PhysicalOnHand: 20m,
            ActiveReservedQty: 25m,
            ValuedQty: 20m);

        var result = AvailableToPromiseCalculator.Calculate(slice);

        Assert.Equal(0m, result.AvailableToPromiseQty);
        Assert.Equal(0m, result.SellableAtpQty);
    }

    [Fact]
    public void Calculate_WhenZeroPhysicalOnHand_ReturnsZero()
    {
        var slice = new AtpStockSlice(
            VariantId: Guid.NewGuid(),
            WarehouseId: Guid.NewGuid(),
            BinId: null,
            PhysicalOnHand: 0m,
            ActiveReservedQty: 0m,
            ValuedQty: 0m);

        var result = AvailableToPromiseCalculator.Calculate(slice);

        Assert.Equal(0m, result.AvailableToPromiseQty);
        Assert.Equal(0m, result.SellableAtpQty);
    }

    [Theory]
    [InlineData(50, 10, 45, 40, true)]
    [InlineData(50, 10, 45, 40.0001, false)]
    [InlineData(50, 50, 50, 1, false)]
    [InlineData(50, 10, 45, 0, false)]
    [InlineData(50, 10, 45, -5, false)]
    public void CanReserve_EvaluatesCorrectly(decimal physical, decimal reserved, decimal valued, decimal requestQty, bool expected)
    {
        var slice = new AtpStockSlice(Guid.NewGuid(), Guid.NewGuid(), null, physical, reserved, valued);
        Assert.Equal(expected, AvailableToPromiseCalculator.CanReserve(slice, requestQty));
    }

    [Theory]
    [InlineData(50, 10, 30, 30, true)]
    [InlineData(50, 10, 30, 31, false)] // ATP is 40, but Valued is only 30 -> cannot sell immediately
    [InlineData(50, 10, 45, 40, true)]
    [InlineData(50, 10, 45, 41, false)]
    [InlineData(50, 10, 45, 0, false)]
    public void CanSellImmediately_EnforcesValuedStockProtection(decimal physical, decimal reserved, decimal valued, decimal requestQty, bool expected)
    {
        var slice = new AtpStockSlice(Guid.NewGuid(), Guid.NewGuid(), null, physical, reserved, valued);
        Assert.Equal(expected, AvailableToPromiseCalculator.CanSellImmediately(slice, requestQty));
    }
}
