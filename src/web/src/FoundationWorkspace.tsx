import { useState, useEffect, type FormEvent } from "react";
import { apiFetch, type TenantCompanyContext } from "./auth";
import {
  FALLBACK_CURRENCIES,
  FUNCTIONAL_CURRENCY_EXPLANATION,
  PRESENTATION_CURRENCY_EXPLANATION,
  describeRateType,
  formatExchangeRateDisplay,
  validateCurrencySettingsDraft,
  validateExchangeRateDraft,
  type CompanyCurrencyHistoryDto,
  type CompanyCurrencySettingsDto,
  type CurrencyOption,
  type ExchangeRateDto,
  type RateType,
} from "./adminCurrencyHelpers";

type Props = {
  enabled: boolean;
  workspace: TenantCompanyContext | null;
  onCurrencySettingsUpdated?: () => void;
};

type RoleDetail = string | { roleCode?: string; name?: string };

type UserProfile = {
  userId: string;
  email: string;
  displayName: string;
  tenantId: string;
  companyId: string;
  roles: RoleDetail[];
  permissions: string[];
};

type CompanySettings = {
  companyId: string;
  companyCode: string;
  name: string;
  legalName?: string | null;
  taxNumber?: string | null;
  registrationNumber?: string | null;
  baseCurrencyCode: string;
  timezone: string;
  countryCode: string;
};

type CompanyUser = {
  userId: string;
  email: string;
  displayName: string;
  status: string;
  roles?: RoleDetail[];
  assignedRoles?: RoleDetail[];
  invitedAt?: string | null;
  joinedAt?: string | null;
  createdAt?: string | null;
};

type RoleInfo = {
  roleCode: string;
  name: string;
  description: string;
  permissions: {
    permissionCode: string;
    description: string;
    moduleCode: string;
  }[];
};

type AuditEvent = {
  eventId: string;
  eventType: string;
  entityName: string;
  entityId?: string | null;
  actorEmail?: string | null;
  occurredAt: string;
  details?: string | null;
};

