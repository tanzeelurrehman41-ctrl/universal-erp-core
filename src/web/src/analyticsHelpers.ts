/**
 * Universal ERP - Phase 12 Enterprise Analytics & BI Helpers
 * KPI evaluation, financial metrics, reconciliation audits, and export formatting
 */

export interface KpiEvaluation {
  variance: number;
  variancePercentage: number;
  status: 'on_track' | 'warning' | 'critical';
}

export interface PlatformHealthEvaluation {
  isHealthy: boolean;
  glStatus: 'BALANCED' | 'DISCREPANCY';
  fifoStatus: 'RECONCILED' | 'VARIANCE';
  summary: string;
}

/**
 * Calculates variance and percentage against a KPI target.
 */
export function calculateVariance(actual: number, target: number): { variance: number; variancePercentage: number } {
  const variance = actual - target;
  const variancePercentage = target !== 0 ? Math.round(((actual - target) / target) * 10000) / 100 : 0;
  return { variance, variancePercentage };
}

/**
 * Evaluates KPI status taking into account whether higher or lower values are favorable.
 * For instance, Revenue: higher is better. Scrap Rate: lower is better.
 */
export function evaluateKpiStatus(
  actual: number,
  target: number,
  isHigherBetter: boolean = true
): 'on_track' | 'warning' | 'critical' {
  if (target === 0) return 'on_track';

  const diffPct = ((actual - target) / target) * 100;

  if (isHigherBetter) {
    if (diffPct >= 0) return 'on_track';
    if (diffPct >= -10) return 'warning';
    return 'critical';
  } else {
    // Lower is better (e.g. Scrap rate, expenses, DPO excess)
    if (diffPct <= 0) return 'on_track';
    if (diffPct <= 10) return 'warning';
    return 'critical';
  }
}

/**
 * Formats a KPI number according to its metric unit (PKR, %, EA, Days, Ratio).
 */
export function formatKpiValue(val: number, unit: string): string {
  if (unit.toUpperCase() === 'PKR') {
    return `PKR ${Math.round(val).toLocaleString()}`;
  }
  if (unit === '%') {
    return `${val.toFixed(1)}%`;
  }
  if (unit.toLowerCase() === 'days') {
    return `${val.toFixed(1)} days`;
  }
  if (unit.toLowerCase() === 'ratio') {
    return `${val.toFixed(2)}x`;
  }
  return `${val.toLocaleString()} ${unit}`;
}

/**
 * Calculates Gross Profit Margin percentage.
 */
export function calculateGrossProfitMargin(revenue: number, cogs: number): number {
  if (revenue <= 0) return 0;
  return Math.round(((revenue - cogs) / revenue) * 10000) / 100;
}

/**
 * Calculates Net Profit Margin percentage.
 */
export function calculateNetProfitMargin(revenue: number, netIncome: number): number {
  if (revenue <= 0) return 0;
  return Math.round((netIncome / revenue) * 10000) / 100;
}

/**
 * Calculates Days Sales Outstanding (DSO) = (AR / Credit Sales) * Period Days.
 */
export function calculateDso(accountsReceivable: number, creditSales: number, days: number = 365): number {
  if (creditSales <= 0) return 0;
  return Math.round((accountsReceivable / creditSales) * days * 10) / 10;
}

/**
 * Calculates Days Payable Outstanding (DPO) = (AP / Total Purchases or COGS) * Period Days.
 */
export function calculateDpo(accountsPayable: number, purchasesOrCogs: number, days: number = 365): number {
  if (purchasesOrCogs <= 0) return 0;
  return Math.round((accountsPayable / purchasesOrCogs) * days * 10) / 10;
}

/**
 * Calculates Inventory Turnover Ratio = COGS / Average Inventory.
 */
export function calculateInventoryTurnover(cogs: number, avgInventory: number): number {
  if (avgInventory <= 0) return 0;
  return Math.round((cogs / avgInventory) * 100) / 100;
}

/**
 * Evaluates full platform reconciliation between GL double-entry balance and Model A FIFO cost layers.
 */
export function evaluatePlatformHealth(
  glBalanced: boolean,
  glDiscrepancy: number,
  fifoReconciled: boolean,
  fifoVariance: number
): PlatformHealthEvaluation {
  const isHealthy = glBalanced && glDiscrepancy < 0.01 && fifoReconciled && fifoVariance < 0.01;
  const glStatus = glBalanced && glDiscrepancy < 0.01 ? 'BALANCED' : 'DISCREPANCY';
  const fifoStatus = fifoReconciled && fifoVariance < 0.01 ? 'RECONCILED' : 'VARIANCE';

  let summary = 'Platform integrity verified: General Ledger is balanced and FIFO valuation reconciles with GL Account 1200 with zero variance.';
  if (!isHealthy) {
    if (glStatus === 'DISCREPANCY' && fifoStatus === 'VARIANCE') {
      summary = `Reconciliation Warning: GL is unbalanced (Discrepancy: PKR ${glDiscrepancy.toLocaleString()}) and FIFO variance exists (PKR ${fifoVariance.toLocaleString()}).`;
    } else if (glStatus === 'DISCREPANCY') {
      summary = `General Ledger Discrepancy: Total debits do not match total credits (Difference: PKR ${glDiscrepancy.toLocaleString()}).`;
    } else {
      summary = `Inventory Valuation Variance: FIFO active layers do not match GL Account 1200 (Difference: PKR ${fifoVariance.toLocaleString()}).`;
    }
  }

  return { isHealthy, glStatus, fifoStatus, summary };
}

/**
 * Generates properly quoted CSV string from headers and data rows.
 */
export function buildCsvContent(headers: string[], rows: (string | number | boolean)[][]): string {
  const escapeCell = (val: string | number | boolean): string => {
    const s = String(val ?? '');
    if (s.includes('"') || s.includes(',') || s.includes('\n')) {
      return `"${s.replace(/"/g, '""')}"`;
    }
    return s;
  };

  const headerLine = headers.map(escapeCell).join(',');
  const rowLines = rows.map(r => r.map(escapeCell).join(','));
  return [headerLine, ...rowLines].join('\r\n');
}
