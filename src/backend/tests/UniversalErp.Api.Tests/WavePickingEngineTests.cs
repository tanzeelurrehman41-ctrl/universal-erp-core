using System;
using System.Collections.Generic;
using UniversalErp.Application.Warehouse;
using Xunit;

namespace UniversalErp.Api.Tests;

public class WavePickingEngineTests
{
    [Fact]
    public void SequencePickPath_PerformsSerpentineOrderingByAisle()
    {
        var items = new List<PickPathItem>
        {
            new() { ItemId = Guid.NewGuid(), BinCode = "A1-R1", Aisle = "01", Rack = "01" },
            new() { ItemId = Guid.NewGuid(), BinCode = "A1-R3", Aisle = "01", Rack = "03" },
            new() { ItemId = Guid.NewGuid(), BinCode = "A2-R1", Aisle = "02", Rack = "01" },
            new() { ItemId = Guid.NewGuid(), BinCode = "A2-R3", Aisle = "02", Rack = "03" },
        };

        var sequenced = WavePickingEngine.SequencePickPath(items);

        // Aisle 01 (first aisle, even index 0) is ascending: A1-R1, then A1-R3
        Assert.Equal("A1-R1", sequenced[0].BinCode);
        Assert.Equal("A1-R3", sequenced[1].BinCode);

        // Aisle 02 (second aisle, odd index 1) is serpentine descending: A2-R3, then A2-R1
        Assert.Equal("A2-R3", sequenced[2].BinCode);
        Assert.Equal("A2-R1", sequenced[3].BinCode);
    }

    [Fact]
    public void CalculateWaveProgress_ComputesPercentageAndCompletionStatus()
    {
        var items = new List<PickPathItem>
        {
            new() { RequestedQuantity = 10, PickedQuantity = 10, Status = "picked" },
            new() { RequestedQuantity = 20, PickedQuantity = 10, Status = "pending" }
        };

        var progress = WavePickingEngine.CalculateWaveProgress(items);

        Assert.Equal(2, progress.TotalLines);
        Assert.Equal(1, progress.CompletedLines);
        Assert.Equal(30m, progress.TotalRequestedUnits);
        Assert.Equal(20m, progress.TotalPickedUnits);
        Assert.Equal(66.7m, progress.CompletionPercentage);
        Assert.False(progress.IsFullyPicked);
    }

    [Fact]
    public void ValidatePickConfirmation_EnforcesQuantityAndBarcodeMatch()
    {
        var item = new PickPathItem
        {
            RequestedQuantity = 5,
            PickedQuantity = 0,
            BinCode = "PICK-01"
        };

        // 1. Overpick should fail
        var (overpickValid, overpickErr) = WavePickingEngine.ValidatePickConfirmation(item, 6, "BIN:PICK-01", "PICK-01", null);
        Assert.False(overpickValid);
        Assert.Contains("Over-picking not permitted", overpickErr);

        // 2. Bin mismatch should fail
        var (binMismatchValid, binErr) = WavePickingEngine.ValidatePickConfirmation(item, 2, "BIN:WRONG-BIN", "PICK-01", null);
        Assert.False(binMismatchValid);
        Assert.Contains("Bin scan mismatch", binErr);

        // 3. Valid confirmation succeeds
        var (validConfirm, err) = WavePickingEngine.ValidatePickConfirmation(item, 2, "BIN:PICK-01", "PICK-01", "PROD:SKU-A", "SKU-A");
        Assert.True(validConfirm);
        Assert.Null(err);
    }
}
