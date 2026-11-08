import { useState, useEffect, type FormEvent } from "react";
import { apiFetch, type TenantCompanyContext } from "./auth";

export type Account = {
  accountId: string;
  accountCode: string;
  accountName: string;
  accountType: string;
  normalBalance: string;
  currencyCode?: string | null;
  isControlAccount: boolean;
  allowManualPosting: boolean;
  isActive: boolean;
};

export type FiscalPeriod = {
  periodId: string;
  fiscalYearId: string;
  periodNo: number;
  name: string;
  startsOn: string;
  endsOn: string;
  status: string;
};

export type JournalLine = {
  lineNo: number;
  accountId: string;
  accountCode: string;
  accountName: string;
  description?: string | null;
  transactionCurrency: string;
  transactionAmount: number;
  debitFunctional: number;
  creditFunctional: number;
};

export type Journal = {
  journalId: string;
  journalNo: string;
  journalDate: string;
  fiscalPeriodId: string;
  description: string;
  functionalCurrency: string;
  sourceModule: string;
  status: string;
  postedAt?: string | null;
  totalDebit: number;
  totalCredit: number;
  lines: JournalLine[];
};

export type TrialBalanceRow = {
  accountId: string;
  accountCode: string;
  accountName: string;
  accountType: string;
  normalBalance: string;
  totalDebit: number;
  totalCredit: number;
  netBalance: number;
};

type Props = {
  enabled: boolean;
  workspace: TenantCompanyContext | null;
};

type Tab = "accounts" | "post-journal" | "journals" | "trial-balance";

type NewJournalLine = {
  accountId: string;
  description: string;
  debit: string;
  credit: string;
};

