using UniversalErp.Application.Inventory;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class FifoCostAllocatorTests
{
    private static readonly Guid WhMain = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid WhStore = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BinPick01 = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BinReceive01 = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid BinStore01 = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void Test1_Allocate_Consumes_Oldest_Layer_Within_Selected_Bin()
    {
        var olderId = Guid.Parse("00000000-0000-4000-8000-000000000001");
        var newerId = Guid.Parse("00000000-0000-4000-8000-000000000002");
        var layers = new[]
        {
            new FifoCostLayerSlice(newerId, DateTimeOffset.Parse("2026-11-10T10:00:00Z"), 10m, 55m, WhMain, BinPick01),
            new FifoCostLayerSlice(olderId, DateTimeOffset.Parse("2026-11-09T10:00:00Z"), 10m, 40m, WhMain, BinPick01),
        };

        var result = FifoCostAllocator.Allocate(
            layers,
            5m,
            locationDescription: "warehouse MAIN-W1, bin PICK-01",
            expectedWarehouseId: WhMain,
            expectedBinId: BinPick01);

        Assert.Single(result.Allocations);
        Assert.Equal(olderId, result.Allocations[0].LayerId);
        Assert.Equal(5m, result.Allocations[0].ConsumedQty);
        Assert.Equal(40m, result.Allocations[0].UnitCost);
        Assert.Equal(200m, result.TotalCost);
        Assert.Equal(40m, result.EffectiveUnitCost);
    }

    [Fact]
    public void Test2_Allocate_Handles_Partial_Layer_Consumption()
    {
        var layerId = Guid.NewGuid();
        var layers = new[]
        {
            new FifoCostLayerSlice(layerId, DateTimeOffset.Parse("2026-11-01T00:00:00Z"), 20m, 45m, WhMain, BinPick01),
        };

        var result = FifoCostAllocator.Allocate(
            layers,
            5m,
            locationDescription: "warehouse MAIN-W1, bin PICK-01",
            expectedWarehouseId: WhMain,
            expectedBinId: BinPick01);

        Assert.Single(result.Allocations);
        Assert.Equal(layerId, result.Allocations[0].LayerId);
        Assert.Equal(5m, result.Allocations[0].ConsumedQty);
        Assert.Equal(45m, result.Allocations[0].UnitCost);
        Assert.Equal(225m, result.TotalCost);
        Assert.Equal(45m, result.EffectiveUnitCost);
    }

    [Fact]
    public void Test3_Allocate_Handles_Multiple_Layers_In_Same_Bin_With_Different_Costs()
    {
        var l1 = Guid.NewGuid();
        var l2 = Guid.NewGuid();
        var l3 = Guid.NewGuid();
        var layers = new[]
        {
            new FifoCostLayerSlice(l1, DateTimeOffset.Parse("2026-11-01T00:00:00Z"), 4m, 40m, WhMain, BinPick01),
            new FifoCostLayerSlice(l2, DateTimeOffset.Parse("2026-11-02T00:00:00Z"), 6m, 50m, WhMain, BinPick01),
            new FifoCostLayerSlice(l3, DateTimeOffset.Parse("2026-11-03T00:00:00Z"), 10m, 60m, WhMain, BinPick01),
        };

        // Request 7 EA -> consumes all 4 EA @ 40 + 3 EA @ 50 (partial of l2)
        var result = FifoCostAllocator.Allocate(
            layers,
            7m,
            locationDescription: "warehouse MAIN-W1, bin PICK-01",
            expectedWarehouseId: WhMain,
            expectedBinId: BinPick01);

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
    public void Test4_Allocate_Throws_Cross_Bin_Valuation_Forbidden_When_Layer_From_Another_Bin_Provided()
    {
        var alienLayerId = Guid.NewGuid();
        // Layer belongs to RECEIVE-01, but checkout is occurring from PICK-01
        var layers = new[]
        {
            new FifoCostLayerSlice(alienLayerId, DateTimeOffset.Parse("2026-11-01T00:00:00Z"), 30m, 45m, WhMain, BinReceive01),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FifoCostAllocator.Allocate(
                layers,
                5m,
                locationDescription: "warehouse MAIN-W1, bin PICK-01",
                expectedWarehouseId: WhMain,
                expectedBinId: BinPick01));

        Assert.StartsWith("CROSS_BIN_VALUATION_FORBIDDEN", ex.Message);
        Assert.Contains(alienLayerId.ToString(), ex.Message);
    }

    [Fact]
    public void Test5_Allocate_Throws_Missing_Valuation_Data_When_Bin_Contains_Unvalued_Stock()
    {
        // Unvalued stock in STORE-01 (e.g. 3 EA physical on hand, but 0 FIFO layers)
        var exEmpty = Assert.Throws<InvalidOperationException>(() =>
            FifoCostAllocator.Allocate(
                [],
                1m,
                locationDescription: "warehouse STORE-01, bin STORE-01",
                expectedWarehouseId: WhStore,
                expectedBinId: BinStore01));

        Assert.StartsWith("MISSING_VALUATION_DATA", exEmpty.Message);
        Assert.Contains("unvalued stock", exEmpty.Message);
        Assert.Contains("warehouse STORE-01, bin STORE-01", exEmpty.Message);

        // Layers with 0 remaining or 0 unit cost
        var exhausted = new[]
        {
            new FifoCostLayerSlice(Guid.NewGuid(), DateTimeOffset.UtcNow, 0m, 45m, WhStore, BinStore01),
        };
        var exZero = Assert.Throws<InvalidOperationException>(() =>
            FifoCostAllocator.Allocate(
                exhausted,
                1m,
                locationDescription: "warehouse STORE-01, bin STORE-01",
                expectedWarehouseId: WhStore,
                expectedBinId: BinStore01));

        Assert.StartsWith("MISSING_VALUATION_DATA", exZero.Message);
    }

    [Fact]
    public void Test6_Allocate_Throws_Insufficient_Valuation_Layers_When_Qty_Short_In_Selected_Bin()
    {
        var layers = new[]
        {
            new FifoCostLayerSlice(Guid.NewGuid(), DateTimeOffset.UtcNow, 3m, 45m, WhMain, BinPick01),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FifoCostAllocator.Allocate(
                layers,
                5m,
                locationDescription: "warehouse MAIN-W1, bin PICK-01",
                expectedWarehouseId: WhMain,
                expectedBinId: BinPick01));

        Assert.StartsWith("INSUFFICIENT_VALUATION_LAYERS", ex.Message);
        Assert.Contains("Available valued quantity is 3", ex.Message);
        Assert.Contains("Requested 5", ex.Message);
        Assert.Contains("warehouse MAIN-W1, bin PICK-01", ex.Message);
    }
}
