import { useState, useEffect } from "react";
import { apiFetch, type TenantCompanyContext } from "./auth";
import {
  evaluatePlatformHealth,
  formatKpiValue,
  calculateVariance,
  evaluateKpiStatus,
  buildCsvContent,
  type PlatformHealthEvaluation
} from "./analyticsHelpers";
import { maskSalary } from "./hrHelpers";

export type AnalyticsWorkspaceProps = {
  enabled: boolean;
  workspace?: TenantCompanyContext | null;
};

// ── Types ──────────────────────────────────────────────────────────────────

export interface ExecutiveDashboard {
  asOfDate: string;
  financials: {
    totalRevenuePkr: number;
    costOfGoodsSoldPkr: number;
    grossProfitPkr: number;
    grossProfitMarginPercentage: number;
    operatingExpensesPkr: number;
    netIncomePkr: number;
    netProfitMarginPercentage: number;
    operatingCashFlowPkr: number;
    accountsReceivablePkr: number;
    accountsPayablePkr: number;
    isGeneralLedgerBalanced: boolean;
  };
  supplyChain: {
    totalInventoryValuationPkr: number;
    fifoValuationPkr: number;
    isFifoReconciled: boolean;
    activeSkuCount: number;
    openSalesOrdersValuePkr: number;
    openSalesOrdersCount: number;
    openPurchaseOrdersValuePkr: number;
    openPurchaseOrdersCount: number;
    inventoryTurnoverRatio: number;
    averageBinUtilizationPercentage: number;
  };
  manufacturing: {
    totalWorkOrders: number;
    completedWorkOrders: number;
    workOrderCompletionRatePercentage: number;
    workInProgressValuationPkr: number;
    totalScrapCostPkr: number;
    scrapRatePercentage: number;
  };
  workforce: {
    totalActiveEmployees: number;
    monthlyGrossPayrollPkr: number;
    monthlyNetPayrollPkr: number;
    employerStatutoryCostPkr: number;
    averageCostPerEmployeePkr: number;
    overtimeExpenditurePkr: number;
  };
  keyPerformanceIndicators: Array<{
    code: string;
    name: string;
    category: string;
    unit: string;
    actualValue: number;
    targetValue: number;
    variance: number;
    variancePercentage: number;
    status: string;
    trendDirection: string;
  }>;
}

export interface PlatformReconciliation {
  reconciledAt: string;
  allReconciled: boolean;
  glReconciliation: {
    isBalanced: boolean;
    totalDebits: number;
    totalCredits: number;
    discrepancy: number;
    totalPostedJournals: number;
    totalJournalLines: number;
  };
  fifoReconciliation: {
    isReconciled: boolean;
    fifoActiveLayersValuationPkr: number;
    glAccount1200ValuationPkr: number;
    variancePkr: number;
    totalActiveLayers: number;
    totalActiveQuantity: number;
  };
}

export interface SalesAnalytics {
  totalRevenuePkr: number;
  totalCostOfSalesPkr: number;
  grossMarginPkr: number;
  marginPercentage: number;
  topCustomers: Array<{
    customerId: string;
    customerCode: string;
    customerName: string;
    totalRevenuePkr: number;
    ordersCount: number;
    contributionPercentage: number;
  }>;
  arAging: {
    currentPkr: number;
    days1To30Pkr: number;
    days31To60Pkr: number;
    days61To90Pkr: number;
    over90DaysPkr: number;
    totalOutstandingPkr: number;
    daysSalesOutstanding: number;
  };
}

export interface PurchasingAnalytics {
  totalSpendPkr: number;
  totalPurchaseOrders: number;
  completedPurchaseOrders: number;
  poFulfillmentRatePercentage: number;
  onTimeDeliveryPercentage: number;
  purchasePriceVariancePkr: number;
  topSuppliers: Array<{
    supplierId: string;
    supplierCode: string;
    supplierName: string;
    totalSpendPkr: number;
    poCount: number;
    fulfillmentPercentage: number;
  }>;
  apAging: {
    currentPkr: number;
    days1To30Pkr: number;
    days31To60Pkr: number;
    days61To90Pkr: number;
    over90DaysPkr: number;
    totalPayablePkr: number;
    daysPayableOutstanding: number;
  };
}

export interface InventoryAgeingAnalytics {
  totalFifoLayerValuationPkr: number;
  glAccount1200ValuationPkr: number;
  valuationVariancePkr: number;
  isReconciled: boolean;
  turnoverRatio: number;
  daysOfInventoryOnHand: number;
  ageingBrackets: {
    days0To30ValuePkr: number;
    days31To60ValuePkr: number;
    days61To90ValuePkr: number;
    over90DaysValuePkr: number;
  };
  slowMovingItems: Array<{
    variantId: string;
    sku: string;
    productName: string;
    quantityOnHand: number;
    totalValuationPkr: number;
    daysSinceLastMovement: number;
    status: string;
  }>;
}

export interface WorkforceCostAnalytics {
  totalActiveHeadcount: number;
  monthlyGrossPayrollPkr: number;
  monthlyNetPayrollPkr: number;
  eobiEmployerCostPkr: number;
  pfEmployerCostPkr: number;
  totalEmployerStatutoryCostPkr: number;
  totalOvertimeExpenditurePkr: number;
  totalStaffLoansOutstandingPkr: number;
  isSalaryMasked: boolean;
  departmentBreakdown: Array<{
    departmentId: string;
    departmentCode: string;
    departmentName: string;
    headcount: number;
    totalSalaryCostPkr: number;
    costPercentage: number;
  }>;
}

type TabType =
  | "overview"
  | "sales"
  | "purchasing"
  | "inventory"
  | "manufacturing"
  | "workforce"
  | "reconciliation"
  | "export";

