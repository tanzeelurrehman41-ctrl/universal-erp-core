import { useState, useEffect, type FormEvent } from "react";
import { apiFetch, type TenantCompanyContext } from "./auth";

type Props = {
  enabled: boolean;
  workspace: TenantCompanyContext | null;
};

type UserProfile = {
  userId: string;
  email: string;
  displayName: string;
  tenantId: string;
  companyId: string;
  roles: string[];
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
  assignedRoles: string[];
  invitedAt?: string | null;
  joinedAt?: string | null;
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

export default function FoundationWorkspace({ enabled, workspace }: Props) {
  const [activeTab, setActiveTab] = useState<"profile" | "settings" | "users" | "roles" | "audit">("profile");
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [settings, setSettings] = useState<CompanySettings | null>(null);
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

  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteName, setInviteName] = useState("");
  const [inviteRole, setInviteRole] = useState("company_user");

  useEffect(() => {
    if (!enabled || !workspace) return;
    loadAllData();
  }, [enabled, workspace?.tenantId, workspace?.companyId]);

  const loadAllData = async () => {
    if (!workspace) return;
    setIsLoading(true);
    setErrorMessage("");
    try {
      const [profRes, setRes, usrRes, rolRes, audRes] = await Promise.all([
        apiFetch("/api/v1/foundation/profile", {}, workspace).catch(() => null),
        apiFetch("/api/v1/foundation/settings", {}, workspace).catch(() => null),
        apiFetch("/api/v1/foundation/users", {}, workspace).catch(() => null),
        apiFetch("/api/v1/foundation/roles", {}, workspace).catch(() => null),
        apiFetch("/api/v1/foundation/audit-events?limit=30", {}, workspace).catch(() => null),
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
                profile.roles.map((r) => (
                  <span key={r} className="tag tag-starter" style={{ fontSize: "12px" }}>{r}</span>
                ))
              ) : (
                <span style={{ color: "#64748b" }}>Standard Member</span>
              )}
            </div>
            <p><strong>Effective Permissions ({profile?.permissions?.length ?? 0}):</strong></p>
            <div style={{ maxHeight: "150px", overflowY: "auto", display: "flex", flexWrap: "wrap", gap: "4px" }}>
              {profile?.permissions?.map((p) => (
                <span key={p} style={{ fontSize: "11px", background: "#e2e8f0", padding: "2px 6px", borderRadius: "4px" }}>
                  {p}
                </span>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* ── SETTINGS TAB ────────────────────────────────────────── */}
      {activeTab === "settings" && (
        <form onSubmit={handleUpdateSettings} style={{ maxWidth: "600px", marginTop: "12px" }}>
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
                <label style={{ display: "block", fontWeight: 600, marginBottom: "4px" }}>Base Functional Currency</label>
                <input value={settings?.baseCurrencyCode ?? "PKR"} disabled style={{ width: "100%", padding: "8px", background: "#f1f5f9" }} />
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
                    {u.assignedRoles && u.assignedRoles.length > 0 ? (
                      u.assignedRoles.map((r) => (
                        <span key={r} style={{ background: "#e2e8f0", padding: "2px 6px", borderRadius: "4px", marginRight: "4px", fontSize: "11px" }}>
                          {r}
                        </span>
                      ))
                    ) : (
                      <span style={{ color: "#94a3b8" }}>None</span>
                    )}
                  </td>
                  <td style={{ padding: "8px", border: "1px solid #e2e8f0" }}>
                    {u.joinedAt ? new Date(u.joinedAt).toLocaleDateString() : "Pending"}
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
