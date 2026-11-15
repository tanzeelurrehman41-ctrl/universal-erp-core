// ── Phase 7 Core Sales & Order-to-Cash Helper Functions ─────────────────────

export type CreditEvaluationInput = {
  creditLimit: number;
  currentBalance: number;
  uninvoicedOrdersTotal: number;
  newOrderAmount: number;
};

export type CreditEvaluationResult = {
  isAllowed: boolean;
  requiresOverride: boolean;
  currentExposure: number;
  projectedExposure: number;
  availableCredit: number;
  excessAmount: number;
  message: string;
};

export function evaluateCustomerCredit(input: CreditEvaluationInput): CreditEvaluationResult {
  const { creditLimit, currentBalance, uninvoicedOrdersTotal, newOrderAmount } = input;
  if (newOrderAmount < 0) {
    throw new Error('New order amount cannot be negative.');
  }

  const currentExposure = Math.round((currentBalance + uninvoicedOrdersTotal) * 100) / 100;
  const projectedExposure = Math.round((currentExposure + newOrderAmount) * 100) / 100;

  if (creditLimit <= 0) {
    if (newOrderAmount > 0) {
      return {
        isAllowed: false,
        requiresOverride: true,
        currentExposure,
        projectedExposure,
        availableCredit: 0,
        excessAmount: newOrderAmount,
        message: 'Customer has no credit limit facility configured (cash-only terms). Credit sale requires authorized approval.',
      };
    }
    return {
      isAllowed: true,
      requiresOverride: false,
      currentExposure,
      projectedExposure,
      availableCredit: 0,
      excessAmount: 0,
      message: 'Order approved.',
    };
  }

  if (projectedExposure <= creditLimit) {
    const availableCredit = Math.round((creditLimit - projectedExposure) * 100) / 100;
    return {
      isAllowed: true,
      requiresOverride: false,
      currentExposure,
      projectedExposure,
      availableCredit,
      excessAmount: 0,
      message: `Credit check passed. Available credit after order: PKR ${availableCredit.toLocaleString('en-US', { minimumFractionDigits: 2 })}.`,
    };
  }

  const excessAmount = Math.round((projectedExposure - creditLimit) * 100) / 100;
  return {
    isAllowed: false,
    requiresOverride: true,
    currentExposure,
    projectedExposure,
    availableCredit: 0,
    excessAmount,
    message: `CREDIT_LIMIT_EXCEEDED: Projected exposure of PKR ${projectedExposure.toLocaleString('en-US', { minimumFractionDigits: 2 })} exceeds limit of PKR ${creditLimit.toLocaleString('en-US', { minimumFractionDigits: 2 })} by PKR ${excessAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })}. Approval override required.`,
  };
}

export type QuotationLineInput = {
  quantity: number;
  unitPrice: number;
  taxRate?: number; // e.g. 0.10 for 10%
  discountPercent?: number; // e.g. 5 for 5%
};

export type QuotationTotalsResult = {
  subtotal: number;
  discountAmount: number;
  netRevenue: number;
  taxAmount: number;
  totalAmount: number;
};

export function calculateQuotationTotals(
  lines: QuotationLineInput[],
  headerDiscount: number = 0
): QuotationTotalsResult {
  let subtotal = 0;
  let lineDiscounts = 0;
  let taxAmount = 0;

  for (const l of lines) {
    const price = l.unitPrice ?? 0;
    const gross = l.quantity * price;
    const discPct = l.discountPercent ?? 0;
    const disc = gross * (discPct / 100);
    const net = gross - disc;
    const rate = l.taxRate ?? 0;
    const tax = net * rate;

    subtotal += gross;
    lineDiscounts += disc;
    taxAmount += tax;
  }

  const totalDiscount = Math.round((lineDiscounts + headerDiscount) * 100) / 100;
  const netRevenue = Math.round(Math.max(0, subtotal - totalDiscount) * 100) / 100;
  const roundedTax = Math.round(taxAmount * 100) / 100;
  const totalAmount = Math.round((netRevenue + roundedTax) * 100) / 100;

  return {
    subtotal: Math.round(subtotal * 100) / 100,
    discountAmount: totalDiscount,
    netRevenue,
    taxAmount: roundedTax,
    totalAmount,
  };
}

export type OpenInvoiceAgingInput = {
  invoiceId: string;
  invoiceNumber: string;
  invoiceDate: string; // YYYY-MM-DD
  dueDate: string;     // YYYY-MM-DD
  totalAmount: number;
  paidAmount: number;
};

