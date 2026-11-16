import test from 'node:test';
import assert from 'node:assert/strict';
import {
  calculateReciprocalElimination,
  calculateUnrealizedProfit,
  calculateEliminationJournalBalance,
  formatTransferStatus,
  formatEliminationType,
  calculateConsolidatedBalance,
  type EliminationLineDto
} from '../src/consolidationHelpers.ts';

test('calculateReciprocalElimination matches minimum of reciprocal accounts and reports imbalance', () => {
  const balanced = calculateReciprocalElimination(50000, 50000);
  assert.equal(balanced.eliminatedAmount, 50000);
  assert.equal(balanced.imbalance, 0);

  const unbalanced = calculateReciprocalElimination(65000, 50000);
  assert.equal(unbalanced.eliminatedAmount, 50000);
  assert.equal(unbalanced.imbalance, 15000);
});

test('calculateUnrealizedProfit calculates markup embedded in ending inventory', () => {
  // 100 units held with unit cost 200 and transfer price 250 -> profit = 100 * 50 = 5,000
  const profit = calculateUnrealizedProfit(100, 200, 250);
  assert.equal(profit, 5000);

  // No markup -> 0 profit
  assert.equal(calculateUnrealizedProfit(100, 250, 250), 0);

  // 0 units held -> 0 profit
  assert.equal(calculateUnrealizedProfit(0, 200, 250), 0);
});

test('calculateEliminationJournalBalance verifies double-entry balance and net zero variance', () => {
  const balancedLines: EliminationLineDto[] = [
    {
      id: 'l-1',
      consolidationRunId: 'r-1',
      eliminationType: 'intercompany_balance',
      fromCompanyId: 'c-1',
      toCompanyId: 'c-2',
      accountCode: '2200',
      debitAmountPkr: 50000,
      creditAmountPkr: 0,
      description: 'Dr Payable'
    },
    {
      id: 'l-2',
      consolidationRunId: 'r-1',
      eliminationType: 'intercompany_balance',
      fromCompanyId: 'c-1',
      toCompanyId: 'c-2',
      accountCode: '1300',
      debitAmountPkr: 0,
      creditAmountPkr: 50000,
      description: 'Cr Receivable'
    }
  ];

  const res = calculateEliminationJournalBalance(balancedLines);
  assert.equal(res.totalDebits, 50000);
  assert.equal(res.totalCredits, 50000);
  assert.equal(res.isBalanced, true);
  assert.equal(res.netVariance, 0);

  const unbalancedLines: EliminationLineDto[] = [
    ...balancedLines,
    {
      id: 'l-3',
      consolidationRunId: 'r-1',
      eliminationType: 'unrealized_inventory_profit',
      fromCompanyId: 'c-1',
      toCompanyId: 'c-2',
      accountCode: '5000',
      debitAmountPkr: 2000,
      creditAmountPkr: 0,
      description: 'Unbalanced Dr'
    }
  ];

  const unbRes = calculateEliminationJournalBalance(unbalancedLines);
  assert.equal(unbRes.isBalanced, false);
  assert.equal(unbRes.netVariance, 2000);
});

test('formatTransferStatus maps transfer workflow stages', () => {
  assert.equal(formatTransferStatus('draft').label, 'Draft');
  assert.equal(formatTransferStatus('dispatched').label, 'Dispatched');
  assert.equal(formatTransferStatus('in_transit').label, 'In Transit');
  assert.equal(formatTransferStatus('received').label, 'Received');
  assert.equal(formatTransferStatus('reconciled').label, 'Reconciled');
});

test('formatEliminationType produces clean accounting descriptions', () => {
  assert.equal(formatEliminationType('intercompany_balance'), 'Reciprocal Balance Elimination (AR / AP)');
  assert.equal(formatEliminationType('intercompany_revenue'), 'Intercompany Revenue & COGS Elimination');
  assert.equal(formatEliminationType('unrealized_inventory_profit'), 'Unrealized Inventory Profit Elimination');
});

test('calculateConsolidatedBalance merges individual companies and eliminations', () => {
  const companyBalances = {
    'c-1': 100000,
    'c-2': 150000
  };
  // Elim Dr 20,000, Elim Cr 50,000 -> Net elim = -30,000
  // Consolidated = 250,000 - 30,000 = 220,000
  const consolidated = calculateConsolidatedBalance(companyBalances, 20000, 50000, 'asset');
  assert.equal(consolidated, 220000);
});
