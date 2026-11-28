import test from "node:test";
import assert from "node:assert/strict";
import {
  evaluateCustomerCredit,
  calculateQuotationTotals,
  calculateArAgingBuckets,
  autoAllocateReceipt,
  validateDispatchQuantity,
  type OpenInvoiceAgingInput,
} from "../src/salesHelpers.ts";

test("evaluateCustomerCredit approves order when projected exposure is within credit limit", () => {
  const result = evaluateCustomerCredit({
    creditLimit: 50000,
    currentBalance: 15000,
    uninvoicedOrdersTotal: 5000,
    newOrderAmount: 10000,
  });

  assert.equal(result.isAllowed, true);
  assert.equal(result.requiresOverride, false);
  assert.equal(result.currentExposure, 20000);
  assert.equal(result.projectedExposure, 30000);
  assert.equal(result.availableCredit, 20000);
  assert.equal(result.excessAmount, 0);
  assert.match(result.message, /Credit check passed/);
});

test("evaluateCustomerCredit requires override when projected exposure exceeds credit limit", () => {
  const result = evaluateCustomerCredit({
    creditLimit: 25000,
    currentBalance: 15000,
    uninvoicedOrdersTotal: 5000,
    newOrderAmount: 10000,
  });

  assert.equal(result.isAllowed, false);
  assert.equal(result.requiresOverride, true);
  assert.equal(result.currentExposure, 20000);
  assert.equal(result.projectedExposure, 30000);
  assert.equal(result.excessAmount, 5000);
  assert.match(result.message, /CREDIT_LIMIT_EXCEEDED/);
});

test("evaluateCustomerCredit requires override for zero credit limit with credit order", () => {
  const result = evaluateCustomerCredit({
    creditLimit: 0,
    currentBalance: 0,
    uninvoicedOrdersTotal: 0,
    newOrderAmount: 5000,
  });

  assert.equal(result.isAllowed, false);
  assert.equal(result.requiresOverride, true);
  assert.match(result.message, /no credit limit facility configured/);
});

test("calculateQuotationTotals calculates subtotal, discounts, tax, and net totals accurately", () => {
  const lines = [
    { quantity: 5, unitPrice: 100, taxRate: 0.10, discountPercent: 0 },
    { quantity: 2, unitPrice: 200, taxRate: 0.05, discountPercent: 10 }, // Gross 400, disc 40, net 360, tax 18
  ];

  const result = calculateQuotationTotals(lines, 20); // 20 additional header discount

  // Gross = 500 + 400 = 900
  // Line discounts = 0 + 40 = 40
  // Total discounts = 40 + 20 = 60
  // Tax = 50 + 18 = 68
  // Net revenue = 900 - 60 = 840
  // Total = 840 + 68 = 908
  assert.equal(result.subtotal, 900);
  assert.equal(result.discountAmount, 60);
  assert.equal(result.netRevenue, 840);
  assert.equal(result.taxAmount, 68);
  assert.equal(result.totalAmount, 908);
});

test("calculateArAgingBuckets distributes open invoices across aging periods", () => {
  const asOf = "2026-11-15T00:00:00Z";
  const invoices: OpenInvoiceAgingInput[] = [
    // 10 days past due -> current
    { invoiceId: "1", invoiceNumber: "INV-01", invoiceDate: "2026-10-01", dueDate: "2026-11-05", totalAmount: 1000, paidAmount: 0 },
    // 40 days past due -> days 31-60
    { invoiceId: "2", invoiceNumber: "INV-02", invoiceDate: "2026-09-01", dueDate: "2026-10-06", totalAmount: 2500, paidAmount: 500 }, // 2000 outstanding
    // 75 days past due -> days 61-90
    { invoiceId: "3", invoiceNumber: "INV-03", invoiceDate: "2026-08-01", dueDate: "2026-09-01", totalAmount: 3000, paidAmount: 0 },
    // 120 days past due -> over 90 days
    { invoiceId: "4", invoiceNumber: "INV-04", invoiceDate: "2026-06-01", dueDate: "2026-07-18", totalAmount: 4000, paidAmount: 1000 }, // 3000 outstanding
  ];

  const result = calculateArAgingBuckets(invoices, 500, 1500, asOf);

  assert.equal(result.openInvoiceCount, 4);
  assert.equal(result.currentBalance, 1000);
  assert.equal(result.days31To60, 2000);
  assert.equal(result.days61To90, 3000);
  assert.equal(result.over90Days, 3000);
  assert.equal(result.totalReceivable, 9000);
  assert.equal(result.totalCreditNotes, 500);
  assert.equal(result.netOutstanding, 8500); // 9000 - 500
});

test("autoAllocateReceipt allocates cash in FIFO order across open invoices", () => {
  const invoices: OpenInvoiceAgingInput[] = [
    { invoiceId: "inv-2", invoiceNumber: "INV-02", invoiceDate: "2026-10-01", dueDate: "2026-10-20", totalAmount: 1500, paidAmount: 500 }, // 1000 outstanding
    { invoiceId: "inv-1", invoiceNumber: "INV-01", invoiceDate: "2026-09-01", dueDate: "2026-09-20", totalAmount: 800, paidAmount: 0 },    // 800 outstanding (older)
    { invoiceId: "inv-3", invoiceNumber: "INV-03", invoiceDate: "2026-11-01", dueDate: "2026-11-20", totalAmount: 2000, paidAmount: 0 },   // 2000 outstanding
  ];

  const result = autoAllocateReceipt(1200, invoices);

  // Should allocate 800 to inv-1 first (older), then 400 to inv-2, 0 to inv-3
  assert.equal(result.allocations.length, 2);
  assert.equal(result.allocations[0].invoiceId, "inv-1");
  assert.equal(result.allocations[0].allocatedAmount, 800);
  assert.equal(result.allocations[1].invoiceId, "inv-2");
  assert.equal(result.allocations[1].allocatedAmount, 400);
  assert.equal(result.unallocatedAmount, 0);
});

test("validateDispatchQuantity enforces remaining balance bounds", () => {
  // Ordered 10, already dispatched 4 -> remaining 6
  const valid = validateDispatchQuantity(10, 4, 5);
  assert.equal(valid.isValid, true);
  assert.equal(valid.remainingOrderQty, 6);

  const exceeded = validateDispatchQuantity(10, 4, 7);
  assert.equal(exceeded.isValid, false);
  assert.match(exceeded.error!, /exceeds remaining order balance/);

  const zeroQty = validateDispatchQuantity(10, 4, 0);
  assert.equal(zeroQty.isValid, false);
  assert.match(zeroQty.error!, /greater than zero/);
});
