import { useEffect, useRef, useState, type FormEvent } from "react";
import { apiFetch, type ApiWorkspaceScope } from "./auth";
import {
  buildAvailabilityUrl,
  getAvailableStock,
  parseStrictNumber,
  validatePosCheckout,
  type PosLineDraft,
  type StockBalance,
} from "./posHelpers";

// ── Types ─────────────────────────────────────────────────────────────────────

type Customer = {
  customerId: string;
  customerCode: string;
  name: string;
  customerType: string;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  ntnNumber: string | null;
  strnNumber: string | null;
  creditLimit: number;
  outstandingBalance: number;
  currencyCode: string;
  paymentTermsDays: number;
  isActive: boolean;
};

type SalesOrderLine = {
  lineNumber: number;
  variantId: string;
  sku: string;
  binId: string;
  binCode: string;
  orderedQty: number;
  fulfilledQty: number;
  uomCode: string;
  unitPrice: number;
  taxRate: number;
  lineTotal: number;
  lineStatus: string;
};

type SalesOrder = {
  orderId: string;
  orderNumber: string;
  customerId: string;
  customerName: string;
  warehouseId: string;
  warehouseName: string;
  orderType: string;
  orderStatus: string;
  currencyCode: string;
  orderDate: string;
  subtotalAmount: number;
  taxAmount: number;
  discountAmount: number;
  totalAmount: number;
  note: string | null;
  lines: SalesOrderLine[];
};

type SalesInvoiceLine = {
  lineNumber: number;
  orderLineNumber: number | null;
  variantId: string;
  sku: string;
  warehouseId: string;
  warehouseCode: string;
  binId: string;
  binCode: string;
  quantity: number;
  uomCode: string;
  unitPrice: number;
  unitCost: number | null;
  taxRate: number;
  lineTotal: number;
};

type SalesInvoice = {
  invoiceId: string;
  invoiceNumber: string;
  clientTransactionId: string;
  orderId: string | null;
  customerId: string;
  customerName: string;
  paymentMethod: string;
  invoiceStatus: string;
  invoiceDate: string;
  subtotalAmount: number;
  taxAmount: number;
  discountAmount: number;
  totalAmount: number;
  paidAmount: number;
  note: string | null;
  postedAt: string;
  lines: SalesInvoiceLine[];
};

type Variant = {
  variantId: string;
  sku: string;
  productName: string;
  isActive?: boolean;
  baseUnitOfMeasureId: string;
  baseUnitCode: string;
};

type Warehouse = {
  warehouseId: string;
  code: string;
  name: string;
  isActive: boolean;
};

type Bin = {
  binId: string;
  warehouseId: string;
  code: string;
  binType: string;
  isActive: boolean;
};

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
  workspace: ApiWorkspaceScope | null,
  headers?: Record<string, string>
): Promise<T> {
  const response = await apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...(headers ?? {}) },
    body: JSON.stringify(body),
  }, workspace);
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { title?: string; code?: string; detail?: string };
    throw new Error(problem.detail ?? problem.title ?? problem.code ?? `Request failed (${response.status}).`);
  }
  return (await response.json()) as T;
}