export default function FoundationWorkspace({ enabled, workspace, onCurrencySettingsUpdated }: Props) {
  const [activeTab, setActiveTab] = useState<"profile" | "settings" | "users" | "roles" | "audit">("profile");
  const [settingsSection, setSettingsSection] = useState<"currency" | "general">("currency");
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [settings, setSettings] = useState<CompanySettings | null>(null);
  const [currencySettings, setCurrencySettings] = useState<CompanyCurrencySettingsDto | null>(null);
  const [currencyHistory, setCurrencyHistory] = useState<CompanyCurrencyHistoryDto[]>([]);
  const [currencies, setCurrencies] = useState<CurrencyOption[]>(FALLBACK_CURRENCIES);
  const [exchangeRates, setExchangeRates] = useState<ExchangeRateDto[]>([]);
  const [currencyPermissionDenied, setCurrencyPermissionDenied] = useState(false);
  const [users, setUsers] = useState<CompanyUser[]>([]);
  const [roles, setRoles] = useState<RoleInfo[]>([]);
  const [auditEvents, setAuditEvents] = useState<AuditEvent[]>([]);
  const [statusMessage, setStatusMessage] = useState("");
  const [errorMessage, setErrorMessage] = useState("");
  const [isLoading, setIsLoading] = useState(false);

  // Form states
  const [editLegalName, setEditLegalName] = useState("");
  const [editTaxNumber, setEditTaxNumber] = useState("");
  const [editRegNumber, setEditRegNumber] = useState("");
  const [editTimezone, setEditTimezone] = useState("");

  // Currency settings form + confirmation dialog
  const [editFunctionalCurrency, setEditFunctionalCurrency] = useState("PKR");
  const [editPresentationCurrency, setEditPresentationCurrency] = useState("PKR");
  const [editCurrencyEffectiveDate, setEditCurrencyEffectiveDate] = useState(new Date().toISOString().slice(0, 10));
  const [editCurrencyReason, setEditCurrencyReason] = useState("");
  const [showCurrencyConfirmModal, setShowCurrencyConfirmModal] = useState(false);
  const [currencyConfirmChecked, setCurrencyConfirmChecked] = useState(false);

  // Exchange rate form
  const [editingRateId, setEditingRateId] = useState<string | null>(null);
  const [rateFromCurrency, setRateFromCurrency] = useState("USD");
  const [rateToCurrency, setRateToCurrency] = useState("PKR");
  const [rateValue, setRateValue] = useState("");
  const [rateEffectiveDate, setRateEffectiveDate] = useState(new Date().toISOString().slice(0, 10));
  const [rateType, setRateType] = useState<RateType>("closing");
  const [rateSource, setRateSource] = useState("SBP");
  const [rateReference, setRateReference] = useState("");

  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteName, setInviteName] = useState("");
  const [inviteRole, setInviteRole] = useState("company_user");

  useEffect(() => {
    if (!enabled || !workspace) return;
    void loadAllData();
  }, [enabled, workspace?.tenantId, workspace?.companyId]);

  const loadAllData = async () => {
    if (!workspace) return;
    setIsLoading(true);
    setErrorMessage("");
    setCurrencyPermissionDenied(false);
    try {
      const [profRes, setRes, usrRes, rolRes, audRes, curSetRes, curHistRes, curListRes, fxRes] = await Promise.all([
        apiFetch("/api/v1/foundation/profile", {}, workspace).catch(() => null),
        apiFetch("/api/v1/foundation/settings", {}, workspace).catch(() => null),
        apiFetch("/api/v1/foundation/users", {}, workspace).catch(() => null),
        apiFetch("/api/v1/foundation/roles", {}, workspace).catch(() => null),
        apiFetch("/api/v1/foundation/audit-events?limit=30", {}, workspace).catch(() => null),
        apiFetch("/api/v1/foundation/currency-settings", {}, workspace).catch(() => null),
        apiFetch("/api/v1/foundation/currency-settings/history?limit=50", {}, workspace).catch(() => null),
        apiFetch("/api/v1/finance/currencies", {}, workspace).catch(() => null),
        apiFetch("/api/v1/finance/exchange-rates", {}, workspace).catch(() => null),
      ]);

      if (profRes && profRes.ok) {
        setProfile(await profRes.json());
      }
      if (setRes && setRes.ok) {
        const data: CompanySettings = await setRes.json();
        setSettings(data);
        setEditLegalName(data.legalName ?? "");
        setEditTaxNumber(data.taxNumber ?? "");
        setEditRegNumber(data.registrationNumber ?? "");
        setEditTimezone(data.timezone ?? "");
      }
      if (curSetRes) {
        if (curSetRes.status === 403) {
          setCurrencyPermissionDenied(true);
        } else if (curSetRes.ok) {
          const curData: CompanyCurrencySettingsDto = await curSetRes.json();
          setCurrencySettings(curData);
          setEditFunctionalCurrency(curData.functionalCurrencyCode);
          setEditPresentationCurrency(curData.presentationCurrencyCode);
          setEditCurrencyEffectiveDate(curData.currencyEffectiveDate);
        }
      }
      if (curHistRes && curHistRes.ok) {
        setCurrencyHistory(await curHistRes.json());
      }
      if (curListRes && curListRes.ok) {
        const curItems = (await curListRes.json()) as CurrencyOption[];
        if (Array.isArray(curItems) && curItems.length > 0) {
          setCurrencies(curItems);
        }
      }
      if (fxRes && fxRes.ok) {
        setExchangeRates(await fxRes.json());
      }
      if (usrRes && usrRes.ok) {
        setUsers(await usrRes.json());
      }
      if (rolRes && rolRes.ok) {
        setRoles(await rolRes.json());
      }
      if (audRes && audRes.ok) {
        setAuditEvents(await audRes.json());
      }
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to load organization foundation data.");
    } finally {
      setIsLoading(false);
    }
  };

  const handleRequestSaveCurrencySettings = (e: FormEvent) => {
    e.preventDefault();
    setStatusMessage("");
    setErrorMessage("");
    const check = validateCurrencySettingsDraft(
      {
        functionalCurrencyCode: editFunctionalCurrency,
        presentationCurrencyCode: editPresentationCurrency,
        effectiveDate: editCurrencyEffectiveDate,
        changeReason: editCurrencyReason,
      },
      currencySettings
    );
    if (!check.valid) {
      setErrorMessage(check.errors.join(" "));
      return;
    }
    if (!check.hasChanges) {
      setStatusMessage("No currency configuration changes to save.");
      return;
    }
    if (check.requiresConfirmation) {
      setCurrencyConfirmChecked(false);
      setShowCurrencyConfirmModal(true);
      return;
    }
    void submitCurrencySettings(false);
  };

  const submitCurrencySettings = async (confirmFutureTransactions: boolean) => {
    if (!workspace) return;
    setIsLoading(true);
    setStatusMessage("");
    setErrorMessage("");
    try {
      const res = await apiFetch(
        "/api/v1/foundation/currency-settings",
        {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            functionalCurrencyCode: editFunctionalCurrency,
            presentationCurrencyCode: editPresentationCurrency,
            effectiveDate: editCurrencyEffectiveDate,
            confirmFutureTransactions,
            changeReason: editCurrencyReason.trim() || null,
          }),
        },
        workspace
      );
      if (!res.ok) {
        const errBody = await res.json().catch(() => ({}));
        throw new Error(errBody.detail || errBody.error || `Currency update failed (HTTP ${res.status})`);
      }
      const updated = (await res.json()) as CompanyCurrencySettingsDto;
      setCurrencySettings(updated);
      setEditFunctionalCurrency(updated.functionalCurrencyCode);
      setEditPresentationCurrency(updated.presentationCurrencyCode);
      setEditCurrencyEffectiveDate(updated.currencyEffectiveDate);
      setEditCurrencyReason("");
      setShowCurrencyConfirmModal(false);
      setCurrencyConfirmChecked(false);
      setStatusMessage(
        `Currency settings saved: Functional ${updated.functionalCurrencyCode} · Presentation ${updated.presentationCurrencyCode} (effective ${updated.currencyEffectiveDate}). Historical transactions remain unchanged.`
      );
      onCurrencySettingsUpdated?.();
      await loadAllData();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to save currency settings.");
    } finally {
      setIsLoading(false);
    }
  };

  const handleSaveExchangeRate = async (e: FormEvent) => {
    e.preventDefault();
    if (!workspace) return;
    setStatusMessage("");
    setErrorMessage("");

    const validation = validateExchangeRateDraft(
      {
        fromCurrency: rateFromCurrency,
        toCurrency: rateToCurrency,
        rate: rateValue,
        effectiveDate: rateEffectiveDate,
        rateType,
        source: rateSource,
        reference: rateReference,
      },
      exchangeRates,
      editingRateId
    );
    if (!validation.valid || validation.parsedRate === null) {
      setErrorMessage(validation.errors.join(" "));
      return;
    }

    setIsLoading(true);
    try {
      const endpoint = editingRateId
        ? `/api/v1/finance/exchange-rates/${editingRateId}`
        : "/api/v1/finance/exchange-rates";
      const method = editingRateId ? "PUT" : "POST";
      const res = await apiFetch(
        endpoint,
        {
          method,
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            fromCurrency: rateFromCurrency.trim().toUpperCase(),
            toCurrency: rateToCurrency.trim().toUpperCase(),
            rate: validation.parsedRate,
            effectiveDate: rateEffectiveDate,
            rateType,
            source: rateSource.trim(),
            reference: rateReference.trim() || null,
          }),
        },
        workspace
      );
      if (!res.ok) {
        const errBody = await res.json().catch(() => ({}));
        throw new Error(errBody.detail || errBody.error || `Exchange rate save failed (HTTP ${res.status})`);
      }
      const saved = (await res.json()) as ExchangeRateDto;
      setStatusMessage(
        `${editingRateId ? "Updated" : "Recorded"} ${formatExchangeRateDisplay(saved.fromCurrency, saved.toCurrency, saved.rate)} (${saved.rateType}, effective ${saved.effectiveDate}).`
      );
      setEditingRateId(null);
      setRateValue("");
      setRateReference("");
      await loadAllData();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to save exchange rate.");
    } finally {
      setIsLoading(false);
    }
  };

  const handleUpdateSettings = async (e: FormEvent) => {
    e.preventDefault();
    if (!workspace) return;
    setIsLoading(true);
    setStatusMessage("");
    setErrorMessage("");
    try {
      const res = await apiFetch(
        "/api/v1/foundation/settings",
        {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            legalName: editLegalName,
            taxNumber: editTaxNumber,
            registrationNumber: editRegNumber,
            timezone: editTimezone,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`Settings update failed (${res.status})`);
      const updated: CompanySettings = await res.json();
      setSettings(updated);
      setStatusMessage("Company settings successfully updated.");
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to update settings.");
    } finally {
      setIsLoading(false);
    }
  };

  const handleInviteUser = async (e: FormEvent) => {
    e.preventDefault();
    if (!workspace) return;
    setIsLoading(true);
    setStatusMessage("");
    setErrorMessage("");
    try {
      const res = await apiFetch(
        "/api/v1/foundation/users",
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            email: inviteEmail,
            displayName: inviteName,
            roleCode: inviteRole,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`User invitation failed (${res.status})`);
      setStatusMessage(`User ${inviteEmail} assigned to company successfully.`);
      setInviteEmail("");
      setInviteName("");
      // Refresh user list
      const usrRes = await apiFetch("/api/v1/foundation/users", {}, workspace);
      if (usrRes.ok) setUsers(await usrRes.json());
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to invite user.");
    } finally {
      setIsLoading(false);
    }
  };

  if (!enabled) {
    return (
      <section className="catalog" style={{ marginTop: "24px" }}>
        <div className="catalog-heading">
          <div>
            <p className="eyebrow">ORGANIZATION FOUNDATION</p>
            <h2>Company & Administration</h2>
          </div>
        </div>
        <p className="catalog-note">Sign in and select an active company to access administration and foundation settings.</p>
      </section>
    );
  }

  return (
    <section className="catalog" style={{ marginTop: "24px" }}>
      <div className="catalog-heading">
        <div>
          <p className="eyebrow">STAGE 1 · ERP FOUNDATION</p>
          <h2>Company & Organization Administration</h2>
        </div>
        <span className="tag tag-progress">{workspace?.companyName ?? "Active Company"}</span>
      </div>

      <nav style={{ display: "flex", gap: "8px", margin: "16px 0", borderBottom: "1px solid #d8deea", paddingBottom: "8px" }}>
        <button
          type="button"
          className="auth-button"
          onClick={() => setActiveTab("profile")}
          style={{ background: activeTab === "profile" ? "#2563eb" : "#f1f5f9", color: activeTab === "profile" ? "white" : "#334155" }}
        >
          My Profile & Access
        </button>
        <button
          type="button"
          className="auth-button"
          onClick={() => setActiveTab("settings")}
          style={{ background: activeTab === "settings" ? "#2563eb" : "#f1f5f9", color: activeTab === "settings" ? "white" : "#334155" }}
        >
          Company Settings
        </button>
        <button
          type="button"
          className="auth-button"
          onClick={() => setActiveTab("users")}
          style={{ background: activeTab === "users" ? "#2563eb" : "#f1f5f9", color: activeTab === "users" ? "white" : "#334155" }}
        >
          Users & Memberships ({users.length})
        </button>
        <button
          type="button"
          className="auth-button"
          onClick={() => setActiveTab("roles")}
          style={{ background: activeTab === "roles" ? "#2563eb" : "#f1f5f9", color: activeTab === "roles" ? "white" : "#334155" }}
        >
          Roles & Permissions ({roles.length})
        </button>
        <button
          type="button"
          className="auth-button"
          onClick={() => setActiveTab("audit")}
          style={{ background: activeTab === "audit" ? "#2563eb" : "#f1f5f9", color: activeTab === "audit" ? "white" : "#334155" }}
        >
          Audit Trail ({auditEvents.length})
        </button>
      </nav>

      {statusMessage && <p style={{ color: "#166534", background: "#f0fdf4", padding: "8px 12px", borderRadius: "6px" }}>{statusMessage}</p>}
      {errorMessage && <p style={{ color: "#991b1b", background: "#fef2f2", padding: "8px 12px", borderRadius: "6px" }}>{errorMessage}</p>}
      {isLoading && <p style={{ color: "#475569" }}>Loading foundation data…</p>}

      {/* ── PROFILE TAB ─────────────────────────────────────────── */}
      {activeTab === "profile" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px", marginTop: "12px" }}>
          <div style={{ background: "#f8fafc", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "16px" }}>User Profile</h3>
            <p><strong>Name:</strong> {profile?.displayName || "N/A"}</p>
            <p><strong>Email:</strong> {profile?.email || "N/A"}</p>
            <p><strong>User ID:</strong> <code style={{ fontSize: "11px" }}>{profile?.userId || "N/A"}</code></p>
            <p><strong>Tenant:</strong> {workspace?.tenantName} (<code style={{ fontSize: "11px" }}>{workspace?.tenantId}</code>)</p>
            <p><strong>Company:</strong> {workspace?.companyName} (<code style={{ fontSize: "11px" }}>{workspace?.companyId}</code>)</p>
          </div>

          <div style={{ background: "#f8fafc", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "16px" }}>Assigned Roles & Permissions</h3>
            <p><strong>Roles:</strong></p>
            <div style={{ display: "flex", flexWrap: "wrap", gap: "6px", marginBottom: "12px" }}>
              {profile?.roles && profile.roles.length > 0 ? (
                profile.roles.map((r, idx) => {
                  const roleName = typeof r === "string" ? r : (r.name || r.roleCode || "Role");
                  const roleKey = typeof r === "string" ? `${r}-${idx}` : (r.roleCode || r.name || idx);
                  return (
                    <span key={roleKey} className="tag tag-starter" style={{ fontSize: "12px" }}>
                      {roleName}
                    </span>
                  );
                })
              ) : (
                <span style={{ color: "#64748b" }}>Standard Member</span>
              )}
            </div>
            <p><strong>Effective Permissions ({profile?.permissions?.length ?? 0}):</strong></p>
            <div style={{ maxHeight: "150px", overflowY: "auto", display: "flex", flexWrap: "wrap", gap: "4px" }}>
              {profile?.permissions?.map((p, idx) => {
                const permText = typeof p === "string" ? p : JSON.stringify(p);
                return (
                  <span key={`${permText}-${idx}`} style={{ fontSize: "11px", background: "#e2e8f0", padding: "2px 6px", borderRadius: "4px" }}>
                    {permText}
                  </span>
                );
              })}
            </div>
          </div>
        </div>
      )}

      {/* ── SETTINGS TAB (ADMIN → COMPANY SETTINGS → CURRENCY SETTINGS) ──── */}
      {activeTab === "settings" && (
        <div style={{ marginTop: "12px", display: "grid", gap: "20px" }}>
          <div style={{ display: "flex", gap: "8px", alignItems: "center", flexWrap: "wrap" }}>
            <button
              type="button"
              onClick={() => setSettingsSection("currency")}
              style={{
                padding: "6px 12px",
                borderRadius: "6px",
                border: "1px solid #cbd5e1",
                background: settingsSection === "currency" ? "#0f172a" : "#f8fafc",
                color: settingsSection === "currency" ? "#ffffff" : "#334155",
                fontWeight: 600,
                cursor: "pointer",
              }}
            >
              Currency Settings & Exchange Rates
            </button>
            <button
              type="button"
              onClick={() => setSettingsSection("general")}
              style={{
                padding: "6px 12px",
                borderRadius: "6px",
                border: "1px solid #cbd5e1",
                background: settingsSection === "general" ? "#0f172a" : "#f8fafc",
                color: settingsSection === "general" ? "#ffffff" : "#334155",
                fontWeight: 600,
                cursor: "pointer",
              }}
            >
              Company Profile & Legal Identity
            </button>
          </div>

          {settingsSection === "currency" && (
            <div style={{ display: "grid", gap: "18px" }}>
              {/* Explanation Box: Functional vs Presentation Currency */}
              <div
                style={{
                  background: "#f8fafc",
                  border: "1px solid #cbd5e1",
                  borderRadius: "8px",
                  padding: "16px",
                }}
              >
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: "8px" }}>
                  <div>
                    <p style={{ margin: 0, fontSize: "11px", fontWeight: 700, color: "#475569", letterSpacing: "0.04em" }}>
                      ADMIN · COMPANY SETTINGS · CURRENCY SETTINGS
                    </p>
                    <h3 style={{ margin: "4px 0 0 0", fontSize: "18px" }}>
                      Functional vs Presentation Currency Configuration
                    </h3>
                  </div>
                  {currencySettings && (
                    <span
                      style={{
                        padding: "4px 10px",
                        borderRadius: "999px",
                        fontSize: "12px",
                        fontWeight: 700,
                        background: currencySettings.hasPostedTransactions ? "#fef3c7" : "#dcfce7",
                        color: currencySettings.hasPostedTransactions ? "#92400e" : "#166534",
                      }}
                    >
                      {currencySettings.hasPostedTransactions
                        ? "Posted Historical Ledger Active (Preserved)"
                        : "Pre-Posting Configuration"}
                    </span>
                  )}
                </div>

                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))",
                    gap: "12px",
                    marginTop: "12px",
                  }}
                >
                  <div style={{ background: "#ffffff", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                    <strong style={{ color: "#1e40af", display: "block", marginBottom: "4px" }}>
                      Functional / Accounting Currency ({currencySettings?.functionalCurrencyCode ?? editFunctionalCurrency})
                    </strong>
                    <p style={{ margin: 0, fontSize: "13px", color: "#334155", lineHeight: 1.45 }}>
                      {currencySettings?.functionalCurrencyGuidance || FUNCTIONAL_CURRENCY_EXPLANATION}
                    </p>
                  </div>
                  <div style={{ background: "#ffffff", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                    <strong style={{ color: "#0f766e", display: "block", marginBottom: "4px" }}>
                      Presentation / Reporting Currency ({currencySettings?.presentationCurrencyCode ?? editPresentationCurrency})
                    </strong>
                    <p style={{ margin: 0, fontSize: "13px", color: "#334155", lineHeight: 1.45 }}>
                      {currencySettings?.presentationCurrencyGuidance || PRESENTATION_CURRENCY_EXPLANATION}
                    </p>
                  </div>
                </div>
              </div>

              {currencyPermissionDenied ? (
                <div style={{ background: "#fffbeb", border: "1px solid #f59e0b", padding: "14px", borderRadius: "8px", color: "#92400e" }}>
                  Your active role does not have permission (<code>admin.currency_settings.view</code>) to view or edit Company Currency Settings. Contact your Tenant or Finance Administrator.
                </div>
              ) : (
                <>
                  {/* Currency Settings Form */}
                  <form
                    onSubmit={handleRequestSaveCurrencySettings}
                    style={{ background: "#ffffff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}
                  >
                    <h4 style={{ margin: "0 0 12px 0" }}>Company Currency Policy ({workspace?.companyCode ?? ""})</h4>
                    <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: "12px" }}>
                      <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontWeight: 600, fontSize: "13px" }}>
                        Functional / Accounting Currency
                        <select
                          id="functional-currency-select"
                          value={editFunctionalCurrency}
                          onChange={(e) => setEditFunctionalCurrency(e.target.value)}
                          style={{ padding: "8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                        >
                          {currencies.map((c) => (
                            <option key={`func-${c.code}`} value={c.code}>
                              {c.code} — {c.name}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontWeight: 600, fontSize: "13px" }}>
                        Presentation / Reporting Currency
                        <select
                          id="presentation-currency-select"
                          value={editPresentationCurrency}
                          onChange={(e) => setEditPresentationCurrency(e.target.value)}
                          style={{ padding: "8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                        >
                          {currencies.map((c) => (
                            <option key={`pres-${c.code}`} value={c.code}>
                              {c.code} — {c.name}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontWeight: 600, fontSize: "13px" }}>
                        Effective Date
                        <input
                          id="currency-effective-date"
                          type="date"
                          value={editCurrencyEffectiveDate}
                          onChange={(e) => setEditCurrencyEffectiveDate(e.target.value)}
                          required
                          style={{ padding: "8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                        />
                      </label>
                      <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontWeight: 600, fontSize: "13px" }}>
                        Audit / Approval Reason
                        <input
                          id="currency-change-reason"
                          type="text"
                          value={editCurrencyReason}
                          onChange={(e) => setEditCurrencyReason(e.target.value)}
                          placeholder="e.g. Board policy aligning PKR ledger with USD reporting"
                          style={{ padding: "8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                        />
                      </label>
                    </div>
                    <div style={{ marginTop: "14px", display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: "8px" }}>
                      <span style={{ fontSize: "12px", color: "#475569" }}>
                        Historical posted journals and FIFO inventory layers are immutable and never rewritten when settings change.
                      </span>
                      <button
                        type="submit"
                        disabled={isLoading}
                        className="auth-button"
                        style={{ background: "#2563eb", color: "white", padding: "8px 16px" }}
                      >
                        Save Currency Settings
                      </button>
                    </div>
                  </form>

                  {/* Confirmation Modal for Changes Affecting Future Transactions */}
                  {showCurrencyConfirmModal && (
                    <div
                      role="dialog"
                      aria-modal="true"
                      style={{
                        background: "#fffbeb",
                        border: "2px solid #d97706",
                        borderRadius: "8px",
                        padding: "16px",
                      }}
                    >
                      <h4 style={{ margin: "0 0 8px 0", color: "#92400e" }}>
                        Confirm Company Currency Configuration Update
                      </h4>
                      <p style={{ margin: "0 0 10px 0", fontSize: "13px", color: "#78350f" }}>
                        This change updates company-level currency settings effective{" "}
                        <strong>{editCurrencyEffectiveDate}</strong>:
                      </p>
                      <ul style={{ margin: "0 0 12px 18px", fontSize: "13px", color: "#78350f" }}>
                        <li>
                          Functional Currency:{" "}
                          <strong>{currencySettings?.functionalCurrencyCode ?? "—"}</strong> {"->"}{" "}
                          <strong>{editFunctionalCurrency}</strong>
                        </li>
                        <li>
                          Presentation Currency:{" "}
                          <strong>{currencySettings?.presentationCurrencyCode ?? "—"}</strong> {"->"}{" "}
                          <strong>{editPresentationCurrency}</strong>
                        </li>
                        <li>
                          Existing inventory layers, posted journals, and GL 1200 history remain preserved in their original transaction currencies. Only future default transactions use the updated functional currency.
                        </li>
                      </ul>
                      <label style={{ display: "flex", gap: "8px", alignItems: "center", fontSize: "13px", fontWeight: 600, marginBottom: "12px", color: "#78350f" }}>
                        <input
                          type="checkbox"
                          checked={currencyConfirmChecked}
                          onChange={(e) => setCurrencyConfirmChecked(e.target.checked)}
                        />
                        I confirm this configuration applies prospectively to future transactions without altering historical records.
                      </label>
                      <div style={{ display: "flex", gap: "8px" }}>
                        <button
                          type="button"
                          disabled={!currencyConfirmChecked || isLoading}
                          onClick={() => void submitCurrencySettings(true)}
                          className="auth-button"
                          style={{
                            background: currencyConfirmChecked ? "#b45309" : "#d1d5db",
                            color: "white",
                            padding: "8px 14px",
                          }}
                        >
                          Confirm & Apply Settings
                        </button>
                        <button
                          type="button"
                          onClick={() => {
                            setShowCurrencyConfirmModal(false);
                            setCurrencyConfirmChecked(false);
                          }}
                          className="auth-button"
                          style={{ background: "#e2e8f0", color: "#334155", padding: "8px 14px" }}
                        >
                          Cancel
                        </button>
                      </div>
                    </div>
                  )}

                  {/* Exchange Rate Management */}
                  <div style={{ background: "#ffffff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
                    <h4 style={{ margin: "0 0 4px 0" }}>
                      Exchange Rate Management ({currencySettings?.functionalCurrencyCode ?? editFunctionalCurrency} {"<->"} {currencySettings?.presentationCurrencyCode ?? editPresentationCurrency})
                    </h4>
                    <p style={{ margin: "0 0 12px 0", fontSize: "12px", color: "#475569" }}>
                      Enter explicit rates with rate type, effective date, and source authority. Maintain a single consistent direction per date and rate type; duplicate or inverse-ambiguous entries are rejected.
                    </p>

                    <form onSubmit={handleSaveExchangeRate} style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0", marginBottom: "14px" }}>
                      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))", gap: "10px" }}>
                        <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", fontWeight: 600 }}>
                          From Currency
                          <select
                            id="fx-from-currency"
                            value={rateFromCurrency}
                            onChange={(e) => setRateFromCurrency(e.target.value)}
                            disabled={Boolean(editingRateId)}
                            style={{ padding: "6px 8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                          >
                            {currencies.map((c) => (
                              <option key={`fx-from-${c.code}`} value={c.code}>{c.code}</option>
                            ))}
                          </select>
                        </label>
                        <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", fontWeight: 600 }}>
                          To Currency
                          <select
                            id="fx-to-currency"
                            value={rateToCurrency}
                            onChange={(e) => setRateToCurrency(e.target.value)}
                            disabled={Boolean(editingRateId)}
                            style={{ padding: "6px 8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                          >
                            {currencies.map((c) => (
                              <option key={`fx-to-${c.code}`} value={c.code}>{c.code}</option>
                            ))}
                          </select>
                        </label>
                        <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", fontWeight: 600 }}>
                          Rate (1 {rateFromCurrency} = ? {rateToCurrency})
                          <input
                            id="fx-rate-value"
                            type="number"
                            step="0.000001"
                            min="0.000001"
                            placeholder="e.g. 278.50"
                            value={rateValue}
                            onChange={(e) => setRateValue(e.target.value)}
                            required
                            style={{ padding: "6px 8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                          />
                        </label>
                        <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", fontWeight: 600 }}>
                          Effective Date
                          <input
                            id="fx-effective-date"
                            type="date"
                            value={rateEffectiveDate}
                            onChange={(e) => setRateEffectiveDate(e.target.value)}
                            disabled={Boolean(editingRateId)}
                            required
                            style={{ padding: "6px 8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                          />
                        </label>
                        <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", fontWeight: 600 }}>
                          Rate Type
                          <select
                            id="fx-rate-type"
                            value={rateType}
                            onChange={(e) => setRateType(e.target.value as RateType)}
                            disabled={Boolean(editingRateId)}
                            style={{ padding: "6px 8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                          >
                            <option value="closing">closing (Assets & Liabilities)</option>
                            <option value="transaction">transaction (Income, Expenses & Equity)</option>
                            <option value="average">average (Period P&L Average)</option>
                          </select>
                        </label>
                        <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", fontWeight: 600 }}>
                          Source
                          <input
                            id="fx-rate-source"
                            type="text"
                            placeholder="SBP, Reuters, Manual"
                            value={rateSource}
                            onChange={(e) => setRateSource(e.target.value)}
                            required
                            style={{ padding: "6px 8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                          />
                        </label>
                        <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px", fontWeight: 600 }}>
                          Reference (Optional)
                          <input
                            id="fx-rate-reference"
                            type="text"
                            placeholder="SBP Circular / Deal Ref"
                            value={rateReference}
                            onChange={(e) => setRateReference(e.target.value)}
                            style={{ padding: "6px 8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                          />
                        </label>
                      </div>
                      <div style={{ marginTop: "10px", display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: "8px" }}>
                        <span style={{ fontSize: "12px", color: "#475569" }}>
                          {describeRateType(rateType)}
                          {Number(rateValue) > 0 && ` · Preview: ${formatExchangeRateDisplay(rateFromCurrency, rateToCurrency, Number(rateValue))}`}
                        </span>
                        <div style={{ display: "flex", gap: "8px" }}>
                          {editingRateId && (
                            <button
                              type="button"
                              onClick={() => {
                                setEditingRateId(null);
                                setRateValue("");
                                setRateReference("");
                              }}
                              className="auth-button"
                              style={{ background: "#e2e8f0", color: "#334155", padding: "6px 12px" }}
                            >
                              Cancel Edit
                            </button>
                          )}
                          <button
                            type="submit"
                            disabled={isLoading}
                            className="auth-button"
                            style={{ background: "#0f766e", color: "white", padding: "6px 14px" }}
                          >
                            {editingRateId ? "Update Exchange Rate" : "+ Add Exchange Rate"}
                          </button>
                        </div>
                      </div>
                    </form>

                    <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "12px" }}>
                      <thead>
                        <tr style={{ background: "#f1f5f9", textAlign: "left" }}>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Pair</th>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Rate</th>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Effective Date</th>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Rate Type</th>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Source / Ref</th>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Updated By</th>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {exchangeRates.length === 0 ? (
                          <tr>
                            <td colSpan={7} style={{ padding: "14px", textAlign: "center", color: "#64748b" }}>
                              No company exchange rates configured yet. USD presentation statements and valuation conversions remain marked <strong>Unavailable</strong> until applicable rates are entered.
                            </td>
                          </tr>
                        ) : (
                          exchangeRates.map((r) => (
                            <tr key={r.exchangeRateId}>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0", fontWeight: 700 }}>
                                {r.fromCurrency}/{r.toCurrency}
                              </td>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                                {formatExchangeRateDisplay(r.fromCurrency, r.toCurrency, r.rate)}
                              </td>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>{r.effectiveDate}</td>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                                <span className="tag tag-starter">{r.rateType}</span>
                              </td>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                                {r.source}
                                {r.reference ? ` · ${r.reference}` : ""}
                              </td>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                                {(r.updatedByEmail || r.createdByEmail || "admin")} · {new Date(r.updatedAt || r.createdAt).toLocaleString()}
                              </td>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                                <button
                                  type="button"
                                  onClick={() => {
                                    setEditingRateId(r.exchangeRateId);
                                    setRateFromCurrency(r.fromCurrency);
                                    setRateToCurrency(r.toCurrency);
                                    setRateValue(String(r.rate));
                                    setRateEffectiveDate(r.effectiveDate);
                                    setRateType(r.rateType);
                                    setRateSource(r.source);
                                    setRateReference(r.reference ?? "");
                                  }}
                                  style={{ padding: "3px 8px", fontSize: "11px", cursor: "pointer" }}
                                >
                                  Edit
                                </button>
                              </td>
                            </tr>
                          ))
                        )}
                      </tbody>
                    </table>
                  </div>

                  {/* Currency Change Audit History */}
                  <div style={{ background: "#ffffff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
                    <h4 style={{ margin: "0 0 10px 0" }}>Currency Setting Change History</h4>
                    <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "12px" }}>
                      <thead>
                        <tr style={{ background: "#f1f5f9", textAlign: "left" }}>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Changed At</th>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>User</th>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Functional</th>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Presentation</th>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Effective Date</th>
                          <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Reason</th>
                        </tr>
                      </thead>
                      <tbody>
                        {currencyHistory.length === 0 ? (
                          <tr>
                            <td colSpan={6} style={{ padding: "12px", textAlign: "center", color: "#64748b" }}>
                              No currency setting changes recorded yet.
                            </td>
                          </tr>
                        ) : (
                          currencyHistory.map((h) => (
                            <tr key={h.historyId}>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0", whiteSpace: "nowrap" }}>
                                {new Date(h.changedAt).toLocaleString()}
                              </td>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                                {h.changedByName || h.changedByEmail || h.changedByUserId.slice(0, 8)}
                              </td>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                                {h.previousFunctionalCurrency} {"->"} <strong>{h.newFunctionalCurrency}</strong>
                              </td>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                                {h.previousPresentationCurrency} {"->"} <strong>{h.newPresentationCurrency}</strong>
                              </td>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                                {h.newEffectiveDate}
                              </td>
                              <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                                {h.changeReason || "—"}
                              </td>
                            </tr>
                          ))
                        )}
                      </tbody>
                    </table>
                  </div>
                </>
              )}
            </div>
          )}

          {settingsSection === "general" && (
            <form onSubmit={handleUpdateSettings} style={{ maxWidth: "600px" }}>
              <div style={{ display: "grid", gap: "12px" }}>
                <div>
                  <label style={{ display: "block", fontWeight: 600, marginBottom: "4px" }}>Company Code</label>
                  <input value={settings?.companyCode ?? ""} disabled style={{ width: "100%", padding: "8px", background: "#f1f5f9" }} />
                </div>
                <div>
                  <label style={{ display: "block", fontWeight: 600, marginBottom: "4px" }}>Company Display Name</label>
                  <input value={settings?.name ?? ""} disabled style={{ width: "100%", padding: "8px", background: "#f1f5f9" }} />
                </div>
                <div>
                  <label style={{ display: "block", fontWeight: 600, marginBottom: "4px" }}>Legal Registered Name</label>
                  <input
                    value={editLegalName}
                    onChange={(e) => setEditLegalName(e.target.value)}
                    placeholder="e.g. Acme Logistics Private Limited"
                    style={{ width: "100%", padding: "8px" }}
                  />
                </div>
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                  <div>
                    <label style={{ display: "block", fontWeight: 600, marginBottom: "4px" }}>Tax / NTN Number</label>
                    <input
                      value={editTaxNumber}
                      onChange={(e) => setEditTaxNumber(e.target.value)}
                      placeholder="e.g. 1234567-8"
                      style={{ width: "100%", padding: "8px" }}
                    />
                  </div>
                  <div>
                    <label style={{ display: "block", fontWeight: 600, marginBottom: "4px" }}>Registration / Incorporation No.</label>
                    <input
                      value={editRegNumber}
                      onChange={(e) => setEditRegNumber(e.target.value)}
                      placeholder="e.g. 00129384"
                      style={{ width: "100%", padding: "8px" }}
                    />
                  </div>
                </div>
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                  <div>
                    <label style={{ display: "block", fontWeight: 600, marginBottom: "4px" }}>Active Functional / Presentation Currencies</label>
                    <input
                      value={`${currencySettings?.functionalCurrencyCode ?? settings?.baseCurrencyCode ?? "PKR"} / ${currencySettings?.presentationCurrencyCode ?? currencySettings?.functionalCurrencyCode ?? settings?.baseCurrencyCode ?? "PKR"}`}
                      disabled
                      style={{ width: "100%", padding: "8px", background: "#f1f5f9" }}
                    />
                  </div>
                  <div>
                    <label style={{ display: "block", fontWeight: 600, marginBottom: "4px" }}>Default Timezone</label>
                    <input
                      value={editTimezone}
                      onChange={(e) => setEditTimezone(e.target.value)}
                      placeholder="e.g. Asia/Karachi or UTC"
                      style={{ width: "100%", padding: "8px" }}
                    />
                  </div>
                </div>
                <button type="submit" className="auth-button" style={{ background: "#2563eb", color: "white", padding: "10px", marginTop: "8px" }}>
                  Save Company Settings
                </button>
              </div>
            </form>
          )}
        </div>
      )}

      {/* ── USERS TAB ───────────────────────────────────────────── */}
      {activeTab === "users" && (
        <div style={{ marginTop: "12px" }}>
          <form onSubmit={handleInviteUser} style={{ background: "#f8fafc", padding: "14px", borderRadius: "8px", border: "1px solid #e2e8f0", marginBottom: "16px" }}>
            <h4 style={{ margin: "0 0 8px 0" }}>Invite or Assign User to Company</h4>
            <div style={{ display: "flex", gap: "8px", flexWrap: "wrap", alignItems: "center" }}>
              <input
                type="email"
                placeholder="User email"
                value={inviteEmail}
                onChange={(e) => setInviteEmail(e.target.value)}
                required
                style={{ padding: "8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
              />
              <input
                type="text"
                placeholder="Full display name"
                value={inviteName}
                onChange={(e) => setInviteName(e.target.value)}
                required
                style={{ padding: "8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
              />
              <select
                value={inviteRole}
                onChange={(e) => setInviteRole(e.target.value)}
                style={{ padding: "8px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
              >
                <option value="company_user">Company User</option>
                <option value="company_admin">Company Admin</option>
                <option value="inventory_manager">Inventory Manager</option>
                <option value="accountant">Accountant</option>
                <option value="cashier">Cashier</option>
              </select>
              <button type="submit" className="auth-button" style={{ background: "#2563eb", color: "white" }}>
                Add to Company
              </button>
            </div>
          </form>

          <table style={{ width: "100%", borderCollapse: "collapse", marginTop: "8px", fontSize: "13px" }}>
            <thead>
              <tr style={{ background: "#f1f5f9", textAlign: "left" }}>
                <th style={{ padding: "8px", border: "1px solid #e2e8f0" }}>User</th>
                <th style={{ padding: "8px", border: "1px solid #e2e8f0" }}>Email</th>
                <th style={{ padding: "8px", border: "1px solid #e2e8f0" }}>Status</th>
                <th style={{ padding: "8px", border: "1px solid #e2e8f0" }}>Assigned Roles</th>
                <th style={{ padding: "8px", border: "1px solid #e2e8f0" }}>Joined</th>
              </tr>
            </thead>
            <tbody>
              {users.map((u) => (
                <tr key={u.userId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "8px", border: "1px solid #e2e8f0" }}><strong>{u.displayName}</strong></td>
                  <td style={{ padding: "8px", border: "1px solid #e2e8f0" }}>{u.email}</td>
                  <td style={{ padding: "8px", border: "1px solid #e2e8f0" }}>
                    <span className={`tag ${u.status === "active" ? "tag-starter" : "tag-progress"}`}>{u.status}</span>
                  </td>
                  <td style={{ padding: "8px", border: "1px solid #e2e8f0" }}>
                    {(() => {
                      const userRoles = u.roles || u.assignedRoles || [];
                      return userRoles && userRoles.length > 0 ? (
                        userRoles.map((r, idx) => {
                          const roleName = typeof r === "string" ? r : (r.name || r.roleCode || "Role");
                          const roleKey = typeof r === "string" ? `${r}-${idx}` : (r.roleCode || r.name || idx);
                          return (
                            <span key={roleKey} style={{ background: "#e2e8f0", padding: "2px 6px", borderRadius: "4px", marginRight: "4px", fontSize: "11px" }}>
                              {roleName}
                            </span>
                          );
                        })
                      ) : (
                        <span style={{ color: "#94a3b8" }}>None</span>
                      );
                    })()}
                  </td>
                  <td style={{ padding: "8px", border: "1px solid #e2e8f0" }}>
                    {u.joinedAt || u.createdAt ? new Date(u.joinedAt || u.createdAt!).toLocaleDateString() : "Pending"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── ROLES TAB ───────────────────────────────────────────── */}
      {activeTab === "roles" && (
        <div style={{ marginTop: "12px", display: "grid", gap: "12px" }}>
          {roles.map((r) => (
            <div key={r.roleCode} style={{ background: "#f8fafc", padding: "12px 16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                <h4 style={{ margin: 0 }}>{r.name} <code style={{ fontSize: "12px", color: "#64748b" }}>({r.roleCode})</code></h4>
                <span className="tag">{r.permissions.length} permissions</span>
              </div>
              <p style={{ margin: "4px 0 8px 0", fontSize: "13px", color: "#475569" }}>{r.description}</p>
              <div style={{ display: "flex", flexWrap: "wrap", gap: "4px" }}>
                {r.permissions.map((p) => (
                  <span key={p.permissionCode} title={p.description} style={{ fontSize: "11px", background: "#e2e8f0", padding: "2px 6px", borderRadius: "4px" }}>
                    {p.permissionCode}
                  </span>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* ── AUDIT TAB ───────────────────────────────────────────── */}
      {activeTab === "audit" && (
        <div style={{ marginTop: "12px" }}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "12px" }}>
            <thead>
              <tr style={{ background: "#f1f5f9", textAlign: "left" }}>
                <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Timestamp</th>
                <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Event</th>
                <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Entity</th>
                <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Actor</th>
                <th style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>Details</th>
              </tr>
            </thead>
            <tbody>
              {auditEvents.length === 0 ? (
                <tr>
                  <td colSpan={5} style={{ padding: "16px", textAlign: "center", color: "#94a3b8" }}>
                    No audit events recorded yet.
                  </td>
                </tr>
              ) : (
                auditEvents.map((ev) => (
                  <tr key={ev.eventId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                    <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0", whiteSpace: "nowrap" }}>
                      {new Date(ev.occurredAt).toLocaleString()}
                    </td>
                    <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                      <span className="tag tag-starter">{ev.eventType}</span>
                    </td>
                    <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                      {ev.entityName} {ev.entityId && <code style={{ fontSize: "10px" }}>{ev.entityId.slice(0, 8)}</code>}
                    </td>
                    <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0" }}>
                      {ev.actorEmail ?? "system"}
                    </td>
                    <td style={{ padding: "6px 8px", border: "1px solid #e2e8f0", fontFamily: "monospace", fontSize: "11px" }}>
                      {ev.details ?? "—"}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