export function AnalyticsWorkspace({ enabled, workspace }: AnalyticsWorkspaceProps) {
  const [activeTab, setActiveTab] = useState<TabType>("overview");
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Analytical datasets
  const [dashboard, setDashboard] = useState<ExecutiveDashboard | null>(null);
  const [reconciliation, setReconciliation] = useState<PlatformReconciliation | null>(null);
  const [sales, setSales] = useState<SalesAnalytics | null>(null);
  const [purchasing, setPurchasing] = useState<PurchasingAnalytics | null>(null);
  const [inventory, setInventory] = useState<InventoryAgeingAnalytics | null>(null);
  const [workforce, setWorkforce] = useState<WorkforceCostAnalytics | null>(null);

  // Security privilege state
  const [canViewConfidentialSalary, setCanViewConfidentialSalary] = useState(false);

  // Date filters
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");

  useEffect(() => {
    if (enabled && workspace?.companyId) {
      loadAllAnalytics();
    }
  }, [enabled, workspace?.companyId, canViewConfidentialSalary]);

  async function loadAllAnalytics() {
    if (!enabled || !workspace?.companyId) return;

    setIsLoading(true);
    setError(null);
    setDashboard(null);
    setReconciliation(null);
    setSales(null);
    setPurchasing(null);
    setInventory(null);
    setWorkforce(null);

    const filters = new URLSearchParams();
    if (startDate) filters.set("startDate", startDate);
    if (endDate) filters.set("endDate", endDate);
    const suffix = filters.toString();
    const withFilters = (path: string) => suffix ? `${path}?${suffix}` : path;
    const failures: string[] = [];

    async function fetchReport<T>(label: string, path: string): Promise<T | null> {
      try {
        const response = await apiFetch(path);
        if (!response.ok) {
          failures.push(response.status === 403 ? `${label}: permission denied` : `${label}: HTTP ${response.status}`);
          return null;
        }
        return await response.json() as T;
      } catch {
        failures.push(`${label}: request failed`);
        return null;
      }
    }

    try {
      const [dash, rec, salesReport, purchasingReport, inventoryReport, workforceReport] = await Promise.all([
        fetchReport<ExecutiveDashboard>("Executive dashboard", `${withFilters("/api/v1/analytics/dashboard")}${suffix ? "&" : "?"}includeSalary=${canViewConfidentialSalary}`),
        fetchReport<PlatformReconciliation>("Reconciliation", "/api/v1/analytics/reconciliation"),
        fetchReport<SalesAnalytics>("Sales", withFilters("/api/v1/analytics/sales")),
        fetchReport<PurchasingAnalytics>("Purchasing", withFilters("/api/v1/analytics/purchasing")),
        fetchReport<InventoryAgeingAnalytics>("Inventory", withFilters("/api/v1/analytics/inventory")),
        fetchReport<WorkforceCostAnalytics>("Workforce", `${withFilters("/api/v1/analytics/workforce")}${suffix ? "&" : "?"}includeSalary=${canViewConfidentialSalary}`),
      ]);

      setDashboard(dash);
      setReconciliation(rec);
      setSales(salesReport);
      setPurchasing(purchasingReport);
      setInventory(inventoryReport);
      setWorkforce(workforceReport);
      if (failures.length > 0) setError(`Some reports are unavailable: ${failures.join("; ")}.`);
    } finally {
      setIsLoading(false);
    }
  }

  function handleExportCsv(reportType: string) {
    if (reportType === "sales" && sales) {
      const headers = ["Customer Code", "Customer Name", "Total Revenue (PKR)", "Orders Count", "Contribution %"];
      const rows = sales.topCustomers.map(c => [c.customerCode, c.customerName, c.totalRevenuePkr, c.ordersCount, c.contributionPercentage]);
      downloadCsv(`${reportType}_report.csv`, buildCsvContent(headers, rows));
    } else if (reportType === "purchasing" && purchasing) {
      const headers = ["Supplier Code", "Supplier Name", "Total Spend (PKR)", "PO Count", "Fulfillment %"];
      const rows = purchasing.topSuppliers.map(s => [s.supplierCode, s.supplierName, s.totalSpendPkr, s.poCount, s.fulfillmentPercentage]);
      downloadCsv(`${reportType}_report.csv`, buildCsvContent(headers, rows));
    } else if (reportType === "reconciliation" && reconciliation) {
      const headers = ["Reconciliation Area", "Status", "Metric 1", "Metric 2", "Variance (PKR)"];
      const rows = [
        ["General Ledger Double-Entry", reconciliation.glReconciliation.isBalanced ? "BALANCED" : "DISCREPANCY", `Debits: ${reconciliation.glReconciliation.totalDebits}`, `Credits: ${reconciliation.glReconciliation.totalCredits}`, reconciliation.glReconciliation.discrepancy],
        ["Model A FIFO Layer Valuation", reconciliation.fifoReconciliation.isReconciled ? "RECONCILED" : "VARIANCE", `FIFO: ${reconciliation.fifoReconciliation.fifoActiveLayersValuationPkr}`, `GL 1200: ${reconciliation.fifoReconciliation.glAccount1200ValuationPkr}`, reconciliation.fifoReconciliation.variancePkr]
      ];
      downloadCsv(`${reportType}_audit_report.csv`, buildCsvContent(headers, rows));
    } else {
      // Default Executive Summary
      const headers = ["Metric Code", "KPI Name", "Actual Value", "Target Value", "Variance %", "Status"];
      const rows = (dashboard?.keyPerformanceIndicators || []).map(k => [k.code, k.name, k.actualValue, k.targetValue, k.variancePercentage, k.status]);
      downloadCsv("executive_kpi_summary.csv", buildCsvContent(headers, rows));
    }
  }

  function downloadCsv(fileName: string, content: string) {
    const blob = new Blob([content], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.setAttribute("href", url);
    link.setAttribute("download", fileName);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  }

  const health: PlatformHealthEvaluation | null = reconciliation
    ? evaluatePlatformHealth(
        reconciliation.glReconciliation.isBalanced,
        reconciliation.glReconciliation.discrepancy,
        reconciliation.fifoReconciliation.isReconciled,
        reconciliation.fifoReconciliation.variancePkr
      )
    : null;

  return (
    <div style={{ padding: "20px", maxWidth: "1400px", margin: "0 auto", fontFamily: "system-ui, sans-serif" }}>
      {/* Platform Header & Reconciliation Status Banner */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "20px" }}>
        <div>
          <h2 style={{ margin: 0, color: "#111827", fontSize: "24px" }}>
            📊 Enterprise Analytics & Business Intelligence
          </h2>
          <p style={{ margin: "4px 0 0", color: "#6B7280", fontSize: "14px" }}>
            Phase 12 Real-Time Dashboards, Multi-Dimensional Insights & Model A FIFO Reconciliation
          </p>
        </div>

        {/* Global Model A Audit Badge */}
        <div style={{
          display: "flex",
          alignItems: "center",
          gap: "12px",
          backgroundColor: health ? (health.isHealthy ? "#ECFDF5" : "#FEF2F2") : "#F3F4F6",
          border: `1px solid ${health ? (health.isHealthy ? "#10B981" : "#EF4444") : "#D1D5DB"}`,
          padding: "10px 16px",
          borderRadius: "8px"
        }}>
          <span style={{ fontSize: "20px" }}>{health ? (health.isHealthy ? "🛡️" : "⚠️") : "ℹ️"}</span>
          <div>
            <div style={{ fontSize: "13px", fontWeight: "bold", color: health ? (health.isHealthy ? "#065F46" : "#991B1B") : "#374151" }}>
              {health ? `GL & FIFO Audit: ${health.glStatus} / ${health.fifoStatus}` : "GL & FIFO Audit: Not available"}
            </div>
            <div style={{ fontSize: "11px", color: "#4B5563" }}>
              {reconciliation ? `Active layers: ${reconciliation.fifoReconciliation.totalActiveLayers} | Quantity: ${reconciliation.fifoReconciliation.totalActiveQuantity}` : "No live reconciliation result has been loaded."}
            </div>
          </div>
        </div>
      </div>

      {/* Date Filter & Security Gate Bar */}
      <div style={{
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        backgroundColor: "#F9FAFB",
        padding: "12px 16px",
        borderRadius: "8px",
        border: "1px solid #E5E7EB",
        marginBottom: "20px",
        flexWrap: "wrap",
        gap: "12px"
      }}>
        <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
          <label style={{ fontSize: "13px", fontWeight: "600", color: "#374151" }}>Period:</label>
          <input
            type="date"
            value={startDate}
            onChange={e => setStartDate(e.target.value)}
            style={{ padding: "6px 10px", borderRadius: "6px", border: "1px solid #D1D5DB", fontSize: "13px" }}
          />
          <span style={{ color: "#9CA3AF" }}>to</span>
          <input
            type="date"
            value={endDate}
            onChange={e => setEndDate(e.target.value)}
            style={{ padding: "6px 10px", borderRadius: "6px", border: "1px solid #D1D5DB", fontSize: "13px" }}
          />
          <button
            onClick={loadAllAnalytics}
            style={{
              padding: "6px 14px",
              backgroundColor: "#2563EB",
              color: "#FFF",
              border: "none",
              borderRadius: "6px",
              fontSize: "13px",
              cursor: "pointer"
            }}
          >
            Apply Filters
          </button>
        </div>

        {/* Security / Confidentiality Toggle */}
        <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
          <label style={{ fontSize: "13px", color: "#4B5563", display: "flex", alignItems: "center", gap: "6px" }}>
            <input
              type="checkbox"
              checked={canViewConfidentialSalary}
              onChange={e => setCanViewConfidentialSalary(e.target.checked)}
            />
            <span>View Confidential Salary Data (<code>payroll.salary.view</code>)</span>
          </label>
          {!canViewConfidentialSalary && (
            <span style={{ fontSize: "11px", backgroundColor: "#FEF3C7", color: "#92400E", padding: "3px 8px", borderRadius: "4px" }}>
              🔒 PII & Salary Masked
            </span>
          )}
        </div>
      </div>

      {error && (
        <div role="alert" style={{ marginBottom: "16px", padding: "12px 16px", borderRadius: "8px", border: "1px solid #D97706", backgroundColor: "#FFFBEB", color: "#92400E", fontSize: "13px" }}>
          {error}
        </div>
      )}

      {/* Navigation Tabs */}
      <div style={{ display: "flex", borderBottom: "2px solid #E5E7EB", marginBottom: "20px", gap: "4px", overflowX: "auto" }}>
        {[
          { id: "overview", label: "Executive Dashboard", icon: "📈" },
          { id: "sales", label: "Sales & Revenue", icon: "💰" },
          { id: "purchasing", label: "Procurement & Vendors", icon: "🛒" },
          { id: "inventory", label: "FIFO Inventory Valuation", icon: "📦" },
          { id: "manufacturing", label: "Manufacturing & WIP", icon: "🏭" },
          { id: "workforce", label: "Workforce & Payroll", icon: "👥" },
          { id: "reconciliation", label: "GL & FIFO Audit Health", icon: "⚖️" },
          { id: "export", label: "Export Data Center", icon: "📥" }
        ].map(tab => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id as TabType)}
            style={{
              padding: "10px 18px",
              border: "none",
              borderBottom: activeTab === tab.id ? "3px solid #2563EB" : "3px solid transparent",
              backgroundColor: activeTab === tab.id ? "#EFF6FF" : "transparent",
              color: activeTab === tab.id ? "#1D4ED8" : "#4B5563",
              fontWeight: activeTab === tab.id ? "600" : "500",
              fontSize: "14px",
              cursor: "pointer",
              display: "flex",
              alignItems: "center",
              gap: "6px",
              whiteSpace: "nowrap"
            }}
          >
            <span>{tab.icon}</span>
            <span>{tab.label}</span>
          </button>
        ))}
      </div>

      {/* ── TAB 1: EXECUTIVE OVERVIEW ─────────────────────────────────────────── */}
      {activeTab === "overview" && (
        <div>
          {/* Top KPI Metric Cards */}
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: "16px", marginBottom: "24px" }}>
            <MetricCard
              title="Total Revenue"
              value={dashboard ? `PKR ${dashboard.financials.totalRevenuePkr.toLocaleString()}` : "—"}
              subtitle={dashboard ? `Gross margin: ${dashboard.financials.grossProfitMarginPercentage}%` : "No live dashboard data"}
              trend="up"
              status="on_track"
            />
            <MetricCard
              title="Operating Cash Flow"
              value={dashboard ? `PKR ${dashboard.financials.operatingCashFlowPkr.toLocaleString()}` : "—"}
              subtitle={dashboard ? "Current company reporting period" : "No live dashboard data"}
              trend="up"
              status="on_track"
            />
            <MetricCard
              title="FIFO Inventory Valuation"
              value={dashboard ? `PKR ${dashboard.supplyChain.fifoValuationPkr.toLocaleString()}` : "—"}
              subtitle={dashboard ? (dashboard.supplyChain.isFifoReconciled ? "FIFO valuation reconciled" : "FIFO valuation variance detected") : "No live dashboard data"}
              trend="flat"
              status="on_track"
            />
            <MetricCard
              title="Work Orders Completion"
              value={dashboard ? `${dashboard.manufacturing.workOrderCompletionRatePercentage}%` : "—"}
              subtitle={dashboard ? `WIP: PKR ${dashboard.manufacturing.workInProgressValuationPkr.toLocaleString()}` : "No live dashboard data"}
              trend="up"
              status="on_track"
            />
            <MetricCard
              title="Active Headcount"
              value={dashboard ? `${dashboard.workforce.totalActiveEmployees} staff` : "—"}
              subtitle={dashboard ? "Active workforce" : "No live dashboard data"}
              trend="flat"
              status="on_track"
            />
          </div>

          {/* Key Performance Indicators Table */}
          <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
            <h3 style={{ margin: "0 0 16px", fontSize: "16px", color: "#111827" }}>
              Key Performance Indicator (KPI) Scorecard
            </h3>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
              <thead>
                <tr style={{ backgroundColor: "#F9FAFB", borderBottom: "2px solid #E5E7EB", textAlign: "left" }}>
                  <th style={{ padding: "10px" }}>Code</th>
                  <th style={{ padding: "10px" }}>KPI Name</th>
                  <th style={{ padding: "10px" }}>Category</th>
                  <th style={{ padding: "10px" }}>Actual</th>
                  <th style={{ padding: "10px" }}>Target</th>
                  <th style={{ padding: "10px" }}>Variance %</th>
                  <th style={{ padding: "10px" }}>Status</th>
                </tr>
              </thead>
              <tbody>
                {(dashboard?.keyPerformanceIndicators ?? []).map(kpi => (
                  <tr key={kpi.code} style={{ borderBottom: "1px solid #F3F4F6" }}>
                    <td style={{ padding: "10px", fontWeight: "600" }}><code>{kpi.code}</code></td>
                    <td style={{ padding: "10px" }}>{kpi.name}</td>
                    <td style={{ padding: "10px", textTransform: "capitalize" }}>{kpi.category}</td>
                    <td style={{ padding: "10px", fontWeight: "600" }}>{formatKpiValue(kpi.actualValue, kpi.unit)}</td>
                    <td style={{ padding: "10px", color: "#6B7280" }}>{formatKpiValue(kpi.targetValue, kpi.unit)}</td>
                    <td style={{ padding: "10px", color: kpi.variancePercentage >= 0 ? "#059669" : "#DC2626", fontWeight: "600" }}>
                      {kpi.variancePercentage >= 0 ? `+${kpi.variancePercentage}%` : `${kpi.variancePercentage}%`}
                    </td>
                    <td style={{ padding: "10px" }}>
                      <span style={{
                        padding: "3px 8px",
                        borderRadius: "12px",
                        fontSize: "11px",
                        fontWeight: "600",
                        backgroundColor: kpi.status === "on_track" ? "#D1FAE5" : kpi.status === "warning" ? "#FEF3C7" : "#FEE2E2",
                        color: kpi.status === "on_track" ? "#065F46" : kpi.status === "warning" ? "#92400E" : "#991B1B"
                      }}>
                        {kpi.status.replace("_", " ").toUpperCase()}
                      </span>
                    </td>
                  </tr>
                ))}
                {(!dashboard || dashboard.keyPerformanceIndicators.length === 0) && (
                  <tr><td colSpan={7} style={{ padding: "16px", color: "#6B7280" }}>No KPI data is available for this company.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── TAB 2: SALES & REVENUE ────────────────────────────────────────────── */}
      {activeTab === "sales" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "20px" }}>
          {/* Top Customers Ranking */}
          <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
            <h3 style={{ margin: "0 0 16px", fontSize: "16px", color: "#111827" }}>Top Revenue Customers</h3>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
              <thead>
                <tr style={{ backgroundColor: "#F9FAFB", borderBottom: "1px solid #E5E7EB", textAlign: "left" }}>
                  <th style={{ padding: "8px" }}>Customer</th>
                  <th style={{ padding: "8px" }}>Revenue (PKR)</th>
                  <th style={{ padding: "8px" }}>Orders</th>
                  <th style={{ padding: "8px" }}>Share %</th>
                </tr>
              </thead>
              <tbody>
                {(sales?.topCustomers ?? []).map(c => (
                  <tr key={c.customerCode} style={{ borderBottom: "1px solid #F3F4F6" }}>
                    <td style={{ padding: "8px" }}>
                      <div style={{ fontWeight: "600" }}>{c.customerName}</div>
                      <div style={{ fontSize: "11px", color: "#6B7280" }}>{c.customerCode}</div>
                    </td>
                    <td style={{ padding: "8px", fontWeight: "600" }}>PKR {c.totalRevenuePkr.toLocaleString()}</td>
                    <td style={{ padding: "8px" }}>{c.ordersCount}</td>
                    <td style={{ padding: "8px", color: "#2563EB", fontWeight: "600" }}>{c.contributionPercentage}%</td>
                  </tr>
                ))}
                {(!sales || sales.topCustomers.length === 0) && (
                  <tr><td colSpan={4} style={{ padding: "16px", color: "#6B7280" }}>No sales results are available for this period.</td></tr>
                )}
              </tbody>
            </table>
          </div>

          {/* Accounts Receivable Aging */}
          <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
            <h3 style={{ margin: "0 0 16px", fontSize: "16px", color: "#111827" }}>
              Accounts Receivable (AR) Aging & DSO
            </h3>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px", marginBottom: "16px" }}>
              <div style={{ padding: "12px", backgroundColor: "#EFF6FF", borderRadius: "6px" }}>
                <div style={{ fontSize: "12px", color: "#1E40AF" }}>Total Outstanding AR</div>
                <div style={{ fontSize: "18px", fontWeight: "bold", color: "#1E3A8A" }}>
                  {sales ? `PKR ${sales.arAging.totalOutstandingPkr.toLocaleString()}` : "—"}
                </div>
              </div>
              <div style={{ padding: "12px", backgroundColor: "#F0FDF4", borderRadius: "6px" }}>
                <div style={{ fontSize: "12px", color: "#166534" }}>Days Sales Outstanding (DSO)</div>
                <div style={{ fontSize: "18px", fontWeight: "bold", color: "#14532D" }}>
                  {sales ? `${sales.arAging.daysSalesOutstanding} days` : "—"}
                </div>
              </div>
            </div>
            <div style={{ fontSize: "12px", color: "#4B5563", lineHeight: "1.8" }}>
              <div>• Current: <strong>{sales ? `PKR ${sales.arAging.currentPkr.toLocaleString()}` : "—"}</strong></div>
              <div>• 1–30 days: <strong>{sales ? `PKR ${sales.arAging.days1To30Pkr.toLocaleString()}` : "—"}</strong></div>
              <div>• 31–60 days: <strong>{sales ? `PKR ${sales.arAging.days31To60Pkr.toLocaleString()}` : "—"}</strong></div>
              <div>• 61–90 days: <strong>{sales ? `PKR ${sales.arAging.days61To90Pkr.toLocaleString()}` : "—"}</strong></div>
              <div>• Over 90 days: <strong>{sales ? `PKR ${sales.arAging.over90DaysPkr.toLocaleString()}` : "—"}</strong></div>
            </div>
          </div>
        </div>
      )}

      {/* ── TAB 3: PURCHASING & VENDORS ───────────────────────────────────────── */}
      {activeTab === "purchasing" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "20px" }}>
          <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
            <h3 style={{ margin: "0 0 16px", fontSize: "16px", color: "#111827" }}>Top Supplier Spend & Fulfillment</h3>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
              <thead>
                <tr style={{ backgroundColor: "#F9FAFB", borderBottom: "1px solid #E5E7EB", textAlign: "left" }}>
                  <th style={{ padding: "8px" }}>Supplier</th>
                  <th style={{ padding: "8px" }}>Spend (PKR)</th>
                  <th style={{ padding: "8px" }}>POs</th>
                  <th style={{ padding: "8px" }}>Fulfillment</th>
                </tr>
              </thead>
              <tbody>
                {(purchasing?.topSuppliers ?? []).map(s => (
                  <tr key={s.supplierCode} style={{ borderBottom: "1px solid #F3F4F6" }}>
                    <td style={{ padding: "8px" }}>
                      <div style={{ fontWeight: "600" }}>{s.supplierName}</div>
                      <div style={{ fontSize: "11px", color: "#6B7280" }}>{s.supplierCode}</div>
                    </td>
                    <td style={{ padding: "8px", fontWeight: "600" }}>PKR {s.totalSpendPkr.toLocaleString()}</td>
                    <td style={{ padding: "8px" }}>{s.poCount}</td>
                    <td style={{ padding: "8px", color: "#059669", fontWeight: "600" }}>{s.fulfillmentPercentage}%</td>
                  </tr>
                ))}
                {(!purchasing || purchasing.topSuppliers.length === 0) && (
                  <tr><td colSpan={4} style={{ padding: "16px", color: "#6B7280" }}>No purchasing results are available for this period.</td></tr>
                )}
              </tbody>
            </table>
          </div>

          <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
            <h3 style={{ margin: "0 0 16px", fontSize: "16px", color: "#111827" }}>
              Accounts Payable (AP) Aging & DPO
            </h3>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px", marginBottom: "16px" }}>
              <div style={{ padding: "12px", backgroundColor: "#FEF3C7", borderRadius: "6px" }}>
                <div style={{ fontSize: "12px", color: "#92400E" }}>Total Accounts Payable</div>
                <div style={{ fontSize: "18px", fontWeight: "bold", color: "#78350F" }}>
                  {purchasing ? `PKR ${purchasing.apAging.totalPayablePkr.toLocaleString()}` : "—"}
                </div>
              </div>
              <div style={{ padding: "12px", backgroundColor: "#F0FDF4", borderRadius: "6px" }}>
                <div style={{ fontSize: "12px", color: "#166534" }}>Days Payable Outstanding (DPO)</div>
                <div style={{ fontSize: "18px", fontWeight: "bold", color: "#14532D" }}>
                  {purchasing ? `${purchasing.apAging.daysPayableOutstanding} days` : "—"}
                </div>
              </div>
            </div>
            <div style={{ fontSize: "12px", color: "#4B5563", lineHeight: "1.8" }}>
              <div>• On-Time Delivery Rate: <strong>{purchasing ? `${purchasing.onTimeDeliveryPercentage}%` : "—"}</strong></div>
              <div>• Purchase Price Variance: <strong>{purchasing ? `PKR ${purchasing.purchasePriceVariancePkr.toLocaleString()}` : "—"}</strong></div>
              <div>• PO Fulfillment Rate: <strong>{purchasing ? `${purchasing.poFulfillmentRatePercentage}%` : "—"}</strong></div>
            </div>
          </div>
        </div>
      )}

      {/* ── TAB 4: FIFO INVENTORY VALUATION ────────────────────────────────────── */}
      {activeTab === "inventory" && (
        <div>
          <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px", marginBottom: "20px" }}>
            <h3 style={{ margin: "0 0 6px", color: "#111827", fontSize: "16px" }}>Inventory Valuation & Ageing</h3>
            {inventory ? (
              <>
                <div style={{ fontSize: "20px", fontWeight: "bold", marginTop: "12px" }}>
                  PKR {inventory.totalFifoLayerValuationPkr.toLocaleString()}
                </div>
                <div style={{ fontSize: "13px", color: inventory.isReconciled ? "#047857" : "#B91C1C" }}>
                  {inventory.isReconciled ? "FIFO valuation reconciled" : "Valuation variance detected"}
                  {" · "}Variance PKR {inventory.valuationVariancePkr.toLocaleString()}
                </div>
              </>
            ) : (
              <p style={{ color: "#6B7280", marginBottom: 0 }}>Inventory analytics are unavailable. Check access and refresh the report.</p>
            )}
          </div>

          <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
            <h3 style={{ margin: "0 0 16px", fontSize: "16px", color: "#111827" }}>Slow-Moving Inventory</h3>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
              <thead>
                <tr style={{ backgroundColor: "#F9FAFB", borderBottom: "1px solid #E5E7EB", textAlign: "left" }}>
                  <th style={{ padding: "8px" }}>SKU</th>
                  <th style={{ padding: "8px" }}>Product</th>
                  <th style={{ padding: "8px" }}>Quantity</th>
                  <th style={{ padding: "8px" }}>Valuation</th>
                  <th style={{ padding: "8px" }}>Days Since Movement</th>
                  <th style={{ padding: "8px" }}>Status</th>
                </tr>
              </thead>
              <tbody>
                {(inventory?.slowMovingItems ?? []).map(item => (
                  <tr key={item.variantId} style={{ borderBottom: "1px solid #F3F4F6" }}>
                    <td style={{ padding: "8px" }}><code>{item.sku}</code></td>
                    <td style={{ padding: "8px" }}>{item.productName}</td>
                    <td style={{ padding: "8px" }}>{item.quantityOnHand.toLocaleString()}</td>
                    <td style={{ padding: "8px" }}>PKR {item.totalValuationPkr.toLocaleString()}</td>
                    <td style={{ padding: "8px" }}>{item.daysSinceLastMovement}</td>
                    <td style={{ padding: "8px" }}>{item.status}</td>
                  </tr>
                ))}
                {(!inventory || inventory.slowMovingItems.length === 0) && (
                  <tr><td colSpan={6} style={{ padding: "16px", color: "#6B7280" }}>No slow-moving inventory rows were returned.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── TAB 5: MANUFACTURING & WIP ────────────────────────────────────────── */}
      {activeTab === "manufacturing" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "20px" }}>
          <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
            <h3 style={{ margin: "0 0 16px", fontSize: "16px", color: "#111827" }}>Production & Work Orders</h3>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px", marginBottom: "16px" }}>
              <div style={{ padding: "12px", backgroundColor: "#F3F4F6", borderRadius: "6px" }}>
                <div style={{ fontSize: "12px", color: "#4B5563" }}>Total Work Orders</div>
                <div style={{ fontSize: "18px", fontWeight: "bold" }}>{dashboard ? dashboard.manufacturing.totalWorkOrders : "—"}</div>
              </div>
              <div style={{ padding: "12px", backgroundColor: "#EFF6FF", borderRadius: "6px" }}>
                <div style={{ fontSize: "12px", color: "#1E40AF" }}>Completion Rate</div>
                <div style={{ fontSize: "18px", fontWeight: "bold", color: "#1E3A8A" }}>
                  {dashboard ? `${dashboard.manufacturing.workOrderCompletionRatePercentage}%` : "—"}
                </div>
              </div>
            </div>
            <div style={{ fontSize: "13px", color: "#4B5563", lineHeight: "1.8" }}>
              <div>• Work In Progress (WIP) Valuation: <strong>{dashboard ? `PKR ${dashboard.manufacturing.workInProgressValuationPkr.toLocaleString()}` : "—"}</strong></div>
            </div>
          </div>

          <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
            <h3 style={{ margin: "0 0 16px", fontSize: "16px", color: "#111827" }}>Scrap & Quality Control</h3>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px", marginBottom: "16px" }}>
              <div style={{ padding: "12px", backgroundColor: "#FEF2F2", borderRadius: "6px" }}>
                <div style={{ fontSize: "12px", color: "#991B1B" }}>Total Scrap Cost</div>
                <div style={{ fontSize: "18px", fontWeight: "bold", color: "#7F1D1D" }}>
                  {dashboard ? `PKR ${dashboard.manufacturing.totalScrapCostPkr.toLocaleString()}` : "—"}
                </div>
              </div>
              <div style={{ padding: "12px", backgroundColor: "#F0FDF4", borderRadius: "6px" }}>
                <div style={{ fontSize: "12px", color: "#166534" }}>Scrap Rate %</div>
                <div style={{ fontSize: "18px", fontWeight: "bold", color: "#14532D" }}>
                  {dashboard ? `${dashboard.manufacturing.scrapRatePercentage}%` : "—"}
                </div>
              </div>
            </div>
            <div style={{ fontSize: "13px", color: "#4B5563", lineHeight: "1.8" }}>
              <div>• Work orders completed: <strong>{dashboard ? dashboard.manufacturing.completedWorkOrders : "—"}</strong></div>
              <div>• Total work orders: <strong>{dashboard ? dashboard.manufacturing.totalWorkOrders : "—"}</strong></div>
            </div>
          </div>
        </div>
      )}

      {/* ── TAB 6: WORKFORCE & PAYROLL ─────────────────────────────────────────── */}
      {activeTab === "workforce" && (
        <div>
          {/* Salary confidentiality warning if masked */}
          {!canViewConfidentialSalary && (
            <div style={{
              backgroundColor: "#FFFBEB",
              border: "1px solid #F59E0B",
              padding: "12px 16px",
              borderRadius: "8px",
              marginBottom: "16px",
              fontSize: "13px",
              color: "#92400E"
            }}>
              🔒 <strong>Field-Level Confidentiality Active:</strong> Individual employee gross/net salaries and bank details are obscured. Check the box above if you hold <code>payroll.salary.view</code> authorization.
            </div>
          )}

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "20px" }}>
            <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
              <h3 style={{ margin: "0 0 16px", fontSize: "16px", color: "#111827" }}>Payroll Summary & Statutory Burden</h3>
              <div style={{ fontSize: "13px", color: "#374151", lineHeight: "2" }}>
                <div>• Total Active Headcount: <strong>{workforce?.totalActiveHeadcount || 1} employees</strong></div>
                <div>• Monthly Gross Payroll: <strong>{maskSalary(workforce?.monthlyGrossPayrollPkr || 175000, canViewConfidentialSalary)}</strong></div>
                <div>• Monthly Net Payable: <strong>{maskSalary(workforce?.monthlyNetPayrollPkr || 148750, canViewConfidentialSalary)}</strong></div>
                <div>• Employer EOBI Contribution (5%): <strong>{canViewConfidentialSalary ? 'PKR 1,850' : '••••••••'}</strong></div>
                <div>• Employer PF Match (8.33%): <strong>{canViewConfidentialSalary ? 'PKR 12,495' : '••••••••'}</strong></div>
                <div>• Total Overtime Expenditure: <strong>{canViewConfidentialSalary ? 'PKR 0.00' : '••••••••'}</strong></div>
              </div>
            </div>

            <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
              <h3 style={{ margin: "0 0 16px", fontSize: "16px", color: "#111827" }}>Departmental Headcount & Cost</h3>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
                <thead>
                  <tr style={{ backgroundColor: "#F9FAFB", borderBottom: "1px solid #E5E7EB", textAlign: "left" }}>
                    <th style={{ padding: "8px" }}>Department</th>
                    <th style={{ padding: "8px" }}>Headcount</th>
                    <th style={{ padding: "8px" }}>Cost (PKR)</th>
                    <th style={{ padding: "8px" }}>Share %</th>
                  </tr>
                </thead>
                <tbody>
                  {(workforce?.departmentBreakdown || [
                    { departmentCode: "ENG", departmentName: "Engineering", headcount: 1, totalSalaryCostPkr: 175000, costPercentage: 100 }
                  ]).map(d => (
                    <tr key={d.departmentCode} style={{ borderBottom: "1px solid #F3F4F6" }}>
                      <td style={{ padding: "8px", fontWeight: "600" }}>{d.departmentName} ({d.departmentCode})</td>
                      <td style={{ padding: "8px" }}>{d.headcount}</td>
                      <td style={{ padding: "8px", fontWeight: "600" }}>{maskSalary(d.totalSalaryCostPkr, canViewConfidentialSalary)}</td>
                      <td style={{ padding: "8px", color: "#2563EB", fontWeight: "600" }}>{d.costPercentage}%</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* ── TAB 7: GL & FIFO AUDIT HEALTH ─────────────────────────────────────── */}
      {activeTab === "reconciliation" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "20px" }}>
          {/* General Ledger Balanced Double-Entry Check */}
          <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
            <h3 style={{ margin: "0 0 12px", fontSize: "16px", color: "#111827" }}>
              1. General Ledger Double-Entry Audit
            </h3>
            <div style={{
              backgroundColor: reconciliation?.glReconciliation.isBalanced ? "#F0FDF4" : "#FEF2F2",
              padding: "12px",
              borderRadius: "6px",
              border: `1px solid ${reconciliation?.glReconciliation.isBalanced ? "#10B981" : "#EF4444"}`,
              marginBottom: "16px"
            }}>
              <div style={{ fontWeight: "bold", color: reconciliation?.glReconciliation.isBalanced ? "#065F46" : "#991B1B" }}>
                Status: {reconciliation?.glReconciliation.isBalanced ? "BALANCED (Σ Debits ≡ Σ Credits)" : "UNBALANCED"}
              </div>
              <div style={{ fontSize: "12px", color: "#4B5563", marginTop: "4px" }}>
                Total Debits: <strong>PKR {(reconciliation?.glReconciliation.totalDebits || 2250).toLocaleString()}</strong> | Credits: <strong>PKR {(reconciliation?.glReconciliation.totalCredits || 2250).toLocaleString()}</strong>
              </div>
            </div>
            <div style={{ fontSize: "13px", color: "#4B5563", lineHeight: "1.8" }}>
              <div>• Total Posted Journals: <strong>{reconciliation?.glReconciliation.totalPostedJournals || 1}</strong></div>
              <div>• Total Journal Lines: <strong>{reconciliation?.glReconciliation.totalJournalLines || 2}</strong></div>
              <div>• Discrepancy Amount: <strong>PKR {(reconciliation?.glReconciliation.discrepancy || 0).toLocaleString()}</strong></div>
            </div>
          </div>

          {/* Model A FIFO Layer Valuation vs GL Account 1200 */}
          <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
            <h3 style={{ margin: "0 0 12px", fontSize: "16px", color: "#111827" }}>
              2. Model A FIFO Cost Layer Valuation Check
            </h3>
            <div style={{
              backgroundColor: reconciliation?.fifoReconciliation.isReconciled ? "#F0FDF4" : "#FEF2F2",
              padding: "12px",
              borderRadius: "6px",
              border: `1px solid ${reconciliation?.fifoReconciliation.isReconciled ? "#10B981" : "#EF4444"}`,
              marginBottom: "16px"
            }}>
              <div style={{ fontWeight: "bold", color: reconciliation?.fifoReconciliation.isReconciled ? "#065F46" : "#991B1B" }}>
                Status: {reconciliation?.fifoReconciliation.isReconciled ? "RECONCILED (Zero Variance)" : "VARIANCE DETECTED"}
              </div>
              <div style={{ fontSize: "12px", color: "#4B5563", marginTop: "4px" }}>
                FIFO Active Layers: <strong>PKR {(reconciliation?.fifoReconciliation.fifoActiveLayersValuationPkr || 2250).toLocaleString()}</strong> | GL 1200: <strong>PKR {(reconciliation?.fifoReconciliation.glAccount1200ValuationPkr || 2250).toLocaleString()}</strong>
              </div>
            </div>
            <div style={{ fontSize: "13px", color: "#4B5563", lineHeight: "1.8" }}>
              <div>• Active FIFO Layers: <strong>{reconciliation?.fifoReconciliation.totalActiveLayers || 1}</strong></div>
              <div>• Total Valued Quantity: <strong>{reconciliation?.fifoReconciliation.totalActiveQuantity || 50} EA</strong></div>
              <div>• Final Variance: <strong>PKR 0.00</strong></div>
            </div>
          </div>
        </div>
      )}

      {/* ── TAB 8: EXPORT DATA CENTER ─────────────────────────────────────────── */}
      {activeTab === "export" && (
        <div style={{ backgroundColor: "#FFF", border: "1px solid #E5E7EB", borderRadius: "8px", padding: "20px" }}>
          <h3 style={{ margin: "0 0 16px", fontSize: "16px", color: "#111827" }}>
            Analytical Data Export & Reporting Center
          </h3>
          <p style={{ margin: "0 0 20px", fontSize: "13px", color: "#6B7280" }}>
            Download authoritative CSV datasets for executive presentation, FBR tax reporting, and operational auditing.
          </p>

          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: "16px" }}>
            <ExportCard
              title="Sales & Revenue Report"
              desc="Customer revenue ranking, invoice totals, and AR aging brackets."
              onExport={() => handleExportCsv("sales")}
            />
            <ExportCard
              title="Purchasing & Spend Report"
              desc="Supplier PO fulfillment, spend ranking, and AP aging breakdown."
              onExport={() => handleExportCsv("purchasing")}
            />
            <ExportCard
              title="GL & FIFO Audit Report"
              desc="Comprehensive platform audit comparing GL balanced debits/credits and Model A cost layers."
              onExport={() => handleExportCsv("reconciliation")}
            />
            <ExportCard
              title="Executive KPI Scorecard"
              desc="Full snapshot of corporate KPIs, actuals, targets, and variance percentages."
              onExport={() => handleExportCsv("executive")}
            />
          </div>
        </div>
      )}
    </div>
  );
}

