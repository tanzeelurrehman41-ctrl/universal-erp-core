export type AtpStockInput = {
  physicalOnHand: number;
  activeReserved: number;
  valuedStock: number;
};

export type AtpStockResult = {
  atp: number;
  sellableAtp: number;
};

/**
 * Calculates Available-To-Promise (ATP) and Sellable ATP.
 * - ATP = max(0, physicalOnHand - activeReserved).
 * - Sellable ATP = max(0, min(valuedStock, ATP)), ensuring unvalued stock is never promised or sold.
 */
export function calculateAtp(input: AtpStockInput): AtpStockResult {
  const physical = Math.max(0, Number(input.physicalOnHand) || 0);
  const reserved = Math.max(0, Number(input.activeReserved) || 0);
  const valued = Math.max(0, Number(input.valuedStock) || 0);

  const atp = Math.max(0, physical - reserved);
  const sellableAtp = Math.max(0, Math.min(valued, atp));

  return { atp, sellableAtp };
}

export function validateReservation(params: {
  variantId: string;
  warehouseId: string;
  quantity: string;
  atp: number;
  enabled: boolean;
  isSaving: boolean;
}): {
  isValid: boolean;
  validationError: string | null;
} {
  const { variantId, warehouseId, quantity, atp, enabled, isSaving } = params;
  const trimmed = quantity.trim();
  const qtyNum = Number(trimmed);
  const hasQty = trimmed !== "" && !Number.isNaN(qtyNum);

  let validationError: string | null = null;

  if (!variantId) {
    validationError = "Please select a product variant.";
  } else if (!warehouseId) {
    validationError = "Please select a warehouse.";
  } else if (!hasQty || qtyNum <= 0) {
    validationError = "Reservation quantity must be greater than zero.";
  } else if (qtyNum > atp) {
    validationError = `Requested quantity (${qtyNum}) exceeds Available-To-Promise (${atp}).`;
  }

  const isValid = Boolean(
    enabled &&
    !isSaving &&
    variantId &&
    warehouseId &&
    hasQty &&
    qtyNum > 0 &&
    qtyNum <= atp
  );

  return { isValid, validationError };
}

export function validateCountLine(params: {
  snapshotQty: number;
  countedQtyStr: string;
}): {
  isValid: boolean;
  varianceQty: number;
  validationError: string | null;
} {
  const trimmed = params.countedQtyStr.trim();
  const countedNum = Number(trimmed);
  const hasCount = trimmed !== "" && !Number.isNaN(countedNum);

  if (!hasCount || countedNum < 0) {
    return {
      isValid: false,
      varianceQty: 0,
      validationError: "Physical count must be a non-negative number.",
    };
  }

  const varianceQty = countedNum - params.snapshotQty;
  return {
    isValid: true,
    varianceQty,
    validationError: null,
  };
}

export function calculateCountVariance(
  snapshotQty: number,
  countedQty: number,
  unitCost: number
): { varianceQty: number; varianceCost: number } {
  const varianceQty = countedQty - snapshotQty;
  const varianceCost = varianceQty * (Number(unitCost) || 0);
  return { varianceQty, varianceCost };
}
