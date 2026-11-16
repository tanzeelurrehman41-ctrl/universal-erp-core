/**
 * Universal ERP — Phase 14 Multi-Tenant SaaS Platform Helpers
 * Statutory tax rules, proration engines, entitlement evaluation, and status formatters.
 */

import {
  calculateGlobalTax,
  SUPPORTED_COUNTRIES,
  type CountryPackage,
  type CountryJurisdiction,
  getSupportedCountries,
  getCountryByCode,
  getJurisdictionsForCountry,
  formatCurrency,
  getCurrencySymbol,
  getCurrencyDecimals,
  calculateGlobalPayroll,
  type PayrollCalculationRequest,
  type PayrollCalculationResult,
} from "./localization/index.ts";

export {
  calculateGlobalTax,
  SUPPORTED_COUNTRIES,
  type CountryPackage,
  type CountryJurisdiction,
  getSupportedCountries,
  getCountryByCode,
  getJurisdictionsForCountry,
  formatCurrency,
  getCurrencySymbol,
  getCurrencyDecimals,
  calculateGlobalPayroll,
  type PayrollCalculationRequest,
  type PayrollCalculationResult,
};

export type PlanPricing = {
  planId: string;
  billingInterval: "month" | "year";
  currencyCode: string;
  price: number;
  effectiveFrom: string;
  effectiveUntil?: string | null;
};

export type PlanFeature = {
  featureCode: string;
  enabled: boolean;
  quantityLimit: number | null;
  limitUnit: string | null;
};

export type SubscriptionPlan = {
  id: string;
  planCode: string;
  name: string;
  description?: string | null;
  tierRank: number;
  status: string;
  isPublic: boolean;
  features: PlanFeature[];
  pricing: PlanPricing[];
};

export type TenantBrief = {
  id: string;
  tenantCode: string;
  displayName: string;
  legalName?: string | null;
  status: "pending" | "provisioning" | "active" | "suspended" | "cancelled" | "closed" | "archived" | string;
  defaultCountryCode: string;
  defaultCurrencyCode: string;
  defaultTimezone: string;
  createdAt: string;
  suspendedAt?: string | null;
  suspensionReason?: string | null;
};

export type TenantEntitlement = {
  featureCode: string;
  enabled: boolean;
  quantityLimit: number | null;
  limitUnit: string | null;
  sourceType: string;
  effectiveFrom: string;
};

export type UsageAggregation = {
  id: string;
  metricCode: string;
  currentQuantity: number;
  quantityLimit?: number | null;
  isExceeded: boolean;
  warningThresholdPct: number;
  warningIssued: boolean;
  lastAggregatedAt: string;
};

export type SubscriptionInvoiceLine = {
  id?: string;
  lineNumber: number;
  description: string;
  lineType: "base_plan" | "proration" | "overage" | "discount" | "tax" | "credit" | string;
  quantity: number;
  unitPrice: number;
  amount: number;
};

export type SubscriptionInvoice = {
  id: string;
  tenantId: string;
  invoiceNumber: string;
  subtotal: number;
  taxAmount: number;
  discountAmount: number;
  creditsApplied: number;
  totalAmount: number;
  currencyCode: string;
  status: "draft" | "issued" | "paid" | "past_due" | "void" | string;
  issuedAt: string;
  dueAt: string;
  paidAt?: string | null;
  lines: SubscriptionInvoiceLine[];
};

export type SubscriptionPayment = {
  id: string;
  tenantId: string;
  invoiceId?: string | null;
  paymentReference: string;
  providerType: string;
  amount: number;
  currencyCode: string;
  status: "initiated" | "pending" | "succeeded" | "failed" | "refunded" | string;
  failureReason?: string | null;
  idempotencyKey: string;
  processedAt: string;
};

export type SupportAccessGrant = {
  id: string;
  tenantId: string;
  supportStaffEmail: string;
  reason: string;
  isApproved: boolean;
  approvedBy?: string | null;
  startsAt: string;
  expiresAt: string;
  isRevoked: boolean;
  createdAt: string;
};

export type SaaSPlatformAlert = {
  id: string;
  tenantId?: string | null;
  alertType: string;
  severity: "info" | "warning" | "error" | "critical" | string;
  title: string;
  message: string;
  isAcknowledged: boolean;
  createdAt: string;
};

