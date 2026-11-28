import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildBranchTree,
  findBranchPath,
  formatBranchType,
  validateBranchCode,
  filterBranchesByStatus,
  type BranchDto
} from '../src/branchHelpers.ts';

const mockBranches: BranchDto[] = [
  {
    id: 'b-hq',
    tenantId: 't-1',
    companyId: 'c-1',
    branchCode: 'HQ-01',
    branchName: 'Corporate Headquarters',
    branchType: 'headquarters',
    parentBranchId: null,
    taxRegistrationNumber: 'TRN-1001',
    addressLine: 'Main Boulevard',
    city: 'Karachi',
    provinceOrState: 'Sindh',
    countryCode: 'PK',
    managerId: null,
    isActive: true,
    createdAt: '2026-11-16T00:00:00Z'
  },
  {
    id: 'b-plant',
    tenantId: 't-1',
    companyId: 'c-1',
    branchCode: 'PLANT-01',
    branchName: 'Korangi Manufacturing Plant',
    branchType: 'plant',
    parentBranchId: 'b-hq',
    taxRegistrationNumber: null,
    addressLine: 'Industrial Area',
    city: 'Karachi',
    provinceOrState: 'Sindh',
    countryCode: 'PK',
    managerId: null,
    isActive: true,
    createdAt: '2026-11-16T00:00:00Z'
  },
  {
    id: 'b-depot',
    tenantId: 't-1',
    companyId: 'c-1',
    branchCode: 'DEPOT-01',
    branchName: 'Hub Regional Depot',
    branchType: 'warehouse_hub',
    parentBranchId: 'b-plant',
    taxRegistrationNumber: null,
    addressLine: 'Highway Hub',
    city: 'Hub',
    provinceOrState: 'Balochistan',
    countryCode: 'PK',
    managerId: null,
    isActive: false,
    createdAt: '2026-11-16T00:00:00Z'
  }
];

test('buildBranchTree correctly constructs multi-level parent-child hierarchy', () => {
  const tree = buildBranchTree(mockBranches);
  assert.equal(tree.length, 1);
  assert.equal(tree[0].branch.id, 'b-hq');
  assert.equal(tree[0].children.length, 1);
  assert.equal(tree[0].children[0].branch.id, 'b-plant');
  assert.equal(tree[0].children[0].children.length, 1);
  assert.equal(tree[0].children[0].children[0].branch.id, 'b-depot');
});

test('findBranchPath builds full breadcrumb hierarchy path', () => {
  const path = findBranchPath(mockBranches, 'b-depot');
  assert.equal(path, 'Corporate Headquarters > Korangi Manufacturing Plant > Hub Regional Depot');
});

test('formatBranchType converts enum codes to human-readable strings', () => {
  assert.equal(formatBranchType('headquarters'), 'Headquarters (HQ)');
  assert.equal(formatBranchType('plant'), 'Manufacturing Plant');
  assert.equal(formatBranchType('warehouse_hub'), 'Warehouse Hub / Depot');
  assert.equal(formatBranchType('retail_store'), 'Retail Store (POS)');
  assert.equal(formatBranchType('sales_office'), 'Regional Sales Office');
});

test('validateBranchCode checks alphanumeric pattern and length', () => {
  assert.equal(validateBranchCode('BR-01'), true);
  assert.equal(validateBranchCode('PLANT_NORTH'), true);
  assert.equal(validateBranchCode(''), false);
  assert.equal(validateBranchCode('A'), false); // Too short
  assert.equal(validateBranchCode('INVALID CODE WITH SPACES'), false);
});

test('filterBranchesByStatus respects active flag filter', () => {
  const activeOnly = filterBranchesByStatus(mockBranches, true);
  assert.equal(activeOnly.length, 2);
  const all = filterBranchesByStatus(mockBranches, false);
  assert.equal(all.length, 3);
});
