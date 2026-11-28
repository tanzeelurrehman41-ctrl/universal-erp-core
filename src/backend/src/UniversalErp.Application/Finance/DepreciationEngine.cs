namespace UniversalErp.Application.Finance;

public static class DepreciationEngine
{
    public static decimal CalculatePeriodDepreciation(
        decimal acquisitionCost,
        decimal salvageValue,
        int usefulLifeMonths,
        decimal accumulatedDepreciation,
        string method = "straight_line",
        decimal annualRatePercent = 20m)
    {
        if (acquisitionCost <= 0) return 0m;
        if (usefulLifeMonths <= 0) return 0m;

        var depreciableBase = Math.Max(0m, acquisitionCost - salvageValue);
        var remainingDepreciable = Math.Max(0m, depreciableBase - accumulatedDepreciation);
        if (remainingDepreciable <= 0m) return 0m;

        if (string.Equals(method, "reducing_balance", StringComparison.OrdinalIgnoreCase))
        {
            var currentBookValue = Math.Max(salvageValue, acquisitionCost - accumulatedDepreciation);
            var monthlyRate = (annualRatePercent / 100m) / 12m;
            var depreciation = Math.Round(currentBookValue * monthlyRate, 4);
            return Math.Min(depreciation, remainingDepreciable);
        }

        // Default: Straight-line depreciation
        var monthlyStraightLine = Math.Round(depreciableBase / usefulLifeMonths, 4);
        return Math.Min(monthlyStraightLine, remainingDepreciable);
    }

    public static (decimal NewAccumulated, decimal NewBookValue, bool IsFullyDepreciated) UpdateAssetDepreciation(
        decimal acquisitionCost,
        decimal salvageValue,
        decimal currentAccumulated,
        decimal depreciationAmount)
    {
        var newAccumulated = currentAccumulated + depreciationAmount;
        var depreciableBase = Math.Max(0m, acquisitionCost - salvageValue);
        if (newAccumulated >= depreciableBase)
        {
            newAccumulated = depreciableBase;
        }

        var newBookValue = acquisitionCost - newAccumulated;
        var isFullyDepreciated = newAccumulated >= depreciableBase;

        return (newAccumulated, newBookValue, isFullyDepreciated);
    }
}
