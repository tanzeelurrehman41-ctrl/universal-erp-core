/**
 * Universal ERP — Multi-Currency Formatting Engine
 * Formats currency values according to ISO 4217 currency specifications,
 * locale formatting conventions, and jurisdiction rounding precision.
 */

export type CurrencyInfo = {
  code: string;
  name: string;
  symbol: string;
  decimals: number;
};

export const CURRENCY_REGISTRY: Record<string, CurrencyInfo> = {
  USD: { code: "USD", name: "US Dollar", symbol: "$", decimals: 2 },
  EUR: { code: "EUR", name: "Euro", symbol: "€", decimals: 2 },
  GBP: { code: "GBP", name: "British Pound", symbol: "£", decimals: 2 },
  PKR: { code: "PKR", name: "Pakistani Rupee", symbol: "Rs.", decimals: 2 },
  AED: { code: "AED", name: "UAE Dirham", symbol: "AED", decimals: 2 },
  SAR: { code: "SAR", name: "Saudi Riyal", symbol: "SAR", decimals: 2 },
  CAD: { code: "CAD", name: "Canadian Dollar", symbol: "CA$", decimals: 2 },
  AUD: { code: "AUD", name: "Australian Dollar", symbol: "A$", decimals: 2 },
  JPY: { code: "JPY", name: "Japanese Yen", symbol: "¥", decimals: 0 },
  KRW: { code: "KRW", name: "South Korean Won", symbol: "₩", decimals: 0 },
  BHD: { code: "BHD", name: "Bahraini Dinar", symbol: "BD", decimals: 3 },
  KWD: { code: "KWD", name: "Kuwaiti Dinar", symbol: "KD", decimals: 3 },
  OMR: { code: "OMR", name: "Omani Rial", symbol: "OMR", decimals: 3 },
  INR: { code: "INR", name: "Indian Rupee", symbol: "₹", decimals: 2 },
  SGD: { code: "SGD", name: "Singapore Dollar", symbol: "S$", decimals: 2 },
  CHF: { code: "CHF", name: "Swiss Franc", symbol: "CHF", decimals: 2 },
};

/**
 * Gets decimal precision for a currency code.
 */
export function getCurrencyDecimals(currencyCode = "USD"): number {
  const norm = currencyCode.trim().toUpperCase();
  return CURRENCY_REGISTRY[norm]?.decimals ?? 2;
}

/**
 * Gets symbol for a currency code.
 */
export function getCurrencySymbol(currencyCode = "USD"): string {
  const norm = currencyCode.trim().toUpperCase();
  return CURRENCY_REGISTRY[norm]?.symbol ?? norm;
}

/**
 * Formats a numeric amount using international currency standards.
 * Supports JPY (0 decimals), BHD (3 decimals), USD/EUR/GBP/PKR (2 decimals).
 */
export function formatCurrency(
  amount: number,
  currencyCode = "USD",
  locale?: string
): string {
  const normCode = currencyCode.trim().toUpperCase();
  const info = CURRENCY_REGISTRY[normCode];
  const decimals = info?.decimals ?? 2;
  const symbol = info?.symbol ?? normCode;

  // South Asian numbering system for PKR/INR if requested or default
  if ((normCode === "PKR" || normCode === "INR") && (!locale || locale.startsWith("ur") || locale.startsWith("en-PK") || locale.startsWith("hi"))) {
    const isNegative = amount < 0;
    const absVal = Math.abs(amount).toFixed(decimals);
    const [intPart, decPart] = absVal.split(".");
    
    // Format integer part with South Asian lakh/crore commas: last 3 digits, then groups of 2
    let formattedInt = "";
    if (intPart.length > 3) {
      const lastThree = intPart.substring(intPart.length - 3);
      const remaining = intPart.substring(0, intPart.length - 3);
      formattedInt = remaining.replace(/\B(?=(\d{2})+(?!\d))/g, ",") + "," + lastThree;
    } else {
      formattedInt = intPart;
    }

    const res = `${symbol} ${formattedInt}${decPart ? "." + decPart : ""}`;
    return isNegative ? `-${res}` : res;
  }

  // Standard Western comma grouping (thousands)
  const isNegative = amount < 0;
  const absVal = Math.abs(amount).toFixed(decimals);
  const [intPart, decPart] = absVal.split(".");
  const formattedInt = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  const formattedNum = `${formattedInt}${decPart ? "." + decPart : ""}`;

  const res = `${symbol} ${formattedNum}`;
  return isNegative ? `-${res}` : res;
}

/**
 * Backward-compatible helper for legacy Pakistani Rupee formatting.
 */
export function formatCurrencyPkr(amount: number): string {
  return formatCurrency(amount, "PKR", "en-PK");
}
