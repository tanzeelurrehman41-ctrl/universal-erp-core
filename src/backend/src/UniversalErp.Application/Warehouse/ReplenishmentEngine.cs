using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalErp.Application.Warehouse;

public class ReserveBinStock
{
    public Guid BinId { get; set; }
    public string BinCode { get; set; } = string.Empty;
    public Guid LayerId { get; set; }
    public decimal AvailableQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public class ReplenishmentRecommendation
{
    public bool TriggerNeeded { get; set; }
    public Guid PickBinId { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal ReorderPoint { get; set; }
    public decimal RecommendedQuantity { get; set; }
    public Guid? SourceReserveBinId { get; set; }
    public string? SourceReserveBinCode { get; set; }
    public Guid? SourceLayerId { get; set; }
    public string Rationale { get; set; } = string.Empty;
}

public static class ReplenishmentEngine
{
    public static ReplenishmentRecommendation EvaluateReplenishment(
        ReplenishmentRuleRecord rule,
        decimal currentPickBinStock,
        IReadOnlyList<ReserveBinStock> reserveStock)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        if (currentPickBinStock > rule.ReorderPoint)
        {
            return new ReplenishmentRecommendation
            {
                TriggerNeeded = false,
                PickBinId = rule.PickBinId,
                CurrentStock = currentPickBinStock,
                ReorderPoint = rule.ReorderPoint,
                RecommendedQuantity = 0,
                Rationale = $"Stock level ({currentPickBinStock}) is above reorder point ({rule.ReorderPoint})."
            };
        }

        var rawDeficit = rule.MaxQuantity - currentPickBinStock;
        if (rawDeficit <= 0)
        {
            return new ReplenishmentRecommendation
            {
                TriggerNeeded = false,
                PickBinId = rule.PickBinId,
                CurrentStock = currentPickBinStock,
                ReorderPoint = rule.ReorderPoint,
                RecommendedQuantity = 0,
                Rationale = "Current stock already at or above maximum capacity."
            };
        }

        // Apply increment rounding
        var increment = rule.ReplenishIncrementQty > 0 ? rule.ReplenishIncrementQty : 1m;
        var steps = Math.Ceiling(rawDeficit / increment);
        var targetQty = steps * increment;

        // Total available reserve
        var totalReserve = reserveStock?.Sum(r => r.AvailableQuantity) ?? 0;
        if (totalReserve <= 0)
        {
            return new ReplenishmentRecommendation
            {
                TriggerNeeded = true,
                PickBinId = rule.PickBinId,
                CurrentStock = currentPickBinStock,
                ReorderPoint = rule.ReorderPoint,
                RecommendedQuantity = 0,
                Rationale = "Replenishment trigger breached, but zero reserve stock available in bulk/reserve zones (stockout alert)."
            };
        }

        // Select oldest FIFO layer from reserve
        var oldestReserve = reserveStock!
            .Where(r => r.AvailableQuantity > 0)
            .OrderBy(r => r.ReceivedAt)
            .FirstOrDefault();

        var qtyToTransfer = Math.Min(targetQty, totalReserve);

        return new ReplenishmentRecommendation
        {
            TriggerNeeded = true,
            PickBinId = rule.PickBinId,
            CurrentStock = currentPickBinStock,
            ReorderPoint = rule.ReorderPoint,
            RecommendedQuantity = qtyToTransfer,
            SourceReserveBinId = oldestReserve?.BinId,
            SourceReserveBinCode = oldestReserve?.BinCode,
            SourceLayerId = oldestReserve?.LayerId,
            Rationale = $"Replenish {qtyToTransfer} units to reach target max level {rule.MaxQuantity} from oldest reserve layer ({oldestReserve?.BinCode})."
        };
    }
}
