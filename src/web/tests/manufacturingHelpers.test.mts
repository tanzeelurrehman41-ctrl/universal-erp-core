import test from 'node:test';
import assert from 'node:assert/strict';
import {
  explodeBomRequirements,
  calculateWorkOrderCosting,
  validateWorkOrderProduction,
  formatCurrencyPkr,
  type BomItemInput,
} from '../src/manufacturingHelpers.ts';

test('explodeBomRequirements calculates gross quantities with scrap and yield factors', () => {
  const items: BomItemInput[] = [
    {
      componentVariantId: 'raw-1',
      componentName: 'Steel Rod 10mm',
      componentType: 'raw_material',
      quantityPer: 2,
      scrapPercentage: 10, // 10% scrap allowance -> factor 1.10
    },
    {
      componentVariantId: 'raw-2',
      componentName: 'Rubber O-Ring',
      componentType: 'raw_material',
      quantityPer: 4,
      scrapPercentage: 0,
    },
  ];

  // 10 units ordered, baseQty 1, yield 100%
  const exploded = explodeBomRequirements(10, 1, 100, items);

  assert.equal(exploded.length, 2);

  const steel = exploded.find((e) => e.componentVariantId === 'raw-1');
  assert.ok(steel);
  // 10 * 2 * 1.10 = 22.0
  assert.equal(steel.grossRequiredQuantity, 22);

  const ring = exploded.find((e) => e.componentVariantId === 'raw-2');
  assert.ok(ring);
  // 10 * 4 = 40.0
  assert.equal(ring.grossRequiredQuantity, 40);
});

test('calculateWorkOrderCosting derives unit cost, finished goods valuation, and scrap expense', () => {
  // Material 3,000 PKR, Labor 1,500 PKR, Overhead 500 PKR -> Total 5,000 PKR
  // Accepted: 9 units, Scrapped: 1 unit -> Total: 10 units
  // Unit cost = 5,000 / 10 = 500 PKR
  const costing = calculateWorkOrderCosting(3000, 1500, 500, 9, 1);

  assert.equal(costing.totalMaterialCost, 3000);
  assert.equal(costing.totalLaborCost, 1500);
  assert.equal(costing.totalOverheadCost, 500);
  assert.equal(costing.totalProductionCost, 5000);
  assert.equal(costing.unitCostFinal, 500);
  assert.equal(costing.finishedGoodsValuation, 4500); // 9 * 500
  assert.equal(costing.scrapExpense, 500); // 1 * 500
  assert.equal(costing.costVariance, 0);
});

test('validateWorkOrderProduction blocks overproduction exceeding tolerance', () => {
  // Planned: 10 units, current: 0 units, tolerance: 10% -> Max allowed: 11 units

  // Valid: 10 units
  const valid = validateWorkOrderProduction(10, 0, 10, 0, 10);
  assert.equal(valid.isValid, true);

  // Valid: 11 units (exactly 10% over)
  const exactMax = validateWorkOrderProduction(10, 0, 11, 0, 10);
  assert.equal(exactMax.isValid, true);

  // Invalid: 12 units (exceeds 10% over)
  const exceeded = validateWorkOrderProduction(10, 0, 12, 0, 10);
  assert.equal(exceeded.isValid, false);
  assert.ok(exceeded.error?.includes('OVERPRODUCTION_LIMIT_EXCEEDED'));

  // Invalid: 0 units
  const zero = validateWorkOrderProduction(10, 0, 0, 0, 10);
  assert.equal(zero.isValid, false);
  assert.equal(zero.error, 'ZERO_COMPLETION_QUANTITY');
});

test('formatCurrencyPkr formats currency properly', () => {
  const formatted = formatCurrencyPkr(1250.5);
  assert.ok(formatted.includes('1,250.50'));
});
