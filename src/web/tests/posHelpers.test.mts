import test from "node:test";
import assert from "node:assert/strict";
import {
  buildAvailabilityUrl,
  getAvailableStock,
  getValuedStock,
  validatePosCheckout,
  type StockBalance,
} from "../src/posHelpers.ts";

const WH_MAIN = "11111111-1111-1111-1111-111111111111";
const WH_STORE = "22222222-2222-2222-2222-222222222222";
const BIN_PICK01 = "33333333-3333-3333-3333-333333333333";
const BIN_RECEIVE01 = "44444444-4444-4444-4444-444444444444";
const BIN_STORE01 = "55555555-5555-5555-5555-555555555555";
const VAR_SCISSOR = "2871558f-e577-4d2d-b365-fa60be311111";
const VAR_OTHER = "66666666-6666-6666-6666-666666666666";
const UOM_EA = "77777777-7777-7777-7777-777777777777";
const CUST_ID = "88888888-8888-8888-8888-888888888888";

const sampleBalances: StockBalance[] = [
  {
    variantId: VAR_SCISSOR,
    warehouseId: WH_MAIN,
    binId: BIN_PICK01,
    stockStatus: "available",
    onHandQuantity: 18,
    unitOfMeasureId: UOM_EA,
    valuedQuantity: 18,
  },
  {
    variantId: VAR_SCISSOR,
    warehouseId: WH_MAIN,
    binId: BIN_PICK01,
    stockStatus: "quarantine",
    onHandQuantity: 4,
    unitOfMeasureId: UOM_EA,
    valuedQuantity: 0,
  },
  {
    variantId: VAR_SCISSOR,
    warehouseId: WH_MAIN,
    binId: BIN_RECEIVE01,
    stockStatus: "available",
    onHandQuantity: 29,
    unitOfMeasureId: UOM_EA,
    valuedQuantity: 29,
  },
  {
    variantId: VAR_SCISSOR,
    warehouseId: WH_STORE,
    binId: BIN_STORE01,
    stockStatus: "available",
    onHandQuantity: 3,
    unitOfMeasureId: UOM_EA,
    valuedQuantity: 0,
  },
];

const variants = [
  { variantId: VAR_SCISSOR, sku: "SCISSOR-EA-01", productName: "Scissors" },
  { variantId: VAR_OTHER, sku: "TAPE-EA-01", productName: "Packing Tape" },
];

test("POS availability displays 18 EA for MAIN-W1 / PICK-01 available stock", () => {
  const avail = getAvailableStock(sampleBalances, VAR_SCISSOR, WH_MAIN, BIN_PICK01);
  assert.equal(avail, 18);
});

test("Availability URL scopes by selected warehouse and refreshes across warehouse/bin/product changes", () => {
  assert.equal(
    buildAvailabilityUrl(WH_MAIN),
    `/api/v1/inventory/availability?warehouseId=${WH_MAIN}`
  );
  assert.equal(
    buildAvailabilityUrl(WH_STORE),
    `/api/v1/inventory/availability?warehouseId=${WH_STORE}`
  );

  // Switching bin changes available stock immediately
  assert.equal(getAvailableStock(sampleBalances, VAR_SCISSOR, WH_MAIN, BIN_PICK01), 18);
  assert.equal(getAvailableStock(sampleBalances, VAR_SCISSOR, WH_MAIN, BIN_RECEIVE01), 29);

  // Switching warehouse changes available stock immediately
  assert.equal(getAvailableStock(sampleBalances, VAR_SCISSOR, WH_STORE, BIN_STORE01), 3);

  // Switching product updates available stock immediately (0 EA for unstocked product)
  assert.equal(getAvailableStock(sampleBalances, VAR_OTHER, WH_MAIN, BIN_PICK01), 0);
});

