import test from "node:test";
import assert from "node:assert/strict";
import {
  calculateAtp,
  validateReservation,
  validateCountLine,
  calculateCountVariance,
} from "../src/reservationHelpers.ts";

test("calculateAtp computes standard stock correctly", () => {
  const result = calculateAtp({
    physicalOnHand: 50,
    activeReserved: 10,
    valuedStock: 50,
  });
  assert.equal(result.atp, 40);
  assert.equal(result.sellableAtp, 40);
});

test("calculateAtp caps sellableAtp when stock is partially unvalued", () => {
  // Physical: 50, Reserved: 10 => ATP: 40
  // But only 30 units are valued => Sellable ATP must be 30
  const result = calculateAtp({
    physicalOnHand: 50,
    activeReserved: 10,
    valuedStock: 30,
  });
  assert.equal(result.atp, 40);
  assert.equal(result.sellableAtp, 30);
});

test("calculateAtp returns 0 when reservations exceed physical stock", () => {
  const result = calculateAtp({
    physicalOnHand: 20,
    activeReserved: 25,
    valuedStock: 20,
  });
  assert.equal(result.atp, 0);
  assert.equal(result.sellableAtp, 0);
});

test("validateReservation returns error when requested quantity exceeds ATP", () => {
  const check = validateReservation({
    variantId: "var-1",
    warehouseId: "wh-1",
    quantity: "25",
    atp: 20,
    enabled: true,
    isSaving: false,
  });
  assert.equal(check.isValid, false);
  assert.match(check.validationError ?? "", /exceeds Available-To-Promise/);
});

test("validateReservation succeeds when quantity is within ATP", () => {
  const check = validateReservation({
    variantId: "var-1",
    warehouseId: "wh-1",
    quantity: "15",
    atp: 20,
    enabled: true,
    isSaving: false,
  });
  assert.equal(check.isValid, true);
  assert.equal(check.validationError, null);
});

test("validateCountLine checks non-negative numbers and computes variance", () => {
  const invalid = validateCountLine({ snapshotQty: 10, countedQtyStr: "-5" });
  assert.equal(invalid.isValid, false);

  const gain = validateCountLine({ snapshotQty: 10, countedQtyStr: "12" });
  assert.equal(gain.isValid, true);
  assert.equal(gain.varianceQty, 2);

  const loss = validateCountLine({ snapshotQty: 10, countedQtyStr: "8" });
  assert.equal(loss.isValid, true);
  assert.equal(loss.varianceQty, -2);
});

test("calculateCountVariance calculates quantity and financial variance", () => {
  const gain = calculateCountVariance(10, 15, 45);
  assert.equal(gain.varianceQty, 5);
  assert.equal(gain.varianceCost, 225);

  const loss = calculateCountVariance(20, 18, 45);
  assert.equal(loss.varianceQty, -2);
  assert.equal(loss.varianceCost, -90);
});
