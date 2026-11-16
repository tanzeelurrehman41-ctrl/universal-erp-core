/**
 * Universal ERP - Phase 13 Multi-Branch Operations Helpers
 */

export interface BranchDto {
  id: string;
  tenantId: string;
  companyId: string;
  branchCode: string;
  branchName: string;
  branchType: 'headquarters' | 'plant' | 'warehouse_hub' | 'retail_store' | 'sales_office' | string;
  parentBranchId: string | null;
  taxRegistrationNumber: string | null;
  addressLine: string | null;
  city: string;
  provinceOrState: string;
  countryCode: string;
  managerId: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface BranchTreeNode {
  branch: BranchDto;
  children: BranchTreeNode[];
}

/**
 * Builds a hierarchical tree from a flat list of branches.
 */
export function buildBranchTree(branches: BranchDto[], parentId: string | null = null): BranchTreeNode[] {
  return branches
    .filter(b => b.parentBranchId === parentId)
    .map(b => ({
      branch: b,
      children: buildBranchTree(branches, b.id)
    }));
}

/**
 * Resolves the breadcrumb path for a branch (e.g. "HQ > Plant 1 > Hub A").
 */
export function findBranchPath(branches: BranchDto[], branchId: string): string {
  const branchMap = new Map(branches.map(b => [b.id, b]));
  const path: string[] = [];
  let current: BranchDto | undefined = branchMap.get(branchId);

  while (current) {
    path.unshift(current.branchName);
    current = current.parentBranchId ? branchMap.get(current.parentBranchId) : undefined;
  }

  return path.join(' > ');
}

/**
 * Formats branch type into human-readable label.
 */
export function formatBranchType(type: string): string {
  switch (type) {
    case 'headquarters':
      return 'Headquarters (HQ)';
    case 'plant':
      return 'Manufacturing Plant';
    case 'warehouse_hub':
      return 'Warehouse Hub / Depot';
    case 'retail_store':
      return 'Retail Store (POS)';
    case 'sales_office':
      return 'Regional Sales Office';
    default:
      return type.replace(/_/g, ' ').replace(/\b\w/g, l => l.toUpperCase());
  }
}

/**
 * Validates branch alphanumeric code format.
 */
export function validateBranchCode(code: string): boolean {
  if (!code || code.trim().length === 0) return false;
  const regex = /^[A-Z0-9_-]{2,20}$/i;
  return regex.test(code.trim());
}

/**
 * Filters branches based on active status.
 */
export function filterBranchesByStatus(branches: BranchDto[], activeOnly: boolean = true): BranchDto[] {
  return activeOnly ? branches.filter(b => b.isActive) : [...branches];
}