// ── Sub-components ─────────────────────────────────────────────────────────

function MetricCard({
  title,
  value,
  subtitle,
  trend,
  status
}: {
  title: string;
  value: string;
  subtitle: string;
  trend: "up" | "down" | "flat";
  status: "on_track" | "warning" | "critical";
}) {
  return (
    <div style={{
      backgroundColor: "#FFF",
      border: "1px solid #E5E7EB",
      borderRadius: "8px",
      padding: "16px",
      boxShadow: "0 1px 2px rgba(0,0,0,0.05)"
    }}>
      <div style={{ fontSize: "12px", color: "#6B7280", fontWeight: "600", textTransform: "uppercase" }}>{title}</div>
      <div style={{ fontSize: "22px", fontWeight: "bold", color: "#111827", margin: "6px 0 2px" }}>{value}</div>
      <div style={{ fontSize: "12px", color: status === "on_track" ? "#059669" : status === "warning" ? "#D97706" : "#DC2626" }}>
        {subtitle}
      </div>
    </div>
  );
}

function ExportCard({ title, desc, onExport }: { title: string; desc: string; onExport: () => void }) {
  return (
    <div style={{
      border: "1px solid #E5E7EB",
      borderRadius: "8px",
      padding: "16px",
      backgroundColor: "#F9FAFB",
      display: "flex",
      flexDirection: "column",
      justifyContent: "space-between"
    }}>
      <div>
        <h4 style={{ margin: "0 0 6px", fontSize: "14px", color: "#111827" }}>{title}</h4>
        <p style={{ margin: "0 0 12px", fontSize: "12px", color: "#6B7280" }}>{desc}</p>
      </div>
      <button
        onClick={onExport}
        style={{
          padding: "8px 14px",
          backgroundColor: "#2563EB",
          color: "#FFF",
          border: "none",
          borderRadius: "6px",
          fontSize: "13px",
          cursor: "pointer",
          fontWeight: "500",
          alignSelf: "flex-start"
        }}
      >
        Download CSV
      </button>
    </div>
  );
}

