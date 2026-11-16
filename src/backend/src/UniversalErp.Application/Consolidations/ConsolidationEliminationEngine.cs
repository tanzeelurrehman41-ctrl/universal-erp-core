using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalErp.Application.Consolidations;

public class ConsolidationEliminationEngine
{
    public record IntercompanyBalancePair(
        Guid FromCompanyId,
        Guid ToCompanyId,
        decimal DueFromAmount,
        decimal DueToAmount,
        string DueFromAccountCode = "1300",
        string DueToAccountCode = "2200"
    );

    public record IntercompanyTradingVolume(
        Guid SellerCompanyId,
        Guid BuyerCompanyId,
        decimal TotalSalesAmount,
        string SalesRevenueAccountCode = "4000",
        string CostOfGoodsSoldAccountCode = "5000"
    );

    public record UnrealizedInventoryStock(
        Guid SellerCompanyId,
        Guid BuyerCompanyId,
        decimal QuantityHeld,
        decimal UnitCost,
        decimal UnitTransferPrice,
        string InventoryAccountCode = "1200",
        string CogsAccountCode = "5000"
    );

    public record EliminationCalculationResult(
        IReadOnlyList<EliminationLineDto> Lines,
        decimal TotalIntercompanyBalanceEliminated,
        decimal TotalRevenueEliminated,
        decimal TotalUnrealizedProfitEliminated,
        decimal TotalDebits,
        decimal TotalCredits,
        bool IsBalanced
    );

    public EliminationCalculationResult CalculateEliminations(
        Guid consolidationRunId,
        IReadOnlyList<IntercompanyBalancePair> balancePairs,
        IReadOnlyList<IntercompanyTradingVolume> tradingVolumes,
        IReadOnlyList<UnrealizedInventoryStock> heldStocks,
        bool eliminateUnrealizedProfit = true)
    {
        var lines = new List<EliminationLineDto>();
        decimal balanceEliminated = 0m;
        decimal revenueEliminated = 0m;
        decimal unrealizedProfitEliminated = 0m;

        // 1. Reciprocal Due-To / Due-From Elimination
        foreach (var pair in balancePairs)
        {
            var eliminationAmount = Math.Min(pair.DueFromAmount, pair.DueToAmount);
            if (eliminationAmount <= 0)
                continue;

            // Dr 2200 Intercompany Payable (Buyer/Debtor company)
            lines.Add(new EliminationLineDto(
                Guid.NewGuid(),
                consolidationRunId,
                "intercompany_balance",
                pair.FromCompanyId,
                pair.ToCompanyId,
                pair.DueToAccountCode,
                eliminationAmount,
                0m,
                $"Elimination of reciprocal intercompany payable ({pair.DueToAccountCode}) to company {pair.FromCompanyId}"
            ));

            // Cr 1300 Intercompany Receivable (Seller/Creditor company)
            lines.Add(new EliminationLineDto(
                Guid.NewGuid(),
                consolidationRunId,
                "intercompany_balance",
                pair.FromCompanyId,
                pair.ToCompanyId,
                pair.DueFromAccountCode,
                0m,
                eliminationAmount,
                $"Elimination of reciprocal intercompany receivable ({pair.DueFromAccountCode}) from company {pair.ToCompanyId}"
            ));

            balanceEliminated += eliminationAmount;
        }

        // 2. Intercompany Revenue & Cost of Goods Sold Elimination
        foreach (var trade in tradingVolumes)
        {
            if (trade.TotalSalesAmount <= 0)
                continue;

            // Dr 4000 Sales Revenue (Seller company)
            lines.Add(new EliminationLineDto(
                Guid.NewGuid(),
                consolidationRunId,
                "intercompany_revenue",
                trade.SellerCompanyId,
                trade.BuyerCompanyId,
                trade.SalesRevenueAccountCode,
                trade.TotalSalesAmount,
                0m,
                $"Elimination of intercompany sales revenue ({trade.SalesRevenueAccountCode}) from seller {trade.SellerCompanyId}"
            ));

            // Cr 5000 Cost of Goods Sold (Buyer / Group COGS)
            lines.Add(new EliminationLineDto(
                Guid.NewGuid(),
                consolidationRunId,
                "intercompany_revenue",
                trade.SellerCompanyId,
                trade.BuyerCompanyId,
                trade.CostOfGoodsSoldAccountCode,
                0m,
                trade.TotalSalesAmount,
                $"Elimination of intercompany cost of goods sold ({trade.CostOfGoodsSoldAccountCode}) on purchases from seller {trade.SellerCompanyId}"
            ));

            revenueEliminated += trade.TotalSalesAmount;
        }

        // 3. Unrealized Inventory Profit Elimination (on ending inventory held)
        if (eliminateUnrealizedProfit)
        {
            foreach (var stock in heldStocks)
            {
                if (stock.QuantityHeld <= 0 || stock.UnitTransferPrice <= stock.UnitCost)
                    continue;

                var unitProfit = stock.UnitTransferPrice - stock.UnitCost;
                var unrealizedProfit = Math.Round(stock.QuantityHeld * unitProfit, 4, MidpointRounding.AwayFromZero);

                if (unrealizedProfit <= 0)
                    continue;

                // Dr 5000 Cost of Goods Sold (Increases group expense to eliminate profit)
                lines.Add(new EliminationLineDto(
                    Guid.NewGuid(),
                    consolidationRunId,
                    "unrealized_inventory_profit",
                    stock.SellerCompanyId,
                    stock.BuyerCompanyId,
                    stock.CogsAccountCode,
                    unrealizedProfit,
                    0m,
                    $"Elimination of unrealized profit in ending inventory (Dr {stock.CogsAccountCode}) held by buyer {stock.BuyerCompanyId}"
                ));

                // Cr 1200 Inventory Asset (Reduces group inventory asset down to actual cost)
                lines.Add(new EliminationLineDto(
                    Guid.NewGuid(),
                    consolidationRunId,
                    "unrealized_inventory_profit",
                    stock.SellerCompanyId,
                    stock.BuyerCompanyId,
                    stock.InventoryAccountCode,
                    0m,
                    unrealizedProfit,
                    $"Elimination of unrealized markup on ending inventory (Cr {stock.InventoryAccountCode}) down to cost"
                ));

                unrealizedProfitEliminated += unrealizedProfit;
            }
        }

        decimal totalDebits = lines.Sum(l => l.DebitAmountPkr);
        decimal totalCredits = lines.Sum(l => l.CreditAmountPkr);
        bool isBalanced = Math.Abs(totalDebits - totalCredits) < 0.0001m;

        return new EliminationCalculationResult(
            lines,
            balanceEliminated,
            revenueEliminated,
            unrealizedProfitEliminated,
            totalDebits,
            totalCredits,
            isBalanced
        );
    }
}
