using UniversalErp.Application.Manufacturing;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class WorkOrderCostingEngineTests
{
    [Fact]
    public void CalculateOperationCost_CalculatesLaborAndOverheadCorrectly()
    {
        // 30 min setup + 90 min run = 120 min = 2 hours
        // Labor rate: 500 PKR/hr -> 1,000 PKR
        // Overhead rate: 250 PKR/hr -> 500 PKR
        var result = WorkOrderCostingEngine.CalculateOperationCost(
            actualSetupMinutes: 30m,
            actualRunMinutes: 90m,
            hourlyLaborRate: 500m,
            hourlyOverheadRate: 250m);

        Assert.Equal(2.0000m, result.LaborHours);
        Assert.Equal(1000.0000m, result.LaborCost);
        Assert.Equal(500.0000m, result.OverheadCost);
        Assert.Equal(1500.0000m, result.TotalOperationCost);
    }

    [Fact]
    public void SummarizeWorkOrderCosts_ComputesUnitCostAndReconcilesScrapAndVariance()
    {
        // Material: 4,000 PKR, Labor: 1,500 PKR, Overhead: 500 PKR -> Total: 6,000 PKR
        // Accepted: 9 units, Scrapped: 1 unit -> Total: 10 units
        // Unit cost = 6,000 / 10 = 600 PKR
        // FG valuation = 9 * 600 = 5,400 PKR
        // Scrap expense = 1 * 600 = 600 PKR
        // Cost variance = 6,000 - (5,400 + 600) = 0 PKR
        var summary = WorkOrderCostingEngine.SummarizeWorkOrderCosts(
            totalMaterialCost: 4000m,
            totalLaborCost: 1500m,
            totalOverheadCost: 500m,
            acceptedQuantity: 9m,
            scrappedQuantity: 1m);

        Assert.Equal(4000m, summary.TotalMaterialCost);
        Assert.Equal(1500m, summary.TotalLaborCost);
        Assert.Equal(500m, summary.TotalOverheadCost);
        Assert.Equal(6000m, summary.TotalProductionCost);
        Assert.Equal(600.0000m, summary.UnitCostFinal);
        Assert.Equal(5400.0000m, summary.FinishedGoodsValuation);
        Assert.Equal(600.0000m, summary.ScrapExpense);
        Assert.Equal(0.0000m, summary.CostVariance);
        Assert.Equal(summary.TotalProductionCost, summary.FinishedGoodsValuation + summary.ScrapExpense + summary.CostVariance);
    }
}
