using UniversalErp.Application.Manufacturing;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class BomExplosionEngineTests
{
    [Fact]
    public void Explode_SingleLevelBom_CalculatesGrossRequirementsWithScrap()
    {
        var fgId = Guid.NewGuid();
        var rawId = Guid.NewGuid();

        var bom = new BomExplosionEngine.BomDefinition(
            BomId: Guid.NewGuid(),
            ProductVariantId: fgId,
            BaseQuantity: 1m,
            YieldPercent: 100m,
            Items: new[]
            {
                new BomExplosionEngine.BomItemDefinition(
                    ComponentVariantId: rawId,
                    ComponentType: "raw_material",
                    QuantityPer: 2m,
                    ScrapPercentage: 10m) // 10% scrap -> factor 1.10
            });

        var registry = new Dictionary<Guid, BomExplosionEngine.BomDefinition>
        {
            [fgId] = bom
        };

        var lines = BomExplosionEngine.Explode(fgId, 10m, registry);

        Assert.Single(lines);
        var line = lines[0];
        Assert.Equal(rawId, line.ComponentVariantId);
        Assert.Equal(1, line.Level);
        Assert.Equal(2m, line.NetQuantityPer);
        Assert.Equal(10m, line.ScrapPercentage);
        // 10 units * 2 * 1.10 = 22.0
        Assert.Equal(22.0000m, line.GrossRequiredQuantity);
    }

    [Fact]
    public void Explode_MultiLevelBom_RecursivelyCalculatesNestedRequirements()
    {
        var fgId = Guid.NewGuid();
        var subassemblyId = Guid.NewGuid();
        var rawId = Guid.NewGuid();

        var fgBom = new BomExplosionEngine.BomDefinition(
            BomId: Guid.NewGuid(),
            ProductVariantId: fgId,
            BaseQuantity: 1m,
            YieldPercent: 100m,
            Items: new[]
            {
                new BomExplosionEngine.BomItemDefinition(
                    ComponentVariantId: subassemblyId,
                    ComponentType: "subassembly",
                    QuantityPer: 1m,
                    ScrapPercentage: 0m)
            });

        var subBom = new BomExplosionEngine.BomDefinition(
            BomId: Guid.NewGuid(),
            ProductVariantId: subassemblyId,
            BaseQuantity: 1m,
            YieldPercent: 100m,
            Items: new[]
            {
                new BomExplosionEngine.BomItemDefinition(
                    ComponentVariantId: rawId,
                    ComponentType: "raw_material",
                    QuantityPer: 4m,
                    ScrapPercentage: 5m) // 5% scrap
            });

        var registry = new Dictionary<Guid, BomExplosionEngine.BomDefinition>
        {
            [fgId] = fgBom,
            [subassemblyId] = subBom
        };

        var lines = BomExplosionEngine.Explode(fgId, 5m, registry);

        Assert.Equal(2, lines.Count);

        var subLine = lines.First(l => l.ComponentVariantId == subassemblyId);
        Assert.Equal(1, subLine.Level);
        Assert.Equal(5.0000m, subLine.GrossRequiredQuantity);

        var rawLine = lines.First(l => l.ComponentVariantId == rawId);
        Assert.Equal(2, rawLine.Level);
        // 5 subassemblies * 4 per unit * 1.05 = 21.0
        Assert.Equal(21.0000m, rawLine.GrossRequiredQuantity);
    }

    [Fact]
    public void Explode_WithMaterialSubstitution_ReplacesVariant()
    {
        var fgId = Guid.NewGuid();
        var rawId = Guid.NewGuid();
        var substituteRawId = Guid.NewGuid();

        var bom = new BomExplosionEngine.BomDefinition(
            BomId: Guid.NewGuid(),
            ProductVariantId: fgId,
            BaseQuantity: 1m,
            YieldPercent: 100m,
            Items: new[]
            {
                new BomExplosionEngine.BomItemDefinition(
                    ComponentVariantId: rawId,
                    ComponentType: "raw_material",
                    QuantityPer: 3m,
                    ScrapPercentage: 0m)
            });

        var registry = new Dictionary<Guid, BomExplosionEngine.BomDefinition>
        {
            [fgId] = bom
        };

        var substitutions = new Dictionary<Guid, Guid>
        {
            [rawId] = substituteRawId
        };

        var lines = BomExplosionEngine.Explode(fgId, 2m, registry, substitutions);

        Assert.Single(lines);
        var line = lines[0];
        Assert.Equal(substituteRawId, line.ComponentVariantId);
        Assert.Equal(substituteRawId, line.SubstituteVariantId);
        Assert.Equal(6.0000m, line.GrossRequiredQuantity);
    }

    [Fact]
    public void Explode_CircularBom_ThrowsInvalidOperationException()
    {
        var variantA = Guid.NewGuid();
        var variantB = Guid.NewGuid();

        var bomA = new BomExplosionEngine.BomDefinition(
            BomId: Guid.NewGuid(),
            ProductVariantId: variantA,
            BaseQuantity: 1m,
            YieldPercent: 100m,
            Items: new[]
            {
                new BomExplosionEngine.BomItemDefinition(
                    ComponentVariantId: variantB,
                    ComponentType: "subassembly",
                    QuantityPer: 1m,
                    ScrapPercentage: 0m)
            });

        var bomB = new BomExplosionEngine.BomDefinition(
            BomId: Guid.NewGuid(),
            ProductVariantId: variantB,
            BaseQuantity: 1m,
            YieldPercent: 100m,
            Items: new[]
            {
                new BomExplosionEngine.BomItemDefinition(
                    ComponentVariantId: variantA,
                    ComponentType: "subassembly",
                    QuantityPer: 1m,
                    ScrapPercentage: 0m)
            });

        var registry = new Dictionary<Guid, BomExplosionEngine.BomDefinition>
        {
            [variantA] = bomA,
            [variantB] = bomB
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BomExplosionEngine.Explode(variantA, 1m, registry));

        Assert.Contains("CIRCULAR_BOM_DETECTED", ex.Message);
    }
}