export default function SalesWorkspace({ enabled, workspace }: Props) {
  // State
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [orders, setOrders] = useState<SalesOrder[]>([]);
  const [invoices, setInvoices] = useState<SalesInvoice[]>([]);
  const [variants, setVariants] = useState<Variant[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [bins, setBins] = useState<Bin[]>([]);
  const [balances, setBalances] = useState<StockBalance[]>([]);
  const [message, setMessage] = useState("");
  const [isSaving, setIsSaving] = useState(false);
  const [activeTab, setActiveTab] = useState<"pos" | "orders" | "customers" | "invoices">("pos");
  const [reloadVersion, setReloadVersion] = useState(0);

  // POS Form State
  const [posCustomerId, setPosCustomerId] = useState("");
  const [posWarehouseId, setPosWarehouseId] = useState("");
  const [posBinId, setPosBinId] = useState("");
  const [posPaymentMethod, setPosPaymentMethod] = useState("cash");
  const [posDiscount, setPosDiscount] = useState("0");
  const [posNote, setPosNote] = useState("");
  const [posLines, setPosLines] = useState<PosLineDraft[]>([
    { variantId: "", qty: "1", price: "", taxRate: "0" },
  ]);
  const [isLoadingAvailability, setIsLoadingAvailability] = useState(false);

  // Customer Form State
  const [custCode, setCustCode] = useState("");
  const [custName, setCustName] = useState("");
  const [custType, setCustType] = useState("retail");
  const [custContact, setCustContact] = useState("");
  const [custEmail, setCustEmail] = useState("");
  const [custPhone, setCustPhone] = useState("");
  const [custCreditLimit, setCustCreditLimit] = useState("0");
  const [custTermsDays, setCustTermsDays] = useState("0");

  // Sales Order Form State
  const [soNumber, setSoNumber] = useState("");
  const [soCustomerId, setSoCustomerId] = useState("");
  const [soWarehouseId, setSoWarehouseId] = useState("");
  const [soNote, setSoNote] = useState("");
  const [soLines, setSoLines] = useState<Array<{
    variantId: string;
    binId: string;
    qty: string;
    price: string;
    taxRate: string;
  }>>([{ variantId: "", binId: "", qty: "1", price: "0", taxRate: "0" }]);

  // Load Data
  useEffect(() => {
    if (!enabled) return;
    let mounted = true;
    const load = async () => {
      try {
        const [custData, orderData, invData] = await Promise.all([
          apiGet<Customer[]>("/api/v1/sales/customers", workspace).catch(() => []),
          apiGet<SalesOrder[]>("/api/v1/sales/orders", workspace).catch(() => []),
          apiGet<SalesInvoice[]>("/api/v1/sales/invoices", workspace).catch(() => []),
        ]);
        if (!mounted) return;
        setCustomers(custData);
        setOrders(orderData);
        setInvoices(invData);

        // Pre-select Walk-in customer if available
        if (custData.length > 0 && !posCustomerId) {
          const walkIn = custData.find((c) => c.customerCode === "CUST-WALKIN") ?? custData[0];
          setPosCustomerId(walkIn.customerId);
        }
      } catch (err) {
        if (mounted) setMessage(err instanceof Error ? err.message : "Failed loading sales data.");
      }

      // Load inventory metadata
      try {
        const [varData, whData, binData, balData] = await Promise.all([
          apiGet<Variant[]>("/api/v1/inventory/variants", workspace).catch(() => []),
          apiGet<Warehouse[]>("/api/v1/inventory/warehouses", workspace).catch(() => []),
          apiGet<Bin[]>("/api/v1/inventory/bins", workspace).catch(() => []),
          apiGet<StockBalance[]>(buildAvailabilityUrl(), workspace).catch(() => []),
        ]);
        if (!mounted) return;
        setVariants(varData);
        setWarehouses(whData);
        setBins(binData);
        setBalances(balData);

        if (whData.length > 0 && !posWarehouseId) {
          setPosWarehouseId(whData[0].warehouseId);
        }
      } catch {
        // optional inventory data
      }
    };

    void load();
    return () => { mounted = false; };
  }, [enabled, workspace, reloadVersion]);

  // Auto-set default bin when warehouse changes
  useEffect(() => {
    if (posWarehouseId && bins.length > 0) {
      const match = bins.find((b) => b.warehouseId === posWarehouseId && b.isActive && b.binType === "storage")
        ?? bins.find((b) => b.warehouseId === posWarehouseId && b.isActive);
      if (match) setPosBinId(match.binId);
    }
  }, [posWarehouseId, bins]);

  // Refresh live bin availability whenever warehouse, bin, or selected variants change
  const selectedPosVariantsKey = posLines.map((l) => l.variantId).join(",");
  useEffect(() => {
    if (!enabled || !posWarehouseId) return;
    let active = true;
    setIsLoadingAvailability(true);
    apiGet<StockBalance[]>(buildAvailabilityUrl(posWarehouseId), workspace)
      .then((fresh) => {
        if (active) setBalances(fresh);
      })
      .catch(() => {
        // Keep last known balances on transient network error
      })
      .finally(() => {
        if (active) setIsLoadingAvailability(false);
      });
    return () => {
      active = false;
    };
  }, [enabled, workspace, posWarehouseId, posBinId, selectedPosVariantsKey, reloadVersion]);

  // Calculations & Strict Validation for POS checkout (server computes FIFO COGS)
  const {
    subtotal: posSubtotal,
    taxTotal: posTaxTotal,
    discountNum: posDiscountNum,
    netPayable: posNetPayable,
    validationError: posValidationError,
  } = validatePosCheckout({
    posCustomerId,
    posWarehouseId,
    posBinId,
    posLines,
    posDiscount,
    balances,
    variants,
    isLoadingAvailability,
  });

  // Handlers
  const handleCreateCustomer = async (e: FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    setMessage("");
    try {
      await apiPost("/api/v1/sales/customers", {
        customerCode: custCode,
        name: custName,
        customerType: custType,
        contactName: custContact || null,
        email: custEmail || null,
        phone: custPhone || null,
        creditLimit: parseFloat(custCreditLimit) || 0,
        paymentTermsDays: parseInt(custTermsDays, 10) || 0,
        currencyCode: "PKR",
      }, workspace);
      setMessage(`Customer "${custName}" created successfully.`);
      setCustCode("");
      setCustName("");
      setCustContact("");
      setCustEmail("");
      setCustPhone("");
      setReloadVersion((v) => v + 1);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Failed to create customer.");
    } finally {
      setIsSaving(false);
    }
  };

  const handleCreateSalesOrder = async (e: FormEvent) => {
    e.preventDefault();
    if (!soCustomerId || !soWarehouseId) return;
    setIsSaving(true);
    setMessage("");
    try {
      const formattedLines = soLines.map((l) => {
        const variant = variants.find((v) => v.variantId === l.variantId);
        return {
          variantId: l.variantId,
          binId: l.binId || posBinId,
          orderedQty: parseFloat(l.qty) || 1,
          unitOfMeasureId: variant?.baseUnitOfMeasureId ?? "00000000-0000-0000-0000-000000000000",
          unitPrice: parseFloat(l.price) || 0,
          taxRate: (parseFloat(l.taxRate) || 0) / 100,
        };
      });

      await apiPost("/api/v1/sales/orders", {
        orderNumber: soNumber,
        customerId: soCustomerId,
        warehouseId: soWarehouseId,
        note: soNote || null,
        lines: formattedLines,
      }, workspace);

      setMessage(`Sales Order ${soNumber} created in draft.`);
      setSoNumber("");
      setSoNote("");
      setSoLines([{ variantId: "", binId: "", qty: "1", price: "0", taxRate: "0" }]);
      setReloadVersion((v) => v + 1);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Failed to create sales order.");
    } finally {
      setIsSaving(false);
    }
  };

  const handleConfirmOrder = async (orderId: string) => {
    setIsSaving(true);
    setMessage("");
    try {
      await apiPost(`/api/v1/sales/orders/${orderId}/confirm`, {}, workspace);
      setMessage("Sales Order confirmed successfully.");
      setReloadVersion((v) => v + 1);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Failed to confirm sales order.");
    } finally {
      setIsSaving(false);
    }
  };

  const handlePostPosSale = async (e: FormEvent) => {
    e.preventDefault();
    if (posValidationError) {
      setMessage(posValidationError);
      return;
    }

    const discountAmount = parseStrictNumber(posDiscount);
    if (discountAmount === null || discountAmount < 0) {
      setMessage("Discount (PKR) must be a valid non-negative number.");
      return;
    }

    setIsSaving(true);
    setMessage("");

    try {
      const clientTxId = crypto.randomUUID();
      const invoiceNumber = `POS-${Date.now().toString(36).toUpperCase()}`;

      const formattedLines = posLines.map((l) => {
        const variant = variants.find((v) => v.variantId === l.variantId);
        const qty = parseStrictNumber(l.qty);
        const price = parseStrictNumber(l.price);
        const tax = parseStrictNumber(l.taxRate);
        if (qty === null || qty <= 0 || price === null || price <= 0 || tax === null || tax < 0) {
          throw new Error("Invalid line values: positive quantity and selling price are required.");
        }
        return {
          variantId: l.variantId,
          warehouseId: posWarehouseId,
          binId: posBinId,
          quantity: qty,
          unitOfMeasureId: variant?.baseUnitOfMeasureId ?? "00000000-0000-0000-0000-000000000000",
          unitPrice: price,
          taxRate: tax / 100,
        };
      });

      const posted = await apiPost<SalesInvoice>("/api/v1/sales/invoices", {
        clientTransactionId: clientTxId,
        invoiceNumber,
        customerId: posCustomerId,
        paymentMethod: posPaymentMethod,
        discountAmount,
        note: posNote || null,
        lines: formattedLines,
      }, workspace, { "Idempotency-Key": clientTxId });

      setMessage(`Sale completed! Invoice #${posted.invoiceNumber} posted (${posted.paymentMethod.toUpperCase()}: PKR ${posted.totalAmount.toLocaleString()}). Inventory relieved & General Ledger balanced.`);
      setPosLines([{ variantId: "", qty: "1", price: "", taxRate: "0" }]);
      setPosNote("");
      setReloadVersion((v) => v + 1);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "POS transaction failed.");
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <section className="catalog" aria-labelledby="sales-title" style={{ marginTop: "2rem" }}>
      <div className="catalog-heading">
        <div>
          <p className="eyebrow">REVENUE · POINT OF SALE & ORDERS</p>
          <h2 id="sales-title">Sales & Point of Sale (POS)</h2>
        </div>
        <span className="tag tag-progress">Phase 4 Active</span>
      </div>

      <p className="catalog-note">
        Real-time retail counter and wholesale distribution. Automatically relieves on-hand inventory, enforces stock balance limits, updates customer balances, and posts balanced double-entry GL revenue &amp; COGS journals.
      </p>

      {/* Workspace Sub-tabs */}
      <div className="inventory-tabs" role="tablist">
        <button
          className={`inventory-tab ${activeTab === "pos" ? "active" : ""}`}
          onClick={() => setActiveTab("pos")}
          type="button"
          role="tab"
          aria-selected={activeTab === "pos"}
        >
          🛒 Point of Sale (POS)
        </button>
        <button
          className={`inventory-tab ${activeTab === "orders" ? "active" : ""}`}
          onClick={() => setActiveTab("orders")}
          type="button"
          role="tab"
          aria-selected={activeTab === "orders"}
        >
          📋 Sales Orders ({orders.length})
        </button>
        <button
          className={`inventory-tab ${activeTab === "customers" ? "active" : ""}`}
          onClick={() => setActiveTab("customers")}
          type="button"
          role="tab"
          aria-selected={activeTab === "customers"}
        >
          👥 Customers ({customers.length})
        </button>
        <button
          className={`inventory-tab ${activeTab === "invoices" ? "active" : ""}`}
          onClick={() => setActiveTab("invoices")}
          type="button"
          role="tab"
          aria-selected={activeTab === "invoices"}
        >
          🧾 Invoices Ledger ({invoices.length})
        </button>
      </div>

      {message && <p className="catalog-message" role="status" style={{ margin: "1rem 0" }}>{message}</p>}

      {/* ── TAB 1: POINT OF SALE (POS) ────────────────────────────────────── */}
      {activeTab === "pos" && (
        <div className="inventory-grid" style={{ display: "grid", gridTemplateColumns: "1.2fr 1fr", gap: "1.5rem" }}>
          {/* Checkout Register Form */}
          <article className="inventory-card">
            <h3>Retail Checkout Register</h3>
            <form className="inventory-form" onSubmit={(e) => void handlePostPosSale(e)}>
              <div style={{ display: "flex", gap: "1rem", flexWrap: "wrap" }}>
                <label style={{ flex: "1" }}>
                  Customer
                  <select required value={posCustomerId} onChange={(e) => setPosCustomerId(e.target.value)}>
                    <option value="">Select customer</option>
                    {customers.filter((c) => c.isActive).map((c) => (
                      <option key={c.customerId} value={c.customerId}>
                        {c.customerCode} · {c.name} {c.customerType === "retail" ? "(Walk-in)" : `(Bal: Rs ${c.outstandingBalance})`}
                      </option>
                    ))}
                  </select>
                </label>

                <label style={{ flex: "1" }}>
                  Payment Method
                  <select value={posPaymentMethod} onChange={(e) => setPosPaymentMethod(e.target.value)}>
                    <option value="cash">💵 Cash Counter</option>
                    <option value="card">💳 Card / POS Machine</option>
                    <option value="bank_transfer">🏛 Bank Transfer (IBFT)</option>
                    <option value="mobile_wallet">📱 Mobile Wallet (Easypaisa/JazzCash)</option>
                    <option value="credit">📑 Customer Credit Account</option>
                  </select>
                </label>
              </div>

              <div style={{ display: "flex", gap: "1rem", flexWrap: "wrap", marginTop: "0.5rem" }}>
                <label style={{ flex: "1" }}>
                  Dispatch Warehouse
                  <select required value={posWarehouseId} onChange={(e) => setPosWarehouseId(e.target.value)}>
                    <option value="">Select warehouse</option>
                    {warehouses.filter((w) => w.isActive).map((w) => (
                      <option key={w.warehouseId} value={w.warehouseId}>{w.code} · {w.name}</option>
                    ))}
                  </select>
                </label>

                <label style={{ flex: "1" }}>
                  Dispatch Bin
                  <select required value={posBinId} onChange={(e) => setPosBinId(e.target.value)}>
                    <option value="">Select storage bin</option>
                    {bins.filter((b) => b.warehouseId === posWarehouseId && b.isActive).map((b) => (
                      <option key={b.binId} value={b.binId}>{b.code} ({b.binType})</option>
                    ))}
                  </select>
                </label>
              </div>

              <h4 className="inventory-subheading" style={{ marginTop: "1rem" }}>Cart Items</h4>
              {posLines.map((line, idx) => {
                const avail = line.variantId && posWarehouseId && posBinId
                  ? getAvailableStock(balances, line.variantId, posWarehouseId, posBinId)
                  : null;
                const reqQty = parseStrictNumber(line.qty) ?? 0;
                const isInsufficient = avail !== null && (avail <= 0 || reqQty > avail);
                return (
                  <div key={idx} style={{ border: "1px solid var(--border)", padding: "0.75rem", borderRadius: "6px", marginBottom: "0.75rem" }}>
                    <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
                      <select
                        required
                        value={line.variantId}
                        onChange={(e) => {
                          const vId = e.target.value;
                          setPosLines(posLines.map((l, i) => i === idx ? { ...l, variantId: vId } : l));
                        }}
                        style={{ flex: "2", minWidth: "180px" }}
                      >
                        <option value="">Select product variant</option>
                        {variants.filter((v) => v.isActive).map((v) => (
                          <option key={v.variantId} value={v.variantId}>
                            {v.sku} · {v.productName}
                          </option>
                        ))}
                      </select>

                      <input
                        type="number" min="0.001" step="0.001" placeholder="Qty" required
                        value={line.qty}
                        onChange={(e) => setPosLines(posLines.map((l, i) => i === idx ? { ...l, qty: e.target.value } : l))}
                        style={{ flex: "1", minWidth: "70px" }}
                      />

                      <input
                        type="number" min="0.01" step="0.01" placeholder="Price (PKR)" required
                        value={line.price}
                        onChange={(e) => setPosLines(posLines.map((l, i) => i === idx ? { ...l, price: e.target.value } : l))}
                        style={{ flex: "1", minWidth: "90px" }}
                      />

                      <input
                        type="number" min="0" max="100" step="0.1" placeholder="Tax %" required
                        value={line.taxRate}
                        onChange={(e) => setPosLines(posLines.map((l, i) => i === idx ? { ...l, taxRate: e.target.value } : l))}
                        style={{ flex: "0.8", minWidth: "60px" }}
                      />

                      {posLines.length > 1 && (
                        <button type="button" onClick={() => setPosLines(posLines.filter((_, i) => i !== idx))}>✕</button>
                      )}
                    </div>
                    {avail !== null && (
                      <div style={{ fontSize: "0.75rem", marginTop: "0.25rem", color: isInsufficient && !isLoadingAvailability ? "var(--color-danger, #d32f2f)" : "var(--text-muted)" }}>
                        Available in Bin: <strong>{isLoadingAvailability ? "Refreshing…" : `${avail} EA`}</strong>{" "}
                        {!isLoadingAvailability && isInsufficient && "(Insufficient stock for requested quantity!)"}
                      </div>
                    )}
                  </div>
                );
              })}

              <button
                type="button"
                className="secondary-button"
                style={{ marginBottom: "1rem" }}
                onClick={() => setPosLines([...posLines, { variantId: "", qty: "1", price: "", taxRate: "0" }])}
              >
                + Add item line
              </button>

              <div style={{ display: "flex", gap: "1rem", flexWrap: "wrap" }}>
                <label style={{ flex: "1" }}>
                  Discount (PKR)
                  <input
                    type="number" min="0" step="1" required
                    value={posDiscount}
                    onChange={(e) => setPosDiscount(e.target.value)}
                  />
                </label>
                <label style={{ flex: "2" }}>
                  Note / Reference
                  <input
                    maxLength={500}
                    placeholder="e.g. Counter order ref"
                    value={posNote}
                    onChange={(e) => setPosNote(e.target.value)}
                  />
                </label>
              </div>

              {posValidationError && (
                <div
                  role="alert"
                  style={{
                    marginTop: "0.75rem",
                    padding: "0.6rem 0.85rem",
                    borderRadius: "6px",
                    background: "rgba(211, 47, 47, 0.08)",
                    border: "1px solid rgba(211, 47, 47, 0.35)",
                    color: "#b71c1c",
                    fontSize: "0.85rem",
                  }}
                >
                  {posValidationError}
                </div>
              )}

              <button
                disabled={!enabled || isSaving || posLines.length === 0 || posValidationError !== null}
                type="submit"
                style={{ marginTop: "1rem", width: "100%", padding: "0.8rem", fontSize: "1.05rem" }}
              >
                {isSaving
                  ? "Posting Sale & Updating Ledger…"
                  : posNetPayable > 0
                    ? `Pay PKR ${posNetPayable.toLocaleString()} & Complete Sale`
                    : "Enter Valid Price & Positive Total to Checkout"}
              </button>
            </form>
          </article>

          {/* POS Bill Summary & Recent Transactions */}
          <div>
            <article className="inventory-card" style={{ marginBottom: "1.5rem" }}>
              <h3>Current Receipt Summary</h3>
              <div style={{ fontSize: "0.9rem", lineHeight: "1.8" }}>
                <div style={{ display: "flex", justifyContent: "space-between" }}>
                  <span>Gross Subtotal:</span>
                  <strong>PKR {posSubtotal.toLocaleString(undefined, { minimumFractionDigits: 2 })}</strong>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between" }}>
                  <span>Sales Tax:</span>
                  <strong>PKR {posTaxTotal.toLocaleString(undefined, { minimumFractionDigits: 2 })}</strong>
                </div>
                {posDiscountNum > 0 && (
                  <div style={{ display: "flex", justifyContent: "space-between", color: "#e65100" }}>
                    <span>Discount:</span>
                    <span>- PKR {posDiscountNum.toLocaleString(undefined, { minimumFractionDigits: 2 })}</span>
                  </div>
                )}
                <div style={{ display: "flex", justifyContent: "space-between", borderTop: "2px solid var(--border)", paddingTop: "0.5rem", marginTop: "0.5rem", fontSize: "1.15rem" }}>
                  <span>Total Payable:</span>
                  <span style={{ color: "var(--color-primary, #0066cc)", fontWeight: "bold" }}>
                    PKR {posNetPayable.toLocaleString(undefined, { minimumFractionDigits: 2 })}
                  </span>
                </div>
              </div>
            </article>

            <article className="inventory-card">
              <h3>Recent POS Invoices ({invoices.slice(0, 5).length})</h3>
              {invoices.length === 0 ? (
                <p className="inventory-empty">No invoices recorded yet.</p>
              ) : (
                invoices.slice(0, 5).map((inv) => (
                  <div key={inv.invoiceId} style={{ borderBottom: "1px solid var(--border)", paddingBottom: "0.5rem", marginBottom: "0.5rem" }}>
                    <div className="inventory-list-row">
                      <strong>{inv.invoiceNumber}</strong>
                      <span className="tag tag-starter">{inv.paymentMethod.toUpperCase()}</span>
                    </div>
                    <small>{inv.customerName} · PKR {inv.totalAmount.toLocaleString()} · {inv.lines.length} lines</small>
                  </div>
                ))
              )}
            </article>
          </div>
        </div>
      )}

      {/* ── TAB 2: SALES ORDERS ───────────────────────────────────────────── */}
      {activeTab === "orders" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>New Sales Order</h3>
            <form className="inventory-form" onSubmit={(e) => void handleCreateSalesOrder(e)}>
              <label>
                Order number
                <input
                  required maxLength={64}
                  value={soNumber}
                  onChange={(e) => setSoNumber(e.target.value)}
                  placeholder="e.g. SO-2026-001"
                />
              </label>

              <label>
                Customer
                <select required value={soCustomerId} onChange={(e) => setSoCustomerId(e.target.value)}>
                  <option value="">Select customer</option>
                  {customers.filter((c) => c.isActive).map((c) => (
                    <option key={c.customerId} value={c.customerId}>{c.customerCode} · {c.name}</option>
                  ))}
                </select>
              </label>

              <label>
                Fulfillment Warehouse
                <select required value={soWarehouseId} onChange={(e) => setSoWarehouseId(e.target.value)}>
                  <option value="">Select warehouse</option>
                  {warehouses.filter((w) => w.isActive).map((w) => (
                    <option key={w.warehouseId} value={w.warehouseId}>{w.code} · {w.name}</option>
                  ))}
                </select>
              </label>

              <label>
                Note
                <input
                  maxLength={500}
                  placeholder="Optional delivery or shipping instructions"
                  value={soNote}
                  onChange={(e) => setSoNote(e.target.value)}
                />
              </label>

              <h4 className="inventory-subheading">Order Lines</h4>
              {soLines.map((line, idx) => (
                <div key={idx} style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap", marginBottom: "0.5rem" }}>
                  <select
                    required
                    value={line.variantId}
                    onChange={(e) => setSoLines(soLines.map((l, i) => i === idx ? { ...l, variantId: e.target.value } : l))}
                    style={{ flex: "2" }}
                  >
                    <option value="">Choose variant</option>
                    {variants.filter((v) => v.isActive).map((v) => (
                      <option key={v.variantId} value={v.variantId}>{v.sku} · {v.productName}</option>
                    ))}
                  </select>

                  <select
                    value={line.binId}
                    onChange={(e) => setSoLines(soLines.map((l, i) => i === idx ? { ...l, binId: e.target.value } : l))}
                    style={{ flex: "1.5" }}
                  >
                    <option value="">Bin</option>
                    {bins.filter((b) => b.warehouseId === soWarehouseId && b.isActive).map((b) => (
                      <option key={b.binId} value={b.binId}>{b.code}</option>
                    ))}
                  </select>

                  <input
                    type="number" min="0.001" step="0.001" placeholder="Qty" required
                    value={line.qty}
                    onChange={(e) => setSoLines(soLines.map((l, i) => i === idx ? { ...l, qty: e.target.value } : l))}
                    style={{ flex: "1", minWidth: "70px" }}
                  />

                  <input
                    type="number" min="0" step="0.01" placeholder="Unit price" required
                    value={line.price}
                    onChange={(e) => setSoLines(soLines.map((l, i) => i === idx ? { ...l, price: e.target.value } : l))}
                    style={{ flex: "1", minWidth: "80px" }}
                  />

                  {soLines.length > 1 && (
                    <button type="button" onClick={() => setSoLines(soLines.filter((_, i) => i !== idx))}>✕</button>
                  )}
                </div>
              ))}

              <button
                type="button"
                className="secondary-button"
                style={{ marginBottom: "1rem" }}
                onClick={() => setSoLines([...soLines, { variantId: "", binId: "", qty: "1", price: "0", taxRate: "0" }])}
              >
                + Add line
              </button>

              <button disabled={!enabled || isSaving || !soCustomerId || !soWarehouseId} type="submit">
                Create Draft Sales Order
              </button>
            </form>
          </article>

          <article className="inventory-card">
            <h3>Sales Orders · {orders.length}</h3>
            {orders.length === 0 ? (
              <p className="inventory-empty">No sales orders found.</p>
            ) : (
              orders.map((so) => (
                <div key={so.orderId} style={{ borderBottom: "1px solid var(--border)", paddingBottom: "0.75rem", marginBottom: "0.75rem" }}>
                  <div className="inventory-list-row">
                    <strong>{so.orderNumber}</strong>
                    <span className={`tag ${so.orderStatus === "confirmed" ? "tag-starter" : "tag-progress"}`}>
                      {so.orderStatus}
                    </span>
                  </div>
                  <small>{so.customerName} → {so.warehouseName} · PKR {so.totalAmount.toLocaleString()} ({so.orderDate})</small>
                  {so.orderStatus === "draft" && (
                    <div style={{ marginTop: "0.5rem" }}>
                      <button
                        className="secondary-button"
                        style={{ fontSize: "0.75rem" }}
                        disabled={isSaving}
                        onClick={() => void handleConfirmOrder(so.orderId)}
                        type="button"
                      >
                        ✓ Confirm Order
                      </button>
                    </div>
                  )}
                  {so.lines.map((l) => (
                    <div key={l.lineNumber} style={{ fontSize: "0.8rem", color: "var(--text-muted)", paddingLeft: "0.5rem", marginTop: "0.2rem" }}>
                      Line {l.lineNumber}: {l.sku} — ordered {l.orderedQty} {l.uomCode} @ PKR {l.unitPrice}
                    </div>
                  ))}
                </div>
              ))
            )}
          </article>
        </div>
      )}

      {/* ── TAB 3: CUSTOMERS & RECEIVABLES ───────────────────────────────── */}
      {activeTab === "customers" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>Add Customer</h3>
            <form className="inventory-form" onSubmit={(e) => void handleCreateCustomer(e)}>
              <label>
                Customer code
                <input
                  required maxLength={64}
                  value={custCode}
                  onChange={(e) => setCustCode(e.target.value)}
                  placeholder="e.g. CUST-002"
                />
              </label>

              <label>
                Customer / Business name
                <input
                  required maxLength={200}
                  value={custName}
                  onChange={(e) => setCustName(e.target.value)}
                  placeholder="e.g. Metro Department Store"
                />
              </label>

              <label>
                Customer type
                <select value={custType} onChange={(e) => setCustType(e.target.value)}>
                  <option value="retail">Retail</option>
                  <option value="wholesale">Wholesale</option>
                  <option value="distributor">Distributor</option>
                  <option value="corporate">Corporate</option>
                </select>
              </label>

              <label>
                Contact person
                <input maxLength={100} value={custContact} onChange={(e) => setCustContact(e.target.value)} placeholder="Optional" />
              </label>

              <label>
                Phone number
                <input maxLength={30} value={custPhone} onChange={(e) => setCustPhone(e.target.value)} placeholder="e.g. +92 300 1234567" />
              </label>

              <label>
                Email
                <input type="email" maxLength={100} value={custEmail} onChange={(e) => setCustEmail(e.target.value)} placeholder="Optional" />
              </label>

              <div style={{ display: "flex", gap: "1rem" }}>
                <label style={{ flex: "1" }}>
                  Credit limit (PKR)
                  <input
                    type="number" min="0" step="1000"
                    value={custCreditLimit}
                    onChange={(e) => setCustCreditLimit(e.target.value)}
                  />
                </label>
                <label style={{ flex: "1" }}>
                  Payment terms (days)
                  <input
                    type="number" min="0" max="365"
                    value={custTermsDays}
                    onChange={(e) => setCustTermsDays(e.target.value)}
                  />
                </label>
              </div>

              <button disabled={!enabled || isSaving} type="submit" style={{ marginTop: "1rem" }}>
                Save Customer
              </button>
            </form>
          </article>

          <article className="inventory-card">
            <h3>Customer Directory · {customers.length}</h3>
            {customers.length === 0 ? (
              <p className="inventory-empty">No customers registered yet.</p>
            ) : (
              customers.map((c) => (
                <div key={c.customerId} style={{ borderBottom: "1px solid var(--border)", paddingBottom: "0.75rem", marginBottom: "0.75rem" }}>
                  <div className="inventory-list-row">
                    <strong>{c.name}</strong>
                    <span className="tag">{c.customerType}</span>
                  </div>
                  <div style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
                    Code: <code>{c.customerCode}</code> · Credit Limit: PKR {c.creditLimit.toLocaleString()}
                  </div>
                  <div style={{ fontSize: "0.85rem", marginTop: "0.2rem" }}>
                    Outstanding Balance: <strong style={{ color: c.outstandingBalance > 0 ? "var(--color-danger, #d32f2f)" : "inherit" }}>
                      PKR {c.outstandingBalance.toLocaleString()}
                    </strong>
                    {c.paymentTermsDays > 0 && ` (${c.paymentTermsDays} days terms)`}
                  </div>
                  {c.phone && <small style={{ color: "var(--text-muted)" }}>Phone: {c.phone}</small>}
                </div>
              ))
            )}
          </article>
        </div>
      )}

      {/* ── TAB 4: INVOICES LEDGER ────────────────────────────────────────── */}
      {activeTab === "invoices" && (
        <article className="inventory-card">
          <h3>Sales Invoices &amp; Fiscal Ledger ({invoices.length})</h3>
          {invoices.length === 0 ? (
            <p className="inventory-empty">No sales invoices posted yet.</p>
          ) : (
            invoices.map((inv) => (
              <div key={inv.invoiceId} style={{ borderBottom: "1px solid var(--border)", paddingBottom: "1rem", marginBottom: "1rem" }}>
                <div className="inventory-list-row">
                  <div>
                    <strong>{inv.invoiceNumber}</strong>
                    <span style={{ marginLeft: "0.75rem", fontSize: "0.85rem", color: "var(--text-muted)" }}>
                      {inv.invoiceDate} · {inv.customerName}
                    </span>
                  </div>
                  <div>
                    <span className="tag tag-starter" style={{ marginRight: "0.5rem" }}>{inv.paymentMethod.toUpperCase()}</span>
                    <span className="tag">{inv.invoiceStatus}</span>
                  </div>
                </div>

                <div style={{ display: "flex", gap: "2rem", marginTop: "0.4rem", fontSize: "0.9rem" }}>
                  <span>Subtotal: PKR {inv.subtotalAmount.toLocaleString()}</span>
                  <span>Tax: PKR {inv.taxAmount.toLocaleString()}</span>
                  <span>Discount: PKR {inv.discountAmount.toLocaleString()}</span>
                  <strong>Total: PKR {inv.totalAmount.toLocaleString()}</strong>
                  <span style={{ color: "var(--color-success, #2e7d32)" }}>Paid: PKR {inv.paidAmount.toLocaleString()}</span>
                </div>

                {(() => {
                  const showCost = inv.lines.some((l) => l.unitCost !== null && l.unitCost !== undefined);
                  return (
                    <table className="stock-table" style={{ marginTop: "0.5rem", fontSize: "0.8rem", width: "100%" }}>
                      <thead>
                        <tr>
                          <th>#</th>
                          <th>Variant SKU</th>
                          <th>Bin</th>
                          <th>Qty</th>
                          <th>Unit Price</th>
                          {showCost && <th>FIFO Cost</th>}
                          <th>Tax %</th>
                          <th>Line Total</th>
                        </tr>
                      </thead>
                      <tbody>
                        {inv.lines.map((line) => (
                          <tr key={line.lineNumber}>
                            <td>{line.lineNumber}</td>
                            <td><strong>{line.sku}</strong></td>
                            <td>{line.binCode}</td>
                            <td>{line.quantity} {line.uomCode}</td>
                            <td>PKR {line.unitPrice.toLocaleString()}</td>
                            {showCost && <td>{line.unitCost !== null ? `PKR ${line.unitCost.toLocaleString()}` : "—"}</td>}
                            <td>{line.taxRate}%</td>
                            <td><strong>PKR {line.lineTotal.toLocaleString()}</strong></td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  );
                })()}
              </div>
            ))
          )}
        </article>
      )}
    </section>
  );
}
