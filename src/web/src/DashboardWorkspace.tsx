import React, { useState, useEffect } from "react";
import { Card, KPICard, Badge, Button, PageHeader } from "./components";
import { apiFetch, type TenantCompanyContext } from "./auth";
import {
  formatMoneyWithCurrency,
  resolveDashboardCurrencyState,
  type ResolvedDashboardCurrencyState,
  type TrialBalanceRowSummary,
  type ValuationReportResponse,
} from "./dashboardCurrencyHelpers";

interface DashboardWorkspaceProps {
  workspace: TenantCompanyContext | null;
  onNavigate: (module: string) => void;
}

interface AuditEventItem {
  eventId: string;
  occurredAt: string;
  actorEmail?: string | null;
  actorName?: string | null;
  eventCode: string;
  entityType: string;
  entityId?: string | null;
  metadata?: unknown;
}

interface BinDistribution {
  binId: string;
  warehouseCode: string;
  binCode: string;
  quantity: number;
  percentage: number;
}

interface WarehouseItem {
  warehouseId: string;
  code: string;
  name: string;
  isActive?: boolean;
}

interface BinItem {
  binId: string;
  warehouseId: string;
  code: string;
  binType: string;
  isActive?: boolean;
}

interface BalanceItem {
  variantId: string;
  warehouseId: string;
  binId: string;
  onHandQuantity: number;
}

interface TrialBalanceRow {
  accountId: string;
  accountCode: string;
  accountName: string;
  accountType: string;
  normalBalance: string;
  totalDebit: number;
  totalCredit: number;
  netBalance: number;
}