export type JurisdictionalTaxRate = {
  ratePct: number;
  taxAmount: number;
  totalAmount: number;
  taxAuthority: string;
  jurisdiction: string;
  tariffCode?: string;
  taxableBase?: number;
  isTaxInclusive?: boolean;
  taxRegime?: string;
  statutoryReference?: string;
  countryCode?: string;
  inputTaxRecoverable?: boolean;
  explanation?: string;
};

export type TaxCalculationOptions = {
  regime?: "standard_with_input_tax" | "reduced_without_input_tax" | "export_zero_rated" | "reverse_charge" | string;
  isTaxInclusive?: boolean;
  effectiveDate?: string;
  isExport?: boolean;
  tariffCode?: string;
  customerCountryCode?: string;
  supplierCountryCode?: string;
  customerType?: "b2b" | "b2c" | string;
};

/**
 * Calculates effective proration credit when upgrading/changing plans midway through a cycle.
 */
export function calculateProrationCredit(
  oldPlanPrice: number,
  totalDaysInPeriod: number,
  remainingDays: number
): number {
  if (totalDaysInPeriod <= 0 || remainingDays <= 0 || oldPlanPrice <= 0) {
    return 0;
  }
  const ratio = Math.min(1, remainingDays / totalDaysInPeriod);
  return Math.round(oldPlanPrice * ratio * 100) / 100;
}

/**
 * Calculates net payable amount for plan upgrade factoring in proration credit and existing billing credits.
 */
export function calculateNetUpgradePayable(
  newPlanPrice: number,
  prorationCredit: number,
  existingBillingCredits = 0
): { netPayable: number; creditsApplied: number; excessCreditRemaining: number } {
  const adjustedPrice = Math.max(0, newPlanPrice - prorationCredit);
  const creditsApplied = Math.min(adjustedPrice, Math.max(0, existingBillingCredits));
  const netPayable = Math.max(0, Math.round((adjustedPrice - creditsApplied) * 100) / 100);
  const excessCreditRemaining = Math.max(0, existingBillingCredits - creditsApplied);

  return { netPayable, creditsApplied, excessCreditRemaining };
}

/**
 * Resolves statutory sales tax on software/SaaS services across all 5 Pakistani jurisdictions and exports:
 * - Sindh SRB: 15% standard (Sindh Finance Act 2024 / SSTS Act 2011 Sec 8(1)); 3% conditional reduced rate without input tax. Tariff heading 9815.6000.
 * - Punjab PRA: 16% standard; 5% conditional reduced rate without input tax. Tariff heading 9815.6000.
 * - Federal ICT: 15% standard; 5% conditional reduced rate for PSEB certified providers. Tariff heading 9815.6000.
 * - KP KPRA: 15% standard; 2% conditional reduced rate under KP Finance Act 2026. Tariff heading 9850.0000.
 * - Balochistan BRA: 15% standard; 6% conditional reduced rate under Balochistan Finance Act 2026 (historical 3% pre-2026). Tariff heading 9868.0000.
 * - Export of Services: 0% zero-rated / exempt under provincial Place of Provision of Services Rules.
 */