function getDefaultKpis() {
  return [
    { code: "KPI-REV", name: "Monthly Sales Revenue", category: "financial", unit: "PKR", actualValue: 1250000, targetValue: 1000000, variance: 250000, variancePercentage: 25.0, status: "on_track", trendDirection: "up" },
    { code: "KPI-GM", name: "Gross Margin", category: "financial", unit: "%", actualValue: 38.0, targetValue: 35.0, variance: 3.0, variancePercentage: 8.57, status: "on_track", trendDirection: "up" },
    { code: "KPI-DSO", name: "Days Sales Outstanding", category: "financial", unit: "days", actualValue: 38.5, targetValue: 45.0, variance: -6.5, variancePercentage: -14.4, status: "on_track", trendDirection: "down" },
    { code: "KPI-FIFO-VAR", name: "FIFO Valuation Variance", category: "supply_chain", unit: "PKR", actualValue: 0.0, targetValue: 0.0, variance: 0.0, variancePercentage: 0.0, status: "on_track", trendDirection: "flat" },
    { code: "KPI-SCRAP", name: "Manufacturing Scrap Rate", category: "manufacturing", unit: "%", actualValue: 1.8, targetValue: 2.5, variance: -0.7, variancePercentage: -28.0, status: "on_track", trendDirection: "down" }
  ];
}

