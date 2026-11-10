import { useEffect, useRef, useState, type FormEvent } from "react";
import { apiFetch, type ApiWorkspaceScope } from "./auth";

// ── Types ─────────────────────────────────────────────────────────────────────

type Supplier = {
  supplierId: string;
  supplierCode: string;
  name: string;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  currencyCode: string;
  paymentTermsDays: number;
  isActive: boolean;
};

type PoLine = {
  lineNumber: number;
  variantId: string;
  sku: string;
  orderedQty: number;
  receivedQty: number;
  uomCode: string;
  unitPrice: number;
  lineStatus: string;
};

type PurchaseOrder = {
  purchaseOrderId: string;
  poNumber: string;
  supplierId: string;
  supplierName: string;
  warehouseId: string;
  warehouseName: string;
  orderStatus: string;
  currencyCode: string;
  orderDate: string;
  expectedDate: string | null;
  note: string | null;
  lines: PoLine[];
};

type GrnLine = {
  lineNumber: number;
  poLineNumber: number;
  variantId: string;
  sku: string;
  binId: string;
  binCode: string;
  receivedQty: number;
  uomCode: string;
  unitPrice: number;
};

type Grn = {
  grnId: string;
  grnNumber: string;
  purchaseOrderId: string;
  poNumber: string;
  supplierName: string;
  warehouseName: string;
  grnStatus: string;
  receivedDate: string;
  supplierReference: string | null;
  note: string | null;
  postedAt: string | null;
  lines: GrnLine[];
};

// Inventory data — loaded by the purchasing workspace itself from the same API
type Variant = { variantId: string; sku: string; productName: string; isActive?: boolean; baseUnitOfMeasureId: string; baseUnitCode: string };
type Warehouse = { warehouseId: string; code: string; name: string; isActive: boolean };
type Bin = { binId: string; warehouseId: string; code: string; binType: string; isActive: boolean };

type Props = {
  enabled: boolean;
  workspace: ApiWorkspaceScope | null;
};

// ── Helpers ───────────────────────────────────────────────────────────────────

async function apiGet<T>(path: string, workspace: ApiWorkspaceScope | null): Promise<T> {
  const response = await apiFetch(path, {}, workspace);
  if (!response.ok) throw new Error(`Request failed (${response.status}).`);
  return (await response.json()) as T;
}