export function calculateJurisdictionalTax(
  amount: number,
  jurisdiction = "FEDERAL",
  options: TaxCalculationOptions = {}
): JurisdictionalTaxRate {
  const norm = jurisdiction.trim().toUpperCase();
  const isExport = Boolean(options.isExport || norm === "EXPORT" || norm === "INTERNATIONAL");

  // Check for International Country or Subnational Jurisdiction
  const isUsState = ["NY", "TX", "WA", "CA", "IL"].includes(norm);
  const isIntlCountry = ["US", "GB", "UK", "DE", "FR", "IE", "AE", "UAE", "SA", "KSA", "CA", "AU"].includes(norm);
  const explicitCountry = options.customerCountryCode && options.customerCountryCode.trim().toUpperCase() !== "PK";

  if (isUsState || isIntlCountry || explicitCountry) {
    const customerCountry = explicitCountry
      ? options.customerCountryCode!
      : isUsState
      ? "US"
      : norm === "UK"
      ? "GB"
      : norm === "UAE"
      ? "AE"
      : norm === "KSA"
      ? "SA"
      : norm;

    const res = calculateGlobalTax({
      amount,
      customerCountryCode: customerCountry,
      supplierCountryCode: options.supplierCountryCode || customerCountry,
      customerJurisdictionCode: isUsState ? norm : jurisdiction,
      customerType: options.customerType || "b2b",
      taxRegime: options.regime,
      isTaxInclusive: options.isTaxInclusive,
      effectiveDate: options.effectiveDate,
      tariffCode: options.tariffCode,
      isExport: options.isExport,
    });

    return {
      ratePct: res.ratePct,
      taxAmount: res.taxAmount,
      totalAmount: res.totalAmount,
      taxAuthority: res.taxAuthority,
      jurisdiction: res.jurisdiction,
      tariffCode: res.tariffCode,
      taxableBase: res.taxableAmount,
      isTaxInclusive: res.isTaxInclusive,
      taxRegime: res.taxRegime,
      statutoryReference: res.statutoryReference,
      countryCode: customerCountry,
      inputTaxRecoverable: res.inputTaxRecoverable,
      explanation: res.explanation,
    };
  }

  let ratePct = 15.0;
  let taxAuthority = "FBR ICT Sales Tax on Services";
  let statutoryReference = "Islamabad Capital Territory (Tax on Services) Ordinance, 2001, Schedule Table, Heading 9815.6000";
  let taxRegime = "standard_with_input_tax";
  let tariffCode = options.tariffCode || "9815.6000";

  if (isExport) {
    ratePct = 0.0;
    tariffCode = options.tariffCode || "9815.6000";
    taxAuthority = "FBR / Provincial Harmonized Place of Provision Rules";
    statutoryReference = "SRB / PRA / KPRA / FBR Place of Provision of Services Rules (Export Zero-Rating)";
    taxRegime = "export_zero_rated";
  } else if (norm === "SINDH") {
    tariffCode = options.tariffCode || "9815.6000";
    // Check historical rate interval (Pre-July 1, 2024 was 13%)
    if (options.effectiveDate && options.effectiveDate < "2024-07-01") {
      ratePct = 13.0;
      taxAuthority = "SRB Sindh Sales Tax on Services (Historical Standard)";
      statutoryReference = "Sindh Sales Tax on Services Act, 2011, Sec 8(1) (pre-2024 interval)";
      taxRegime = "standard_with_input_tax";
    } else if (options.regime === "reduced_without_input_tax") {
      ratePct = 3.0;
      taxAuthority = "SRB Reduced Rate for IT/Software (No Input Credit)";
      statutoryReference = "SRB Notification No. SRB-3-4/7/2013 as amended & Second Schedule Heading 9815.6000";
      taxRegime = "reduced_without_input_tax";
    } else {
      // Standard Sindh rate verified at 15.0% under Sindh Finance Act 2024
      ratePct = 15.0;
      taxAuthority = "SRB Sindh Sales Tax on Services (Standard SaaS)";
      statutoryReference = "Sindh Sales Tax on Services Act, 2011, Sec 8(1) as amended by Sindh Finance Act 2024";
      taxRegime = "standard_with_input_tax";
    }
  } else if (norm === "PUNJAB") {
    tariffCode = options.tariffCode || "9815.6000";
    if (options.regime === "reduced_without_input_tax") {
      ratePct = 5.0;
      taxAuthority = "PRA Reduced Rate for IT/Software (No Input Credit)";
      statutoryReference = "PRA Notification No. SO(TAX)1-1/2020 & Second Schedule Heading 9815.6000";
      taxRegime = "reduced_without_input_tax";
    } else {
      ratePct = 16.0;
      taxAuthority = "PRA Punjab Sales Tax on Services (Standard SaaS)";
      statutoryReference = "Punjab Sales Tax on Services Act, 2012, Second Schedule, Heading 9815.6000";
      taxRegime = "standard_with_input_tax";
    }
  } else if (norm === "KP" || norm === "KHYBER PAKHTUNKHWA" || norm === "KPK") {
    tariffCode = options.tariffCode || "9850.0000";
    if (options.regime === "reduced_without_input_tax") {
      ratePct = 2.0;
      taxAuthority = "KPRA Reduced Rate for IT/Software (No Input Credit)";
      statutoryReference = "KP Finance Act 2026 & Second Schedule Heading 9850.0000 Reduced Column";
      taxRegime = "reduced_without_input_tax";
    } else {
      ratePct = 15.0;
      taxAuthority = "KPRA Khyber Pakhtunkhwa Sales Tax on Services (Standard)";
      statutoryReference = "Khyber Pakhtunkhwa Sales Tax on Services Act, 2022, Second Schedule, Heading 9850.0000 as amended by KP Finance Act 2026";
      taxRegime = "standard_with_input_tax";
    }
  } else if (norm === "BALOCHISTAN") {
    tariffCode = options.tariffCode || "9868.0000";
    if (options.regime === "reduced_without_input_tax") {
      if (options.effectiveDate && options.effectiveDate < "2026-07-01") {
        ratePct = 3.0;
        taxAuthority = "BRA Historical Reduced Rate for IT/Software (Pre-2026)";
        statutoryReference = "Balochistan Sales Tax on Services Act, 2015, Second Schedule (historical interval prior to Finance Act 2026)";
        taxRegime = "reduced_without_input_tax";
      } else {
        ratePct = 6.0;
        taxAuthority = "BRA Reduced Rate for IT/Software (No Input Credit)";
        statutoryReference = "Balochistan Finance Act 2026 & Second Schedule Heading 9868.0000 Reduced Rate Column";
        taxRegime = "reduced_without_input_tax";
      }
    } else {
      ratePct = 15.0;
      taxAuthority = "BRA Balochistan Sales Tax on Services (Standard SaaS)";
      statutoryReference = "Balochistan Sales Tax on Services Act, 2015, Second Schedule, Heading 9868.0000 as amended by Balochistan Finance Act 2026";
      taxRegime = "standard_with_input_tax";
    }
  } else {
    // Federal / ICT
    tariffCode = options.tariffCode || "9815.6000";
    if (options.regime === "reduced_without_input_tax") {
      ratePct = 5.0;
      taxAuthority = "ICT Reduced Rate for IT Services (PSEB Certified)";
      statutoryReference = "ICT Ordinance 2001, Table-II & S.R.O. Notifications for IT-enabled services";
      taxRegime = "reduced_without_input_tax";
    } else {
      ratePct = 15.0;
      taxAuthority = "ICT Sales Tax on IT/SaaS Services (Standard)";
      statutoryReference = "Islamabad Capital Territory (Tax on Services) Ordinance, 2001, Schedule Table, Heading 9815.6000";
      taxRegime = "standard_with_input_tax";
    }
  }

  let taxableBase = amount;
  let taxAmount = 0;
  let totalAmount = amount;

  if (options.isTaxInclusive) {
    if (ratePct > 0) {
      taxableBase = Math.round((amount / (1 + ratePct / 100)) * 100) / 100;
      taxAmount = Math.round((amount - taxableBase) * 100) / 100;
      totalAmount = amount;
    } else {
      taxableBase = amount;
      taxAmount = 0;
      totalAmount = amount;
    }
  } else {
    taxableBase = amount;
    taxAmount = Math.round(amount * (ratePct / 100) * 100) / 100;
    totalAmount = Math.round((amount + taxAmount) * 100) / 100;
  }

  return {
    ratePct,
    taxAmount,
    totalAmount,
    taxAuthority,
    jurisdiction: isExport ? "EXPORT" : norm,
    tariffCode,
    taxableBase,
    isTaxInclusive: Boolean(options.isTaxInclusive),
    taxRegime,
    statutoryReference,
    countryCode: "PK",
    inputTaxRecoverable: taxRegime !== "reduced_without_input_tax",
  };
}

