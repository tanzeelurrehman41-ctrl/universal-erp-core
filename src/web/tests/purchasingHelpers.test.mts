import test from "node:test";
import assert from "node:assert/strict";
import {
  evaluateThreeWayMatch,
  calculateSupplierBalance,
  validateInspectionQuantities,
  type MatchingTolerancePolicy,
  type MatchLineInput,
} from "../src/purchasingHelpers.ts";

test("evaluateThreeWayMatch handles exact match", () => {
  const policy: MatchingTolerancePolicy = {
    priceTolerancePercent: 2.0,
    quantityTolerancePercent: 0.0,
    allowPriceVariance: true,
    allowQuantityVariance: false,
  };

  const lines: MatchLineInput[] = [
    {
      lineNumber: 1,
      poOrderedQty: 10,
      poUnitPrice: 100,
      grnReceivedQty: 10,
      grnUnitPrice: 100,
      invoiceQty: 10,
      invoiceUnitPrice: 100,
    },
  ];

  const result = evaluateThreeWayMatch(lines, policy);
  assert.equal(result.isMatched, true);
  assert.equal(result.overallStatus, "matched");
  assert.equal(result.totalPriceVariance, 0);
  assert.equal(result.totalQuantityVariance, 0);
  assert.equal(result.lines[0].lineStatus, "matched");
});

test("evaluateThreeWayMatch accepts price variance within tolerance", () => {
  const policy: MatchingTolerancePolicy = {
    priceTolerancePercent: 5.0,
    quantityTolerancePercent: 0.0,
    allowPriceVariance: true,
    allowQuantityVariance: false,
  };

  const lines: MatchLineInput[] = [
    {
      lineNumber: 1,
      poOrderedQty: 10,
      poUnitPrice: 100,
      grnReceivedQty: 10,
      grnUnitPrice: 100,
      invoiceQty: 10,
      invoiceUnitPrice: 103, // 3% variance <= 5% tolerance
    },
  ];

  const result = evaluateThreeWayMatch(lines, policy);
  assert.equal(result.isMatched, true);
  assert.equal(result.overallStatus, "matched");
  assert.equal(result.totalPriceVariance, 30);
  assert.equal(result.lines[0].isPriceWithinTolerance, true);
  assert.equal(result.lines[0].lineStatus, "price_variance");
});

test("evaluateThreeWayMatch flags variance exceeded when exceeding tolerance", () => {
  const policy: MatchingTolerancePolicy = {
    priceTolerancePercent: 2.0,
    quantityTolerancePercent: 0.0,
    allowPriceVariance: true,
    allowQuantityVariance: false,
  };

  const lines: MatchLineInput[] = [
    {
      lineNumber: 1,
      poOrderedQty: 10,
      poUnitPrice: 100,
      grnReceivedQty: 10,
      grnUnitPrice: 100,
      invoiceQty: 10,
      invoiceUnitPrice: 110, // 10% variance > 2% tolerance
    },
  ];

  const result = evaluateThreeWayMatch(lines, policy);
  assert.equal(result.isMatched, false);
  assert.equal(result.overallStatus, "variance_exceeded");
  assert.equal(result.lines[0].isPriceWithinTolerance, false);
  assert.equal(result.lines[0].lineStatus, "variance_exceeded");
});

test("calculateSupplierBalance computes net payable, outstanding and status", () => {
  // Case 1: Unpaid with debit note
  const b1 = calculateSupplierBalance(1000, 200, 0);
  assert.equal(b1.netPayable, 800);
  assert.equal(b1.outstandingBalance, 800);
  assert.equal(b1.paymentStatus, "unpaid");

  // Case 2: Partially paid
  const b2 = calculateSupplierBalance(1000, 200, 500);
  assert.equal(b2.netPayable, 800);
  assert.equal(b2.outstandingBalance, 300);
  assert.equal(b2.paymentStatus, "partially_paid");

  // Case 3: Fully paid
  const b3 = calculateSupplierBalance(1000, 200, 800);
  assert.equal(b3.netPayable, 800);
  assert.equal(b3.outstandingBalance, 0);
  assert.equal(b3.paymentStatus, "paid");
});

test("validateInspectionQuantities enforces sum and non-negative values", () => {
  const valid = validateInspectionQuantities(10, 8, 2, 0);
  assert.equal(valid.isValid, true);

  const invalidSum = validateInspectionQuantities(10, 8, 1, 0);
  assert.equal(invalidSum.isValid, false);

  const negative = validateInspectionQuantities(10, -1, 11, 0);
  assert.equal(negative.isValid, false);
});