export default function FinanceWorkspace({ enabled, workspace }: Props) {
  const [activeTab, setActiveTab] = useState<Tab>("accounts");
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [periods, setPeriods] = useState<FiscalPeriod[]>([]);
  const [journals, setJournals] = useState<Journal[]>([]);
  const [trialBalance, setTrialBalance] = useState<TrialBalanceRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  // New Account form
  const [newCode, setNewCode] = useState("");
  const [newName, setNewName] = useState("");
  const [newType, setNewType] = useState("expense");
  const [newBalance, setNewBalance] = useState("debit");

  // New Journal form
  const [journalNo, setJournalNo] = useState(`JRN-${Date.now().toString().slice(-6)}`);
  const [journalDate, setJournalDate] = useState(new Date().toISOString().slice(0, 10));
  const [selectedPeriodId, setSelectedPeriodId] = useState("");
  const [journalDescription, setJournalDescription] = useState("");
  const [journalLines, setJournalLines] = useState<NewJournalLine[]>([
    { accountId: "", description: "", debit: "0", credit: "0" },
    { accountId: "", description: "", debit: "0", credit: "0" },
  ]);

  const loadData = async () => {
    if (!enabled) return;
    setLoading(true);
    setError("");
    try {
      const [accRes, perRes, jrnRes, tbRes] = await Promise.all([
        apiFetch("/api/v1/finance/accounts", {}, workspace),
        apiFetch("/api/v1/finance/periods", {}, workspace),
        apiFetch("/api/v1/finance/journals", {}, workspace),
        apiFetch("/api/v1/finance/trial-balance", {}, workspace),
      ]);

      if (accRes.ok) setAccounts((await accRes.json()) as Account[]);
      if (perRes.ok) {
        const perList = (await perRes.json()) as FiscalPeriod[];
        setPeriods(perList);
        if (perList.length > 0 && !selectedPeriodId) {
          const openPeriod = perList.find((p) => p.status === "open") ?? perList[0];
          setSelectedPeriodId(openPeriod.periodId);
        }
      }
      if (jrnRes.ok) setJournals((await jrnRes.json()) as Journal[]);
      if (tbRes.ok) setTrialBalance((await tbRes.json()) as TrialBalanceRow[]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load finance data.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadData();
  }, [enabled, workspace]);

  const handleCreateAccount = async (e: FormEvent) => {
    e.preventDefault();
    setMessage("");
    setError("");

    try {
      const res = await apiFetch("/api/v1/finance/accounts", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          accountCode: newCode.trim().toUpperCase(),
          accountName: newName.trim(),
          accountType: newType,
          normalBalance: newBalance,
          currencyCode: "PKR",
          isControlAccount: false,
          allowManualPosting: true,
        }),
      }, workspace);

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.title || `Account creation failed (HTTP ${res.status})`);
      }

      setMessage(`Account ${newCode} created successfully!`);
      setNewCode("");
      setNewName("");
      void loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error creating account.");
    }
  };

  // Journal line helpers
  const updateLine = (index: number, field: keyof NewJournalLine, value: string) => {
    setJournalLines((prev) => {
      const next = [...prev];
      next[index] = { ...next[index], [field]: value };
      return next;
    });
  };

  const addLine = () => {
    setJournalLines((prev) => [...prev, { accountId: "", description: "", debit: "0", credit: "0" }]);
  };

  const removeLine = (index: number) => {
    if (journalLines.length <= 2) return;
    setJournalLines((prev) => prev.filter((_, i) => i !== index));
  };

  const totalDebit = journalLines.reduce((sum, l) => sum + (parseFloat(l.debit) || 0), 0);
  const totalCredit = journalLines.reduce((sum, l) => sum + (parseFloat(l.credit) || 0), 0);
  const isBalanced = Math.abs(totalDebit - totalCredit) < 0.001 && totalDebit > 0;

  const handlePostJournal = async (e: FormEvent) => {
    e.preventDefault();
    if (!isBalanced) return;

    setMessage("");
    setError("");
    const idempotencyKey = crypto.randomUUID();

    try {
      const formattedLines = journalLines.map((l) => {
        const deb = parseFloat(l.debit) || 0;
        const cred = parseFloat(l.credit) || 0;
        return {
          accountId: l.accountId,
          description: l.description.trim() || journalDescription.trim(),
          transactionCurrency: "PKR",
          transactionAmount: deb > 0 ? deb : cred,
          exchangeRate: 1,
          debitFunctional: deb,
          creditFunctional: cred,
        };
      });

      const res = await apiFetch("/api/v1/finance/journals", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": idempotencyKey,
        },
        body: JSON.stringify({
          journalNo: journalNo.trim().toUpperCase(),
          journalDate,
          fiscalPeriodId: selectedPeriodId,
          description: journalDescription.trim(),
          functionalCurrency: "PKR",
          sourceModule: "manual",
          sourceType: "general",
          sourceId: idempotencyKey,
          lines: formattedLines,
        }),
      }, workspace);

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.title || `Journal posting failed (HTTP ${res.status})`);
      }

      setMessage(`Journal ${journalNo} successfully posted to the General Ledger!`);
      setJournalNo(`JRN-${Date.now().toString().slice(-6)}`);
      setJournalDescription("");
      setJournalLines([
        { accountId: "", description: "", debit: "0", credit: "0" },
        { accountId: "", description: "", debit: "0", credit: "0" },
      ]);
      void loadData();
      setActiveTab("journals");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error posting journal.");
    }
  };

  const tbTotalDebit = trialBalance.reduce((sum, r) => sum + r.totalDebit, 0);
  const tbTotalCredit = trialBalance.reduce((sum, r) => sum + r.totalCredit, 0);

  return (
    <section className="catalog-panel" aria-label="Finance & Double-Entry Accounting">
      <div className="panel-header">
        <div>
          <h2>Finance & General Ledger</h2>
          <p>
            Chart of Accounts, strict double-entry journal posting, and real-time Trial Balance verification.
          </p>
        </div>
        <div style={{ display: "flex", gap: "0.5rem" }}>
          <button
            type="button"
            className={activeTab === "accounts" ? "active-tab" : ""}
            onClick={() => setActiveTab("accounts")}
          >
            Chart of Accounts ({accounts.length})
          </button>
          <button
            type="button"
            className={activeTab === "post-journal" ? "active-tab" : ""}
            onClick={() => setActiveTab("post-journal")}
          >
            + Post Journal
          </button>
          <button
            type="button"
            className={activeTab === "journals" ? "active-tab" : ""}
            onClick={() => setActiveTab("journals")}
          >
            Journal Entries ({journals.length})
          </button>
          <button
            type="button"
            className={activeTab === "trial-balance" ? "active-tab" : ""}
            onClick={() => setActiveTab("trial-balance")}
          >
            Trial Balance
          </button>
        </div>
      </div>

      {loading && <p className="catalog-message">Loading financial ledgers…</p>}
      {message && <p className="catalog-message" style={{ color: "#166534", fontWeight: 600 }}>{message}</p>}
      {error && <p className="catalog-message" style={{ color: "#991b1b", fontWeight: 600 }}>{error}</p>}

      {/* ── 1. Chart of Accounts Tab ────────────────────────────────────── */}
      {activeTab === "accounts" && (
        <div>
          <form onSubmit={handleCreateAccount} className="inventory-form" style={{ marginBottom: "1.5rem" }}>
            <h4>Add New Ledger Account</h4>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: "0.75rem" }}>
              <label>
                Account Code
                <input
                  required
                  placeholder="e.g. 1030"
                  value={newCode}
                  onChange={(e) => setNewCode(e.target.value)}
                />
              </label>
              <label>
                Account Name
                <input
                  required
                  placeholder="e.g. Petty Cash Branch 1"
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                />
              </label>
              <label>
                Category
                <select value={newType} onChange={(e) => setNewType(e.target.value)}>
                  <option value="asset">Asset</option>
                  <option value="liability">Liability</option>
                  <option value="equity">Equity</option>
                  <option value="revenue">Revenue</option>
                  <option value="expense">Expense</option>
                </select>
              </label>
              <label>
                Normal Balance
                <select value={newBalance} onChange={(e) => setNewBalance(e.target.value)}>
                  <option value="debit">Debit</option>
                  <option value="credit">Credit</option>
                </select>
              </label>
              <div style={{ display: "flex", alignItems: "flex-end" }}>
                <button type="submit" style={{ width: "100%" }}>Create Account</button>
              </div>
            </div>
          </form>

          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem" }}>
            <thead>
              <tr style={{ background: "#f1f5f9", textAlign: "left", borderBottom: "2px solid #cbd5e1" }}>
                <th style={{ padding: "0.5rem" }}>Code</th>
                <th style={{ padding: "0.5rem" }}>Account Name</th>
                <th style={{ padding: "0.5rem" }}>Type</th>
                <th style={{ padding: "0.5rem" }}>Normal Balance</th>
                <th style={{ padding: "0.5rem" }}>Currency</th>
                <th style={{ padding: "0.5rem" }}>Control</th>
              </tr>
            </thead>
            <tbody>
              {accounts.map((a) => (
                <tr key={a.accountId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "0.5rem", fontWeight: 600 }}><code>{a.accountCode}</code></td>
                  <td style={{ padding: "0.5rem" }}>{a.accountName}</td>
                  <td style={{ padding: "0.5rem" }}>
                    <span className="tag" style={{ textTransform: "capitalize" }}>{a.accountType}</span>
                  </td>
                  <td style={{ padding: "0.5rem", textTransform: "uppercase" }}>{a.normalBalance}</td>
                  <td style={{ padding: "0.5rem" }}>{a.currencyCode ?? "PKR"}</td>
                  <td style={{ padding: "0.5rem" }}>{a.isControlAccount ? "Control" : "General"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── 2. Post Journal Tab ─────────────────────────────────────────── */}
      {activeTab === "post-journal" && (
        <form onSubmit={handlePostJournal} className="inventory-form">
          <h3>Record Double-Entry Journal</h3>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: "1rem", marginBottom: "1rem" }}>
            <label>
              Journal #
              <input
                required
                value={journalNo}
                onChange={(e) => setJournalNo(e.target.value)}
              />
            </label>
            <label>
              Date
              <input
                type="date"
                required
                value={journalDate}
                onChange={(e) => setJournalDate(e.target.value)}
              />
            </label>
            <label>
              Fiscal Period
              <select
                required
                value={selectedPeriodId}
                onChange={(e) => setSelectedPeriodId(e.target.value)}
              >
                {periods.map((p) => (
                  <option key={p.periodId} value={p.periodId}>
                    {p.name} ({p.status})
                  </option>
                ))}
              </select>
            </label>
          </div>

          <label style={{ display: "block", marginBottom: "1rem" }}>
            Description / Memo
            <input
              required
              placeholder="e.g. November office rent and utilities payment via bank transfer"
              value={journalDescription}
              onChange={(e) => setJournalDescription(e.target.value)}
            />
          </label>

          <h4>Journal Lines (Double-Entry Balanced)</h4>
          <table style={{ width: "100%", borderCollapse: "collapse", marginBottom: "1rem", fontSize: "0.875rem" }}>
            <thead>
              <tr style={{ background: "#f8fafc", textAlign: "left", borderBottom: "2px solid #cbd5e1" }}>
                <th style={{ padding: "0.5rem", width: "35%" }}>Account</th>
                <th style={{ padding: "0.5rem", width: "35%" }}>Line Memo</th>
                <th style={{ padding: "0.5rem", width: "15%" }}>Debit (PKR)</th>
                <th style={{ padding: "0.5rem", width: "15%" }}>Credit (PKR)</th>
                <th style={{ padding: "0.5rem" }}></th>
              </tr>
            </thead>
            <tbody>
              {journalLines.map((line, idx) => (
                <tr key={idx} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "0.5rem" }}>
                    <select
                      required
                      value={line.accountId}
                      onChange={(e) => updateLine(idx, "accountId", e.target.value)}
                      style={{ width: "100%" }}
                    >
                      <option value="">Select Account…</option>
                      {accounts.map((a) => (
                        <option key={a.accountId} value={a.accountId}>
                          {a.accountCode} - {a.accountName}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td style={{ padding: "0.5rem" }}>
                    <input
                      placeholder="Line description"
                      value={line.description}
                      onChange={(e) => updateLine(idx, "description", e.target.value)}
                      style={{ width: "100%" }}
                    />
                  </td>
                  <td style={{ padding: "0.5rem" }}>
                    <input
                      type="number"
                      step="0.01"
                      min="0"
                      value={line.debit}
                      onChange={(e) => updateLine(idx, "debit", e.target.value)}
                      style={{ width: "100%" }}
                    />
                  </td>
                  <td style={{ padding: "0.5rem" }}>
                    <input
                      type="number"
                      step="0.01"
                      min="0"
                      value={line.credit}
                      onChange={(e) => updateLine(idx, "credit", e.target.value)}
                      style={{ width: "100%" }}
                    />
                  </td>
                  <td style={{ padding: "0.5rem" }}>
                    {journalLines.length > 2 && (
                      <button
                        type="button"
                        onClick={() => removeLine(idx)}
                        style={{ background: "#fee2e2", color: "#991b1b", border: "none", padding: "0.25rem 0.5rem" }}
                      >
                        ✕
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr style={{ background: isBalanced ? "#f0fdf4" : "#fef2f2", fontWeight: 700 }}>
                <td style={{ padding: "0.5rem" }}>Totals</td>
                <td style={{ padding: "0.5rem" }}>
                  {isBalanced ? (
                    <span style={{ color: "#166534" }}>✓ Balanced</span>
                  ) : (
                    <span style={{ color: "#991b1b" }}>
                      ✕ Unbalanced (Diff: PKR {Math.abs(totalDebit - totalCredit).toLocaleString()})
                    </span>
                  )}
                </td>
                <td style={{ padding: "0.5rem" }}>PKR {totalDebit.toLocaleString(undefined, { minimumFractionDigits: 2 })}</td>
                <td style={{ padding: "0.5rem" }}>PKR {totalCredit.toLocaleString(undefined, { minimumFractionDigits: 2 })}</td>
                <td></td>
              </tr>
            </tfoot>
          </table>

          <div style={{ display: "flex", justifyContent: "space-between" }}>
            <button type="button" onClick={addLine}>+ Add Line</button>
            <button type="submit" disabled={!isBalanced || totalDebit === 0}>
              Commit & Post to General Ledger
            </button>
          </div>
        </form>
      )}

      {/* ── 3. Journal Entries Tab ──────────────────────────────────────── */}
      {activeTab === "journals" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
          {journals.length === 0 ? (
            <p className="catalog-empty">No journal entries recorded yet.</p>
          ) : (
            journals.map((j) => (
              <article
                key={j.journalId}
                style={{
                  border: "1px solid #cbd5e1",
                  borderRadius: "8px",
                  padding: "1rem",
                  background: "#ffffff",
                }}
              >
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
                  <div>
                    <h3 style={{ margin: "0 0 0.25rem" }}>
                      <code>{j.journalNo}</code> · {j.description}
                    </h3>
                    <span style={{ fontSize: "0.8rem", color: "#64748b" }}>
                      Date: {j.journalDate} · Functional Currency: {j.functionalCurrency} · Status: <strong>{j.status.toUpperCase()}</strong>
                    </span>
                  </div>
                  <div style={{ textAlign: "right" }}>
                    <span style={{ fontWeight: 700, fontSize: "1rem", color: "#0f172a" }}>
                      PKR {j.totalDebit.toLocaleString(undefined, { minimumFractionDigits: 2 })}
                    </span>
                  </div>
                </div>
              </article>
            ))
          )}
        </div>
      )}

      {/* ── 4. Trial Balance Tab ────────────────────────────────────────── */}
      {activeTab === "trial-balance" && (
        <div>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline", marginBottom: "1rem" }}>
            <h3>General Ledger Trial Balance</h3>
            <span style={{ fontSize: "0.875rem", color: "#64748b" }}>
              As of {new Date().toLocaleDateString()}
            </span>
          </div>

          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem" }}>
            <thead>
              <tr style={{ background: "#f1f5f9", textAlign: "left", borderBottom: "2px solid #cbd5e1" }}>
                <th style={{ padding: "0.5rem" }}>Account Code</th>
                <th style={{ padding: "0.5rem" }}>Account Name</th>
                <th style={{ padding: "0.5rem" }}>Classification</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Total Debit (PKR)</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Total Credit (PKR)</th>
                <th style={{ padding: "0.5rem", textAlign: "right" }}>Net Balance (PKR)</th>
              </tr>
            </thead>
            <tbody>
              {trialBalance.map((r) => (
                <tr key={r.accountId} style={{ borderBottom: "1px solid #e2e8f0" }}>
                  <td style={{ padding: "0.5rem", fontWeight: 600 }}><code>{r.accountCode}</code></td>
                  <td style={{ padding: "0.5rem" }}>{r.accountName}</td>
                  <td style={{ padding: "0.5rem", textTransform: "capitalize" }}>{r.accountType}</td>
                  <td style={{ padding: "0.5rem", textAlign: "right" }}>
                    {r.totalDebit > 0 ? r.totalDebit.toLocaleString(undefined, { minimumFractionDigits: 2 }) : "—"}
                  </td>
                  <td style={{ padding: "0.5rem", textAlign: "right" }}>
                    {r.totalCredit > 0 ? r.totalCredit.toLocaleString(undefined, { minimumFractionDigits: 2 }) : "—"}
                  </td>
                  <td style={{ padding: "0.5rem", textAlign: "right", fontWeight: 600 }}>
                    {r.netBalance.toLocaleString(undefined, { minimumFractionDigits: 2 })}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr style={{ background: "#f8fafc", fontWeight: 700, borderTop: "2px solid #0f172a" }}>
                <td colSpan={3} style={{ padding: "0.5rem" }}>Grand Totals (Balanced Ledger Proof)</td>
                <td style={{ padding: "0.5rem", textAlign: "right" }}>
                  PKR {tbTotalDebit.toLocaleString(undefined, { minimumFractionDigits: 2 })}
                </td>
                <td style={{ padding: "0.5rem", textAlign: "right" }}>
                  PKR {tbTotalCredit.toLocaleString(undefined, { minimumFractionDigits: 2 })}
                </td>
                <td style={{ padding: "0.5rem", textAlign: "right", color: Math.abs(tbTotalDebit - tbTotalCredit) < 0.001 ? "#166534" : "#991b1b" }}>
                  {Math.abs(tbTotalDebit - tbTotalCredit) < 0.001 ? "✓ MATCHED" : "✕ OUT OF BALANCE"}
                </td>
              </tr>
            </tfoot>
          </table>
        </div>
      )}
    </section>
  );
}
