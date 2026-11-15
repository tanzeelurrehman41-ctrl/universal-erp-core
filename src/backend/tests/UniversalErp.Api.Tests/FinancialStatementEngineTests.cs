using UniversalErp.Application.Finance;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class FinancialStatementEngineTests
{
    [Fact]
    public void BuildStatements_Balances_AssetsEqualsLiabilitiesPlusEquityPlusNetIncome()
    {
        var trialBalance = new List<TrialBalanceRow>
        {
            // Assets: 15,000 (Bank 10,000 + Inventory 5,000)
            new(Guid.NewGuid(), "1020", "Bank Account", "asset", "debit", 10000m, 0m, 10000m),
            new(Guid.NewGuid(), "1200", "Inventory Asset", "asset", "debit", 5000m, 0m, 5000m),

            // Liabilities: 4,000 (AP 4,000)
            new(Guid.NewGuid(), "2010", "Accounts Payable", "liability", "credit", 0m, 4000m, 4000m),

            // Equity: 6,000 (Owner Equity 6,000)
            new(Guid.NewGuid(), "3010", "Owner's Equity", "equity", "credit", 0m, 6000m, 6000m),

            // Revenue: 8,000 (Sales Revenue 8,000)
            new(Guid.NewGuid(), "4010", "Sales Revenue", "revenue", "credit", 0m, 8000m, 8000m),

            // Expense: 3,000 (COGS 2,000 + Rent Expense 1,000)
            new(Guid.NewGuid(), "5010", "Cost of Goods Sold", "expense", "debit", 2000m, 0m, 2000m),
            new(Guid.NewGuid(), "6010", "Rent Expense", "expense", "debit", 1000m, 0m, 1000m)
        };

        // Net income = 8,000 - 3,000 = 5,000
        // Total Liabilities & Equity = 4,000 + 6,000 + 5,000 = 15,000
        // Total Assets = 15,000 -> Perfect balance!

        var statements = FinancialStatementEngine.BuildStatements(trialBalance);

        Assert.Equal(15000m, statements.TotalAssets);
        Assert.Equal(4000m, statements.TotalLiabilities);
        Assert.Equal(6000m, statements.TotalEquity);
        Assert.Equal(8000m, statements.TotalRevenue);
        Assert.Equal(2000m, statements.TotalCostOfSales);
        Assert.Equal(1000m, statements.TotalOperatingExpenses);
        Assert.Equal(5000m, statements.NetIncome);
        Assert.True(statements.IsBalanceSheetBalanced);
        Assert.Equal(0m, statements.BalanceSheetDiscrepancy);
    }
}
