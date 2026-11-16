import React, { useState, useEffect } from 'react';
import {
  buildBranchTree,
  formatBranchType,
  findBranchPath,
  validateBranchCode,
  type BranchDto,
  type BranchTreeNode
} from './branchHelpers.ts';
import {
  calculateReciprocalElimination,
  calculateUnrealizedProfit,
  calculateEliminationJournalBalance,
  formatTransferStatus,
  formatEliminationType,
  calculateConsolidatedBalance,
  type EliminationLineDto
} from './consolidationHelpers.ts';
import {
  calculateNextBackoffDelaySeconds,
  formatDeliveryStatus,
  validateWebhookUrl,
  maskSecret
} from './integrationHelpers.ts';

interface ConsolidationWorkspaceProps {
  tenantId: string;
  companyId: string;
  userId: string;
}

export const ConsolidationWorkspace: React.FC<ConsolidationWorkspaceProps> = ({
  tenantId,
  companyId,
  userId
}) => {
  const [activeTab, setActiveTab] = useState<'branches' | 'transfers' | 'consolidation' | 'integrations'>('branches');

  // Branch state
  const [branches, setBranches] = useState<BranchDto[]>([]);
  const [selectedBranchId, setSelectedBranchId] = useState<string>('');
  const [branchCode, setBranchCode] = useState('');
  const [branchName, setBranchName] = useState('');
  const [branchType, setBranchType] = useState('warehouse_hub');
  const [parentBranchId, setParentBranchId] = useState('');
  const [city, setCity] = useState('Karachi');
  const [province, setProvince] = useState('Sindh');
  const [branchMessage, setBranchMessage] = useState('');

  // Intercompany Transfer state
  const [transfers, setTransfers] = useState<any[]>([]);
  const [transferNumber, setTransferNumber] = useState(`ICT-${Date.now().toString().slice(-6)}`);
  const [toCompanyId, setToCompanyId] = useState('');
  const [transferQty, setTransferQty] = useState(10);
  const [unitCost, setUnitCost] = useState(100);
  const [transferPrice, setTransferPrice] = useState(120);
  const [transferMessage, setTransferMessage] = useState('');

  // Consolidation Run state
  const [runs, setRuns] = useState<any[]>([]);
  const [runCode, setRunCode] = useState(`CONS-2026-Q1`);
  const [selectedRun, setSelectedRun] = useState<any>(null);
  const [statementType, setStatementType] = useState<'balance_sheet' | 'income_statement'>('balance_sheet');
  const [statement, setStatement] = useState<any>(null);
  const [consolidationMessage, setConsolidationMessage] = useState('');

  // Integrations state
  const [webhooks, setWebhooks] = useState<any[]>([]);
  const [deliveries, setDeliveries] = useState<any[]>([]);
  const [partners, setPartners] = useState<any[]>([]);
  const [webhookUrl, setWebhookUrl] = useState('https://api.partner-logistics.com/v1/webhook');
  const [webhookEventType, setWebhookEventType] = useState('intercompany.transfer.dispatched');
  const [partnerName, setPartnerName] = useState('');
  const [createdSecret, setCreatedSecret] = useState<string | null>(null);
  const [createdApiKey, setCreatedApiKey] = useState<string | null>(null);
  const [integrationMessage, setIntegrationMessage] = useState('');

  // Load initial data
  useEffect(() => {
    loadBranches();
    loadTransfers();
    loadRuns();
    loadIntegrations();
  }, [tenantId, companyId]);

  const loadBranches = async () => {
    try {
      const res = await fetch('/api/v1/branches', {
        headers: { 'X-Tenant-Id': tenantId, 'X-Company-Id': companyId }
      });
      if (res.ok) {
        const data = await res.json();
        setBranches(data);
        if (data.length > 0 && !selectedBranchId) {
          setSelectedBranchId(data[0].id);
        }
      }
    } catch {
      // Offline fallback
    }
  };

  const handleCreateBranch = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!validateBranchCode(branchCode)) {
      setBranchMessage('Error: Branch code must be 2-20 alphanumeric characters.');
      return;
    }

    try {
      const res = await fetch('/api/v1/branches', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Tenant-Id': tenantId,
          'X-Company-Id': companyId
        },
        body: JSON.stringify({
          branchCode,
          branchName,
          branchType,
          parentBranchId: parentBranchId || null,
          city,
          provinceOrState: province,
          countryCode: 'PK'
        })
      });

      if (res.ok) {
        setBranchMessage(`Branch ${branchCode} created successfully.`);
        setBranchCode('');
        setBranchName('');
        await loadBranches();
      } else {
        const err = await res.json();
        setBranchMessage(`Failed to create branch: ${err.detail || err.title || 'Error'}`);
      }
    } catch (err: any) {
      setBranchMessage(`Network error: ${err.message}`);
    }
  };

  const loadTransfers = async () => {
    try {
      const res = await fetch('/api/v1/intercompany/transfers', {
        headers: { 'X-Tenant-Id': tenantId, 'X-Company-Id': companyId }
      });
      if (res.ok) {
        setTransfers(await res.json());
      }
    } catch {
      // Fallback
    }
  };

  const handleDispatch = async (transferId: string) => {
    try {
      const res = await fetch(`/api/v1/intercompany/transfers/${transferId}/dispatch`, {
        method: 'POST',
        headers: { 'X-Tenant-Id': tenantId, 'X-Company-Id': companyId, 'X-User-Id': userId }
      });
      if (res.ok) {
        setTransferMessage(`Transfer dispatched successfully. Sender GL Journal posted.`);
        await loadTransfers();
      } else {
        const err = await res.json();
        setTransferMessage(`Dispatch error: ${err.detail || 'Failed'}`);
      }
    } catch (err: any) {
      setTransferMessage(`Error: ${err.message}`);
    }
  };

  const handleReceive = async (transferId: string) => {
    try {
      const res = await fetch(`/api/v1/intercompany/transfers/${transferId}/receive`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Tenant-Id': tenantId,
          'X-Company-Id': companyId,
          'X-User-Id': userId
        },
        body: JSON.stringify({
          receiptDate: new Date().toISOString().split('T')[0]
        })
      });
      if (res.ok) {
        setTransferMessage(`Transfer received. Receiver GL Journal posted and FIFO layers created.`);
        await loadTransfers();
      } else {
        const err = await res.json();
        setTransferMessage(`Receipt error: ${err.detail || 'Failed'}`);
      }
    } catch (err: any) {
      setTransferMessage(`Error: ${err.message}`);
    }
  };

  const loadRuns = async () => {
    try {
      const res = await fetch('/api/v1/consolidations/runs', {
        headers: { 'X-Tenant-Id': tenantId }
      });
      if (res.ok) {
        setRuns(await res.json());
      }
    } catch {
      // Fallback
    }
  };

  const handleSelectRun = async (run: any) => {
    setSelectedRun(run);
    try {
      const res = await fetch(`/api/v1/consolidations/runs/${run.id}/statements?type=${statementType}`, {
        headers: { 'X-Tenant-Id': tenantId }
      });
      if (res.ok) {
        setStatement(await res.json());
      }
    } catch {
      // Fallback
    }
  };

  const loadIntegrations = async () => {
    try {
      const [whRes, delRes, partRes] = await Promise.all([
        fetch('/api/v1/integrations/webhooks', { headers: { 'X-Tenant-Id': tenantId, 'X-Company-Id': companyId } }),
        fetch('/api/v1/integrations/deliveries', { headers: { 'X-Tenant-Id': tenantId } }),
        fetch('/api/v1/integrations/partners', { headers: { 'X-Tenant-Id': tenantId, 'X-Company-Id': companyId } })
      ]);

      if (whRes.ok) setWebhooks(await whRes.json());
      if (delRes.ok) setDeliveries(await delRes.json());
      if (partRes.ok) setPartners(await partRes.json());
    } catch {
      // Fallback
    }
  };

  const handleCreateWebhook = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!validateWebhookUrl(webhookUrl)) {
      setIntegrationMessage('Invalid Webhook URL. Must be an absolute HTTP or HTTPS URL.');
      return;
    }

    try {
      const res = await fetch('/api/v1/integrations/webhooks', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Tenant-Id': tenantId,
          'X-Company-Id': companyId
        },
        body: JSON.stringify({
          eventType: webhookEventType,
          targetUrl: webhookUrl
        })
      });

      if (res.ok) {
        const result = await res.json();
        setCreatedSecret(result.plaintextSecret);
        setIntegrationMessage('Webhook created. Store the cryptographic signing secret safely!');
        await loadIntegrations();
      }
    } catch (err: any) {
      setIntegrationMessage(`Error: ${err.message}`);
    }
  };

  const branchTree = buildBranchTree(branches);

  const renderBranchTreeNode = (node: BranchTreeNode) => (
    <li key={node.branch.id} style={{ marginLeft: '1.5rem', marginTop: '0.5rem' }}>
      <strong>{node.branch.branchCode}</strong> — {node.branch.branchName} ({formatBranchType(node.branch.branchType)})
      <span style={{ marginLeft: '0.5rem', color: '#666', fontSize: '0.85rem' }}>
        [{node.branch.city}, {node.branch.provinceOrState}]
      </span>
      {node.children.length > 0 && (
        <ul style={{ listStyleType: 'circle', paddingLeft: '1rem' }}>
          {node.children.map(renderBranchTreeNode)}
        </ul>
      )}
    </li>
  );

  return (
    <div className="consolidation-workspace" style={{ padding: '1.5rem', maxWidth: '1200px', margin: '0 auto' }}>
      <header style={{ marginBottom: '1.5rem', borderBottom: '1px solid #e2e8f0', paddingBottom: '1rem' }}>
        <h1 style={{ fontSize: '1.8rem', fontWeight: 600, color: '#1e293b' }}>
          Multi-Branch Operations, Intercompany Consolidation & Integrations Hub
        </h1>
        <p style={{ color: '#64748b', fontSize: '0.95rem' }}>
          Phase 13 enterprise operating architecture, atomic stock transfers, reciprocal elimination, and webhook event streaming.
        </p>

        {/* Navigation Tabs */}
        <div style={{ display: 'flex', gap: '0.75rem', marginTop: '1rem' }}>
          <button
            onClick={() => setActiveTab('branches')}
            style={{
              padding: '0.5rem 1rem',
              borderRadius: '6px',
              border: 'none',
              background: activeTab === 'branches' ? '#2563eb' : '#f1f5f9',
              color: activeTab === 'branches' ? '#fff' : '#334155',
              cursor: 'pointer',
              fontWeight: 500
            }}
          >
            Branch Management
          </button>
          <button
            onClick={() => setActiveTab('transfers')}
            style={{
              padding: '0.5rem 1rem',
              borderRadius: '6px',
              border: 'none',
              background: activeTab === 'transfers' ? '#2563eb' : '#f1f5f9',
              color: activeTab === 'transfers' ? '#fff' : '#334155',
              cursor: 'pointer',
              fontWeight: 500
            }}
          >
            Intercompany Transfers
          </button>
          <button
            onClick={() => setActiveTab('consolidation')}
            style={{
              padding: '0.5rem 1rem',
              borderRadius: '6px',
              border: 'none',
              background: activeTab === 'consolidation' ? '#2563eb' : '#f1f5f9',
              color: activeTab === 'consolidation' ? '#fff' : '#334155',
              cursor: 'pointer',
              fontWeight: 500
            }}
          >
            Group Financial Consolidation
          </button>
          <button
            onClick={() => setActiveTab('integrations')}
            style={{
              padding: '0.5rem 1rem',
              borderRadius: '6px',
              border: 'none',
              background: activeTab === 'integrations' ? '#2563eb' : '#f1f5f9',
              color: activeTab === 'integrations' ? '#fff' : '#334155',
              cursor: 'pointer',
              fontWeight: 500
            }}
          >
            External Integrations Hub
          </button>
        </div>
      </header>

      {/* Tab 1: Multi-Branch Operations */}
      {activeTab === 'branches' && (
        <section>
          <h2>Branch Hierarchy & Operating Locations</h2>
          {branchMessage && (
            <div style={{ padding: '0.75rem', marginBottom: '1rem', background: '#eff6ff', borderRadius: '4px', color: '#1d4ed8' }}>
              {branchMessage}
            </div>
          )}

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1.5rem' }}>
            <div style={{ background: '#fff', border: '1px solid #cbd5e1', borderRadius: '8px', padding: '1rem' }}>
              <h3>Operating Branch Hierarchy</h3>
              {branchTree.length === 0 ? (
                <p style={{ color: '#94a3b8' }}>No branches defined yet. Create your first branch on the right.</p>
              ) : (
                <ul style={{ listStyleType: 'disc', paddingLeft: '1rem' }}>
                  {branchTree.map(renderBranchTreeNode)}
                </ul>
              )}
            </div>

            <div style={{ background: '#fff', border: '1px solid #cbd5e1', borderRadius: '8px', padding: '1rem' }}>
              <h3>Create Operating Branch</h3>
              <form onSubmit={handleCreateBranch} style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                <div>
                  <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 500 }}>Branch Code</label>
                  <input
                    type="text"
                    value={branchCode}
                    onChange={e => setBranchCode(e.target.value)}
                    placeholder="e.g. BR-PLANT-01"
                    required
                    style={{ width: '100%', padding: '0.5rem', borderRadius: '4px', border: '1px solid #cbd5e1' }}
                  />
                </div>
                <div>
                  <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 500 }}>Branch Name</label>
                  <input
                    type="text"
                    value={branchName}
                    onChange={e => setBranchName(e.target.value)}
                    placeholder="e.g. Port Qasim Processing Plant"
                    required
                    style={{ width: '100%', padding: '0.5rem', borderRadius: '4px', border: '1px solid #cbd5e1' }}
                  />
                </div>
                <div>
                  <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 500 }}>Branch Type</label>
                  <select
                    value={branchType}
                    onChange={e => setBranchType(e.target.value)}
                    style={{ width: '100%', padding: '0.5rem', borderRadius: '4px', border: '1px solid #cbd5e1' }}
                  >
                    <option value="headquarters">Headquarters (HQ)</option>
                    <option value="plant">Manufacturing Plant</option>
                    <option value="warehouse_hub">Warehouse Hub / Depot</option>
                    <option value="retail_store">Retail Store (POS)</option>
                    <option value="sales_office">Regional Sales Office</option>
                  </select>
                </div>
                <div>
                  <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 500 }}>Parent Branch (Optional)</label>
                  <select
                    value={parentBranchId}
                    onChange={e => setParentBranchId(e.target.value)}
                    style={{ width: '100%', padding: '0.5rem', borderRadius: '4px', border: '1px solid #cbd5e1' }}
                  >
                    <option value="">(None - Top Level)</option>
                    {branches.map(b => (
                      <option key={b.id} value={b.id}>{b.branchCode} - {b.branchName}</option>
                    ))}
                  </select>
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
                  <div>
                    <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 500 }}>City</label>
                    <input
                      type="text"
                      value={city}
                      onChange={e => setCity(e.target.value)}
                      required
                      style={{ width: '100%', padding: '0.5rem', borderRadius: '4px', border: '1px solid #cbd5e1' }}
                    />
                  </div>
                  <div>
                    <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 500 }}>Province</label>
                    <input
                      type="text"
                      value={province}
                      onChange={e => setProvince(e.target.value)}
                      required
                      style={{ width: '100%', padding: '0.5rem', borderRadius: '4px', border: '1px solid #cbd5e1' }}
                    />
                  </div>
                </div>
                <button
                  type="submit"
                  style={{
                    padding: '0.6rem',
                    background: '#2563eb',
                    color: '#fff',
                    border: 'none',
                    borderRadius: '4px',
                    fontWeight: 500,
                    cursor: 'pointer',
                    marginTop: '0.5rem'
                  }}
                >
                  Create Branch
                </button>
              </form>
            </div>
          </div>
        </section>
      )}

      {/* Tab 2: Intercompany Transfers */}
      {activeTab === 'transfers' && (
        <section>
          <h2>Intercompany Stock Transfer Console</h2>
          {transferMessage && (
            <div style={{ padding: '0.75rem', marginBottom: '1rem', background: '#f0fdf4', borderRadius: '4px', color: '#15803d' }}>
              {transferMessage}
            </div>
          )}

          <div style={{ background: '#fff', border: '1px solid #cbd5e1', borderRadius: '8px', padding: '1rem', marginBottom: '1.5rem' }}>
            <h3>Transfer Pipeline (Dispatched → In Transit → Received)</h3>
            <table style={{ width: '100%', borderCollapse: 'collapse', marginTop: '0.75rem' }}>
              <thead>
                <tr style={{ background: '#f8fafc', borderBottom: '2px solid #e2e8f0', textAlign: 'left' }}>
                  <th style={{ padding: '0.6rem' }}>Transfer #</th>
                  <th style={{ padding: '0.6rem' }}>From Company</th>
                  <th style={{ padding: '0.6rem' }}>To Company</th>
                  <th style={{ padding: '0.6rem' }}>Dispatch Date</th>
                  <th style={{ padding: '0.6rem' }}>Total PKR</th>
                  <th style={{ padding: '0.6rem' }}>Status</th>
                  <th style={{ padding: '0.6rem' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {transfers.length === 0 ? (
                  <tr>
                    <td colSpan={7} style={{ padding: '1rem', textAlign: 'center', color: '#94a3b8' }}>
                      No intercompany transfers recorded.
                    </td>
                  </tr>
                ) : (
                  transfers.map(t => {
                    const statusMeta = formatTransferStatus(t.status);
                    return (
                      <tr key={t.id} style={{ borderBottom: '1px solid #f1f5f9' }}>
                        <td style={{ padding: '0.6rem', fontWeight: 600 }}>{t.transferNumber}</td>
                        <td style={{ padding: '0.6rem' }}>{t.fromCompanyId.substring(0, 8)}...</td>
                        <td style={{ padding: '0.6rem' }}>{t.toCompanyId.substring(0, 8)}...</td>
                        <td style={{ padding: '0.6rem' }}>{t.dispatchDate}</td>
                        <td style={{ padding: '0.6rem', fontWeight: 500 }}>PKR {Math.round(t.totalAmountPkr).toLocaleString()}</td>
                        <td style={{ padding: '0.6rem' }}>
                          <span style={{ padding: '0.2rem 0.6rem', borderRadius: '12px', fontSize: '0.8rem', background: '#e0f2fe', color: '#0369a1' }}>
                            {statusMeta.label}
                          </span>
                        </td>
                        <td style={{ padding: '0.6rem' }}>
                          {t.status === 'draft' && (
                            <button
                              onClick={() => handleDispatch(t.id)}
                              style={{ padding: '0.3rem 0.6rem', background: '#16a34a', color: '#fff', border: 'none', borderRadius: '4px', cursor: 'pointer', fontSize: '0.85rem' }}
                            >
                              Dispatch
                            </button>
                          )}
                          {(t.status === 'dispatched' || t.status === 'in_transit') && (
                            <button
                              onClick={() => handleReceive(t.id)}
                              style={{ padding: '0.3rem 0.6rem', background: '#2563eb', color: '#fff', border: 'none', borderRadius: '4px', cursor: 'pointer', fontSize: '0.85rem' }}
                            >
                              Receive
                            </button>
                          )}
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* Tab 3: Group Financial Consolidation */}
      {activeTab === 'consolidation' && (
        <section>
          <h2>Group Financial Consolidation Engine</h2>
          {consolidationMessage && (
            <div style={{ padding: '0.75rem', marginBottom: '1rem', background: '#eff6ff', borderRadius: '4px', color: '#1d4ed8' }}>
              {consolidationMessage}
            </div>
          )}

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: '1.5rem' }}>
            <div style={{ background: '#fff', border: '1px solid #cbd5e1', borderRadius: '8px', padding: '1rem' }}>
              <h3>Consolidation Runs</h3>
              <ul style={{ listStyle: 'none', padding: 0 }}>
                {runs.map(r => (
                  <li
                    key={r.id}
                    onClick={() => handleSelectRun(r)}
                    style={{
                      padding: '0.75rem',
                      borderRadius: '6px',
                      background: selectedRun?.id === r.id ? '#e0f2fe' : '#f8fafc',
                      marginBottom: '0.5rem',
                      cursor: 'pointer',
                      border: '1px solid #e2e8f0'
                    }}
                  >
                    <div style={{ fontWeight: 600 }}>{r.runCode}</div>
                    <div style={{ fontSize: '0.85rem', color: '#64748b' }}>
                      Assets: PKR {Math.round(r.totalConsolidatedAssetsPkr).toLocaleString()} | Elim: PKR {Math.round(r.totalIntercompanyEliminationsPkr).toLocaleString()}
                    </div>
                  </li>
                ))}
              </ul>
            </div>

            <div style={{ background: '#fff', border: '1px solid #cbd5e1', borderRadius: '8px', padding: '1rem' }}>
              <h3>Consolidated Financial Statement</h3>
              {statement ? (
                <div>
                  <div style={{ marginBottom: '1rem', display: 'flex', justifyContent: 'space-between' }}>
                    <div>
                      <strong>Run:</strong> {statement.runCode} | <strong>Status:</strong> {statement.isBalanced ? 'BALANCED (Zero Discrepancy)' : 'IMBALANCED'}
                    </div>
                    <div>
                      <strong>Total Eliminations:</strong> PKR {Math.round(statement.totalDebits).toLocaleString()}
                    </div>
                  </div>

                  <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                    <thead>
                      <tr style={{ background: '#f8fafc', borderBottom: '2px solid #e2e8f0', textAlign: 'left' }}>
                        <th style={{ padding: '0.5rem' }}>Account</th>
                        <th style={{ padding: '0.5rem' }}>Name</th>
                        <th style={{ padding: '0.5rem' }}>Type</th>
                        <th style={{ padding: '0.5rem' }}>Elim Dr</th>
                        <th style={{ padding: '0.5rem' }}>Elim Cr</th>
                        <th style={{ padding: '0.5rem' }}>Consolidated PKR</th>
                      </tr>
                    </thead>
                    <tbody>
                      {statement.lines.map((l: any) => (
                        <tr key={l.accountCode} style={{ borderBottom: '1px solid #f1f5f9' }}>
                          <td style={{ padding: '0.5rem', fontWeight: 600 }}>{l.accountCode}</td>
                          <td style={{ padding: '0.5rem' }}>{l.accountName}</td>
                          <td style={{ padding: '0.5rem', textTransform: 'capitalize' }}>{l.accountType}</td>
                          <td style={{ padding: '0.5rem', color: l.eliminationDebit > 0 ? '#dc2626' : '#94a3b8' }}>
                            {l.eliminationDebit > 0 ? `PKR ${Math.round(l.eliminationDebit).toLocaleString()}` : '-'}
                          </td>
                          <td style={{ padding: '0.5rem', color: l.eliminationCredit > 0 ? '#16a34a' : '#94a3b8' }}>
                            {l.eliminationCredit > 0 ? `PKR ${Math.round(l.eliminationCredit).toLocaleString()}` : '-'}
                          </td>
                          <td style={{ padding: '0.5rem', fontWeight: 600 }}>
                            PKR {Math.round(l.consolidatedBalance).toLocaleString()}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : (
                <p style={{ color: '#94a3b8' }}>Select a consolidation run to view side-by-side statements and eliminations.</p>
              )}
            </div>
          </div>
        </section>
      )}

      {/* Tab 4: External Integrations Hub */}
      {activeTab === 'integrations' && (
        <section>
          <h2>External Integrations & Webhooks Hub</h2>
          {integrationMessage && (
            <div style={{ padding: '0.75rem', marginBottom: '1rem', background: '#eff6ff', borderRadius: '4px', color: '#1d4ed8' }}>
              {integrationMessage}
            </div>
          )}

          {createdSecret && (
            <div style={{ padding: '1rem', marginBottom: '1rem', background: '#fef3c7', border: '1px solid #f59e0b', borderRadius: '6px' }}>
              <strong>Save Secret Token:</strong> Webhook HMAC Secret is only shown once:
              <code style={{ display: 'block', marginTop: '0.5rem', padding: '0.5rem', background: '#fff', borderRadius: '4px', wordBreak: 'break-all' }}>
                {createdSecret}
              </code>
            </div>
          )}

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1.5rem' }}>
            <div style={{ background: '#fff', border: '1px solid #cbd5e1', borderRadius: '8px', padding: '1rem' }}>
              <h3>Webhook Subscriptions</h3>
              <form onSubmit={handleCreateWebhook} style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem', marginBottom: '1rem' }}>
                <div>
                  <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 500 }}>Target Endpoint URL</label>
                  <input
                    type="url"
                    value={webhookUrl}
                    onChange={e => setWebhookUrl(e.target.value)}
                    required
                    style={{ width: '100%', padding: '0.5rem', borderRadius: '4px', border: '1px solid #cbd5e1' }}
                  />
                </div>
                <div>
                  <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 500 }}>Event Topic</label>
                  <select
                    value={webhookEventType}
                    onChange={e => setWebhookEventType(e.target.value)}
                    style={{ width: '100%', padding: '0.5rem', borderRadius: '4px', border: '1px solid #cbd5e1' }}
                  >
                    <option value="intercompany.transfer.dispatched">intercompany.transfer.dispatched</option>
                    <option value="intercompany.transfer.received">intercompany.transfer.received</option>
                    <option value="consolidation.run.completed">consolidation.run.completed</option>
                    <option value="invoice.posted">invoice.posted</option>
                    <option value="inventory.movement">inventory.movement</option>
                  </select>
                </div>
                <button
                  type="submit"
                  style={{
                    padding: '0.6rem',
                    background: '#2563eb',
                    color: '#fff',
                    border: 'none',
                    borderRadius: '4px',
                    fontWeight: 500,
                    cursor: 'pointer'
                  }}
                >
                  Register Webhook
                </button>
              </form>

              <h4>Active Subscriptions</h4>
              <ul style={{ listStyle: 'none', padding: 0 }}>
                {webhooks.map(w => (
                  <li key={w.id} style={{ padding: '0.5rem', borderBottom: '1px solid #f1f5f9' }}>
                    <div style={{ fontWeight: 600 }}>{w.eventType}</div>
                    <div style={{ fontSize: '0.85rem', color: '#64748b' }}>{w.targetUrl}</div>
                  </li>
                ))}
              </ul>
            </div>

            <div style={{ background: '#fff', border: '1px solid #cbd5e1', borderRadius: '8px', padding: '1rem' }}>
              <h3>Outbound Delivery Log & Dead Letter Queue</h3>
              <table style={{ width: '100%', borderCollapse: 'collapse', marginTop: '0.5rem' }}>
                <thead>
                  <tr style={{ background: '#f8fafc', borderBottom: '1px solid #e2e8f0', textAlign: 'left', fontSize: '0.85rem' }}>
                    <th style={{ padding: '0.4rem' }}>Event</th>
                    <th style={{ padding: '0.4rem' }}>Status</th>
                    <th style={{ padding: '0.4rem' }}>Attempts</th>
                    <th style={{ padding: '0.4rem' }}>Code</th>
                  </tr>
                </thead>
                <tbody>
                  {deliveries.length === 0 ? (
                    <tr>
                      <td colSpan={4} style={{ padding: '1rem', textAlign: 'center', color: '#94a3b8' }}>
                        No delivery events logged yet.
                      </td>
                    </tr>
                  ) : (
                    deliveries.map(d => {
                      const statusMeta = formatDeliveryStatus(d.deliveryStatus);
                      return (
                        <tr key={d.id} style={{ borderBottom: '1px solid #f1f5f9', fontSize: '0.85rem' }}>
                          <td style={{ padding: '0.4rem' }}>{d.eventType}</td>
                          <td style={{ padding: '0.4rem' }}>
                            <span style={{ padding: '0.15rem 0.4rem', borderRadius: '8px', fontSize: '0.75rem', background: '#f1f5f9' }}>
                              {statusMeta.label}
                            </span>
                          </td>
                          <td style={{ padding: '0.4rem' }}>{d.attemptCount} / 5</td>
                          <td style={{ padding: '0.4rem' }}>{d.lastResponseCode || '-'}</td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </section>
      )}
    </div>
  );
};