export default function DashboardWorkspace({
  workspace,
  onNavigate,
}: DashboardWorkspaceProps) {
  const [loading, setLoading] = useState(false);
  const [inventoryStats, setInventoryStats] = useState<{
    totalUnits: number | null;
    valuation: number | null;
    activeWarehouses: number | null;
    activeBins: number | null;
  }>({
    totalUnits: null,
    valuation: null,
    activeWarehouses: null,
    activeBins: null,
  });

  const [financeStats, setFinanceStats] = useState<{
    gl1200Debit: number | null;
    trialBalanceBalanced: boolean | null;
    variance: number | null;
  }>({
    gl1200Debit: null,
    trialBalanceBalanced: null,
    variance: null,
  });

  const [currencyState, setCurrencyState] = useState<ResolvedDashboardCurrencyState>(() =>
    resolveDashboardCurrencyState(workspace?.baseCurrencyCode, null, null)
  );

  const [binDistributions, setBinDistributions] = useState<BinDistribution[]>([]);
  const [binApiError, setBinApiError] = useState(false);
  const [auditEvents, setAuditEvents] = useState<AuditEventItem[]>([]);

  // Format financial values strictly using verified functional currency
  const baseCurrency = currencyState.companyBaseCurrency || workspace?.baseCurrencyCode?.trim().toUpperCase() || null;

  const formatMoney = (val: number | null | undefined): string =>
    formatMoneyWithCurrency(val, baseCurrency, 2);

  useEffect(() => {
    let isMounted = true;
    async function loadDashboardData() {
      if (!workspace) return;
      setLoading(true);

      try {
        // Parallel requests to authoritative live endpoints
        const [
          valResResult,
          whResResult,
          binResResult,
          availResResult,
          tbResResult,
          auditResResult,
        ] = await Promise.allSettled([
          apiFetch("/api/v1/inventory/valuation-report", {}, workspace),
          apiFetch("/api/v1/inventory/warehouses", {}, workspace),
          apiFetch("/api/v1/inventory/bins", {}, workspace),
          apiFetch("/api/v1/inventory/availability", {}, workspace),
          apiFetch("/api/v1/finance/trial-balance", {}, workspace),
          apiFetch("/api/v1/foundation/audit-events?limit=10", {}, workspace),
        ]);

        if (!isMounted) return;

        // 1. Process Currency-Safe Valuation Report
        let valData: ValuationReportResponse | null = null;
        if (valResResult.status === "fulfilled" && valResResult.value.ok) {
          try {
            const parsed = await valResResult.value.json();
            if (parsed && typeof parsed === "object") {
              valData = parsed as ValuationReportResponse;
            }
          } catch {
            // keep null
          }
        }

        // 2. Process Warehouses, Bins & Physical Availability
        let warehouses: WarehouseItem[] | null = null;
        let bins: BinItem[] | null = null;
        let balances: BalanceItem[] | null = null;
        let hasBinError = false;

        if (whResResult.status === "fulfilled" && whResResult.value.ok) {
          try {
            const whData = await whResResult.value.json();
            if (Array.isArray(whData)) warehouses = whData;
          } catch {
            // keep null
          }
        }

        if (binResResult.status === "fulfilled" && binResResult.value.ok) {
          try {
            const binData = await binResResult.value.json();
            if (Array.isArray(binData)) bins = binData;
          } catch {
            // keep null
          }
        } else {
          hasBinError = true;
        }

        if (availResResult.status === "fulfilled" && availResResult.value.ok) {
          try {
            const availData = await availResResult.value.json();
            if (Array.isArray(availData)) balances = availData;
          } catch {
            // keep null
          }
        } else {
          hasBinError = true;
        }

        setBinApiError(hasBinError);

        const warehouseMap = new Map<string, string>();
        if (warehouses) {
          warehouses.forEach((w) => {
            warehouseMap.set(w.warehouseId, w.code || w.name);
          });
        }

        // Sum actual physical on-hand quantity per bin
        const binQtyMap = new Map<string, number>();
        let sumAvailableUnits: number | null = null;
        if (balances) {
          sumAvailableUnits = 0;
          balances.forEach((b) => {
            const qty = Number(b.onHandQuantity ?? 0);
            sumAvailableUnits! += qty;
            if (b.binId) {
              binQtyMap.set(b.binId, (binQtyMap.get(b.binId) || 0) + qty);
            }
          });
        }

        // 3. Process Trial Balance and resolve currency-safe dashboard state
        let tbData: TrialBalanceRowSummary[] | null = null;
        if (tbResResult.status === "fulfilled" && tbResResult.value.ok) {
          try {
            const parsedTb = await tbResResult.value.json();
            if (Array.isArray(parsedTb)) {
              tbData = parsedTb as TrialBalanceRowSummary[];
            }
          } catch {
            // keep null
          }
        }

        const resolvedCurrencyState = resolveDashboardCurrencyState(
          workspace.baseCurrencyCode,
          valData,
          tbData
        );

        // Effective physical stock: prefer valuation report, fallback to sum of balances, or null if both failed
        let effectiveTotalUnits: number | null = null;
        if (resolvedCurrencyState.totalPhysicalStock !== null) {
          effectiveTotalUnits = resolvedCurrencyState.totalPhysicalStock;
        } else if (sumAvailableUnits !== null) {
          effectiveTotalUnits = sumAvailableUnits;
        }

        // Build dynamic bin distributions from active bins
        let distributions: BinDistribution[] = [];
        let activeBinsList: BinItem[] = [];
        if (bins) {
          activeBinsList = bins.filter((b) => b.isActive !== false);
          distributions = activeBinsList.map((bin) => {
            const qty = binQtyMap.get(bin.binId) || 0;
            const pct =
              effectiveTotalUnits !== null && effectiveTotalUnits > 0
                ? (qty / effectiveTotalUnits) * 100
                : 0;
            return {
              binId: bin.binId,
              warehouseCode: warehouseMap.get(bin.warehouseId) || "WH",
              binCode: bin.code,
              quantity: qty,
              percentage: Math.round(pct * 10) / 10,
            };
          });
          // Sort bins: largest stock first, then alphabetically by code
          distributions.sort((a, b) => b.quantity - a.quantity || a.binCode.localeCompare(b.binCode));
        }

        setCurrencyState(resolvedCurrencyState);

        setInventoryStats({
          totalUnits: effectiveTotalUnits,
          valuation: resolvedCurrencyState.functionalValuation,
          activeWarehouses: warehouses !== null ? warehouses.length : null,
          activeBins: bins !== null ? activeBinsList.length : null,
        });

        setFinanceStats({
          gl1200Debit: resolvedCurrencyState.functionalGl1200Balance,
          trialBalanceBalanced: resolvedCurrencyState.trialBalanceBalanced,
          variance: resolvedCurrencyState.functionalVariance,
        });

        setBinDistributions(distributions);

        // 4. Process Live Audit Events
        let liveEvents: AuditEventItem[] = [];
        if (auditResResult.status === "fulfilled" && auditResResult.value.ok) {
          try {
            const evtData = await auditResResult.value.json();
            if (Array.isArray(evtData)) {
              liveEvents = evtData;
            }
          } catch {
            // empty array
          }
        }
        setAuditEvents(liveEvents);
      } catch (err) {
        console.error("Failed to load dashboard metrics", err);
      } finally {
        if (isMounted) setLoading(false);
      }
    }

    void loadDashboardData();
    return () => {
      isMounted = false;
    };
  }, [workspace]);

  // Helpers for audit events
  const formatEventTime = (isoString: string): string => {
    try {
      const d = new Date(isoString);
      if (isNaN(d.getTime())) return "Recent";
      const diffMs = Date.now() - d.getTime();
      const diffMins = Math.floor(diffMs / 60000);
      if (diffMins < 1) return "Just now";
      if (diffMins < 60) return `${diffMins}m ago`;
      const diffHours = Math.floor(diffMins / 60);
      if (diffHours < 24) return `${diffHours}h ago`;
      const diffDays = Math.floor(diffHours / 24);
      if (diffDays < 7) return `${diffDays}d ago`;
      return d.toLocaleDateString();
    } catch {
      return "Recent";
    }
  };

  const formatEventTitle = (code: string): string => {
    if (!code) return "Operational Event";
    return code
      .replace(/[._-]/g, " ")
      .replace(/\b\w/g, (c) => c.toUpperCase());
  };

  const isBalanced = financeStats.variance !== null && financeStats.variance === 0;
  const hasDiscrepancy = financeStats.variance !== null && financeStats.variance > 0;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-6)" }}>
      <PageHeader
        title="Executive Overview"
        subtitle={
          workspace
            ? `Real-time consolidated KPIs for ${workspace.companyName} (${workspace.tenantId}) · Base Currency: ${
                baseCurrency || "Unspecified"
              }`
            : "Enterprise Multi-Workspace Management Dashboard"
        }
        actions={
          <div style={{ display: "flex", gap: "var(--space-3)" }}>
            <Button
              variant="outline"
              size="sm"
              icon="💳"
              onClick={() => onNavigate("sales")}
            >
              Open POS
            </Button>
            <Button
              variant="primary"
              size="sm"
              icon="📦"
              onClick={() => onNavigate("inventory")}
            >
              Stock Management
            </Button>
          </div>
        }
      />

      {/* KPI Cards Row */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))",
          gap: "var(--space-4)",
        }}
      >
        <KPICard
          title="Total Physical Inventory"
          value={
            loading
              ? "Loading..."
              : inventoryStats.totalUnits !== null
              ? `${inventoryStats.totalUnits} EA`
              : "Unavailable"
          }
          icon="📦"
          color={inventoryStats.totalUnits !== null ? "blue" : "amber"}
          change={
            inventoryStats.totalUnits !== null ? "Physical Count" : "API Unavailable"
          }
          changeType={
            inventoryStats.totalUnits !== null && inventoryStats.totalUnits > 0
              ? "up"
              : "neutral"
          }
          changePeriod={
            inventoryStats.totalUnits !== null
              ? `${inventoryStats.totalUnits} EA physically verified`
              : "Unable to retrieve physical stock"
          }
          onClick={() => onNavigate("inventory")}
        />

        <KPICard
          title="Inventory FIFO Valuation"
          value={loading ? "Loading..." : formatMoney(inventoryStats.valuation)}
          icon="💰"
          color={
            inventoryStats.valuation === null
              ? "amber"
              : isBalanced
              ? "green"
              : hasDiscrepancy
              ? "amber"
              : "blue"
          }
          details={
            loading ? undefined : currencyState.isCurrencyUnresolved ? (
              <>
                <span>
                  Source valuation: {currencyState.sourceValuationBalancesText || "Unavailable"}
                </span>
                <span>
                  Company base currency: {currencyState.companyBaseCurrency || "Unspecified"}
                </span>
                <span>Functional valuation: Unavailable</span>
              </>
            ) : (
              <>
                <span>
                  {financeStats.gl1200Debit !== null
                    ? `GL 1200: ${formatMoney(financeStats.gl1200Debit)}`
                    : "GL 1200: Unavailable"}
                </span>
                <span>
                  Company base currency: {currencyState.companyBaseCurrency || "Unspecified"}
                </span>
              </>
            )
          }
          onClick={() => onNavigate("finance")}
        />

        <KPICard
          title="GL Account 1200"
          value={
            loading
              ? "Loading..."
              : currencyState.isGlCurrencyAligned && financeStats.gl1200Debit !== null
              ? formatMoney(financeStats.gl1200Debit)
              : "Unavailable"
          }
          icon="📒"
          color={
            currencyState.isCurrencyUnresolved
              ? "amber"
              : isBalanced
              ? "green"
              : hasDiscrepancy
              ? "red"
              : "amber"
          }
          details={
            loading ? undefined : (
              <>
                <span>
                  Source ledger balance:{" "}
                  {currencyState.sourceLedgerBalancesText || "Unavailable"}
                </span>
                <span>
                  Company base currency: {currencyState.companyBaseCurrency || "Unspecified"}
                </span>
                <span>
                  Functional GL balance:{" "}
                  {currencyState.isGlCurrencyAligned
                    ? formatMoney(financeStats.gl1200Debit)
                    : "Unavailable"}
                </span>
                <span>Reconciliation: {currencyState.reconciliationLabel}</span>
              </>
            )
          }
          onClick={() => onNavigate("finance")}
        />

        <KPICard
          title="General Ledger Variance"
          value={loading ? "Loading..." : formatMoney(financeStats.variance)}
          icon="⚖️"
          color={
            financeStats.variance === null
              ? "amber"
              : isBalanced
              ? "green"
              : "red"
          }
          change={
            currencyState.isCurrencyUnresolved
              ? currencyState.reconciliationLabel
              : financeStats.variance === null
              ? "Unavailable"
              : isBalanced
              ? "0.00 (Balanced)"
              : "Discrepancy"
          }
          changeType={isBalanced ? "neutral" : financeStats.variance !== null ? "down" : "neutral"}
          changePeriod={
            currencyState.isCurrencyUnresolved
              ? `Company base currency: ${currencyState.companyBaseCurrency || "Unspecified"}`
              : financeStats.variance === null
              ? "Variance calculation unavailable"
              : isBalanced
              ? "Zero discrepancy baseline"
              : `Discrepancy: ${formatMoney(financeStats.variance)}`
          }
          onClick={() => onNavigate("finance")}
        />

        <KPICard
          title="Active Warehouses & Bins"
          value={
            loading
              ? "Loading..."
              : inventoryStats.activeWarehouses !== null
              ? `${inventoryStats.activeWarehouses} Warehouses`
              : "Unavailable"
          }
          icon="🏭"
          color="purple"
          change={
            inventoryStats.activeBins !== null
              ? `${inventoryStats.activeBins} Active Bins`
              : "Bins Unavailable"
          }
          changeType="neutral"
          changePeriod={
            binDistributions.length > 0
              ? binDistributions.map((b) => b.binCode).join(" · ")
              : inventoryStats.activeBins !== null
              ? "No active bins configured"
              : "Warehouse topology unavailable"
          }
          onClick={() => onNavigate("warehouse")}
        />
      </div>

      {/* Quick Launch & Operational Workflows */}
      <Card
        title="Operational Workspaces & Fast Launch"
        subtitle="Direct access to operational modules and cashier point-of-sale"
        icon="🚀"
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
            gap: "var(--space-4)",
          }}
        >
          <div
            onClick={() => onNavigate("sales")}
            style={{
              padding: "var(--space-4)",
              background: "var(--bg-subtle)",
              borderRadius: "var(--radius-lg)",
              border: "1px solid var(--border-base)",
              cursor: "pointer",
              transition: "all var(--transition-fast)",
            }}
            onMouseEnter={(e) => (e.currentTarget.style.borderColor = "var(--primary)")}
            onMouseLeave={(e) => (e.currentTarget.style.borderColor = "var(--border-base)")}
          >
            <div style={{ fontSize: 24, marginBottom: 8 }}>💳</div>
            <div style={{ fontWeight: 700, fontSize: "var(--font-size-md)", color: "var(--text-primary)" }}>
              Sales & POS Cashier
            </div>
            <p style={{ fontSize: "var(--font-size-xs)", color: "var(--text-muted)", margin: "4px 0 12px" }}>
              Fast barcoded cashier checkout, customer orders, and quotes.
            </p>
            <span style={{ fontSize: "var(--font-size-xs)", color: "var(--primary)", fontWeight: 600 }}>
              Launch Cashier →
            </span>
          </div>

          <div
            onClick={() => onNavigate("inventory")}
            style={{
              padding: "var(--space-4)",
              background: "var(--bg-subtle)",
              borderRadius: "var(--radius-lg)",
              border: "1px solid var(--border-base)",
              cursor: "pointer",
              transition: "all var(--transition-fast)",
            }}
            onMouseEnter={(e) => (e.currentTarget.style.borderColor = "var(--primary)")}
            onMouseLeave={(e) => (e.currentTarget.style.borderColor = "var(--border-base)")}
          >
            <div style={{ fontSize: 24, marginBottom: 8 }}>📦</div>
            <div style={{ fontWeight: 700, fontSize: "var(--font-size-md)", color: "var(--text-primary)" }}>
              Catalog & Inventory
            </div>
            <p style={{ fontSize: "var(--font-size-xs)", color: "var(--text-muted)", margin: "4px 0 12px" }}>
              Stock valuation, FIFO layers, bin transfers, and stock counts.
            </p>
            <span style={{ fontSize: "var(--font-size-xs)", color: "var(--primary)", fontWeight: 600 }}>
              View Stock →
            </span>
          </div>

          <div
            onClick={() => onNavigate("purchasing")}
            style={{
              padding: "var(--space-4)",
              background: "var(--bg-subtle)",
              borderRadius: "var(--radius-lg)",
              border: "1px solid var(--border-base)",
              cursor: "pointer",
              transition: "all var(--transition-fast)",
            }}
            onMouseEnter={(e) => (e.currentTarget.style.borderColor = "var(--primary)")}
            onMouseLeave={(e) => (e.currentTarget.style.borderColor = "var(--border-base)")}
          >
            <div style={{ fontSize: 24, marginBottom: 8 }}>🛒</div>
            <div style={{ fontWeight: 700, fontSize: "var(--font-size-md)", color: "var(--text-primary)" }}>
              Purchasing & GRN
            </div>
            <p style={{ fontSize: "var(--font-size-xs)", color: "var(--text-muted)", margin: "4px 0 12px" }}>
              Suppliers, purchase orders, 3-way invoice matching, and inspections.
            </p>
            <span style={{ fontSize: "var(--font-size-xs)", color: "var(--primary)", fontWeight: 600 }}>
              Manage POs →
            </span>
          </div>

          <div
            onClick={() => onNavigate("finance")}
            style={{
              padding: "var(--space-4)",
              background: "var(--bg-subtle)",
              borderRadius: "var(--radius-lg)",
              border: "1px solid var(--border-base)",
              cursor: "pointer",
              transition: "all var(--transition-fast)",
            }}
            onMouseEnter={(e) => (e.currentTarget.style.borderColor = "var(--primary)")}
            onMouseLeave={(e) => (e.currentTarget.style.borderColor = "var(--border-base)")}
          >
            <div style={{ fontSize: 24, marginBottom: 8 }}>💰</div>
            <div style={{ fontWeight: 700, fontSize: "var(--font-size-md)", color: "var(--text-primary)" }}>
              Finance & Ledger
            </div>
            <p style={{ fontSize: "var(--font-size-xs)", color: "var(--text-muted)", margin: "4px 0 12px" }}>
              Real-time Trial Balance, Chart of Accounts, and journal entries.
            </p>
            <span style={{ fontSize: "var(--font-size-xs)", color: "var(--primary)", fontWeight: 600 }}>
              Open Ledger →
            </span>
          </div>
        </div>
      </Card>

      {/* Two Column Layout: Financial Visual Trend & Activity Feed */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(450px, 1fr))",
          gap: "var(--space-6)",
        }}
      >
        {/* Inventory & Asset Valuation Visual Distribution */}
        <Card
          title="Stock Distribution by Bin"
          subtitle={`Physical bin quantities (Total: ${
            inventoryStats.totalUnits !== null ? `${inventoryStats.totalUnits} EA` : "Unavailable"
          })`}
          icon="📊"
        >
          <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-4)" }}>
            {binApiError ? (
              <div
                style={{
                  padding: "var(--space-4)",
                  textAlign: "center",
                  color: "var(--danger-text, #b91c1c)",
                  background: "var(--danger-bg, #fef2f2)",
                  border: "1px solid var(--danger-border, #fecaca)",
                  borderRadius: "var(--radius-md)",
                  fontSize: "var(--font-size-sm)",
                }}
              >
                ⚠️ Unable to load physical bin quantities: inventory API unavailable.
              </div>
            ) : binDistributions.length === 0 ? (
              <div
                style={{
                  padding: "var(--space-4)",
                  textAlign: "center",
                  color: "var(--text-muted)",
                  fontSize: "var(--font-size-sm)",
                }}
              >
                {loading ? "Loading bin distribution..." : "No physical inventory recorded in active bins."}
              </div>
            ) : (
              binDistributions.map((bin) => (
                <div key={bin.binId || bin.binCode}>
                  <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 6 }}>
                    <span style={{ fontSize: "var(--font-size-sm)", fontWeight: 600 }}>
                      {bin.warehouseCode} / {bin.binCode}
                    </span>
                    <span
                      style={{
                        fontSize: "var(--font-size-sm)",
                        fontWeight: 700,
                        color: bin.quantity > 0 ? "var(--primary)" : "var(--text-muted)",
                      }}
                    >
                      {bin.quantity} EA ({bin.percentage}%)
                    </span>
                  </div>
                  <div style={{ height: 10, background: "var(--bg-subtle)", borderRadius: 5, overflow: "hidden" }}>
                    <div
                      style={{
                        height: "100%",
                        width: `${Math.min(100, Math.max(0, bin.percentage))}%`,
                        background: bin.quantity > 0 ? "var(--primary)" : "var(--neutral-400)",
                        borderRadius: 5,
                        transition: "width 0.3s ease",
                      }}
                    />
                  </div>
                </div>
              ))
            )}

            {/* Reconciliation Banner */}
            {currencyState.isCurrencyUnresolved ? (
              <div
                style={{
                  marginTop: "var(--space-3)",
                  padding: "var(--space-3)",
                  background: "var(--warning-bg, #fffbeb)",
                  border: "1px solid var(--warning-border, #fde68a)",
                  borderRadius: "var(--radius-md)",
                  display: "flex",
                  alignItems: "center",
                  gap: "var(--space-3)",
                }}
              >
                <span style={{ fontSize: 18 }}>⚠️</span>
                <div style={{ fontSize: "var(--font-size-xs)", color: "var(--warning-text, #92400e)" }}>
                  <strong>Reconciliation: {currencyState.reconciliationLabel}.</strong>{" "}
                  Source ledger balance: {currencyState.sourceLedgerBalancesText || "Unavailable"} · Company base currency:{" "}
                  {currencyState.companyBaseCurrency || "Unspecified"} · Functional GL balance:{" "}
                  {currencyState.isGlCurrencyAligned ? formatMoney(financeStats.gl1200Debit) : "Unavailable"}.
                </div>
              </div>
            ) : inventoryStats.totalUnits === null || financeStats.variance === null ? (
              <div
                style={{
                  marginTop: "var(--space-3)",
                  padding: "var(--space-3)",
                  background: "var(--bg-subtle)",
                  border: "1px solid var(--border-base)",
                  borderRadius: "var(--radius-md)",
                  display: "flex",
                  alignItems: "center",
                  gap: "var(--space-3)",
                }}
              >
                <span style={{ fontSize: 18 }}>ℹ️</span>
                <div style={{ fontSize: "var(--font-size-xs)", color: "var(--text-muted)" }}>
                  <strong>Reconciliation Status:</strong> Awaiting authoritative valuation and general ledger responses to complete reconciliation.
                </div>
              </div>
            ) : isBalanced ? (
              <div
                style={{
                  marginTop: "var(--space-3)",
                  padding: "var(--space-3)",
                  background: "var(--success-bg)",
                  border: "1px solid var(--success-border)",
                  borderRadius: "var(--radius-md)",
                  display: "flex",
                  alignItems: "center",
                  gap: "var(--space-3)",
                }}
              >
                <span style={{ fontSize: 18 }}>✓</span>
                <div style={{ fontSize: "var(--font-size-xs)", color: "var(--success-text)" }}>
                  <strong>Physical Inventory Reconciled:</strong> Book balance matches physical count (
                  {inventoryStats.totalUnits} EA across {binDistributions.length} bins). Zero reconciliation variance.
                </div>
              </div>
            ) : (
              <div
                style={{
                  marginTop: "var(--space-3)",
                  padding: "var(--space-3)",
                  background: "var(--warning-bg, #fffbeb)",
                  border: "1px solid var(--warning-border, #fde68a)",
                  borderRadius: "var(--radius-md)",
                  display: "flex",
                  alignItems: "center",
                  gap: "var(--space-3)",
                }}
              >
                <span style={{ fontSize: 18 }}>⚠️</span>
                <div style={{ fontSize: "var(--font-size-xs)", color: "var(--warning-text, #92400e)" }}>
                  <strong>Reconciliation Variance Detected:</strong> Physical stock is {inventoryStats.totalUnits} EA. Variance between FIFO valuation and GL 1200 is {formatMoney(financeStats.variance)}.
                </div>
              </div>
            )}
          </div>
        </Card>

        {/* Governance & Audit Stream */}
        <Card
          title="Operational Audit & Status Feed"
          subtitle="Live operational events and audit log"
          icon="📋"
        >
          <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
            {auditEvents.length === 0 ? (
              <div
                style={{
                  padding: "var(--space-6)",
                  textAlign: "center",
                  color: "var(--text-muted)",
                  background: "var(--bg-subtle)",
                  borderRadius: "var(--radius-md)",
                  border: "1px dashed var(--border-base)",
                  fontSize: "var(--font-size-sm)",
                }}
              >
                {loading ? "Loading audit events..." : "No recent audit events recorded for this company context."}
              </div>
            ) : (
              auditEvents.slice(0, 5).map((evt) => (
                <div
                  key={evt.eventId}
                  style={{
                    display: "flex",
                    gap: "var(--space-3)",
                    padding: "var(--space-3)",
                    background: "var(--bg-surface)",
                    borderRadius: "var(--radius-md)",
                    border: "1px solid var(--border-light)",
                  }}
                >
                  <div style={{ paddingTop: 2 }}>
                    <Badge variant="info" dot>
                      {formatEventTime(evt.occurredAt)}
                    </Badge>
                  </div>
                  <div style={{ flex: 1 }}>
                    <div style={{ fontWeight: 600, fontSize: "var(--font-size-sm)", color: "var(--text-primary)" }}>
                      {formatEventTitle(evt.eventCode)}
                    </div>
                    <div style={{ fontSize: "var(--font-size-xs)", color: "var(--text-muted)", marginTop: 2 }}>
                      {evt.actorEmail ? `By ${evt.actorEmail} · ` : ""}
                      {evt.entityType} {evt.entityId ? `[${evt.entityId.slice(0, 8)}]` : ""}
                    </div>
                  </div>
                </div>
              ))
            )}
          </div>
        </Card>
      </div>
    </div>
  );
}
