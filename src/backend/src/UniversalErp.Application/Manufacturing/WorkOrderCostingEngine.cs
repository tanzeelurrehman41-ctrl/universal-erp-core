namespace UniversalErp.Application.Manufacturing;

public sealed record OperationCostCalculation(
    decimal LaborHours,
    decimal LaborCost,
    decimal OverheadCost,
    decimal TotalOperationCost);

public sealed record ProductionCostSummary(
    decimal TotalMaterialCost,
    decimal TotalLaborCost,
    decimal TotalOverheadCost,
    decimal TotalProductionCost,
    decimal UnitCostFinal,
    decimal FinishedGoodsValuation,
    decimal ScrapExpense,
    decimal CostVariance);

public static class WorkOrderCostingEngine
{
    public static OperationCostCalculation CalculateOperationCost(
        decimal actualSetupMinutes,
        decimal actualRunMinutes,
        decimal hourlyLaborRate,
        decimal hourlyOverheadRate)
    {
        var totalMinutes = Math.Max(0m, actualSetupMinutes) + Math.Max(0m, actualRunMinutes);
        var hours = totalMinutes / 60.0m;
        var laborCost = Math.Round(hours * Math.Max(0m, hourlyLaborRate), 4, MidpointRounding.AwayFromZero);
        var overheadCost = Math.Round(hours * Math.Max(0m, hourlyOverheadRate), 4, MidpointRounding.AwayFromZero);

        return new OperationCostCalculation(
            LaborHours: Math.Round(hours, 4, MidpointRounding.AwayFromZero),
            LaborCost: laborCost,
            OverheadCost: overheadCost,
            TotalOperationCost: laborCost + overheadCost);
    }

    public static ProductionCostSummary SummarizeWorkOrderCosts(
        decimal totalMaterialCost,
        decimal totalLaborCost,
        decimal totalOverheadCost,
        decimal acceptedQuantity,
        decimal scrappedQuantity)
    {
        var totalCost = Math.Max(0m, totalMaterialCost) + Math.Max(0m, totalLaborCost) + Math.Max(0m, totalOverheadCost);
        var totalUnitsProduced = Math.Max(0m, acceptedQuantity) + Math.Max(0m, scrappedQuantity);

        if (totalUnitsProduced <= 0m || totalCost <= 0m)
        {
            return new ProductionCostSummary(
                TotalMaterialCost: totalMaterialCost,
                TotalLaborCost: totalLaborCost,
                TotalOverheadCost: totalOverheadCost,
                TotalProductionCost: totalCost,
                UnitCostFinal: 0m,
                FinishedGoodsValuation: 0m,
                ScrapExpense: 0m,
                CostVariance: 0m);
        }

        var unitCost = Math.Round(totalCost / totalUnitsProduced, 4, MidpointRounding.AwayFromZero);
        var fgValuation = Math.Round(acceptedQuantity * unitCost, 4, MidpointRounding.AwayFromZero);
        var scrapExpense = Math.Round(scrappedQuantity * unitCost, 4, MidpointRounding.AwayFromZero);
        var variance = totalCost - (fgValuation + scrapExpense);

        return new ProductionCostSummary(
            TotalMaterialCost: totalMaterialCost,
            TotalLaborCost: totalLaborCost,
            TotalOverheadCost: totalOverheadCost,
            TotalProductionCost: totalCost,
            UnitCostFinal: unitCost,
            FinishedGoodsValuation: fgValuation,
            ScrapExpense: scrapExpense,
            CostVariance: variance);
    }
}
