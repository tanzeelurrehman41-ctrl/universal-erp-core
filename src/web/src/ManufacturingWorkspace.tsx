import { useState, useEffect, type FormEvent } from "react";
import { apiFetch, type TenantCompanyContext } from "./auth";
import {
  explodeBomRequirements,
  calculateWorkOrderCosting,
  validateWorkOrderProduction,
  formatCurrencyPkr,
  type ExplodedRequirement,
  type WorkOrderCostRollup,
} from "./manufacturingHelpers";

export type WorkCenter = {
  id: string;
  code: string;
  name: string;
  hourlyLaborRate: number;
  hourlyOverheadRate: number;
  dailyCapacityHours: number;
  efficiencyPercent: number;
  isActive: boolean;
};

export type RoutingOperation = {
  id: string;
  routingId: string;
  operationSequence: number;
  operationName: string;
  workCenterId: string;
  setupTimeMinutes: number;
  runTimeMinutesPerUnit: number;
  description?: string | null;
};

export type Routing = {
  id: string;
  routingNumber: string;
  name: string;
  productVariantId: string;
  isActive: boolean;
  description?: string | null;
  operations?: RoutingOperation[];
};

export type BomItem = {
  id: string;
  bomRevisionId: string;
  lineNumber: number;
  componentVariantId: string;
  componentType: string;
  quantityPer: number;
  scrapPercentage: number;
  notes?: string | null;
};

export type BomRevision = {
  id: string;
  bomId: string;
  revisionCode: string;
  effectiveFrom: string;
  effectiveTo?: string | null;
  status: string;
  changeReason?: string | null;
  items?: BomItem[];
};

export type Bom = {
  id: string;
  bomNumber: string;
  productVariantId: string;
  name: string;
  baseQuantity: number;
  uomId?: string | null;
  yieldPercent: number;
  currentRevision: string;
  status: string;
  revisions?: BomRevision[];
};

export type MaterialSubstitution = {
  id: string;
  originalVariantId: string;
  substituteVariantId: string;
  conversionFactor: number;
  priority: number;
  isActive: boolean;
  notes?: string | null;
};

export type WorkOrderMaterial = {
  id: string;
  workOrderId: string;
  lineNumber: number;
  componentVariantId: string;
  requiredQuantity: number;
  reservedQuantity: number;
  issuedQuantity: number;
  returnedQuantity: number;
  reservationId?: string | null;
  consumedCost: number;
  status: string;
};

export type WorkOrderOperation = {
  id: string;
  workOrderId: string;
  operationSequence: number;
  operationName: string;
  workCenterId: string;
  setupTimeMinutes: number;
  runTimeMinutesPerUnit: number;
  actualSetupMinutes: number;
  actualRunMinutes: number;
  completedQuantity: number;
  scrappedQuantity: number;
  allocatedLaborCost: number;
  allocatedOverheadCost: number;
  status: string;
};

export type WorkOrder = {
  id: string;
  workOrderNumber: string;
  productVariantId: string;
  bomId?: string | null;
  bomRevisionId?: string | null;
  routingId?: string | null;
  targetWarehouseId: string;
  targetBinId: string;
  plannedQuantity: number;
  completedQuantity: number;
  scrappedQuantity: number;
  rejectedQuantity: number;
  startDate: string;
  dueDate: string;
  status: string;
  totalMaterialCost: number;
  totalLaborCost: number;
  totalOverheadCost: number;
  totalProductionCost: number;
  unitCostFinal: number;
  notes?: string | null;
  materials?: WorkOrderMaterial[];
  operations?: WorkOrderOperation[];
};

export type ProductionInspection = {
  id: string;
  workOrderId: string;
  inspectionNumber: string;
  inspectionType: string;
  inspectedQuantity: number;
  acceptedQuantity: number;
  rejectedQuantity: number;
  quarantineBinId?: string | null;
  disposition: string;
  inspectionDate: string;
  notes?: string | null;
};

export type GenealogyLayerSource = {
  layerId: string;
  movementId: string;
  consumedQty: number;
  unitCost: number;
  extendedCost: number;
  receivedAt: string;
  sourceDocumentType: string;
  sourceDocumentId?: string | null;
};

export type GenealogyComponentNode = {
  componentVariantId: string;
  consumedQuantity: number;
  consumedCost: number;
  layerSources: GenealogyLayerSource[];
};

export type GenealogyRecord = {
  workOrderId: string;
  workOrderNumber: string;
  productVariantId: string;
  completedQuantity: number;
  completedAt?: string | null;
  consumedComponents: GenealogyComponentNode[];
};

export type WipSummary = {
  workOrderId: string;
  workOrderNumber: string;
  status: string;
  plannedQuantity: number;
  completedQuantity: number;
  materialCost: number;
  laborCost: number;
  overheadCost: number;
  totalWip: number;
};

export type WipReport = {
  totalActiveWorkOrders: number;
  totalAccumulatedWip: number;
  totalMaterialCost: number;
  totalLaborCost: number;
  totalOverheadCost: number;
  workOrders: WipSummary[];
};

type Tab =
  | "work-orders"
  | "boms"
  | "routings"
  | "work-centers"
  | "substitutions"
  | "issues"
  | "operations"
  | "qc"
  | "completion"
  | "genealogy"
  | "wip-report";

type Props = {
  enabled: boolean;
  workspace: TenantCompanyContext | null;
};

