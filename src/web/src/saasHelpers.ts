/**
 * Universal ERP — Phase 14 Multi-Tenant SaaS Platform Helpers
 * Statutory tax rules, proration engines, entitlement evaluation, and status formatters.
 */

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
 * Resolves Pakistani provincial & federal sales tax on software/SaaS services.
 * FBR ICT (15%), PRA Punjab (16%), SRB Sindh (13%), KPRA KP (15%), BRA Balochistan (15%).
 */
export function calculateJurisdictionalTax(
  subtotal: number,
  jurisdiction = "FEDERAL"
): JurisdictionalTaxRate {
  const norm = jurisdiction.trim().toUpperCase();
  let ratePct = 15.0;
  let taxAuthority = "FBR ICT Sales Tax on Services";

  if (norm === "PUNJAB") {
    ratePct = 16.0;
    taxAuthority = "PRA Punjab Sales Tax on Services";
  } else if (norm === "SINDH") {
    ratePct = 13.0;
    taxAuthority = "SRB Sindh Sales Tax on Services";
  } else if (norm === "KP" || norm === "KHYBER PAKHTUNKHWA") {
    ratePct = 15.0;
    taxAuthority = "KPRA Khyber Pakhtunkhwa Sales Tax on Services";
  } else if (norm === "BALOCHISTAN") {
    ratePct = 15.0;
    taxAuthority = "BRA Balochistan Sales Tax on Services";
  }

  const taxAmount = Math.round(subtotal * (ratePct / 100) * 100) / 100;
  const totalAmount = Math.round((subtotal + taxAmount) * 100) / 100;

  return {
    ratePct,
    taxAmount,
    totalAmount,
    taxAuthority,
    jurisdiction: norm
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
