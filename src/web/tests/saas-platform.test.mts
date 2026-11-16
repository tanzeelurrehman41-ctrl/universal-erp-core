import { test } from "node:test";
import assert from "node:assert/strict";
import {
  calculateProrationCredit,
  calculateNetUpgradePayable,
  calculateJurisdictionalTax,
  calculateCreditNote,
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

test("calculateJurisdictionalTax resolves Pakistani provincial standard rates and verified Sindh 15%", () => {
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

  // Sindh (SRB) Standard Rate - 15.0% (Verified under Sindh Finance Act 2024 / SSTS Act 2011 Sec 8(1))
  const sindh = calculateJurisdictionalTax(100000, "SINDH");
  assert.equal(sindh.ratePct, 15.0);
  assert.equal(sindh.taxAmount, 15000.0);
  assert.equal(sindh.totalAmount, 115000.0);
  assert.match(sindh.taxAuthority, /SRB/);
  assert.match(sindh.statutoryReference!, /Sindh Finance Act 2024/);

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

test("calculateJurisdictionalTax resolves conditional reduced rates when opted without input tax", () => {
  // Sindh SRB 3% reduced rate without input tax credit (SRB Notification No. SRB-3-4/7/2013)
  const srbReduced = calculateJurisdictionalTax(100000, "SINDH", { regime: "reduced_without_input_tax" });
  assert.equal(srbReduced.ratePct, 3.0);
  assert.equal(srbReduced.taxAmount, 3000.0);
  assert.equal(srbReduced.totalAmount, 103000.0);
  assert.equal(srbReduced.taxRegime, "reduced_without_input_tax");

  // Punjab PRA 5% reduced rate without input tax credit
  const praReduced = calculateJurisdictionalTax(100000, "PUNJAB", { regime: "reduced_without_input_tax" });
  assert.equal(praReduced.ratePct, 5.0);
  assert.equal(praReduced.taxAmount, 5000.0);

  // Federal ICT 5% reduced rate for certified IT services
  const ictReduced = calculateJurisdictionalTax(100000, "FEDERAL", { regime: "reduced_without_input_tax" });
  assert.equal(ictReduced.ratePct, 5.0);
  assert.equal(ictReduced.taxAmount, 5000.0);

  // KP KPRA 2% reduced rate
  const kpReduced = calculateJurisdictionalTax(100000, "KP", { regime: "reduced_without_input_tax" });
  assert.equal(kpReduced.ratePct, 2.0);
  assert.equal(kpReduced.taxAmount, 2000.0);

  // Balochistan BRA 3% reduced rate
  const braReduced = calculateJurisdictionalTax(100000, "BALOCHISTAN", { regime: "reduced_without_input_tax" });
  assert.equal(braReduced.ratePct, 3.0);
  assert.equal(braReduced.taxAmount, 3000.0);
});

test("calculateJurisdictionalTax resolves historical rate intervals and export zero-rating", () => {
  // Historical Sindh rate before 2024-07-01: 13.0%
  const historical = calculateJurisdictionalTax(100000, "SINDH", { effectiveDate: "2024-05-15" });
  assert.equal(historical.ratePct, 13.0);
  assert.equal(historical.taxAmount, 13000.0);
  assert.match(historical.statutoryReference!, /pre-2024 interval/);

  // Export of Services (Foreign customer / remittance in foreign exchange): 0.0%
  const exportTax = calculateJurisdictionalTax(100000, "SINDH", { isExport: true });
  assert.equal(exportTax.ratePct, 0.0);
  assert.equal(exportTax.taxAmount, 0.0);
  assert.equal(exportTax.totalAmount, 100000.0);
  assert.equal(exportTax.taxRegime, "export_zero_rated");
});

test("calculateJurisdictionalTax handles tax-inclusive pricing without rounding leakage", () => {
  // Total PKR 115,000 tax-inclusive on 15% Sindh standard rate
  // Base = 115,000 / 1.15 = 100,000
  // Tax = 15,000
  const inclusive = calculateJurisdictionalTax(115000, "SINDH", { isTaxInclusive: true });
  assert.equal(inclusive.isTaxInclusive, true);
  assert.equal(inclusive.taxableBase, 100000.0);
  assert.equal(inclusive.taxAmount, 15000.0);
  assert.equal(inclusive.totalAmount, 115000.0);

  // Uneven fraction: Total PKR 100,000 tax-inclusive on 15% rate
  // Base = round(100,000 / 1.15) = 86,956.52
  // Tax = 100,000 - 86,956.52 = 13,043.48
  // Total = exactly 100,000.00
  const uneven = calculateJurisdictionalTax(100000, "SINDH", { isTaxInclusive: true });
  assert.equal(uneven.taxableBase, 86956.52);
  assert.equal(uneven.taxAmount, 13043.48);
  assert.equal(uneven.totalAmount, 100000.0);
});

test("calculateCreditNote reverses subtotal and proportional sales tax", () => {
  // Tax-exclusive credit note for PKR 20,000 refund at 15% rate
  const creditEx = calculateCreditNote(20000, "SINDH");
  assert.equal(creditEx.refundSubtotal, 20000.0);
  assert.equal(creditEx.taxReversed, 3000.0);
  assert.equal(creditEx.totalCreditNote, 23000.0);

  // Tax-inclusive credit note for PKR 23,000 refund at 15% rate
  const creditInc = calculateCreditNote(23000, "SINDH", { isTaxInclusive: true });
  assert.equal(creditInc.refundSubtotal, 20000.0);
  assert.equal(creditInc.taxReversed, 3000.0);
  assert.equal(creditInc.totalCreditNote, 23000.0);
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
    { lineType: "tax", amount: 4500 }
  ];

  const totals = calculateInvoiceTotals(lines);
  // subtotal: 45000 - 15000 + 2000 = 32000
  assert.equal(totals.subtotal, 32000);
  assert.equal(totals.discount, 2000);
  assert.equal(totals.credit, 3000);
  assert.equal(totals.tax, 4500);
  // netPayable: 32000 - 2000 - 3000 + 4500 = 31500
  assert.equal(totals.netPayable, 31500);
});

test("formatCurrencyPkr formats currency amounts with PKR identifier", () => {
  const formatted = formatCurrencyPkr(45000.5);
  assert.match(formatted, /PKR/);
  assert.match(formatted, /45,000\.50/);
});
