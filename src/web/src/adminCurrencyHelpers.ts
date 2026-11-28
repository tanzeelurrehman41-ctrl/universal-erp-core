export type CurrencyOption = {
  code: string;
  name: string;
  minorUnit?: number;
  isActive?: boolean;
};

export type CompanyCurrencySettingsDto = {
  companyId: string;
  tenantId: string;
  companyCode: string;
  companyName: string;
  functionalCurrencyCode: string;
  presentationCurrencyCode: string;
  currencyEffectiveDate: string;
  hasPostedTransactions: boolean;
  updatedAt?: string | null;
  functionalCurrencyGuidance: string;
  presentationCurrencyGuidance: string;
};

export type CompanyCurrencyHistoryDto = {
  historyId: string;
  previousFunctionalCurrency: string;
  newFunctionalCurrency: string;
  previousPresentationCurrency: string;
  newPresentationCurrency: string;
  previousEffectiveDate: string;
  newEffectiveDate: string;
  confirmedFutureTransactions: boolean;
  changeReason?: string | null;
  changedByUserId: string;
  changedByEmail?: string | null;
  changedByName?: string | null;
  changedAt: string;
};

export type RateType = "transaction" | "average" | "closing";

export type ExchangeRateDto = {
  exchangeRateId: string;
  companyId?: string | null;
  fromCurrency: string;
  toCurrency: string;
  rate: number;
  effectiveDate: string;
  effectiveAt: string;
  rateType: RateType;
  source: string;
  reference?: string | null;
  createdByUserId?: string | null;
  createdByEmail?: string | null;
  createdByName?: string | null;
  createdAt: string;
  updatedByUserId?: string | null;
  updatedByEmail?: string | null;
  updatedAt?: string | null;
};

export type CurrencySettingsDraft = {
  functionalCurrencyCode: string;
  presentationCurrencyCode: string;
  effectiveDate: string;
  changeReason: string;
};

export type ExchangeRateDraft = {
  fromCurrency: string;
  toCurrency: string;
  rate: string;
  effectiveDate: string;
  rateType: RateType;
  source: string;
  reference: string;
};

export const FALLBACK_CURRENCIES: CurrencyOption[] = [
  { code: "PKR", name: "Pakistani Rupee", minorUnit: 2, isActive: true },
  { code: "USD", name: "US Dollar", minorUnit: 2, isActive: true },
  { code: "EUR", name: "Euro", minorUnit: 2, isActive: true },
  { code: "GBP", name: "British Pound Sterling", minorUnit: 2, isActive: true },
  { code: "AED", name: "UAE Dirham", minorUnit: 2, isActive: true },
  { code: "SAR", name: "Saudi Riyal", minorUnit: 2, isActive: true },
  { code: "CAD", name: "Canadian Dollar", minorUnit: 2, isActive: true },
  { code: "AUD", name: "Australian Dollar", minorUnit: 2, isActive: true },
  { code: "CNY", name: "Chinese Yuan", minorUnit: 2, isActive: true },
  { code: "JPY", name: "Japanese Yen", minorUnit: 0, isActive: true },
  { code: "SGD", name: "Singapore Dollar", minorUnit: 2, isActive: true },
  { code: "CHF", name: "Swiss Franc", minorUnit: 2, isActive: true },
];

export const FUNCTIONAL_CURRENCY_EXPLANATION =
  "Primary economic and accounting currency used for day-to-day ledgers, FIFO inventory layers, and default new journal postings. Updating it affects future transactions only and never rewrites historical entries.";

export const PRESENTATION_CURRENCY_EXPLANATION =
  "Financial reporting currency used to display consolidated reports under IAS 21 translation rules (closing rates for assets and liabilities, transaction-date or explicit average rates for income and expenses, and OCI translation differences).";

export function describeRateType(rateType: RateType | string): string {
  switch (rateType.toLowerCase()) {
    case "closing":
      return "Closing Rate (Assets & Liabilities at period/report date)";
    case "average":
      return "Period Average Rate (Optional P&L translation method)";
    case "transaction":
    default:
      return "Transaction Date Rate (Revenue, Expenses & Historical Equity)";
  }
}

export function normalizeCurrencyCode(code: string | null | undefined, fallback = ""): string {
  const cleaned = code?.trim().toUpperCase() ?? "";
  return cleaned || fallback;
}

export function resolveWorkspaceCurrencyDisplay(workspace?: {
  baseCurrencyCode?: string | null;
  functionalCurrencyCode?: string | null;
  presentationCurrencyCode?: string | null;
} | null): {
  functionalCurrency: string | null;
  presentationCurrency: string | null;
  badgeText: string;
} {
  if (!workspace) {
    return { functionalCurrency: null, presentationCurrency: null, badgeText: "" };
  }
  const functionalCurrency =
    normalizeCurrencyCode(workspace.functionalCurrencyCode) ||
    normalizeCurrencyCode(workspace.baseCurrencyCode) ||
    null;
  const presentationCurrency = normalizeCurrencyCode(workspace.presentationCurrencyCode) || null;

  if (functionalCurrency && presentationCurrency && functionalCurrency !== presentationCurrency) {
    return {
      functionalCurrency,
      presentationCurrency,
      badgeText: `Functional: ${functionalCurrency} · Reporting: ${presentationCurrency}`,
    };
  }
  if (functionalCurrency) {
    return {
      functionalCurrency,
      presentationCurrency: presentationCurrency ?? functionalCurrency,
      badgeText: `Currency: ${functionalCurrency}`,
    };
  }
  return { functionalCurrency: null, presentationCurrency, badgeText: "" };
}