test("Checkout rejects zero selling price without requiring manual cost field", () => {
  const res = validatePosCheckout({
    posCustomerId: CUST_ID,
    posWarehouseId: WH_MAIN,
    posBinId: BIN_PICK01,
    posLines: [{ variantId: VAR_SCISSOR, qty: "5", price: "0", taxRate: "0" }],
    posDiscount: "0",
    balances: sampleBalances,
    variants,
  });
  assert.match(res.validationError ?? "", /Selling price \(PKR\) must be greater than zero/);
});

test("Checkout blocks when requested quantity exceeds bin availability", () => {
  const res = validatePosCheckout({
    posCustomerId: CUST_ID,
    posWarehouseId: WH_MAIN,
    posBinId: BIN_PICK01,
    posLines: [{ variantId: VAR_SCISSOR, qty: "19", price: "75", taxRate: "0" }],
    posDiscount: "0",
    balances: sampleBalances,
    variants,
  });
  assert.match(res.validationError ?? "", /Insufficient stock.*Available: 18 EA.*Requested: 19 EA/);
});

test("Checkout succeeds for valid 5 EA at PKR 75 in PICK-01 without manual cost", () => {
  const res = validatePosCheckout({
    posCustomerId: CUST_ID,
    posWarehouseId: WH_MAIN,
    posBinId: BIN_PICK01,
    posLines: [{ variantId: VAR_SCISSOR, qty: "5", price: "75", taxRate: "0" }],
    posDiscount: "0",
    balances: sampleBalances,
    variants,
  });
  assert.equal(res.validationError, null);
  assert.equal(res.subtotal, 375);
  assert.equal(res.netPayable, 375);
});

test("Checkout blocks unvalued stock in STORE-01 with clear explanation", () => {
  const avail = getAvailableStock(sampleBalances, VAR_SCISSOR, WH_STORE, BIN_STORE01);
  const valued = getValuedStock(sampleBalances, VAR_SCISSOR, WH_STORE, BIN_STORE01);
  assert.equal(avail, 3);
  assert.equal(valued, 0);

  const res = validatePosCheckout({
    posCustomerId: CUST_ID,
    posWarehouseId: WH_STORE,
    posBinId: BIN_STORE01,
    posLines: [{ variantId: VAR_SCISSOR, qty: "1", price: "75", taxRate: "0" }],
    posDiscount: "0",
    balances: sampleBalances,
    variants,
  });
  assert.equal(
    res.validationError,
    "Selected bin contains unvalued stock for SCISSOR-EA-01 (Physical: 3 EA, Valued: 0 EA). Unvalued stock cannot be sold."
  );
});

test("Checkout blocks when requested quantity exceeds valued quantity", () => {
  const partialBalances: StockBalance[] = [
    {
      variantId: VAR_SCISSOR,
      warehouseId: WH_MAIN,
      binId: BIN_PICK01,
      stockStatus: "available",
      onHandQuantity: 10,
      unitOfMeasureId: UOM_EA,
      valuedQuantity: 5,
    },
  ];

  const resExceeded = validatePosCheckout({
    posCustomerId: CUST_ID,
    posWarehouseId: WH_MAIN,
    posBinId: BIN_PICK01,
    posLines: [{ variantId: VAR_SCISSOR, qty: "6", price: "75", taxRate: "0" }],
    posDiscount: "0",
    balances: partialBalances,
    variants,
  });
  assert.equal(
    resExceeded.validationError,
    "Insufficient valued stock for SCISSOR-EA-01 in selected bin (Valued: 5 EA, Physical: 10 EA, Requested: 6 EA)."
  );

  const resValid = validatePosCheckout({
    posCustomerId: CUST_ID,
    posWarehouseId: WH_MAIN,
    posBinId: BIN_PICK01,
    posLines: [{ variantId: VAR_SCISSOR, qty: "4", price: "75", taxRate: "0" }],
    posDiscount: "0",
    balances: partialBalances,
    variants,
  });
  assert.equal(resValid.validationError, null);
});
