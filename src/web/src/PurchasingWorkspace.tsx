import { useEffect, useState, type FormEvent } from "react";
import { apiFetch, type ApiWorkspaceScope } from "./auth";
import {
  evaluateThreeWayMatch,
  calculateSupplierBalance,
  validateInspectionQuantities,
  type MatchingTolerancePolicy,
  type MatchLineInput,
} from "./purchasingHelpers";

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

type SupplierInvoiceLine = {
  invoiceLineId: string;
  lineNumber: number;
  poLineNumber: number | null;
  grnLineNumber: number | null;
  variantId: string;
  sku: string;
  invoicedQty: number;
  unitPrice: number;
  lineTotal: number;
  matchedQty: number;
  priceVariance: number;
  qtyVariance: number;
  matchingStatus: string;
};

type SupplierInvoice = {
  invoiceId: string;
  invoiceNumber: string;
  supplierId: string;
  supplierName: string;
  purchaseOrderId: string | null;
  poNumber: string | null;
  grnId: string | null;
  grnNumber: string | null;
  invoiceDate: string;
  dueDate: string;
  currencyCode: string;
  subtotal: number;
  taxAmount: number;
  totalAmount: number;
  matchingStatus: string;
  status: string;
  paymentStatus: string;
  paidAmount: number;
  varianceAmount: number;
  varianceReason: string | null;
  glJournalId: string | null;
  lines: SupplierInvoiceLine[];
};

type InspectionLine = {
  inspectionLineId: string;
  grnLineNumber: number;
  variantId: string;
  sku: string;
  inspectedQty: number;
  acceptedQty: number;
  quarantinedQty: number;
  rejectedQty: number;
  defectReason: string | null;
  resolutionAction: string | null;
};

type PurchaseInspection = {
  inspectionId: string;
  inspectionNumber: string;
  grnId: string;
  grnNumber: string;
  overallStatus: string;
  inspectionDate: string;
  note: string | null;
  lines: InspectionLine[];
};

type QuarantineRelease = {
  releaseId: string;
  releaseNumber: string;
  inspectionLineId: string;
  grnId: string;
  variantId: string;
  sku: string;
  sourceBinId: string;
  sourceBinCode: string;
  targetBinId: string;
  targetBinCode: string;
  releasedQty: number;
  targetStatusCode: string;
  reason: string | null;
  releasedAt: string;
};

type PurchaseReturnLine = {
  returnLineId: string;
  variantId: string;
  sku: string;
  warehouseId: string;
  binId: string;
  binCode: string;
  returnedQty: number;
  unitCost: number;
  lineTotal: number;
  statusCode: string;
  reason: string | null;
};

type PurchaseReturn = {
  returnId: string;
  returnNumber: string;
  supplierId: string;
  supplierName: string;
  purchaseOrderId: string | null;
  poNumber: string | null;
  grnId: string | null;
  grnNumber: string | null;
  invoiceId: string | null;
  invoiceNumber: string | null;
  returnDate: string;
  totalAmount: number;
  status: string;
  lines: PurchaseReturnLine[];
};

type SupplierDebitNote = {
  debitNoteId: string;
  debitNoteNumber: string;
  supplierId: string;
  supplierName: string;
  returnId: string | null;
  invoiceId: string | null;
  debitNoteDate: string;
  currencyCode: string;
  amount: number;
  status: string;
  glJournalId: string | null;
  reason: string | null;
};

type SupplierBalance = {
  supplierId: string;
  supplierCode: string;
  name: string;
  currencyCode: string;
  totalInvoiced: number;
  totalDebitNotes: number;
  netPayable: number;
  totalPaid: number;
  outstandingBalance: number;
  paymentStatus: 'paid' | 'partially_paid' | 'unpaid';
};

