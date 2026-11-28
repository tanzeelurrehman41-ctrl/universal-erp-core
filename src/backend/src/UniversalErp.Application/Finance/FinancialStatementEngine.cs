namespace UniversalErp.Application.Finance;

public sealed record FormattedFinancialStatements(
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    decimal TotalRevenue,
    decimal TotalCostOfSales,
    decimal TotalOperatingExpenses,
    decimal NetIncome,
    bool IsBalanceSheetBalanced,
    decimal BalanceSheetDiscrepancy,
    IReadOnlyList<FinancialStatementLine> AssetLines,
    IReadOnlyList<FinancialStatementLine> LiabilityLines,
    IReadOnlyList<FinancialStatementLine> EquityLines,
    IReadOnlyList<FinancialStatementLine> RevenueLines,
    IReadOnlyList<FinancialStatementLine> ExpenseLines);

public static class FinancialStatementEngine
{
    public static FormattedFinancialStatements BuildStatements(IReadOnlyList<TrialBalanceRow> trialBalanceRows)
    {
        var assetLines = new List<FinancialStatementLine>();
        var liabilityLines = new List<FinancialStatementLine>();
        var equityLines = new List<FinancialStatementLine>();
        var revenueLines = new List<FinancialStatementLine>();
        var expenseLines = new List<FinancialStatementLine>();

        decimal totalAssets = 0m;
        decimal totalLiabilities = 0m;
        decimal totalEquity = 0m;
        decimal totalRevenue = 0m;
        decimal totalCostOfSales = 0m;
        decimal totalOperatingExpenses = 0m;

        foreach (var row in trialBalanceRows)
        {
            var type = row.AccountType.ToLowerInvariant();
            var line = new FinancialStatementLine(row.AccountCode, row.AccountName, row.AccountType, row.NetBalance);

            switch (type)
            {
                case "asset":
                    assetLines.Add(line);
                    totalAssets += row.NetBalance;
                    break;
                case "liability":
                    liabilityLines.Add(line);
                    totalLiabilities += row.NetBalance;
                    break;
                case "equity":
                    equityLines.Add(line);
                    totalEquity += row.NetBalance;
                    break;
                case "revenue":
                    revenueLines.Add(line);
                    totalRevenue += row.NetBalance;
                    break;
                case "expense":
                    expenseLines.Add(line);
                    if (row.AccountCode.StartsWith("50", StringComparison.Ordinal))
                    {
                        totalCostOfSales += row.NetBalance;
                    }
                    else
                    {
                        totalOperatingExpenses += row.NetBalance;
                    }
                    break;
            }
        }

        var totalExpenses = totalCostOfSales + totalOperatingExpenses;
        var netIncome = totalRevenue - totalExpenses;

        // When Net Income is transferred to Retained Earnings:
        // Total Liabilities & Equity = Total Liabilities + Total Equity + Net Income
        var totalLiabAndEquity = totalLiabilities + totalEquity + netIncome;
        var discrepancy = Math.Round(totalAssets - totalLiabAndEquity, 4);
        var isBalanced = Math.Abs(discrepancy) < 0.01m;

        return new FormattedFinancialStatements(
            TotalAssets: Math.Round(totalAssets, 2),
            TotalLiabilities: Math.Round(totalLiabilities, 2),
            TotalEquity: Math.Round(totalEquity, 2),
            TotalRevenue: Math.Round(totalRevenue, 2),
            TotalCostOfSales: Math.Round(totalCostOfSales, 2),
            TotalOperatingExpenses: Math.Round(totalOperatingExpenses, 2),
            NetIncome: Math.Round(netIncome, 2),
            IsBalanceSheetBalanced: isBalanced,
            BalanceSheetDiscrepancy: discrepancy,
            AssetLines: assetLines,
            LiabilityLines: liabilityLines,
            EquityLines: equityLines,
            RevenueLines: revenueLines,
            ExpenseLines: expenseLines);
    }
}
