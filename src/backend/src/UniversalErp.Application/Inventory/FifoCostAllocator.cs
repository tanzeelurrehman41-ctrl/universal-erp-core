namespace UniversalErp.Application.Inventory;

/// <summary>
/// Represents an open FIFO inventory receipt/valuation layer available for outbound consumption.
/// </summary>
public sealed record FifoCostLayerSlice(
    Guid LayerId,
    DateTimeOffset ReceivedAt,
    decimal RemainingQty,
    decimal UnitCost,
    Guid? WarehouseId = null,
    Guid? BinId = null);

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
/// Strictly bin-bounded: never permits borrowing valuation layers across warehouses or bins.
/// </summary>
public static class FifoCostAllocator
{
    /// <summary>
    /// Allocates <paramref name="requestedQty"/> across <paramref name="availableLayers"/> in FIFO order
    /// (oldest <see cref="FifoCostLayerSlice.ReceivedAt"/> first, breaking ties by <see cref="FifoCostLayerSlice.LayerId"/>).
    /// Throws <see cref="InvalidOperationException"/> with <c>MISSING_VALUATION_DATA</c> or
    /// <c>INSUFFICIENT_VALUATION_LAYERS</c> when valid open layers in the target bin cannot cover the full quantity.
    /// Throws <c>CROSS_BIN_VALUATION_FORBIDDEN</c> if any layer targets a different warehouse/bin.
    /// </summary>
    public static FifoAllocationResult Allocate(
        IReadOnlyList<FifoCostLayerSlice> availableLayers,
        decimal requestedQty,
        string? locationDescription = null,
        Guid? expectedWarehouseId = null,
        Guid? expectedBinId = null)
    {
        if (requestedQty <= 0m)
        {
            throw new InvalidOperationException("INVALID_INVOICE_LINE: Requested quantity must be greater than zero.");
        }

        // Strict bin containment check: fail-closed if any layer does not belong to the target location
        if (expectedWarehouseId.HasValue || expectedBinId.HasValue)
        {
            foreach (var l in availableLayers)
            {
                if ((expectedWarehouseId.HasValue && l.WarehouseId.HasValue && l.WarehouseId.Value != expectedWarehouseId.Value) ||
                    (expectedBinId.HasValue && l.BinId.HasValue && l.BinId.Value != expectedBinId.Value))
                {
                    throw new InvalidOperationException(
                        $"CROSS_BIN_VALUATION_FORBIDDEN: Attempted to consume layer {l.LayerId} belonging to Warehouse {l.WarehouseId}/Bin {l.BinId} for checkout in {locationDescription ?? $"{expectedWarehouseId}/{expectedBinId}"}.");
                }
            }
        }

        var openLayers = availableLayers
            .Where(l => l.RemainingQty > 0m && l.UnitCost > 0m)
            .OrderBy(l => l.ReceivedAt)
            .ThenBy(l => l.LayerId)
            .ToList();

        var locationSuffix = string.IsNullOrWhiteSpace(locationDescription)
            ? (expectedBinId.HasValue ? $"in bin {expectedBinId}" : "in selected bin")
            : $"in {locationDescription}";

        if (openLayers.Count == 0)
        {
            throw new InvalidOperationException(
                $"MISSING_VALUATION_DATA: Selected bin contains unvalued stock or no valid FIFO cost layers exist {locationSuffix} to value {requestedQty} units.");
        }

        var totalAvailable = openLayers.Sum(l => l.RemainingQty);
        if (totalAvailable < requestedQty)
        {
            throw new InvalidOperationException(
                $"INSUFFICIENT_VALUATION_LAYERS: Selected bin contains unvalued or insufficiently valued stock {locationSuffix}. Available valued quantity is {totalAvailable}, Requested {requestedQty}.");
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