export default function ManufacturingWorkspace({ enabled, workspace }: Props) {
  const [activeTab, setActiveTab] = useState<Tab>("work-orders");
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  // Master Data State
  const [workCenters, setWorkCenters] = useState<WorkCenter[]>([]);
  const [routings, setRoutings] = useState<Routing[]>([]);
  const [boms, setBoms] = useState<Bom[]>([]);
  const [substitutions, setSubstitutions] = useState<MaterialSubstitution[]>([]);
  const [workOrders, setWorkOrders] = useState<WorkOrder[]>([]);
  const [selectedWorkOrder, setSelectedWorkOrder] = useState<WorkOrder | null>(null);
  const [genealogy, setGenealogy] = useState<GenealogyRecord | null>(null);
  const [wipReport, setWipReport] = useState<WipReport | null>(null);

  // Forms State
  // New Work Center
  const [wcCode, setWcCode] = useState("");
  const [wcName, setWcName] = useState("");
  const [wcLaborRate, setWcLaborRate] = useState("500");
  const [wcOverheadRate, setWcOverheadRate] = useState("200");
  const [wcDailyCap, setWcDailyCap] = useState("8");
  const [wcEfficiency, setWcEfficiency] = useState("100");

  // New BOM
  const [bomNumber, setBomNumber] = useState("");
  const [bomName, setBomName] = useState("");
  const [bomVariantId, setBomVariantId] = useState("");
  const [bomYield, setBomYield] = useState("100");
  const [bomRevisionCode, setBomRevisionCode] = useState("A");
  const [bomItemVariantId, setBomItemVariantId] = useState("");
  const [bomItemQtyPer, setBomItemQtyPer] = useState("1");
  const [bomItemScrap, setBomItemScrap] = useState("0");

  // New Work Order
  const [woNumber, setWoNumber] = useState("");
  const [woVariantId, setWoVariantId] = useState("");
  const [woBomId, setWoBomId] = useState("");
  const [woRoutingId, setWoRoutingId] = useState("");
  const [woWarehouseId, setWoWarehouseId] = useState("");
  const [woBinId, setWoBinId] = useState("");
  const [woPlannedQty, setWoPlannedQty] = useState("10");
  const [woDueDate, setWoDueDate] = useState(new Date().toISOString().split("T")[0]);

  // Material Issue Form
  const [issueLineNumber, setIssueLineNumber] = useState("1");
  const [issueVariantId, setIssueVariantId] = useState("");
  const [issueQty, setIssueQty] = useState("");
  const [issueWarehouseId, setIssueWarehouseId] = useState("");
  const [issueBinId, setIssueBinId] = useState("");

  // Operation Progress Form
  const [opSeq, setOpSeq] = useState("10");
  const [opSetupMin, setOpSetupMin] = useState("30");
  const [opRunMin, setOpRunMin] = useState("60");
  const [opCompletedQty, setOpCompletedQty] = useState("10");
  const [opScrappedQty, setOpScrappedQty] = useState("0");

  // QC Form
  const [qcNumber, setQcNumber] = useState("");
  const [qcInspectedQty, setQcInspectedQty] = useState("10");
  const [qcAcceptedQty, setQcAcceptedQty] = useState("9");
  const [qcRejectedQty, setQcRejectedQty] = useState("1");
  const [qcDisposition, setQcDisposition] = useState("quarantine");
  const [qcQuarantineBinId, setQcQuarantineBinId] = useState("");

  // Completion Form
  const [completeAcceptedQty, setCompleteAcceptedQty] = useState("9");
  const [completeScrappedQty, setCompleteScrappedQty] = useState("1");

  const fetchJson = async <T = any,>(url: string, init?: RequestInit): Promise<T> => {
    const res = await apiFetch(url, init, workspace);
    if (!res.ok) {
      let errMsg = `Request failed: ${res.statusText}`;
      try {
        const body = await res.json();
        if (body?.error) errMsg = body.error;
        else if (body?.message) errMsg = body.message;
      } catch {}
      throw new Error(errMsg);
    }
    return (await res.json()) as T;
  };

  // Load initial data
  useEffect(() => {
    if (!enabled) return;
    refreshData();
  }, [enabled, workspace]);

  const refreshData = async () => {
    setLoading(true);
    setError("");
    try {
      const [wcRes, routRes, bomRes, subRes, woRes, wipRes] = await Promise.all([
        fetchJson<WorkCenter[]>("/api/v1/manufacturing/work-centers"),
        fetchJson<Routing[]>("/api/v1/manufacturing/routings"),
        fetchJson<Bom[]>("/api/v1/manufacturing/boms"),
        fetchJson<MaterialSubstitution[]>("/api/v1/manufacturing/substitutions"),
        fetchJson<WorkOrder[]>("/api/v1/manufacturing/work-orders"),
        fetchJson<WipReport>("/api/v1/manufacturing/reports/wip-variance"),
      ]);

      if (wcRes) setWorkCenters(wcRes);
      if (routRes) setRoutings(routRes);
      if (bomRes) setBoms(bomRes);
      if (subRes) setSubstitutions(subRes);
      if (woRes) {
        setWorkOrders(woRes);
        if (woRes.length > 0 && !selectedWorkOrder) {
          loadWorkOrderDetail(woRes[0].id);
        }
      }
      if (wipRes) setWipReport(wipRes);
    } catch (err: any) {
      setError(err.message || "Failed to load manufacturing data.");
    } finally {
      setLoading(false);
    }
  };

  const loadWorkOrderDetail = async (woId: string) => {
    try {
      const detail = await fetchJson<WorkOrder>(`/api/v1/manufacturing/work-orders/${woId}`);
      setSelectedWorkOrder(detail);
      if (detail && detail.materials && detail.materials.length > 0) {
        setIssueLineNumber(detail.materials[0].lineNumber.toString());
        setIssueVariantId(detail.materials[0].componentVariantId);
        setIssueQty(detail.materials[0].requiredQuantity.toString());
        setIssueWarehouseId(detail.targetWarehouseId);
        setIssueBinId(detail.targetBinId);
      }
    } catch (err: any) {
      setError(err.message || "Failed to load work order details.");
    }
  };

  // 1. Create Work Center
  const handleCreateWorkCenter = async (e: FormEvent) => {
    e.preventDefault();
    setError("");
    setMessage("");
    try {
      await fetchJson("/api/v1/manufacturing/work-centers", {
        method: "POST",
        body: JSON.stringify({
          code: wcCode,
          name: wcName,
          hourlyLaborRate: parseFloat(wcLaborRate),
          hourlyOverheadRate: parseFloat(wcOverheadRate),
          dailyCapacityHours: parseFloat(wcDailyCap),
          efficiencyPercent: parseFloat(wcEfficiency),
        }),
      });
      setMessage(`Work center ${wcCode} created successfully.`);
      setWcCode("");
      setWcName("");
      refreshData();
    } catch (err: any) {
      setError(err.message || "Failed to create work center.");
    }
  };

  // 2. Create BOM
  const handleCreateBom = async (e: FormEvent) => {
    e.preventDefault();
    setError("");
    setMessage("");
    try {
      await fetchJson("/api/v1/manufacturing/boms", {
        method: "POST",
        body: JSON.stringify({
          bomNumber,
          productVariantId: bomVariantId,
          name: bomName,
          baseQuantity: 1,
          yieldPercent: parseFloat(bomYield),
          revisionCode: bomRevisionCode,
          effectiveFrom: new Date().toISOString().split("T")[0],
          changeReason: "Initial revision",
          items: [
            {
              lineNumber: 1,
              componentVariantId: bomItemVariantId,
              componentType: "raw_material",
              quantityPer: parseFloat(bomItemQtyPer),
              scrapPercentage: parseFloat(bomItemScrap),
            },
          ],
        }),
      });
      setMessage(`BOM ${bomNumber} created successfully.`);
      setBomNumber("");
      setBomName("");
      refreshData();
    } catch (err: any) {
      setError(err.message || "Failed to create BOM.");
    }
  };

  // 3. Create Work Order
  const handleCreateWorkOrder = async (e: FormEvent) => {
    e.preventDefault();
    setError("");
    setMessage("");
    try {
      const created = await fetchJson<WorkOrder>("/api/v1/manufacturing/work-orders", {
        method: "POST",
        body: JSON.stringify({
          workOrderNumber: woNumber,
          productVariantId: woVariantId,
          bomId: woBomId || null,
          routingId: woRoutingId || null,
          targetWarehouseId: woWarehouseId,
          targetBinId: woBinId,
          plannedQuantity: parseFloat(woPlannedQty),
          startDate: new Date().toISOString().split("T")[0],
          dueDate: woDueDate,
        }),
      });
      setMessage(`Work order ${woNumber} created in planned status.`);
      setWoNumber("");
      refreshData();
      if (created) loadWorkOrderDetail(created.id);
    } catch (err: any) {
      setError(err.message || "Failed to create work order.");
    }
  };

  // 4. Release Work Order
  const handleReleaseWorkOrder = async (woId: string) => {
    setError("");
    setMessage("");
    try {
      await fetchJson(`/api/v1/manufacturing/work-orders/${woId}/release`, {
        method: "POST",
      });
      setMessage("Work order released. Stock reservations created in warehouse.");
      refreshData();
      loadWorkOrderDetail(woId);
    } catch (err: any) {
      setError(err.message || "Failed to release work order.");
    }
  };

  // 5. Issue Material
  const handleIssueMaterial = async (e: FormEvent) => {
    e.preventDefault();
    if (!selectedWorkOrder) return;
    setError("");
    setMessage("");
    try {
      const res = await fetchJson<any>(`/api/v1/manufacturing/work-orders/${selectedWorkOrder.id}/issue-material`, {
        method: "POST",
        body: JSON.stringify({
          lineNumber: parseInt(issueLineNumber),
          componentVariantId: issueVariantId,
          quantity: parseFloat(issueQty),
          warehouseId: issueWarehouseId,
          binId: issueBinId,
        }),
      });
      setMessage(`Issued ${issueQty} units. WIP debited and Raw Materials credited by ${formatCurrencyPkr(res.consumedCost)}.`);
      refreshData();
      loadWorkOrderDetail(selectedWorkOrder.id);
    } catch (err: any) {
      setError(err.message || "Failed to issue material.");
    }
  };

  // 6. Report Operation Progress
  const handleReportProgress = async (e: FormEvent) => {
    e.preventDefault();
    if (!selectedWorkOrder) return;
    setError("");
    setMessage("");
    try {
      const res = await fetchJson<any>(`/api/v1/manufacturing/work-orders/${selectedWorkOrder.id}/report-progress`, {
        method: "POST",
        body: JSON.stringify({
          operationSequence: parseInt(opSeq),
          actualSetupMinutes: parseFloat(opSetupMin),
          actualRunMinutes: parseFloat(opRunMin),
          completedQuantity: parseFloat(opCompletedQty),
          scrappedQuantity: parseFloat(opScrappedQty),
        }),
      });
      setMessage(`Operation ${opSeq} reported. Direct labor ${formatCurrencyPkr(res.allocatedLaborCost)} and applied overhead ${formatCurrencyPkr(res.allocatedOverheadCost)} absorbed into WIP.`);
      refreshData();
      loadWorkOrderDetail(selectedWorkOrder.id);
    } catch (err: any) {
      setError(err.message || "Failed to report operation progress.");
    }
  };

  // 7. Record QC
  const handleRecordQc = async (e: FormEvent) => {
    e.preventDefault();
    if (!selectedWorkOrder) return;
    setError("");
    setMessage("");
    try {
      await fetchJson(`/api/v1/manufacturing/work-orders/${selectedWorkOrder.id}/inspections`, {
        method: "POST",
        body: JSON.stringify({
          inspectionNumber: qcNumber,
          inspectionType: "finished_goods",
          inspectedQuantity: parseFloat(qcInspectedQty),
          acceptedQuantity: parseFloat(qcAcceptedQty),
          rejectedQuantity: parseFloat(qcRejectedQty),
          quarantineBinId: qcQuarantineBinId || null,
          disposition: qcDisposition,
          notes: "QC inspection recorded",
        }),
      });
      setMessage(`Inspection ${qcNumber} recorded successfully.`);
      setQcNumber("");
      refreshData();
      loadWorkOrderDetail(selectedWorkOrder.id);
    } catch (err: any) {
      setError(err.message || "Failed to record inspection.");
    }
  };

  // 8. Complete Work Order
  const handleCompleteWorkOrder = async (e: FormEvent) => {
    e.preventDefault();
    if (!selectedWorkOrder) return;
    setError("");
    setMessage("");

    const accepted = parseFloat(completeAcceptedQty);
    const scrapped = parseFloat(completeScrappedQty);

    const validation = validateWorkOrderProduction(
      selectedWorkOrder.plannedQuantity,
      selectedWorkOrder.completedQuantity,
      accepted,
      scrapped
    );

    if (!validation.isValid) {
      setError(validation.error || "Invalid completion quantities.");
      return;
    }

    try {
      const res = await fetchJson<any>(`/api/v1/manufacturing/work-orders/${selectedWorkOrder.id}/complete`, {
        method: "POST",
        body: JSON.stringify({
          acceptedQuantity: accepted,
          scrappedQuantity: scrapped,
          notes: "Finished goods completion",
        }),
      });
      setMessage(`Work order completed! Capitalized ${accepted} units at unit cost ${formatCurrencyPkr(res.unitCostFinal)}. Finished goods FIFO layer created.`);
      refreshData();
      loadWorkOrderDetail(selectedWorkOrder.id);
    } catch (err: any) {
      setError(err.message || "Failed to complete work order.");
    }
  };

  // 9. Load Genealogy
  const handleLoadGenealogy = async (woId: string) => {
    setError("");
    try {
      const gen = await fetchJson<GenealogyRecord>(`/api/v1/manufacturing/work-orders/${woId}/genealogy`);
      setGenealogy(gen);
      setActiveTab("genealogy");
    } catch (err: any) {
      setError(err.message || "Failed to load genealogy.");
    }
  };

  return (
    <div className="workspace-container">
      <div className="workspace-header">
        <h2>Manufacturing, BOM & Work Orders (Phase 9)</h2>
        <p className="subtitle">
          Multi-level BOM engineering, work centers, routing execution, FIFO raw material issues, and balanced double-entry WIP accounting.
        </p>
      </div>

      {message && <div className="alert alert-success">{message}</div>}
      {error && <div className="alert alert-danger">{error}</div>}

      <div className="tabs-nav">
        <button className={activeTab === "work-orders" ? "active" : ""} onClick={() => setActiveTab("work-orders")}>
          Work Orders ({workOrders.length})
        </button>
        <button className={activeTab === "issues" ? "active" : ""} onClick={() => setActiveTab("issues")}>
          Material Issue & FIFO
        </button>
        <button className={activeTab === "operations" ? "active" : ""} onClick={() => setActiveTab("operations")}>
          Operation Execution
        </button>
        <button className={activeTab === "qc" ? "active" : ""} onClick={() => setActiveTab("qc")}>
          Quality Control
        </button>
        <button className={activeTab === "completion" ? "active" : ""} onClick={() => setActiveTab("completion")}>
          FG Completion & Costing
        </button>
        <button className={activeTab === "boms" ? "active" : ""} onClick={() => setActiveTab("boms")}>
          BOM Master ({boms.length})
        </button>
        <button className={activeTab === "routings" ? "active" : ""} onClick={() => setActiveTab("routings")}>
          Routings ({routings.length})
        </button>
        <button className={activeTab === "work-centers" ? "active" : ""} onClick={() => setActiveTab("work-centers")}>
          Work Centers ({workCenters.length})
        </button>
        <button className={activeTab === "genealogy" ? "active" : ""} onClick={() => setActiveTab("genealogy")}>
          Genealogy & Traceability
        </button>
        <button className={activeTab === "wip-report" ? "active" : ""} onClick={() => setActiveTab("wip-report")}>
          WIP & Variance Report
        </button>
      </div>

      {loading && <div className="loading-spinner">Loading manufacturing data...</div>}

      {/* ── WORK ORDERS TAB ─────────────────────────────────────────── */}
      {activeTab === "work-orders" && (
        <div className="tab-pane">
          <div className="split-view">
            <div className="left-pane">
              <h3>Active Work Orders</h3>
              <table className="data-table">
                <thead>
                  <tr>
                    <th>WO Number</th>
                    <th>Status</th>
                    <th>Planned</th>
                    <th>Completed</th>
                    <th>WIP Cost</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {workOrders.map((wo) => (
                    <tr
                      key={wo.id}
                      className={selectedWorkOrder?.id === wo.id ? "selected-row" : ""}
                      onClick={() => loadWorkOrderDetail(wo.id)}
                      style={{ cursor: "pointer" }}
                    >
                      <td><strong>{wo.workOrderNumber}</strong></td>
                      <td>
                        <span className={`status-pill status-${wo.status}`}>{wo.status}</span>
                      </td>
                      <td>{wo.plannedQuantity}</td>
                      <td>{wo.completedQuantity}</td>
                      <td>{formatCurrencyPkr(wo.totalProductionCost)}</td>
                      <td>
                        {wo.status === "planned" && (
                          <button className="btn-sm btn-primary" onClick={(e) => { e.stopPropagation(); handleReleaseWorkOrder(wo.id); }}>
                            Release
                          </button>
                        )}
                        {wo.status === "completed" && (
                          <button className="btn-sm btn-secondary" onClick={(e) => { e.stopPropagation(); handleLoadGenealogy(wo.id); }}>
                            Genealogy
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                  {workOrders.length === 0 && (
                    <tr>
                      <td colSpan={6} className="text-center">No work orders created yet.</td>
                    </tr>
                  )}
                </tbody>
              </table>

              <div className="card" style={{ marginTop: "1.5rem" }}>
                <h4>Create New Work Order</h4>
                <form onSubmit={handleCreateWorkOrder}>
                  <div className="form-group">
                    <label>Work Order Number</label>
                    <input type="text" required value={woNumber} onChange={(e) => setWoNumber(e.target.value)} placeholder="WO-2026-001" />
                  </div>
                  <div className="form-group">
                    <label>Product Variant ID</label>
                    <input type="text" required value={woVariantId} onChange={(e) => setWoVariantId(e.target.value)} placeholder="UUID" />
                  </div>
                  <div className="form-row">
                    <div className="form-group">
                      <label>BOM</label>
                      <select value={woBomId} onChange={(e) => setWoBomId(e.target.value)}>
                        <option value="">Auto-select Active BOM</option>
                        {boms.map((b) => <option key={b.id} value={b.id}>{b.bomNumber} - {b.name}</option>)}
                      </select>
                    </div>
                    <div className="form-group">
                      <label>Routing</label>
                      <select value={woRoutingId} onChange={(e) => setWoRoutingId(e.target.value)}>
                        <option value="">None (Manual)</option>
                        {routings.map((r) => <option key={r.id} value={r.id}>{r.routingNumber} - {r.name}</option>)}
                      </select>
                    </div>
                  </div>
                  <div className="form-row">
                    <div className="form-group">
                      <label>Target Warehouse ID</label>
                      <input type="text" required value={woWarehouseId} onChange={(e) => setWoWarehouseId(e.target.value)} placeholder="UUID" />
                    </div>
                    <div className="form-group">
                      <label>Target Bin ID</label>
                      <input type="text" required value={woBinId} onChange={(e) => setWoBinId(e.target.value)} placeholder="UUID" />
                    </div>
                  </div>
                  <div className="form-row">
                    <div className="form-group">
                      <label>Planned Quantity</label>
                      <input type="number" step="1" min="1" required value={woPlannedQty} onChange={(e) => setWoPlannedQty(e.target.value)} />
                    </div>
                    <div className="form-group">
                      <label>Due Date</label>
                      <input type="date" required value={woDueDate} onChange={(e) => setWoDueDate(e.target.value)} />
                    </div>
                  </div>
                  <button type="submit" className="btn btn-primary" style={{ marginTop: "1rem" }}>
                    Create Planned Work Order
                  </button>
                </form>
              </div>
            </div>

            <div className="right-pane">
              {selectedWorkOrder ? (
                <div className="card">
                  <h3>Work Order Details: {selectedWorkOrder.workOrderNumber}</h3>
                  <div className="grid-2">
                    <div>
                      <p><strong>Status:</strong> <span className={`status-pill status-${selectedWorkOrder.status}`}>{selectedWorkOrder.status}</span></p>
                      <p><strong>Planned Quantity:</strong> {selectedWorkOrder.plannedQuantity}</p>
                      <p><strong>Completed Quantity:</strong> {selectedWorkOrder.completedQuantity}</p>
                      <p><strong>Scrapped Quantity:</strong> {selectedWorkOrder.scrappedQuantity}</p>
                    </div>
                    <div>
                      <p><strong>Total Material Cost:</strong> {formatCurrencyPkr(selectedWorkOrder.totalMaterialCost)}</p>
                      <p><strong>Total Labor Cost:</strong> {formatCurrencyPkr(selectedWorkOrder.totalLaborCost)}</p>
                      <p><strong>Total Overhead Cost:</strong> {formatCurrencyPkr(selectedWorkOrder.totalOverheadCost)}</p>
                      <p><strong>Accumulated WIP Cost:</strong> <strong style={{ color: "#2563eb" }}>{formatCurrencyPkr(selectedWorkOrder.totalProductionCost)}</strong></p>
                    </div>
                  </div>

                  <h4 style={{ marginTop: "1rem" }}>Component Requirements & Stock Reservations</h4>
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Line</th>
                        <th>Required</th>
                        <th>Reserved</th>
                        <th>Issued</th>
                        <th>Consumed Cost</th>
                        <th>Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {selectedWorkOrder.materials?.map((m) => (
                        <tr key={m.id}>
                          <td>{m.lineNumber}</td>
                          <td>{m.requiredQuantity}</td>
                          <td>{m.reservedQuantity}</td>
                          <td>{m.issuedQuantity}</td>
                          <td>{formatCurrencyPkr(m.consumedCost)}</td>
                          <td><span className={`status-pill status-${m.status}`}>{m.status}</span></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>

                  <h4 style={{ marginTop: "1rem" }}>Operations Progress</h4>
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Seq</th>
                        <th>Operation</th>
                        <th>Actual Hours</th>
                        <th>Labor Cost</th>
                        <th>Overhead Cost</th>
                        <th>Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {selectedWorkOrder.operations?.map((op) => (
                        <tr key={op.id}>
                          <td>{op.operationSequence}</td>
                          <td>{op.operationName}</td>
                          <td>{((op.actualSetupMinutes + op.actualRunMinutes) / 60).toFixed(2)} hrs</td>
                          <td>{formatCurrencyPkr(op.allocatedLaborCost)}</td>
                          <td>{formatCurrencyPkr(op.allocatedOverheadCost)}</td>
                          <td><span className={`status-pill status-${op.status}`}>{op.status}</span></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : (
                <div className="card text-center" style={{ padding: "3rem" }}>
                  <p>Select a work order from the list to view requirements, reservations, and progress.</p>
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* ── MATERIAL ISSUE TAB ─────────────────────────────────────────── */}
      {activeTab === "issues" && (
        <div className="tab-pane">
          <div className="card">
            <h3>Issue Raw Materials to WIP (Model A FIFO Valuation)</h3>
            <p className="subtitle">
              Raw materials are consumed strictly from the specified warehouse and bin in First-In, First-Out order.
              Cross-bin borrowing is strictly prevented. Balanced GL journal (Dr WIP 1250 / Cr Raw Materials 1200) is generated automatically.
            </p>

            {selectedWorkOrder ? (
              <form onSubmit={handleIssueMaterial} style={{ maxWidth: "600px" }}>
                <p><strong>Target Work Order:</strong> {selectedWorkOrder.workOrderNumber} ({selectedWorkOrder.status})</p>
                <div className="form-group">
                  <label>Material Line Number</label>
                  <input type="number" min="1" required value={issueLineNumber} onChange={(e) => setIssueLineNumber(e.target.value)} />
                </div>
                <div className="form-group">
                  <label>Component Variant ID</label>
                  <input type="text" required value={issueVariantId} onChange={(e) => setIssueVariantId(e.target.value)} />
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label>Source Warehouse ID</label>
                    <input type="text" required value={issueWarehouseId} onChange={(e) => setIssueWarehouseId(e.target.value)} />
                  </div>
                  <div className="form-group">
                    <label>Source Bin ID</label>
                    <input type="text" required value={issueBinId} onChange={(e) => setIssueBinId(e.target.value)} />
                  </div>
                </div>
                <div className="form-group">
                  <label>Quantity to Issue</label>
                  <input type="number" step="0.0001" min="0.0001" required value={issueQty} onChange={(e) => setIssueQty(e.target.value)} />
                </div>
                <button type="submit" className="btn btn-primary" style={{ marginTop: "1rem" }}>
                  Issue Materials via FIFO
                </button>
              </form>
            ) : (
              <p>Please select a released work order first.</p>
            )}
          </div>
        </div>
      )}

      {/* ── OPERATIONS TAB ────────────────────────────────────────────── */}
      {activeTab === "operations" && (
        <div className="tab-pane">
          <div className="card">
            <h3>Report Operation Labor & Machine Run-Time</h3>
            <p className="subtitle">
              Direct labor and applied manufacturing overhead are calculated using work center hourly rates and absorbed into WIP (Dr WIP 1250 / Cr Labor Clearing 2050 & Overhead Clearing 6060).
            </p>

            {selectedWorkOrder ? (
              <form onSubmit={handleReportProgress} style={{ maxWidth: "600px" }}>
                <p><strong>Target Work Order:</strong> {selectedWorkOrder.workOrderNumber}</p>
                <div className="form-group">
                  <label>Operation Sequence</label>
                  <input type="number" required value={opSeq} onChange={(e) => setOpSeq(e.target.value)} />
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label>Actual Setup Minutes</label>
                    <input type="number" required value={opSetupMin} onChange={(e) => setOpSetupMin(e.target.value)} />
                  </div>
                  <div className="form-group">
                    <label>Actual Run Minutes</label>
                    <input type="number" required value={opRunMin} onChange={(e) => setOpRunMin(e.target.value)} />
                  </div>
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label>Completed Quantity</label>
                    <input type="number" required value={opCompletedQty} onChange={(e) => setOpCompletedQty(e.target.value)} />
                  </div>
                  <div className="form-group">
                    <label>Scrapped Quantity</label>
                    <input type="number" required value={opScrappedQty} onChange={(e) => setOpScrappedQty(e.target.value)} />
                  </div>
                </div>
                <button type="submit" className="btn btn-primary" style={{ marginTop: "1rem" }}>
                  Post Operation Progress
                </button>
              </form>
            ) : (
              <p>Please select a work order first.</p>
            )}
          </div>
        </div>
      )}

      {/* ── QUALITY CONTROL TAB ───────────────────────────────────────── */}
      {activeTab === "qc" && (
        <div className="tab-pane">
          <div className="card">
            <h3>Quality Control & In-Process Inspection</h3>
            <p className="subtitle">
              Inspect produced units, record accepted vs rejected items, and route defective parts to a quarantine bin.
            </p>

            {selectedWorkOrder ? (
              <form onSubmit={handleRecordQc} style={{ maxWidth: "600px" }}>
                <p><strong>Work Order:</strong> {selectedWorkOrder.workOrderNumber}</p>
                <div className="form-group">
                  <label>Inspection Number</label>
                  <input type="text" required value={qcNumber} onChange={(e) => setQcNumber(e.target.value)} placeholder="QC-2026-001" />
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label>Inspected Quantity</label>
                    <input type="number" required value={qcInspectedQty} onChange={(e) => setQcInspectedQty(e.target.value)} />
                  </div>
                  <div className="form-group">
                    <label>Accepted Quantity</label>
                    <input type="number" required value={qcAcceptedQty} onChange={(e) => setQcAcceptedQty(e.target.value)} />
                  </div>
                  <div className="form-group">
                    <label>Rejected Quantity</label>
                    <input type="number" required value={qcRejectedQty} onChange={(e) => setQcRejectedQty(e.target.value)} />
                  </div>
                </div>
                <div className="form-row">
                  <div className="form-group">
                    <label>Disposition</label>
                    <select value={qcDisposition} onChange={(e) => setQcDisposition(e.target.value)}>
                      <option value="accepted">Accepted</option>
                      <option value="quarantine">Quarantine</option>
                      <option value="scrap">Scrap</option>
                      <option value="rework">Rework</option>
                    </select>
                  </div>
                  <div className="form-group">
                    <label>Quarantine Bin ID (Optional)</label>
                    <input type="text" value={qcQuarantineBinId} onChange={(e) => setQcQuarantineBinId(e.target.value)} placeholder="UUID" />
                  </div>
                </div>
                <button type="submit" className="btn btn-primary" style={{ marginTop: "1rem" }}>
                  Record Inspection
                </button>
              </form>
            ) : (
              <p>Please select a work order first.</p>
            )}
          </div>
        </div>
      )}

      {/* ── FG COMPLETION TAB ─────────────────────────────────────────── */}
      {activeTab === "completion" && (
        <div className="tab-pane">
          <div className="card">
            <h3>Finished Goods Completion & Valuation</h3>
            <p className="subtitle">
              Capitalize finished goods into new FIFO inventory valuation layers. All actual material, labor, and overhead costs in WIP are reconciled.
              Scrap losses are expensed to account 5020.
            </p>

            {selectedWorkOrder ? (
              <form onSubmit={handleCompleteWorkOrder} style={{ maxWidth: "600px" }}>
                <p><strong>Work Order:</strong> {selectedWorkOrder.workOrderNumber}</p>
                <p><strong>Accumulated WIP Cost:</strong> {formatCurrencyPkr(selectedWorkOrder.totalProductionCost)}</p>
                <div className="form-row">
                  <div className="form-group">
                    <label>Accepted Quantity</label>
                    <input type="number" step="0.0001" required value={completeAcceptedQty} onChange={(e) => setCompleteAcceptedQty(e.target.value)} />
                  </div>
                  <div className="form-group">
                    <label>Scrapped Quantity</label>
                    <input type="number" step="0.0001" required value={completeScrappedQty} onChange={(e) => setCompleteScrappedQty(e.target.value)} />
                  </div>
                </div>
                <div className="costing-preview card" style={{ background: "#f8fafc", marginTop: "1rem" }}>
                  <h4>Costing Preview</h4>
                  {(() => {
                    const costing = calculateWorkOrderCosting(
                      selectedWorkOrder.totalMaterialCost,
                      selectedWorkOrder.totalLaborCost,
                      selectedWorkOrder.totalOverheadCost,
                      parseFloat(completeAcceptedQty) || 0,
                      parseFloat(completeScrappedQty) || 0
                    );
                    return (
                      <div>
                        <p><strong>Derived Unit Cost:</strong> {formatCurrencyPkr(costing.unitCostFinal)} / unit</p>
                        <p><strong>Finished Goods Valuation (Dr 1210):</strong> {formatCurrencyPkr(costing.finishedGoodsValuation)}</p>
                        <p><strong>Scrap Expense (Dr 5020):</strong> {formatCurrencyPkr(costing.scrapExpense)}</p>
                        <p><strong>WIP Relieved (Cr 1250):</strong> {formatCurrencyPkr(costing.totalProductionCost)}</p>
                        <p><strong>Balanced Journal Equation:</strong> {costing.costVariance === 0 ? "Balanced (0.00 variance)" : `Variance: ${formatCurrencyPkr(costing.costVariance)}`}</p>
                      </div>
                    );
                  })()}
                </div>
                <button type="submit" className="btn btn-primary" style={{ marginTop: "1.5rem" }}>
                  Complete Work Order & Capitalize FG
                </button>
              </form>
            ) : (
              <p>Please select a work order first.</p>
            )}
          </div>
        </div>
      )}

      {/* ── BOM TAB ──────────────────────────────────────────────────── */}
      {activeTab === "boms" && (
        <div className="tab-pane">
          <div className="split-view">
            <div className="left-pane">
              <h3>Bills of Materials (BOM)</h3>
              <table className="data-table">
                <thead>
                  <tr>
                    <th>BOM Number</th>
                    <th>Name</th>
                    <th>Base Qty</th>
                    <th>Yield %</th>
                    <th>Revision</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {boms.map((b) => (
                    <tr key={b.id}>
                      <td><strong>{b.bomNumber}</strong></td>
                      <td>{b.name}</td>
                      <td>{b.baseQuantity}</td>
                      <td>{b.yieldPercent}%</td>
                      <td>{b.currentRevision}</td>
                      <td><span className={`status-pill status-${b.status}`}>{b.status}</span></td>
                    </tr>
                  ))}
                  {boms.length === 0 && (
                    <tr>
                      <td colSpan={6} className="text-center">No BOMs found.</td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
            <div className="right-pane">
              <div className="card">
                <h3>Create New Bill of Materials</h3>
                <form onSubmit={handleCreateBom}>
                  <div className="form-group">
                    <label>BOM Number</label>
                    <input type="text" required value={bomNumber} onChange={(e) => setBomNumber(e.target.value)} placeholder="BOM-PUMP-01" />
                  </div>
                  <div className="form-group">
                    <label>Product Variant ID</label>
                    <input type="text" required value={bomVariantId} onChange={(e) => setBomVariantId(e.target.value)} placeholder="UUID" />
                  </div>
                  <div className="form-group">
                    <label>BOM Name</label>
                    <input type="text" required value={bomName} onChange={(e) => setBomName(e.target.value)} placeholder="Hydraulic Pump Assembly" />
                  </div>
                  <div className="form-row">
                    <div className="form-group">
                      <label>Yield %</label>
                      <input type="number" step="0.1" min="1" max="100" required value={bomYield} onChange={(e) => setBomYield(e.target.value)} />
                    </div>
                    <div className="form-group">
                      <label>Initial Revision</label>
                      <input type="text" required value={bomRevisionCode} onChange={(e) => setBomRevisionCode(e.target.value)} />
                    </div>
                  </div>
                  <h4 style={{ marginTop: "1rem" }}>Initial Component Line</h4>
                  <div className="form-group">
                    <label>Component Variant ID</label>
                    <input type="text" required value={bomItemVariantId} onChange={(e) => setBomItemVariantId(e.target.value)} placeholder="UUID" />
                  </div>
                  <div className="form-row">
                    <div className="form-group">
                      <label>Quantity Per</label>
                      <input type="number" step="0.0001" min="0.0001" required value={bomItemQtyPer} onChange={(e) => setBomItemQtyPer(e.target.value)} />
                    </div>
                    <div className="form-group">
                      <label>Scrap %</label>
                      <input type="number" step="0.1" min="0" max="99" required value={bomItemScrap} onChange={(e) => setBomItemScrap(e.target.value)} />
                    </div>
                  </div>
                  <button type="submit" className="btn btn-primary" style={{ marginTop: "1rem" }}>
                    Save BOM & Initial Revision
                  </button>
                </form>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ── WORK CENTERS TAB ─────────────────────────────────────────── */}
      {activeTab === "work-centers" && (
        <div className="tab-pane">
          <div className="split-view">
            <div className="left-pane">
              <h3>Work Centers & Capacities</h3>
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Code</th>
                    <th>Name</th>
                    <th>Labor Rate</th>
                    <th>Overhead Rate</th>
                    <th>Daily Cap</th>
                    <th>Efficiency</th>
                  </tr>
                </thead>
                <tbody>
                  {workCenters.map((wc) => (
                    <tr key={wc.id}>
                      <td><strong>{wc.code}</strong></td>
                      <td>{wc.name}</td>
                      <td>{formatCurrencyPkr(wc.hourlyLaborRate)}/hr</td>
                      <td>{formatCurrencyPkr(wc.hourlyOverheadRate)}/hr</td>
                      <td>{wc.dailyCapacityHours} hrs</td>
                      <td>{wc.efficiencyPercent}%</td>
                    </tr>
                  ))}
                  {workCenters.length === 0 && (
                    <tr>
                      <td colSpan={6} className="text-center">No work centers registered yet.</td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
            <div className="right-pane">
              <div className="card">
                <h3>Add Work Center</h3>
                <form onSubmit={handleCreateWorkCenter}>
                  <div className="form-group">
                    <label>Code</label>
                    <input type="text" required value={wcCode} onChange={(e) => setWcCode(e.target.value)} placeholder="WC-MACH-01" />
                  </div>
                  <div className="form-group">
                    <label>Name</label>
                    <input type="text" required value={wcName} onChange={(e) => setWcName(e.target.value)} placeholder="Machining Center 1" />
                  </div>
                  <div className="form-row">
                    <div className="form-group">
                      <label>Hourly Labor Rate (PKR)</label>
                      <input type="number" step="0.01" min="0" required value={wcLaborRate} onChange={(e) => setWcLaborRate(e.target.value)} />
                    </div>
                    <div className="form-group">
                      <label>Hourly Overhead Rate (PKR)</label>
                      <input type="number" step="0.01" min="0" required value={wcOverheadRate} onChange={(e) => setWcOverheadRate(e.target.value)} />
                    </div>
                  </div>
                  <div className="form-row">
                    <div className="form-group">
                      <label>Daily Capacity Hours</label>
                      <input type="number" step="0.5" min="1" max="24" required value={wcDailyCap} onChange={(e) => setWcDailyCap(e.target.value)} />
                    </div>
                    <div className="form-group">
                      <label>Efficiency %</label>
                      <input type="number" step="1" min="1" max="200" required value={wcEfficiency} onChange={(e) => setWcEfficiency(e.target.value)} />
                    </div>
                  </div>
                  <button type="submit" className="btn btn-primary" style={{ marginTop: "1rem" }}>
                    Create Work Center
                  </button>
                </form>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ── GENEALOGY TAB ─────────────────────────────────────────────── */}
      {activeTab === "genealogy" && (
        <div className="tab-pane">
          <div className="card">
            <h3>Component Genealogy & Traceability Report</h3>
            {genealogy ? (
              <div>
                <p><strong>Work Order:</strong> {genealogy.workOrderNumber}</p>
                <p><strong>Finished Good Variant ID:</strong> {genealogy.productVariantId}</p>
                <p><strong>Total Completed:</strong> {genealogy.completedQuantity} units</p>

                <h4 style={{ marginTop: "1.5rem" }}>Consumed Raw Materials & Source Layers</h4>
                {genealogy.consumedComponents.map((comp, idx) => (
                  <div key={idx} className="card" style={{ background: "#f8fafc", marginBottom: "1rem" }}>
                    <p><strong>Component Variant ID:</strong> {comp.componentVariantId}</p>
                    <p><strong>Total Quantity Consumed:</strong> {comp.consumedQuantity} EA</p>
                    <p><strong>Total Consumed Cost:</strong> {formatCurrencyPkr(comp.consumedCost)}</p>

                    <h5 style={{ marginTop: "0.5rem" }}>FIFO Layer Lineage:</h5>
                    <table className="data-table">
                      <thead>
                        <tr>
                          <th>Layer ID</th>
                          <th>Movement ID</th>
                          <th>Consumed Qty</th>
                          <th>Unit Cost</th>
                          <th>Extended Cost</th>
                          <th>Source Document</th>
                        </tr>
                      </thead>
                      <tbody>
                        {comp.layerSources.map((ls, sIdx) => (
                          <tr key={sIdx}>
                            <td>{ls.layerId.substring(0, 8)}...</td>
                            <td>{ls.movementId.substring(0, 8)}...</td>
                            <td>{ls.consumedQty}</td>
                            <td>{formatCurrencyPkr(ls.unitCost)}</td>
                            <td>{formatCurrencyPkr(ls.extendedCost)}</td>
                            <td>{ls.sourceDocumentType} ({ls.sourceDocumentId?.substring(0, 8) || "N/A"})</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ))}
              </div>
            ) : (
              <p>Select a completed work order from the Work Orders tab and click "Genealogy" to inspect source receipts.</p>
            )}
          </div>
        </div>
      )}

      {/* ── WIP & VARIANCE REPORT TAB ──────────────────────────────────── */}
      {activeTab === "wip-report" && (
        <div className="tab-pane">
          <div className="card">
            <h3>WIP & Manufacturing Cost Variance Summary</h3>
            {wipReport ? (
              <div>
                <div className="grid-4" style={{ marginBottom: "1.5rem" }}>
                  <div className="card stat-card">
                    <h4>Active Work Orders</h4>
                    <p className="stat-number">{wipReport.totalActiveWorkOrders}</p>
                  </div>
                  <div className="card stat-card">
                    <h4>Accumulated Material WIP</h4>
                    <p className="stat-number">{formatCurrencyPkr(wipReport.totalMaterialCost)}</p>
                  </div>
                  <div className="card stat-card">
                    <h4>Accumulated Labor WIP</h4>
                    <p className="stat-number">{formatCurrencyPkr(wipReport.totalLaborCost)}</p>
                  </div>
                  <div className="card stat-card">
                    <h4>Total Active WIP Value</h4>
                    <p className="stat-number" style={{ color: "#2563eb" }}>{formatCurrencyPkr(wipReport.totalAccumulatedWip)}</p>
                  </div>
                </div>

                <h4>Active Work Order WIP Breakdown</h4>
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>WO Number</th>
                      <th>Status</th>
                      <th>Planned Qty</th>
                      <th>Material Cost</th>
                      <th>Labor Cost</th>
                      <th>Overhead Cost</th>
                      <th>Total WIP</th>
                    </tr>
                  </thead>
                  <tbody>
                    {wipReport.workOrders.map((wo) => (
                      <tr key={wo.workOrderId}>
                        <td><strong>{wo.workOrderNumber}</strong></td>
                        <td><span className={`status-pill status-${wo.status}`}>{wo.status}</span></td>
                        <td>{wo.plannedQuantity}</td>
                        <td>{formatCurrencyPkr(wo.materialCost)}</td>
                        <td>{formatCurrencyPkr(wo.laborCost)}</td>
                        <td>{formatCurrencyPkr(wo.overheadCost)}</td>
                        <td><strong>{formatCurrencyPkr(wo.totalWip)}</strong></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <p>No WIP data loaded.</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
