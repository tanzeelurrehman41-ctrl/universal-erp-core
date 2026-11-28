export type InventoryBalanceRow = {
  variantId: string;
  warehouseId: string;
  binId: string;
  stockStatus: string;
  onHandQuantity: number;
  unitOfMeasureId: string;
  valuedQuantity?: number;
};

export type InventoryMovementFeedChange = {
  entityType: string;
  changeType: string;
  entityId: string;
  cursor: string;
  payload: unknown;
};

type JsonRecord = Record<string, unknown>;

function isRecord(value: unknown): value is JsonRecord {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function readField(record: JsonRecord, camelName: string, pascalName: string): unknown {
  return record[camelName] ?? record[pascalName];
}

function requiredId(record: JsonRecord, camelName: string, pascalName: string): string {
  const value = readField(record, camelName, pascalName);
  if (typeof value !== "string" || value.length === 0) {
    throw new Error(`Inventory movement is missing ${camelName}.`);
  }
  return value;
}

function requiredNumber(record: JsonRecord, camelName: string, pascalName: string): number {
  const value = readField(record, camelName, pascalName);
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error(`Inventory movement has an invalid ${camelName}.`);
  }
  return value;
}

function applyDelta(
  balances: InventoryBalanceRow[],
  variantId: string,
  warehouseId: string,
  binId: string,
  unitOfMeasureId: string,
  delta: number,
  stockStatus = "available",
): void {
  const existing = balances.find((balance) =>
    balance.variantId === variantId &&
    balance.warehouseId === warehouseId &&
    balance.binId === binId &&
    balance.stockStatus === stockStatus,
  );

  if (existing) {
    existing.onHandQuantity += delta;
    // Movement events lack cost-layer detail, so the previous valuation is stale.
    existing.valuedQuantity = undefined;
    return;
  }

  balances.push({
    variantId,
    warehouseId,
    binId,
    stockStatus,
    onHandQuantity: delta,
    unitOfMeasureId,
  });
}

export function applyInventoryMovementChanges(
  currentBalances: InventoryBalanceRow[],
  changes: readonly InventoryMovementFeedChange[],
): InventoryBalanceRow[] {
  const next = currentBalances.map((balance) => ({ ...balance }));

  for (const change of changes) {
    if (change.entityType !== "inventory_movement" || change.changeType !== "upsert") {
      throw new Error("The inventory feed contains an unsupported movement change.");
    }

    if (!isRecord(change.payload)) {
      throw new Error("The inventory feed contains an invalid movement payload.");
    }

    const payload = change.payload;
    const legs = payload.legs;
    if (Array.isArray(legs)) {
      for (const item of legs) {
        if (!isRecord(item)) throw new Error("Inventory movement contains an invalid leg.");
        const legVariantId = requiredId(item, "variantId", "VariantId");
        const warehouseId = requiredId(item, "warehouseId", "WarehouseId");
        const binId = requiredId(item, "binId", "BinId");
        const legUomId = requiredId(item, "unitOfMeasureId", "UnitOfMeasureId");
        const delta = requiredNumber(item, "quantityDelta", "QuantityDelta");
        const stockStatusValue = readField(item, "stockStatus", "StockStatus");
        const stockStatus = typeof stockStatusValue === "string" && stockStatusValue.length > 0
          ? stockStatusValue
          : "available";
        applyDelta(next, legVariantId, warehouseId, binId, legUomId, delta, stockStatus);
      }
      continue;
    }

    const variantId = requiredId(payload, "variantId", "VariantId");
    const unitOfMeasureId = requiredId(payload, "unitOfMeasureId", "UnitOfMeasureId");
    const sourceWarehouse = readField(payload, "sourceWarehouseId", "SourceWarehouseId");
    const destinationWarehouse = readField(payload, "destinationWarehouseId", "DestinationWarehouseId");

    if (typeof sourceWarehouse === "string" && typeof destinationWarehouse === "string") {
      const quantity = requiredNumber(payload, "quantity", "Quantity");
      if (quantity <= 0) throw new Error("Inventory transfer quantity must be positive.");
      const sourceBinId = requiredId(payload, "sourceBinId", "SourceBinId");
      const destinationBinId = requiredId(payload, "destinationBinId", "DestinationBinId");

      applyDelta(next, variantId, sourceWarehouse, sourceBinId, unitOfMeasureId, -quantity);
      applyDelta(next, variantId, destinationWarehouse, destinationBinId, unitOfMeasureId, quantity);
      continue;
    }

    const warehouseId = requiredId(payload, "warehouseId", "WarehouseId");
    const binId = requiredId(payload, "binId", "BinId");
    const adjustmentDelta = readField(payload, "quantityDelta", "QuantityDelta");

    if (typeof adjustmentDelta === "number") {
      if (!Number.isFinite(adjustmentDelta)) {
        throw new Error("Inventory adjustment quantity is invalid.");
      }
      applyDelta(next, variantId, warehouseId, binId, unitOfMeasureId, adjustmentDelta);
      continue;
    }

    const quantity = requiredNumber(payload, "quantity", "Quantity");
    if (quantity <= 0) throw new Error("Inventory receipt quantity must be positive.");
    applyDelta(next, variantId, warehouseId, binId, unitOfMeasureId, quantity);
  }

  return next;
}