async function apiPost<T>(
  path: string,
  body: unknown,
  workspace: ApiWorkspaceScope | null
): Promise<T> {
  const response = await apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  }, workspace);
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { title?: string; code?: string };
    throw new Error(problem.title ?? problem.code ?? `Request failed (${response.status}).`);
  }
  return (await response.json()) as T;
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function PurchasingWorkspace({ enabled, workspace }: Props) {
  // State
  const [suppliers, setSuppliers] = useState<Supplier[]>([]);
  const [purchaseOrders, setPurchaseOrders] = useState<PurchaseOrder[]>([]);
  const [grns, setGrns] = useState<Grn[]>([]);
  const [variants, setVariants] = useState<Variant[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [bins, setBins] = useState<Bin[]>([]);
  const [message, setMessage] = useState("");
  const [isSaving, setIsSaving] = useState(false);
  const [activeTab, setActiveTab] = useState<"suppliers" | "orders" | "receive">("suppliers");
  const [reloadVersion, setReloadVersion] = useState(0);

  // Supplier form
  const [supCode, setSupCode] = useState("");
  const [supName, setSupName] = useState("");
  const [supContact, setSupContact] = useState("");
  const [supEmail, setSupEmail] = useState("");
  const [supPhone, setSupPhone] = useState("");
  const [supTerms, setSupTerms] = useState("30");

  // PO form
  const [poNumber, setPoNumber] = useState("");
  const [poSupplierId, setPoSupplierId] = useState("");
  const [poWarehouseId, setPoWarehouseId] = useState("");
  const [poExpected, setPoExpected] = useState("");
  const [poNote, setPoNote] = useState("");
  const [poLines, setPoLines] = useState<Array<{ variantId: string; qty: string; price: string }>>([
    { variantId: "", qty: "", price: "0" },
  ]);

  // GRN form
  const [grnNumber, setGrnNumber] = useState("");
  const [grnPoId, setGrnPoId] = useState("");
  const [grnSupRef, setGrnSupRef] = useState("");
  const [grnNote, setGrnNote] = useState("");
  const [grnLines, setGrnLines] = useState<Array<{ poLineNumber: string; binId: string; qty: string; price: string }>>([
    { poLineNumber: "", binId: "", qty: "", price: "0" },
  ]);

  const selectedPo = purchaseOrders.find((po) => po.purchaseOrderId === grnPoId) ?? null;
  const grnWarehouseId = selectedPo?.warehouseId ?? "";
  const binsForGrnWarehouse = bins.filter(
    (b) => b.warehouseId === grnWarehouseId && b.isActive !== false && (!b.binType || ["storage", "receiving"].includes(b.binType.toLowerCase()))
  );

  // Load data
  useEffect(() => {
    if (!enabled) return;
    let mounted = true;
    const load = async () => {
      try {
        const [s, po, g, vars, whs, b] = await Promise.all([
          apiGet<Supplier[]>("/api/v1/purchasing/suppliers", workspace),
          apiGet<PurchaseOrder[]>("/api/v1/purchasing/purchase-orders", workspace),
          apiGet<Grn[]>("/api/v1/purchasing/grns", workspace),
          apiGet<Variant[]>("/api/v1/inventory/variants", workspace).catch(() => []),
          apiGet<Warehouse[]>("/api/v1/inventory/warehouses", workspace).catch(() => []),
          apiGet<Bin[]>("/api/v1/inventory/bins", workspace).catch(() => []),
        ]);
        if (!mounted) return;
        setSuppliers(s);
        setPurchaseOrders(po);
        setGrns(g);
        setVariants(vars);
        setWarehouses(whs);
        setBins(b);
        if (!poSupplierId && s.length > 0) setPoSupplierId(s[0].supplierId);
        if (!poWarehouseId) {
          const first = whs.find((w) => w.isActive !== false);
          if (first) setPoWarehouseId(first.warehouseId);
        }
        if (!grnPoId && po.length > 0) {
          const firstConfirmed = po.find((p) => p.orderStatus === "confirmed" || p.orderStatus === "partially_received");
          setGrnPoId(firstConfirmed?.purchaseOrderId ?? po[0].purchaseOrderId);
        }
      } catch (err) {
        if (mounted) setMessage(err instanceof Error ? err.message : "Could not load purchasing data.");
      }
    };
    void load();
    return () => { mounted = false; };
  }, [enabled, workspace?.tenantId, workspace?.companyId, reloadVersion]);

  const reload = () => {
    setMessage("");
    setReloadVersion((v) => v + 1);
  };

  // Auto-populate default bin and line when a confirmed PO is selected for receiving
  useEffect(() => {
    if (!selectedPo) return;
    const unreceived = (selectedPo.lines ?? []).filter((l) => l.lineStatus !== "received" && l.lineStatus !== "cancelled");
    const defaultBin = binsForGrnWarehouse.find((b) => b.binType?.toLowerCase() === "receiving") ?? binsForGrnWarehouse[0];

    setGrnLines((prev) => {
      if (prev.length === 1 && (!prev[0].poLineNumber || prev[0].poLineNumber === "") && unreceived.length > 0) {
        const firstLine = unreceived[0];
        const remQty = firstLine.orderedQty - firstLine.receivedQty;
        return [{
          poLineNumber: String(firstLine.lineNumber),
          binId: defaultBin?.binId ?? "",
          qty: remQty > 0 ? String(remQty) : String(firstLine.orderedQty),
          price: String(firstLine.unitPrice ?? 0),
        }];
      }
      return prev.map((l) => ({
        ...l,
        binId: l.binId || (defaultBin?.binId ?? ""),
      }));
    });
  }, [grnPoId, selectedPo?.purchaseOrderId, binsForGrnWarehouse.length]);

  // ── Submit handlers ────────────────────────────────────────────────────────

  const submitSupplier = async (e: FormEvent) => {
    e.preventDefault();
    setIsSaving(true); setMessage("");
    try {
      await apiPost<Supplier>("/api/v1/purchasing/suppliers", {
        supplierCode: supCode,
        name: supName,
        contactName: supContact || null,
        email: supEmail || null,
        phone: supPhone || null,
        address: null,
        currencyCode: "PKR",
        paymentTermsDays: Number(supTerms),
      }, workspace);
      setSupCode(""); setSupName(""); setSupContact(""); setSupEmail(""); setSupPhone("");
      setMessage("Supplier created.");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Supplier could not be saved.");
    } finally {
      setIsSaving(false);
    }
  };

  const submitPurchaseOrder = async (e: FormEvent) => {
    e.preventDefault();
    const validLines = poLines.filter((l) => l.variantId && Number(l.qty) > 0);
    if (validLines.length === 0) { setMessage("Add at least one line with a positive quantity."); return; }
    setIsSaving(true); setMessage("");
    try {
      const variant0 = variants.find((v) => v.variantId === validLines[0].variantId);
      await apiPost<PurchaseOrder>("/api/v1/purchasing/purchase-orders", {
        poNumber,
        supplierId: poSupplierId,
        warehouseId: poWarehouseId,
        orderDate: new Date().toISOString().slice(0, 10),
        expectedDate: poExpected || null,
        note: poNote || null,
        lines: validLines.map((l) => ({
          variantId: l.variantId,
          orderedQty: Number(l.qty),
          unitOfMeasureId: variants.find((v) => v.variantId === l.variantId)?.baseUnitOfMeasureId ?? variant0?.baseUnitOfMeasureId,
          unitPrice: Number(l.price),
        })),
      }, workspace);
      setPoNumber(""); setPoNote(""); setPoExpected("");
      setPoLines([{ variantId: "", qty: "", price: "0" }]);
      setMessage("Purchase order created in draft.");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Purchase order could not be saved.");
    } finally {
      setIsSaving(false);
    }
  };

  const confirmOrder = async (purchaseOrderId: string) => {
    setIsSaving(true); setMessage("");
    try {
      await apiPost<PurchaseOrder>(`/api/v1/purchasing/purchase-orders/${purchaseOrderId}/confirm`, {}, workspace);
      setMessage("Purchase order confirmed.");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Could not confirm order.");
    } finally {
      setIsSaving(false);
    }
  };

  const submitGrn = async (e: FormEvent) => {
    e.preventDefault();
    const validLines = grnLines.filter((l) => l.binId && l.poLineNumber && Number(l.qty) > 0);
    if (!selectedPo) { setMessage("Select a confirmed purchase order."); return; }
    if (validLines.length === 0) { setMessage("Add at least one line."); return; }
    setIsSaving(true); setMessage("");
    try {
      const lineDetails = validLines.map((l) => {
        const poLine = selectedPo.lines.find((pl) => pl.lineNumber === Number(l.poLineNumber));
        return {
          poLineNumber: Number(l.poLineNumber),
          variantId: poLine?.variantId ?? "",
          binId: l.binId,
          receivedQty: Number(l.qty),
          unitOfMeasureId: variants.find((v) => v.variantId === poLine?.variantId)?.baseUnitOfMeasureId ?? "",
          unitPrice: Number(l.price),
        };
      });
      await apiPost<Grn>("/api/v1/purchasing/grns", {
        clientTransactionId: crypto.randomUUID(),
        grnNumber,
        purchaseOrderId: grnPoId,
        receivedDate: new Date().toISOString().slice(0, 10),
        supplierReference: grnSupRef || null,
        note: grnNote || null,
        lines: lineDetails,
      }, workspace);
      setGrnNumber(""); setGrnSupRef(""); setGrnNote("");
      setGrnLines([{ poLineNumber: "", binId: "", qty: "", price: "0" }]);
      setMessage("Goods received note posted. Stock movement is pending inventory sync.");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "GRN could not be posted.");
    } finally {
      setIsSaving(false);
    }
  };

  const processOutbox = async () => {
    setIsSaving(true);
    setMessage("");
    try {
      const res = await apiPost<{ processedEvents: number; stockMovementsCreated: number; journalsPosted: number; details: string[] }>(
        "/api/v1/purchasing/outbox/process",
        {},
        workspace
      );
      setMessage(`Outbox processed: ${res.processedEvents} event(s), ${res.stockMovementsCreated} movement(s), ${res.journalsPosted} GL journal(s) posted.`);
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Outbox processing failed.");
    } finally {
      setIsSaving(false);
    }
  };

  const statusColor = (s: string) =>
    s === "confirmed" || s === "received" || s === "posted"
      ? "tag-progress"
      : s === "partially_received"
      ? "tag-starter"
      : "";

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <section className="inventory-workspace" aria-labelledby="purchasing-title">
      <div className="catalog-heading">
        <div>
          <p className="eyebrow">PURCHASING WORKFLOW</p>
          <h2 id="purchasing-title">Purchasing</h2>
        </div>
        <div style={{ display: "flex", gap: "0.5rem" }}>
          <button
            className={`secondary-button${activeTab === "suppliers" ? " active-tab" : ""}`}
            disabled={!enabled} onClick={() => setActiveTab("suppliers")} type="button"
          >Suppliers</button>
          <button
            className={`secondary-button${activeTab === "orders" ? " active-tab" : ""}`}
            disabled={!enabled} onClick={() => setActiveTab("orders")} type="button"
          >Orders</button>
          <button
            className={`secondary-button${activeTab === "receive" ? " active-tab" : ""}`}
            disabled={!enabled} onClick={() => setActiveTab("receive")} type="button"
          >Receive</button>
          <button className="secondary-button" disabled={!enabled || isSaving} onClick={processOutbox} type="button" title="Synchronize pending goods receipts into inventory balances and double-entry accounting journals">
            Sync Outbox to GL
          </button>
          <button className="secondary-button" disabled={!enabled || isSaving} onClick={reload} type="button">
            Refresh
          </button>
        </div>
      </div>
      <p className="catalog-note">
        Create suppliers, draft and confirm purchase orders, then post goods received notes.
        Each GRN raises an inventory event — the stock movement is posted when the event is processed.
      </p>
      {!enabled && (
        <p className="catalog-storage-note">API is offline or sign-in is unavailable. Purchasing is paused.</p>
      )}
      {message && <p className="catalog-message" role="status">{message}</p>}

      {/* ── Suppliers tab ─────────────────────────────────────────────── */}
      {activeTab === "suppliers" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>New supplier</h3>
            <form className="inventory-form" onSubmit={(e) => void submitSupplier(e)}>
              <label>Supplier code<input required maxLength={64} value={supCode} onChange={(e) => setSupCode(e.target.value)} placeholder="e.g. SUP-001" /></label>
              <label>Name<input required maxLength={200} value={supName} onChange={(e) => setSupName(e.target.value)} placeholder="Supplier company name" /></label>
              <label>Contact name<input maxLength={200} value={supContact} onChange={(e) => setSupContact(e.target.value)} placeholder="Optional" /></label>
              <label>Email<input type="email" maxLength={200} value={supEmail} onChange={(e) => setSupEmail(e.target.value)} placeholder="Optional" /></label>
              <label>Phone<input maxLength={50} value={supPhone} onChange={(e) => setSupPhone(e.target.value)} placeholder="Optional" /></label>
              <label>Payment terms (days)<input type="number" min="0" max="365" value={supTerms} onChange={(e) => setSupTerms(e.target.value)} /></label>
              <button disabled={!enabled || isSaving} type="submit">Create supplier</button>
            </form>
          </article>
          <article className="inventory-card">
            <h3>Suppliers · {suppliers.length}</h3>
            {suppliers.length === 0
              ? <p className="inventory-empty">No suppliers yet.</p>
              : suppliers.map((s) => (
                <div className="inventory-list-row" key={s.supplierId}>
                  <strong>{s.name}</strong>
                  <span>{s.supplierCode} · {s.currencyCode} · {s.paymentTermsDays} days</span>
                  {s.email && <small>{s.email}</small>}
                </div>
              ))}
          </article>
        </div>
      )}

      {/* ── Orders tab ────────────────────────────────────────────────── */}
      {activeTab === "orders" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>New purchase order</h3>
            <form className="inventory-form" onSubmit={(e) => void submitPurchaseOrder(e)}>
              <label>PO number<input required maxLength={64} value={poNumber} onChange={(e) => setPoNumber(e.target.value)} placeholder="e.g. PO-2024-001" /></label>
              <label>Supplier
                <select required value={poSupplierId} onChange={(e) => setPoSupplierId(e.target.value)}>
                  <option value="">Select supplier</option>
                  {suppliers.filter((s) => s.isActive).map((s) => (
                    <option key={s.supplierId} value={s.supplierId}>{s.supplierCode} · {s.name}</option>
                  ))}
                </select>
              </label>
              <label>Receiving warehouse
                <select required value={poWarehouseId} onChange={(e) => setPoWarehouseId(e.target.value)}>
                  <option value="">Select warehouse</option>
                  {warehouses.filter((w) => w.isActive !== false).map((w) => (
                    <option key={w.warehouseId} value={w.warehouseId}>{w.code} · {w.name}</option>
                  ))}
                </select>
              </label>
              <label>Expected delivery<input type="date" value={poExpected} onChange={(e) => setPoExpected(e.target.value)} /></label>
              <label>Note<input maxLength={2000} value={poNote} onChange={(e) => setPoNote(e.target.value)} placeholder="Optional" /></label>
              <h4 className="inventory-subheading">Order lines</h4>
              {poLines.map((line, idx) => (
                <div key={idx} style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap", marginBottom: "0.5rem" }}>
                  <select
                    value={line.variantId}
                    onChange={(e) => setPoLines(poLines.map((l, i) => i === idx ? { ...l, variantId: e.target.value } : l))}
                    style={{ flex: "2" }}
                  >
                    <option value="">Choose variant</option>
                    {variants.filter((v) => v.isActive !== false).map((v) => (
                      <option key={v.variantId} value={v.variantId}>{v.sku} · {v.productName}</option>
                    ))}
                  </select>
                  <input
                    type="number" min="0.001" step="0.001" placeholder="Qty"
                    value={line.qty}
                    onChange={(e) => setPoLines(poLines.map((l, i) => i === idx ? { ...l, qty: e.target.value } : l))}
                    style={{ flex: "1", minWidth: "80px" }}
                  />
                  <input
                    type="number" min="0" step="0.01" placeholder="Unit price"
                    value={line.price}
                    onChange={(e) => setPoLines(poLines.map((l, i) => i === idx ? { ...l, price: e.target.value } : l))}
                    style={{ flex: "1", minWidth: "90px" }}
                  />
                  {poLines.length > 1 && (
                    <button type="button" onClick={() => setPoLines(poLines.filter((_, i) => i !== idx))}>✕</button>
                  )}
                </div>
              ))}
              <button type="button" className="secondary-button" onClick={() => setPoLines([...poLines, { variantId: "", qty: "", price: "0" }])}>
                + Add line
              </button>
              <button disabled={!enabled || isSaving || !poSupplierId || !poWarehouseId} type="submit">
                Create draft PO
              </button>
            </form>
          </article>
          <article className="inventory-card">
            <h3>Purchase orders · {purchaseOrders.length}</h3>
            {purchaseOrders.length === 0
              ? <p className="inventory-empty">No purchase orders yet.</p>
              : purchaseOrders.map((po) => (
                <div key={po.purchaseOrderId} style={{ borderBottom: "1px solid var(--border)", paddingBottom: "0.75rem", marginBottom: "0.75rem" }}>
                  <div className="inventory-list-row">
                    <strong>{po.poNumber}</strong>
                    <span className={`tag ${statusColor(po.orderStatus)}`}>{po.orderStatus.replace("_", " ")}</span>
                  </div>
                  <small>{po.supplierName} → {po.warehouseName}</small>
                  {po.orderStatus === "draft" && (
                    <button
                      className="secondary-button" style={{ marginTop: "0.4rem", fontSize: "0.75rem" }}
                      disabled={isSaving}
                      onClick={() => void confirmOrder(po.purchaseOrderId)}
                      type="button"
                    >Confirm order</button>
                  )}
                  {po.lines.map((l) => (
                    <div key={l.lineNumber} style={{ fontSize: "0.8rem", color: "var(--text-muted)", paddingLeft: "0.5rem" }}>
                      Line {l.lineNumber}: {l.sku} — ordered {l.orderedQty} {l.uomCode}, received {l.receivedQty}
                    </div>
                  ))}
                </div>
              ))}
          </article>
        </div>
      )}

      {/* ── Receive tab ───────────────────────────────────────────────── */}
      {activeTab === "receive" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>Post goods received note</h3>
            <form className="inventory-form" onSubmit={(e) => void submitGrn(e)}>
              <label>GRN number<input required maxLength={64} value={grnNumber} onChange={(e) => setGrnNumber(e.target.value)} placeholder="e.g. GRN-2024-001" /></label>
              <label>Purchase order
                <select required value={grnPoId} onChange={(e) => setGrnPoId(e.target.value)}>
                  <option value="">Select confirmed PO</option>
                  {purchaseOrders
                    .filter((po) => po.orderStatus === "confirmed" || po.orderStatus === "partially_received")
                    .map((po) => (
                      <option key={po.purchaseOrderId} value={po.purchaseOrderId}>
                        {po.poNumber} · {po.supplierName}
                      </option>
                    ))}
                </select>
              </label>
              <label>Supplier reference<input maxLength={200} value={grnSupRef} onChange={(e) => setGrnSupRef(e.target.value)} placeholder="Supplier invoice / delivery note" /></label>
              <label>Note<input maxLength={2000} value={grnNote} onChange={(e) => setGrnNote(e.target.value)} placeholder="Optional" /></label>

              <h4 className="inventory-subheading">Received items</h4>
              {grnLines.map((line, idx) => (
                <div key={idx} style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap", marginBottom: "0.5rem" }}>
                  <select
                    value={line.poLineNumber}
                    onChange={(e) => setGrnLines(grnLines.map((l, i) => i === idx ? { ...l, poLineNumber: e.target.value } : l))}
                    style={{ flex: "1" }}
                  >
                    <option value="">PO line</option>
                    {(selectedPo?.lines ?? []).filter((l) => l.lineStatus !== "received" && l.lineStatus !== "cancelled").map((l) => (
                      <option key={l.lineNumber} value={l.lineNumber}>{l.lineNumber}: {l.sku}</option>
                    ))}
                  </select>
                  <select
                    value={line.binId}
                    onChange={(e) => setGrnLines(grnLines.map((l, i) => i === idx ? { ...l, binId: e.target.value } : l))}
                    style={{ flex: "1" }}
                  >
                    <option value="">Bin</option>
                    {binsForGrnWarehouse.map((b) => (
                      <option key={b.binId} value={b.binId}>{b.code}</option>
                    ))}
                  </select>
                  <input
                    type="number" min="0.001" step="0.001" placeholder="Qty"
                    value={line.qty}
                    onChange={(e) => setGrnLines(grnLines.map((l, i) => i === idx ? { ...l, qty: e.target.value } : l))}
                    style={{ flex: "1", minWidth: "80px" }}
                  />
                  <input
                    type="number" min="0" step="0.01" placeholder="Unit price"
                    value={line.price}
                    onChange={(e) => setGrnLines(grnLines.map((l, i) => i === idx ? { ...l, price: e.target.value } : l))}
                    style={{ flex: "1", minWidth: "90px" }}
                  />
                  {grnLines.length > 1 && (
                    <button type="button" onClick={() => setGrnLines(grnLines.filter((_, i) => i !== idx))}>✕</button>
                  )}
                </div>
              ))}
              <button type="button" className="secondary-button" onClick={() => setGrnLines([...grnLines, { poLineNumber: "", binId: "", qty: "", price: "0" }])}>
                + Add line
              </button>
              <button disabled={!enabled || isSaving || !grnPoId || !grnNumber} type="submit">
                {isSaving ? "Posting…" : "Post GRN"}
              </button>
            </form>
            <p className="inventory-footnote">
              Each posted GRN raises an outbox event. The inventory movement is applied when that event is processed by the inventory module.
            </p>
          </article>

          <article className="inventory-card">
            <h3>Posted GRNs · {grns.length}</h3>
            {grns.length === 0
              ? <p className="inventory-empty">No goods received notes yet.</p>
              : grns.map((g) => (
                <div key={g.grnId} style={{ borderBottom: "1px solid var(--border)", paddingBottom: "0.75rem", marginBottom: "0.75rem" }}>
                  <div className="inventory-list-row">
                    <strong>{g.grnNumber}</strong>
                    <span className={`tag ${statusColor(g.grnStatus)}`}>{g.grnStatus}</span>
                  </div>
                  <small>PO: {g.poNumber} · {g.supplierName} → {g.warehouseName}</small>
                  {g.lines.map((l) => (
                    <div key={l.lineNumber} style={{ fontSize: "0.8rem", color: "var(--text-muted)", paddingLeft: "0.5rem" }}>
                      {l.sku} · {l.receivedQty} {l.uomCode} → bin {l.binCode}
                    </div>
                  ))}
                </div>
              ))}
          </article>
        </div>
      )}
    </section>
  );
}