export function validateCurrencySettingsDraft(
  draft: CurrencySettingsDraft,
  current: CompanyCurrencySettingsDto | null
): {
  valid: boolean;
  errors: string[];
  hasChanges: boolean;
  functionalChanged: boolean;
  requiresConfirmation: boolean;
} {
  const errors: string[] = [];
  const nextFunc = normalizeCurrencyCode(draft.functionalCurrencyCode);
  const nextPres = normalizeCurrencyCode(draft.presentationCurrencyCode);
  const dateText = draft.effectiveDate.trim();

  if (!/^[A-Z]{3}$/.test(nextFunc)) {
    errors.push("Functional currency must be a valid 3-letter ISO currency code.");
  }
  if (!/^[A-Z]{3}$/.test(nextPres)) {
    errors.push("Presentation currency must be a valid 3-letter ISO currency code.");
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(dateText)) {
    errors.push("Effective date must be a valid YYYY-MM-DD date.");
  }

  const prevFunc = normalizeCurrencyCode(current?.functionalCurrencyCode);
  const prevPres = normalizeCurrencyCode(current?.presentationCurrencyCode);
  const prevDate = current?.currencyEffectiveDate ?? "";

  const functionalChanged = Boolean(current) && nextFunc !== prevFunc;
  const hasChanges =
    !current ||
    functionalChanged ||
    nextPres !== prevPres ||
    dateText !== prevDate;

  const requiresConfirmation =
    functionalChanged || Boolean(current?.hasPostedTransactions && hasChanges);

  return {
    valid: errors.length === 0,
    errors,
    hasChanges,
    functionalChanged,
    requiresConfirmation,
  };
}

export function validateExchangeRateDraft(
  draft: ExchangeRateDraft,
  existingRates: ExchangeRateDto[],
  editingRateId: string | null = null
): {
  valid: boolean;
  errors: string[];
  parsedRate: number | null;
} {
  const errors: string[] = [];
  const from = normalizeCurrencyCode(draft.fromCurrency);
  const to = normalizeCurrencyCode(draft.toCurrency);
  const effectiveDate = draft.effectiveDate.trim();
  const rateType = draft.rateType;
  const source = draft.source.trim();
  const parsedRate = Number(draft.rate);

  if (!/^[A-Z]{3}$/.test(from)) {
    errors.push("From Currency must be a 3-letter ISO code.");
  }
  if (!/^[A-Z]{3}$/.test(to)) {
    errors.push("To Currency must be a 3-letter ISO code.");
  }
  if (from && to && from === to) {
    errors.push("From Currency and To Currency must be different.");
  }
  if (!Number.isFinite(parsedRate) || parsedRate <= 0) {
    errors.push("Exchange rate must be a positive number greater than 0.");
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(effectiveDate)) {
    errors.push("Effective date must be formatted as YYYY-MM-DD.");
  }
  if (!["transaction", "average", "closing"].includes(rateType)) {
    errors.push("Rate type must be transaction, average, or closing.");
  }
  if (!source) {
    errors.push("Exchange rate source / authority (e.g. SBP, Treasury) is required.");
  }

  for (const existing of existingRates) {
    if (editingRateId && existing.exchangeRateId === editingRateId) continue;
    const exFrom = normalizeCurrencyCode(existing.fromCurrency);
    const exTo = normalizeCurrencyCode(existing.toCurrency);
    const exType = existing.rateType.toLowerCase();
    const sameBucket = existing.effectiveDate === effectiveDate && exType === rateType;
    if (!sameBucket) continue;

    if (exFrom === from && exTo === to) {
      errors.push(
        `Duplicate exchange rate: ${from}/${to} (${rateType}) already exists for ${effectiveDate}. Edit the existing entry instead.`
      );
      break;
    }
    if (exFrom === to && exTo === from) {
      errors.push(
        `Ambiguous inverse pair: ${exFrom}/${exTo} (${rateType}) already exists for ${effectiveDate}. Maintain one consistent quotation direction to prevent translation ambiguity.`
      );
      break;
    }
  }

  return {
    valid: errors.length === 0,
    errors,
    parsedRate: Number.isFinite(parsedRate) && parsedRate > 0 ? parsedRate : null,
  };
}

export function formatExchangeRateDisplay(fromCurrency: string, toCurrency: string, rate: number): string {
  const from = normalizeCurrencyCode(fromCurrency);
  const to = normalizeCurrencyCode(toCurrency);
  const decimals = rate >= 10 ? 4 : 6;
  return `1 ${from} = ${Number(rate).toLocaleString("en-US", {
    minimumFractionDigits: 2,
    maximumFractionDigits: decimals,
  })} ${to}`;
}
