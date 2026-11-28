import test from "node:test";
import assert from "node:assert/strict";
import {
  calculateStraightLineDepreciation,
  calculateReducingBalanceDepreciation,
  calculateReconciliationDiscrepancy,
  computeFinancialStatementSummary,
  formatCurrencyPkr,
  type TrialBalanceItem,
} from "../src/financeHelpers.ts";

test("calculateStraightLineDepreciation calculates monthly depreciation correctly", () => {
  // 120,000 cost, 0 salvage, 60 months = 2,000/mo
  const monthly = calculateStraightLineDepreciation(120000, 0, 60, 0);
  assert.equal(monthly, 2000);

  // With salvage 20,000 -> 100,000 base / 50 months = 2,000/mo
  const withSalvage = calculateStraightLineDepreciation(120000, 20000, 50, 0);
  assert.equal(withSalvage, 2000);

  // Near end of life, capped at remaining
  const capped = calculateStraightLineDepreciation(100000, 10000, 10, 89500);
  assert.equal(capped, 500);

  // Fully depreciated
  const fullyDep = calculateStraightLineDepreciation(100000, 10000, 10, 90000);
  assert.equal(fullyDep, 0);
});

test("calculateReducingBalanceDepreciation calculates based on declining book value", () => {
  // 100,000 cost, 24% annual = 2% monthly -> month 1 is 2,000
  const m1 = calculateReducingBalanceDepreciation(100000, 0, 0, 24);
  assert.equal(m1, 2000);

  // month 2 with 2,000 accumulated: book value is 98,000 * 2% = 1,960
  const m2 = calculateReducingBalanceDepreciation(100000, 0, 2000, 24);
  assert.equal(m2, 1960);
});

test("calculateReconciliationDiscrepancy detects balanced vs discrepancy", () => {
  const balanced = calculateReconciliationDiscrepancy(50000, 50000);
  assert.equal(balanced.isBalanced, true);
  assert.equal(balanced.discrepancy, 0);

  const diff = calculateReconciliationDiscrepancy(52500, 50000);
  assert.equal(diff.isBalanced, false);
  assert.equal(diff.discrepancy, 2500);
});

test("computeFinancialStatementSummary computes balance sheet equation and net income", () => {
  const trialBalance: TrialBalanceItem[] = [
    { accountType: "asset", accountCode: "1020", netBalance: 10000 },
    { accountType: "asset", accountCode: "1200", netBalance: 5000 },
    { accountType: "liability", accountCode: "2010", netBalance: 4000 },
    { accountType: "equity", accountCode: "3010", netBalance: 6000 },
    { accountType: "revenue", accountCode: "4010", netBalance: 8000 },
    { accountType: "expense", accountCode: "5010", netBalance: 2000 },
    { accountType: "expense", accountCode: "6010", netBalance: 1000 },
  ];

  const summary = computeFinancialStatementSummary(trialBalance);

  assert.equal(summary.totalAssets, 15000);
  assert.equal(summary.totalLiabilities, 4000);
  assert.equal(summary.totalEquity, 6000);
  assert.equal(summary.totalRevenue, 8000);
  assert.equal(summary.totalExpenses, 3000);
  assert.equal(summary.netIncome, 5000);
  assert.equal(summary.isBalanced, true);
  assert.equal(summary.discrepancy, 0);
});

test("formatCurrencyPkr formats currency properly", () => {
  const formatted = formatCurrencyPkr(12345.67);
  assert.match(formatted, /PKR\s+12,345\.67/);
});
