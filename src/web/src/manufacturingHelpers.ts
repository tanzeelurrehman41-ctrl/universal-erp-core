export interface BomItemInput {
  componentVariantId: string;
  componentName: string;
  componentType: 'raw_material' | 'subassembly' | 'phantom' | 'packaging';
  quantityPer: number;
  scrapPercentage: number;
}

export interface ExplodedRequirement {
  componentVariantId: string;
  componentName: string;
  componentType: string;
  netQuantityPer: number;
  scrapPercentage: number;
  grossRequiredQuantity: number;
}

export function explodeBomRequirements(
  orderQuantity: number,
  baseQuantity: number = 1,
  yieldPercent: number = 100,
  items: BomItemInput[] = []
): ExplodedRequirement[] {
  if (orderQuantity <= 0) return [];
  const baseQty = baseQuantity > 0 ? baseQuantity : 1;
  const yieldFactor = yieldPercent > 0 ? yieldPercent / 100 : 1;
  const effectiveMultiplier = orderQuantity / (baseQty * yieldFactor);

  return items.map((item) => {
    const scrapFactor = 1 + Math.max(0, item.scrapPercentage) / 100;
    const grossQty = Math.round(effectiveMultiplier * item.quantityPer * scrapFactor * 10000) / 10000;

    return {
      componentVariantId: item.componentVariantId,
      componentName: item.componentName,
      componentType: item.componentType,
      netQuantityPer: item.quantityPer,
      scrapPercentage: item.scrapPercentage,
      grossRequiredQuantity: grossQty,
    };
  });
}

export interface WorkOrderCostRollup {
  totalMaterialCost: number;
  totalLaborCost: number;
  totalOverheadCost: number;
  totalProductionCost: number;
  unitCostFinal: number;
  finishedGoodsValuation: number;
  scrapExpense: number;
  costVariance: number;
}

export function calculateWorkOrderCosting(
  materialCost: number,
  laborCost: number,
  overheadCost: number,
  acceptedQuantity: number,
  scrappedQuantity: number
): WorkOrderCostRollup {
  const totalCost = Math.max(0, materialCost) + Math.max(0, laborCost) + Math.max(0, overheadCost);
  const totalUnits = Math.max(0, acceptedQuantity) + Math.max(0, scrappedQuantity);

  if (totalUnits <= 0 || totalCost <= 0) {
    return {
      totalMaterialCost: materialCost,
      totalLaborCost: laborCost,
      totalOverheadCost: overheadCost,
      totalProductionCost: totalCost,
      unitCostFinal: 0,
      finishedGoodsValuation: 0,
      scrapExpense: 0,
      costVariance: 0,
    };
  }

  const unitCost = Math.round((totalCost / totalUnits) * 10000) / 10000;
  const fgValuation = Math.round(acceptedQuantity * unitCost * 100) / 100;
  const scrapExpense = Math.round(scrappedQuantity * unitCost * 100) / 100;
  const variance = Math.round((totalCost - (fgValuation + scrapExpense)) * 100) / 100;

  return {
    totalMaterialCost: materialCost,
    totalLaborCost: laborCost,
    totalOverheadCost: overheadCost,
    totalProductionCost: totalCost,
    unitCostFinal: unitCost,
    finishedGoodsValuation: fgValuation,
    scrapExpense: scrapExpense,
    costVariance: variance,
  };
}

export function validateWorkOrderProduction(
  plannedQuantity: number,
  currentCompleted: number,
  newAcceptedQuantity: number,
  newScrappedQuantity: number,
  maxOverproductionPercent: number = 10
): { isValid: boolean; error?: string } {
  if (newAcceptedQuantity < 0 || newScrappedQuantity < 0) {
    return { isValid: false, error: 'NEGATIVE_QUANTITY_FORBIDDEN' };
  }

  if (newAcceptedQuantity + newScrappedQuantity <= 0) {
    return { isValid: false, error: 'ZERO_COMPLETION_QUANTITY' };
  }

  const totalCompleted = currentCompleted + newAcceptedQuantity;
  const maxAllowed = plannedQuantity * (1 + maxOverproductionPercent / 100);

  if (totalCompleted > maxAllowed) {
    return {
      isValid: false,
      error: `OVERPRODUCTION_LIMIT_EXCEEDED: Cannot exceed planned quantity (${plannedQuantity}) by more than ${maxOverproductionPercent}%. Max allowed: ${maxAllowed}`,
    };
  }

  return { isValid: true };
}

export function formatCurrencyPkr(amount: number): string {
  return new Intl.NumberFormat('en-PK', {
    style: 'currency',
    currency: 'PKR',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount);
}
