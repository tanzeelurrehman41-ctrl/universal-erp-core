using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalErp.Application.Warehouse;

public class CandidateBinInfo
{
    public Guid BinId { get; set; }
    public string BinCode { get; set; } = string.Empty;
    public Guid? ZoneId { get; set; }
    public string? ZoneCode { get; set; }
    public string ZoneType { get; set; } = "storage";
    public string ZoneStorageType { get; set; } = "ambient";
    public string BinStorageRestriction { get; set; } = "standard";
    public bool IsPickFace { get; set; }
    public decimal CurrentUnits { get; set; }
    public decimal CurrentWeightKg { get; set; }
    public decimal CurrentVolumeCbm { get; set; }
    public decimal? MaxUnits { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public decimal? MaxVolumeCbm { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PutawayVariantRequirements
{
    public Guid VariantId { get; set; }
    public Guid? CategoryId { get; set; }
    public string StorageRequirement { get; set; } = "ambient"; // ambient, cold_chain, hazardous
    public decimal UnitWeightKg { get; set; } = 1.0m;
    public decimal UnitVolumeCbm { get; set; } = 0.01m;
}

public static class DirectedPutawayEngine
{
    public static IReadOnlyList<PutawayRecommendation> RecommendPutawayBins(
        PutawayVariantRequirements variant,
        decimal quantity,
        IReadOnlyList<CandidateBinInfo> bins,
        IReadOnlyList<PutawayStrategyRecord>? strategies = null)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Putaway quantity must be positive.");

        var recommendations = new List<PutawayRecommendation>();
        var requiredWeight = quantity * variant.UnitWeightKg;
        var requiredVolume = quantity * variant.UnitVolumeCbm;

        foreach (var bin in bins)
        {
            if (!bin.IsActive)
                continue;

            // 1. Storage compatibility check
            if (!IsStorageCompatible(variant.StorageRequirement, bin.ZoneStorageType, bin.BinStorageRestriction))
                continue;

            // 2. Capacity validation
            var projectedUnits = bin.CurrentUnits + quantity;
            var projectedWeight = bin.CurrentWeightKg + requiredWeight;
            var projectedVolume = bin.CurrentVolumeCbm + requiredVolume;

            if (bin.MaxUnits.HasValue && projectedUnits > bin.MaxUnits.Value)
                continue;

            if (bin.MaxWeightKg.HasValue && projectedWeight > bin.MaxWeightKg.Value)
                continue;

            if (bin.MaxVolumeCbm.HasValue && projectedVolume > bin.MaxVolumeCbm.Value)
                continue;

            // Compute projected capacity utilization
            var utilization = CalculateProjectedUtilization(bin, projectedUnits, projectedWeight, projectedVolume);

            // 3. Score calculation
            var score = 100;
            var rationale = "Standard available bin with matching storage conditions";

            // Evaluate strategies
            if (strategies != null)
            {
                foreach (var strat in strategies.Where(s => s.IsActive).OrderBy(s => s.Priority))
                {
                    if (strat.PreferredBinId.HasValue && strat.PreferredBinId.Value == bin.BinId)
                    {
                        score += 1000;
                        rationale = $"Matched fixed putaway bin rule ({strat.StrategyType})";
                        break;
                    }

                    if (strat.PreferredZoneId.HasValue && strat.PreferredZoneId.Value == bin.ZoneId)
                    {
                        score += 500;
                        rationale = $"Matched preferred zone rule: {bin.ZoneCode}";
                        break;
                    }
                }
            }

            // Zone type priorities
            if (bin.ZoneType == "bulk_reserve" || bin.ZoneType == "storage")
            {
                score += 50;
            }
            else if (bin.IsPickFace)
            {
                score += 75;
                if (score < 500) rationale = "Pick-face location with available capacity";
            }

            // Prefer empty or low-utilization bins
            if (bin.CurrentUnits == 0)
            {
                score += 30;
            }
            else
            {
                // Penalize if close to full (> 90%)
                if (utilization > 90m)
                    score -= 20;
            }

            recommendations.Add(new PutawayRecommendation(
                bin.BinId,
                bin.BinCode,
                bin.ZoneCode,
                bin.CurrentUnits,
                bin.MaxUnits,
                Math.Round(utilization, 1),
                score,
                rationale));
        }

        return recommendations
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.ProjectedUtilizationPercent)
            .ToList();
    }

    private static bool IsStorageCompatible(string requirement, string zoneStorageType, string binRestriction)
    {
        // Cold chain items MUST be in cold chain zone
        if (requirement.Equals("cold_chain", StringComparison.OrdinalIgnoreCase))
        {
            return zoneStorageType.Equals("cold_chain", StringComparison.OrdinalIgnoreCase);
        }

        // Hazardous items MUST be in hazardous zone / bin
        if (requirement.Equals("hazardous", StringComparison.OrdinalIgnoreCase))
        {
            return zoneStorageType.Equals("hazardous", StringComparison.OrdinalIgnoreCase) ||
                   binRestriction.Equals("hazardous", StringComparison.OrdinalIgnoreCase);
        }

        // Ambient items should not take up cold chain storage unless specifically allowed
        if (zoneStorageType.Equals("cold_chain", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static decimal CalculateProjectedUtilization(
        CandidateBinInfo bin,
        decimal projectedUnits,
        decimal projectedWeight,
        decimal projectedVolume)
    {
        var metrics = new List<decimal>();

        if (bin.MaxUnits.HasValue && bin.MaxUnits.Value > 0)
            metrics.Add((projectedUnits / bin.MaxUnits.Value) * 100m);

        if (bin.MaxWeightKg.HasValue && bin.MaxWeightKg.Value > 0)
            metrics.Add((projectedWeight / bin.MaxWeightKg.Value) * 100m);

        if (bin.MaxVolumeCbm.HasValue && bin.MaxVolumeCbm.Value > 0)
            metrics.Add((projectedVolume / bin.MaxVolumeCbm.Value) * 100m);

        if (metrics.Count == 0)
            return Math.Min(100m, projectedUnits * 5m); // Default estimate if no limits defined

        return Math.Min(100m, metrics.Max());
    }
}