/**
 * Computes formal credit note figures for service refunds or proration credit adjustments.
 */
export function calculateCreditNote(
  refundAmount: number,
  jurisdiction = "FEDERAL",
  options: TaxCalculationOptions = {}
): {
  refundSubtotal: number;
  taxReversed: number;
  totalCreditNote: number;
  ratePct: number;
  taxAuthority: string;
} {
  const tax = calculateJurisdictionalTax(refundAmount, jurisdiction, options);

  if (options.isTaxInclusive) {
    return {
      refundSubtotal: tax.taxableBase ?? refundAmount,
      taxReversed: tax.taxAmount,
      totalCreditNote: refundAmount,
      ratePct: tax.ratePct,
      taxAuthority: tax.taxAuthority
    };
  }

  return {
    refundSubtotal: refundAmount,
    taxReversed: tax.taxAmount,
    totalCreditNote: tax.totalAmount,
    ratePct: tax.ratePct,
    taxAuthority: tax.taxAuthority
  };
}

/**
 * Evaluates quantitative quota consumption against plan limits with 80% soft warning.
 */
export function evaluateQuotaStatus(
  currentUsage: number,
  limit: number | null
): {
  pct: number;
  isWarning: boolean;
  isExceeded: boolean;
  statusColor: "success" | "warning" | "danger" | "neutral";
  displayStatus: string;
} {
  if (limit === null || limit <= 0) {
    return {
      pct: 0,
      isWarning: false,
      isExceeded: false,
      statusColor: "neutral",
      displayStatus: "Unlimited"
    };
  }

  const rawPct = (currentUsage / limit) * 100;
  const pct = Math.round(rawPct * 10) / 10;
  const isExceeded = currentUsage > limit;
  const isWarning = !isExceeded && rawPct >= 80;

  let statusColor: "success" | "warning" | "danger" | "neutral" = "success";
  let displayStatus = `${pct}% Used`;

  if (isExceeded) {
    statusColor = "danger";
    displayStatus = `Exceeded (${pct}%)`;
  } else if (isWarning) {
    statusColor = "warning";
    displayStatus = `Warning (${pct}%)`;
  }

  return { pct, isWarning, isExceeded, statusColor, displayStatus };
}

