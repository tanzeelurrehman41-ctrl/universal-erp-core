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

test("multi-leg dispatch applies every signed movement leg", () => {
  const secondBin: InventoryBalanceRow = {
    ...balance(),
    binId: "bin-b",
    onHandQuantity: 5,
  };
  const result = applyInventoryMovementChanges([balance(), secondBin], [
    change({
      movementId: "movement-issue",
      movementType: "issue",
      legs: [
        {
          variantId: "variant-1",
          warehouseId: "warehouse-a",
          binId: "bin-a",
          stockStatus: "available",
          quantityDelta: -4,
          unitOfMeasureId: "uom-ea",
        },
        {
          variantId: "variant-1",
          warehouseId: "warehouse-a",
          binId: "bin-b",
          stockStatus: "available",
          quantityDelta: -2,
          unitOfMeasureId: "uom-ea",
        },
      ],
    }),
  ]);

  assert.equal(result.find(row => row.binId === "bin-a")?.onHandQuantity, 6);
  assert.equal(result.find(row => row.binId === "bin-b")?.onHandQuantity, 3);
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

test("quarantine status transfers move quantity between status rows without changing total stock", () => {
  const available = balance();
  const quarantined: InventoryBalanceRow = {
    ...balance(),
    stockStatus: "quarantine",
    onHandQuantity: 2,
    valuedQuantity: undefined,
  };
  const result = applyInventoryMovementChanges([available, quarantined], [
    change({
      legs: [
        {
          variantId: "variant-1",
          warehouseId: "warehouse-a",
          binId: "bin-a",
          stockStatus: "available",
          quantityDelta: -3,
          unitOfMeasureId: "uom-ea",
        },
        {
          variantId: "variant-1",
          warehouseId: "warehouse-a",
          binId: "bin-a",
          stockStatus: "quarantine",
          quantityDelta: 3,
          unitOfMeasureId: "uom-ea",
        },
      ],
    }),
  ]);

  assert.equal(result.find(row => row.stockStatus === "available")?.onHandQuantity, 7);
  assert.equal(result.find(row => row.stockStatus === "quarantine")?.onHandQuantity, 5);
  assert.equal(result.reduce((sum, row) => sum + row.onHandQuantity, 0), 12);
});

test("movement cannot create a negative balance when the bin is absent from the snapshot", () => {
  assert.throws(
    () => applyInventoryMovementChanges([], [
      change({
        variantId: "variant-1",
        warehouseId: "warehouse-a",
        binId: "bin-a",
        quantityDelta: -1,
        unitOfMeasureId: "uom-ea",
      }),
    ]),
    /absent in the current baseline/,
  );
});

test("movement cannot reduce an existing status balance below zero", () => {
  assert.throws(
    () => applyInventoryMovementChanges([balance()], [
      change({
        variantId: "variant-1",
        warehouseId: "warehouse-a",
        binId: "bin-a",
        quantityDelta: -11,
        unitOfMeasureId: "uom-ea",
      }),
    ]),
    /would make available stock negative/,
  );
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
