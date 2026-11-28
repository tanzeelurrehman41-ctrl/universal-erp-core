/**
 * Universal ERP - Phase 13 Intercompany & Group Consolidation Helpers
 */

export interface EliminationLineDto {
  id: string;
  consolidationRunId: string;
  eliminationType: 'intercompany_balance' | 'intercompany_revenue' | 'unrealized_inventory_profit' | string;
  fromCompanyId: string;
  toCompanyId: string;
  accountCode: string;
  debitAmountPkr: number;
  creditAmountPkr: number;
  description: string;
}

/**
 * Calculates reciprocal Due-To / Due-From elimination amount and any discrepancy.
 */
export function calculateReciprocalElimination(dueFrom: number, dueTo: number): { eliminatedAmount: number; imbalance: number } {
  const eliminatedAmount = Math.min(Math.max(dueFrom, 0), Math.max(dueTo, 0));
  const imbalance = Math.abs(dueFrom - dueTo);
  return { eliminatedAmount, imbalance };
}

/**
 * Calculates unrealized markup profit on intercompany ending inventory held.
 */
export function calculateUnrealizedProfit(qtyHeld: number, unitCost: number, unitTransferPrice: number): number {
  if (qtyHeld <= 0 || unitTransferPrice <= unitCost) return 0;
  const unitProfit = unitTransferPrice - unitCost;
  return Math.round(qtyHeld * unitProfit * 100) / 100;
}

/**
 * Checks double-entry balance of elimination lines.
 */
export function calculateEliminationJournalBalance(lines: EliminationLineDto[]): {
  totalDebits: number;
  totalCredits: number;
  isBalanced: boolean;
  netVariance: number;
} {
  const totalDebits = Math.round(lines.reduce((sum, l) => sum + (l.debitAmountPkr || 0), 0) * 100) / 100;
  const totalCredits = Math.round(lines.reduce((sum, l) => sum + (l.creditAmountPkr || 0), 0) * 100) / 100;
  const netVariance = Math.round(Math.abs(totalDebits - totalCredits) * 100) / 100;
  const isBalanced = netVariance < 0.01;

  return { totalDebits, totalCredits, isBalanced, netVariance };
}

/**
 * Formats transfer status with label and styling.
 */
export function formatTransferStatus(status: string): { label: string; badgeClass: string } {
  switch (status.toLowerCase()) {
    case 'draft':
      return { label: 'Draft', badgeClass: 'badge-draft' };
    case 'dispatched':
      return { label: 'Dispatched', badgeClass: 'badge-dispatched' };
    case 'in_transit':
      return { label: 'In Transit', badgeClass: 'badge-transit' };
    case 'received':
      return { label: 'Received', badgeClass: 'badge-received' };
    case 'reconciled':
      return { label: 'Reconciled', badgeClass: 'badge-reconciled' };
    case 'cancelled':
      return { label: 'Cancelled', badgeClass: 'badge-cancelled' };
    default:
      return { label: status, badgeClass: 'badge-default' };
  }
}

/**
 * Formats elimination type into clean audit description.
 */
export function formatEliminationType(type: string): string {
  switch (type) {
    case 'intercompany_balance':
      return 'Reciprocal Balance Elimination (AR / AP)';
    case 'intercompany_revenue':
      return 'Intercompany Revenue & COGS Elimination';
    case 'unrealized_inventory_profit':
      return 'Unrealized Inventory Profit Elimination';
    default:
      return type.replace(/_/g, ' ').replace(/\b\w/g, l => l.toUpperCase());
  }
}

/**
 * Computes consolidated line balance given individual company balances and elimination debits/credits.
 */
export function calculateConsolidatedBalance(
  companyBalances: Record<string, number>,
  elimDebits: number,
  elimCredits: number,
  accountType: string
): number {
  const totalRaw = Object.values(companyBalances).reduce((sum, v) => sum + v, 0);
  const netElim = elimDebits - elimCredits;
  return Math.round((totalRaw + netElim) * 100) / 100;
}
