export interface ValuationCurrencyTotal {
  currencyCode: string;
  amount: number;
  itemCount?: number;
  quantity?: number | null;
}

export interface ValuationReportResponse {
  totalPhysicalStock?: number | null;
  functionalCurrencyCode?: string | null;
  functionalValuation?: number | null;
  generalLedger1200FunctionalBalance?: number | null;
  reconciliationVarianceFunctional?: number | null;
  isReconciled?: boolean | null;
  reconciliationStatus?: string | null;
  isFunctionalValuationResolved?: boolean | null;
  isGeneralLedgerCurrencyAligned?: boolean | null;
  missingExchangeRateLayerCount?: number | null;
  unresolvedCurrencyIssue?: string | null;
  sourceValuationByCurrency?: ValuationCurrencyTotal[] | null;
  generalLedger1200BalancesByCurrency?: ValuationCurrencyTotal[] | null;
  totalValuation?: number | null;
  generalLedger1200Balance?: number | null;
  reconciliationVariance?: number | null;
  totalValuationPkr?: number | null;
  generalLedger1200BalancePkr?: number | null;
  reconciliationVariancePkr?: number | null;
}

export interface TrialBalanceRowSummary {
  accountId: string;
  accountCode: string;
  accountName: string;
  accountType: string;
  normalBalance: string;
  totalDebit: number;
  totalCredit: number;
  netBalance: number;
}

export interface ResolvedDashboardCurrencyState {
  companyBaseCurrency: string | null;
  totalPhysicalStock: number | null;
  functionalValuation: number | null;
  functionalGl1200Balance: number | null;
  functionalVariance: number | null;
  isBalanced: boolean;
  hasDiscrepancy: boolean;
  isCurrencyUnresolved: boolean;
  isValuationVerified: boolean;
  isGlCurrencyAligned: boolean;
  reconciliationCode: string | null;
  reconciliationLabel: string;
  sourceLedgerBalancesText: string | null;
  sourceValuationBalancesText: string | null;
  unresolvedCurrencyIssue: string | null;
  trialBalanceBalanced: boolean | null;
}

export function formatMoneyWithCurrency(
  val: number | null | undefined,
  currencyCode: string | null | undefined,
  decimals: number = 2
): string {
  if (val === null || val === undefined || Number.isNaN(val)) {
    return "Unavailable";
  }

  const normalizedCurrency = currencyCode?.trim().toUpperCase() || null;
  const formatted = val.toLocaleString("en-US", {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });
  return normalizedCurrency ? `${normalizedCurrency} ${formatted}` : formatted;
}

export function formatSourceCurrencyTotals(
  totals: ValuationCurrencyTotal[] | null | undefined
): string | null {
  if (!Array.isArray(totals) || totals.length === 0) {
    return null;
  }

  const formattedParts = totals
    .filter((t) => typeof t?.currencyCode === "string" && typeof t?.amount === "number" && !Number.isNaN(t.amount))
    .map((t) => {
      const decimals = Number.isInteger(t.amount) ? 0 : 2;
      return formatMoneyWithCurrency(t.amount, t.currencyCode, decimals);
    });

  return formattedParts.length > 0 ? formattedParts.join(", ") : null;
}

export function formatReconciliationStatusLabel(
  statusCode: string | null | undefined
): string {
  switch (statusCode) {
    case "UNRESOLVED_CURRENCY_MISMATCH":
      return "Unresolved currency mismatch";
    case "MISSING_HISTORICAL_EXCHANGE_RATE":
      return "Missing historical exchange rate";
    case "GL_CURRENCY_MISMATCH":
      return "GL currency mismatch";
    case "RECONCILED":
      return "Reconciled";
    case "VARIANCE_DETECTED":
      return "Variance detected";
    default:
      return "Unavailable";
  }
}

function toFiniteNumberOrNull(value: unknown): number | null {
  if (value === null || value === undefined || value === "") {
    return null;
  }
  const num = Number(value);
  return Number.isFinite(num) ? num : null;
}

/**
 * Resolves functional-currency Executive Dashboard KPIs without ever relabeling
 * unconverted source-currency (e.g. PKR) amounts as the company's base currency (e.g. USD).
 */