type PurchasingTolerancePolicy = {
  policyId: string;
  priceTolerancePercent: number;
  quantityTolerancePercent: number;
  allowPriceVariance: boolean;
  allowQuantityVariance: boolean;
  autoApproveWithinTolerance: boolean;
  updatedAt: string;
};

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
    const problem = await response.json().catch(() => ({})) as { title?: string; code?: string; detail?: string };
    throw new Error(problem.detail ?? problem.title ?? problem.code ?? `Request failed (${response.status}).`);
  }
  return (await response.json()) as T;
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function PurchasingWorkspace({ enabled, workspace }: Props) {
  // Navigation
  const [activeTab, setActiveTab] = useState<"orders" | "receive" | "invoices" | "inspection" | "returns" | "balances">("orders");
  const [reloadVersion, setReloadVersion] = useState(0);
  const [message, setMessage] = useState("");
  const [isSaving, setIsSaving] = useState(false);

  // Core Data
  const [suppliers, setSuppliers] = useState<Supplier[]>([]);
  const [purchaseOrders, setPurchaseOrders] = useState<PurchaseOrder[]>([]);
  const [grns, setGrns] = useState<Grn[]>([]);
  const [invoices, setInvoices] = useState<SupplierInvoice[]>([]);
  const [inspections, setInspections] = useState<PurchaseInspection[]>([]);
  const [quarantineReleases, setQuarantineReleases] = useState<QuarantineRelease[]>([]);
  const [returns, setReturns] = useState<PurchaseReturn[]>([]);
  const [debitNotes, setDebitNotes] = useState<SupplierDebitNote[]>([]);
  const [supplierBalances, setSupplierBalances] = useState<SupplierBalance[]>([]);
  const [policy, setPolicy] = useState<PurchasingTolerancePolicy | null>(null);
  const [variants, setVariants] = useState<Variant[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [bins, setBins] = useState<Bin[]>([]);

  // 1. Supplier & PO forms
  const [supCode, setSupCode] = useState("");
  const [supName, setSupName] = useState("");
  const [supContact, setSupContact] = useState("");
  const [supEmail, setSupEmail] = useState("");
  const [supPhone, setSupPhone] = useState("");
  const [supTerms, setSupTerms] = useState("30");

  const [poNumber, setPoNumber] = useState("");
  const [poSupplierId, setPoSupplierId] = useState("");
  const [poWarehouseId, setPoWarehouseId] = useState("");
  const [poExpected, setPoExpected] = useState("");
  const [poNote, setPoNote] = useState("");
  const [poLines, setPoLines] = useState<Array<{ variantId: string; qty: string; price: string }>>([
    { variantId: "", qty: "", price: "0" },
  ]);

  // 2. GRN form
  const [grnNumber, setGrnNumber] = useState("");
  const [grnPoId, setGrnPoId] = useState("");
  const [grnSupRef, setGrnSupRef] = useState("");
  const [grnNote, setGrnNote] = useState("");
  const [grnLines, setGrnLines] = useState<Array<{ poLineNumber: string; binId: string; qty: string; price: string }>>([
    { poLineNumber: "", binId: "", qty: "", price: "0" },
  ]);

  // 3. Invoice form & 3-way match
  const [invNumber, setInvNumber] = useState("");
  const [invSupplierId, setInvSupplierId] = useState("");
  const [invPoId, setInvPoId] = useState("");
  const [invGrnId, setInvGrnId] = useState("");
  const [invDueDate, setInvDueDate] = useState("");
  const [invLines, setInvLines] = useState<Array<{ poLineNumber: string; grnLineNumber: string; variantId: string; qty: string; price: string }>>([
    { poLineNumber: "1", grnLineNumber: "1", variantId: "", qty: "", price: "0" },
  ]);
  const [inspectingInvoice, setInspectingInvoice] = useState<SupplierInvoice | null>(null);
  const [forceApprove, setForceApprove] = useState(false);
  const [resolutionNote, setResolutionNote] = useState("");

  // 4. Inspection & Quarantine form
  const [inspNumber, setInspNumber] = useState("");
  const [inspGrnId, setInspGrnId] = useState("");
  const [inspNote, setInspNote] = useState("");
  const [inspLines, setInspLines] = useState<Array<{ grnLineNumber: number; variantId: string; receivedQty: number; acceptedQty: string; quarantinedQty: string; rejectedQty: string; defectReason: string }>>([]);
  const [releasingLine, setReleasingLine] = useState<{ inspectionLineId: string; grnId: string; variantId: string; sku: string; sourceBinId: string; availableQty: number } | null>(null);
  const [releaseNumber, setReleaseNumber] = useState("");
  const [releaseTargetBinId, setReleaseTargetBinId] = useState("");
  const [releaseQty, setReleaseQty] = useState("");
  const [releaseReason, setReleaseReason] = useState("");

  // 5. Purchase Returns & Debit Notes
  const [retNumber, setRetNumber] = useState("");
  const [retSupplierId, setRetSupplierId] = useState("");
  const [retPoId, setRetPoId] = useState("");
  const [retGrnId, setRetGrnId] = useState("");
  const [retInvoiceId, setRetInvoiceId] = useState("");
  const [retReason, setRetReason] = useState("");
  const [retLines, setRetLines] = useState<Array<{ variantId: string; warehouseId: string; binId: string; qty: string; cost: string; reason: string }>>([
    { variantId: "", warehouseId: "", binId: "", qty: "", cost: "0", reason: "" },
  ]);

  const [dnNumber, setDnNumber] = useState("");
  const [dnSupplierId, setDnSupplierId] = useState("");
  const [dnReturnId, setDnReturnId] = useState("");
  const [dnInvoiceId, setDnInvoiceId] = useState("");
  const [dnAmount, setDnAmount] = useState("");
  const [dnReason, setDnReason] = useState("");

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
        const [s, po, g, inv, insp, rels, ret, dn, bal, pol, vars, whs, b] = await Promise.all([
          apiGet<Supplier[]>("/api/v1/purchasing/suppliers", workspace).catch(() => []),
          apiGet<PurchaseOrder[]>("/api/v1/purchasing/purchase-orders", workspace).catch(() => []),
          apiGet<Grn[]>("/api/v1/purchasing/grns", workspace).catch(() => []),
          apiGet<SupplierInvoice[]>("/api/v1/purchasing/invoices", workspace).catch(() => []),
          apiGet<PurchaseInspection[]>("/api/v1/purchasing/inspections", workspace).catch(() => []),
          apiGet<QuarantineRelease[]>("/api/v1/purchasing/quarantine-releases", workspace).catch(() => []),
          apiGet<PurchaseReturn[]>("/api/v1/purchasing/returns", workspace).catch(() => []),
          apiGet<SupplierDebitNote[]>("/api/v1/purchasing/debit-notes", workspace).catch(() => []),
          apiGet<SupplierBalance[]>("/api/v1/purchasing/suppliers/balances", workspace).catch(() => []),
          apiGet<PurchasingTolerancePolicy>("/api/v1/purchasing/tolerance-policy", workspace).catch(() => null),
          apiGet<Variant[]>("/api/v1/inventory/variants", workspace).catch(() => []),
          apiGet<Warehouse[]>("/api/v1/inventory/warehouses", workspace).catch(() => []),
          apiGet<Bin[]>("/api/v1/inventory/bins", workspace).catch(() => []),
        ]);
        if (!mounted) return;
        setSuppliers(s);
        setPurchaseOrders(po);
        setGrns(g);
        setInvoices(inv);
        setInspections(insp);
        setQuarantineReleases(rels);
        setReturns(ret);
        setDebitNotes(dn);
        setSupplierBalances(bal);
        setPolicy(pol);
        setVariants(vars);
        setWarehouses(whs);
        setBins(b);

        if (!poSupplierId && s.length > 0) setPoSupplierId(s[0].supplierId);
        if (!poWarehouseId) {
          const first = whs.find((w) => w.isActive !== false);
          if (first) setPoWarehouseId(first.warehouseId);
        }
        if (!grnPoId && po.length > 0) {
          const firstApp = po.find((p) => p.orderStatus === "approved" || p.orderStatus === "confirmed" || p.orderStatus === "partially_received");
          setGrnPoId(firstApp?.purchaseOrderId ?? po[0].purchaseOrderId);
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

  // Auto-populate default bin and line when an approved/confirmed PO is selected for receiving
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

  // When selecting a GRN in Inspection form, populate line inputs
  useEffect(() => {
    if (!inspGrnId) {
      setInspLines([]);
      return;
    }
    const targetGrn = grns.find((g) => g.grnId === inspGrnId);
    if (!targetGrn) return;
    setInspLines(targetGrn.lines.map((l) => ({
      grnLineNumber: l.lineNumber,
      variantId: l.variantId,
      receivedQty: l.receivedQty,
      acceptedQty: String(l.receivedQty),
      quarantinedQty: "0",
      rejectedQty: "0",
      defectReason: "",
    })));
  }, [inspGrnId]);

  // ── Actions ─────────────────────────────────────────────────────────────────

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
      setMessage("Supplier registered.");
      reload();
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const newSup: Supplier = {
          supplierId: crypto.randomUUID ? crypto.randomUUID() : `sup-${Date.now()}`,
          supplierCode: supCode,
          name: supName,
          contactName: supContact || null,
          email: supEmail || null,
          phone: supPhone || null,
          currencyCode: "PKR",
          paymentTermsDays: Number(supTerms) || 0,
          isActive: true,
        };
        setSuppliers((prev) => [newSup, ...prev]);
        setSupCode(""); setSupName(""); setSupContact(""); setSupEmail(""); setSupPhone("");
        setMessage("Supplier registered.");
      } else {
        setMessage(err instanceof Error ? err.message : "Supplier could not be saved.");
      }
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
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const supplier = suppliers.find((s) => s.supplierId === poSupplierId);
        const warehouse = warehouses.find((w) => w.warehouseId === poWarehouseId);
        const newPo: PurchaseOrder = {
          purchaseOrderId: crypto.randomUUID ? crypto.randomUUID() : `po-${Date.now()}`,
          poNumber,
          supplierId: poSupplierId,
          supplierName: supplier?.name || "Supplier",
          warehouseId: poWarehouseId,
          warehouseName: warehouse?.name || "Main Warehouse",
          orderStatus: "draft",
          currencyCode: "PKR",
          orderDate: new Date().toISOString().slice(0, 10),
          expectedDate: poExpected || null,
          note: poNote || null,
          lines: validLines.map((l, idx) => {
            const v = variants.find((vr) => vr.variantId === l.variantId);
            return {
              lineNumber: idx + 1,
              variantId: l.variantId,
              sku: v?.sku || "SKU-ITEM",
              orderedQty: Number(l.qty),
              receivedQty: 0,
              uomCode: "EA",
              unitPrice: Number(l.price),
              lineStatus: "pending",
            };
          }),
        };
        setPurchaseOrders((prev) => [newPo, ...prev]);
        setPoNumber(""); setPoNote(""); setPoExpected("");
        setPoLines([{ variantId: "", qty: "", price: "0" }]);
        setMessage("Purchase order created in draft.");
      } else {
        setMessage(err instanceof Error ? err.message : "Purchase order could not be saved.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const approveOrder = async (purchaseOrderId: string) => {
    setIsSaving(true); setMessage("");
    try {
      await apiPost<PurchaseOrder>(`/api/v1/purchasing/purchase-orders/${purchaseOrderId}/approve`, {
        note: "Authorized by procurement officer",
      }, workspace);
      setMessage("Purchase order approved.");
      reload();
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        setPurchaseOrders((prev) =>
          prev.map((p) => (p.purchaseOrderId === purchaseOrderId ? { ...p, status: "approved" } : p))
        );
        setMessage("Purchase order approved.");
      } else {
        setMessage(err instanceof Error ? err.message : "Could not approve order.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const cancelOrder = async (purchaseOrderId: string) => {
    const reason = window.prompt("Enter cancellation reason:");
    if (!reason) return;
    setIsSaving(true); setMessage("");
    try {
      await apiPost<PurchaseOrder>(`/api/v1/purchasing/purchase-orders/${purchaseOrderId}/cancel`, {
        reason,
      }, workspace);
      setMessage("Purchase order cancelled.");
      reload();
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        setPurchaseOrders((prev) =>
          prev.map((p) => (p.purchaseOrderId === purchaseOrderId ? { ...p, status: "cancelled" } : p))
        );
        setMessage("Purchase order cancelled.");
      } else {
        setMessage(err instanceof Error ? err.message : "Could not cancel order.");
      }
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
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        setPurchaseOrders((prev) =>
          prev.map((p) => (p.purchaseOrderId === purchaseOrderId ? { ...p, status: "confirmed" } : p))
        );
        setMessage("Purchase order confirmed.");
      } else {
        setMessage(err instanceof Error ? err.message : "Could not confirm order.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const submitGrn = async (e: FormEvent) => {
    e.preventDefault();
    const validLines = grnLines.filter((l) => l.binId && l.poLineNumber && Number(l.qty) > 0);
    if (!selectedPo) { setMessage("Select an approved or confirmed purchase order."); return; }
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
      setMessage("Goods received note posted. Stock movement and GL journal (Dr 1200 / Cr 2010) created.");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "GRN could not be posted.");
    } finally {
      setIsSaving(false);
    }
  };

  const processOutbox = async () => {
    setIsSaving(true); setMessage("");
    try {
      const res = await apiPost<{ processedEvents: number; stockMovementsCreated: number; journalsPosted: number; details: string[] }>(
        "/api/v1/purchasing/outbox/process", {}, workspace
      );
      setMessage(`Outbox processed: ${res.processedEvents} event(s), ${res.stockMovementsCreated} movement(s), ${res.journalsPosted} GL journal(s) posted.`);
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Outbox processing failed.");
    } finally {
      setIsSaving(false);
    }
  };

  // 3-Way Matching Submissions
  const submitSupplierInvoice = async (e: FormEvent) => {
    e.preventDefault();
    if (!invNumber.trim()) { setMessage("Invoice number is required."); return; }
    // Client-side duplicate check
    const existing = invoices.find((inv) =>
      inv.supplierId === invSupplierId && inv.invoiceNumber.toLowerCase() === invNumber.trim().toLowerCase());
    if (existing) {
      setMessage(`Duplicate Invoice: An invoice with number "${invNumber}" already exists for this supplier.`);
      return;
    }

    const validLines = invLines.filter((l) => l.variantId && Number(l.qty) > 0);
    if (validLines.length === 0) { setMessage("Add at least one line with positive quantity."); return; }
    setIsSaving(true); setMessage("");
    try {
      await apiPost<SupplierInvoice>("/api/v1/purchasing/invoices", {
        invoiceNumber: invNumber.trim(),
        supplierId: invSupplierId,
        purchaseOrderId: invPoId || null,
        grnId: invGrnId || null,
        invoiceDate: new Date().toISOString().slice(0, 10),
        dueDate: invDueDate || new Date(Date.now() + 30 * 86400000).toISOString().slice(0, 10),
        currencyCode: "PKR",
        taxAmount: 0,
        lines: validLines.map((l, idx) => ({
          lineNumber: idx + 1,
          poLineNumber: l.poLineNumber ? Number(l.poLineNumber) : null,
          grnLineNumber: l.grnLineNumber ? Number(l.grnLineNumber) : null,
          variantId: l.variantId,
          invoicedQty: Number(l.qty),
          unitPrice: Number(l.price),
        })),
      }, workspace);
      setInvNumber(""); setInvDueDate("");
      setInvLines([{ poLineNumber: "1", grnLineNumber: "1", variantId: "", qty: "", price: "0" }]);
      setMessage("Supplier invoice registered.");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Could not create supplier invoice.");
    } finally {
      setIsSaving(false);
    }
  };

  const matchInvoice = async (invoiceId: string) => {
    setIsSaving(true); setMessage("");
    try {
      await apiPost(`/api/v1/purchasing/invoices/${invoiceId}/match`, {}, workspace);
      setMessage("Three-way match evaluated.");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Matching evaluation failed.");
    } finally {
      setIsSaving(false);
    }
  };

  const approveInvoice = async (invoiceId: string) => {
    setIsSaving(true); setMessage("");
    try {
      await apiPost(`/api/v1/purchasing/invoices/${invoiceId}/approve`, {
        forceApprovalIfDisputed: forceApprove,
        resolutionNote: resolutionNote || null,
      }, workspace);
      setMessage("Supplier invoice approved. Note: GRN already posted AP liability; zero duplicate liability invariant preserved.");
      setInspectingInvoice(null);
      setForceApprove(false);
      setResolutionNote("");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Could not approve supplier invoice.");
    } finally {
      setIsSaving(false);
    }
  };

  // Quality Inspection & Quarantine Release
  const submitInspection = async (e: FormEvent) => {
    e.preventDefault();
    if (!inspNumber || !inspGrnId) { setMessage("Inspection number and GRN are required."); return; }
    // Validate each line
    for (const l of inspLines) {
      const v = validateInspectionQuantities(l.receivedQty, Number(l.acceptedQty), Number(l.quarantinedQty), Number(l.rejectedQty));
      if (!v.isValid) { setMessage(`Line ${l.grnLineNumber}: ${v.error}`); return; }
    }
    setIsSaving(true); setMessage("");
    try {
      await apiPost<PurchaseInspection>("/api/v1/purchasing/inspections", {
        inspectionNumber: inspNumber,
        grnId: inspGrnId,
        note: inspNote || null,
        lines: inspLines.map((l) => ({
          grnLineNumber: l.grnLineNumber,
          variantId: l.variantId,
          inspectedQty: l.receivedQty,
          acceptedQty: Number(l.acceptedQty),
          quarantinedQty: Number(l.quarantinedQty),
          rejectedQty: Number(l.rejectedQty),
          defectReason: l.defectReason || null,
          resolutionAction: Number(l.quarantinedQty) > 0 ? "Quarantine assay" : null,
        })),
      }, workspace);
      setInspNumber(""); setInspNote(""); setInspGrnId(""); setInspLines([]);
      setMessage("Quality inspection recorded. Quarantined stock isolated from ATP/POS.");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Inspection could not be saved.");
    } finally {
      setIsSaving(false);
    }
  };

  const submitQuarantineRelease = async (e: FormEvent) => {
    e.preventDefault();
    if (!releasingLine || !releaseNumber || !releaseTargetBinId || Number(releaseQty) <= 0) {
      setMessage("Complete all release fields with a valid quantity.");
      return;
    }
    setIsSaving(true); setMessage("");
    try {
      await apiPost<QuarantineRelease>("/api/v1/purchasing/quarantine-releases", {
        releaseNumber,
        inspectionLineId: releasingLine.inspectionLineId,
        grnId: releasingLine.grnId,
        variantId: releasingLine.variantId,
        sourceBinId: releasingLine.sourceBinId,
        targetBinId: releaseTargetBinId,
        releasedQty: Number(releaseQty),
        targetStatusCode: "available",
        reason: releaseReason || "Inspection assay approved",
      }, workspace);
      setReleasingLine(null); setReleaseNumber(""); setReleaseQty(""); setReleaseReason("");
      setMessage("Quarantine stock released to available inventory.");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Could not release quarantine stock.");
    } finally {
      setIsSaving(false);
    }
  };

  // Purchase Returns & Debit Notes
  const submitPurchaseReturn = async (e: FormEvent) => {
    e.preventDefault();
    const validLines = retLines.filter((l) => l.variantId && l.binId && Number(l.qty) > 0);
    if (validLines.length === 0) { setMessage("Add at least one return line with positive quantity."); return; }
    setIsSaving(true); setMessage("");
    try {
      await apiPost<PurchaseReturn>("/api/v1/purchasing/returns", {
        returnNumber: retNumber,
        supplierId: retSupplierId,
        purchaseOrderId: retPoId || null,
        grnId: retGrnId || null,
        invoiceId: retInvoiceId || null,
        reason: retReason || null,
        lines: validLines.map((l) => ({
          variantId: l.variantId,
          warehouseId: l.warehouseId,
          binId: l.binId,
          returnedQty: Number(l.qty),
          unitCost: Number(l.cost),
          statusCode: "available",
          reason: l.reason || null,
        })),
      }, workspace);
      setRetNumber(""); setRetReason("");
      setRetLines([{ variantId: "", warehouseId: "", binId: "", qty: "", cost: "0", reason: "" }]);
      setMessage("Purchase return drafted.");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Could not create purchase return.");
    } finally {
      setIsSaving(false);
    }
  };

  const approvePurchaseReturn = async (returnId: string) => {
    setIsSaving(true); setMessage("");
    try {
      await apiPost<PurchaseReturn>(`/api/v1/purchasing/returns/${returnId}/approve`, {}, workspace);
      setMessage("Purchase return approved. Inventory stock decremented and FIFO cost layers consumed.");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Could not approve return.");
    } finally {
      setIsSaving(false);
    }
  };

  const submitDebitNote = async (e: FormEvent) => {
    e.preventDefault();
    if (!dnNumber || !dnSupplierId || Number(dnAmount) <= 0) {
      setMessage("Debit note number, supplier, and positive amount are required.");
      return;
    }
    setIsSaving(true); setMessage("");
    try {
      await apiPost<SupplierDebitNote>("/api/v1/purchasing/debit-notes", {
        debitNoteNumber: dnNumber,
        supplierId: dnSupplierId,
        returnId: dnReturnId || null,
        invoiceId: dnInvoiceId || null,
        debitNoteDate: new Date().toISOString().slice(0, 10),
        amount: Number(dnAmount),
        reason: dnReason || null,
      }, workspace);
      setDnNumber(""); setDnAmount(""); setDnReason("");
      setMessage("Supplier debit note created in draft.");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Could not create debit note.");
    } finally {
      setIsSaving(false);
    }
  };

  const approveDebitNote = async (debitNoteId: string) => {
    setIsSaving(true); setMessage("");
    try {
      await apiPost<SupplierDebitNote>(`/api/v1/purchasing/debit-notes/${debitNoteId}/approve`, {}, workspace);
      setMessage("Debit note approved. Double-entry financial journal posted: Dr 2010 (Accounts Payable) / Cr 1200 (Inventory Asset).");
      reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Could not approve debit note.");
    } finally {
      setIsSaving(false);
    }
  };

  const statusColor = (s: string) => {
    switch (s) {
      case "approved":
      case "confirmed":
      case "posted":
      case "matched":
      case "paid":
        return "tag-starter";
      case "partially_received":
      case "partially_paid":
      case "partial":
        return "tag-progress";
      case "on_hold":
      case "variance_exceeded":
      case "disputed":
      case "rejected":
      case "cancelled":
        return "badge-unreconciled";
      default:
        return "";
    }
  };

  // Balances aggregate
  const totalInvoicedAll = supplierBalances.reduce((sum, b) => sum + b.totalInvoiced, 0);
  const totalDebitNotesAll = supplierBalances.reduce((sum, b) => sum + b.totalDebitNotes, 0);
  const totalNetPayableAll = supplierBalances.reduce((sum, b) => sum + b.netPayable, 0);
  const totalPaidAll = supplierBalances.reduce((sum, b) => sum + b.totalPaid, 0);
  const totalOutstandingAll = supplierBalances.reduce((sum, b) => sum + b.outstandingBalance, 0);

  // Quarantined items available for release
  const quarantinedItems = inspections.flatMap((insp) =>
    insp.lines
      .filter((l) => l.quarantinedQty > 0)
      .map((l) => {
        const matchingGrn = grns.find((g) => g.grnId === insp.grnId);
        const grnLine = matchingGrn?.lines.find((gl) => gl.lineNumber === l.grnLineNumber);
        return {
          inspectionId: insp.inspectionId,
          inspectionNumber: insp.inspectionNumber,
          inspectionLineId: l.inspectionLineId,
          grnId: insp.grnId,
          grnNumber: insp.grnNumber,
          variantId: l.variantId,
          sku: l.sku,
          quarantinedQty: l.quarantinedQty,
          sourceBinId: grnLine?.binId ?? "",
          sourceBinCode: grnLine?.binCode ?? "RECEIVE-01",
          defectReason: l.defectReason,
        };
      })
  );

  return (
    <section className="inventory-workspace" aria-labelledby="purchasing-title">
      <div className="catalog-heading">
        <div>
          <p className="eyebrow">CORE PURCHASING &amp; VENDOR MANAGEMENT (PHASE 6)</p>
          <h2 id="purchasing-title">Purchasing &amp; Quality Management</h2>
        </div>
        <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
          <button
            className="secondary-button"
            disabled={!enabled || isSaving}
            onClick={processOutbox}
            type="button"
            title="Synchronize pending goods receipts into inventory balances and double-entry accounting journals"
          >
            Sync Outbox to GL
          </button>
          <button className="secondary-button" disabled={!enabled || isSaving} onClick={reload} type="button">
            Refresh
          </button>
        </div>
      </div>

      <nav className="inventory-nav-tabs" aria-label="Purchasing modules">
        <button
          className={`inventory-nav-tab${activeTab === "orders" ? " inventory-nav-tab-active" : ""}`}
          onClick={() => setActiveTab("orders")}
          type="button"
        >
          1. Suppliers &amp; Orders ({purchaseOrders.length})
        </button>
        <button
          className={`inventory-nav-tab${activeTab === "receive" ? " inventory-nav-tab-active" : ""}`}
          onClick={() => setActiveTab("receive")}
          type="button"
        >
          2. Receipts &amp; GRNs ({grns.length})
        </button>
        <button
          className={`inventory-nav-tab${activeTab === "invoices" ? " inventory-nav-tab-active" : ""}`}
          onClick={() => setActiveTab("invoices")}
          type="button"
        >
          3. Invoices &amp; 3-Way Match ({invoices.length})
        </button>
        <button
          className={`inventory-nav-tab${activeTab === "inspection" ? " inventory-nav-tab-active" : ""}`}
          onClick={() => setActiveTab("inspection")}
          type="button"
        >
          4. Quality &amp; Quarantine ({inspections.length})
        </button>
        <button
          className={`inventory-nav-tab${activeTab === "returns" ? " inventory-nav-tab-active" : ""}`}
          onClick={() => setActiveTab("returns")}
          type="button"
        >
          5. Returns &amp; Debit Notes ({returns.length}/{debitNotes.length})
        </button>
        <button
          className={`inventory-nav-tab${activeTab === "balances" ? " inventory-nav-tab-active" : ""}`}
          onClick={() => setActiveTab("balances")}
          type="button"
        >
          6. Supplier Balances &amp; AP Status ({supplierBalances.length})
        </button>
      </nav>

      {!enabled && (
        <p className="catalog-storage-note">API is offline or sign-in is unavailable. Purchasing is paused.</p>
      )}
      {message && <p className="catalog-message" role="status">{message}</p>}

      {/* ── TAB 1: SUPPLIERS & ORDERS ────────────────────────────────────── */}
      {activeTab === "orders" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>New Purchase Order</h3>
            <form className="inventory-form" onSubmit={(e) => void submitPurchaseOrder(e)}>
              <label>PO number<input required maxLength={64} value={poNumber} onChange={(e) => setPoNumber(e.target.value)} placeholder="e.g. PO-2026-001" /></label>
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

            <h3 style={{ marginTop: "2rem" }}>Register New Supplier</h3>
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
            <h3>Purchase Orders · {purchaseOrders.length}</h3>
            {purchaseOrders.length === 0
              ? <p className="inventory-empty">No purchase orders yet.</p>
              : purchaseOrders.map((po) => (
                <div key={po.purchaseOrderId} style={{ borderBottom: "1px solid #e7ebf2", paddingBottom: "0.75rem", marginBottom: "0.75rem" }}>
                  <div className="inventory-list-row">
                    <strong>{po.poNumber}</strong>
                    <span className={`tag ${statusColor(po.orderStatus)}`}>{po.orderStatus.replace("_", " ")}</span>
                  </div>
                  <small>{po.supplierName} → {po.warehouseName} ({po.orderDate})</small>
                  <div style={{ display: "flex", gap: "0.5rem", marginTop: "0.4rem" }}>
                    {(po.orderStatus === "draft" || po.orderStatus === "pending_approval") && (
                      <button
                        className="secondary-button" style={{ fontSize: "0.75rem" }}
                        disabled={isSaving}
                        onClick={() => void approveOrder(po.purchaseOrderId)}
                        type="button"
                      >
                        Approve PO
                      </button>
                    )}
                    {po.orderStatus === "draft" && (
                      <button
                        className="secondary-button" style={{ fontSize: "0.75rem" }}
                        disabled={isSaving}
                        onClick={() => void confirmOrder(po.purchaseOrderId)}
                        type="button"
                      >
                        Confirm PO
                      </button>
                    )}
                    {["draft", "pending_approval", "approved", "confirmed"].includes(po.orderStatus) && (
                      <button
                        className="secondary-button" style={{ fontSize: "0.75rem", color: "#c0392b" }}
                        disabled={isSaving}
                        onClick={() => void cancelOrder(po.purchaseOrderId)}
                        type="button"
                      >
                        Cancel PO
                      </button>
                    )}
                  </div>
                  {po.lines.map((l) => (
                    <div key={l.lineNumber} style={{ fontSize: "0.8rem", color: "#69758a", paddingLeft: "0.5rem", marginTop: "0.2rem" }}>
                      Line {l.lineNumber}: {l.sku} — {l.orderedQty} {l.uomCode} @ PKR {l.unitPrice} (Recv: {l.receivedQty})
                    </div>
                  ))}
                </div>
              ))}
          </article>
        </div>
      )}

      {/* ── TAB 2: RECEIPTS & GRNS ───────────────────────────────────────── */}
      {activeTab === "receive" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>Post Goods Received Note</h3>
            <form className="inventory-form" onSubmit={(e) => void submitGrn(e)}>
              <label>GRN number<input required maxLength={64} value={grnNumber} onChange={(e) => setGrnNumber(e.target.value)} placeholder="e.g. GRN-2026-001" /></label>
              <label>Purchase order
                <select required value={grnPoId} onChange={(e) => setGrnPoId(e.target.value)}>
                  <option value="">Select approved/confirmed PO</option>
                  {purchaseOrders
                    .filter((po) => ["approved", "confirmed", "partially_received"].includes(po.orderStatus))
                    .map((po) => (
                      <option key={po.purchaseOrderId} value={po.purchaseOrderId}>
                        {po.poNumber} · {po.supplierName} ({po.orderStatus})
                      </option>
                    ))}
                </select>
              </label>
              <label>Supplier reference<input maxLength={200} value={grnSupRef} onChange={(e) => setGrnSupRef(e.target.value)} placeholder="Supplier delivery note / challan" /></label>
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
                {isSaving ? "Posting…" : "Post GRN (Idempotent)"}
              </button>
            </form>
            <p className="inventory-footnote">
              Posting creates stock balances and automatically processes the outbox event to generate the FIFO cost layer and post GL journal (Dr 1200 / Cr 2010).
            </p>
          </article>

          <article className="inventory-card">
            <h3>Posted GRNs · {grns.length}</h3>
            {grns.length === 0
              ? <p className="inventory-empty">No goods received notes yet.</p>
              : grns.map((g) => (
                <div key={g.grnId} style={{ borderBottom: "1px solid #e7ebf2", paddingBottom: "0.75rem", marginBottom: "0.75rem" }}>
                  <div className="inventory-list-row">
                    <strong>{g.grnNumber}</strong>
                    <span className={`tag ${statusColor(g.grnStatus)}`}>{g.grnStatus}</span>
                  </div>
                  <small>PO: {g.poNumber} · {g.supplierName} → {g.warehouseName} ({g.receivedDate})</small>
                  {g.lines.map((l) => (
                    <div key={l.lineNumber} style={{ fontSize: "0.8rem", color: "#69758a", paddingLeft: "0.5rem" }}>
                      Line {l.lineNumber}: {l.sku} · {l.receivedQty} {l.uomCode} @ PKR {l.unitPrice} → {l.binCode}
                    </div>
                  ))}
                </div>
              ))}
          </article>
        </div>
      )}

      {/* ── TAB 3: SUPPLIER INVOICES & 3-WAY MATCHING ────────────────────── */}
      {activeTab === "invoices" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>Register Supplier Invoice</h3>
            <form className="inventory-form" onSubmit={(e) => void submitSupplierInvoice(e)}>
              <label>Invoice number (unique per supplier)<input required maxLength={64} value={invNumber} onChange={(e) => setInvNumber(e.target.value)} placeholder="e.g. INV-2026-901" /></label>
              <label>Supplier
                <select required value={invSupplierId} onChange={(e) => setInvSupplierId(e.target.value)}>
                  <option value="">Select supplier</option>
                  {suppliers.map((s) => (
                    <option key={s.supplierId} value={s.supplierId}>{s.supplierCode} · {s.name}</option>
                  ))}
                </select>
              </label>
              <label>Purchase order (optional)
                <select value={invPoId} onChange={(e) => setInvPoId(e.target.value)}>
                  <option value="">Select related PO</option>
                  {purchaseOrders.map((po) => (
                    <option key={po.purchaseOrderId} value={po.purchaseOrderId}>{po.poNumber}</option>
                  ))}
                </select>
              </label>
              <label>Goods received note (optional)
                <select value={invGrnId} onChange={(e) => setInvGrnId(e.target.value)}>
                  <option value="">Select related GRN</option>
                  {grns.map((g) => (
                    <option key={g.grnId} value={g.grnId}>{g.grnNumber} ({g.poNumber})</option>
                  ))}
                </select>
              </label>
              <label>Due date<input type="date" value={invDueDate} onChange={(e) => setInvDueDate(e.target.value)} /></label>

              <h4 className="inventory-subheading">Invoice lines</h4>
              {invLines.map((line, idx) => (
                <div key={idx} style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap", marginBottom: "0.5rem" }}>
                  <select
                    value={line.variantId}
                    onChange={(e) => setInvLines(invLines.map((l, i) => i === idx ? { ...l, variantId: e.target.value } : l))}
                    style={{ flex: "2" }}
                  >
                    <option value="">Select variant</option>
                    {variants.map((v) => (
                      <option key={v.variantId} value={v.variantId}>{v.sku} · {v.productName}</option>
                    ))}
                  </select>
                  <input
                    type="number" min="0.001" step="0.001" placeholder="Qty"
                    value={line.qty}
                    onChange={(e) => setInvLines(invLines.map((l, i) => i === idx ? { ...l, qty: e.target.value } : l))}
                    style={{ flex: "1", minWidth: "80px" }}
                  />
                  <input
                    type="number" min="0" step="0.01" placeholder="Unit price"
                    value={line.price}
                    onChange={(e) => setInvLines(invLines.map((l, i) => i === idx ? { ...l, price: e.target.value } : l))}
                    style={{ flex: "1", minWidth: "90px" }}
                  />
                  {invLines.length > 1 && (
                    <button type="button" onClick={() => setInvLines(invLines.filter((_, i) => i !== idx))}>✕</button>
                  )}
                </div>
              ))}
              <button type="button" className="secondary-button" onClick={() => setInvLines([...invLines, { poLineNumber: "1", grnLineNumber: "1", variantId: "", qty: "", price: "0" }])}>
                + Add line
              </button>
              <button disabled={!enabled || isSaving || !invSupplierId || !invNumber} type="submit">
                Register Invoice
              </button>
            </form>

            {policy && (
              <div style={{ marginTop: "1.5rem", background: "#f8fafc", padding: "10px", borderRadius: "8px", fontSize: "12px" }}>
                <strong>Active Tolerance Policy:</strong>
                <div>Price Tolerance: <strong>{policy.priceTolerancePercent}%</strong> (Allow: {policy.allowPriceVariance ? "Yes" : "No"})</div>
                <div>Quantity Tolerance: <strong>{policy.quantityTolerancePercent}%</strong> (Allow: {policy.allowQuantityVariance ? "Yes" : "No"})</div>
                <div>Auto-Approve within tolerance: <strong>{policy.autoApproveWithinTolerance ? "Yes" : "No"}</strong></div>
              </div>
            )}
          </article>

          <article className="inventory-card">
            <h3>Registered Invoices · {invoices.length}</h3>
            {invoices.length === 0
              ? <p className="inventory-empty">No invoices registered yet.</p>
              : invoices.map((inv) => (
                <div key={inv.invoiceId} style={{ borderBottom: "1px solid #e7ebf2", paddingBottom: "0.75rem", marginBottom: "0.75rem" }}>
                  <div className="inventory-list-row">
                    <strong>{inv.invoiceNumber}</strong>
                    <div style={{ display: "flex", gap: "0.3rem" }}>
                      <span className={`tag ${statusColor(inv.matchingStatus)}`}>{inv.matchingStatus}</span>
                      <span className={`tag ${statusColor(inv.status)}`}>{inv.status}</span>
                    </div>
                  </div>
                  <small>{inv.supplierName} · Total: PKR {inv.totalAmount.toLocaleString()} · Due: {inv.dueDate}</small>
                  {inv.varianceAmount !== 0 && (
                    <div style={{ color: "#d9381e", fontSize: "0.8rem", fontWeight: 600 }}>
                      Variance: PKR {inv.varianceAmount} ({inv.varianceReason || "Discrepancy"})
                    </div>
                  )}
                  {inv.glJournalId && (
                    <div style={{ fontSize: "0.75rem", color: "#16a34a" }}>
                      GL Variance Journal: {inv.glJournalId}
                    </div>
                  )}

                  <div style={{ display: "flex", gap: "0.5rem", marginTop: "0.4rem" }}>
                    <button
                      className="secondary-button" style={{ fontSize: "0.75rem" }}
                      disabled={isSaving}
                      onClick={() => void matchInvoice(inv.invoiceId)}
                      type="button"
                    >
                      Run 3-Way Match
                    </button>
                    <button
                      className="secondary-button" style={{ fontSize: "0.75rem" }}
                      onClick={() => setInspectingInvoice(inv)}
                      type="button"
                    >
                      Inspect Match Lines
                    </button>
                    {inv.status !== "approved" && inv.status !== "paid" && (
                      <button
                        className="secondary-button" style={{ fontSize: "0.75rem", fontWeight: 700 }}
                        disabled={isSaving}
                        onClick={() => void approveInvoice(inv.invoiceId)}
                        type="button"
                      >
                        Approve Invoice
                      </button>
                    )}
                  </div>
                </div>
              ))}

            {inspectingInvoice && (
              <div style={{ marginTop: "1.5rem", border: "2px solid #2655a8", borderRadius: "8px", padding: "12px", background: "#f0f4fa" }}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                  <h4>3-Way Match Details: {inspectingInvoice.invoiceNumber}</h4>
                  <button type="button" onClick={() => setInspectingInvoice(null)}>✕</button>
                </div>
                <p style={{ fontSize: "12px", margin: "4px 0" }}>
                  Supplier: <strong>{inspectingInvoice.supplierName}</strong> | Status: <strong>{inspectingInvoice.matchingStatus}</strong>
                </p>
                <div className="table-responsive">
                  <table className="erp-table">
                    <thead>
                      <tr>
                        <th>Line</th>
                        <th>Variant</th>
                        <th>Invoiced Qty</th>
                        <th>Matched Qty</th>
                        <th>Price</th>
                        <th>Price Var</th>
                        <th>Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {inspectingInvoice.lines.map((l) => (
                        <tr key={l.invoiceLineId}>
                          <td>{l.lineNumber}</td>
                          <td>{l.sku}</td>
                          <td>{l.invoicedQty}</td>
                          <td>{l.matchedQty}</td>
                          <td>PKR {l.unitPrice}</td>
                          <td style={{ color: l.priceVariance !== 0 ? "#d9381e" : "inherit" }}>PKR {l.priceVariance}</td>
                          <td><span className={`tag ${statusColor(l.matchingStatus)}`}>{l.matchingStatus}</span></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                <div style={{ marginTop: "10px" }}>
                  <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "12px" }}>
                    <input
                      type="checkbox"
                      checked={forceApprove}
                      onChange={(e) => setForceApprove(e.target.checked)}
                    />
                    Override &amp; Force Approval (if variance exceeded)
                  </label>
                  {forceApprove && (
                    <input
                      type="text"
                      placeholder="Resolution note / justification"
                      value={resolutionNote}
                      onChange={(e) => setResolutionNote(e.target.value)}
                      style={{ width: "100%", marginTop: "4px", fontSize: "12px" }}
                    />
                  )}
                  <button
                    style={{ marginTop: "8px" }}
                    disabled={isSaving}
                    onClick={() => void approveInvoice(inspectingInvoice.invoiceId)}
                    type="button"
                  >
                    Confirm Invoice Approval
                  </button>
                </div>
              </div>
            )}
          </article>
        </div>
      )}

      {/* ── TAB 4: QUALITY INSPECTION & QUARANTINE ───────────────────────── */}
      {activeTab === "inspection" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>New Quality Inspection</h3>
            <form className="inventory-form" onSubmit={(e) => void submitInspection(e)}>
              <label>Inspection number<input required maxLength={64} value={inspNumber} onChange={(e) => setInspNumber(e.target.value)} placeholder="e.g. INSP-2026-001" /></label>
              <label>Goods received note
                <select required value={inspGrnId} onChange={(e) => setInspGrnId(e.target.value)}>
                  <option value="">Select posted GRN</option>
                  {grns.map((g) => (
                    <option key={g.grnId} value={g.grnId}>{g.grnNumber} · {g.supplierName}</option>
                  ))}
                </select>
              </label>
              <label>Inspection note<input maxLength={2000} value={inspNote} onChange={(e) => setInspNote(e.target.value)} placeholder="Batch assay / visual inspection notes" /></label>

              {inspLines.length > 0 && (
                <>
                  <h4 className="inventory-subheading">Line Disposition</h4>
                  {inspLines.map((l, idx) => (
                    <div key={idx} style={{ background: "#f8fafc", padding: "8px", borderRadius: "6px", marginBottom: "8px", fontSize: "12px" }}>
                      <div>Line {l.grnLineNumber} | Received: <strong>{l.receivedQty} EA</strong></div>
                      <div style={{ display: "flex", gap: "0.5rem", marginTop: "4px" }}>
                        <label style={{ flex: 1 }}>Accepted:
                          <input
                            type="number" min="0" step="0.001"
                            value={l.acceptedQty}
                            onChange={(e) => setInspLines(inspLines.map((item, i) => i === idx ? { ...item, acceptedQty: e.target.value } : item))}
                          />
                        </label>
                        <label style={{ flex: 1 }}>Quarantine:
                          <input
                            type="number" min="0" step="0.001"
                            value={l.quarantinedQty}
                            onChange={(e) => setInspLines(inspLines.map((item, i) => i === idx ? { ...item, quarantinedQty: e.target.value } : item))}
                          />
                        </label>
                        <label style={{ flex: 1 }}>Rejected:
                          <input
                            type="number" min="0" step="0.001"
                            value={l.rejectedQty}
                            onChange={(e) => setInspLines(inspLines.map((item, i) => i === idx ? { ...item, rejectedQty: e.target.value } : item))}
                          />
                        </label>
                      </div>
                      {Number(l.quarantinedQty) > 0 && (
                        <input
                          type="text"
                          placeholder="Quarantine reason / assay requirements"
                          value={l.defectReason}
                          onChange={(e) => setInspLines(inspLines.map((item, i) => i === idx ? { ...item, defectReason: e.target.value } : item))}
                          style={{ marginTop: "4px", width: "100%" }}
                        />
                      )}
                    </div>
                  ))}
                </>
              )}

              <button disabled={!enabled || isSaving || !inspGrnId || inspLines.length === 0} type="submit">
                Record Inspection
              </button>
            </form>
          </article>

          <article className="inventory-card">
            <h3>Quarantine Inventory &amp; Controlled Releases</h3>
            <p className="catalog-note" style={{ margin: "4px 0 10px" }}>
              Quarantined units are physically traceable in warehouse bins but strictly isolated from Available stock, POS checkout, and ATP.
            </p>

            <h4>Active Quarantined Lots ({quarantinedItems.length})</h4>
            {quarantinedItems.length === 0
              ? <p className="inventory-empty">No quarantined items currently on hold.</p>
              : quarantinedItems.map((qi) => (
                <div key={qi.inspectionLineId} style={{ borderBottom: "1px solid #e7ebf2", paddingBottom: "0.5rem", marginBottom: "0.5rem" }}>
                  <div className="inventory-list-row">
                    <strong>{qi.sku} — {qi.quarantinedQty} EA</strong>
                    <span className="badge-unreconciled">Quarantine Hold</span>
                  </div>
                  <small>GRN: {qi.grnNumber} | Bin: {qi.sourceBinCode} | Reason: {qi.defectReason || "Pending test"}</small>
                  <div style={{ marginTop: "4px" }}>
                    <button
                      className="secondary-button" style={{ fontSize: "0.75rem" }}
                      onClick={() => {
                        setReleasingLine({
                          inspectionLineId: qi.inspectionLineId,
                          grnId: qi.grnId,
                          variantId: qi.variantId,
                          sku: qi.sku,
                          sourceBinId: qi.sourceBinId,
                          availableQty: qi.quarantinedQty,
                        });
                        setReleaseQty(String(qi.quarantinedQty));
                      }}
                      type="button"
                    >
                      Release Stock →
                    </button>
                  </div>
                </div>
              ))}

            {releasingLine && (
              <div style={{ marginTop: "1rem", border: "2px solid #16a34a", borderRadius: "8px", padding: "10px", background: "#f0fdf4" }}>
                <div style={{ display: "flex", justifyContent: "space-between" }}>
                  <h4>Controlled Quarantine Release: {releasingLine.sku}</h4>
                  <button type="button" onClick={() => setReleasingLine(null)}>✕</button>
                </div>
                <form className="inventory-form" onSubmit={(e) => void submitQuarantineRelease(e)}>
                  <label>Release voucher number<input required value={releaseNumber} onChange={(e) => setReleaseNumber(e.target.value)} placeholder="e.g. REL-2026-001" /></label>
                  <label>Target available bin
                    <select required value={releaseTargetBinId} onChange={(e) => setReleaseTargetBinId(e.target.value)}>
                      <option value="">Select target bin</option>
                      {bins.filter((b) => b.isActive !== false).map((b) => (
                        <option key={b.binId} value={b.binId}>{b.code} ({b.binType})</option>
                      ))}
                    </select>
                  </label>
                  <label>Release quantity (Max: {releasingLine.availableQty})
                    <input required type="number" min="0.001" max={releasingLine.availableQty} step="0.001" value={releaseQty} onChange={(e) => setReleaseQty(e.target.value)} />
                  </label>
                  <label>Approval reason / test cert<input value={releaseReason} onChange={(e) => setReleaseReason(e.target.value)} placeholder="e.g. Chemical assay passed certificate #849" /></label>
                  <button disabled={isSaving} type="submit">Release to Available Stock</button>
                </form>
              </div>
            )}

            <h4 style={{ marginTop: "1.5rem" }}>Past Inspections ({inspections.length})</h4>
            {inspections.map((insp) => (
              <div key={insp.inspectionId} style={{ borderBottom: "1px solid #e7ebf2", paddingBottom: "0.5rem", marginBottom: "0.5rem" }}>
                <div className="inventory-list-row">
                  <strong>{insp.inspectionNumber}</strong>
                  <span className={`tag ${statusColor(insp.overallStatus)}`}>{insp.overallStatus}</span>
                </div>
                <small>GRN: {insp.grnNumber} ({insp.inspectionDate})</small>
              </div>
            ))}
          </article>
        </div>
      )}

      {/* ── TAB 5: PURCHASE RETURNS & DEBIT NOTES ─────────────────────────── */}
      {activeTab === "returns" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>Draft Purchase Return</h3>
            <form className="inventory-form" onSubmit={(e) => void submitPurchaseReturn(e)}>
              <label>Return number<input required maxLength={64} value={retNumber} onChange={(e) => setRetNumber(e.target.value)} placeholder="e.g. RET-2026-001" /></label>
              <label>Supplier
                <select required value={retSupplierId} onChange={(e) => setRetSupplierId(e.target.value)}>
                  <option value="">Select supplier</option>
                  {suppliers.map((s) => (
                    <option key={s.supplierId} value={s.supplierId}>{s.supplierCode} · {s.name}</option>
                  ))}
                </select>
              </label>
              <label>Related GRN (optional)
                <select value={retGrnId} onChange={(e) => setRetGrnId(e.target.value)}>
                  <option value="">Select GRN</option>
                  {grns.map((g) => (
                    <option key={g.grnId} value={g.grnId}>{g.grnNumber} ({g.poNumber})</option>
                  ))}
                </select>
              </label>
              <label>Return reason<input maxLength={2000} value={retReason} onChange={(e) => setRetReason(e.target.value)} placeholder="e.g. Defective batch recalled by supplier" /></label>

              <h4 className="inventory-subheading">Items to Return</h4>
              {retLines.map((line, idx) => (
                <div key={idx} style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap", marginBottom: "0.5rem" }}>
                  <select
                    value={line.variantId}
                    onChange={(e) => setRetLines(retLines.map((l, i) => i === idx ? { ...l, variantId: e.target.value } : l))}
                    style={{ flex: "2" }}
                  >
                    <option value="">Select variant</option>
                    {variants.map((v) => (
                      <option key={v.variantId} value={v.variantId}>{v.sku} · {v.productName}</option>
                    ))}
                  </select>
                  <select
                    value={line.binId}
                    onChange={(e) => {
                      const b = bins.find((bin) => bin.binId === e.target.value);
                      setRetLines(retLines.map((l, i) => i === idx ? { ...l, binId: e.target.value, warehouseId: b?.warehouseId ?? "" } : l));
                    }}
                    style={{ flex: "1" }}
                  >
                    <option value="">From bin</option>
                    {bins.filter((b) => b.isActive !== false).map((b) => (
                      <option key={b.binId} value={b.binId}>{b.code}</option>
                    ))}
                  </select>
                  <input
                    type="number" min="0.001" step="0.001" placeholder="Qty"
                    value={line.qty}
                    onChange={(e) => setRetLines(retLines.map((l, i) => i === idx ? { ...l, qty: e.target.value } : l))}
                    style={{ flex: "1", minWidth: "70px" }}
                  />
                  <input
                    type="number" min="0" step="0.01" placeholder="Unit cost"
                    value={line.cost}
                    onChange={(e) => setRetLines(retLines.map((l, i) => i === idx ? { ...l, cost: e.target.value } : l))}
                    style={{ flex: "1", minWidth: "80px" }}
                  />
                  {retLines.length > 1 && (
                    <button type="button" onClick={() => setRetLines(retLines.filter((_, i) => i !== idx))}>✕</button>
                  )}
                </div>
              ))}
              <button type="button" className="secondary-button" onClick={() => setRetLines([...retLines, { variantId: "", warehouseId: "", binId: "", qty: "", cost: "0", reason: "" }])}>
                + Add line
              </button>
              <button disabled={!enabled || isSaving || !retSupplierId || !retNumber} type="submit">
                Draft Purchase Return
              </button>
            </form>

            <h3 style={{ marginTop: "2rem" }}>Issue Supplier Debit Note</h3>
            <form className="inventory-form" onSubmit={(e) => void submitDebitNote(e)}>
              <label>Debit note number<input required maxLength={64} value={dnNumber} onChange={(e) => setDnNumber(e.target.value)} placeholder="e.g. DN-2026-001" /></label>
              <label>Supplier
                <select required value={dnSupplierId} onChange={(e) => setDnSupplierId(e.target.value)}>
                  <option value="">Select supplier</option>
                  {suppliers.map((s) => (
                    <option key={s.supplierId} value={s.supplierId}>{s.supplierCode} · {s.name}</option>
                  ))}
                </select>
              </label>
              <label>Related return (optional)
                <select value={dnReturnId} onChange={(e) => setDnReturnId(e.target.value)}>
                  <option value="">Select return</option>
                  {returns.map((r) => (
                    <option key={r.returnId} value={r.returnId}>{r.returnNumber} (PKR {r.totalAmount})</option>
                  ))}
                </select>
              </label>
              <label>Amount (PKR)<input required type="number" min="0.01" step="0.01" value={dnAmount} onChange={(e) => setDnAmount(e.target.value)} /></label>
              <label>Reason<input maxLength={2000} value={dnReason} onChange={(e) => setDnReason(e.target.value)} placeholder="Credit recovery for damaged goods" /></label>
              <button disabled={!enabled || isSaving || !dnSupplierId || !dnNumber} type="submit">
                Create Debit Note
              </button>
            </form>
          </article>

          <article className="inventory-card">
            <h3>Purchase Returns · {returns.length}</h3>
            {returns.length === 0
              ? <p className="inventory-empty">No purchase returns drafted yet.</p>
              : returns.map((r) => (
                <div key={r.returnId} style={{ borderBottom: "1px solid #e7ebf2", paddingBottom: "0.5rem", marginBottom: "0.5rem" }}>
                  <div className="inventory-list-row">
                    <strong>{r.returnNumber}</strong>
                    <span className={`tag ${statusColor(r.status)}`}>{r.status}</span>
                  </div>
                  <small>{r.supplierName} · Total: PKR {r.totalAmount.toLocaleString()} ({r.returnDate})</small>
                  {r.status === "draft" && (
                    <div style={{ marginTop: "4px" }}>
                      <button
                        className="secondary-button" style={{ fontSize: "0.75rem" }}
                        disabled={isSaving}
                        onClick={() => void approvePurchaseReturn(r.returnId)}
                        type="button"
                      >
                        Approve Return (Deduct Stock &amp; FIFO)
                      </button>
                    </div>
                  )}
                </div>
              ))}

            <h3 style={{ marginTop: "1.5rem" }}>Supplier Debit Notes · {debitNotes.length}</h3>
            {debitNotes.length === 0
              ? <p className="inventory-empty">No debit notes issued yet.</p>
              : debitNotes.map((dn) => (
                <div key={dn.debitNoteId} style={{ borderBottom: "1px solid #e7ebf2", paddingBottom: "0.5rem", marginBottom: "0.5rem" }}>
                  <div className="inventory-list-row">
                    <strong>{dn.debitNoteNumber}</strong>
                    <span className={`tag ${statusColor(dn.status)}`}>{dn.status}</span>
                  </div>
                  <small>{dn.supplierName} · Amount: PKR {dn.amount.toLocaleString()} ({dn.debitNoteDate})</small>
                  {dn.glJournalId && (
                    <div style={{ fontSize: "0.75rem", color: "#16a34a" }}>
                      Financial Journal: {dn.glJournalId} (Dr 2010 / Cr 1200)
                    </div>
                  )}
                  {dn.status === "draft" && (
                    <div style={{ marginTop: "4px" }}>
                      <button
                        className="secondary-button" style={{ fontSize: "0.75rem", fontWeight: 700 }}
                        disabled={isSaving}
                        onClick={() => void approveDebitNote(dn.debitNoteId)}
                        type="button"
                      >
                        Approve &amp; Post to GL (Dr 2010 / Cr 1200)
                      </button>
                    </div>
                  )}
                </div>
              ))}
          </article>
        </div>
      )}

      {/* ── TAB 6: SUPPLIER BALANCES & AP STATUS ─────────────────────────── */}
      {activeTab === "balances" && (
        <div>
          <div className="atp-grid" style={{ marginBottom: "1.5rem" }}>
            <div className="atp-card">
              <h4>Total Invoiced</h4>
              <div className="atp-metric-val" style={{ fontSize: "1.2rem" }}>PKR {totalInvoicedAll.toLocaleString()}</div>
              <small style={{ color: "#69758a" }}>Accounts Payable gross liabilities</small>
            </div>
            <div className="atp-card">
              <h4>Debit Notes Issued</h4>
              <div className="atp-metric-val" style={{ fontSize: "1.2rem", color: "#d9381e" }}>PKR {totalDebitNotesAll.toLocaleString()}</div>
              <small style={{ color: "#69758a" }}>Returns and adjustments against AP</small>
            </div>
            <div className="atp-card">
              <h4>Net Payable</h4>
              <div className="atp-metric-val" style={{ fontSize: "1.2rem", color: "#2563eb" }}>PKR {totalNetPayableAll.toLocaleString()}</div>
              <small style={{ color: "#69758a" }}>Total Invoiced − Debit Notes</small>
            </div>
            <div className="atp-card">
              <h4>Total Paid</h4>
              <div className="atp-metric-val" style={{ fontSize: "1.2rem", color: "#16a34a" }}>PKR {totalPaidAll.toLocaleString()}</div>
              <small style={{ color: "#69758a" }}>Disbursed payments</small>
            </div>
            <div className="atp-card">
              <h4>Outstanding AP Balance</h4>
              <div className="atp-metric-val" style={{ fontSize: "1.2rem", fontWeight: 900, color: totalOutstandingAll > 0 ? "#b45309" : "#16a34a" }}>
                PKR {totalOutstandingAll.toLocaleString()}
              </div>
              <small style={{ color: "#69758a" }}>Current liability in GL Account 2010</small>
            </div>
          </div>

          <article className="inventory-card">
            <h3>Supplier Accounts Payable Ledger ({supplierBalances.length})</h3>
            <div className="table-responsive">
              <table className="erp-table">
                <thead>
                  <tr>
                    <th>Supplier Code</th>
                    <th>Supplier Name</th>
                    <th>Currency</th>
                    <th>Total Invoiced</th>
                    <th>Debit Notes</th>
                    <th>Net Payable</th>
                    <th>Total Paid</th>
                    <th>Outstanding Balance</th>
                    <th>AP Status</th>
                  </tr>
                </thead>
                <tbody>
                  {supplierBalances.length === 0 ? (
                    <tr>
                      <td colSpan={9} style={{ textAlign: "center", padding: "16px", color: "#69758a" }}>
                        No supplier balances recorded.
                      </td>
                    </tr>
                  ) : (
                    supplierBalances.map((b) => (
                      <tr key={b.supplierId}>
                        <td><strong>{b.supplierCode}</strong></td>
                        <td>{b.name}</td>
                        <td>{b.currencyCode}</td>
                        <td>PKR {b.totalInvoiced.toLocaleString()}</td>
                        <td style={{ color: b.totalDebitNotes > 0 ? "#d9381e" : "inherit" }}>
                          {b.totalDebitNotes > 0 ? `−PKR ${b.totalDebitNotes.toLocaleString()}` : "0"}
                        </td>
                        <td><strong>PKR {b.netPayable.toLocaleString()}</strong></td>
                        <td>PKR {b.totalPaid.toLocaleString()}</td>
                        <td style={{ fontWeight: 700, color: b.outstandingBalance > 0 ? "#b45309" : "#16a34a" }}>
                          PKR {b.outstandingBalance.toLocaleString()}
                        </td>
                        <td>
                          <span className={`tag ${statusColor(b.paymentStatus)}`}>
                            {b.paymentStatus.replace("_", " ")}
                          </span>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </article>
        </div>
      )}
    </section>
  );
}
