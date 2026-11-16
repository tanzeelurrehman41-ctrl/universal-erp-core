import { test } from "node:test";
import assert from "node:assert/strict";
import {
  calculateProrationCredit,
  calculateNetUpgradePayable,
  calculateJurisdictionalTax,
  evaluateQuotaStatus,
  getTenantStatusBadge,
  getSubscriptionStatusBadge,
  getPaymentStatusBadge,
  calculateInvoiceTotals,
  formatCurrencyPkr
} from "../src/saasHelpers.ts";

test("calculateProrationCredit calculates unconsumed plan credit accurately", () => {
  // 15 days unconsumed out of 30 on a PKR 30,000 plan
  const creditHalf = calculateProrationCredit(30000, 30, 15);
  assert.equal(creditHalf, 15000);

  // 10 days unconsumed out of 30 on a PKR 45,000 plan
  const creditThird = calculateProrationCredit(45000, 30, 10);
  assert.equal(creditThird, 15000);

  // Zero days remaining yields 0 credit
  assert.equal(calculateProrationCredit(45000, 30, 0), 0);
  assert.equal(calculateProrationCredit(45000, 0, 10), 0);
  assert.equal(calculateProrationCredit(0, 30, 15), 0);
});

test("calculateNetUpgradePayable calculates net payable and applies billing credits", () => {
  // Upgrade to Pro (PKR 45,000) with PKR 15,000 proration credit and PKR 5,000 billing credit
  const result = calculateNetUpgradePayable(45000, 15000, 5000);
  assert.equal(result.netPayable, 25000);
  assert.equal(result.creditsApplied, 5000);
  assert.equal(result.excessCreditRemaining, 0);

  // Excess credits case
  const excess = calculateNetUpgradePayable(20000, 10000, 15000);
  assert.equal(excess.netPayable, 0);
  assert.equal(excess.creditsApplied, 10000);
  assert.equal(excess.excessCreditRemaining, 5000);
});

test("calculateJurisdictionalTax resolves Pakistani provincial and ICT sales tax rates", () => {
  // Federal / ICT - 15%
  const fed = calculateJurisdictionalTax(100000, "FEDERAL");
  assert.equal(fed.ratePct, 15.0);
  assert.equal(fed.taxAmount, 15000.0);
  assert.equal(fed.totalAmount, 115000.0);
  assert.match(fed.taxAuthority, /ICT/);

  // Punjab (PRA) - 16%
  const punjab = calculateJurisdictionalTax(50000, "PUNJAB");
  assert.equal(punjab.ratePct, 16.0);
  assert.equal(punjab.taxAmount, 8000.0);
  assert.equal(punjab.totalAmount, 58000.0);
  assert.match(punjab.taxAuthority, /PRA/);

  // Sindh (SRB) - 13%
  const sindh = calculateJurisdictionalTax(100000, "SINDH");
  assert.equal(sindh.ratePct, 13.0);
  assert.equal(sindh.taxAmount, 13000.0);
  assert.equal(sindh.totalAmount, 113000.0);
  assert.match(sindh.taxAuthority, /SRB/);

  // Khyber Pakhtunkhwa (KPRA) - 15%
  const kp = calculateJurisdictionalTax(40000, "KP");
  assert.equal(kp.ratePct, 15.0);
  assert.equal(kp.taxAmount, 6000.0);
  assert.equal(kp.totalAmount, 46000.0);
  assert.match(kp.taxAuthority, /KPRA/);

  // Balochistan (BRA) - 15%
  const balochistan = calculateJurisdictionalTax(20000, "BALOCHISTAN");
  assert.equal(balochistan.ratePct, 15.0);
  assert.equal(balochistan.taxAmount, 3000.0);
  assert.equal(balochistan.totalAmount, 23000.0);
  assert.match(balochistan.taxAuthority, /BRA/);
});