export function resolveDashboardCurrencyState(
  workspaceBaseCurrency: string | null | undefined,
  valData: ValuationReportResponse | null,
  trialBalanceRows: TrialBalanceRowSummary[] | null
): ResolvedDashboardCurrencyState {
  const normalizedWorkspaceCurrency = workspaceBaseCurrency?.trim().toUpperCase() || null;
  const reportCurrency = valData?.functionalCurrencyCode?.trim().toUpperCase() || null;
  const effectiveBaseCurrency = reportCurrency || normalizedWorkspaceCurrency;

  const totalPhysicalStock = toFiniteNumberOrNull(valData?.totalPhysicalStock);

  // Check trial balance mathematical balance only (not GL 1200 currency safety)
  let directGl1200: number | null = null;
  let trialBalanceBalanced: boolean | null = null;
  if (Array.isArray(trialBalanceRows) && trialBalanceRows.length > 0) {
    const row1200 = trialBalanceRows.find(
      (r) => r.accountCode === "1200" || (typeof r.accountCode === "string" && r.accountCode.startsWith("1200"))
    );
    if (row1200) {
      directGl1200 = toFiniteNumberOrNull(
        row1200.netBalance ?? (Number(row1200.totalDebit ?? 0) - Number(row1200.totalCredit ?? 0))
      );
    }
    const totalDebits = trialBalanceRows.reduce((acc, r) => acc + (Number(r.totalDebit) || 0), 0);
    const totalCredits = trialBalanceRows.reduce((acc, r) => acc + (Number(r.totalCredit) || 0), 0);
    trialBalanceBalanced = Math.abs(totalDebits - totalCredits) < 0.01;
  }

  // Case 1: Currency-safe Option A2 valuation contract (explicit functionalCurrencyCode / status)
  const hasCurrencySafeContract = Boolean(
    valData &&
      (valData.functionalCurrencyCode !== undefined ||
        valData.reconciliationStatus !== undefined ||
        valData.isGeneralLedgerCurrencyAligned !== undefined ||
        valData.isFunctionalValuationResolved !== undefined)
  );

  if (valData && hasCurrencySafeContract) {
    const isValuationVerified =
      valData.isFunctionalValuationResolved === true &&
      toFiniteNumberOrNull(valData.functionalValuation ?? valData.totalValuation) !== null;
    const isGlCurrencyAligned =
      valData.isGeneralLedgerCurrencyAligned === true &&
      toFiniteNumberOrNull(valData.generalLedger1200FunctionalBalance ?? valData.generalLedger1200Balance) !== null;

    const functionalValuation = isValuationVerified
      ? toFiniteNumberOrNull(valData.functionalValuation ?? valData.totalValuation)
      : null;
    const functionalGl1200Balance = isGlCurrencyAligned
      ? toFiniteNumberOrNull(valData.generalLedger1200FunctionalBalance ?? valData.generalLedger1200Balance)
      : null;

    const reportVariance =
      isValuationVerified && isGlCurrencyAligned
        ? toFiniteNumberOrNull(valData.reconciliationVarianceFunctional ?? valData.reconciliationVariance)
        : null;
    const functionalVariance =
      reportVariance !== null
        ? Math.abs(reportVariance)
        : functionalValuation !== null && functionalGl1200Balance !== null
        ? Math.abs(functionalValuation - functionalGl1200Balance)
        : null;

    const isCurrencyUnresolved = !isValuationVerified || !isGlCurrencyAligned;
    const reconciliationCode =
      valData.reconciliationStatus ||
      (isCurrencyUnresolved
        ? "UNRESOLVED_CURRENCY_MISMATCH"
        : functionalVariance === 0
        ? "RECONCILED"
        : "VARIANCE_DETECTED");

    const sourceLedgerBalancesText = formatSourceCurrencyTotals(valData.generalLedger1200BalancesByCurrency);
    const sourceValuationBalancesText = formatSourceCurrencyTotals(valData.sourceValuationByCurrency);

    const isBalanced = !isCurrencyUnresolved && functionalVariance !== null && functionalVariance === 0;
    const hasDiscrepancy = !isCurrencyUnresolved && functionalVariance !== null && functionalVariance > 0;

    return {
      companyBaseCurrency: effectiveBaseCurrency,
      totalPhysicalStock,
      functionalValuation,
      functionalGl1200Balance,
      functionalVariance,
      isBalanced,
      hasDiscrepancy,
      isCurrencyUnresolved,
      isValuationVerified,
      isGlCurrencyAligned,
      reconciliationCode,
      reconciliationLabel: formatReconciliationStatusLabel(reconciliationCode),
      sourceLedgerBalancesText,
      sourceValuationBalancesText,
      unresolvedCurrencyIssue: valData.unresolvedCurrencyIssue ?? null,
      trialBalanceBalanced,
    };
  }

  // Case 2: Legacy valuation payload without explicit functionalCurrencyCode (e.g. local PKR staging)
  if (valData) {
    const hasLegacyPkrValues =
      toFiniteNumberOrNull(valData.totalValuationPkr) !== null ||
      toFiniteNumberOrNull(valData.generalLedger1200BalancePkr) !== null;

    if (hasLegacyPkrValues && effectiveBaseCurrency !== "PKR") {
      // Fail closed: legacy PKR-suffixed payload must NEVER be labeled as USD or any non-PKR base currency!
      const rawPkrVal = toFiniteNumberOrNull(valData.totalValuationPkr);
      const rawPkrGl = toFiniteNumberOrNull(valData.generalLedger1200BalancePkr) ?? directGl1200;
      return {
        companyBaseCurrency: effectiveBaseCurrency,
        totalPhysicalStock,
        functionalValuation: null,
        functionalGl1200Balance: null,
        functionalVariance: null,
        isBalanced: false,
        hasDiscrepancy: false,
        isCurrencyUnresolved: true,
        isValuationVerified: false,
        isGlCurrencyAligned: false,
        reconciliationCode: "UNRESOLVED_CURRENCY_MISMATCH",
        reconciliationLabel: "Unresolved currency mismatch",
        sourceLedgerBalancesText: rawPkrGl !== null ? formatMoneyWithCurrency(rawPkrGl, "PKR", Number.isInteger(rawPkrGl) ? 0 : 2) : null,
        sourceValuationBalancesText: rawPkrVal !== null ? formatMoneyWithCurrency(rawPkrVal, "PKR", Number.isInteger(rawPkrVal) ? 0 : 2) : null,
        unresolvedCurrencyIssue: `Legacy valuation response is PKR-denominated while company base currency is ${effectiveBaseCurrency || "Unspecified"}.`,
        trialBalanceBalanced,
      };
    }

    const legacyVal = toFiniteNumberOrNull(valData.totalValuationPkr ?? valData.totalValuation);
    const legacyGl = toFiniteNumberOrNull(valData.generalLedger1200BalancePkr ?? valData.generalLedger1200Balance);
    const effectiveGl = directGl1200 !== null ? directGl1200 : legacyGl;
    const variance = legacyVal !== null && effectiveGl !== null ? Math.abs(legacyVal - effectiveGl) : null;
    const isBalanced = variance !== null && variance === 0;
    const hasDiscrepancy = variance !== null && variance > 0;
    const statusCode = isBalanced ? "RECONCILED" : hasDiscrepancy ? "VARIANCE_DETECTED" : null;

    return {
      companyBaseCurrency: effectiveBaseCurrency,
      totalPhysicalStock,
      functionalValuation: legacyVal,
      functionalGl1200Balance: effectiveGl,
      functionalVariance: variance,
      isBalanced,
      hasDiscrepancy,
      isCurrencyUnresolved: false,
      isValuationVerified: legacyVal !== null,
      isGlCurrencyAligned: effectiveGl !== null,
      reconciliationCode: statusCode,
      reconciliationLabel: formatReconciliationStatusLabel(statusCode),
      sourceLedgerBalancesText:
        effectiveGl !== null && effectiveBaseCurrency
          ? formatMoneyWithCurrency(effectiveGl, effectiveBaseCurrency, Number.isInteger(effectiveGl) ? 0 : 2)
          : null,
      sourceValuationBalancesText:
        legacyVal !== null && effectiveBaseCurrency
          ? formatMoneyWithCurrency(legacyVal, effectiveBaseCurrency, Number.isInteger(legacyVal) ? 0 : 2)
          : null,
      unresolvedCurrencyIssue: null,
      trialBalanceBalanced,
    };
  }

  // Case 3: Valuation endpoint unavailable (401/403/404/offline).
  // Never assume /api/v1/finance/trial-balance carries verified functional currency without valuation confirmation.
  return {
    companyBaseCurrency: effectiveBaseCurrency,
    totalPhysicalStock: null,
    functionalValuation: null,
    functionalGl1200Balance: null,
    functionalVariance: null,
    isBalanced: false,
    hasDiscrepancy: false,
    isCurrencyUnresolved: false,
    isValuationVerified: false,
    isGlCurrencyAligned: false,
    reconciliationCode: null,
    reconciliationLabel: "Unavailable",
    sourceLedgerBalancesText: null,
    sourceValuationBalancesText: null,
    unresolvedCurrencyIssue:
      directGl1200 !== null
        ? "GL 1200 currency alignment could not be verified without the valuation report response."
        : null,
    trialBalanceBalanced,
  };
}
