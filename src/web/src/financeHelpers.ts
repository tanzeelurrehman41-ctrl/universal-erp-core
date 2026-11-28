export function calculateStraightLineDepreciation(
  cost: number,
  salvage: number,
  usefulLifeMonths: number,
  currentAccumulated: number
): number {
  if (cost <= 0 || usefulLifeMonths <= 0) return 0;
  const depreciableBase = Math.max(0, cost - salvage);
  const remainingDepreciable = Math.max(0, depreciableBase - currentAccumulated);
  if (remainingDepreciable <= 0) return 0;

  const monthlyStraightLine = Math.round((depreciableBase / usefulLifeMonths) * 100) / 100;
  return Math.min(monthlyStraightLine, remainingDepreciable);
}

export function calculateReducingBalanceDepreciation(
  cost: number,
  salvage: number,
  currentAccumulated: number,
  annualRatePercent: number = 20
): number {
  if (cost <= 0) return 0;
  const depreciableBase = Math.max(0, cost - salvage);
  const remainingDepreciable = Math.max(0, depreciableBase - currentAccumulated);
  if (remainingDepreciable <= 0) return 0;

  const currentBookValue = Math.max(salvage, cost - currentAccumulated);
  const monthlyRate = annualRatePercent / 100 / 12;
  const monthlyDepr = Math.round(currentBookValue * monthlyRate * 100) / 100;
  return Math.min(monthlyDepr, remainingDepreciable);
}

export function calculateReconciliationDiscrepancy(
  statementClosingBalance: number,
  glBookBalance: number
): { discrepancy: number; isBalanced: boolean } {
  const discrepancy = Math.round((statementClosingBalance - glBookBalance) * 100) / 100;
  return {
    discrepancy,
    isBalanced: Math.abs(discrepancy) < 0.01,
  };
}

export interface TrialBalanceItem {
  accountType: string;
  accountCode: string;
  netBalance: number;
}

export interface FinancialStatementSummary {
  totalAssets: number;
  totalLiabilities: number;
  totalEquity: number;
  totalRevenue: number;
  totalExpenses: number;
  netIncome: number;
  isBalanced: boolean;
  discrepancy: number;
}

export function computeFinancialStatementSummary(rows: TrialBalanceItem[]): FinancialStatementSummary {
  let totalAssets = 0;
  let totalLiabilities = 0;
  let totalEquity = 0;
  let totalRevenue = 0;
  let totalExpenses = 0;

  for (const row of rows) {
    const type = row.accountType.toLowerCase();
    const balance = row.netBalance;
    if (type === "asset") {
      totalAssets += balance;
    } else if (type === "liability") {
      totalLiabilities += balance;
    } else if (type === "equity") {
      totalEquity += balance;
    } else if (type === "revenue") {
      totalRevenue += balance;
    } else if (type === "expense") {
      totalExpenses += balance;
    }
  }

  const netIncome = totalRevenue - totalExpenses;
  const totalLiabAndEquity = totalLiabilities + totalEquity + netIncome;
  const discrepancy = Math.round((totalAssets - totalLiabAndEquity) * 100) / 100;
  const isBalanced = Math.abs(discrepancy) < 0.01;

  return {
    totalAssets: Math.round(totalAssets * 100) / 100,
    totalLiabilities: Math.round(totalLiabilities * 100) / 100,
    totalEquity: Math.round(totalEquity * 100) / 100,
    totalRevenue: Math.round(totalRevenue * 100) / 100,
    totalExpenses: Math.round(totalExpenses * 100) / 100,
    netIncome: Math.round(netIncome * 100) / 100,
    isBalanced,
    discrepancy,
  };
}

export function formatMoney(amount: number | null | undefined, currencyCode: string = "PKR"): string {
  if (amount === null || amount === undefined || Number.isNaN(Number(amount))) {
    return "Unavailable";
  }
  const normalized = (currencyCode || "PKR").trim().toUpperCase();
  return `${normalized} ${Number(amount).toLocaleString("en-US", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
}

export function formatCurrencyPkr(amount: number): string {
  return "PKR " + Number(amount || 0).toLocaleString("en-PK", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}
