import test from "node:test";
import assert from "node:assert/strict";
import {
  getTransferSourceStock,
  validateStockTransfer,
  type TransferStockBalance,
} from "../src/transferHelpers.ts";

const sampleBalances: TransferStockBalance[] = [
  {
    variantId: "var-scissor",
    warehouseId: "wh-store-01",
    binId: "bin-store-01",
    stockStatus: "available",
    onHandQuantity: 0,
  },
  {
    variantId: "var-scissor",
    warehouseId: "wh-store-01",
    binId: "bin-pick-01",
    stockStatus: "available",
    onHandQuantity: 1,
  },
  {
    variantId: "var-scissor",
    warehouseId: "wh-main-w1",
    binId: "bin-pick-01",
    stockStatus: "available",
    onHandQuantity: 20,
  },
];

test("getTransferSourceStock returns correct available stock by location", () => {
  assert.equal(getTransferSourceStock(sampleBalances, "var-scissor", "wh-store-01", "bin-store-01"), 0);
  assert.equal(getTransferSourceStock(sampleBalances, "var-scissor", "wh-store-01", "bin-pick-01"), 1);
  assert.equal(getTransferSourceStock(sampleBalances, "var-scissor", "wh-main-w1", "bin-pick-01"), 20);
  assert.equal(getTransferSourceStock(sampleBalances, "var-scissor", "wh-unknown", "bin-none"), 0);
});

test("validateStockTransfer disables button and flags error when source bin has 0 on hand", () => {
  const result = validateStockTransfer({
    variantId: "var-scissor",
    sourceWarehouseId: "wh-store-01",
    sourceBinId: "bin-store-01",
    destinationWarehouseId: "wh-main-w1",
    destinationBinId: "bin-pick-01",
    quantity: "2",
    balances: sampleBalances,
    enabled: true,
    isSaving: false,
  });

  assert.equal(result.isValid, false);
  assert.equal(result.availableStock, 0);
  assert.equal(result.validationError, "Selected source bin has 0 available on hand.");
});

test("validateStockTransfer disables button when requested quantity exceeds available stock", () => {
  const result = validateStockTransfer({
    variantId: "var-scissor",
    sourceWarehouseId: "wh-store-01",
    sourceBinId: "bin-pick-01", // has 1 EA
    destinationWarehouseId: "wh-main-w1",
    destinationBinId: "bin-receive-01",
    quantity: "2", // requests 2 EA
    balances: sampleBalances,
    enabled: true,
    isSaving: false,
  });

  assert.equal(result.isValid, false);
  assert.equal(result.availableStock, 1);
  assert.equal(result.validationError, "Quantity exceeds available stock. Available: 1, Requested: 2.");
});

test("validateStockTransfer blocks transfer when source and destination bins are identical", () => {
  const result = validateStockTransfer({
    variantId: "var-scissor",
    sourceWarehouseId: "wh-main-w1",
    sourceBinId: "bin-pick-01",
    destinationWarehouseId: "wh-main-w1",
    destinationBinId: "bin-pick-01",
    quantity: "2",
    balances: sampleBalances,
    enabled: true,
    isSaving: false,
  });

  assert.equal(result.isValid, false);
  assert.equal(result.validationError, "Source and destination bins must differ.");
});

test("validateStockTransfer succeeds and enables button when quantity is within available stock", () => {
  const result = validateStockTransfer({
    variantId: "var-scissor",
    sourceWarehouseId: "wh-main-w1",
    sourceBinId: "bin-pick-01", // has 20 EA
    destinationWarehouseId: "wh-store-01",
    destinationBinId: "bin-receive-01",
    quantity: "2", // requests 2 EA
    balances: sampleBalances,
    enabled: true,
    isSaving: false,
  });

  assert.equal(result.isValid, true);
  assert.equal(result.availableStock, 20);
  assert.equal(result.validationError, null);
});
