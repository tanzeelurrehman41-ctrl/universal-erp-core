import test from "node:test";
import assert from "node:assert/strict";
import {
  applyInventoryMovementChanges,
  type InventoryBalanceRow,
  type InventoryMovementFeedChange,
} from "../src/inventoryMovementSync.ts";

const balance = (): InventoryBalanceRow => ({
  variantId: "variant-1",
  warehouseId: "warehouse-a",
  binId: "bin-a",
  stockStatus: "available",
  onHandQuantity: 10,
  unitOfMeasureId: "uom-ea",
  valuedQuantity: 10,
});

const change = (payload: unknown): InventoryMovementFeedChange => ({
  entityType: "inventory_movement",
  changeType: "upsert",
  entityId: "movement-1",
  cursor: "1",
  payload,
});

test("receipt movement increments the target bin and invalidates stale valuation", () => {
  const result = applyInventoryMovementChanges([balance()], [
    change({
      VariantId: "variant-1",
      WarehouseId: "warehouse-a",
      BinId: "bin-a",
      Quantity: 4,
      unitOfMeasureId: "uom-ea",
    }),
  ]);

  assert.equal(result[0].onHandQuantity, 14);
  assert.equal(result[0].valuedQuantity, undefined);
  assert.equal(result[0].unitOfMeasureId, "uom-ea");
});

test("transfer movement applies equal and opposite source and destination deltas", () => {
  const result = applyInventoryMovementChanges([balance()], [
    change({
      variantId: "variant-1",
      sourceWarehouseId: "warehouse-a",
      sourceBinId: "bin-a",
      destinationWarehouseId: "warehouse-b",
      destinationBinId: "bin-b",
      quantity: 3,
      unitOfMeasureId: "uom-ea",
    }),
  ]);

  assert.equal(result.find(row => row.warehouseId === "warehouse-a")?.onHandQuantity, 7);
  assert.equal(result.find(row => row.warehouseId === "warehouse-b")?.onHandQuantity, 3);
  assert.equal(result.length, 2);
});

test("adjustment movement applies a signed quantity delta", () => {
  const result = applyInventoryMovementChanges([balance()], [
    change({
      variantId: "variant-1",
      warehouseId: "warehouse-a",
      binId: "bin-a",
      quantityDelta: -2.5,
      unitOfMeasureId: "uom-ea",
    }),
  ]);

  assert.equal(result[0].onHandQuantity, 7.5);
});

test("a new balance row is created when a movement targets an empty bin", () => {
  const result = applyInventoryMovementChanges([], [
    change({
      variantId: "variant-1",
      warehouseId: "warehouse-a",
      binId: "bin-a",
      quantity: 5,
      unitOfMeasureId: "uom-ea",
    }),
  ]);

  assert.deepEqual(result, [{
    variantId: "variant-1",
    warehouseId: "warehouse-a",
    binId: "bin-a",
    stockStatus: "available",
    onHandQuantity: 5,
    unitOfMeasureId: "uom-ea",
  }]);
});

test("unsupported feed events fail closed without mutating the saved balances", () => {
  const saved = [balance()];

  assert.throws(
    () => applyInventoryMovementChanges(saved, [{
      ...change({}),
      entityType: "inventory_reservation",
    }]),
    /unsupported movement change/,
  );
  assert.equal(saved[0].onHandQuantity, 10);
});