test("evaluateQuotaStatus evaluates limits, 80% soft warning and hard quota blocks", () => {
  // Unlimited feature
  const unlimited = evaluateQuotaStatus(5000, null);
  assert.equal(unlimited.statusColor, "neutral");
  assert.equal(unlimited.displayStatus, "Unlimited");
  assert.equal(unlimited.isExceeded, false);

  // Under threshold (50 / 100 = 50%)
  const safe = evaluateQuotaStatus(50, 100);
  assert.equal(safe.statusColor, "success");
  assert.equal(safe.pct, 50.0);
  assert.equal(safe.isWarning, false);
  assert.equal(safe.isExceeded, false);

  // Soft warning threshold (85 / 100 = 85%)
  const warning = evaluateQuotaStatus(85, 100);
  assert.equal(warning.statusColor, "warning");
  assert.equal(warning.pct, 85.0);
  assert.equal(warning.isWarning, true);
  assert.equal(warning.isExceeded, false);

  // Hard limit exceeded (105 / 100 = 105%)
  const exceeded = evaluateQuotaStatus(105, 100);
  assert.equal(exceeded.statusColor, "danger");
  assert.equal(exceeded.pct, 105.0);
  assert.equal(exceeded.isWarning, false);
  assert.equal(exceeded.isExceeded, true);
});

test("getTenantStatusBadge returns proper badge styling for all lifecycle states", () => {
  assert.equal(getTenantStatusBadge("active").badgeClass, "badge-success");
  assert.equal(getTenantStatusBadge("provisioning").badgeClass, "badge-info");
  assert.equal(getTenantStatusBadge("pending").badgeClass, "badge-warning");
  assert.equal(getTenantStatusBadge("suspended").badgeClass, "badge-danger");
  assert.equal(getTenantStatusBadge("cancelled").badgeClass, "badge-secondary");
  assert.equal(getTenantStatusBadge("closed").badgeClass, "badge-dark");
  assert.equal(getTenantStatusBadge("archived").badgeClass, "badge-slate");
});

test("getSubscriptionStatusBadge returns proper badge styling for subscription states", () => {
  assert.equal(getSubscriptionStatusBadge("active").badgeClass, "badge-success");
  assert.equal(getSubscriptionStatusBadge("trial").badgeClass, "badge-info");
  assert.equal(getSubscriptionStatusBadge("past_due").badgeClass, "badge-danger");
  assert.equal(getSubscriptionStatusBadge("grace_period").badgeClass, "badge-warning");
  assert.equal(getSubscriptionStatusBadge("suspended").badgeClass, "badge-danger");
});

test("getPaymentStatusBadge returns proper badge styling for payment statuses", () => {
  assert.equal(getPaymentStatusBadge("succeeded").badgeClass, "badge-success");
  assert.equal(getPaymentStatusBadge("pending").badgeClass, "badge-warning");
  assert.equal(getPaymentStatusBadge("failed").badgeClass, "badge-danger");
  assert.equal(getPaymentStatusBadge("refunded").badgeClass, "badge-purple");
});

test("calculateInvoiceTotals computes subtotal, discounts, credits, and net payable", () => {
  const lines = [
    { lineType: "base_plan", amount: 45000 },
    { lineType: "proration", amount: -15000 },
    { lineType: "overage", amount: 2000 },
    { lineType: "discount", amount: 2000 },
    { lineType: "credit", amount: 3000 },
    { lineType: "tax", amount: 4320 }
  ];

  const totals = calculateInvoiceTotals(lines);
  // subtotal: 45000 - 15000 + 2000 = 32000
  assert.equal(totals.subtotal, 32000);
  assert.equal(totals.discount, 2000);
  assert.equal(totals.credit, 3000);
  assert.equal(totals.tax, 4320);
  // netPayable: 32000 - 2000 - 3000 + 4320 = 31320
  assert.equal(totals.netPayable, 31320);
});

test("formatCurrencyPkr formats currency amounts with PKR identifier", () => {
  const formatted = formatCurrencyPkr(45000.5);
  assert.match(formatted, /PKR/);
  assert.match(formatted, /45,000\.50/);
});
