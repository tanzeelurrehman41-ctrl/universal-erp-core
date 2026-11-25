import test from "node:test";
import assert from "node:assert/strict";
import {
  formatMoneyWithCurrency,
  formatReconciliationStatusLabel,
  resolveDashboardCurrencyState,
  type TrialBalanceRowSummary,
  type ValuationReportResponse,
} from "../src/dashboardCurrencyHelpers.ts";

const unresolvedProductionValuation: ValuationReportResponse = {
  totalPhysicalStock: 50,
  functionalCurrencyCode: "USD",
  functionalValuation: null,
  generalLedger1200FunctionalBalance: null,
  reconciliationVarianceFunctional: null,
  isReconciled: false,
  reconciliationStatus: "UNRESOLVED_CURRENCY_MISMATCH",
  isFunctionalValuationResolved: false,
  isGeneralLedgerCurrencyAligned: false,
  missingExchangeRateLayerCount: 3,
  unresolvedCurrencyIssue:
    "3 active FIFO layer(s) (PKR 2,250.00) lack a historically applicable exchange rate to company functional currency USD.",
  sourceValuationByCurrency: [{ currencyCode: "PKR", amount: 2250, itemCount: 3, quantity: 50 }],
  generalLedger1200BalancesByCurrency: [{ currencyCode: "PKR", amount: 2250, itemCount: 6 }],
  totalValuation: null,
  generalLedger1200Balance: null,
  reconciliationVariance: null,
  totalValuationPkr: null,
  generalLedger1200BalancePkr: null,
  reconciliationVariancePkr: null,
};

const rawTrialBalanceWith2250: TrialBalanceRowSummary[] = [
  {
    accountId: "acc-1200",
    accountCode: "1200",
    accountName: "Merchandise Inventory Asset",
    accountType: "asset",
    normalBalance: "debit",
    totalDebit: 2520,
    totalCredit: 270,
    netBalance: 2250,
  },
];

test("unresolved USD/PKR mismatch never renders PKR 2,250 as USD and exposes exact status strings", () => {
  const state = resolveDashboardCurrencyState("USD", unresolvedProductionValuation, rawTrialBalanceWith2250);

  assert.equal(state.companyBaseCurrency, "USD");
  assert.equal(state.totalPhysicalStock, 50);
  assert.equal(state.functionalValuation, null);
  assert.equal(state.functionalGl1200Balance, null);
  assert.equal(state.functionalVariance, null);
  assert.equal(state.isCurrencyUnresolved, true);
  assert.equal(state.isGlCurrencyAligned, false);
  assert.equal(state.isValuationVerified, false);
  assert.equal(state.sourceLedgerBalancesText, "PKR 2,250");
  assert.equal(state.sourceValuationBalancesText, "PKR 2,250");
  assert.equal(state.reconciliationLabel, "Unresolved currency mismatch");
  assert.equal(formatMoneyWithCurrency(state.functionalValuation, state.companyBaseCurrency), "Unavailable");
  assert.equal(formatMoneyWithCurrency(state.functionalGl1200Balance, state.companyBaseCurrency), "Unavailable");
});

test("reconciled PKR local staging baseline preserves 90 EA / PKR 4,075.00 display", () => {
  const localValuation: ValuationReportResponse = {
    totalPhysicalStock: 90,
    totalValuationPkr: 4075,
    generalLedger1200BalancePkr: 4075,
    reconciliationVariancePkr: 0,
    isReconciled: true,
  };
  const localTb: TrialBalanceRowSummary[] = [
    {
      accountId: "acc-1200",
      accountCode: "1200",
      accountName: "Merchandise Inventory Asset",
      accountType: "asset",
      normalBalance: "debit",
      totalDebit: 4075,
      totalCredit: 0,
      netBalance: 4075,
    },
  ];

  const state = resolveDashboardCurrencyState("PKR", localValuation, localTb);

  assert.equal(state.companyBaseCurrency, "PKR");
  assert.equal(state.totalPhysicalStock, 90);
  assert.equal(state.functionalValuation, 4075);
  assert.equal(state.functionalGl1200Balance, 4075);
  assert.equal(state.functionalVariance, 0);
  assert.equal(state.isBalanced, true);
  assert.equal(state.isCurrencyUnresolved, false);
  assert.equal(formatMoneyWithCurrency(state.functionalValuation, state.companyBaseCurrency), "PKR 4,075.00");
  assert.equal(formatMoneyWithCurrency(state.functionalGl1200Balance, state.companyBaseCurrency), "PKR 4,075.00");
});

test("verified USD historical FX valuation displays USD amounts cleanly when aligned", () => {
  const convertedUsdReport: ValuationReportResponse = {
    totalPhysicalStock: 50,
    functionalCurrencyCode: "USD",
    functionalValuation: 8.15,
    generalLedger1200FunctionalBalance: 8.15,
    reconciliationVarianceFunctional: 0,
    isReconciled: true,
    reconciliationStatus: "RECONCILED",
    isFunctionalValuationResolved: true,
    isGeneralLedgerCurrencyAligned: true,
    missingExchangeRateLayerCount: 0,
    sourceValuationByCurrency: [{ currencyCode: "PKR", amount: 2250 }],
    generalLedger1200BalancesByCurrency: [{ currencyCode: "USD", amount: 8.15 }],
    totalValuation: 8.15,
    generalLedger1200Balance: 8.15,
    reconciliationVariance: 0,
    totalValuationPkr: null,
    generalLedger1200BalancePkr: null,
    reconciliationVariancePkr: null,
  };

  const state = resolveDashboardCurrencyState("USD", convertedUsdReport, rawTrialBalanceWith2250);

  assert.equal(state.isCurrencyUnresolved, false);
  assert.equal(state.isBalanced, true);
  assert.equal(formatMoneyWithCurrency(state.functionalValuation, state.companyBaseCurrency), "USD 8.15");
  assert.equal(formatMoneyWithCurrency(state.functionalGl1200Balance, state.companyBaseCurrency), "USD 8.15");
});

test("unavailable valuation endpoint prevents raw /finance/trial-balance 2250 from rendering as USD 2,250", () => {
  const state = resolveDashboardCurrencyState("USD", null, rawTrialBalanceWith2250);

  assert.equal(state.functionalValuation, null);
  assert.equal(state.functionalGl1200Balance, null);
  assert.equal(state.functionalVariance, null);
  assert.equal(state.isGlCurrencyAligned, false);
  assert.equal(formatReconciliationStatusLabel(state.reconciliationCode), "Unavailable");
  assert.equal(formatMoneyWithCurrency(state.functionalGl1200Balance, state.companyBaseCurrency), "Unavailable");
});

test("legacy PKR-suffixed payload fails closed when workspace baseCurrency is USD", () => {
  const legacyPayload: ValuationReportResponse = {
    totalPhysicalStock: 50,
    totalValuationPkr: 2250,
    generalLedger1200BalancePkr: 2250,
    reconciliationVariancePkr: 0,
    isReconciled: true,
  };

  const state = resolveDashboardCurrencyState("USD", legacyPayload, rawTrialBalanceWith2250);

  assert.equal(state.isCurrencyUnresolved, true);
  assert.equal(state.functionalValuation, null);
  assert.equal(state.functionalGl1200Balance, null);
  assert.equal(state.sourceLedgerBalancesText, "PKR 2,250");
  assert.equal(state.reconciliationLabel, "Unresolved currency mismatch");
});