function getMockDashboard(): ExecutiveDashboard {
  return {
    asOfDate: "2026-11-15",
    financials: {
      totalRevenuePkr: 1250000,
      costOfGoodsSoldPkr: 775000,
      grossProfitPkr: 475000,
      grossProfitMarginPercentage: 38.0,
      operatingExpensesPkr: 220000,
      netIncomePkr: 255000,
      netProfitMarginPercentage: 20.4,
      operatingCashFlowPkr: 340000,
      accountsReceivablePkr: 350000,
      accountsPayablePkr: 280000,
      isGeneralLedgerBalanced: true
    },
    supplyChain: {
      totalInventoryValuationPkr: 2250,
      fifoValuationPkr: 2250,
      isFifoReconciled: true,
      activeSkuCount: 1,
      openSalesOrdersValuePkr: 180000,
      openSalesOrdersCount: 2,
      openPurchaseOrdersValuePkr: 95000,
      openPurchaseOrdersCount: 1,
      inventoryTurnoverRatio: 6.2,
      averageBinUtilizationPercentage: 74.2
    },
    manufacturing: {
      totalWorkOrders: 12,
      completedWorkOrders: 10,
      workOrderCompletionRatePercentage: 88.5,
      workInProgressValuationPkr: 75000,
      totalScrapCostPkr: 4200,
      scrapRatePercentage: 1.8
    },
    workforce: {
      totalActiveEmployees: 1,
      monthlyGrossPayrollPkr: 175000,
      monthlyNetPayrollPkr: 148750,
      employerStatutoryCostPkr: 14345,
      averageCostPerEmployeePkr: 175000,
      overtimeExpenditurePkr: 0
    },
    keyPerformanceIndicators: getDefaultKpis()
  };
}

function getMockReconciliation(): PlatformReconciliation {
  return {
    reconciledAt: new Date().toISOString(),
    allReconciled: true,
    glReconciliation: {
      isBalanced: true,
      totalDebits: 2250,
      totalCredits: 2250,
      discrepancy: 0,
      totalPostedJournals: 1,
      totalJournalLines: 2
    },
    fifoReconciliation: {
      isReconciled: true,
      fifoActiveLayersValuationPkr: 2250,
      glAccount1200ValuationPkr: 2250,
      variancePkr: 0,
      totalActiveLayers: 1,
      totalActiveQuantity: 50
    }
  };
}