export type ArAgingBucketsResult = {
  currentBalance: number; // 0-30 days past due
  days31To60: number;
  days61To90: number;
  over90Days: number;
  totalReceivable: number;
  totalCreditNotes: number;
  totalReceipts: number;
  netOutstanding: number;
  openInvoiceCount: number;
};

export function calculateArAgingBuckets(
  invoices: OpenInvoiceAgingInput[],
  totalCreditNotes: number = 0,
  totalReceipts: number = 0,
  asOfDateStr?: string
): ArAgingBucketsResult {
  const asOf = asOfDateStr ? new Date(asOfDateStr) : new Date();

  let current = 0;
  let d31To60 = 0;
  let d61To90 = 0;
  let over90 = 0;
  let totalReceivable = 0;
  let openCount = 0;

  for (const inv of invoices) {
    const outstanding = Math.max(0, inv.totalAmount - inv.paidAmount);
    if (outstanding <= 0) continue;

    openCount++;
    totalReceivable += outstanding;

    const dueDate = new Date(inv.dueDate);
    const diffTime = asOf.getTime() - dueDate.getTime();
    const daysPastDue = Math.floor(diffTime / (1000 * 60 * 60 * 24));

    if (daysPastDue <= 30) {
      current += outstanding;
    } else if (daysPastDue <= 60) {
      d31To60 += outstanding;
    } else if (daysPastDue <= 90) {
      d61To90 += outstanding;
    } else {
      over90 += outstanding;
    }
  }

  const netOutstanding = Math.max(0, totalReceivable - totalCreditNotes);

  return {
    currentBalance: Math.round(current * 100) / 100,
    days31To60: Math.round(d31To60 * 100) / 100,
    days61To90: Math.round(d61To90 * 100) / 100,
    over90Days: Math.round(over90 * 100) / 100,
    totalReceivable: Math.round(totalReceivable * 100) / 100,
    totalCreditNotes: Math.round(totalCreditNotes * 100) / 100,
    totalReceipts: Math.round(totalReceipts * 100) / 100,
    netOutstanding: Math.round(netOutstanding * 100) / 100,
    openInvoiceCount: openCount,
  };
}

export type AutoAllocationItem = {
  invoiceId: string;
  invoiceNumber: string;
  allocatedAmount: number;
};

export type AutoAllocationResult = {
  allocations: AutoAllocationItem[];
  unallocatedAmount: number;
};

export function autoAllocateReceipt(
  receiptAmount: number,
  openInvoices: OpenInvoiceAgingInput[]
): AutoAllocationResult {
  if (receiptAmount <= 0) {
    return { allocations: [], unallocatedAmount: 0 };
  }

  let remaining = receiptAmount;
  const allocations: AutoAllocationItem[] = [];

  // Sort invoices oldest due date first
  const sorted = [...openInvoices].sort(
    (a, b) => new Date(a.dueDate).getTime() - new Date(b.dueDate).getTime()
  );

  for (const inv of sorted) {
    if (remaining <= 0) break;
    const outstanding = Math.max(0, inv.totalAmount - inv.paidAmount);
    if (outstanding <= 0) continue;

    const toAllocate = Math.min(remaining, outstanding);
    allocations.push({
      invoiceId: inv.invoiceId,
      invoiceNumber: inv.invoiceNumber,
      allocatedAmount: Math.round(toAllocate * 100) / 100,
    });
    remaining = Math.round((remaining - toAllocate) * 100) / 100;
  }

  return {
    allocations,
    unallocatedAmount: remaining,
  };
}

export function validateDispatchQuantity(
  orderedQty: number,
  previouslyDispatchedQty: number,
  attemptedDispatchQty: number
): { isValid: boolean; remainingOrderQty: number; error?: string } {
  const remaining = Math.max(0, orderedQty - previouslyDispatchedQty);
  if (attemptedDispatchQty <= 0) {
    return { isValid: false, remainingOrderQty: remaining, error: 'Dispatch quantity must be greater than zero.' };
  }
  if (attemptedDispatchQty > remaining) {
    return {
      isValid: false,
      remainingOrderQty: remaining,
      error: `Attempted dispatch (${attemptedDispatchQty}) exceeds remaining order balance (${remaining}).`,
    };
  }
  return { isValid: true, remainingOrderQty: remaining };
}
