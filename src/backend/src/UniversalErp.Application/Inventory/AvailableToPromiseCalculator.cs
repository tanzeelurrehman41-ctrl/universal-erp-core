namespace UniversalErp.Application.Inventory;

/// <summary>
/// Slice of physical stock and open reservation demands for ATP calculation.
/// </summary>
public sealed record AtpStockSlice(
    Guid VariantId,
    Guid WarehouseId,
    Guid? BinId,
    decimal PhysicalOnHand,
    decimal ActiveReservedQty,
    decimal ValuedQty);

/// <summary>
/// Output calculation of Available-To-Promise inventory.
/// </summary>
public sealed record AtpCalculationResult(
    Guid VariantId,
    Guid WarehouseId,
    Guid? BinId,
    decimal PhysicalOnHand,
    decimal ActiveReservedQty,
    decimal AvailableToPromiseQty,
    decimal ValuedQty,
    decimal SellableAtpQty);

/// <summary>
/// Available-to-Promise (ATP) Calculator:
/// ATP = max(0, Physical Stock - Active Reservations).
/// Sellable ATP = min(Valued Stock, ATP), ensuring unvalued stock is never promised or sold.
/// </summary>
public static class AvailableToPromiseCalculator
{
    public static AtpCalculationResult Calculate(AtpStockSlice slice)
    {
        var atp = Math.Max(0m, slice.PhysicalOnHand - slice.ActiveReservedQty);
        var sellableAtp = Math.Min(slice.ValuedQty, atp);

        return new AtpCalculationResult(
            VariantId: slice.VariantId,
            WarehouseId: slice.WarehouseId,
            BinId: slice.BinId,
            PhysicalOnHand: slice.PhysicalOnHand,
            ActiveReservedQty: slice.ActiveReservedQty,
            AvailableToPromiseQty: atp,
            ValuedQty: slice.ValuedQty,
            SellableAtpQty: Math.Max(0m, sellableAtp));
    }

    public static bool CanReserve(AtpStockSlice slice, decimal requestedQty)
    {
        if (requestedQty <= 0m) return false;
        var atp = Calculate(slice);
        return atp.AvailableToPromiseQty >= requestedQty;
    }

    public static bool CanSellImmediately(AtpStockSlice slice, decimal requestedQty)
    {
        if (requestedQty <= 0m) return false;
        var atp = Calculate(slice);
        return atp.SellableAtpQty >= requestedQty;
    }
}
