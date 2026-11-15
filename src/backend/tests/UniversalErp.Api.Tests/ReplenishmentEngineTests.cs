using System;
using System.Collections.Generic;
using UniversalErp.Application.Warehouse;
using Xunit;

namespace UniversalErp.Api.Tests;

public class ReplenishmentEngineTests
{
    [Fact]
    public void EvaluateReplenishment_DoesNotTriggerWhenAboveReorderPoint()
    {
        var rule = new ReplenishmentRuleRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), null,
            MinQuantity: 20, MaxQuantity: 100, ReorderPoint: 30, ReplenishIncrementQty: 10,
            IsActive: true, DateTimeOffset.UtcNow);

        // Current stock 35 > ReorderPoint 30
        var result = ReplenishmentEngine.EvaluateReplenishment(rule, 35, Array.Empty<ReserveBinStock>());

        Assert.False(result.TriggerNeeded);
        Assert.Equal(0, result.RecommendedQuantity);
    }

    [Fact]
    public void EvaluateReplenishment_TriggersWithIncrementRoundingAndOldestFifoLayer()
    {
        var pickBinId = Guid.NewGuid();
        var reserveBin1 = Guid.NewGuid();
        var reserveBin2 = Guid.NewGuid();

        var rule = new ReplenishmentRuleRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), pickBinId, null,
            MinQuantity: 20, MaxQuantity: 100, ReorderPoint: 30, ReplenishIncrementQty: 25,
            IsActive: true, DateTimeOffset.UtcNow);

        var now = DateTimeOffset.UtcNow;
        var reserveStock = new List<ReserveBinStock>
        {
            // Newer layer
            new() { BinId = reserveBin2, BinCode = "RES-02", LayerId = Guid.NewGuid(), AvailableQuantity = 100, ReceivedAt = now.AddHours(-1) },
            // Older layer (oldest receipt)
            new() { BinId = reserveBin1, BinCode = "RES-01", LayerId = Guid.NewGuid(), AvailableQuantity = 100, ReceivedAt = now.AddHours(-10) }
        };

        // Current stock 20 <= ReorderPoint 30. Deficit = 100 - 20 = 80.
        // Increment = 25 -> Ceil(80 / 25) = 4 steps -> 100 units target
        var result = ReplenishmentEngine.EvaluateReplenishment(rule, 20, reserveStock);

        Assert.True(result.TriggerNeeded);
        Assert.Equal(100, result.RecommendedQuantity);
        Assert.Equal(reserveBin1, result.SourceReserveBinId);
        Assert.Equal("RES-01", result.SourceReserveBinCode);
    }

    [Fact]
    public void EvaluateReplenishment_FlagsStockoutWhenZeroReserveAvailable()
    {
        var rule = new ReplenishmentRuleRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), null,
            MinQuantity: 20, MaxQuantity: 100, ReorderPoint: 30, ReplenishIncrementQty: 10,
            IsActive: true, DateTimeOffset.UtcNow);

        var result = ReplenishmentEngine.EvaluateReplenishment(rule, 15, Array.Empty<ReserveBinStock>());

        Assert.True(result.TriggerNeeded);
        Assert.Equal(0, result.RecommendedQuantity);
        Assert.Contains("stockout alert", result.Rationale);
    }
}
