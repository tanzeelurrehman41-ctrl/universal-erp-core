namespace UniversalErp.Application.Manufacturing;

public sealed record ExplodedComponentLine(
    int Level,
    Guid ComponentVariantId,
    string ComponentType,
    decimal NetQuantityPer,
    decimal ScrapPercentage,
    decimal GrossRequiredQuantity,
    Guid? SubstituteVariantId = null,
    string? Notes = null);

public static class BomExplosionEngine
{
    public sealed record BomDefinition(
        Guid BomId,
        Guid ProductVariantId,
        decimal BaseQuantity,
        decimal YieldPercent,
        IReadOnlyList<BomItemDefinition> Items);

    public sealed record BomItemDefinition(
        Guid ComponentVariantId,
        string ComponentType,
        decimal QuantityPer,
        decimal ScrapPercentage,
        string? Notes = null);

    public static IReadOnlyList<ExplodedComponentLine> Explode(
        Guid rootVariantId,
        decimal orderQuantity,
        IReadOnlyDictionary<Guid, BomDefinition> bomRegistry,
        IReadOnlyDictionary<Guid, Guid>? preferredSubstitutes = null)
    {
        if (orderQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(orderQuantity), "Order quantity must be positive.");
        }

        if (!bomRegistry.TryGetValue(rootVariantId, out var rootBom))
        {
            throw new InvalidOperationException($"NO_ACTIVE_BOM: No active BOM found for variant {rootVariantId}");
        }

        var results = new List<ExplodedComponentLine>();
        var visited = new HashSet<Guid>();

        ExplodeInternal(
            variantId: rootVariantId,
            currentMultiplier: orderQuantity,
            level: 1,
            bomRegistry: bomRegistry,
            preferredSubstitutes: preferredSubstitutes,
            visited: visited,
            results: results);

        return results;
    }

    private static void ExplodeInternal(
        Guid variantId,
        decimal currentMultiplier,
        int level,
        IReadOnlyDictionary<Guid, BomDefinition> bomRegistry,
        IReadOnlyDictionary<Guid, Guid>? preferredSubstitutes,
        HashSet<Guid> visited,
        List<ExplodedComponentLine> results)
    {
        if (!visited.Add(variantId))
        {
            throw new InvalidOperationException($"CIRCULAR_BOM_DETECTED: Variant {variantId} causes a circular reference.");
        }

        try
        {
            if (!bomRegistry.TryGetValue(variantId, out var bom))
            {
                return;
            }

            var baseQty = bom.BaseQuantity > 0m ? bom.BaseQuantity : 1.0m;
            var yieldFactor = bom.YieldPercent > 0m ? (bom.YieldPercent / 100m) : 1.0m;
            var effectiveOrderQty = currentMultiplier / (baseQty * yieldFactor);

            foreach (var item in bom.Items)
            {
                var targetVariantId = item.ComponentVariantId;
                Guid? substituteId = null;

                if (preferredSubstitutes != null && preferredSubstitutes.TryGetValue(targetVariantId, out var sub))
                {
                    substituteId = sub;
                    targetVariantId = sub;
                }

                var scrapFactor = 1.0m + (Math.Max(0m, item.ScrapPercentage) / 100m);
                var grossQty = Math.Round(effectiveOrderQty * item.QuantityPer * scrapFactor, 4, MidpointRounding.AwayFromZero);

                results.Add(new ExplodedComponentLine(
                    Level: level,
                    ComponentVariantId: targetVariantId,
                    ComponentType: item.ComponentType,
                    NetQuantityPer: item.QuantityPer,
                    ScrapPercentage: item.ScrapPercentage,
                    GrossRequiredQuantity: grossQty,
                    SubstituteVariantId: substituteId,
                    Notes: item.Notes));

                // If subassembly and has BOM, recurse
                if (item.ComponentType == "subassembly" && bomRegistry.ContainsKey(targetVariantId))
                {
                    ExplodeInternal(
                        variantId: targetVariantId,
                        currentMultiplier: grossQty,
                        level: level + 1,
                        bomRegistry: bomRegistry,
                        preferredSubstitutes: preferredSubstitutes,
                        visited: visited,
                        results: results);
                }
            }
        }
        finally
        {
            visited.Remove(variantId);
        }
    }
}