/**
 * Maps tenant lifecycle state to UI badge classes and human labels.
 */
export function getTenantStatusBadge(status: string): { label: string; badgeClass: string } {
  switch (status.toLowerCase()) {
    case "active":
      return { label: "Active", badgeClass: "badge-success" };
    case "provisioning":
      return { label: "Provisioning", badgeClass: "badge-info" };
    case "pending":
      return { label: "Pending Setup", badgeClass: "badge-warning" };
    case "suspended":
      return { label: "Suspended", badgeClass: "badge-danger" };
    case "cancelled":
      return { label: "Cancelled", badgeClass: "badge-secondary" };
    case "closed":
      return { label: "Closed", badgeClass: "badge-dark" };
    case "archived":
      return { label: "Archived", badgeClass: "badge-slate" };
    default:
      return { label: status, badgeClass: "badge-neutral" };
  }
}

/**
 * Maps subscription status to UI badge styling.
 */
export function getSubscriptionStatusBadge(status: string): { label: string; badgeClass: string } {
  switch (status.toLowerCase()) {
    case "active":
      return { label: "Active", badgeClass: "badge-success" };
    case "trial":
      return { label: "Trial", badgeClass: "badge-info" };
    case "past_due":
      return { label: "Past Due", badgeClass: "badge-danger" };
    case "grace_period":
    case "grace":
      return { label: "Grace Period", badgeClass: "badge-warning" };
    case "suspended":
      return { label: "Suspended", badgeClass: "badge-danger" };
    case "cancelled":
      return { label: "Cancelled", badgeClass: "badge-secondary" };
    case "expired":
      return { label: "Expired", badgeClass: "badge-dark" };
    default:
      return { label: status, badgeClass: "badge-neutral" };
  }
}

/**
 * Maps payment status to UI badge styling.
 */
export function getPaymentStatusBadge(status: string): { label: string; badgeClass: string } {
  switch (status.toLowerCase()) {
    case "succeeded":
      return { label: "Succeeded", badgeClass: "badge-success" };
    case "pending":
    case "initiated":
      return { label: "Pending", badgeClass: "badge-warning" };
    case "failed":
      return { label: "Failed", badgeClass: "badge-danger" };
    case "refunded":
      return { label: "Refunded", badgeClass: "badge-purple" };
    default:
      return { label: status, badgeClass: "badge-neutral" };
  }
}

/**
 * Computes consolidated totals for invoice lines.
 */
export function calculateInvoiceTotals(lines: { amount: number; lineType: string }[]): {
  subtotal: number;
  discount: number;
  credit: number;
  tax: number;
  netPayable: number;
} {
  let subtotal = 0;
  let discount = 0;
  let credit = 0;
  let tax = 0;

  for (const line of lines) {
    if (line.lineType === "discount") {
      discount += Math.abs(line.amount);
    } else if (line.lineType === "credit") {
      credit += Math.abs(line.amount);
    } else if (line.lineType === "tax") {
      tax += line.amount;
    } else {
      subtotal += line.amount;
    }
  }

  const netPayable = Math.max(0, Math.round((subtotal - discount - credit + tax) * 100) / 100);

  return {
    subtotal: Math.round(subtotal * 100) / 100,
    discount: Math.round(discount * 100) / 100,
    credit: Math.round(credit * 100) / 100,
    tax: Math.round(tax * 100) / 100,
    netPayable
  };
}

/**
 * Formats PKR currency amounts.
 */
export function formatCurrencyPkr(amount: number): string {
  return "PKR " + Number(amount || 0).toLocaleString("en-PK", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
  });
}
