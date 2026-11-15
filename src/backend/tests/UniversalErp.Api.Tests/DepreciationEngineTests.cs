using UniversalErp.Application.Finance;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class DepreciationEngineTests
{
    [Fact]
    public void StraightLineDepreciation_CalculatesMonthlyExpenseCorrectly()
    {
        // 120,000 cost, 0 salvage, 60 months useful life -> 2,000 per month
        decimal cost = 120000m;
        decimal salvage = 0m;
        int usefulLife = 60;
        decimal currentAcc = 0m;

        decimal monthlyExpense = DepreciationEngine.CalculatePeriodDepreciation(
            cost, salvage, usefulLife, currentAcc, "straight_line");

        Assert.Equal(2000m, monthlyExpense);
    }

    [Fact]
    public void StraightLineDepreciation_RespectsSalvageValue()
    {
        // 110,000 cost, 10,000 salvage, 50 months -> Depreciable base = 100,000 -> 2,000/mo
        decimal cost = 110000m;
        decimal salvage = 10000m;
        int usefulLife = 50;
        decimal currentAcc = 0m;

        decimal monthlyExpense = DepreciationEngine.CalculatePeriodDepreciation(
            cost, salvage, usefulLife, currentAcc, "straight_line");

        Assert.Equal(2000m, monthlyExpense);
    }

    [Fact]
    public void StraightLineDepreciation_CapsAtRemainingDepreciableBase()
    {
        // 100,000 cost, 10,000 salvage (depreciable = 90,000). Current accumulated = 89,500.
        // Monthly straight line would be 90,000 / 10 = 9,000.
        // But only 500 remaining!
        decimal cost = 100000m;
        decimal salvage = 10000m;
        int usefulLife = 10;
        decimal currentAcc = 89500m;

        decimal monthlyExpense = DepreciationEngine.CalculatePeriodDepreciation(
            cost, salvage, usefulLife, currentAcc, "straight_line");

        Assert.Equal(500m, monthlyExpense);
    }

    [Fact]
    public void ReducingBalanceDepreciation_CalculatesBasedOnBookValue()
    {
        // 100,000 cost, 0 salvage, 24% annual rate -> 2% per month
        // Month 1: 100,000 * 2% = 2,000
        decimal cost = 100000m;
        decimal salvage = 0m;
        int usefulLife = 60;
        decimal currentAcc = 0m;

        decimal month1Expense = DepreciationEngine.CalculatePeriodDepreciation(
            cost, salvage, usefulLife, currentAcc, "reducing_balance", 24m);

        Assert.Equal(2000m, month1Expense);

        // Month 2: Book value is 98,000 -> 98,000 * 2% = 1,960
        decimal month2Expense = DepreciationEngine.CalculatePeriodDepreciation(
            cost, salvage, usefulLife, 2000m, "reducing_balance", 24m);

        Assert.Equal(1960m, month2Expense);
    }

    [Fact]
    public void UpdateAssetDepreciation_UpdatesBookValueAndFlagsFullyDepreciated()
    {
        decimal cost = 10000m;
        decimal salvage = 1000m;
        decimal currentAcc = 8000m;
        decimal depreciation = 1000m;

        var (newAcc, newBookValue, isFullyDepreciated) = DepreciationEngine.UpdateAssetDepreciation(
            cost, salvage, currentAcc, depreciation);

        Assert.Equal(9000m, newAcc);
        Assert.Equal(1000m, newBookValue); // Salvage value reached!
        Assert.True(isFullyDepreciated);
    }

    [Fact]
    public void CalculatePeriodDepreciation_ReturnsZero_WhenAlreadyFullyDepreciated()
    {
        decimal cost = 10000m;
        decimal salvage = 1000m;
        decimal currentAcc = 9000m; // Max depreciable base is 10000 - 1000 = 9000

        decimal expense = DepreciationEngine.CalculatePeriodDepreciation(
            cost, salvage, 12, currentAcc, "straight_line");

        Assert.Equal(0m, expense);
    }
}
