export type StockBalance = {
  variantId: string;
  warehouseId: string;
  binId: string;
  stockStatus: string;
  onHandQuantity: number;
  unitOfMeasureId: string;
};

export type PosLineDraft = {
  variantId: string;
  qty: string;
  price: string;
  taxRate: string;
};

export type VariantOption = {
  variantId: string;
  sku: string;
  productName: string;
};

export function parseStrictNumber(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === "") return null;
  const num = Number(trimmed);
  return Number.isFinite(num) ? num : null;
}

export function buildAvailabilityUrl(warehouseId?: string | null): string {
  if (warehouseId && warehouseId.trim() !== "") {
    return `/api/v1/inventory/availability?warehouseId=${encodeURIComponent(warehouseId)}`;
  }
  return "/api/v1/inventory/availability";
}

export function getAvailableStock(
  balances: readonly StockBalance[],
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

export function validatePosCheckout(params: {
  posCustomerId: string;
  posWarehouseId: string;
  posBinId: string;
  posLines: readonly PosLineDraft[];
  posDiscount: string;
  balances: readonly StockBalance[];
  variants: readonly VariantOption[];
  isLoadingAvailability?: boolean;
}): {
  subtotal: number;
  taxTotal: number;
  discountNum: number;
  netPayable: number;
  validationError: string | null;
} {
  const {
    posCustomerId,
    posWarehouseId,
    posBinId,
    posLines,
    posDiscount,
    balances,
    variants,
    isLoadingAvailability = false,
  } = params;

  const subtotal = posLines.reduce((sum, l) => {
    const q = parseStrictNumber(l.qty);
    const p = parseStrictNumber(l.price);
    if (q === null || q <= 0 || p === null || p <= 0) return sum;
    return sum + q * p;
  }, 0);

  const taxTotal = posLines.reduce((sum, l) => {
    const q = parseStrictNumber(l.qty);
    const p = parseStrictNumber(l.price);
    const t = parseStrictNumber(l.taxRate);
    if (q === null || q <= 0 || p === null || p <= 0 || t === null || t < 0) return sum;
    return sum + q * p * (t / 100);
  }, 0);

  const parsedDiscount = parseStrictNumber(posDiscount);
  const discountNum = parsedDiscount !== null && parsedDiscount >= 0 ? parsedDiscount : 0;
  const netPayable = subtotal + taxTotal - (parsedDiscount ?? 0);

  const validate = (): string | null => {
    if (!posCustomerId || !posWarehouseId || !posBinId) {
      return "Please select Customer, Warehouse, and Bin.";
    }
    if (posLines.length === 0) {
      return "At least one item line is required.";
    }

    const requestedQtyByVariant = new Map<string, number>();

    for (let i = 0; i < posLines.length; i++) {
      const l = posLines[i];
      if (!l.variantId) return `Line ${i + 1}: Please select a product variant.`;
      const qty = parseStrictNumber(l.qty);
      if (qty === null || qty <= 0) return `Line ${i + 1}: Quantity must be greater than zero.`;
      const price = parseStrictNumber(l.price);
      if (price === null || price <= 0) return `Line ${i + 1}: Selling price (PKR) must be greater than zero.`;
      const tax = parseStrictNumber(l.taxRate);
      if (tax === null || tax < 0 || tax > 100) return `Line ${i + 1}: Tax % must be between 0 and 100.`;

      requestedQtyByVariant.set(l.variantId, (requestedQtyByVariant.get(l.variantId) ?? 0) + qty);
    }

    if (isLoadingAvailability) {
      return "Refreshing live bin availability…";
    }

    for (const [variantId, requestedQty] of requestedQtyByVariant.entries()) {
      const avail = getAvailableStock(balances, variantId, posWarehouseId, posBinId);
      if (requestedQty > avail) {
        const v = variants.find((item) => item.variantId === variantId);
        const skuLabel = v?.sku ?? "selected item";
        return `Insufficient stock for ${skuLabel} in selected bin (Available: ${avail} EA, Requested: ${requestedQty} EA).`;
      }
    }

    if (parsedDiscount === null || parsedDiscount < 0) {
      return "Discount (PKR) must be a valid non-negative number.";
    }
    if (subtotal - parsedDiscount <= 0) {
      return "Discount cannot equal or exceed the gross subtotal.";
    }
    if (netPayable <= 0) {
      return "Invoice total payable must be greater than PKR 0.00.";
    }
    return null;
  };

  return {
    subtotal,
    taxTotal,
    discountNum,
    netPayable,
    validationError: validate(),
  };
}
