// ── Phase 6 Purchasing & Vendor Management Helper Functions ─────────────────

export type MatchingTolerancePolicy = {
  priceTolerancePercent: number;
  quantityTolerancePercent: number;
  allowPriceVariance: boolean;
  allowQuantityVariance: boolean;
};

export type MatchLineInput = {
  lineNumber: number;
  poOrderedQty: number;
  poUnitPrice: number;
  grnReceivedQty: number;
  grnUnitPrice: number;
  invoiceQty: number;
  invoiceUnitPrice: number;
};

export type MatchLineResult = {
  lineNumber: number;
  priceVariance: number;
  priceVariancePercent: number;
  quantityVariance: number;
  quantityVariancePercent: number;
  isPriceWithinTolerance: boolean;
  isQuantityWithinTolerance: boolean;
  lineStatus: 'matched' | 'price_variance' | 'quantity_variance' | 'price_and_quantity_variance' | 'variance_exceeded';
};

export type MatchingEvaluationResult = {
  isMatched: boolean;
  overallStatus: 'matched' | 'variance_exceeded';
  totalPriceVariance: number;
  totalQuantityVariance: number;
  lines: MatchLineResult[];
};

export function evaluateThreeWayMatch(
  lines: MatchLineInput[],
  policy: MatchingTolerancePolicy
): MatchingEvaluationResult {
  const lineResults: MatchLineResult[] = [];
  let allMatched = true;
  let totalPriceVariance = 0;
  let totalQuantityVariance = 0;

  for (const line of lines) {
    const priceVariance = line.invoiceUnitPrice - line.grnUnitPrice;
    const quantityVariance = line.invoiceQty - line.grnReceivedQty;

    const priceVariancePercent =
      line.grnUnitPrice > 0
        ? Math.abs(priceVariance) / line.grnUnitPrice * 100
        : 0;

    const quantityVariancePercent =
      line.grnReceivedQty > 0
        ? Math.abs(quantityVariance) / line.grnReceivedQty * 100
        : 0;

    const isPriceWithin =
      priceVariancePercent <= policy.priceTolerancePercent &&
      (priceVariance === 0 || policy.allowPriceVariance);

    const isQuantityWithin =
      quantityVariancePercent <= policy.quantityTolerancePercent &&
      (quantityVariance === 0 || policy.allowQuantityVariance);

    let status: MatchLineResult['lineStatus'];
    if (priceVariance === 0 && quantityVariance === 0) {
      status = 'matched';
    } else if (!isPriceWithin || !isQuantityWithin) {
      status = 'variance_exceeded';
      allMatched = false;
    } else if (priceVariance !== 0 && quantityVariance !== 0) {
      status = 'price_and_quantity_variance';
    } else if (priceVariance !== 0) {
      status = 'price_variance';
    } else {
      status = 'quantity_variance';
    }

    totalPriceVariance += priceVariance * line.invoiceQty;
    totalQuantityVariance += quantityVariance;

    lineResults.push({
      lineNumber: line.lineNumber,
      priceVariance: Math.round(priceVariance * 100) / 100,
      priceVariancePercent: Math.round(priceVariancePercent * 100) / 100,
      quantityVariance,
      quantityVariancePercent: Math.round(quantityVariancePercent * 100) / 100,
      isPriceWithinTolerance: isPriceWithin,
      isQuantityWithinTolerance: isQuantityWithin,
      lineStatus: status,
    });
  }

  return {
    isMatched: allMatched,
    overallStatus: allMatched ? 'matched' : 'variance_exceeded',
    totalPriceVariance: Math.round(totalPriceVariance * 100) / 100,
    totalQuantityVariance,
    lines: lineResults,
  };
}

export type SupplierBalanceSummary = {
  supplierId: string;
  supplierCode: string;
  name: string;
  currencyCode: string;
  totalInvoiced: number;
  totalDebitNotes: number;
  netPayable: number;
  totalPaid: number;
  outstandingBalance: number;
  paymentStatus: 'paid' | 'partially_paid' | 'unpaid';
};

export function calculateSupplierBalance(
  totalInvoiced: number,
  totalDebitNotes: number,
  totalPaid: number
): {
  netPayable: number;
  outstandingBalance: number;
  paymentStatus: 'paid' | 'partially_paid' | 'unpaid';
} {
  const netPayable = Math.max(0, Math.round((totalInvoiced - totalDebitNotes) * 100) / 100);
  const outstandingBalance = Math.max(0, Math.round((netPayable - totalPaid) * 100) / 100);

  let paymentStatus: 'paid' | 'partially_paid' | 'unpaid';
  if (outstandingBalance === 0) {
    paymentStatus = 'paid';
  } else if (outstandingBalance < netPayable) {
    paymentStatus = 'partially_paid';
  } else {
    paymentStatus = 'unpaid';
  }

  return {
    netPayable,
    outstandingBalance,
    paymentStatus,
  };
}

export function validateInspectionQuantities(
  receivedQty: number,
  acceptedQty: number,
  quarantineQty: number,
  rejectedQty: number
): { isValid: boolean; error?: string } {
  if (acceptedQty < 0 || quarantineQty < 0 || rejectedQty < 0) {
    return { isValid: false, error: 'Inspection quantities must not be negative.' };
  }
  const total = acceptedQty + quarantineQty + rejectedQty;
  if (total !== receivedQty) {
    return {
      isValid: false,
      error: `Sum of accepted (${acceptedQty}), quarantined (${quarantineQty}), and rejected (${rejectedQty}) must equal received quantity (${receivedQty}).`,
    };
  }
  return { isValid: true };
}
