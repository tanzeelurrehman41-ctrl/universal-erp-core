namespace UniversalErp.Application.Inventory;

/// <summary>
/// Represents an open FIFO inventory receipt/valuation layer available for outbound consumption.
/// </summary>
public sealed record FifoCostLayerSlice(
    Guid LayerId,
    DateTimeOffset ReceivedAt,
    decimal RemainingQty,
    decimal UnitCost);

/// <summary>
/// Represents the quantity and cost consumed from a single FIFO valuation layer.
/// </summary>
public sealed record FifoLayerAllocation(
    Guid LayerId,
    decimal ConsumedQty,
    decimal UnitCost,
    decimal ExtendedCost);

/// <summary>
/// Aggregated FIFO allocation result for an outbound inventory line.
/// </summary>
public sealed record FifoAllocationResult(
    IReadOnlyList<FifoLayerAllocation> Allocations,
    decimal TotalCost,
    decimal EffectiveUnitCost);

/// <summary>
/// Deterministic First-In, First-Out (FIFO) inventory cost allocation calculator.
/// </summary>
public static class FifoCostAllocator
{
    /// <summary>
    /// Allocates <paramref name="requestedQty"/> across <paramref name="availableLayers"/> in FIFO order
    /// (oldest <see cref="FifoCostLayerSlice.ReceivedAt"/> first, breaking ties by <see cref="FifoCostLayerSlice.LayerId"/>).
    /// Throws <see cref="InvalidOperationException"/> with <c>MISSING_VALUATION_DATA</c> or
    /// <c>INSUFFICIENT_VALUATION_LAYERS</c> when valid open layers cannot cover the full quantity.
    /// </summary>
    public static FifoAllocationResult Allocate(
        IReadOnlyList<FifoCostLayerSlice> availableLayers,
        decimal requestedQty)
    {
        if (requestedQty <= 0m)
        {
            throw new InvalidOperationException("INVALID_INVOICE_LINE: Requested quantity must be greater than zero.");
        }

        var openLayers = availableLayers
            .Where(l => l.RemainingQty > 0m && l.UnitCost > 0m)
            .OrderBy(l => l.ReceivedAt)
            .ThenBy(l => l.LayerId)
            .ToList();

        if (openLayers.Count == 0)
        {
            throw new InvalidOperationException(
                $"MISSING_VALUATION_DATA: No valid open FIFO valuation layers exist to value {requestedQty} units.");
        }

        var totalAvailable = openLayers.Sum(l => l.RemainingQty);
        if (totalAvailable < requestedQty)
        {
            throw new InvalidOperationException(
                $"INSUFFICIENT_VALUATION_LAYERS: Available valued quantity {totalAvailable}, Requested {requestedQty}.");
        }

        var allocations = new List<FifoLayerAllocation>();
        var remainingToAllocate = requestedQty;

        foreach (var layer in openLayers)
        {
            if (remainingToAllocate <= 0m) break;

            var takeQty = Math.Min(remainingToAllocate, layer.RemainingQty);
            var extendedCost = Math.Round(takeQty * layer.UnitCost, 4, MidpointRounding.AwayFromZero);

            allocations.Add(new FifoLayerAllocation(
                LayerId: layer.LayerId,
                ConsumedQty: takeQty,
                UnitCost: layer.UnitCost,
                ExtendedCost: extendedCost));

            remainingToAllocate -= takeQty;
        }

        var totalCost = allocations.Sum(a => a.ExtendedCost);
        var effectiveUnitCost = Math.Round(totalCost / requestedQty, 4, MidpointRounding.AwayFromZero);
        if (totalCost <= 0m)
        {
            throw new InvalidOperationException("MISSING_VALUATION_DATA: Computed FIFO COGS must be positive.");
        }

        return new FifoAllocationResult(allocations, totalCost, effectiveUnitCost);
    }
}
