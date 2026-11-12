using UniversalErp.Application.Inventory;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class FifoCostAllocatorTests
{
    [Fact]
    public void Allocate_Consumes_Oldest_Layer_First()
    {
        var olderId = Guid.Parse("00000000-0000-4000-8000-000000000001");
        var newerId = Guid.Parse("00000000-0000-4000-8000-000000000002");
        var layers = new[]
        {
            new FifoCostLayerSlice(newerId, DateTimeOffset.Parse("2026-11-10T10:00:00Z"), 10m, 55m),
            new FifoCostLayerSlice(olderId, DateTimeOffset.Parse("2026-11-09T10:00:00Z"), 10m, 40m),
        };

        var result = FifoCostAllocator.Allocate(layers, 5m);

        Assert.Single(result.Allocations);
        Assert.Equal(olderId, result.Allocations[0].LayerId);
        Assert.Equal(5m, result.Allocations[0].ConsumedQty);
        Assert.Equal(40m, result.Allocations[0].UnitCost);
        Assert.Equal(200m, result.TotalCost);
        Assert.Equal(40m, result.EffectiveUnitCost);
    }

    [Fact]
    public void Allocate_Handles_Partial_And_Multiple_Layers_With_Different_Costs()
    {
        var l1 = Guid.NewGuid();
        var l2 = Guid.NewGuid();
        var l3 = Guid.NewGuid();
        var layers = new[]
        {
            new FifoCostLayerSlice(l1, DateTimeOffset.Parse("2026-11-01T00:00:00Z"), 4m, 40m),
            new FifoCostLayerSlice(l2, DateTimeOffset.Parse("2026-11-02T00:00:00Z"), 6m, 50m),
            new FifoCostLayerSlice(l3, DateTimeOffset.Parse("2026-11-03T00:00:00Z"), 10m, 60m),
        };

        // Request 7 EA -> consumes all 4 EA @ 40 + 3 EA @ 50 (partial of l2)
        var result = FifoCostAllocator.Allocate(layers, 7m);

        Assert.Equal(2, result.Allocations.Count);
        Assert.Equal(l1, result.Allocations[0].LayerId);
        Assert.Equal(4m, result.Allocations[0].ConsumedQty);
        Assert.Equal(160m, result.Allocations[0].ExtendedCost);

        Assert.Equal(l2, result.Allocations[1].LayerId);
        Assert.Equal(3m, result.Allocations[1].ConsumedQty);
        Assert.Equal(150m, result.Allocations[1].ExtendedCost);

        Assert.Equal(310m, result.TotalCost);
        Assert.Equal(Math.Round(310m / 7m, 4, MidpointRounding.AwayFromZero), result.EffectiveUnitCost);
    }

    [Fact]
    public void Allocate_Throws_Missing_Valuation_Data_When_No_Valid_Layers_Exist()
    {
        var exEmpty = Assert.Throws<InvalidOperationException>(() =>
            FifoCostAllocator.Allocate([], 5m));
        Assert.StartsWith("MISSING_VALUATION_DATA", exEmpty.Message);

        var exhausted = new[]
        {
            new FifoCostLayerSlice(Guid.NewGuid(), DateTimeOffset.UtcNow, 0m, 45m),
        };
        var exZero = Assert.Throws<InvalidOperationException>(() =>
            FifoCostAllocator.Allocate(exhausted, 5m));
        Assert.StartsWith("MISSING_VALUATION_DATA", exZero.Message);
    }

    [Fact]
    public void Allocate_Throws_Insufficient_Valuation_Layers_When_Qty_Short()
    {
        var layers = new[]
        {
            new FifoCostLayerSlice(Guid.NewGuid(), DateTimeOffset.UtcNow, 3m, 45m),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FifoCostAllocator.Allocate(layers, 5m));
        Assert.StartsWith("INSUFFICIENT_VALUATION_LAYERS", ex.Message);
    }
}
