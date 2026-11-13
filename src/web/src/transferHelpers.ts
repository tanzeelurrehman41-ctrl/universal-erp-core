export type TransferStockBalance = {
  variantId: string;
  warehouseId: string;
  binId: string;
  stockStatus: string;
  onHandQuantity: number;
};

export function getTransferSourceStock(
  balances: readonly TransferStockBalance[],
  variantId: string,
  warehouseId: string,
  binId: string
): number {
  if (!variantId || !warehouseId || !binId) return 0;
  return balances
    .filter(
      (b) =>
        b.variantId === variantId &&
        b.warehouseId === warehouseId &&
        b.binId === binId &&
        b.stockStatus === "available"
    )
    .reduce((sum, b) => sum + (Number(b.onHandQuantity) || 0), 0);
}

export function validateStockTransfer(params: {
  variantId: string;
  sourceWarehouseId: string;
  sourceBinId: string;
  destinationWarehouseId: string;
  destinationBinId: string;
  quantity: string;
  balances: readonly TransferStockBalance[];
  enabled: boolean;
  isSaving: boolean;
}): {
  isValid: boolean;
  availableStock: number;
  validationError: string | null;
} {
  const {
    variantId,
    sourceWarehouseId,
    sourceBinId,
    destinationWarehouseId,
    destinationBinId,
    quantity,
    balances,
    enabled,
    isSaving,
  } = params;

  const availableStock = getTransferSourceStock(balances, variantId, sourceWarehouseId, sourceBinId);
  const trimmedQty = quantity.trim();
  const qtyNum = Number(trimmedQty);
  const hasQty = trimmedQty !== "" && !Number.isNaN(qtyNum);

  let validationError: string | null = null;

  if (sourceBinId && destinationBinId && sourceBinId === destinationBinId) {
    validationError = "Source and destination bins must differ.";
  } else if (variantId && sourceBinId) {
    if (availableStock <= 0) {
      validationError = "Selected source bin has 0 available on hand.";
    } else if (hasQty && qtyNum > availableStock) {
      validationError = `Quantity exceeds available stock. Available: ${availableStock}, Requested: ${qtyNum}.`;
    } else if (hasQty && qtyNum <= 0) {
      validationError = "Quantity must be greater than 0.";
    }
  }

  const isValid = Boolean(
    enabled &&
    !isSaving &&
    variantId &&
    sourceWarehouseId &&
    sourceBinId &&
    destinationWarehouseId &&
    destinationBinId &&
    sourceBinId !== destinationBinId &&
    hasQty &&
    qtyNum > 0 &&
    availableStock > 0 &&
    qtyNum <= availableStock
  );

  return { isValid, availableStock, validationError };
}
