using System;
using System.Collections.Generic;
using UniversalErp.Application.Warehouse;
using Xunit;

namespace UniversalErp.Api.Tests;

public class DirectedPutawayEngineTests
{
    [Fact]
    public void RecommendPutawayBins_FiltersIncompatibleStorageConditions()
    {
        var coldVariant = new PutawayVariantRequirements
        {
            VariantId = Guid.NewGuid(),
            StorageRequirement = "cold_chain",
            UnitWeightKg = 1m,
            UnitVolumeCbm = 0.01m
        };

        var bins = new List<CandidateBinInfo>
        {
            new() { BinId = Guid.NewGuid(), BinCode = "AMB-01", ZoneStorageType = "ambient", MaxUnits = 100 },
            new() { BinId = Guid.NewGuid(), BinCode = "COLD-01", ZoneStorageType = "cold_chain", MaxUnits = 100 }
        };

        var recs = DirectedPutawayEngine.RecommendPutawayBins(coldVariant, 10, bins);

        Assert.Single(recs);
        Assert.Equal("COLD-01", recs[0].BinCode);
    }

    [Fact]
    public void RecommendPutawayBins_FiltersOutOverCapacityBins()
    {
        var variant = new PutawayVariantRequirements
        {
            VariantId = Guid.NewGuid(),
            StorageRequirement = "ambient",
            UnitWeightKg = 5m,
            UnitVolumeCbm = 0.02m
        };

        var binFull = new CandidateBinInfo
        {
            BinId = Guid.NewGuid(),
            BinCode = "BIN-FULL",
            CurrentUnits = 95,
            MaxUnits = 100
        };

        var binAvailable = new CandidateBinInfo
        {
            BinId = Guid.NewGuid(),
            BinCode = "BIN-AVAIL",
            CurrentUnits = 10,
            MaxUnits = 100
        };

        // Putting away 10 units: binFull will exceed 100 (95 + 10 = 105), binAvailable will be 20
        var recs = DirectedPutawayEngine.RecommendPutawayBins(variant, 10, new[] { binFull, binAvailable });

        Assert.Single(recs);
        Assert.Equal("BIN-AVAIL", recs[0].BinCode);
    }

    [Fact]
    public void RecommendPutawayBins_PrioritizesFixedBinRuleOverGeneralBins()
    {
        var fixedBinId = Guid.NewGuid();
        var generalBinId = Guid.NewGuid();

        var bins = new List<CandidateBinInfo>
        {
            new() { BinId = generalBinId, BinCode = "GEN-01", MaxUnits = 500, CurrentUnits = 0 },
            new() { BinId = fixedBinId, BinCode = "FIXED-01", MaxUnits = 500, CurrentUnits = 0 }
        };

        var strategy = new PutawayStrategyRecord(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            null, null, null, fixedBinId, "fixed_bin", 1, true, DateTimeOffset.UtcNow);

        var variant = new PutawayVariantRequirements { VariantId = Guid.NewGuid() };
        var recs = DirectedPutawayEngine.RecommendPutawayBins(variant, 20, bins, new[] { strategy });

        Assert.Equal(2, recs.Count);
        Assert.Equal("FIXED-01", recs[0].BinCode);
        Assert.True(recs[0].Score > recs[1].Score);
    }
}
