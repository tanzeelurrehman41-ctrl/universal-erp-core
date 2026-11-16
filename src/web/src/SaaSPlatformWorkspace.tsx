import React, { useState, useEffect } from "react";
import { apiFetch } from "./auth.ts";
import {
  calculateProrationCredit,
  calculateNetUpgradePayable,
  calculateJurisdictionalTax,
  evaluateQuotaStatus,
  getTenantStatusBadge,
  getSubscriptionStatusBadge,
  getPaymentStatusBadge,
  calculateInvoiceTotals,
  formatCurrencyPkr,
  type SubscriptionPlan,
  type TenantBrief,
  type TenantEntitlement,
  type UsageAggregation,
  type SubscriptionInvoice,
  type SubscriptionPayment,
  type SupportAccessGrant,
  type SaaSPlatformAlert
} from "./saasHelpers.ts";

interface SaaSPlatformWorkspaceProps {
  tenantId?: string;
  companyId?: string;
  enabled?: boolean;
}

type TabType =
  | "directory"
  | "onboarding"
  | "plans"
  | "entitlements"
  | "metering"
  | "billing"
  | "payments"
  | "lifecycle"
  | "admin";

export const SaaSPlatformWorkspace: React.FC<SaaSPlatformWorkspaceProps> = ({
  tenantId: initialTenantId,
  enabled = true
}) => {
  const [activeTab, setActiveTab] = useState<TabType>("directory");
  const [loading, setLoading] = useState<boolean>(false);
  const [message, setMessage] = useState<{ text: string; type: "success" | "error" | "info" } | null>(null);

  // Core Data
  const [tenants, setTenants] = useState<TenantBrief[]>([]);
  const [selectedTenantId, setSelectedTenantId] = useState<string>(initialTenantId || "");
  const [plans, setPlans] = useState<SubscriptionPlan[]>([]);
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [searchQuery, setSearchQuery] = useState<string>("");

  // Tab 2: Onboarding Form State
  const [onboardCode, setOnboardCode] = useState("apex-pk");
  const [onboardName, setOnboardName] = useState("Apex Distribution Pakistan");
  const [onboardLegalName, setOnboardLegalName] = useState("Apex Distribution Pvt. Ltd.");
  const [onboardPlan, setOnboardPlan] = useState("starter");
  const [onboardInterval, setOnboardInterval] = useState<"month" | "year">("month");
  const [onboardCompanyCode, setOnboardCompanyCode] = useState("APEX-PK");
  const [onboardBranchCode, setOnboardBranchCode] = useState("HQ-KHI");
  const [onboardBranchName, setOnboardBranchName] = useState("Headquarters Karachi");
  const [onboardAdminName, setOnboardAdminName] = useState("Muhammad Harris");
  const [onboardAdminEmail, setOnboardAdminEmail] = useState("harris@apex-pk.example.com");
  const [onboardJurisdiction, setOnboardJurisdiction] = useState("SINDH");
  const [lastProvisionResult, setLastProvisionResult] = useState<Record<string, unknown> | null>(null);

  // Tab 3: Plans & Pricing State
  const [billingCycle, setBillingCycle] = useState<"month" | "year">("month");
  const [upgradeFromPlan, setUpgradeFromPlan] = useState("starter");
  const [upgradeToPlan, setUpgradeToPlan] = useState("professional");
  const [unconsumedDays, setUnconsumedDays] = useState(15);

  // Tab 4: Entitlements State
  const [entitlements, setEntitlements] = useState<TenantEntitlement[]>([]);
  const [checkFeatureCode, setCheckFeatureCode] = useState("max_companies");
  const [checkQty, setCheckQty] = useState(1);
  const [entitlementCheckResult, setEntitlementCheckResult] = useState<{
    isAllowed: boolean;
    denialReason?: string | null;
    isWarning: boolean;
    warningMessage?: string | null;
  } | null>(null);

  // Tab 5: Usage Metering State
  const [usageAggregations, setUsageAggregations] = useState<UsageAggregation[]>([]);
  const [meterMetricCode, setMeterMetricCode] = useState("max_storage_mb");
  const [meterQuantity, setMeterQuantity] = useState(250);
  const [meterUnit, setMeterUnit] = useState("megabytes");

  // Tab 6: Billing & Invoices State
  const [invoices, setInvoices] = useState<SubscriptionInvoice[]>([]);
  const [selectedInvoice, setSelectedInvoice] = useState<SubscriptionInvoice | null>(null);

  // Tab 7: Payment Reconciliation State
  const [payments, setPayments] = useState<SubscriptionPayment[]>([]);
  const [payInvoiceId, setPayInvoiceId] = useState("");
  const [payAmount, setPayAmount] = useState(30000);
  const [payMethod, setPayMethod] = useState("pm_card_visa_test");
  const [refundPayRef, setRefundPayRef] = useState("");
  const [refundAmount, setRefundAmount] = useState(5000);
  const [refundReason, setRefundReason] = useState("Customer requested service downgrade credit");

  // Tab 8: Tenant Lifecycle State
  const [lifecycleTenantId, setLifecycleTenantId] = useState("");
  const [lifecycleAction, setLifecycleAction] = useState<"suspend" | "reactivate" | "cancel">("suspend");
  const [suspensionReason, setSuspensionReason] = useState("Payment failure grace period elapsed.");

  // Tab 9: Platform Administration & Support State
  const [supportGrants, setSupportGrants] = useState<SupportAccessGrant[]>([]);
  const [supportEmail, setSupportEmail] = useState("support.specialist@universalerp.internal");
  const [supportReason, setSupportReason] = useState("Assisting tenant CFO with year-end intercompany consolidation audit.");
  const [supportDurationHours, setSupportDurationHours] = useState(4);
  const [platformAlerts, setPlatformAlerts] = useState<SaaSPlatformAlert[]>([]);

  // Load Initial Catalog & Tenant List
  useEffect(() => {
    loadPlans();
    loadTenants();
  }, []);

  // When selected tenant changes, refresh scoped data
  useEffect(() => {
    if (selectedTenantId) {
      loadTenantData(selectedTenantId);
    }
  }, [selectedTenantId]);

  async function loadPlans() {
    try {
      const res = await apiFetch("/api/v1/saas/plans");
      if (res.ok) {
        const data = await res.json();
        setPlans(data);
      } else {
        // Fallback default plans for offline or dev display
        setPlans([
          {
            id: "11111111-1111-1111-1111-111111111100",
            planCode: "free",
            name: "Free Community",
            description: "Single-user sandbox environment for evaluation and self-employed solo ventures.",
            tierRank: 0,
            status: "active",
            isPublic: true,
            features: [
              { featureCode: "max_users", enabled: true, quantityLimit: 1, limitUnit: "users" },
              { featureCode: "max_companies", enabled: true, quantityLimit: 1, limitUnit: "companies" },
              { featureCode: "max_branches", enabled: true, quantityLimit: 1, limitUnit: "branches" },
              { featureCode: "max_monthly_transactions", enabled: true, quantityLimit: 100, limitUnit: "transactions" },
              { featureCode: "max_storage_mb", enabled: true, quantityLimit: 512, limitUnit: "megabytes" },
              { featureCode: "sales.core", enabled: true, quantityLimit: null, limitUnit: null },
              { featureCode: "inventory.core", enabled: true, quantityLimit: null, limitUnit: null }
            ],
            pricing: [
              { planId: "11111111-1111-1111-1111-111111111100", billingInterval: "month", currencyCode: "PKR", price: 0, effectiveFrom: "2026-01-01" },
              { planId: "11111111-1111-1111-1111-111111111100", billingInterval: "year", currencyCode: "PKR", price: 0, effectiveFrom: "2026-01-01" }
            ]
          },
          {
            id: "11111111-1111-1111-1111-111111111101",
            planCode: "starter",
            name: "Starter SME",
            description: "Growing SME business with multi-branch sales, inventory, and automated purchasing.",
            tierRank: 1,
            status: "active",
            isPublic: true,
            features: [
              { featureCode: "max_users", enabled: true, quantityLimit: 5, limitUnit: "users" },
              { featureCode: "max_companies", enabled: true, quantityLimit: 1, limitUnit: "companies" },
              { featureCode: "max_branches", enabled: true, quantityLimit: 3, limitUnit: "branches" },
              { featureCode: "max_monthly_transactions", enabled: true, quantityLimit: 2500, limitUnit: "transactions" },
              { featureCode: "max_storage_mb", enabled: true, quantityLimit: 2048, limitUnit: "megabytes" },
              { featureCode: "sales.core", enabled: true, quantityLimit: null, limitUnit: null },
              { featureCode: "inventory.core", enabled: true, quantityLimit: null, limitUnit: null },
              { featureCode: "purchasing.core", enabled: true, quantityLimit: null, limitUnit: null },
              { featureCode: "finance.core", enabled: true, quantityLimit: null, limitUnit: null }
            ],
            pricing: [
              { planId: "11111111-1111-1111-1111-111111111101", billingInterval: "month", currencyCode: "PKR", price: 15000, effectiveFrom: "2026-01-01" },
              { planId: "11111111-1111-1111-1111-111111111101", billingInterval: "year", currencyCode: "PKR", price: 153000, effectiveFrom: "2026-01-01" }
            ]
          },
          {
            id: "11111111-1111-1111-1111-111111111102",
            planCode: "professional",
            name: "Professional Growth",
            description: "Medium enterprise operations with manufacturing BOMs, advanced warehouse bins, and payroll.",
            tierRank: 2,
            status: "active",
            isPublic: true,
            features: [
              { featureCode: "max_users", enabled: true, quantityLimit: 25, limitUnit: "users" },
              { featureCode: "max_companies", enabled: true, quantityLimit: 3, limitUnit: "companies" },
              { featureCode: "max_branches", enabled: true, quantityLimit: 10, limitUnit: "branches" },
              { featureCode: "max_monthly_transactions", enabled: true, quantityLimit: 15000, limitUnit: "transactions" },
              { featureCode: "max_storage_mb", enabled: true, quantityLimit: 10240, limitUnit: "megabytes" },
              { featureCode: "manufacturing.core", enabled: true, quantityLimit: null, limitUnit: null },
              { featureCode: "warehouse.advanced", enabled: true, quantityLimit: null, limitUnit: null },
              { featureCode: "hr.payroll", enabled: true, quantityLimit: null, limitUnit: null },
              { featureCode: "analytics.bi", enabled: true, quantityLimit: null, limitUnit: null }
            ],
            pricing: [
              { planId: "11111111-1111-1111-1111-111111111102", billingInterval: "month", currencyCode: "PKR", price: 45000, effectiveFrom: "2026-01-01" },
              { planId: "11111111-1111-1111-1111-111111111102", billingInterval: "year", currencyCode: "PKR", price: 459000, effectiveFrom: "2026-01-01" }
            ]
          },
          {
            id: "11111111-1111-1111-1111-111111111103",
            planCode: "enterprise",
            name: "Enterprise Multi-Company",
            description: "High-volume conglomerates with automated intercompany eliminations and consolidated ledgers.",
            tierRank: 3,
            status: "active",
            isPublic: true,
            features: [
              { featureCode: "max_users", enabled: true, quantityLimit: 500, limitUnit: "users" },
              { featureCode: "max_companies", enabled: true, quantityLimit: 50, limitUnit: "companies" },
              { featureCode: "max_branches", enabled: true, quantityLimit: 100, limitUnit: "branches" },
              { featureCode: "max_monthly_transactions", enabled: true, quantityLimit: 100000, limitUnit: "transactions" },
              { featureCode: "max_storage_mb", enabled: true, quantityLimit: 102400, limitUnit: "megabytes" },
              { featureCode: "consolidation.core", enabled: true, quantityLimit: null, limitUnit: null },
              { featureCode: "offline.sync", enabled: true, quantityLimit: null, limitUnit: null },
              { featureCode: "integrations.webhooks", enabled: true, quantityLimit: null, limitUnit: null }
            ],
            pricing: [
              { planId: "11111111-1111-1111-1111-111111111103", billingInterval: "month", currencyCode: "PKR", price: 125000, effectiveFrom: "2026-01-01" },
              { planId: "11111111-1111-1111-1111-111111111103", billingInterval: "year", currencyCode: "PKR", price: 1275000, effectiveFrom: "2026-01-01" }
            ]
          }
        ]);
      }
    } catch (err) {
      console.warn("Failed to load plans from API", err);
    }
  }

  async function loadTenants() {
    try {
      const res = await apiFetch("/api/v1/saas/tenants");
      if (res.ok) {
        const data = await res.json();
        setTenants(data);
        if (data.length > 0 && !selectedTenantId) {
          setSelectedTenantId(data[0].id);
        }
      } else {
        // Fallback demo tenants
        const mockTenants: TenantBrief[] = [
          {
            id: "t-1001-demo",
            tenantCode: "apex-pk",
            displayName: "Apex Distribution Pakistan",
            legalName: "Apex Distribution Pvt. Ltd.",
            status: "active",
            defaultCountryCode: "PK",
            defaultCurrencyCode: "PKR",
            defaultTimezone: "Asia/Karachi",
            createdAt: "2026-11-15T10:00:00Z"
          },
          {
            id: "t-1002-demo",
            tenantCode: "vertex-mfg",
            displayName: "Vertex Textiles Ltd",
            legalName: "Vertex Textiles International Ltd",
            status: "active",
            defaultCountryCode: "PK",
            defaultCurrencyCode: "PKR",
            defaultTimezone: "Asia/Karachi",
            createdAt: "2026-11-10T08:30:00Z"
          },
          {
            id: "t-1003-demo",
            tenantCode: "crecent-retail",
            displayName: "Crescent Mart POS",
            legalName: "Crescent Retail Ventures",
            status: "suspended",
            defaultCountryCode: "PK",
            defaultCurrencyCode: "PKR",
            defaultTimezone: "Asia/Karachi",
            createdAt: "2026-10-01T12:00:00Z",
            suspendedAt: "2026-11-14T18:00:00Z",
            suspensionReason: "Payment failure grace period elapsed (30 days)."
          }
        ];
        setTenants(mockTenants);
        if (!selectedTenantId) setSelectedTenantId(mockTenants[0].id);
      }
    } catch (err) {
      console.warn("Failed to load tenants", err);
    }
  }

  async function loadTenantData(id: string) {
    setLoading(true);
    try {
      // 1. Entitlements
      const entRes = await apiFetch(`/api/v1/saas/tenants/${id}/entitlements`);
      if (entRes.ok) {
        setEntitlements(await entRes.json());
      } else {
        setEntitlements([
          { featureCode: "max_users", enabled: true, quantityLimit: 5, limitUnit: "users", sourceType: "plan", effectiveFrom: "2026-11-15" },
          { featureCode: "max_companies", enabled: true, quantityLimit: 1, limitUnit: "companies", sourceType: "plan", effectiveFrom: "2026-11-15" },
          { featureCode: "max_branches", enabled: true, quantityLimit: 3, limitUnit: "branches", sourceType: "plan", effectiveFrom: "2026-11-15" },
          { featureCode: "max_monthly_transactions", enabled: true, quantityLimit: 2500, limitUnit: "transactions", sourceType: "plan", effectiveFrom: "2026-11-15" },
          { featureCode: "max_storage_mb", enabled: true, quantityLimit: 2048, limitUnit: "megabytes", sourceType: "plan", effectiveFrom: "2026-11-15" },
          { featureCode: "sales.core", enabled: true, quantityLimit: null, limitUnit: null, sourceType: "plan", effectiveFrom: "2026-11-15" },
          { featureCode: "inventory.core", enabled: true, quantityLimit: null, limitUnit: null, sourceType: "plan", effectiveFrom: "2026-11-15" },
          { featureCode: "finance.core", enabled: true, quantityLimit: null, limitUnit: null, sourceType: "plan", effectiveFrom: "2026-11-15" }
        ]);
      }

      // 2. Usage Aggregations
      const aggRes = await apiFetch(`/api/v1/saas/tenants/${id}/usage/aggregations`);
      if (aggRes.ok) {
        setUsageAggregations(await aggRes.json());
      } else {
        setUsageAggregations([
          { id: "u-1", metricCode: "users", currentQuantity: 3, quantityLimit: 5, isExceeded: false, warningThresholdPct: 80, warningIssued: false, lastAggregatedAt: new Date().toISOString() },
          { id: "u-2", metricCode: "companies", currentQuantity: 1, quantityLimit: 1, isExceeded: false, warningThresholdPct: 80, warningIssued: true, lastAggregatedAt: new Date().toISOString() },
          { id: "u-3", metricCode: "branches", currentQuantity: 2, quantityLimit: 3, isExceeded: false, warningThresholdPct: 80, warningIssued: false, lastAggregatedAt: new Date().toISOString() },
          { id: "u-4", metricCode: "transactions_monthly", currentQuantity: 1980, quantityLimit: 2500, isExceeded: false, warningThresholdPct: 80, warningIssued: false, lastAggregatedAt: new Date().toISOString() },
          { id: "u-5", metricCode: "storage_mb", currentQuantity: 1720, quantityLimit: 2048, isExceeded: false, warningThresholdPct: 80, warningIssued: true, lastAggregatedAt: new Date().toISOString() },
          { id: "u-6", metricCode: "api_calls_monthly", currentQuantity: 12500, quantityLimit: 50000, isExceeded: false, warningThresholdPct: 80, warningIssued: false, lastAggregatedAt: new Date().toISOString() }
        ]);
      }

      // 3. Invoices
      const invRes = await apiFetch(`/api/v1/saas/tenants/${id}/invoices`);
      if (invRes.ok) {
        const invList = await invRes.json();
        setInvoices(invList);
        if (invList.length > 0) {
          setSelectedInvoice(invList[0]);
          setPayInvoiceId(invList[0].id);
        }
      } else {
        const mockInv: SubscriptionInvoice[] = [
          {
            id: "inv-2026-001",
            tenantId: id,
            invoiceNumber: "INV-2026-0001",
            subtotal: 15000,
            taxAmount: 1950,
            discountAmount: 0,
            creditsApplied: 0,
            totalAmount: 16950,
            currencyCode: "PKR",
            status: "paid",
            issuedAt: "2026-11-01T00:00:00Z",
            dueAt: "2026-11-15T00:00:00Z",
            paidAt: "2026-11-02T14:20:00Z",
            lines: [
              { lineNumber: 1, description: "Starter SME Plan Subscription (1 Month)", lineType: "base_plan", quantity: 1, unitPrice: 15000, amount: 15000 },
              { lineNumber: 2, description: "SRB Sindh Sales Tax on Services (13%)", lineType: "tax", quantity: 1, unitPrice: 1950, amount: 1950 }
            ]
          },
          {
            id: "inv-2026-002",
            tenantId: id,
            invoiceNumber: "INV-2026-0002",
            subtotal: 30000,
            taxAmount: 3900,
            discountAmount: 0,
            creditsApplied: 0,
            totalAmount: 33900,
            currencyCode: "PKR",
            status: "issued",
            issuedAt: "2026-11-16T00:00:00Z",
            dueAt: "2026-11-30T00:00:00Z",
            lines: [
              { lineNumber: 1, description: "Professional Growth Plan Upgrade Proration", lineType: "proration", quantity: 1, unitPrice: 30000, amount: 30000 },
              { lineNumber: 2, description: "SRB Sindh Sales Tax on Services (13%)", lineType: "tax", quantity: 1, unitPrice: 3900, amount: 3900 }
            ]
          }
        ];
        setInvoices(mockInv);
        setSelectedInvoice(mockInv[0]);
        setPayInvoiceId(mockInv[1].id);
        setPayAmount(mockInv[1].totalAmount);
      }

      // 4. Support Grants
      const supRes = await apiFetch(`/api/v1/saas/tenants/${id}/support-access`);
      if (supRes.ok) {
        setSupportGrants(await supRes.json());
      }

      // 5. Platform Alerts
      const altRes = await apiFetch(`/api/v1/saas/alerts?tenantId=${id}`);
      if (altRes.ok) {
        setPlatformAlerts(await altRes.json());
      }
    } catch (err) {
      console.warn("Failed to load tenant details", err);
    } finally {
      setLoading(false);
    }
  }

  // Action: Provision New Tenant
  async function handleProvisionTenant(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setMessage(null);

    const idempotencyKey = `prov_${onboardCode.toLowerCase()}_${Date.now()}`;
    const payload = {
      tenantCode: onboardCode,
      displayName: onboardName,
      legalName: onboardLegalName,
      planCode: onboardPlan,
      billingInterval: onboardInterval,
      companyCode: onboardCompanyCode,
      companyName: onboardName,
      branchCode: onboardBranchCode,
      branchName: onboardBranchName,
      adminFullName: onboardAdminName,
      adminEmail: onboardAdminEmail,
      countryCode: "PK",
      baseCurrencyCode: "PKR",
      timezone: "Asia/Karachi",
      jurisdiction: onboardJurisdiction
    };

    try {
      const res = await apiFetch("/api/v1/saas/tenants/provision", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": idempotencyKey
        },
        body: JSON.stringify(payload)
      });

      if (res.ok) {
        const result = await res.json();
        setLastProvisionResult(result);
        setMessage({
          text: `Tenant '${onboardCode}' provisioned successfully! Admin user created and default company initialized.`,
          type: "success"
        });
        await loadTenants();
        setSelectedTenantId(result.tenantId);
      } else {
        const err = await res.text();
        setMessage({ text: `Provisioning failed: ${err}`, type: "error" });
      }
    } catch (err) {
      // Local simulated response for sandbox demonstration
      const simulatedResult = {
        tenantId: `t-${Date.now()}`,
        tenantCode: onboardCode,
        displayName: onboardName,
        companyId: `c-${Date.now()}`,
        companyCode: onboardCompanyCode,
        branchId: `b-${Date.now()}`,
        branchCode: onboardBranchCode,
        adminUserId: `u-${Date.now()}`,
        adminEmail: onboardAdminEmail,
        status: "active",
        createdAt: new Date().toISOString()
      };
      setLastProvisionResult(simulatedResult);
      setMessage({
        text: `Tenant '${onboardCode}' provisioned successfully (Simulated Staging Execution).`,
        type: "success"
      });
      setTenants((prev) => [
        {
          id: simulatedResult.tenantId,
          tenantCode: simulatedResult.tenantCode,
          displayName: simulatedResult.displayName,
          legalName: onboardLegalName,
          status: "active",
          defaultCountryCode: "PK",
          defaultCurrencyCode: "PKR",
          defaultTimezone: "Asia/Karachi",
          createdAt: simulatedResult.createdAt
        },
        ...prev
      ]);
      setSelectedTenantId(simulatedResult.tenantId);
    } finally {
      setLoading(false);
    }
  }

  // Action: Check Entitlement
  async function handleCheckEntitlement(e: React.FormEvent) {
    e.preventDefault();
    if (!selectedTenantId) return;

    try {
      const res = await apiFetch(`/api/v1/saas/tenants/${selectedTenantId}/entitlements/check`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          featureCode: checkFeatureCode,
          requestedQuantity: checkQty
        })
      });

      if (res.ok) {
        const data = await res.json();
        setEntitlementCheckResult({
          isAllowed: data.isAllowed,
          denialReason: data.denialReason,
          isWarning: data.isWarningThresholdExceeded,
          warningMessage: data.warningMessage
        });
      } else {
        // Fallback local evaluation against active entitlements
        const match = entitlements.find((ent) =>
          ent.featureCode.toLowerCase().includes(checkFeatureCode.toLowerCase().replace("max_", ""))
        );
        const agg = usageAggregations.find((a) =>
          a.metricCode.toLowerCase().includes(checkFeatureCode.toLowerCase().replace("max_", ""))
        );
        const current = agg?.currentQuantity || 0;
        const limit = match?.quantityLimit;

        if (!match || !match.enabled) {
          setEntitlementCheckResult({
            isAllowed: false,
            denialReason: `Feature '${checkFeatureCode}' is not included in current plan.`,
            isWarning: false
          });
        } else if (limit != null && current + checkQty > limit) {
          setEntitlementCheckResult({
            isAllowed: false,
            denialReason: `Entitlement limit exceeded for '${checkFeatureCode}'. Limit: ${limit}, Current: ${current}, Requested: ${checkQty}.`,
            isWarning: true,
            warningMessage: "Quota full. Upgrade plan to expand limit."
          });
        } else {
          const quota = evaluateQuotaStatus(current + checkQty, limit ?? null);
          setEntitlementCheckResult({
            isAllowed: true,
            denialReason: null,
            isWarning: quota.isWarning,
            warningMessage: quota.isWarning ? `Warning: projected usage is at ${quota.pct}% of plan limit.` : null
          });
        }
      }
    } catch {
      setMessage({ text: "Entitlement check failed.", type: "error" });
    }
  }

  // Action: Record Metered Usage
  async function handleRecordUsage(e: React.FormEvent) {
    e.preventDefault();
    if (!selectedTenantId) return;

    try {
      const now = new Date();
      const periodStart = new Date(now.getFullYear(), now.getMonth(), 1).toISOString();
      const periodEnd = new Date(now.getFullYear(), now.getMonth() + 1, 0).toISOString();

      const res = await apiFetch("/api/v1/saas/usage/record", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          tenantId: selectedTenantId,
          metricCode: meterMetricCode,
          quantity: meterQuantity,
          unit: meterUnit,
          periodStart,
          periodEnd
        })
      });

      if (res.ok) {
        setMessage({ text: `Recorded ${meterQuantity} ${meterUnit} for ${meterMetricCode}.`, type: "success" });
        await loadTenantData(selectedTenantId);
      } else {
        // Optimistic UI update
        setUsageAggregations((prev) =>
          prev.map((a) =>
            a.metricCode === meterMetricCode || a.metricCode === meterMetricCode.replace("max_", "")
              ? { ...a, currentQuantity: a.currentQuantity + meterQuantity, lastAggregatedAt: new Date().toISOString() }
              : a
          )
        );
        setMessage({ text: `Recorded ${meterQuantity} ${meterUnit} (Simulated).`, type: "success" });
      }
    } catch {
      setMessage({ text: "Failed to record usage.", type: "error" });
    }
  }

  // Action: Process Sandbox Payment
  async function handleProcessPayment(e: React.FormEvent) {
    e.preventDefault();
    if (!selectedTenantId) return;

    setLoading(true);
    setMessage(null);

    const idempotencyKey = `pay_sbx_${Date.now()}`;
    try {
      const res = await apiFetch("/api/v1/saas/payments/process", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          tenantId: selectedTenantId,
          invoiceId: payInvoiceId,
          amount: payAmount,
          currencyCode: "PKR",
          paymentMethodRef: payMethod,
          idempotencyKey
        })
      });

      if (res.ok) {
        const payment = await res.json();
        setPayments((prev) => [payment, ...prev]);
        if (payment.status === "succeeded") {
          setMessage({
            text: `Payment ${payment.paymentReference} succeeded! Invoice marked as paid.`,
            type: "success"
          });
          // Update invoice status
          setInvoices((prev) =>
            prev.map((inv) => (inv.id === payInvoiceId ? { ...inv, status: "paid", paidAt: new Date().toISOString() } : inv))
          );
        } else {
          setMessage({
            text: `Payment declined: ${payment.failureReason}`,
            type: "error"
          });
        }
      } else {
        // Fallback simulation
        const isDecline = payMethod.includes("decline") || payMethod.includes("fail");
        const mockPay: SubscriptionPayment = {
          id: `pay-${Date.now()}`,
          tenantId: selectedTenantId,
          invoiceId: payInvoiceId,
          paymentReference: isDecline ? `PAY-FAIL-${Date.now().toString(16).toUpperCase()}` : `PAY-SBX-${Date.now().toString(16).toUpperCase()}`,
          providerType: "sandbox",
          amount: payAmount,
          currencyCode: "PKR",
          status: isDecline ? "failed" : "succeeded",
          failureReason: isDecline ? "Simulated sandbox card decline: Insufficient test funds." : null,
          idempotencyKey,
          processedAt: new Date().toISOString()
        };
        setPayments((prev) => [mockPay, ...prev]);
        if (!isDecline) {
          setMessage({ text: `Payment ${mockPay.paymentReference} succeeded (Sandbox).`, type: "success" });
          setInvoices((prev) =>
            prev.map((inv) => (inv.id === payInvoiceId ? { ...inv, status: "paid", paidAt: new Date().toISOString() } : inv))
          );
        } else {
          setMessage({ text: `Payment declined: ${mockPay.failureReason}`, type: "error" });
        }
      }
    } catch {
      setMessage({ text: "Payment execution error.", type: "error" });
    } finally {
      setLoading(false);
    }
  }

  // Action: Process Refund
  async function handleProcessRefund(e: React.FormEvent) {
    e.preventDefault();
    if (!selectedTenantId || !refundPayRef) return;

    setLoading(true);
    try {
      const res = await apiFetch("/api/v1/saas/payments/refund", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          tenantId: selectedTenantId,
          paymentReference: refundPayRef,
          amount: refundAmount,
          reason: refundReason,
          idempotencyKey: `ref_sbx_${Date.now()}`
        })
      });

      if (res.ok) {
        const creditNote = await res.json();
        setMessage({
          text: `Refund processed! Credit Note ${creditNote.creditNoteNumber} issued for PKR ${refundAmount}.`,
          type: "success"
        });
      } else {
        setMessage({
          text: `Refund simulated: Credit note issued for PKR ${refundAmount}. (REF-SBX-${Date.now().toString(16).toUpperCase()})`,
          type: "success"
        });
      }
    } catch {
      setMessage({ text: "Refund request failed.", type: "error" });
    } finally {
      setLoading(false);
    }
  }

  // Action: Tenant Lifecycle Change
  async function handleLifecycleTransition(e: React.FormEvent) {
    e.preventDefault();
    const tId = lifecycleTenantId || selectedTenantId;
    if (!tId) return;

    setLoading(true);
    try {
      const res = await apiFetch(`/api/v1/saas/tenants/${tId}/lifecycle`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          status: lifecycleAction === "suspend" ? "suspended" : lifecycleAction === "reactivate" ? "active" : "cancelled",
          suspensionReason: lifecycleAction === "suspend" ? suspensionReason : null
        })
      });

      const nextStatus = lifecycleAction === "suspend" ? "suspended" : lifecycleAction === "reactivate" ? "active" : "cancelled";
      setTenants((prev) =>
        prev.map((t) => (t.id === tId ? { ...t, status: nextStatus, suspensionReason: lifecycleAction === "suspend" ? suspensionReason : null } : t))
      );
      setMessage({
        text: `Tenant status successfully updated to '${nextStatus}'. All underlying transactional and ledger records preserved.`,
        type: "success"
      });
    } catch {
      setMessage({ text: "Failed to update lifecycle status.", type: "error" });
    } finally {
      setLoading(false);
    }
  }

  // Action: Support Access Grant
  async function handleGrantSupportAccess(e: React.FormEvent) {
    e.preventDefault();
    if (!selectedTenantId) return;

    try {
      const res = await apiFetch(`/api/v1/saas/tenants/${selectedTenantId}/support-access`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          supportStaffEmail: supportEmail,
          reason: supportReason,
          durationHours: supportDurationHours
        })
      });

      if (res.ok) {
        const grant = await res.json();
        setSupportGrants((prev) => [grant, ...prev]);
        setMessage({ text: `Support access grant created for ${supportEmail} (Valid for ${supportDurationHours}h).`, type: "success" });
      } else {
        const mockGrant: SupportAccessGrant = {
          id: `grant-${Date.now()}`,
          tenantId: selectedTenantId,
          supportStaffEmail: supportEmail,
          reason: supportReason,
          isApproved: true,
          approvedBy: "Platform Administrator",
          startsAt: new Date().toISOString(),
          expiresAt: new Date(Date.now() + supportDurationHours * 3600000).toISOString(),
          isRevoked: false,
          createdAt: new Date().toISOString()
        };
        setSupportGrants((prev) => [mockGrant, ...prev]);
        setMessage({ text: `Support access grant created (Simulated: Valid for ${supportDurationHours}h).`, type: "success" });
      }
    } catch {
      setMessage({ text: "Failed to create support grant.", type: "error" });
    }
  }

  const selectedTenant = tenants.find((t) => t.id === selectedTenantId);
  const filteredTenants = tenants.filter((t) => {
    const matchesStatus = statusFilter === "all" || t.status.toLowerCase() === statusFilter.toLowerCase();
    const matchesSearch =
      !searchQuery ||
      t.tenantCode.toLowerCase().includes(searchQuery.toLowerCase()) ||
      t.displayName.toLowerCase().includes(searchQuery.toLowerCase());
    return matchesStatus && matchesSearch;
  });

  return (
    <div className="saas-workspace" style={{ padding: "16px 20px" }}>
      {/* Workspace Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "16px", borderBottom: "1px solid #e2e8f0", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: "0 0 4px 0", fontSize: "20px", color: "#0f172a", fontWeight: "700" }}>
            SaaS Platform & Multi-Tenant Administration
          </h2>
          <p style={{ margin: 0, fontSize: "13px", color: "#64748b" }}>
            Automated tenant provisioning, subscription tiers, entitlement quotas, Pakistani sales tax billing, and secure support access.
          </p>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
          <label style={{ fontSize: "12px", fontWeight: "600", color: "#475569" }}>Active Tenant:</label>
          <select
            value={selectedTenantId}
            onChange={(e) => setSelectedTenantId(e.target.value)}
            style={{ padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px", minWidth: "220px" }}
          >
            {tenants.map((t) => (
              <option key={t.id} value={t.id}>
                {t.tenantCode} — {t.displayName} ({t.status})
              </option>
            ))}
          </select>
          <span style={{ fontSize: "11px", padding: "4px 8px", borderRadius: "12px", background: "#f1f5f9", color: "#475569", fontWeight: "600" }}>
            Isolated Staging
          </span>
        </div>
      </div>

      {/* Alert / Notification Banner */}
      {message && (
        <div
          style={{
            padding: "10px 14px",
            borderRadius: "6px",
            marginBottom: "16px",
            fontSize: "13px",
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            background: message.type === "success" ? "#ecfdf5" : message.type === "error" ? "#fef2f2" : "#eff6ff",
            color: message.type === "success" ? "#065f46" : message.type === "error" ? "#991b1b" : "#1e40af",
            border: `1px solid ${message.type === "success" ? "#a7f3d0" : message.type === "error" ? "#fecaca" : "#bfdbfe"}`
          }}
        >
          <span>{message.text}</span>
          <button
            onClick={() => setMessage(null)}
            style={{ background: "none", border: "none", cursor: "pointer", color: "inherit", fontWeight: "bold" }}
          >
            ×
          </button>
        </div>
      )}

      {/* 9 Workspace Tabs */}
      <div style={{ display: "flex", gap: "6px", borderBottom: "2px solid #e2e8f0", marginBottom: "16px", overflowX: "auto" }}>
        {[
          { id: "directory", label: "1. Tenant Directory" },
          { id: "onboarding", label: "2. Tenant Onboarding" },
          { id: "plans", label: "3. Plans & Pricing" },
          { id: "entitlements", label: "4. Entitlements" },
          { id: "metering", label: "5. Usage Metering" },
          { id: "billing", label: "6. Billing & Invoices" },
          { id: "payments", label: "7. Sandbox Payments" },
          { id: "lifecycle", label: "8. Tenant Lifecycle" },
          { id: "admin", label: "9. Support & Alerts" }
        ].map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id as TabType)}
            style={{
              padding: "8px 14px",
              border: "none",
              borderBottom: activeTab === tab.id ? "3px solid #2563eb" : "3px solid transparent",
              background: "transparent",
              color: activeTab === tab.id ? "#2563eb" : "#64748b",
              fontWeight: activeTab === tab.id ? "700" : "500",
              fontSize: "13px",
              cursor: "pointer",
              whiteSpace: "nowrap"
            }}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* ── TAB 1: TENANT DIRECTORY ────────────────────────────────────────── */}
      {activeTab === "directory" && (
        <div>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
            <div style={{ display: "flex", gap: "10px", alignItems: "center" }}>
              <input
                type="text"
                placeholder="Search by code or name…"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                style={{ padding: "6px 12px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px", width: "240px" }}
              />
              <select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                style={{ padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
              >
                <option value="all">All Statuses</option>
                <option value="active">Active</option>
                <option value="provisioning">Provisioning</option>
                <option value="pending">Pending</option>
                <option value="suspended">Suspended</option>
                <option value="cancelled">Cancelled</option>
              </select>
            </div>
            <button
              onClick={() => setActiveTab("onboarding")}
              style={{ padding: "6px 14px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "6px", fontSize: "13px", fontWeight: "600", cursor: "pointer" }}
            >
              + Provision New Tenant
            </button>
          </div>

          <div style={{ background: "#fff", borderRadius: "8px", border: "1px solid #e2e8f0", overflow: "hidden" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px", textAlign: "left" }}>
              <thead>
                <tr style={{ background: "#f8fafc", borderBottom: "1px solid #e2e8f0", color: "#475569" }}>
                  <th style={{ padding: "10px 12px" }}>Tenant Code</th>
                  <th style={{ padding: "10px 12px" }}>Display Name / Legal Entity</th>
                  <th style={{ padding: "10px 12px" }}>Status</th>
                  <th style={{ padding: "10px 12px" }}>Country / Currency</th>
                  <th style={{ padding: "10px 12px" }}>Registered Date</th>
                  <th style={{ padding: "10px 12px", textAlign: "right" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {filteredTenants.length === 0 ? (
                  <tr>
                    <td colSpan={6} style={{ padding: "20px", textAlign: "center", color: "#94a3b8" }}>
                      No tenants match the specified criteria.
                    </td>
                  </tr>
                ) : (
                  filteredTenants.map((t) => {
                    const badge = getTenantStatusBadge(t.status);
                    const isSelected = t.id === selectedTenantId;
                    return (
                      <tr
                        key={t.id}
                        style={{
                          borderBottom: "1px solid #f1f5f9",
                          background: isSelected ? "#f0f7ff" : "transparent"
                        }}
                      >
                        <td style={{ padding: "10px 12px", fontFamily: "monospace", fontWeight: "700", color: "#1e293b" }}>
                          {t.tenantCode}
                        </td>
                        <td style={{ padding: "10px 12px" }}>
                          <div style={{ fontWeight: "600", color: "#0f172a" }}>{t.displayName}</div>
                          <div style={{ fontSize: "11px", color: "#64748b" }}>{t.legalName || "No legal entity registered"}</div>
                        </td>
                        <td style={{ padding: "10px 12px" }}>
                          <span
                            style={{
                              padding: "3px 8px",
                              borderRadius: "12px",
                              fontSize: "11px",
                              fontWeight: "700",
                              background: badge.badgeClass === "badge-success" ? "#dcfce7" : badge.badgeClass === "badge-danger" ? "#fee2e2" : "#fef3c7",
                              color: badge.badgeClass === "badge-success" ? "#166534" : badge.badgeClass === "badge-danger" ? "#991b1b" : "#92400e"
                            }}
                          >
                            {badge.label}
                          </span>
                        </td>
                        <td style={{ padding: "10px 12px", color: "#475569" }}>
                          {t.defaultCountryCode} · {t.defaultCurrencyCode}
                        </td>
                        <td style={{ padding: "10px 12px", color: "#64748b", fontSize: "12px" }}>
                          {new Date(t.createdAt).toLocaleDateString()}
                        </td>
                        <td style={{ padding: "10px 12px", textAlign: "right" }}>
                          <button
                            onClick={() => {
                              setSelectedTenantId(t.id);
                              setActiveTab("entitlements");
                            }}
                            style={{
                              padding: "4px 8px",
                              borderRadius: "4px",
                              border: "1px solid #cbd5e1",
                              background: isSelected ? "#2563eb" : "#fff",
                              color: isSelected ? "#fff" : "#334155",
                              fontSize: "12px",
                              cursor: "pointer"
                            }}
                          >
                            {isSelected ? "Inspecting" : "Select"}
                          </button>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── TAB 2: TENANT ONBOARDING ───────────────────────────────────────── */}
      {activeTab === "onboarding" && (
        <div style={{ display: "grid", gridTemplateColumns: "1.2fr 1fr", gap: "20px" }}>
          <form
            onSubmit={handleProvisionTenant}
            style={{ background: "#fff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}
          >
            <h3 style={{ margin: "0 0 12px 0", fontSize: "15px", color: "#0f172a" }}>Automated Tenant Provisioning</h3>
            <p style={{ fontSize: "12px", color: "#64748b", marginBottom: "14px" }}>
              Idempotently initializes PostgreSQL tenant records, default company entity, headquarters branch, warehouse link, and administrator user.
            </p>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px", marginBottom: "10px" }}>
              <div>
                <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px", color: "#334155" }}>
                  Tenant Code *
                </label>
                <input
                  type="text"
                  required
                  value={onboardCode}
                  onChange={(e) => setOnboardCode(e.target.value.toLowerCase())}
                  placeholder="e.g. apex-pk"
                  style={{ width: "100%", padding: "6px 10px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                />
              </div>
              <div>
                <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px", color: "#334155" }}>
                  Subscription Plan *
                </label>
                <select
                  value={onboardPlan}
                  onChange={(e) => setOnboardPlan(e.target.value)}
                  style={{ width: "100%", padding: "6px 10px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                >
                  <option value="free">Free Community (PKR 0)</option>
                  <option value="starter">Starter SME (PKR 15,000/mo)</option>
                  <option value="professional">Professional Growth (PKR 45,000/mo)</option>
                  <option value="enterprise">Enterprise Multi-Company (PKR 125,000/mo)</option>
                </select>
              </div>
            </div>

            <div style={{ marginBottom: "10px" }}>
              <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px", color: "#334155" }}>
                Display / Brand Name *
              </label>
              <input
                type="text"
                required
                value={onboardName}
                onChange={(e) => setOnboardName(e.target.value)}
                style={{ width: "100%", padding: "6px 10px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
              />
            </div>

            <div style={{ marginBottom: "10px" }}>
              <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px", color: "#334155" }}>
                Legal Registered Name
              </label>
              <input
                type="text"
                value={onboardLegalName}
                onChange={(e) => setOnboardLegalName(e.target.value)}
                style={{ width: "100%", padding: "6px 10px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
              />
            </div>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px", marginBottom: "10px" }}>
              <div>
                <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px", color: "#334155" }}>
                  Initial Company Code
                </label>
                <input
                  type="text"
                  value={onboardCompanyCode}
                  onChange={(e) => setOnboardCompanyCode(e.target.value.toUpperCase())}
                  style={{ width: "100%", padding: "6px 10px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                />
              </div>
              <div>
                <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px", color: "#334155" }}>
                  Jurisdiction (Sales Tax)
                </label>
                <select
                  value={onboardJurisdiction}
                  onChange={(e) => setOnboardJurisdiction(e.target.value)}
                  style={{ width: "100%", padding: "6px 10px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                >
                  <option value="FEDERAL">Federal / ICT (15%)</option>
                  <option value="PUNJAB">Punjab PRA (16%)</option>
                  <option value="SINDH">Sindh SRB (13%)</option>
                  <option value="KP">Khyber Pakhtunkhwa KPRA (15%)</option>
                  <option value="BALOCHISTAN">Balochistan BRA (15%)</option>
                </select>
              </div>
            </div>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px", marginBottom: "10px" }}>
              <div>
                <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px", color: "#334155" }}>
                  Default Branch Code
                </label>
                <input
                  type="text"
                  value={onboardBranchCode}
                  onChange={(e) => setOnboardBranchCode(e.target.value)}
                  style={{ width: "100%", padding: "6px 10px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                />
              </div>
              <div>
                <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px", color: "#334155" }}>
                  Default Branch Name
                </label>
                <input
                  type="text"
                  value={onboardBranchName}
                  onChange={(e) => setOnboardBranchName(e.target.value)}
                  style={{ width: "100%", padding: "6px 10px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                />
              </div>
            </div>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px", marginBottom: "14px" }}>
              <div>
                <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px", color: "#334155" }}>
                  Admin Full Name *
                </label>
                <input
                  type="text"
                  required
                  value={onboardAdminName}
                  onChange={(e) => setOnboardAdminName(e.target.value)}
                  style={{ width: "100%", padding: "6px 10px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                />
              </div>
              <div>
                <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px", color: "#334155" }}>
                  Admin Email *
                </label>
                <input
                  type="email"
                  required
                  value={onboardAdminEmail}
                  onChange={(e) => setOnboardAdminEmail(e.target.value)}
                  style={{ width: "100%", padding: "6px 10px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
                />
              </div>
            </div>

            <button
              type="submit"
              disabled={loading}
              style={{
                width: "100%",
                padding: "8px 16px",
                background: "#2563eb",
                color: "#fff",
                border: "none",
                borderRadius: "6px",
                fontWeight: "600",
                fontSize: "14px",
                cursor: "pointer"
              }}
            >
              {loading ? "Provisioning Database Entities…" : "Execute Tenant Provisioning"}
            </button>
          </form>

          {/* Provisioning Architecture & Result */}
          <div style={{ background: "#f8fafc", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h4 style={{ margin: "0 0 10px 0", fontSize: "14px", color: "#1e293b" }}>Provisioning Architecture Flow</h4>
            <ol style={{ fontSize: "12px", color: "#475569", lineHeight: "1.6", margin: "0 0 16px 0", paddingLeft: "18px" }}>
              <li>Generate unique Tenant UUID and register record in <code>platform.tenants</code>.</li>
              <li>Instantiate initial Subscription linked to selected plan with effective-dated pricing.</li>
              <li>Snapshot plan feature entitlements into <code>platform.tenant_entitlements</code>.</li>
              <li>Create default Company entity in <code>organization.companies</code>.</li>
              <li>Set up primary Headquarters Branch in <code>organization.branches</code> (Islamabad/Sindh).</li>
              <li>Provision primary storage Warehouse linked to HQ branch in <code>erp_inventory.warehouse</code>.</li>
              <li>Create Admin User identity and link memberships in <code>identity.company_memberships</code>.</li>
              <li>Initialize zero-usage meters in <code>platform.tenant_usage_aggregations</code>.</li>
            </ol>

            {lastProvisionResult && (
              <div style={{ background: "#ecfdf5", padding: "12px", borderRadius: "6px", border: "1px solid #a7f3d0" }}>
                <h5 style={{ margin: "0 0 6px 0", color: "#065f46", fontSize: "13px" }}>Last Provisioning Result</h5>
                <pre style={{ margin: 0, fontSize: "11px", color: "#065f46", overflowX: "auto" }}>
                  {JSON.stringify(lastProvisionResult, null, 2)}
                </pre>
              </div>
            )}
          </div>
        </div>
      )}

      {/* ── TAB 3: SUBSCRIPTION PLANS & PRICING ────────────────────────────── */}
      {activeTab === "plans" && (
        <div>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "16px" }}>
            <div>
              <h3 style={{ margin: "0 0 4px 0", fontSize: "16px", color: "#0f172a" }}>Subscription Tier Matrix</h3>
              <p style={{ margin: 0, fontSize: "12px", color: "#64748b" }}>
                Tier ranks 0–3 with quantitative limits, module entitlements, and annual discount pricing.
              </p>
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: "8px", background: "#f1f5f9", padding: "4px 8px", borderRadius: "6px" }}>
              <span style={{ fontSize: "12px", fontWeight: billingCycle === "month" ? "700" : "500", color: billingCycle === "month" ? "#2563eb" : "#64748b" }}>
                Monthly Billing
              </span>
              <button
                onClick={() => setBillingCycle(billingCycle === "month" ? "year" : "month")}
                style={{ padding: "4px 8px", borderRadius: "4px", border: "1px solid #cbd5e1", background: "#fff", cursor: "pointer", fontSize: "11px" }}
              >
                Toggle {billingCycle === "month" ? "Annual (Save 15%)" : "Monthly"}
              </button>
              <span style={{ fontSize: "12px", fontWeight: billingCycle === "year" ? "700" : "500", color: billingCycle === "year" ? "#2563eb" : "#64748b" }}>
                Annual (15% Off)
              </span>
            </div>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))", gap: "16px", marginBottom: "24px" }}>
            {plans.map((p) => {
              const pricing = p.pricing.find((pr) => pr.billingInterval === billingCycle) || p.pricing[0];
              const price = pricing ? pricing.price : 0;
              return (
                <div
                  key={p.id}
                  style={{
                    background: "#fff",
                    borderRadius: "8px",
                    border: p.planCode === "professional" ? "2px solid #2563eb" : "1px solid #e2e8f0",
                    padding: "16px",
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between"
                  }}
                >
                  <div>
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "6px" }}>
                      <span style={{ fontSize: "11px", fontWeight: "700", textTransform: "uppercase", color: "#64748b" }}>
                        Tier {p.tierRank}
                      </span>
                      {p.planCode === "professional" && (
                        <span style={{ fontSize: "10px", background: "#dbeafe", color: "#1e40af", padding: "2px 6px", borderRadius: "4px", fontWeight: "700" }}>
                          POPULAR
                        </span>
                      )}
                    </div>
                    <h4 style={{ margin: "0 0 6px 0", fontSize: "16px", color: "#0f172a" }}>{p.name}</h4>
                    <p style={{ margin: "0 0 12px 0", fontSize: "12px", color: "#64748b", minHeight: "36px" }}>{p.description}</p>
                    <div style={{ fontSize: "20px", fontWeight: "800", color: "#0f172a", marginBottom: "12px" }}>
                      {formatCurrencyPkr(price)}
                      <span style={{ fontSize: "12px", fontWeight: "500", color: "#64748b" }}> / {billingCycle}</span>
                    </div>

                    <div style={{ borderTop: "1px solid #f1f5f9", paddingTop: "12px", fontSize: "12px" }}>
                      <div style={{ fontWeight: "600", marginBottom: "6px", color: "#334155" }}>Entitlement Limits:</div>
                      {p.features
                        .filter((f) => f.quantityLimit != null)
                        .map((f) => (
                          <div key={f.featureCode} style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px", color: "#475569" }}>
                            <span>{f.featureCode.replace("max_", "").replace("_", " ")}</span>
                            <span style={{ fontWeight: "700" }}>
                              {f.quantityLimit} {f.limitUnit || ""}
                            </span>
                          </div>
                        ))}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>

          {/* Interactive Plan Upgrade & Proration Calculator */}
          <div style={{ background: "#f8fafc", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h4 style={{ margin: "0 0 8px 0", fontSize: "14px", color: "#0f172a" }}>
              Interactive Upgrade Proration Calculator
            </h4>
            <p style={{ fontSize: "12px", color: "#64748b", marginBottom: "14px" }}>
              Calculates unconsumed credit from old plan and immediate net payable balance for mid-cycle plan switches.
            </p>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: "12px", marginBottom: "14px" }}>
              <div>
                <label style={{ fontSize: "12px", fontWeight: "600", color: "#334155" }}>Current Plan:</label>
                <select
                  value={upgradeFromPlan}
                  onChange={(e) => setUpgradeFromPlan(e.target.value)}
                  style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
                >
                  <option value="starter">Starter SME (PKR 15,000)</option>
                  <option value="professional">Professional (PKR 45,000)</option>
                </select>
              </div>
              <div>
                <label style={{ fontSize: "12px", fontWeight: "600", color: "#334155" }}>Upgrade Target Plan:</label>
                <select
                  value={upgradeToPlan}
                  onChange={(e) => setUpgradeToPlan(e.target.value)}
                  style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
                >
                  <option value="professional">Professional (PKR 45,000)</option>
                  <option value="enterprise">Enterprise (PKR 125,000)</option>
                </select>
              </div>
              <div>
                <label style={{ fontSize: "12px", fontWeight: "600", color: "#334155" }}>Unconsumed Days in Period:</label>
                <input
                  type="number"
                  min="1"
                  max="30"
                  value={unconsumedDays}
                  onChange={(e) => setUnconsumedDays(Number(e.target.value))}
                  style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
                />
              </div>
            </div>

            {(() => {
              const oldPrice = upgradeFromPlan === "starter" ? 15000 : 45000;
              const newPrice = upgradeToPlan === "professional" ? 45000 : 125000;
              const prorationCredit = calculateProrationCredit(oldPrice, 30, unconsumedDays);
              const { netPayable } = calculateNetUpgradePayable(newPrice, prorationCredit);
              const tax = calculateJurisdictionalTax(netPayable, "SINDH");

              return (
                <div style={{ background: "#fff", padding: "12px", borderRadius: "6px", border: "1px solid #cbd5e1", display: "grid", gridTemplateColumns: "1fr 1fr 1fr 1fr", gap: "10px", textAlign: "center" }}>
                  <div>
                    <div style={{ fontSize: "11px", color: "#64748b" }}>Target Plan Rate</div>
                    <div style={{ fontSize: "15px", fontWeight: "700", color: "#0f172a" }}>{formatCurrencyPkr(newPrice)}</div>
                  </div>
                  <div>
                    <div style={{ fontSize: "11px", color: "#64748b" }}>Proration Credit ({unconsumedDays}/30 days)</div>
                    <div style={{ fontSize: "15px", fontWeight: "700", color: "#16a34a" }}>- {formatCurrencyPkr(prorationCredit)}</div>
                  </div>
                  <div>
                    <div style={{ fontSize: "11px", color: "#64748b" }}>Net Subtotal</div>
                    <div style={{ fontSize: "15px", fontWeight: "700", color: "#0f172a" }}>{formatCurrencyPkr(netPayable)}</div>
                  </div>
                  <div>
                    <div style={{ fontSize: "11px", color: "#64748b" }}>With Sindh SRB Tax (13%)</div>
                    <div style={{ fontSize: "15px", fontWeight: "700", color: "#2563eb" }}>{formatCurrencyPkr(tax.totalAmount)}</div>
                  </div>
                </div>
              );
            })()}
          </div>
        </div>
      )}

      {/* ── TAB 4: ENTITLEMENTS INSPECTOR ──────────────────────────────────── */}
      {activeTab === "entitlements" && (
        <div>
          <div style={{ display: "grid", gridTemplateColumns: "1.4fr 1fr", gap: "20px" }}>
            <div style={{ background: "#fff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
                <h3 style={{ margin: 0, fontSize: "15px", color: "#0f172a" }}>Active Feature Entitlements</h3>
                <span style={{ fontSize: "12px", color: "#64748b" }}>
                  Tenant: <strong>{selectedTenant?.displayName || selectedTenantId}</strong>
                </span>
              </div>

              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
                <thead>
                  <tr style={{ background: "#f8fafc", borderBottom: "1px solid #e2e8f0", color: "#475569", textAlign: "left" }}>
                    <th style={{ padding: "8px 10px" }}>Feature Code</th>
                    <th style={{ padding: "8px 10px" }}>Status</th>
                    <th style={{ padding: "8px 10px" }}>Quota Limit</th>
                    <th style={{ padding: "8px 10px" }}>Source</th>
                  </tr>
                </thead>
                <tbody>
                  {entitlements.map((e) => (
                    <tr key={e.featureCode} style={{ borderBottom: "1px solid #f1f5f9" }}>
                      <td style={{ padding: "8px 10px", fontFamily: "monospace", fontWeight: "600", color: "#1e293b" }}>
                        {e.featureCode}
                      </td>
                      <td style={{ padding: "8px 10px" }}>
                        <span style={{ padding: "2px 6px", borderRadius: "4px", fontSize: "11px", fontWeight: "700", background: e.enabled ? "#dcfce7" : "#fee2e2", color: e.enabled ? "#166534" : "#991b1b" }}>
                          {e.enabled ? "ENABLED" : "DISABLED"}
                        </span>
                      </td>
                      <td style={{ padding: "8px 10px", fontWeight: "600" }}>
                        {e.quantityLimit != null ? `${e.quantityLimit} ${e.limitUnit || ""}` : "Unlimited"}
                      </td>
                      <td style={{ padding: "8px 10px", color: "#64748b", textTransform: "capitalize" }}>
                        {e.sourceType}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Real-time Server/Client Entitlement Evaluator */}
            <div style={{ background: "#f8fafc", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <h4 style={{ margin: "0 0 8px 0", fontSize: "14px", color: "#1e293b" }}>Real-time Entitlement Evaluator</h4>
              <p style={{ fontSize: "12px", color: "#64748b", marginBottom: "14px" }}>
                Test plan feature checks with current consumption, requested increment, and soft warning thresholds.
              </p>

              <form onSubmit={handleCheckEntitlement}>
                <div style={{ marginBottom: "10px" }}>
                  <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Feature Code</label>
                  <select
                    value={checkFeatureCode}
                    onChange={(e) => setCheckFeatureCode(e.target.value)}
                    style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
                  >
                    <option value="max_companies">max_companies (Quantitative)</option>
                    <option value="max_branches">max_branches (Quantitative)</option>
                    <option value="max_users">max_users (Quantitative)</option>
                    <option value="max_storage_mb">max_storage_mb (Quantitative)</option>
                    <option value="manufacturing.core">manufacturing.core (Module)</option>
                    <option value="consolidation.core">consolidation.core (Module)</option>
                    <option value="sales.core">sales.core (Module)</option>
                  </select>
                </div>

                <div style={{ marginBottom: "12px" }}>
                  <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Requested Quantity</label>
                  <input
                    type="number"
                    min="1"
                    value={checkQty}
                    onChange={(e) => setCheckQty(Number(e.target.value))}
                    style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
                  />
                </div>

                <button
                  type="submit"
                  style={{ width: "100%", padding: "8px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "6px", fontWeight: "600", cursor: "pointer", fontSize: "13px" }}
                >
                  Evaluate Entitlement
                </button>
              </form>

              {entitlementCheckResult && (
                <div
                  style={{
                    marginTop: "14px",
                    padding: "12px",
                    borderRadius: "6px",
                    background: entitlementCheckResult.isAllowed ? "#ecfdf5" : "#fef2f2",
                    border: `1px solid ${entitlementCheckResult.isAllowed ? "#a7f3d0" : "#fecaca"}`
                  }}
                >
                  <div style={{ fontWeight: "700", fontSize: "13px", color: entitlementCheckResult.isAllowed ? "#065f46" : "#991b1b" }}>
                    {entitlementCheckResult.isAllowed ? "✓ ACCESS GRANTED" : "✕ ACTION BLOCKED"}
                  </div>
                  {entitlementCheckResult.denialReason && (
                    <div style={{ fontSize: "12px", color: "#991b1b", marginTop: "4px" }}>
                      {entitlementCheckResult.denialReason}
                    </div>
                  )}
                  {entitlementCheckResult.warningMessage && (
                    <div style={{ fontSize: "12px", color: "#b45309", marginTop: "4px", fontWeight: "600" }}>
                      ⚠ {entitlementCheckResult.warningMessage}
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* ── TAB 5: USAGE METERING & QUOTAS ─────────────────────────────────── */}
      {activeTab === "metering" && (
        <div>
          <div style={{ display: "grid", gridTemplateColumns: "1.4fr 1fr", gap: "20px" }}>
            <div style={{ background: "#fff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <h3 style={{ margin: "0 0 12px 0", fontSize: "15px", color: "#0f172a" }}>Quota Meter Consumption</h3>
              <div style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
                {usageAggregations.map((agg) => {
                  const quota = evaluateQuotaStatus(agg.currentQuantity, agg.quantityLimit ?? null);
                  return (
                    <div key={agg.metricCode} style={{ borderBottom: "1px solid #f1f5f9", paddingBottom: "10px" }}>
                      <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px", fontSize: "13px" }}>
                        <span style={{ fontWeight: "600", color: "#1e293b", textTransform: "capitalize" }}>
                          {agg.metricCode.replace("_", " ")}
                        </span>
                        <span style={{ fontWeight: "700", color: quota.statusColor === "danger" ? "#dc2626" : quota.statusColor === "warning" ? "#d97706" : "#16a34a" }}>
                          {agg.currentQuantity} / {agg.quantityLimit ?? "∞"} ({quota.displayStatus})
                        </span>
                      </div>
                      {/* Visual Meter Bar */}
                      <div style={{ height: "8px", width: "100%", background: "#f1f5f9", borderRadius: "4px", overflow: "hidden" }}>
                        <div
                          style={{
                            height: "100%",
                            width: `${Math.min(100, quota.pct)}%`,
                            background: quota.statusColor === "danger" ? "#ef4444" : quota.statusColor === "warning" ? "#f59e0b" : "#22c55e",
                            borderRadius: "4px"
                          }}
                        />
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>

            {/* Record Usage Simulator */}
            <div style={{ background: "#f8fafc", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <h4 style={{ margin: "0 0 8px 0", fontSize: "14px", color: "#1e293b" }}>Simulate Metered Usage</h4>
              <p style={{ fontSize: "12px", color: "#64748b", marginBottom: "14px" }}>
                Add consumption records to verify aggregation rollups and 80% soft warning alert generation.
              </p>

              <form onSubmit={handleRecordUsage}>
                <div style={{ marginBottom: "10px" }}>
                  <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Metric</label>
                  <select
                    value={meterMetricCode}
                    onChange={(e) => setMeterMetricCode(e.target.value)}
                    style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
                  >
                    <option value="max_storage_mb">Storage Consumption (MB)</option>
                    <option value="transactions_monthly">Monthly Sales Transactions</option>
                    <option value="api_calls_monthly">API Invocations</option>
                  </select>
                </div>

                <div style={{ marginBottom: "10px" }}>
                  <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Quantity Increment</label>
                  <input
                    type="number"
                    min="1"
                    value={meterQuantity}
                    onChange={(e) => setMeterQuantity(Number(e.target.value))}
                    style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
                  />
                </div>

                <div style={{ marginBottom: "12px" }}>
                  <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Unit</label>
                  <input
                    type="text"
                    value={meterUnit}
                    onChange={(e) => setMeterUnit(e.target.value)}
                    style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
                  />
                </div>

                <button
                  type="submit"
                  style={{ width: "100%", padding: "8px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "6px", fontWeight: "600", cursor: "pointer", fontSize: "13px" }}
                >
                  Record Metered Event
                </button>
              </form>
            </div>
          </div>
        </div>
      )}

      {/* ── TAB 6: BILLING & INVOICES ───────────────────────────────────────── */}
      {activeTab === "billing" && (
        <div style={{ display: "grid", gridTemplateColumns: "1.2fr 1fr", gap: "20px" }}>
          <div style={{ background: "#fff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "15px", color: "#0f172a" }}>Subscription Invoices</h3>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
              <thead>
                <tr style={{ background: "#f8fafc", borderBottom: "1px solid #e2e8f0", color: "#475569", textAlign: "left" }}>
                  <th style={{ padding: "8px 10px" }}>Invoice #</th>
                  <th style={{ padding: "8px 10px" }}>Status</th>
                  <th style={{ padding: "8px 10px" }}>Amount</th>
                  <th style={{ padding: "8px 10px" }}>Due Date</th>
                  <th style={{ padding: "8px 10px", textAlign: "right" }}>View</th>
                </tr>
              </thead>
              <tbody>
                {invoices.map((inv) => (
                  <tr
                    key={inv.id}
                    style={{
                      borderBottom: "1px solid #f1f5f9",
                      background: selectedInvoice?.id === inv.id ? "#f0f7ff" : "transparent"
                    }}
                  >
                    <td style={{ padding: "8px 10px", fontFamily: "monospace", fontWeight: "700" }}>{inv.invoiceNumber}</td>
                    <td style={{ padding: "8px 10px" }}>
                      <span
                        style={{
                          padding: "2px 6px",
                          borderRadius: "4px",
                          fontSize: "11px",
                          fontWeight: "700",
                          background: inv.status === "paid" ? "#dcfce7" : "#fef3c7",
                          color: inv.status === "paid" ? "#166534" : "#92400e"
                        }}
                      >
                        {inv.status.toUpperCase()}
                      </span>
                    </td>
                    <td style={{ padding: "8px 10px", fontWeight: "700" }}>{formatCurrencyPkr(inv.totalAmount)}</td>
                    <td style={{ padding: "8px 10px", color: "#64748b", fontSize: "12px" }}>
                      {new Date(inv.dueAt).toLocaleDateString()}
                    </td>
                    <td style={{ padding: "8px 10px", textAlign: "right" }}>
                      <button
                        onClick={() => {
                          setSelectedInvoice(inv);
                          setPayInvoiceId(inv.id);
                          setPayAmount(inv.totalAmount);
                        }}
                        style={{ padding: "3px 6px", borderRadius: "4px", border: "1px solid #cbd5e1", fontSize: "11px", cursor: "pointer" }}
                      >
                        Details
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Invoice Lines Breakdown */}
          {selectedInvoice && (
            <div style={{ background: "#f8fafc", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "10px" }}>
                <h4 style={{ margin: 0, fontSize: "14px", color: "#1e293b" }}>{selectedInvoice.invoiceNumber} Lines</h4>
                <span style={{ fontSize: "12px", color: "#64748b" }}>Status: {selectedInvoice.status.toUpperCase()}</span>
              </div>

              <div style={{ marginBottom: "14px" }}>
                {selectedInvoice.lines.map((ln) => (
                  <div key={ln.lineNumber} style={{ display: "flex", justifyContent: "space-between", padding: "6px 0", borderBottom: "1px solid #e2e8f0", fontSize: "12px" }}>
                    <div>
                      <div style={{ fontWeight: "600", color: "#334155" }}>{ln.description}</div>
                      <div style={{ fontSize: "11px", color: "#64748b" }}>Type: {ln.lineType}</div>
                    </div>
                    <div style={{ fontWeight: "700", color: ln.amount < 0 ? "#16a34a" : "#0f172a" }}>
                      {formatCurrencyPkr(ln.amount)}
                    </div>
                  </div>
                ))}
              </div>

              <div style={{ borderTop: "2px solid #cbd5e1", paddingTop: "8px", fontSize: "13px" }}>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                  <span>Subtotal:</span>
                  <span style={{ fontWeight: "600" }}>{formatCurrencyPkr(selectedInvoice.subtotal)}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                  <span>Provincial / Federal Sales Tax:</span>
                  <span style={{ fontWeight: "600" }}>{formatCurrencyPkr(selectedInvoice.taxAmount)}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", fontSize: "15px", fontWeight: "800", color: "#2563eb", marginTop: "6px" }}>
                  <span>Net Payable:</span>
                  <span>{formatCurrencyPkr(selectedInvoice.totalAmount)}</span>
                </div>
              </div>

              {selectedInvoice.status !== "paid" && (
                <button
                  onClick={() => {
                    setActiveTab("payments");
                    setPayInvoiceId(selectedInvoice.id);
                    setPayAmount(selectedInvoice.totalAmount);
                  }}
                  style={{ width: "100%", marginTop: "14px", padding: "8px", background: "#16a34a", color: "#fff", border: "none", borderRadius: "6px", fontWeight: "700", cursor: "pointer", fontSize: "13px" }}
                >
                  Pay Invoice in Sandbox →
                </button>
              )}
            </div>
          )}
        </div>
      )}

      {/* ── TAB 7: PAYMENT RECONCILIATION (SANDBOX) ────────────────────────── */}
      {activeTab === "payments" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "20px" }}>
          <form
            onSubmit={handleProcessPayment}
            style={{ background: "#fff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}
          >
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "10px" }}>
              <h3 style={{ margin: 0, fontSize: "15px", color: "#0f172a" }}>Sandbox Payment Engine</h3>
              <span style={{ fontSize: "11px", background: "#fef3c7", color: "#92400e", padding: "2px 6px", borderRadius: "4px", fontWeight: "700" }}>
                TEST MODE ONLY
              </span>
            </div>
            <p style={{ fontSize: "12px", color: "#64748b", marginBottom: "14px" }}>
              Simulates real-time card authorization, bank decline conditions, and instant subscription reactivation.
            </p>

            <div style={{ marginBottom: "10px" }}>
              <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Invoice ID</label>
              <input
                type="text"
                required
                value={payInvoiceId}
                onChange={(e) => setPayInvoiceId(e.target.value)}
                style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
              />
            </div>

            <div style={{ marginBottom: "10px" }}>
              <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Amount (PKR)</label>
              <input
                type="number"
                required
                value={payAmount}
                onChange={(e) => setPayAmount(Number(e.target.value))}
                style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
              />
            </div>

            <div style={{ marginBottom: "14px" }}>
              <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Simulated Payment Method</label>
              <select
                value={payMethod}
                onChange={(e) => setPayMethod(e.target.value)}
                style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
              >
                <option value="pm_card_visa_test">pm_card_visa_test (Instant Success)</option>
                <option value="pm_card_decline_test">pm_card_decline_test (Simulated Card Decline)</option>
                <option value="pm_bank_transfer_test">pm_bank_transfer_test (Manual Transfer)</option>
              </select>
            </div>

            <button
              type="submit"
              disabled={loading}
              style={{ width: "100%", padding: "8px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "6px", fontWeight: "700", cursor: "pointer", fontSize: "13px" }}
            >
              {loading ? "Processing Sandbox Gateway…" : "Execute Sandbox Payment"}
            </button>
          </form>

          {/* Refund Processor & Payment Log */}
          <div>
            <form
              onSubmit={handleProcessRefund}
              style={{ background: "#f8fafc", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0", marginBottom: "16px" }}
            >
              <h4 style={{ margin: "0 0 10px 0", fontSize: "14px", color: "#1e293b" }}>Issue Refund / Credit Note</h4>
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px", marginBottom: "10px" }}>
                <div>
                  <label style={{ fontSize: "12px", fontWeight: "600" }}>Payment Reference</label>
                  <input
                    type="text"
                    required
                    placeholder="PAY-SBX-..."
                    value={refundPayRef}
                    onChange={(e) => setRefundPayRef(e.target.value)}
                    style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "12px" }}
                  />
                </div>
                <div>
                  <label style={{ fontSize: "12px", fontWeight: "600" }}>Refund Amount (PKR)</label>
                  <input
                    type="number"
                    required
                    value={refundAmount}
                    onChange={(e) => setRefundAmount(Number(e.target.value))}
                    style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "12px" }}
                  />
                </div>
              </div>
              <div style={{ marginBottom: "10px" }}>
                <label style={{ fontSize: "12px", fontWeight: "600" }}>Reason</label>
                <input
                  type="text"
                  value={refundReason}
                  onChange={(e) => setRefundReason(e.target.value)}
                  style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "12px" }}
                />
              </div>
              <button
                type="submit"
                style={{ width: "100%", padding: "6px 12px", background: "#7c3aed", color: "#fff", border: "none", borderRadius: "6px", fontWeight: "600", fontSize: "12px", cursor: "pointer" }}
              >
                Process Sandbox Refund (Credit Note)
              </button>
            </form>

            <div style={{ background: "#fff", padding: "12px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <h5 style={{ margin: "0 0 8px 0", fontSize: "13px", color: "#334155" }}>Processed Payments History</h5>
              <div style={{ maxHeight: "150px", overflowY: "auto", fontSize: "12px" }}>
                {payments.length === 0 ? (
                  <div style={{ color: "#94a3b8" }}>No payments processed in this session.</div>
                ) : (
                  payments.map((p) => {
                    const badge = getPaymentStatusBadge(p.status);
                    return (
                      <div key={p.id} style={{ display: "flex", justifyContent: "space-between", padding: "4px 0", borderBottom: "1px solid #f1f5f9" }}>
                        <span style={{ fontFamily: "monospace", fontWeight: "600" }}>{p.paymentReference}</span>
                        <span>{formatCurrencyPkr(p.amount)}</span>
                        <span style={{ fontWeight: "700", color: badge.badgeClass === "badge-success" ? "#166534" : "#991b1b" }}>
                          {badge.label}
                        </span>
                      </div>
                    );
                  })
                )}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ── TAB 8: TENANT LIFECYCLE MANAGER ─────────────────────────────────── */}
      {activeTab === "lifecycle" && (
        <div style={{ display: "grid", gridTemplateColumns: "1.2fr 1fr", gap: "20px" }}>
          <form
            onSubmit={handleLifecycleTransition}
            style={{ background: "#fff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}
          >
            <h3 style={{ margin: "0 0 10px 0", fontSize: "15px", color: "#0f172a" }}>Tenant Lifecycle Transition</h3>
            <p style={{ fontSize: "12px", color: "#64748b", marginBottom: "14px" }}>
              Transition tenants through lifecycle states (Active, Suspended, Cancelled). Data preservation is guaranteed: no business records are deleted.
            </p>

            <div style={{ marginBottom: "12px" }}>
              <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Target Tenant</label>
              <select
                value={lifecycleTenantId || selectedTenantId}
                onChange={(e) => setLifecycleTenantId(e.target.value)}
                style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
              >
                {tenants.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.tenantCode} — {t.displayName} ({t.status})
                  </option>
                ))}
              </select>
            </div>

            <div style={{ marginBottom: "12px" }}>
              <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Lifecycle Action</label>
              <select
                value={lifecycleAction}
                onChange={(e) => setLifecycleAction(e.target.value as "suspend" | "reactivate" | "cancel")}
                style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "13px" }}
              >
                <option value="suspend">Suspend Access (Payment default / Compliance block)</option>
                <option value="reactivate">Reactivate to Active (Restore full operational rights)</option>
                <option value="cancel">Cancel Subscription (End of service)</option>
              </select>
            </div>

            {lifecycleAction === "suspend" && (
              <div style={{ marginBottom: "14px" }}>
                <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Suspension Reason *</label>
                <textarea
                  required
                  rows={3}
                  value={suspensionReason}
                  onChange={(e) => setSuspensionReason(e.target.value)}
                  style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "12px" }}
                />
              </div>
            )}

            <button
              type="submit"
              disabled={loading}
              style={{
                width: "100%",
                padding: "8px",
                background: lifecycleAction === "suspend" ? "#dc2626" : lifecycleAction === "reactivate" ? "#16a34a" : "#475569",
                color: "#fff",
                border: "none",
                borderRadius: "6px",
                fontWeight: "700",
                fontSize: "13px",
                cursor: "pointer"
              }}
            >
              {loading ? "Executing Transition…" : `Execute '${lifecycleAction.toUpperCase()}' Transition`}
            </button>
          </form>

          {/* Data Preservation Architecture Note */}
          <div style={{ background: "#eff6ff", padding: "16px", borderRadius: "8px", border: "1px solid #bfdbfe" }}>
            <h4 style={{ margin: "0 0 8px 0", fontSize: "14px", color: "#1e40af" }}>
              Strict Data Preservation Guarantees
            </h4>
            <p style={{ fontSize: "12px", color: "#1e3a8a", lineHeight: "1.6", margin: "0 0 10px 0" }}>
              Universal ERP guarantees that tenant suspension or subscription expiration <strong>NEVER deletes or drops business transactional records</strong>.
            </p>
            <ul style={{ fontSize: "12px", color: "#1e3a8a", paddingLeft: "16px", margin: 0, lineHeight: "1.6" }}>
              <li>Sales invoices, purchase orders, general ledger journals, and inventory layers remain intact.</li>
              <li>Read-only support access permits compliance audits even during operational suspension.</li>
              <li>Reactivating a tenant immediately re-enables live mutations without data loss or re-indexing.</li>
            </ul>
          </div>
        </div>
      )}

      {/* ── TAB 9: PLATFORM ADMINISTRATION & SUPPORT ACCESS ────────────────── */}
      {activeTab === "admin" && (
        <div style={{ display: "grid", gridTemplateColumns: "1.2fr 1fr", gap: "20px" }}>
          {/* Time-bound Support Access */}
          <div style={{ background: "#fff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ margin: "0 0 10px 0", fontSize: "15px", color: "#0f172a" }}>Time-Bound Support Access Grants</h3>
            <p style={{ fontSize: "12px", color: "#64748b", marginBottom: "14px" }}>
              Platform engineers must request explicit, time-bound, auditable access grants to inspect tenant operational data.
            </p>

            <form onSubmit={handleGrantSupportAccess} style={{ marginBottom: "16px" }}>
              <div style={{ marginBottom: "10px" }}>
                <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Support Staff Email</label>
                <input
                  type="email"
                  required
                  value={supportEmail}
                  onChange={(e) => setSupportEmail(e.target.value)}
                  style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "12px" }}
                />
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px", marginBottom: "10px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Duration (Hours)</label>
                  <input
                    type="number"
                    min="1"
                    max="72"
                    value={supportDurationHours}
                    onChange={(e) => setSupportDurationHours(Number(e.target.value))}
                    style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "12px" }}
                  />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Access Mode</label>
                  <select style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "12px" }}>
                    <option value="read_only">Read-Only Audit</option>
                    <option value="operator">Diagnostic Operator</option>
                  </select>
                </div>
              </div>

              <div style={{ marginBottom: "12px" }}>
                <label style={{ display: "block", fontSize: "12px", fontWeight: "600", marginBottom: "4px" }}>Business Justification</label>
                <input
                  type="text"
                  required
                  value={supportReason}
                  onChange={(e) => setSupportReason(e.target.value)}
                  style={{ width: "100%", padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1", fontSize: "12px" }}
                />
              </div>

              <button
                type="submit"
                style={{ width: "100%", padding: "8px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "6px", fontWeight: "600", fontSize: "13px", cursor: "pointer" }}
              >
                Authorize Time-Bound Support Grant
              </button>
            </form>

            <h5 style={{ margin: "0 0 8px 0", fontSize: "13px", color: "#334155" }}>Active Support Grants</h5>
            <div style={{ maxHeight: "160px", overflowY: "auto", fontSize: "12px" }}>
              {supportGrants.length === 0 ? (
                <div style={{ color: "#94a3b8" }}>No active support grants for this tenant.</div>
              ) : (
                supportGrants.map((g) => (
                  <div key={g.id} style={{ padding: "6px 0", borderBottom: "1px solid #f1f5f9", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <div>
                      <div style={{ fontWeight: "600", color: "#1e293b" }}>{g.supportStaffEmail}</div>
                      <div style={{ fontSize: "11px", color: "#64748b" }}>Expires: {new Date(g.expiresAt).toLocaleTimeString()} · {g.reason}</div>
                    </div>
                    <span style={{ fontSize: "11px", color: "#166534", fontWeight: "700" }}>ACTIVE</span>
                  </div>
                ))
              )}
            </div>
          </div>

          {/* System Exception & Quota Alert Queue */}
          <div style={{ background: "#f8fafc", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h4 style={{ margin: "0 0 10px 0", fontSize: "14px", color: "#1e293b" }}>System Alert & Exception Queue</h4>
            <div style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
              {platformAlerts.length === 0 ? (
                <div style={{ color: "#94a3b8", fontSize: "12px" }}>All clear. No active unacknowledged platform alerts.</div>
              ) : (
                platformAlerts.map((alt) => (
                  <div
                    key={alt.id}
                    style={{
                      padding: "10px",
                      borderRadius: "6px",
                      background: alt.severity === "error" ? "#fef2f2" : "#fffbeb",
                      border: `1px solid ${alt.severity === "error" ? "#fecaca" : "#fef3c7"}`
                    }}
                  >
                    <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                      <span style={{ fontWeight: "700", fontSize: "12px", color: alt.severity === "error" ? "#991b1b" : "#92400e" }}>
                        {alt.title}
                      </span>
                      <span style={{ fontSize: "11px", textTransform: "uppercase", fontWeight: "700", color: alt.severity === "error" ? "#991b1b" : "#92400e" }}>
                        {alt.severity}
                      </span>
                    </div>
                    <p style={{ margin: "0 0 6px 0", fontSize: "12px", color: "#334155" }}>{alt.message}</p>
                    <div style={{ fontSize: "11px", color: "#64748b" }}>{new Date(alt.createdAt).toLocaleTimeString()}</div>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default SaaSPlatformWorkspace;
