import { useState, useEffect, type FormEvent } from "react";
import { apiFetch } from "./auth";

export type SubscriptionPlan = {
  planId: string;
  planCode: string;
  name: string;
  description?: string;
  status: string;
  features: {
    featureCode: string;
    enabled: boolean;
    quantityLimit?: number | null;
    limitUnit?: string | null;
  }[];
};

export type RegisteredTenant = {
  tenantId: string;
  tenantCode: string;
  displayName: string;
  legalName?: string | null;
  status: string;
  defaultCountryCode: string;
  defaultCurrencyCode: string;
  defaultTimezone: string;
  createdAt: string;
  company: {
    companyId: string;
    companyCode: string;
    name: string;
    countryCode: string;
    baseCurrencyCode: string;
    timezone: string;
    status: string;
  };
  subscription: {
    subscriptionId: string;
    planCode: string;
    planName: string;
    status: string;
    startsAt: string;
    currentPeriodEnd: string;
  };
  entitlements: {
    featureCode: string;
    enabled: boolean;
    quantityLimit?: number | null;
    limitUnit?: string | null;
  }[];
  adminUser: {
    userId: string;
    email: string;
    displayName: string;
    status: string;
  };
};

export default function TenancyWorkspace() {
  const [plans, setPlans] = useState<SubscriptionPlan[]>([]);
  const [plansLoading, setPlansLoading] = useState(false);
  const [plansError, setPlansError] = useState("");

  const [tenantCode, setTenantCode] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [legalName, setLegalName] = useState("");
  const [selectedPlan, setSelectedPlan] = useState("starter");
  const [adminFullName, setAdminFullName] = useState("");
  const [adminEmail, setAdminEmail] = useState("");
  const [adminPhone, setAdminPhone] = useState("");

  const [isRegistering, setIsRegistering] = useState(false);
  const [registrationMessage, setRegistrationMessage] = useState("");
  const [registrationError, setRegistrationError] = useState("");
  const [provisionedTenant, setProvisionedTenant] = useState<RegisteredTenant | null>(null);

  useEffect(() => {
    let isMounted = true;
    const fetchPlans = async () => {
      setPlansLoading(true);
      setPlansError("");
      try {
        const response = await apiFetch("/api/v1/plans", {}, undefined, false);
        if (!response.ok) {
          throw new Error(`Failed to load subscription plans: HTTP ${response.status}`);
        }
        const data = (await response.json()) as SubscriptionPlan[];
        if (isMounted) {
          setPlans(data);
          if (data.length > 0 && !data.some((p) => p.planCode === selectedPlan)) {
            setSelectedPlan(data[0].planCode);
          }
        }
      } catch (err) {
        if (isMounted) {
          setPlansError(err instanceof Error ? err.message : "Could not connect to plans service.");
        }
      } finally {
        if (isMounted) setPlansLoading(false);
      }
    };

    void fetchPlans();
    return () => {
      isMounted = false;
    };
  }, []);

  const handleRegister = async (e: FormEvent) => {
    e.preventDefault();
    setIsRegistering(true);
    setRegistrationMessage("");
    setRegistrationError("");

    const idempotencyKey = crypto.randomUUID();

    try {
      const response = await apiFetch("/api/v1/tenants/register", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": idempotencyKey,
        },
        body: JSON.stringify({
          tenantCode: tenantCode.trim().toLowerCase(),
          displayName: displayName.trim(),
          legalName: legalName.trim() || null,
          planCode: selectedPlan,
          adminFullName: adminFullName.trim(),
          adminEmail: adminEmail.trim().toLowerCase(),
          adminPhone: adminPhone.trim() || null,
          countryCode: "PK",
          baseCurrencyCode: "PKR",
          timezone: "Asia/Karachi",
        }),
      }, undefined, false);

      if (!response.ok) {
        const problem = await response.json().catch(() => ({}));
        throw new Error(problem.detail || problem.title || `Registration failed (HTTP ${response.status})`);
      }

      const tenant = (await response.json()) as RegisteredTenant;
      setProvisionedTenant(tenant);
      setRegistrationMessage(
        `Tenant "${tenant.displayName}" (${tenant.tenantCode}) successfully provisioned under the ${tenant.subscription.planName}!`,
      );
      setTenantCode("");
      setDisplayName("");
      setLegalName("");
      setAdminFullName("");
      setAdminEmail("");
      setAdminPhone("");
    } catch (err) {
      setRegistrationError(err instanceof Error ? err.message : "Tenant registration failed.");
    } finally {
      setIsRegistering(false);
    }
  };

  return (
    <section className="catalog-panel" aria-label="SaaS Tenant Onboarding & Plans">
      <div className="panel-header">
        <div>
          <h2>SaaS Platform & Tenant Onboarding</h2>
          <p>
            Multi-company legal entity registration, subscription entitlement snapshots, and secure tenant provisioning.
          </p>
        </div>
      </div>

      {plansLoading && <p className="catalog-message">Loading available subscription plans…</p>}
      {plansError && <p className="catalog-message" style={{ color: "var(--color-danger, #d9534f)" }}>{plansError}</p>}

      {plans.length > 0 && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: "1rem", marginBottom: "1.5rem" }}>
          {plans.map((plan) => (
            <article
              key={plan.planId}
              style={{
                border: selectedPlan === plan.planCode ? "2px solid #2563eb" : "1px solid #e2e8f0",
                borderRadius: "8px",
                padding: "1rem",
                background: selectedPlan === plan.planCode ? "#eff6ff" : "white",
                cursor: "pointer",
              }}
              onClick={() => setSelectedPlan(plan.planCode)}
            >
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
                <h3 style={{ margin: "0 0 0.5rem" }}>{plan.name}</h3>
                <span className="tag" style={{ textTransform: "uppercase", fontSize: "0.75rem" }}>{plan.planCode}</span>
              </div>
              <p style={{ margin: "0 0 0.75rem", fontSize: "0.875rem", color: "#475569" }}>{plan.description}</p>
              <div style={{ fontSize: "0.8rem", borderTop: "1px solid #cbd5e1", paddingTop: "0.5rem" }}>
                <strong>Features & Entitlements:</strong>
                <ul style={{ margin: "0.25rem 0 0", paddingLeft: "1.2rem", color: "#334155" }}>
                  {plan.features.map((f) => (
                    <li key={f.featureCode}>
                      {f.featureCode}
                      {f.quantityLimit ? `: ${f.quantityLimit} ${f.limitUnit ?? ""}` : ""}
                    </li>
                  ))}
                </ul>
              </div>
            </article>
          ))}
        </div>
      )}

      <form onSubmit={handleRegister} className="inventory-form" style={{ maxWidth: "800px" }}>
        <h3>Provision New Tenant Entity</h3>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "1rem" }}>
          <label>
            Tenant Code (unique slug)
            <input
              required
              placeholder="e.g. apex-logistics"
              pattern="^[a-zA-Z0-9][a-zA-Z0-9-]{1,48}[a-zA-Z0-9]$"
              title="3-50 alphanumeric characters or hyphens"
              value={tenantCode}
              onChange={(e) => setTenantCode(e.target.value)}
            />
          </label>
          <label>
            Selected Plan
            <select value={selectedPlan} onChange={(e) => setSelectedPlan(e.target.value)}>
              {plans.length > 0 ? (
                plans.map((p) => (
                  <option key={p.planCode} value={p.planCode}>
                    {p.name} ({p.planCode})
                  </option>
                ))
              ) : (
                <>
                  <option value="starter">Starter Plan</option>
                  <option value="professional">Professional Plan</option>
                  <option value="enterprise">Enterprise Plan</option>
                </>
              )}
            </select>
          </label>
          <label>
            Trading / Display Name
            <input
              required
              placeholder="e.g. Apex Logistics"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
            />
          </label>
          <label>
            Legal Registered Name (optional)
            <input
              placeholder="e.g. Apex Logistics (Pvt) Limited"
              value={legalName}
              onChange={(e) => setLegalName(e.target.value)}
            />
          </label>
          <label>
            Administrator Full Name
            <input
              required
              placeholder="e.g. Zubair Khan"
              value={adminFullName}
              onChange={(e) => setAdminFullName(e.target.value)}
            />
          </label>
          <label>
            Administrator Email
            <input
              required
              type="email"
              placeholder="e.g. zubair@apexlogistics.pk"
              value={adminEmail}
              onChange={(e) => setAdminEmail(e.target.value)}
            />
          </label>
          <label>
            Contact Phone (optional)
            <input
              placeholder="e.g. +923001234567"
              value={adminPhone}
              onChange={(e) => setAdminPhone(e.target.value)}
            />
          </label>
          <div style={{ display: "flex", flexDirection: "column", justifyContent: "flex-end" }}>
            <span style={{ fontSize: "0.8rem", color: "#64748b", marginBottom: "0.5rem" }}>
              Currency: <strong>PKR</strong> · Country: <strong>PK</strong> · Timezone: <strong>Asia/Karachi</strong>
            </span>
            <button type="submit" disabled={isRegistering} style={{ padding: "0.6rem 1.2rem" }}>
              {isRegistering ? "Provisioning Tenant & Company…" : "Register & Provision Tenant"}
            </button>
          </div>
        </div>
      </form>

      {registrationMessage && (
        <p className="catalog-message" style={{ color: "#166534", fontWeight: 600 }}>
          {registrationMessage}
        </p>
      )}
      {registrationError && (
        <p className="catalog-message" style={{ color: "#991b1b", fontWeight: 600 }}>
          {registrationError}
        </p>
      )}

      {provisionedTenant && (
        <div
          style={{
            marginTop: "1.5rem",
            padding: "1rem",
            background: "#f8fafc",
            border: "1px solid #cbd5e1",
            borderRadius: "6px",
          }}
        >
          <h4 style={{ margin: "0 0 0.5rem" }}>Provisioned Tenant Details</h4>
          <p style={{ margin: "0.25rem 0", fontSize: "0.875rem" }}>
            <strong>Tenant ID:</strong> <code>{provisionedTenant.tenantId}</code>
          </p>
          <p style={{ margin: "0.25rem 0", fontSize: "0.875rem" }}>
            <strong>Company:</strong> {provisionedTenant.company.name} (Code: <code>{provisionedTenant.company.companyCode}</code>, Base Currency: {provisionedTenant.company.baseCurrencyCode})
          </p>
          <p style={{ margin: "0.25rem 0", fontSize: "0.875rem" }}>
            <strong>Subscription:</strong> {provisionedTenant.subscription.planName} (Status: {provisionedTenant.subscription.status}, Period End: {new Date(provisionedTenant.subscription.currentPeriodEnd).toLocaleDateString()})
          </p>
          <p style={{ margin: "0.25rem 0", fontSize: "0.875rem" }}>
            <strong>Administrator:</strong> {provisionedTenant.adminUser.displayName} ({provisionedTenant.adminUser.email})
          </p>
          <p style={{ margin: "0.25rem 0", fontSize: "0.875rem" }}>
            <strong>Snapshot Entitlements:</strong> {provisionedTenant.entitlements.map((e) => e.featureCode).join(", ")}
          </p>
        </div>
      )}
    </section>
  );
}
