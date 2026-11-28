import test from 'node:test';
import assert from 'node:assert/strict';
import {
  calculateVariance,
  evaluateKpiStatus,
  formatKpiValue,
  calculateGrossProfitMargin,
  calculateNetProfitMargin,
  calculateDso,
  calculateDpo,
  calculateInventoryTurnover,
  evaluatePlatformHealth,
  buildCsvContent,
} from '../src/analyticsHelpers.ts';

test('calculateVariance computes absolute variance and percentage correctly', () => {
  const res1 = calculateVariance(1050000, 1000000);
  assert.equal(res1.variance, 50000);
  assert.equal(res1.variancePercentage, 5.0);

  const res2 = calculateVariance(90000, 100000);
  assert.equal(res2.variance, -10000);
  assert.equal(res2.variancePercentage, -10.0);

  const resZero = calculateVariance(500, 0);
  assert.equal(resZero.variance, 500);
  assert.equal(resZero.variancePercentage, 0);
});

test('evaluateKpiStatus evaluates standard KPIs where higher is better', () => {
  assert.equal(evaluateKpiStatus(105, 100, true), 'on_track');
  assert.equal(evaluateKpiStatus(100, 100, true), 'on_track');
  assert.equal(evaluateKpiStatus(95, 100, true), 'warning');
  assert.equal(evaluateKpiStatus(85, 100, true), 'critical');
});

test('evaluateKpiStatus evaluates inverse KPIs where lower is better (e.g. Scrap Rate)', () => {
  assert.equal(evaluateKpiStatus(2, 3, false), 'on_track');   // Actual 2% < Target 3% -> on track
  assert.equal(evaluateKpiStatus(3.2, 3, false), 'warning'); // Within 10% overrun -> warning
  assert.equal(evaluateKpiStatus(4.5, 3, false), 'critical'); // > 10% overrun -> critical
});

test('formatKpiValue formats according to unit specifications', () => {
  assert.equal(formatKpiValue(1250000, 'PKR'), 'PKR 1,250,000');
  assert.equal(formatKpiValue(34.56, '%'), '34.6%');
  assert.equal(formatKpiValue(45.2, 'days'), '45.2 days');
  assert.equal(formatKpiValue(6.75, 'ratio'), '6.75x');
  assert.equal(formatKpiValue(100, 'EA'), '100 EA');
});

test('calculateGrossProfitMargin and calculateNetProfitMargin compute correctly', () => {
  assert.equal(calculateGrossProfitMargin(1000000, 650000), 35.0);
  assert.equal(calculateGrossProfitMargin(0, 0), 0);

  assert.equal(calculateNetProfitMargin(1000000, 180000), 18.0);
  assert.equal(calculateNetProfitMargin(0, 0), 0);
});

test('calculateDso and calculateDpo compute correct turnover days', () => {
  // DSO: AR 100,000 on 1,000,000 credit sales = 36.5 days
  assert.equal(calculateDso(100000, 1000000, 365), 36.5);
  assert.equal(calculateDso(100000, 0), 0);

  // DPO: AP 150,000 on 900,000 purchases = 60.8 days
  assert.equal(calculateDpo(150000, 900000, 365), 60.8);
  assert.equal(calculateDpo(150000, 0), 0);
});

test('calculateInventoryTurnover computes ratio', () => {
  // COGS 1,200,000 / Avg Inventory 200,000 = 6.0x
  assert.equal(calculateInventoryTurnover(1200000, 200000), 6.0);
  assert.equal(calculateInventoryTurnover(1200000, 0), 0);
});

test('evaluatePlatformHealth validates reconciled zero-variance state', () => {
  const healthy = evaluatePlatformHealth(true, 0, true, 0);
  assert.equal(healthy.isHealthy, true);
  assert.equal(healthy.glStatus, 'BALANCED');
  assert.equal(healthy.fifoStatus, 'RECONCILED');
  assert.match(healthy.summary, /Platform integrity verified/);

  const unbalanced = evaluatePlatformHealth(false, 50, true, 0);
  assert.equal(unbalanced.isHealthy, false);
  assert.equal(unbalanced.glStatus, 'DISCREPANCY');
  assert.match(unbalanced.summary, /General Ledger Discrepancy/);

  const fifoVar = evaluatePlatformHealth(true, 0, false, 100);
  assert.equal(fifoVar.isHealthy, false);
  assert.equal(fifoVar.fifoStatus, 'VARIANCE');
  assert.match(fifoVar.summary, /Inventory Valuation Variance/);
});

test('buildCsvContent escapes commas, quotes, and newlines', () => {
  const headers = ['Code', 'Name', 'Valuation'];
  const rows = [
    ['SKU-01', 'Widget, Standard', 2250],
    ['SKU-02', 'Flange "Heavy" Duty', 5000]
  ];

  const csv = buildCsvContent(headers, rows);
  assert.match(csv, /"Widget, Standard"/);
  assert.match(csv, /"Flange ""Heavy"" Duty"/);
  assert.match(csv, /2250/);
});
