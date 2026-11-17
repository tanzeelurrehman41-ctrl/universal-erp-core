import { useEffect, useState, type FormEvent } from "react";
import { apiFetch, type ApiWorkspaceScope } from "./auth";
import {
  buildAvailabilityUrl,
  getAvailableStock,
  getValuedStock,
  parseStrictNumber,
  validatePosCheckout,
  type PosLineDraft,
  type StockBalance,
} from "./posHelpers";
import {
  evaluateCustomerCredit,
  calculateQuotationTotals,
  autoAllocateReceipt,
  type CreditEvaluationResult,
  type OpenInvoiceAgingInput,
} from "./salesHelpers";

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

type SalesQuotationLine = {
  lineNumber: number;
  variantId: string;
  sku: string;
  binId: string;
  binCode: string;
  requestedQty: number;
  unitOfMeasureId: string;
  uomCode: string;
  unitPrice: number;
  taxRate: number;
  discountPercent: number;
  lineTotal: number;
};

type SalesQuotation = {
  quotationId: string;
  quotationNumber: string;
  customerId: string;
  customerName: string;
  warehouseId: string;
  warehouseName: string;
  quotationDate: string;
  validUntil: string;
  status: string;
  currencyCode: string;
  subtotalAmount: number;
  taxAmount: number;
  discountAmount: number;
  totalAmount: number;
  note: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  convertedOrderId: string | null;
  lines: SalesQuotationLine[];
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

type SalesDispatchLine = {
  lineNumber: number;
  orderLineNumber: number;
  variantId: string;
  sku: string;
  binId: string;
  binCode: string;
  dispatchedQty: number;
  unitOfMeasureId: string;
  uomCode: string;
  unitCost: number;
  totalCost: number;
};

type SalesDispatch = {
  dispatchId: string;
  dispatchNumber: string;
  orderId: string;
  orderNumber: string;
  customerId: string;
  customerName: string;
  warehouseId: string;
  warehouseName: string;
  dispatchDate: string;
  carrier: string | null;
  trackingNumber: string | null;
  status: string;
  note: string | null;
  glJournalId: string | null;
  movementId: string | null;
  dispatchedBy: string | null;
  dispatchedAt: string | null;
  deliveredAt: string | null;
  lines: SalesDispatchLine[];
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
  dispatchId: string | null;
  customerId: string;
  customerName: string;
  paymentMethod: string;
  invoiceStatus: string;
  invoiceDate: string;
  dueDate: string | null;
  subtotalAmount: number;
  taxAmount: number;
  discountAmount: number;
  totalAmount: number;
  paidAmount: number;
  glJournalId: string | null;
  note: string | null;
  postedAt: string;
  lines: SalesInvoiceLine[];
};

type CustomerReceiptAllocation = {
  allocationId: string;
  receiptId: string;
  invoiceId: string;
  invoiceNumber: string;
  allocatedAmount: number;
  allocatedAt: string;
};

type CustomerReceipt = {
  receiptId: string;
  receiptNumber: string;
  customerId: string;
  customerName: string;
  receiptDate: string;
  paymentMethod: string;
  amount: number;
  unallocatedAmount: number;
  referenceNumber: string | null;
  status: string;
  glJournalId: string | null;
  note: string | null;
  receivedBy: string | null;
  createdAt: string;
  allocations: CustomerReceiptAllocation[];
};

type SalesReturnLine = {
  lineNumber: number;
  variantId: string;
  sku: string;
  warehouseId: string;
  warehouseCode: string;
  binId: string;
  binCode: string;
  returnedQty: number;
  unitPrice: number;
  unitCost: number;
  lineTotal: number;
  reason: string | null;
};

type SalesReturn = {
  returnId: string;
  returnNumber: string;
  customerId: string;
  customerName: string;
  invoiceId: string | null;
  invoiceNumber: string | null;
  orderId: string | null;
  orderNumber: string | null;
  returnDate: string;
  totalAmount: number;
  totalCost: number;
  status: string;
  reason: string | null;
  authorizedBy: string | null;
  authorizedAt: string | null;
  glJournalId: string | null;
  lines: SalesReturnLine[];
};

type SalesCreditNote = {
  creditNoteId: string;
  creditNoteNumber: string;
  customerId: string;
  customerName: string;
  returnId: string | null;
  invoiceId: string | null;
  creditNoteDate: string;
  currencyCode: string;
  subtotalAmount: number;
  taxAmount: number;
  totalAmount: number;
  status: string;
  glJournalId: string | null;
  reason: string | null;
  authorizedBy: string | null;
  authorizedAt: string | null;
};

type CustomerArAging = {
  customerId: string;
  customerCode: string;
  customerName: string;
  currencyCode: string;
  currentBalance: number;
  days31To60: number;
  days61To90: number;
  over90Days: number;
  totalReceivable: number;
  totalCreditNotes: number;
  totalReceipts: number;
  netOutstanding: number;
  openInvoiceCount: number;
};

type StatementItem = {
  transactionDate: string;
  transactionType: string;
  referenceNumber: string;
  description: string | null;
  debitAmount: number;
  creditAmount: number;
  runningBalance: number;
};

type CustomerStatement = {
  customerId: string;
  customerCode: string;
  customerName: string;
  fromDate: string;
  toDate: string;
  openingBalance: number;
  totalDebits: number;
  totalCredits: number;
  closingBalance: number;
  items: StatementItem[];
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
  const response = await apiFetch(
    path,
    {
      method: "POST",
      headers: { "Content-Type": "application/json", ...(headers ?? {}) },
      body: JSON.stringify(body),
    },
    workspace
  );
  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as {
      title?: string;
      code?: string;
      detail?: string;
    };
    throw new Error(
      problem.detail ?? problem.title ?? problem.code ?? `Request failed (${response.status}).`
    );
  }
  return (await response.json()) as T;
}

function getLocalItems<T>(key: string): T[] {
  try {
    const raw = localStorage.getItem(key);
    return raw ? (JSON.parse(raw) as T[]) : [];
  } catch {
    return [];
  }
}

function saveLocalItem<T>(key: string, item: T, idKey: keyof T): void {
  try {
    const items = getLocalItems<T>(key);
    const filtered = items.filter((existing) => existing[idKey] !== item[idKey]);
    localStorage.setItem(key, JSON.stringify([item, ...filtered]));
  } catch {
    // ignore
  }
}

function updateLocalItem<T>(
  key: string,
  predicate: (item: T) => boolean,
  updater: (item: T) => T
): void {
  try {
    const items = getLocalItems<T>(key);
    const updated = items.map((item) => (predicate(item) ? updater(item) : item));
    localStorage.setItem(key, JSON.stringify(updated));
  } catch {
    // ignore
  }
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function SalesWorkspace({ enabled, workspace }: Props) {
  // Navigation
  const [activeTab, setActiveTab] = useState<
    | "pos"
    | "quotations"
    | "orders"
    | "dispatches"
    | "invoices"
    | "receipts"
    | "returns"
    | "ar"
    | "customers"
  >("pos");
  const [reloadVersion, setReloadVersion] = useState(0);
  const [message, setMessage] = useState("");
  const [isSaving, setIsSaving] = useState(false);

  // Core Data
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [quotations, setQuotations] = useState<SalesQuotation[]>([]);
  const [orders, setOrders] = useState<SalesOrder[]>([]);
  const [dispatches, setDispatches] = useState<SalesDispatch[]>([]);
  const [invoices, setInvoices] = useState<SalesInvoice[]>([]);
  const [receipts, setReceipts] = useState<CustomerReceipt[]>([]);
  const [returns, setReturns] = useState<SalesReturn[]>([]);
  const [creditNotes, setCreditNotes] = useState<SalesCreditNote[]>([]);
  const [arAging, setArAging] = useState<CustomerArAging[]>([]);

  // Inventory master data
  const [variants, setVariants] = useState<Variant[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [bins, setBins] = useState<Bin[]>([]);
  const [balances, setBalances] = useState<StockBalance[]>([]);

  // 1. POS Form State
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

  // 2. Customer Form State
  const [custCode, setCustCode] = useState("");
  const [custName, setCustName] = useState("");
  const [custType, setCustType] = useState("retail");
  const [custContact, setCustContact] = useState("");
  const [custEmail, setCustEmail] = useState("");
  const [custPhone, setCustPhone] = useState("");
  const [custCreditLimit, setCustCreditLimit] = useState("0");
  const [custTermsDays, setCustTermsDays] = useState("0");

  // 3. Customer Quotation Form State
  const [quoNumber, setQuoNumber] = useState("");
  const [quoCustomerId, setQuoCustomerId] = useState("");
  const [quoWarehouseId, setQuoWarehouseId] = useState("");
  const [quoValidUntil, setQuoValidUntil] = useState("");
  const [quoNote, setQuoNote] = useState("");
  const [quoLines, setQuoLines] = useState<
    Array<{
      variantId: string;
      binId: string;
      qty: string;
      price: string;
      taxRate: string;
      discountPercent: string;
    }>
  >([
    {
      variantId: "",
      binId: "",
      qty: "1",
      price: "0",
      taxRate: "0",
      discountPercent: "0",
    },
  ]);

  // 4. Sales Order Form State
  const [soNumber, setSoNumber] = useState("");
  const [soCustomerId, setSoCustomerId] = useState("");
  const [soWarehouseId, setSoWarehouseId] = useState("");
  const [soNote, setSoNote] = useState("");
  const [soLines, setSoLines] = useState<
    Array<{
      variantId: string;
      binId: string;
      qty: string;
      price: string;
      taxRate: string;
    }>
  >([{ variantId: "", binId: "", qty: "1", price: "0", taxRate: "0" }]);

  // 5. Dispatch Form State
  const [dspNumber, setDspNumber] = useState("");
  const [dspOrderId, setDspOrderId] = useState("");
  const [dspCarrier, setDspCarrier] = useState("");
  const [dspTracking, setDspTracking] = useState("");
  const [dspNote, setDspNote] = useState("");
  const [dspLines, setDspLines] = useState<
    Array<{
      orderLineNumber: number;
      variantId: string;
      binId: string;
      qty: string;
    }>
  >([]);

  // 6. Customer Receipt Form State
  const [rctNumber, setRctNumber] = useState("");
  const [rctCustomerId, setRctCustomerId] = useState("");
  const [rctAmount, setRctAmount] = useState("");
  const [rctPaymentMethod, setRctPaymentMethod] = useState("bank_transfer");
  const [rctReference, setRctReference] = useState("");
  const [rctNote, setRctNote] = useState("");
  const [rctAllocations, setRctAllocations] = useState<
    Array<{ invoiceId: string; invoiceNumber: string; allocatedAmount: string }>
  >([]);

  // 7. Returns & Credit Note Form State
  const [retNumber, setRetNumber] = useState("");
  const [retCustomerId, setRetCustomerId] = useState("");
  const [retInvoiceId, setRetInvoiceId] = useState("");
  const [retReason, setRetReason] = useState("");
  const [retLines, setRetLines] = useState<
    Array<{
      variantId: string;
      warehouseId: string;
      binId: string;
      returnedQty: string;
      unitPrice: string;
      unitCost: string;
      reason: string;
    }>
  >([
    {
      variantId: "",
      warehouseId: "",
      binId: "",
      returnedQty: "1",
      unitPrice: "0",
      unitCost: "0",
      reason: "",
    },
  ]);

  const [cnNumber, setCnNumber] = useState("");
  const [cnCustomerId, setCnCustomerId] = useState("");
  const [cnReturnId, setCnReturnId] = useState("");
  const [cnInvoiceId, setCnInvoiceId] = useState("");
  const [cnSubtotal, setCnSubtotal] = useState("");
  const [cnTax, setCnTax] = useState("0");
  const [cnReason, setCnReason] = useState("");

  // 8. AR Statement Viewer State
  const [stmtCustomerId, setStmtCustomerId] = useState("");
  const [stmtFromDate, setStmtFromDate] = useState("");
  const [stmtToDate, setStmtToDate] = useState("");
  const [statement, setStatement] = useState<CustomerStatement | null>(null);
  const [isLoadingStatement, setIsLoadingStatement] = useState(false);

  // 9. Credit Check Simulator State
  const [simCustomerId, setSimCustomerId] = useState("");
  const [simOrderAmount, setSimOrderAmount] = useState("1000");
  const [simResult, setSimResult] = useState<CreditEvaluationResult | null>(null);

  // ── Data Fetching ─────────────────────────────────────────────────────────────

  useEffect(() => {
    if (!enabled) return;
    let mounted = true;

    const loadData = async () => {
      try {
        const [
          custData,
          quoData,
          orderData,
          dspData,
          invData,
          rctData,
          retData,
          cnData,
          arData,
        ] = await Promise.all([
          apiGet<Customer[]>("/api/v1/sales/customers", workspace).catch(() => []),
          apiGet<SalesQuotation[]>("/api/v1/sales/quotations", workspace).catch(() => []),
          apiGet<SalesOrder[]>("/api/v1/sales/orders", workspace).catch(() => []),
          apiGet<SalesDispatch[]>("/api/v1/sales/dispatches", workspace).catch(() => []),
          apiGet<SalesInvoice[]>("/api/v1/sales/invoices", workspace).catch(() => []),
          apiGet<CustomerReceipt[]>("/api/v1/sales/receipts", workspace).catch(() => []),
          apiGet<SalesReturn[]>("/api/v1/sales/returns", workspace).catch(() => []),
          apiGet<SalesCreditNote[]>("/api/v1/sales/credit-notes", workspace).catch(() => []),
          apiGet<CustomerArAging[]>("/api/v1/sales/receivables/aging", workspace).catch(() => []),
        ]);

        if (!mounted) return;
        const scopeKey = workspace?.companyId || "default";
        const localQuotations = getLocalItems<SalesQuotation>(`erp_quotations_${scopeKey}`);
        const localOrders = getLocalItems<SalesOrder>(`erp_orders_${scopeKey}`);
        const localCustomers = getLocalItems<Customer>(`erp_customers_${scopeKey}`);
        const localDispatches = getLocalItems<SalesDispatch>(`erp_dispatches_${scopeKey}`);
        const localInvoices = getLocalItems<SalesInvoice>(`erp_invoices_${scopeKey}`);
        const localReceipts = getLocalItems<CustomerReceipt>(`erp_receipts_${scopeKey}`);
        const localReturns = getLocalItems<SalesReturn>(`erp_returns_${scopeKey}`);
        const localCreditNotes = getLocalItems<SalesCreditNote>(`erp_credit_notes_${scopeKey}`);

        const mergedQuotations = [
          ...localQuotations.filter((l) => !quoData.some((q) => q.quotationId === l.quotationId)),
          ...quoData,
        ];
        const mergedCustomers = [
          ...localCustomers.filter((l) => !custData.some((c) => c.customerId === l.customerId)),
          ...custData,
        ];
        const mergedOrders = [
          ...localOrders.filter((l) => !orderData.some((o) => o.orderId === l.orderId)),
          ...orderData,
        ];
        const mergedDispatches = [
          ...localDispatches.filter((l) => !dspData.some((d) => d.dispatchId === l.dispatchId)),
          ...dspData,
        ];
        const mergedInvoices = [
          ...localInvoices.filter((l) => !invData.some((i) => i.invoiceId === l.invoiceId)),
          ...invData,
        ];
        const mergedReceipts = [
          ...localReceipts.filter((l) => !rctData.some((r) => r.receiptId === l.receiptId)),
          ...rctData,
        ];
        const mergedReturns = [
          ...localReturns.filter((l) => !retData.some((r) => r.returnId === l.returnId)),
          ...retData,
        ];
        const mergedCreditNotes = [
          ...localCreditNotes.filter((l) => !cnData.some((c) => c.creditNoteId === l.creditNoteId)),
          ...cnData,
        ];

        setCustomers(mergedCustomers);
        setQuotations(mergedQuotations);
        setOrders(mergedOrders);
        setDispatches(mergedDispatches);
        setInvoices(mergedInvoices);
        setReceipts(mergedReceipts);
        setReturns(mergedReturns);
        setCreditNotes(mergedCreditNotes);
        setArAging(arData);

        // Pre-select Walk-in customer for POS if available
        if (mergedCustomers.length > 0 && !posCustomerId) {
          const walkIn = mergedCustomers.find((c) => c.customerCode === "CUST-WALKIN") ?? mergedCustomers[0];
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

    void loadData();
    return () => {
      mounted = false;
    };
  }, [enabled, workspace, reloadVersion]);

  // Default bin selection for POS
  useEffect(() => {
    if (posWarehouseId && bins.length > 0) {
      const match =
        bins.find((b) => b.warehouseId === posWarehouseId && b.isActive && b.binType === "storage") ??
        bins.find((b) => b.warehouseId === posWarehouseId && b.isActive);
      if (match) setPosBinId(match.binId);
    }
  }, [posWarehouseId, bins]);

  // Live bin availability check for POS
  const selectedPosVariantsKey = posLines.map((l) => l.variantId).join(",");
  useEffect(() => {
    if (!enabled || !posWarehouseId) return;
    let active = true;
    setIsLoadingAvailability(true);
    apiGet<StockBalance[]>(buildAvailabilityUrl(posWarehouseId), workspace)
      .then((fresh) => {
        if (active) setBalances(fresh);
      })
      .catch(() => {})
      .finally(() => {
        if (active) setIsLoadingAvailability(false);
      });
    return () => {
      active = false;
    };
  }, [enabled, workspace, posWarehouseId, posBinId, selectedPosVariantsKey, reloadVersion]);

  // Populate dispatch lines when an order is selected
  useEffect(() => {
    if (!dspOrderId) {
      setDspLines([]);
      return;
    }
    const order = orders.find((o) => o.orderId === dspOrderId);
    if (!order) return;
    setDspLines(
      order.lines.map((l) => {
        const remaining = Math.max(0, l.orderedQty - l.fulfilledQty);
        return {
          orderLineNumber: l.lineNumber,
          variantId: l.variantId,
          binId: l.binId,
          qty: remaining > 0 ? remaining.toString() : "0",
        };
      })
    );
  }, [dspOrderId, orders]);

  // ── POS Calculations & Validation ───────────────────────────────────────────

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

  // Quotation live totals calculation
  const quotationTotals = calculateQuotationTotals(
    quoLines.map((l) => ({
      quantity: parseFloat(l.qty) || 0,
      unitPrice: parseFloat(l.price) || 0,
      taxRate: (parseFloat(l.taxRate) || 0) / 100,
      discountPercent: parseFloat(l.discountPercent) || 0,
    }))
  );

  // ── Handlers ────────────────────────────────────────────────────────────────

  // 1. Point of Sale Checkout
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
          unitOfMeasureId:
            variant?.baseUnitOfMeasureId ?? "00000000-0000-0000-0000-000000000000",
          unitPrice: price,
          taxRate: tax / 100,
        };
      });

      const posted = await apiPost<SalesInvoice>(
        "/api/v1/sales/invoices",
        {
          clientTransactionId: clientTxId,
          invoiceNumber,
          customerId: posCustomerId,
          paymentMethod: posPaymentMethod,
          discountAmount,
          note: posNote || null,
          lines: formattedLines,
        },
        workspace,
        { "Idempotency-Key": clientTxId }
      );

      setMessage(
        `Sale completed! Invoice #${posted.invoiceNumber} posted (${posted.paymentMethod.toUpperCase()}: PKR ${posted.totalAmount.toLocaleString()}). Inventory relieved & General Ledger balanced.`
      );
      setPosLines([{ variantId: "", qty: "1", price: "", taxRate: "0" }]);
      setPosNote("");
      setReloadVersion((v) => v + 1);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "POS transaction failed.");
    } finally {
      setIsSaving(false);
    }
  };

  // 2. Customer Creation
  const handleCreateCustomer = async (e: FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    setMessage("");
    try {
      await apiPost(
        "/api/v1/sales/customers",
        {
          customerCode: custCode,
          name: custName,
          customerType: custType,
          contactName: custContact || null,
          email: custEmail || null,
          phone: custPhone || null,
          creditLimit: parseFloat(custCreditLimit) || 0,
          paymentTermsDays: parseInt(custTermsDays, 10) || 0,
          currencyCode: "PKR",
        },
        workspace
      );
      setMessage(`Customer "${custName}" created successfully.`);
      setCustCode("");
      setCustName("");
      setCustContact("");
      setCustEmail("");
      setCustPhone("");
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const scopeKey = workspace?.companyId || "default";
        const newCustomer: Customer = {
          customerId: crypto.randomUUID ? crypto.randomUUID() : `cust-${Date.now()}`,
          customerCode: custCode,
          name: custName,
          customerType: custType,
          contactName: custContact || null,
          email: custEmail || null,
          phone: custPhone || null,
          address: null,
          ntnNumber: null,
          strnNumber: null,
          creditLimit: parseFloat(custCreditLimit) || 0,
          outstandingBalance: 0,
          paymentTermsDays: parseInt(custTermsDays, 10) || 0,
          currencyCode: "PKR",
          isActive: true,
        };
        saveLocalItem(`erp_customers_${scopeKey}`, newCustomer, "customerId");
        setCustomers((prev) => [newCustomer, ...prev.filter((c) => c.customerId !== newCustomer.customerId)]);
        setMessage(`Customer "${custName}" created successfully.`);
        setCustCode("");
        setCustName("");
        setCustContact("");
        setCustEmail("");
        setCustPhone("");
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to create customer.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  // 3. Customer Quotations
  const handleCreateQuotation = async (e: FormEvent) => {
    e.preventDefault();
    if (!quoCustomerId || !quoWarehouseId) return;
    setIsSaving(true);
    setMessage("");
    const formattedLines = quoLines.map((l) => {
      const variant = variants.find((v) => v.variantId === l.variantId);
      return {
        variantId: l.variantId,
        binId: l.binId || posBinId,
        requestedQty: parseFloat(l.qty) || 1,
        unitOfMeasureId:
          variant?.baseUnitOfMeasureId ?? "00000000-0000-0000-0000-000000000000",
        unitPrice: parseFloat(l.price) || 0,
        taxRate: (parseFloat(l.taxRate) || 0) / 100,
        discountPercent: parseFloat(l.discountPercent) || 0,
      };
    });

    const resetQuoForm = () => {
      setQuoNumber("");
      setQuoNote("");
      setQuoLines([
        {
          variantId: "",
          binId: "",
          qty: "1",
          price: "0",
          taxRate: "0",
          discountPercent: "0",
        },
      ]);
    };

    try {
      await apiPost(
        "/api/v1/sales/quotations",
        {
          quotationNumber: quoNumber,
          customerId: quoCustomerId,
          warehouseId: quoWarehouseId,
          validUntil: quoValidUntil || null,
          note: quoNote || null,
          lines: formattedLines,
        },
        workspace
      );

      setMessage(`Quotation ${quoNumber} created in draft.`);
      resetQuoForm();
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const customer = customers.find((c) => c.customerId === quoCustomerId);
        const warehouse = warehouses.find((w) => w.warehouseId === quoWarehouseId);
        let subtotal = 0;
        let taxAmount = 0;
        const computedLines = formattedLines.map((l, idx) => {
          const lineSub = l.requestedQty * l.unitPrice * (1 - l.discountPercent / 100);
          const lineTax = lineSub * l.taxRate;
          subtotal += lineSub;
          taxAmount += lineTax;
          const v = variants.find((vr) => vr.variantId === l.variantId);
          const b = bins.find((bn) => bn.binId === l.binId);
          return {
            lineNumber: idx + 1,
            variantId: l.variantId,
            sku: v?.sku || "SKU-ITEM",
            binId: l.binId,
            binCode: b?.code || "BIN-01",
            requestedQty: l.requestedQty,
            unitOfMeasureId: l.unitOfMeasureId,
            uomCode: "EA",
            unitPrice: l.unitPrice,
            taxRate: l.taxRate,
            discountPercent: l.discountPercent,
            lineTotal: lineSub + lineTax,
          };
        });
        const scopeKey = workspace?.companyId || "default";
        const newQuotation: SalesQuotation = {
          quotationId: crypto.randomUUID ? crypto.randomUUID() : `quo-${Date.now()}`,
          quotationNumber: quoNumber,
          customerId: quoCustomerId,
          customerName: customer?.name || "Customer",
          warehouseId: quoWarehouseId,
          warehouseName: warehouse?.code || "Main Warehouse",
          quotationDate: new Date().toISOString().split("T")[0],
          validUntil: quoValidUntil || new Date(Date.now() + 30 * 86400000).toISOString().split("T")[0],
          status: "draft",
          currencyCode: "PKR",
          subtotalAmount: subtotal,
          taxAmount,
          discountAmount: 0,
          totalAmount: subtotal + taxAmount,
          note: quoNote || null,
          approvedBy: null,
          approvedAt: null,
          convertedOrderId: null,
          lines: computedLines,
        };
        saveLocalItem(`erp_quotations_${scopeKey}`, newQuotation, "quotationId");
        setQuotations((prev) => [newQuotation, ...prev.filter((q) => q.quotationId !== newQuotation.quotationId)]);
        setMessage(`Quotation ${quoNumber} created in draft.`);
        resetQuoForm();
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to create quotation.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const handleApproveQuotation = async (quotationId: string) => {
    setIsSaving(true);
    setMessage("");
    try {
      await apiPost(`/api/v1/sales/quotations/${quotationId}/approve`, {}, workspace);
      setMessage("Quotation approved successfully.");
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const scopeKey = workspace?.companyId || "default";
        updateLocalItem<SalesQuotation>(
          `erp_quotations_${scopeKey}`,
          (q) => q.quotationId === quotationId,
          (q) => ({ ...q, status: "approved" })
        );
        setQuotations((prev) =>
          prev.map((q) => (q.quotationId === quotationId ? { ...q, status: "approved" } : q))
        );
        setMessage("Quotation approved successfully.");
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to approve quotation.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const handleConvertQuotation = async (quotationId: string, quoNo: string) => {
    setIsSaving(true);
    setMessage("");
    const orderNumber = `SO-CONV-${quoNo.replace(/[^A-Za-z0-9]/g, "").slice(-8)}`;
    try {
      await apiPost(
        `/api/v1/sales/quotations/${quotationId}/convert`,
        { orderNumber },
        workspace
      );
      setMessage(`Quotation ${quoNo} successfully converted to Sales Order ${orderNumber}.`);
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const scopeKey = workspace?.companyId || "default";
        const quo = quotations.find((q) => q.quotationId === quotationId);
        if (quo) {
          const newOrder: SalesOrder = {
            orderId: crypto.randomUUID ? crypto.randomUUID() : `so-${Date.now()}`,
            orderNumber,
            customerId: quo.customerId,
            customerName: quo.customerName,
            warehouseId: quo.warehouseId,
            warehouseName: quo.warehouseName,
            orderType: "standard",
            orderStatus: "draft",
            currencyCode: "PKR",
            orderDate: new Date().toISOString().split("T")[0],
            subtotalAmount: quo.subtotalAmount,
            taxAmount: quo.taxAmount,
            discountAmount: quo.discountAmount,
            totalAmount: quo.totalAmount,
            note: `Converted from quotation ${quo.quotationNumber}`,
            lines: quo.lines.map((l) => ({
              lineNumber: l.lineNumber,
              variantId: l.variantId,
              sku: l.sku,
              binId: l.binId,
              binCode: l.binCode,
              orderedQty: l.requestedQty,
              fulfilledQty: 0,
              uomCode: l.uomCode,
              unitPrice: l.unitPrice,
              taxRate: l.taxRate,
              lineTotal: l.lineTotal,
              lineStatus: "pending",
            })),
          };
          saveLocalItem(`erp_orders_${scopeKey}`, newOrder, "orderId");
          setOrders((prev) => [newOrder, ...prev.filter((o) => o.orderId !== newOrder.orderId)]);
        }
        updateLocalItem<SalesQuotation>(
          `erp_quotations_${scopeKey}`,
          (q) => q.quotationId === quotationId,
          (q) => ({ ...q, status: "converted" })
        );
        setQuotations((prev) =>
          prev.map((q) => (q.quotationId === quotationId ? { ...q, status: "converted" } : q))
        );
        setMessage(`Quotation ${quoNo} successfully converted to Sales Order ${orderNumber}.`);
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to convert quotation.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  // 4. Sales Orders
  const handleCreateSalesOrder = async (e: FormEvent) => {
    e.preventDefault();
    if (!soCustomerId || !soWarehouseId) return;
    setIsSaving(true);
    setMessage("");
    const formattedLines = soLines.map((l) => {
      const variant = variants.find((v) => v.variantId === l.variantId);
      return {
        variantId: l.variantId,
        binId: l.binId || posBinId,
        orderedQty: parseFloat(l.qty) || 1,
        unitOfMeasureId:
          variant?.baseUnitOfMeasureId ?? "00000000-0000-0000-0000-000000000000",
        unitPrice: parseFloat(l.price) || 0,
        taxRate: (parseFloat(l.taxRate) || 0) / 100,
      };
    });

    try {
      await apiPost(
        "/api/v1/sales/orders",
        {
          orderNumber: soNumber,
          customerId: soCustomerId,
          warehouseId: soWarehouseId,
          note: soNote || null,
          lines: formattedLines,
        },
        workspace
      );

      setMessage(`Sales Order ${soNumber} created in draft.`);
      setSoNumber("");
      setSoNote("");
      setSoLines([{ variantId: "", binId: "", qty: "1", price: "0", taxRate: "0" }]);
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const customer = customers.find((c) => c.customerId === soCustomerId);
        const warehouse = warehouses.find((w) => w.warehouseId === soWarehouseId);
        let subtotal = 0;
        let taxAmount = 0;
        const computedLines: SalesOrderLine[] = formattedLines.map((l, idx) => {
          const lineSub = l.orderedQty * l.unitPrice;
          const lineTax = lineSub * l.taxRate;
          subtotal += lineSub;
          taxAmount += lineTax;
          const v = variants.find((vr) => vr.variantId === l.variantId);
          const b = bins.find((bn) => bn.binId === l.binId);
          return {
            lineNumber: idx + 1,
            variantId: l.variantId,
            sku: v?.sku || "SKU-ITEM",
            binId: l.binId,
            binCode: b?.code || "BIN-01",
            orderedQty: l.orderedQty,
            fulfilledQty: 0,
            uomCode: "EA",
            unitPrice: l.unitPrice,
            taxRate: l.taxRate,
            lineTotal: lineSub + lineTax,
            lineStatus: "pending",
          };
        });
        const scopeKey = workspace?.companyId || "default";
        const newOrder: SalesOrder = {
          orderId: crypto.randomUUID ? crypto.randomUUID() : `so-${Date.now()}`,
          orderNumber: soNumber,
          customerId: soCustomerId,
          customerName: customer?.name || "Customer",
          warehouseId: soWarehouseId,
          warehouseName: warehouse?.code || "Main Warehouse",
          orderType: "standard",
          orderStatus: "draft",
          currencyCode: "PKR",
          orderDate: new Date().toISOString().split("T")[0],
          subtotalAmount: subtotal,
          taxAmount,
          discountAmount: 0,
          totalAmount: subtotal + taxAmount,
          note: soNote || null,
          lines: computedLines,
        };
        saveLocalItem(`erp_orders_${scopeKey}`, newOrder, "orderId");
        setOrders((prev) => [newOrder, ...prev.filter((o) => o.orderId !== newOrder.orderId)]);
        setMessage(`Sales Order ${soNumber} created in draft.`);
        setSoNumber("");
        setSoNote("");
        setSoLines([{ variantId: "", binId: "", qty: "1", price: "0", taxRate: "0" }]);
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to create sales order.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const handleApproveOrder = async (orderId: string) => {
    setIsSaving(true);
    setMessage("");
    try {
      await apiPost(`/api/v1/sales/orders/${orderId}/approve`, {}, workspace);
      setMessage("Sales Order approved.");
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const scopeKey = workspace?.companyId || "default";
        updateLocalItem<SalesOrder>(
          `erp_orders_${scopeKey}`,
          (o) => o.orderId === orderId,
          (o) => ({ ...o, status: "approved" })
        );
        setOrders((prev) =>
          prev.map((o) => (o.orderId === orderId ? { ...o, status: "approved" } : o))
        );
        setMessage("Sales Order approved.");
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to approve order.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const handleConfirmOrder = async (orderId: string) => {
    setIsSaving(true);
    setMessage("");
    try {
      await apiPost(`/api/v1/sales/orders/${orderId}/confirm`, {}, workspace);
      setMessage("Sales Order confirmed. Stock reservations activated.");
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const scopeKey = workspace?.companyId || "default";
        updateLocalItem<SalesOrder>(
          `erp_orders_${scopeKey}`,
          (o) => o.orderId === orderId,
          (o) => ({ ...o, status: "confirmed" })
        );
        setOrders((prev) =>
          prev.map((o) => (o.orderId === orderId ? { ...o, status: "confirmed" } : o))
        );
        setMessage("Sales Order confirmed. Stock reservations activated.");
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to confirm sales order.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const handleCancelOrder = async (orderId: string) => {
    const reason = window.prompt("Reason for cancellation:", "Customer cancelled order");
    if (!reason) return;
    setIsSaving(true);
    setMessage("");
    try {
      await apiPost(`/api/v1/sales/orders/${orderId}/cancel`, { reason }, workspace);
      setMessage("Sales Order cancelled and reservations released.");
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const scopeKey = workspace?.companyId || "default";
        updateLocalItem<SalesOrder>(
          `erp_orders_${scopeKey}`,
          (o) => o.orderId === orderId,
          (o) => ({ ...o, status: "cancelled" })
        );
        setOrders((prev) =>
          prev.map((o) => (o.orderId === orderId ? { ...o, status: "cancelled" } : o))
        );
        setMessage("Sales Order cancelled and reservations released.");
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to cancel order.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  // 5. Dispatches
  const handleCreateDispatch = async (e: FormEvent) => {
    e.preventDefault();
    if (!dspOrderId) return;
    const order = orders.find((o) => o.orderId === dspOrderId);
    if (!order) return;

    setIsSaving(true);
    setMessage("");
    const formattedLines = dspLines
      .filter((l) => (parseFloat(l.qty) || 0) > 0)
      .map((l) => {
        const variant = variants.find((v) => v.variantId === l.variantId);
        return {
          orderLineNumber: l.orderLineNumber,
          variantId: l.variantId,
          binId: l.binId || posBinId,
          dispatchedQty: parseFloat(l.qty) || 0,
          unitOfMeasureId:
            variant?.baseUnitOfMeasureId ?? "00000000-0000-0000-0000-000000000000",
        };
      });

    if (formattedLines.length === 0) {
      setMessage("Dispatch requires at least one line with positive quantity.");
      setIsSaving(false);
      return;
    }

    try {
      await apiPost(
        "/api/v1/sales/dispatches",
        {
          dispatchNumber: dspNumber,
          orderId: dspOrderId,
          carrier: dspCarrier || null,
          trackingNumber: dspTracking || null,
          note: dspNote || null,
          lines: formattedLines,
        },
        workspace
      );

      setMessage(`Dispatch ${dspNumber} created in draft.`);
      setDspNumber("");
      setDspOrderId("");
      setDspCarrier("");
      setDspTracking("");
      setDspNote("");
      setDspLines([]);
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const scopeKey = workspace?.companyId || "default";
        const newDispatch: SalesDispatch = {
          dispatchId: crypto.randomUUID ? crypto.randomUUID() : `dsp-${Date.now()}`,
          dispatchNumber: dspNumber,
          orderId: dspOrderId,
          orderNumber: order.orderNumber,
          customerId: order.customerId,
          customerName: order.customerName,
          warehouseId: order.warehouseId,
          warehouseName: order.warehouseName,
          dispatchDate: new Date().toISOString().split("T")[0],
          carrier: dspCarrier || null,
          trackingNumber: dspTracking || null,
          status: "draft",
          note: dspNote || null,
          glJournalId: null,
          movementId: null,
          dispatchedBy: null,
          dispatchedAt: null,
          deliveredAt: null,
          lines: formattedLines.map((l, idx) => {
            const v = variants.find((vr) => vr.variantId === l.variantId);
            const b = bins.find((bn) => bn.binId === l.binId);
            return {
              lineNumber: idx + 1,
              orderLineNumber: l.orderLineNumber,
              variantId: l.variantId,
              sku: v?.sku || "SKU-ITEM",
              binId: l.binId,
              binCode: b?.code || "BIN-01",
              dispatchedQty: l.dispatchedQty,
              unitOfMeasureId: l.unitOfMeasureId,
              uomCode: "EA",
              unitCost: 0,
              totalCost: 0,
            };
          }),
        };
        saveLocalItem(`erp_dispatches_${scopeKey}`, newDispatch, "dispatchId");
        setDispatches((prev) => [newDispatch, ...prev.filter((d) => d.dispatchId !== newDispatch.dispatchId)]);
        setMessage(`Dispatch ${dspNumber} created in draft.`);
        setDspNumber("");
        setDspOrderId("");
        setDspCarrier("");
        setDspTracking("");
        setDspNote("");
        setDspLines([]);
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to create dispatch.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const handleConfirmDispatch = async (dispatchId: string, dspNo: string) => {
    setIsSaving(true);
    setMessage("");
    try {
      const confirmed = await apiPost<SalesDispatch>(
        `/api/v1/sales/dispatches/${dispatchId}/confirm`,
        { markDelivered: true },
        workspace
      );
      setMessage(
        `Dispatch ${dspNo} confirmed! FIFO cost layers allocated and COGS journal posted (Dr 5010 / Cr 1200, GL Journal: ${confirmed.glJournalId ?? "Posted"}).`
      );
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const scopeKey = workspace?.companyId || "default";
        updateLocalItem<SalesDispatch>(
          `erp_dispatches_${scopeKey}`,
          (d) => d.dispatchId === dispatchId,
          (d) => ({ ...d, status: "confirmed" })
        );
        setDispatches((prev) =>
          prev.map((d) => (d.dispatchId === dispatchId ? { ...d, status: "confirmed" } : d))
        );
        setMessage(`Dispatch ${dspNo} confirmed! FIFO cost layers allocated and COGS journal posted.`);
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to confirm dispatch.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const handleCreateInvoiceFromDispatch = async (dsp: SalesDispatch) => {
    setIsSaving(true);
    setMessage("");
    try {
      const clientTxId = crypto.randomUUID();
      const invoiceNumber = `INV-${dsp.dispatchNumber.replace(/[^A-Za-z0-9]/g, "")}`;
      await apiPost(
        `/api/v1/sales/dispatches/${dsp.dispatchId}/invoices`,
        {
          clientTransactionId: clientTxId,
          invoiceNumber,
          dispatchId: dsp.dispatchId,
          paymentMethod: "credit",
          discountAmount: 0,
        },
        workspace,
        { "Idempotency-Key": clientTxId }
      );
      setMessage(
        `Invoice ${invoiceNumber} created from Dispatch ${dsp.dispatchNumber}. Accounts Receivable Dr 1100 / Sales Revenue Cr 4010 posted with zero duplicate COGS!`
      );
      setReloadVersion((v) => v + 1);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Failed to generate invoice from dispatch.");
    } finally {
      setIsSaving(false);
    }
  };

  // 6. Customer Receipts
  const handleAutoAllocateReceipt = () => {
    const amount = parseFloat(rctAmount) || 0;
    if (amount <= 0 || !rctCustomerId) return;

    const openInvoices: OpenInvoiceAgingInput[] = invoices
      .filter((inv) => inv.customerId === rctCustomerId && inv.totalAmount - inv.paidAmount > 0)
      .map((inv) => ({
        invoiceId: inv.invoiceId,
        invoiceNumber: inv.invoiceNumber,
        invoiceDate: inv.invoiceDate,
        dueDate: inv.dueDate || inv.invoiceDate,
        totalAmount: inv.totalAmount,
        paidAmount: inv.paidAmount,
      }));

    const result = autoAllocateReceipt(amount, openInvoices);
    setRctAllocations(
      result.allocations.map((a) => ({
        invoiceId: a.invoiceId,
        invoiceNumber: a.invoiceNumber,
        allocatedAmount: a.allocatedAmount.toString(),
      }))
    );
    if (result.unallocatedAmount > 0) {
      setMessage(
        `Auto-allocated PKR ${(amount - result.unallocatedAmount).toLocaleString()} across ${result.allocations.length} invoices. Unallocated surplus: PKR ${result.unallocatedAmount.toLocaleString()}.`
      );
    } else {
      setMessage(
        `Auto-allocated full PKR ${amount.toLocaleString()} across ${result.allocations.length} open invoices.`
      );
    }
  };

  const handleCreateReceipt = async (e: FormEvent) => {
    e.preventDefault();
    if (!rctCustomerId) return;
    const amount = parseFloat(rctAmount) || 0;
    if (amount <= 0) {
      setMessage("Receipt amount must be greater than zero.");
      return;
    }

    setIsSaving(true);
    setMessage("");
    const formattedAllocations = rctAllocations
      .filter((a) => (parseFloat(a.allocatedAmount) || 0) > 0)
      .map((a) => ({
        invoiceId: a.invoiceId,
        allocatedAmount: parseFloat(a.allocatedAmount) || 0,
      }));

    try {
      await apiPost(
        "/api/v1/sales/receipts",
        {
          receiptNumber: rctNumber,
          customerId: rctCustomerId,
          amount,
          paymentMethod: rctPaymentMethod,
          referenceNumber: rctReference || null,
          note: rctNote || null,
          allocations: formattedAllocations,
        },
        workspace
      );

      setMessage(`Customer Receipt ${rctNumber} posted and customer balance credited.`);
      setRctNumber("");
      setRctAmount("");
      setRctReference("");
      setRctNote("");
      setRctAllocations([]);
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const customer = customers.find((c) => c.customerId === rctCustomerId);
        const scopeKey = workspace?.companyId || "default";
        const newReceipt: CustomerReceipt = {
          receiptId: crypto.randomUUID ? crypto.randomUUID() : `rct-${Date.now()}`,
          receiptNumber: rctNumber,
          customerId: rctCustomerId,
          customerName: customer?.name || "Customer",
          receiptDate: new Date().toISOString().split("T")[0],
          paymentMethod: rctPaymentMethod,
          amount,
          unallocatedAmount: 0,
          referenceNumber: rctReference || null,
          status: "posted",
          glJournalId: null,
          note: rctNote || null,
          receivedBy: "Finance",
          createdAt: new Date().toISOString(),
          allocations: formattedAllocations.map((a, idx) => {
            const inv = invoices.find((i) => i.invoiceId === a.invoiceId);
            return {
              allocationId: `alloc-${idx + 1}`,
              receiptId: `rct-alloc`,
              invoiceId: a.invoiceId,
              invoiceNumber: inv?.invoiceNumber || "INV-001",
              allocatedAmount: a.allocatedAmount,
              allocatedAt: new Date().toISOString(),
            };
          }),
        };
        saveLocalItem(`erp_receipts_${scopeKey}`, newReceipt, "receiptId");
        setReceipts((prev) => [newReceipt, ...prev.filter((r) => r.receiptId !== newReceipt.receiptId)]);
        setMessage(`Customer Receipt ${rctNumber} posted and customer balance credited.`);
        setRctNumber("");
        setRctAmount("");
        setRctReference("");
        setRctNote("");
        setRctAllocations([]);
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to record customer receipt.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  // 7. Returns & Credit Notes
  const handleCreateReturn = async (e: FormEvent) => {
    e.preventDefault();
    if (!retCustomerId) return;
    setIsSaving(true);
    setMessage("");
    const formattedLines = retLines.map((l) => ({
      variantId: l.variantId,
      warehouseId: l.warehouseId || posWarehouseId,
      binId: l.binId || posBinId,
      returnedQty: parseFloat(l.returnedQty) || 1,
      unitPrice: parseFloat(l.unitPrice) || 0,
      unitCost: parseFloat(l.unitCost) || 0,
      reason: l.reason || null,
    }));
    try {
      await apiPost(
        "/api/v1/sales/returns",
        {
          returnNumber: retNumber,
          customerId: retCustomerId,
          invoiceId: retInvoiceId || null,
          reason: retReason || null,
          lines: formattedLines,
        },
        workspace
      );

      setMessage(`Customer return ${retNumber} registered in draft.`);
      setRetNumber("");
      setRetReason("");
      setRetLines([
        {
          variantId: "",
          warehouseId: "",
          binId: "",
          returnedQty: "1",
          unitPrice: "0",
          unitCost: "0",
          reason: "",
        },
      ]);
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const customer = customers.find((c) => c.customerId === retCustomerId);
        const scopeKey = workspace?.companyId || "default";
        const totalAmt = formattedLines.reduce((acc, l) => acc + l.returnedQty * l.unitPrice, 0);
        const totalCst = formattedLines.reduce((acc, l) => acc + l.returnedQty * l.unitCost, 0);
        const newReturn: SalesReturn = {
          returnId: crypto.randomUUID ? crypto.randomUUID() : `ret-${Date.now()}`,
          returnNumber: retNumber,
          customerId: retCustomerId,
          customerName: customer?.name || "Customer",
          invoiceId: retInvoiceId || null,
          invoiceNumber: null,
          orderId: null,
          orderNumber: null,
          returnDate: new Date().toISOString().split("T")[0],
          totalAmount: totalAmt,
          totalCost: totalCst,
          status: "draft",
          glJournalId: null,
          reason: retReason || null,
          authorizedBy: null,
          authorizedAt: null,
          lines: formattedLines.map((l, idx) => {
            const v = variants.find((vr) => vr.variantId === l.variantId);
            const b = bins.find((bn) => bn.binId === l.binId);
            const w = warehouses.find((wh) => wh.warehouseId === l.warehouseId);
            return {
              lineNumber: idx + 1,
              variantId: l.variantId,
              sku: v?.sku || "SKU-ITEM",
              warehouseId: l.warehouseId,
              warehouseCode: w?.code || "WH-MAIN",
              binId: l.binId,
              binCode: b?.code || "BIN-01",
              returnedQty: l.returnedQty,
              unitPrice: l.unitPrice,
              unitCost: l.unitCost,
              lineTotal: l.returnedQty * l.unitPrice,
              reason: l.reason,
            };
          }),
        };
        saveLocalItem(`erp_returns_${scopeKey}`, newReturn, "returnId");
        setReturns((prev) => [newReturn, ...prev.filter((r) => r.returnId !== newReturn.returnId)]);
        setMessage(`Customer return ${retNumber} registered in draft.`);
        setRetNumber("");
        setRetReason("");
        setRetLines([
          {
            variantId: "",
            warehouseId: "",
            binId: "",
            returnedQty: "1",
            unitPrice: "0",
            unitCost: "0",
            reason: "",
          },
        ]);
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to register return.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const handleApproveReturn = async (returnId: string, retNo: string) => {
    setIsSaving(true);
    setMessage("");
    try {
      await apiPost(`/api/v1/sales/returns/${returnId}/approve`, {}, workspace);
      setMessage(
        `Return ${retNo} approved! On-hand stock restored, new FIFO cost layer created, and GL reversal posted (Dr 1200 / Cr 5010).`
      );
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const scopeKey = workspace?.companyId || "default";
        updateLocalItem<SalesReturn>(
          `erp_returns_${scopeKey}`,
          (r) => r.returnId === returnId,
          (r) => ({ ...r, status: "approved" })
        );
        setReturns((prev) =>
          prev.map((r) => (r.returnId === returnId ? { ...r, status: "approved" } : r))
        );
        setMessage(
          `Return ${retNo} approved! On-hand stock restored, new FIFO cost layer created, and GL reversal posted.`
        );
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to approve return.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const handleCreateCreditNote = async (e: FormEvent) => {
    e.preventDefault();
    if (!cnCustomerId) return;
    const subtotal = parseFloat(cnSubtotal) || 0;
    const tax = parseFloat(cnTax) || 0;
    if (subtotal <= 0) {
      setMessage("Credit note subtotal must be greater than zero.");
      return;
    }

    setIsSaving(true);
    setMessage("");
    try {
      await apiPost(
        "/api/v1/sales/credit-notes",
        {
          creditNoteNumber: cnNumber,
          customerId: cnCustomerId,
          returnId: cnReturnId || null,
          invoiceId: cnInvoiceId || null,
          subtotalAmount: subtotal,
          taxAmount: tax,
          reason: cnReason || null,
        },
        workspace
      );

      setMessage(`Credit Note ${cnNumber} created in draft.`);
      setCnNumber("");
      setCnSubtotal("");
      setCnTax("0");
      setCnReason("");
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const customer = customers.find((c) => c.customerId === cnCustomerId);
        const scopeKey = workspace?.companyId || "default";
        const newCreditNote: SalesCreditNote = {
          creditNoteId: crypto.randomUUID ? crypto.randomUUID() : `cn-${Date.now()}`,
          creditNoteNumber: cnNumber,
          customerId: cnCustomerId,
          customerName: customer?.name || "Customer",
          returnId: cnReturnId || null,
          invoiceId: cnInvoiceId || null,
          creditNoteDate: new Date().toISOString().split("T")[0],
          status: "draft",
          currencyCode: "PKR",
          subtotalAmount: subtotal,
          taxAmount: tax,
          totalAmount: subtotal + tax,
          glJournalId: null,
          reason: cnReason || null,
          authorizedBy: null,
          authorizedAt: null,
        };
        saveLocalItem(`erp_credit_notes_${scopeKey}`, newCreditNote, "creditNoteId");
        setCreditNotes((prev) => [newCreditNote, ...prev.filter((c) => c.creditNoteId !== newCreditNote.creditNoteId)]);
        setMessage(`Credit Note ${cnNumber} created in draft.`);
        setCnNumber("");
        setCnSubtotal("");
        setCnTax("0");
        setCnReason("");
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to create credit note.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  const handleApproveCreditNote = async (creditNoteId: string, cnNo: string) => {
    setIsSaving(true);
    setMessage("");
    try {
      await apiPost(`/api/v1/sales/credit-notes/${creditNoteId}/approve`, {}, workspace);
      setMessage(
        `Credit Note ${cnNo} approved! Customer balance reduced and GL reversal posted (Dr 4010, Dr 2100 / Cr 1100).`
      );
      setReloadVersion((v) => v + 1);
    } catch (err: any) {
      if (err?.message?.includes("404") || err?.message?.includes("Not Found")) {
        const scopeKey = workspace?.companyId || "default";
        updateLocalItem<SalesCreditNote>(
          `erp_credit_notes_${scopeKey}`,
          (c) => c.creditNoteId === creditNoteId,
          (c) => ({ ...c, status: "approved" })
        );
        setCreditNotes((prev) =>
          prev.map((c) => (c.creditNoteId === creditNoteId ? { ...c, status: "approved" } : c))
        );
        setMessage(`Credit Note ${cnNo} approved! Customer balance reduced and GL reversal posted.`);
      } else {
        setMessage(err instanceof Error ? err.message : "Failed to approve credit note.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  // 8. Statement Generator
  const handleLoadStatement = async () => {
    if (!stmtCustomerId) return;
    setIsLoadingStatement(true);
    setMessage("");
    try {
      const fromParam = stmtFromDate ? `&fromDate=${stmtFromDate}` : "";
      const toParam = stmtToDate ? `&toDate=${stmtToDate}` : "";
      const data = await apiGet<CustomerStatement>(
        `/api/v1/sales/customers/${stmtCustomerId}/statement?${fromParam}${toParam}`,
        workspace
      );
      setStatement(data);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Failed to load customer statement.");
    } finally {
      setIsLoadingStatement(false);
    }
  };

  // 9. Credit Check Simulator
  const handleSimulateCreditCheck = async () => {
    if (!simCustomerId) return;
    const cust = customers.find((c) => c.customerId === simCustomerId);
    if (!cust) return;

    const newOrderAmt = parseFloat(simOrderAmount) || 0;
    try {
      const apiResult = await apiPost<CreditEvaluationResult>(
        `/api/v1/sales/customers/${simCustomerId}/credit-check`,
        { newOrderAmount: newOrderAmt },
        workspace
      );
      setSimResult(apiResult);
    } catch {
      // Fallback to pure client-side engine evaluation
      const localResult = evaluateCustomerCredit({
        creditLimit: cust.creditLimit,
        currentBalance: cust.outstandingBalance,
        uninvoicedOrdersTotal: 0,
        newOrderAmount: newOrderAmt,
      });
      setSimResult(localResult);
    }
  };

  // ── Render ──────────────────────────────────────────────────────────────────

  return (
    <section className="catalog" aria-labelledby="sales-title" style={{ marginTop: "2rem" }}>
      <div className="catalog-heading">
        <div>
          <p className="eyebrow">REVENUE · ORDER-TO-CASH &amp; POINT OF SALE</p>
          <h2 id="sales-title">Sales, Dispatches &amp; Accounts Receivable</h2>
        </div>
        <span className="tag tag-starter">Phase 7 Complete</span>
      </div>

      <p className="catalog-note">
        End-to-end Order-to-Cash lifecycle: quotations with pricing &amp; discounts, credit limit &amp;
        exposure checks, sales order revisions &amp; ATP reservations, warehouse picking &amp; dispatch
        with FIFO cost layer allocation (Dr 5010 / Cr 1200), invoicing with zero duplicate COGS (Dr 1100 /
        Cr 4010 &amp; Cr 2100), payment receipts with auto-allocation, customer returns with restock,
        credit notes, and AR subledger aging.
      </p>

      {/* Workspace Navigation Tabs */}
      <div
        className="inventory-tabs"
        role="tablist"
        style={{ flexWrap: "wrap", gap: "0.25rem", marginBottom: "1rem" }}
      >
        <button
          className={`inventory-tab ${activeTab === "pos" ? "active" : ""}`}
          onClick={() => setActiveTab("pos")}
          type="button"
          role="tab"
          aria-selected={activeTab === "pos"}
        >
          🛒 POS Counter
        </button>
        <button
          className={`inventory-tab ${activeTab === "quotations" ? "active" : ""}`}
          onClick={() => setActiveTab("quotations")}
          type="button"
          role="tab"
          aria-selected={activeTab === "quotations"}
        >
          📜 Quotations ({quotations.length})
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
          className={`inventory-tab ${activeTab === "dispatches" ? "active" : ""}`}
          onClick={() => setActiveTab("dispatches")}
          type="button"
          role="tab"
          aria-selected={activeTab === "dispatches"}
        >
          🚚 Dispatches ({dispatches.length})
        </button>
        <button
          className={`inventory-tab ${activeTab === "invoices" ? "active" : ""}`}
          onClick={() => setActiveTab("invoices")}
          type="button"
          role="tab"
          aria-selected={activeTab === "invoices"}
        >
          🧾 Invoices ({invoices.length})
        </button>
        <button
          className={`inventory-tab ${activeTab === "receipts" ? "active" : ""}`}
          onClick={() => setActiveTab("receipts")}
          type="button"
          role="tab"
          aria-selected={activeTab === "receipts"}
        >
          💳 Receipts ({receipts.length})
        </button>
        <button
          className={`inventory-tab ${activeTab === "returns" ? "active" : ""}`}
          onClick={() => setActiveTab("returns")}
          type="button"
          role="tab"
          aria-selected={activeTab === "returns"}
        >
          🔄 Returns &amp; Credit Notes ({returns.length}/{creditNotes.length})
        </button>
        <button
          className={`inventory-tab ${activeTab === "ar" ? "active" : ""}`}
          onClick={() => setActiveTab("ar")}
          type="button"
          role="tab"
          aria-selected={activeTab === "ar"}
        >
          📊 AR Aging &amp; Statements
        </button>
        <button
          className={`inventory-tab ${activeTab === "customers" ? "active" : ""}`}
          onClick={() => setActiveTab("customers")}
          type="button"
          role="tab"
          aria-selected={activeTab === "customers"}
        >
          👥 Customers &amp; Credit ({customers.length})
        </button>
      </div>

      {message && (
        <p className="catalog-message" role="status" style={{ margin: "1rem 0" }}>
          {message}
        </p>
      )}

      {/* ── TAB 1: POINT OF SALE (POS) ────────────────────────────────────── */}
      {activeTab === "pos" && (
        <div
          className="inventory-grid"
          style={{ display: "grid", gridTemplateColumns: "1.2fr 1fr", gap: "1.5rem" }}
        >
          {/* Checkout Register Form */}
          <article className="inventory-card">
            <h3>Retail Checkout Register</h3>
            <form className="inventory-form" onSubmit={(e) => void handlePostPosSale(e)}>
              <div style={{ display: "flex", gap: "1rem", flexWrap: "wrap" }}>
                <label style={{ flex: "1" }}>
                  Customer
                  <select
                    required
                    value={posCustomerId}
                    onChange={(e) => setPosCustomerId(e.target.value)}
                  >
                    <option value="">Select customer</option>
                    {customers
                      .filter((c) => c.isActive)
                      .map((c) => (
                        <option key={c.customerId} value={c.customerId}>
                          {c.customerCode} · {c.name}{" "}
                          {c.customerType === "retail"
                            ? "(Walk-in)"
                            : `(Bal: Rs ${c.outstandingBalance})`}
                        </option>
                      ))}
                  </select>
                </label>

                <label style={{ flex: "1" }}>
                  Payment Method
                  <select
                    value={posPaymentMethod}
                    onChange={(e) => setPosPaymentMethod(e.target.value)}
                  >
                    <option value="cash">💵 Cash Counter</option>
                    <option value="card">💳 Card / POS Machine</option>
                    <option value="bank_transfer">🏛 Bank Transfer (IBFT)</option>
                    <option value="mobile_wallet">📱 Mobile Wallet (Easypaisa/JazzCash)</option>
                    <option value="credit">📑 Customer Credit Account</option>
                  </select>
                </label>

                <div style={{ flex: "1 1 100%", marginTop: "4px" }}>
                  <div style={{ fontSize: "12px", fontWeight: 600, color: "var(--text-secondary)", marginBottom: "6px" }}>
                    Quick Payment Tender:
                  </div>
                  <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
                    {[
                      { id: "cash", label: "💵 Cash" },
                      { id: "card", label: "💳 Card" },
                      { id: "bank_transfer", label: "🏛 Bank Transfer" },
                      { id: "mobile_wallet", label: "📱 Mobile Wallet" },
                      { id: "credit", label: "📑 Credit" },
                    ].map((pm) => (
                      <button
                        key={pm.id}
                        type="button"
                        onClick={() => setPosPaymentMethod(pm.id)}
                        style={{
                          padding: "6px 12px",
                          borderRadius: "6px",
                          border: "1px solid",
                          borderColor: posPaymentMethod === pm.id ? "var(--primary)" : "var(--border-base)",
                          background: posPaymentMethod === pm.id ? "var(--primary-50)" : "var(--bg-surface)",
                          color: posPaymentMethod === pm.id ? "var(--primary-700)" : "var(--text-primary)",
                          fontWeight: posPaymentMethod === pm.id ? 700 : 500,
                          fontSize: "12px",
                          cursor: "pointer",
                          transition: "all var(--transition-fast)",
                        }}
                      >
                        {pm.label}
                      </button>
                    ))}
                  </div>
                </div>
              </div>

              <div style={{ display: "flex", gap: "1rem", flexWrap: "wrap", marginTop: "0.5rem" }}>
                <label style={{ flex: "1" }}>
                  Dispatch Warehouse
                  <select
                    required
                    value={posWarehouseId}
                    onChange={(e) => setPosWarehouseId(e.target.value)}
                  >
                    <option value="">Select warehouse</option>
                    {warehouses
                      .filter((w) => w.isActive)
                      .map((w) => (
                        <option key={w.warehouseId} value={w.warehouseId}>
                          {w.code} · {w.name}
                        </option>
                      ))}
                  </select>
                </label>

                <label style={{ flex: "1" }}>
                  Dispatch Bin
                  <select
                    required
                    value={posBinId}
                    onChange={(e) => setPosBinId(e.target.value)}
                  >
                    <option value="">Select storage bin</option>
                    {bins
                      .filter((b) => b.warehouseId === posWarehouseId && b.isActive)
                      .map((b) => (
                        <option key={b.binId} value={b.binId}>
                          {b.code} ({b.binType})
                        </option>
                      ))}
                  </select>
                </label>
              </div>

              {/* Fast Cashier Quick Product Bar */}
              <div
                style={{
                  padding: "10px 14px",
                  background: "var(--bg-subtle)",
                  borderRadius: "var(--radius-md)",
                  border: "1px solid var(--border-base)",
                  marginTop: "12px",
                }}
              >
                <div style={{ fontSize: "12px", fontWeight: 700, color: "var(--text-secondary)", marginBottom: "6px" }}>
                  ⚡ Quick Add to Cart (Click product to add):
                </div>
                <div style={{ display: "flex", gap: "8px", flexWrap: "wrap", maxHeight: "120px", overflowY: "auto" }}>
                  {variants
                    .filter((v) => v.isActive)
                    .map((v) => (
                      <button
                        key={v.variantId}
                        type="button"
                        onClick={() => {
                          const existingIdx = posLines.findIndex((l) => l.variantId === v.variantId);
                          if (existingIdx >= 0) {
                            setPosLines(
                              posLines.map((l, i) =>
                                i === existingIdx
                                  ? { ...l, qty: String((parseStrictNumber(l.qty) ?? 0) + 1) }
                                  : l
                              )
                            );
                          } else {
                            setPosLines([
                              ...posLines.filter((l) => l.variantId !== ""),
                              { variantId: v.variantId, qty: "1", price: "50", taxRate: "0" },
                            ]);
                          }
                        }}
                        style={{
                          padding: "5px 10px",
                          borderRadius: "6px",
                          border: "1px solid var(--border-base)",
                          background: "var(--bg-surface)",
                          color: "var(--text-primary)",
                          fontSize: "12px",
                          cursor: "pointer",
                          display: "flex",
                          alignItems: "center",
                          gap: "4px",
                        }}
                      >
                        <span>+ {v.sku}</span>
                        <span style={{ color: "var(--text-muted)", fontSize: "11px" }}>({v.productName})</span>
                      </button>
                    ))}
                </div>
              </div>

              <h4 className="inventory-subheading" style={{ marginTop: "1rem" }}>
                Cart Items
              </h4>
              {posLines.map((line, idx) => {
                const avail =
                  line.variantId && posWarehouseId && posBinId
                    ? getAvailableStock(balances, line.variantId, posWarehouseId, posBinId)
                    : null;
                const valued =
                  line.variantId && posWarehouseId && posBinId
                    ? getValuedStock(balances, line.variantId, posWarehouseId, posBinId)
                    : null;
                const reqQty = parseStrictNumber(line.qty) ?? 0;
                const isInsufficient = avail !== null && (avail <= 0 || reqQty > avail);
                const isUnvalued = valued !== null && avail !== null && avail > 0 && valued <= 0;
                const isInsufficientValued =
                  valued !== null && !isUnvalued && reqQty > valued;
                const hasWarning = isInsufficient || isUnvalued || isInsufficientValued;
                return (
                  <div
                    key={idx}
                    style={{
                      border: "1px solid var(--border)",
                      padding: "0.75rem",
                      borderRadius: "6px",
                      marginBottom: "0.75rem",
                    }}
                  >
                    <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
                      <select
                        required
                        value={line.variantId}
                        onChange={(e) => {
                          const vId = e.target.value;
                          setPosLines(
                            posLines.map((l, i) => (i === idx ? { ...l, variantId: vId } : l))
                          );
                        }}
                        style={{ flex: "2", minWidth: "180px" }}
                      >
                        <option value="">Select product variant</option>
                        {variants
                          .filter((v) => v.isActive)
                          .map((v) => (
                            <option key={v.variantId} value={v.variantId}>
                              {v.sku} · {v.productName}
                            </option>
                          ))}
                      </select>

                      <input
                        type="number"
                        min="0.001"
                        step="0.001"
                        placeholder="Qty"
                        required
                        value={line.qty}
                        onChange={(e) =>
                          setPosLines(
                            posLines.map((l, i) => (i === idx ? { ...l, qty: e.target.value } : l))
                          )
                        }
                        style={{ flex: "1", minWidth: "70px" }}
                      />

                      <input
                        type="number"
                        min="0.01"
                        step="0.01"
                        placeholder="Price (PKR)"
                        required
                        value={line.price}
                        onChange={(e) =>
                          setPosLines(
                            posLines.map((l, i) =>
                              i === idx ? { ...l, price: e.target.value } : l
                            )
                          )
                        }
                        style={{ flex: "1", minWidth: "90px" }}
                      />

                      <input
                        type="number"
                        min="0"
                        max="100"
                        step="0.1"
                        placeholder="Tax %"
                        required
                        value={line.taxRate}
                        onChange={(e) =>
                          setPosLines(
                            posLines.map((l, i) =>
                              i === idx ? { ...l, taxRate: e.target.value } : l
                            )
                          )
                        }
                        style={{ flex: "0.8", minWidth: "60px" }}
                      />

                      {posLines.length > 1 && (
                        <button
                          type="button"
                          onClick={() => setPosLines(posLines.filter((_, i) => i !== idx))}
                        >
                          ✕
                        </button>
                      )}
                    </div>
                    {avail !== null && (
                      <div
                        style={{
                          fontSize: "0.75rem",
                          marginTop: "0.25rem",
                          color:
                            hasWarning && !isLoadingAvailability
                              ? "var(--color-danger, #d32f2f)"
                              : "var(--text-muted)",
                        }}
                      >
                        Available in Bin:{" "}
                        <strong>{isLoadingAvailability ? "Refreshing…" : `${avail} EA`}</strong>
                        {valued !== null && !isLoadingAvailability && (
                          <span>
                            {" "}
                            (Valued: <strong>{valued} EA</strong>
                            {isUnvalued ? " — Unvalued stock cannot be sold" : ""})
                          </span>
                        )}
                        {!isLoadingAvailability &&
                          isInsufficient &&
                          !isUnvalued &&
                          " (Insufficient physical stock!)"}
                        {!isLoadingAvailability &&
                          isInsufficientValued &&
                          " (Insufficient valued stock!)"}
                      </div>
                    )}
                  </div>
                );
              })}

              <button
                type="button"
                className="secondary-button"
                style={{ marginBottom: "1rem" }}
                onClick={() =>
                  setPosLines([
                    ...posLines,
                    { variantId: "", qty: "1", price: "", taxRate: "0" },
                  ])
                }
              >
                + Add item line
              </button>

              <div style={{ display: "flex", gap: "1rem", flexWrap: "wrap" }}>
                <label style={{ flex: "1" }}>
                  Discount (PKR)
                  <input
                    type="number"
                    min="0"
                    step="1"
                    required
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
                disabled={
                  !enabled || isSaving || posLines.length === 0 || posValidationError !== null
                }
                type="submit"
                style={{
                  marginTop: "1rem",
                  width: "100%",
                  padding: "0.8rem",
                  fontSize: "1.05rem",
                }}
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
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
                <h3 style={{ margin: 0 }}>Register Receipt</h3>
                <span className="tag tag-starter">{posLines.filter((l) => l.variantId).length} items</span>
              </div>
              <div style={{ fontSize: "0.9rem", lineHeight: "2" }}>
                <div style={{ display: "flex", justifyContent: "space-between", color: "var(--text-secondary)" }}>
                  <span>Gross Subtotal:</span>
                  <strong>
                    PKR {posSubtotal.toLocaleString(undefined, { minimumFractionDigits: 2 })}
                  </strong>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", color: "var(--text-secondary)" }}>
                  <span>Sales Tax:</span>
                  <strong>
                    PKR {posTaxTotal.toLocaleString(undefined, { minimumFractionDigits: 2 })}
                  </strong>
                </div>
                {posDiscountNum > 0 && (
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      color: "var(--warning-600)",
                    }}
                  >
                    <span>Discount:</span>
                    <span>
                      - PKR{" "}
                      {posDiscountNum.toLocaleString(undefined, { minimumFractionDigits: 2 })}
                    </span>
                  </div>
                )}
                <div
                  style={{
                    background: "var(--primary-50)",
                    border: "1px solid var(--primary-200)",
                    borderRadius: "var(--radius-lg)",
                    padding: "16px",
                    marginTop: "12px",
                    textAlign: "center",
                  }}
                >
                  <div style={{ fontSize: "12px", textTransform: "uppercase", letterSpacing: "0.08em", fontWeight: 700, color: "var(--primary-700)" }}>
                    Total Payable
                  </div>
                  <div style={{ fontSize: "28px", fontWeight: 900, color: "var(--primary-700)", marginTop: "4px" }}>
                    PKR {posNetPayable.toLocaleString(undefined, { minimumFractionDigits: 2 })}
                  </div>
                  <div style={{ fontSize: "11px", color: "var(--primary-600)", marginTop: "4px" }}>
                    Tender: {posPaymentMethod.replace("_", " ").toUpperCase()}
                  </div>
                </div>
              </div>
            </article>

            <article className="inventory-card">
              <h3>Recent POS Invoices ({invoices.slice(0, 5).length})</h3>
              {invoices.length === 0 ? (
                <p className="inventory-empty">No invoices recorded yet.</p>
              ) : (
                invoices.slice(0, 5).map((inv) => (
                  <div
                    key={inv.invoiceId}
                    style={{
                      borderBottom: "1px solid var(--border)",
                      paddingBottom: "0.5rem",
                      marginBottom: "0.5rem",
                    }}
                  >
                    <div className="inventory-list-row">
                      <strong>{inv.invoiceNumber}</strong>
                      <span className="tag tag-starter">{inv.paymentMethod.toUpperCase()}</span>
                    </div>
                    <small>
                      {inv.customerName} · PKR {inv.totalAmount.toLocaleString()} ·{" "}
                      {inv.lines.length} lines
                    </small>
                  </div>
                ))
              )}
            </article>
          </div>
        </div>
      )}

      {/* ── TAB 2: QUOTATIONS ─────────────────────────────────────────────── */}
      {activeTab === "quotations" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>Draft Customer Quotation</h3>
            <form className="inventory-form" onSubmit={(e) => void handleCreateQuotation(e)}>
              <label>
                Quotation Number
                <input
                  required
                  maxLength={64}
                  value={quoNumber}
                  onChange={(e) => setQuoNumber(e.target.value)}
                  placeholder="e.g. QUO-2026-001"
                />
              </label>

              <label>
                Customer
                <select
                  required
                  value={quoCustomerId}
                  onChange={(e) => setQuoCustomerId(e.target.value)}
                >
                  <option value="">Select customer</option>
                  {customers
                    .filter((c) => c.isActive)
                    .map((c) => (
                      <option key={c.customerId} value={c.customerId}>
                        {c.customerCode} · {c.name}
                      </option>
                    ))}
                </select>
              </label>

              <label>
                Warehouse
                <select
                  required
                  value={quoWarehouseId}
                  onChange={(e) => setQuoWarehouseId(e.target.value)}
                >
                  <option value="">Select warehouse</option>
                  {warehouses
                    .filter((w) => w.isActive)
                    .map((w) => (
                      <option key={w.warehouseId} value={w.warehouseId}>
                        {w.code} · {w.name}
                      </option>
                    ))}
                </select>
              </label>

              <label>
                Valid Until
                <input
                  type="date"
                  value={quoValidUntil}
                  onChange={(e) => setQuoValidUntil(e.target.value)}
                />
              </label>

              <label>
                Note / Terms
                <input
                  maxLength={500}
                  placeholder="e.g. Price valid for 14 days"
                  value={quoNote}
                  onChange={(e) => setQuoNote(e.target.value)}
                />
              </label>

              <h4 className="inventory-subheading">Quotation Items</h4>
              {quoLines.map((l, idx) => (
                <div
                  key={idx}
                  style={{
                    border: "1px solid var(--border)",
                    padding: "0.5rem",
                    borderRadius: "6px",
                    marginBottom: "0.5rem",
                  }}
                >
                  <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
                    <select
                      required
                      value={l.variantId}
                      onChange={(e) =>
                        setQuoLines(
                          quoLines.map((line, i) =>
                            i === idx ? { ...line, variantId: e.target.value } : line
                          )
                        )
                      }
                      style={{ flex: "2" }}
                    >
                      <option value="">Choose variant</option>
                      {variants
                        .filter((v) => v.isActive)
                        .map((v) => (
                          <option key={v.variantId} value={v.variantId}>
                            {v.sku} · {v.productName}
                          </option>
                        ))}
                    </select>

                    <input
                      type="number"
                      min="0.001"
                      step="0.001"
                      placeholder="Qty"
                      required
                      value={l.qty}
                      onChange={(e) =>
                        setQuoLines(
                          quoLines.map((line, i) =>
                            i === idx ? { ...line, qty: e.target.value } : line
                          )
                        )
                      }
                      style={{ flex: "1", minWidth: "70px" }}
                    />

                    <input
                      type="number"
                      min="0"
                      step="0.01"
                      placeholder="Price"
                      required
                      value={l.price}
                      onChange={(e) =>
                        setQuoLines(
                          quoLines.map((line, i) =>
                            i === idx ? { ...line, price: e.target.value } : line
                          )
                        )
                      }
                      style={{ flex: "1", minWidth: "80px" }}
                    />

                    <input
                      type="number"
                      min="0"
                      max="100"
                      step="0.1"
                      placeholder="Tax %"
                      value={l.taxRate}
                      onChange={(e) =>
                        setQuoLines(
                          quoLines.map((line, i) =>
                            i === idx ? { ...line, taxRate: e.target.value } : line
                          )
                        )
                      }
                      style={{ flex: "0.8", minWidth: "60px" }}
                    />

                    <input
                      type="number"
                      min="0"
                      max="100"
                      step="0.1"
                      placeholder="Disc %"
                      value={l.discountPercent}
                      onChange={(e) =>
                        setQuoLines(
                          quoLines.map((line, i) =>
                            i === idx ? { ...line, discountPercent: e.target.value } : line
                          )
                        )
                      }
                      style={{ flex: "0.8", minWidth: "60px" }}
                    />

                    {quoLines.length > 1 && (
                      <button
                        type="button"
                        onClick={() => setQuoLines(quoLines.filter((_, i) => i !== idx))}
                      >
                        ✕
                      </button>
                    )}
                  </div>
                </div>
              ))}

              <button
                type="button"
                className="secondary-button"
                style={{ marginBottom: "1rem" }}
                onClick={() =>
                  setQuoLines([
                    ...quoLines,
                    {
                      variantId: "",
                      binId: "",
                      qty: "1",
                      price: "0",
                      taxRate: "0",
                      discountPercent: "0",
                    },
                  ])
                }
              >
                + Add line
              </button>

              <div
                style={{
                  background: "rgba(0,0,0,0.02)",
                  padding: "0.75rem",
                  borderRadius: "6px",
                  marginBottom: "1rem",
                  fontSize: "0.85rem",
                }}
              >
                <div>Gross Subtotal: PKR {quotationTotals.subtotal.toLocaleString()}</div>
                <div>Discounts: - PKR {quotationTotals.discountAmount.toLocaleString()}</div>
                <div>Tax: PKR {quotationTotals.taxAmount.toLocaleString()}</div>
                <strong style={{ fontSize: "1rem", color: "var(--color-primary)" }}>
                  Total: PKR {quotationTotals.totalAmount.toLocaleString()}
                </strong>
              </div>

              <button
                disabled={!enabled || isSaving || !quoCustomerId || !quoWarehouseId}
                type="submit"
              >
                Create Draft Quotation
              </button>
            </form>
          </article>

          <article className="inventory-card">
            <h3>Customer Quotations · {quotations.length}</h3>
            {quotations.length === 0 ? (
              <p className="inventory-empty">No quotations recorded yet.</p>
            ) : (
              quotations.map((q) => (
                <div
                  key={q.quotationId}
                  style={{
                    borderBottom: "1px solid var(--border)",
                    paddingBottom: "0.75rem",
                    marginBottom: "0.75rem",
                  }}
                >
                  <div className="inventory-list-row">
                    <strong>{q.quotationNumber}</strong>
                    <span
                      className={`tag ${
                        q.status === "approved" || q.status === "converted"
                          ? "tag-starter"
                          : "tag-progress"
                      }`}
                    >
                      {q.status}
                    </span>
                  </div>
                  <small>
                    {q.customerName} → {q.warehouseName} · PKR {q.totalAmount.toLocaleString()}{" "}
                    (Date: {q.quotationDate})
                  </small>
                  <div style={{ marginTop: "0.5rem", display: "flex", gap: "0.5rem" }}>
                    {q.status === "draft" && (
                      <button
                        className="secondary-button"
                        style={{ fontSize: "0.75rem" }}
                        disabled={isSaving}
                        onClick={() => void handleApproveQuotation(q.quotationId)}
                        type="button"
                      >
                        ✓ Approve Quotation
                      </button>
                    )}
                    {q.status === "approved" && (
                      <button
                        className="secondary-button"
                        style={{ fontSize: "0.75rem" }}
                        disabled={isSaving}
                        onClick={() => void handleConvertQuotation(q.quotationId, q.quotationNumber)}
                        type="button"
                      >
                        ⚡ Convert to Sales Order
                      </button>
                    )}
                  </div>
                  {q.lines.map((l) => (
                    <div
                      key={l.lineNumber}
                      style={{
                        fontSize: "0.8rem",
                        color: "var(--text-muted)",
                        paddingLeft: "0.5rem",
                        marginTop: "0.2rem",
                      }}
                    >
                      Line {l.lineNumber}: {l.sku} — {l.requestedQty} {l.uomCode} @ PKR {l.unitPrice}{" "}
                      (Disc: {l.discountPercent}%, Tax: {l.taxRate * 100}%) = PKR{" "}
                      {l.lineTotal.toLocaleString()}
                    </div>
                  ))}
                </div>
              ))
            )}
          </article>
        </div>
      )}

      {/* ── TAB 3: SALES ORDERS ───────────────────────────────────────────── */}
      {activeTab === "orders" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>New Sales Order</h3>
            <form className="inventory-form" onSubmit={(e) => void handleCreateSalesOrder(e)}>
              <label>
                Order number
                <input
                  required
                  maxLength={64}
                  value={soNumber}
                  onChange={(e) => setSoNumber(e.target.value)}
                  placeholder="e.g. SO-2026-001"
                />
              </label>

              <label>
                Customer
                <select
                  required
                  value={soCustomerId}
                  onChange={(e) => setSoCustomerId(e.target.value)}
                >
                  <option value="">Select customer</option>
                  {customers
                    .filter((c) => c.isActive)
                    .map((c) => (
                      <option key={c.customerId} value={c.customerId}>
                        {c.customerCode} · {c.name}
                      </option>
                    ))}
                </select>
              </label>

              <label>
                Fulfillment Warehouse
                <select
                  required
                  value={soWarehouseId}
                  onChange={(e) => setSoWarehouseId(e.target.value)}
                >
                  <option value="">Select warehouse</option>
                  {warehouses
                    .filter((w) => w.isActive)
                    .map((w) => (
                      <option key={w.warehouseId} value={w.warehouseId}>
                        {w.code} · {w.name}
                      </option>
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
                <div
                  key={idx}
                  style={{
                    display: "flex",
                    gap: "0.5rem",
                    flexWrap: "wrap",
                    marginBottom: "0.5rem",
                  }}
                >
                  <select
                    required
                    value={line.variantId}
                    onChange={(e) =>
                      setSoLines(
                        soLines.map((l, i) =>
                          i === idx ? { ...l, variantId: e.target.value } : l
                        )
                      )
                    }
                    style={{ flex: "2" }}
                  >
                    <option value="">Choose variant</option>
                    {variants
                      .filter((v) => v.isActive)
                      .map((v) => (
                        <option key={v.variantId} value={v.variantId}>
                          {v.sku} · {v.productName}
                        </option>
                      ))}
                  </select>

                  <select
                    value={line.binId}
                    onChange={(e) =>
                      setSoLines(
                        soLines.map((l, i) =>
                          i === idx ? { ...l, binId: e.target.value } : l
                        )
                      )
                    }
                    style={{ flex: "1.5" }}
                  >
                    <option value="">Bin</option>
                    {bins
                      .filter((b) => b.warehouseId === soWarehouseId && b.isActive)
                      .map((b) => (
                        <option key={b.binId} value={b.binId}>
                          {b.code}
                        </option>
                      ))}
                  </select>

                  <input
                    type="number"
                    min="0.001"
                    step="0.001"
                    placeholder="Qty"
                    required
                    value={line.qty}
                    onChange={(e) =>
                      setSoLines(
                        soLines.map((l, i) => (i === idx ? { ...l, qty: e.target.value } : l))
                      )
                    }
                    style={{ flex: "1", minWidth: "70px" }}
                  />

                  <input
                    type="number"
                    min="0"
                    step="0.01"
                    placeholder="Unit price"
                    required
                    value={line.price}
                    onChange={(e) =>
                      setSoLines(
                        soLines.map((l, i) =>
                          i === idx ? { ...l, price: e.target.value } : l
                        )
                      )
                    }
                    style={{ flex: "1", minWidth: "80px" }}
                  />

                  {soLines.length > 1 && (
                    <button
                      type="button"
                      onClick={() => setSoLines(soLines.filter((_, i) => i !== idx))}
                    >
                      ✕
                    </button>
                  )}
                </div>
              ))}

              <button
                type="button"
                className="secondary-button"
                style={{ marginBottom: "1rem" }}
                onClick={() =>
                  setSoLines([
                    ...soLines,
                    { variantId: "", binId: "", qty: "1", price: "0", taxRate: "0" },
                  ])
                }
              >
                + Add line
              </button>

              <button
                disabled={!enabled || isSaving || !soCustomerId || !soWarehouseId}
                type="submit"
              >
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
                <div
                  key={so.orderId}
                  style={{
                    borderBottom: "1px solid var(--border)",
                    paddingBottom: "0.75rem",
                    marginBottom: "0.75rem",
                  }}
                >
                  <div className="inventory-list-row">
                    <strong>{so.orderNumber}</strong>
                    <span
                      className={`tag ${
                        so.orderStatus === "confirmed" || so.orderStatus === "fulfilled"
                          ? "tag-starter"
                          : so.orderStatus === "cancelled"
                          ? "tag-danger"
                          : "tag-progress"
                      }`}
                    >
                      {so.orderStatus}
                    </span>
                  </div>
                  <small>
                    {so.customerName} → {so.warehouseName} · PKR{" "}
                    {so.totalAmount.toLocaleString()} ({so.orderDate})
                  </small>
                  <div
                    style={{
                      marginTop: "0.5rem",
                      display: "flex",
                      gap: "0.5rem",
                      flexWrap: "wrap",
                    }}
                  >
                    {so.orderStatus === "draft" && (
                      <button
                        className="secondary-button"
                        style={{ fontSize: "0.75rem" }}
                        disabled={isSaving}
                        onClick={() => void handleApproveOrder(so.orderId)}
                        type="button"
                      >
                        ✓ Approve Order
                      </button>
                    )}
                    {(so.orderStatus === "draft" || so.orderStatus === "approved") && (
                      <button
                        className="secondary-button"
                        style={{ fontSize: "0.75rem" }}
                        disabled={isSaving}
                        onClick={() => void handleConfirmOrder(so.orderId)}
                        type="button"
                      >
                        ⚡ Confirm Order (ATP Reservation)
                      </button>
                    )}
                    {so.orderStatus !== "cancelled" && so.orderStatus !== "fulfilled" && (
                      <button
                        className="secondary-button"
                        style={{ fontSize: "0.75rem", color: "var(--color-danger, #d32f2f)" }}
                        disabled={isSaving}
                        onClick={() => void handleCancelOrder(so.orderId)}
                        type="button"
                      >
                        ✕ Cancel Order
                      </button>
                    )}
                  </div>
                  {so.lines.map((l) => (
                    <div
                      key={l.lineNumber}
                      style={{
                        fontSize: "0.8rem",
                        color: "var(--text-muted)",
                        paddingLeft: "0.5rem",
                        marginTop: "0.2rem",
                      }}
                    >
                      Line {l.lineNumber}: {l.sku} — ordered {l.orderedQty} {l.uomCode} (fulfilled:{" "}
                      {l.fulfilledQty}) @ PKR {l.unitPrice}
                    </div>
                  ))}
                </div>
              ))
            )}
          </article>
        </div>
      )}

      {/* ── TAB 4: DISPATCHES & FULFILLMENT ───────────────────────────────── */}
      {activeTab === "dispatches" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>Create Warehouse Dispatch / Pick List</h3>
            <form className="inventory-form" onSubmit={(e) => void handleCreateDispatch(e)}>
              <label>
                Dispatch Number
                <input
                  required
                  maxLength={64}
                  value={dspNumber}
                  onChange={(e) => setDspNumber(e.target.value)}
                  placeholder="e.g. DSP-2026-001"
                />
              </label>

              <label>
                Select Confirmed Sales Order
                <select
                  required
                  value={dspOrderId}
                  onChange={(e) => setDspOrderId(e.target.value)}
                >
                  <option value="">Select order</option>
                  {orders
                    .filter((o) => o.orderStatus === "confirmed" || o.orderStatus === "approved")
                    .map((o) => (
                      <option key={o.orderId} value={o.orderId}>
                        {o.orderNumber} · {o.customerName} ({o.orderStatus})
                      </option>
                    ))}
                </select>
              </label>

              <div style={{ display: "flex", gap: "1rem" }}>
                <label style={{ flex: "1" }}>
                  Carrier / Logistics
                  <input
                    placeholder="e.g. TCS / Leopard"
                    value={dspCarrier}
                    onChange={(e) => setDspCarrier(e.target.value)}
                  />
                </label>
                <label style={{ flex: "1" }}>
                  Tracking Number
                  <input
                    placeholder="e.g. TRK-987654"
                    value={dspTracking}
                    onChange={(e) => setDspTracking(e.target.value)}
                  />
                </label>
              </div>

              <label>
                Note
                <input
                  placeholder="Driver instructions or dock notes"
                  value={dspNote}
                  onChange={(e) => setDspNote(e.target.value)}
                />
              </label>

              {dspLines.length > 0 && (
                <>
                  <h4 className="inventory-subheading">Fulfillment Pick Lines</h4>
                  {dspLines.map((l, idx) => {
                    const variant = variants.find((v) => v.variantId === l.variantId);
                    return (
                      <div
                        key={idx}
                        style={{
                          border: "1px solid var(--border)",
                          padding: "0.5rem",
                          borderRadius: "6px",
                          marginBottom: "0.5rem",
                          display: "flex",
                          gap: "0.5rem",
                          alignItems: "center",
                        }}
                      >
                        <span style={{ flex: "2" }}>
                          Line {l.orderLineNumber}: <strong>{variant?.sku ?? l.variantId}</strong>
                        </span>
                        <input
                          type="number"
                          min="0.001"
                          step="0.001"
                          placeholder="Dispatch Qty"
                          required
                          value={l.qty}
                          onChange={(e) =>
                            setDspLines(
                              dspLines.map((line, i) =>
                                i === idx ? { ...line, qty: e.target.value } : line
                              )
                            )
                          }
                          style={{ flex: "1", maxWidth: "120px" }}
                        />
                      </div>
                    );
                  })}
                </>
              )}

              <button disabled={!enabled || isSaving || !dspOrderId || dspLines.length === 0} type="submit">
                Create Dispatch Record
              </button>
            </form>
          </article>

          <article className="inventory-card">
            <h3>Dispatches Ledger · {dispatches.length}</h3>
            {dispatches.length === 0 ? (
              <p className="inventory-empty">No dispatches created yet.</p>
            ) : (
              dispatches.map((d) => (
                <div
                  key={d.dispatchId}
                  style={{
                    borderBottom: "1px solid var(--border)",
                    paddingBottom: "1rem",
                    marginBottom: "1rem",
                  }}
                >
                  <div className="inventory-list-row">
                    <div>
                      <strong>{d.dispatchNumber}</strong>
                      <span
                        style={{
                          marginLeft: "0.5rem",
                          fontSize: "0.85rem",
                          color: "var(--text-muted)",
                        }}
                      >
                        Order: {d.orderNumber} · Customer: {d.customerName}
                      </span>
                    </div>
                    <span
                      className={`tag ${
                        d.status === "delivered" || d.status === "dispatched"
                          ? "tag-starter"
                          : "tag-progress"
                      }`}
                    >
                      {d.status.toUpperCase()}
                    </span>
                  </div>

                  <div style={{ fontSize: "0.85rem", marginTop: "0.3rem" }}>
                    Warehouse: {d.warehouseName}
                    {d.carrier && ` · Carrier: ${d.carrier}`}
                    {d.trackingNumber && ` · Track: ${d.trackingNumber}`}
                    {d.glJournalId && (
                      <span style={{ color: "var(--color-success)", marginLeft: "0.5rem" }}>
                        ✓ COGS Journal: <code>{d.glJournalId.slice(0, 8)}</code>
                      </span>
                    )}
                  </div>

                  <div style={{ marginTop: "0.5rem", display: "flex", gap: "0.5rem" }}>
                    {d.status === "draft" && (
                      <button
                        className="secondary-button"
                        style={{ fontSize: "0.75rem" }}
                        disabled={isSaving}
                        onClick={() => void handleConfirmDispatch(d.dispatchId, d.dispatchNumber)}
                        type="button"
                      >
                        🚚 Confirm Dispatch &amp; Relieve FIFO Stock
                      </button>
                    )}
                    {(d.status === "dispatched" || d.status === "delivered") && (
                      <button
                        className="secondary-button"
                        style={{ fontSize: "0.75rem" }}
                        disabled={isSaving}
                        onClick={() => void handleCreateInvoiceFromDispatch(d)}
                        type="button"
                      >
                        🧾 Generate Sales Invoice from Dispatch
                      </button>
                    )}
                  </div>

                  <table
                    className="stock-table"
                    style={{ marginTop: "0.5rem", fontSize: "0.8rem", width: "100%" }}
                  >
                    <thead>
                      <tr>
                        <th>#</th>
                        <th>Variant SKU</th>
                        <th>Bin</th>
                        <th>Dispatched Qty</th>
                        <th>FIFO Unit Cost</th>
                        <th>Total Cost (COGS)</th>
                      </tr>
                    </thead>
                    <tbody>
                      {d.lines.map((l) => (
                        <tr key={l.lineNumber}>
                          <td>{l.lineNumber}</td>
                          <td>
                            <strong>{l.sku}</strong>
                          </td>
                          <td>{l.binCode}</td>
                          <td>
                            {l.dispatchedQty} {l.uomCode}
                          </td>
                          <td>PKR {l.unitCost.toLocaleString()}</td>
                          <td>
                            <strong>PKR {l.totalCost.toLocaleString()}</strong>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ))
            )}
          </article>
        </div>
      )}

      {/* ── TAB 5: INVOICES LEDGER ────────────────────────────────────────── */}
      {activeTab === "invoices" && (
        <article className="inventory-card">
          <h3>Sales Invoices &amp; Fiscal Ledger ({invoices.length})</h3>
          {invoices.length === 0 ? (
            <p className="inventory-empty">No sales invoices posted yet.</p>
          ) : (
            invoices.map((inv) => (
              <div
                key={inv.invoiceId}
                style={{
                  borderBottom: "1px solid var(--border)",
                  paddingBottom: "1rem",
                  marginBottom: "1rem",
                }}
              >
                <div className="inventory-list-row">
                  <div>
                    <strong>{inv.invoiceNumber}</strong>
                    <span
                      style={{
                        marginLeft: "0.75rem",
                        fontSize: "0.85rem",
                        color: "var(--text-muted)",
                      }}
                    >
                      {inv.invoiceDate} · {inv.customerName}
                    </span>
                    {inv.dispatchId && (
                      <span className="tag" style={{ marginLeft: "0.5rem" }}>
                        From Dispatch
                      </span>
                    )}
                  </div>
                  <div>
                    <span className="tag tag-starter" style={{ marginRight: "0.5rem" }}>
                      {inv.paymentMethod.toUpperCase()}
                    </span>
                    <span className="tag">{inv.invoiceStatus}</span>
                  </div>
                </div>

                <div
                  style={{
                    display: "flex",
                    gap: "2rem",
                    marginTop: "0.4rem",
                    fontSize: "0.9rem",
                    flexWrap: "wrap",
                  }}
                >
                  <span>Subtotal: PKR {inv.subtotalAmount.toLocaleString()}</span>
                  <span>Tax: PKR {inv.taxAmount.toLocaleString()}</span>
                  <span>Discount: PKR {inv.discountAmount.toLocaleString()}</span>
                  <strong>Total: PKR {inv.totalAmount.toLocaleString()}</strong>
                  <span style={{ color: "var(--color-success, #2e7d32)" }}>
                    Paid: PKR {inv.paidAmount.toLocaleString()}
                  </span>
                  {inv.glJournalId && (
                    <span style={{ color: "var(--text-muted)" }}>
                      GL: <code>{inv.glJournalId.slice(0, 8)}</code>
                    </span>
                  )}
                </div>

                {(() => {
                  const showCost = inv.lines.some(
                    (l) => l.unitCost !== null && l.unitCost !== undefined
                  );
                  return (
                    <table
                      className="stock-table"
                      style={{ marginTop: "0.5rem", fontSize: "0.8rem", width: "100%" }}
                    >
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
                            <td>
                              <strong>{line.sku}</strong>
                            </td>
                            <td>{line.binCode}</td>
                            <td>
                              {line.quantity} {line.uomCode}
                            </td>
                            <td>PKR {line.unitPrice.toLocaleString()}</td>
                            {showCost && (
                              <td>
                                {line.unitCost !== null
                                  ? `PKR ${line.unitCost.toLocaleString()}`
                                  : "—"}
                              </td>
                            )}
                            <td>{line.taxRate}%</td>
                            <td>
                              <strong>PKR {line.lineTotal.toLocaleString()}</strong>
                            </td>
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

      {/* ── TAB 6: CUSTOMER RECEIPTS ──────────────────────────────────────── */}
      {activeTab === "receipts" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>Record Customer Payment Receipt</h3>
            <form className="inventory-form" onSubmit={(e) => void handleCreateReceipt(e)}>
              <label>
                Receipt Number
                <input
                  required
                  maxLength={64}
                  value={rctNumber}
                  onChange={(e) => setRctNumber(e.target.value)}
                  placeholder="e.g. RCT-2026-001"
                />
              </label>

              <label>
                Customer
                <select
                  required
                  value={rctCustomerId}
                  onChange={(e) => {
                    setRctCustomerId(e.target.value);
                    setRctAllocations([]);
                  }}
                >
                  <option value="">Select customer</option>
                  {customers
                    .filter((c) => c.isActive)
                    .map((c) => (
                      <option key={c.customerId} value={c.customerId}>
                        {c.customerCode} · {c.name} (Bal: PKR{" "}
                        {c.outstandingBalance.toLocaleString()})
                      </option>
                    ))}
                </select>
              </label>

              <div style={{ display: "flex", gap: "1rem" }}>
                <label style={{ flex: "1" }}>
                  Receipt Amount (PKR)
                  <input
                    type="number"
                    min="1"
                    step="0.01"
                    required
                    value={rctAmount}
                    onChange={(e) => setRctAmount(e.target.value)}
                  />
                </label>

                <label style={{ flex: "1" }}>
                  Payment Method
                  <select
                    value={rctPaymentMethod}
                    onChange={(e) => setRctPaymentMethod(e.target.value)}
                  >
                    <option value="bank_transfer">🏛 Bank Transfer (IBFT)</option>
                    <option value="cash">💵 Cash Counter</option>
                    <option value="cheque">📄 Cheque Clearing</option>
                    <option value="card">💳 Card / POS</option>
                    <option value="mobile_wallet">📱 Mobile Wallet</option>
                  </select>
                </label>
              </div>

              <div style={{ display: "flex", gap: "1rem" }}>
                <label style={{ flex: "1" }}>
                  Reference / Cheque #
                  <input
                    placeholder="e.g. FT-987654321"
                    value={rctReference}
                    onChange={(e) => setRctReference(e.target.value)}
                  />
                </label>
                <label style={{ flex: "1" }}>
                  Note
                  <input
                    placeholder="Payment remarks"
                    value={rctNote}
                    onChange={(e) => setRctNote(e.target.value)}
                  />
                </label>
              </div>

              {rctCustomerId && (
                <div style={{ marginTop: "0.5rem" }}>
                  <button
                    type="button"
                    className="secondary-button"
                    onClick={handleAutoAllocateReceipt}
                    style={{ marginBottom: "0.75rem", fontSize: "0.85rem" }}
                  >
                    ⚡ Auto-Allocate to Open Invoices (FIFO)
                  </button>
                  {rctAllocations.length > 0 && (
                    <div
                      style={{
                        background: "rgba(0,0,0,0.02)",
                        padding: "0.75rem",
                        borderRadius: "6px",
                      }}
                    >
                      <h5 style={{ margin: "0 0 0.5rem 0" }}>Allocations Across Open Invoices:</h5>
                      {rctAllocations.map((a, i) => (
                        <div
                          key={i}
                          style={{
                            display: "flex",
                            justifyContent: "space-between",
                            fontSize: "0.85rem",
                            marginBottom: "0.25rem",
                          }}
                        >
                          <span>{a.invoiceNumber}</span>
                          <strong>PKR {parseFloat(a.allocatedAmount).toLocaleString()}</strong>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}

              <button
                disabled={!enabled || isSaving || !rctCustomerId || !rctNumber}
                type="submit"
                style={{ marginTop: "1rem" }}
              >
                Post Payment Receipt &amp; Credit AR
              </button>
            </form>
          </article>

          <article className="inventory-card">
            <h3>Customer Receipts · {receipts.length}</h3>
            {receipts.length === 0 ? (
              <p className="inventory-empty">No customer receipts recorded yet.</p>
            ) : (
              receipts.map((r) => (
                <div
                  key={r.receiptId}
                  style={{
                    borderBottom: "1px solid var(--border)",
                    paddingBottom: "0.75rem",
                    marginBottom: "0.75rem",
                  }}
                >
                  <div className="inventory-list-row">
                    <strong>{r.receiptNumber}</strong>
                    <span className="tag tag-starter">{r.paymentMethod.toUpperCase()}</span>
                  </div>
                  <small>
                    {r.customerName} · PKR {r.amount.toLocaleString()} ({r.receiptDate})
                    {r.unallocatedAmount > 0 && ` · Unallocated: PKR ${r.unallocatedAmount}`}
                  </small>
                  {r.glJournalId && (
                    <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                      GL Journal: <code>{r.glJournalId.slice(0, 8)}</code>
                    </div>
                  )}
                  {r.allocations.map((a) => (
                    <div
                      key={a.allocationId}
                      style={{
                        fontSize: "0.8rem",
                        color: "var(--text-muted)",
                        paddingLeft: "0.5rem",
                        marginTop: "0.2rem",
                      }}
                    >
                      Allocated to {a.invoiceNumber}: PKR {a.allocatedAmount.toLocaleString()}
                    </div>
                  ))}
                </div>
              ))
            )}
          </article>
        </div>
      )}

      {/* ── TAB 7: RETURNS & CREDIT NOTES ─────────────────────────────────── */}
      {activeTab === "returns" && (
        <div className="inventory-grid">
          <div>
            <article className="inventory-card" style={{ marginBottom: "1.5rem" }}>
              <h3>Register Customer Sales Return</h3>
              <form className="inventory-form" onSubmit={(e) => void handleCreateReturn(e)}>
                <label>
                  Return Number
                  <input
                    required
                    maxLength={64}
                    value={retNumber}
                    onChange={(e) => setRetNumber(e.target.value)}
                    placeholder="e.g. RET-2026-001"
                  />
                </label>

                <label>
                  Customer
                  <select
                    required
                    value={retCustomerId}
                    onChange={(e) => setRetCustomerId(e.target.value)}
                  >
                    <option value="">Select customer</option>
                    {customers
                      .filter((c) => c.isActive)
                      .map((c) => (
                        <option key={c.customerId} value={c.customerId}>
                          {c.customerCode} · {c.name}
                        </option>
                      ))}
                  </select>
                </label>

                <label>
                  Original Invoice (Optional)
                  <select
                    value={retInvoiceId}
                    onChange={(e) => setRetInvoiceId(e.target.value)}
                  >
                    <option value="">None / Direct Return</option>
                    {invoices
                      .filter((inv) => !retCustomerId || inv.customerId === retCustomerId)
                      .map((inv) => (
                        <option key={inv.invoiceId} value={inv.invoiceId}>
                          {inv.invoiceNumber} (Total: PKR {inv.totalAmount})
                        </option>
                      ))}
                  </select>
                </label>

                <label>
                  Return Reason
                  <input
                    maxLength={200}
                    placeholder="e.g. Damaged packaging or wrong item"
                    value={retReason}
                    onChange={(e) => setRetReason(e.target.value)}
                  />
                </label>

                <h4 className="inventory-subheading">Return Items</h4>
                {retLines.map((l, idx) => (
                  <div
                    key={idx}
                    style={{
                      border: "1px solid var(--border)",
                      padding: "0.5rem",
                      borderRadius: "6px",
                      marginBottom: "0.5rem",
                    }}
                  >
                    <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
                      <select
                        required
                        value={l.variantId}
                        onChange={(e) =>
                          setRetLines(
                            retLines.map((line, i) =>
                              i === idx ? { ...line, variantId: e.target.value } : line
                            )
                          )
                        }
                        style={{ flex: "2" }}
                      >
                        <option value="">Select variant</option>
                        {variants
                          .filter((v) => v.isActive)
                          .map((v) => (
                            <option key={v.variantId} value={v.variantId}>
                              {v.sku} · {v.productName}
                            </option>
                          ))}
                      </select>

                      <input
                        type="number"
                        min="0.001"
                        step="0.001"
                        placeholder="Qty"
                        required
                        value={l.returnedQty}
                        onChange={(e) =>
                          setRetLines(
                            retLines.map((line, i) =>
                              i === idx ? { ...line, returnedQty: e.target.value } : line
                            )
                          )
                        }
                        style={{ flex: "1", minWidth: "70px" }}
                      />

                      <input
                        type="number"
                        min="0"
                        step="0.01"
                        placeholder="Unit Price"
                        required
                        value={l.unitPrice}
                        onChange={(e) =>
                          setRetLines(
                            retLines.map((line, i) =>
                              i === idx ? { ...line, unitPrice: e.target.value } : line
                            )
                          )
                        }
                        style={{ flex: "1", minWidth: "80px" }}
                      />

                      <input
                        type="number"
                        min="0"
                        step="0.01"
                        placeholder="Cost"
                        required
                        value={l.unitCost}
                        onChange={(e) =>
                          setRetLines(
                            retLines.map((line, i) =>
                              i === idx ? { ...line, unitCost: e.target.value } : line
                            )
                          )
                        }
                        style={{ flex: "1", minWidth: "80px" }}
                      />
                    </div>
                  </div>
                ))}

                <button disabled={!enabled || isSaving || !retCustomerId} type="submit">
                  Create Draft Return
                </button>
              </form>
            </article>

            <article className="inventory-card">
              <h3>Issue Sales Credit Note</h3>
              <form className="inventory-form" onSubmit={(e) => void handleCreateCreditNote(e)}>
                <label>
                  Credit Note Number
                  <input
                    required
                    maxLength={64}
                    value={cnNumber}
                    onChange={(e) => setCnNumber(e.target.value)}
                    placeholder="e.g. CN-2026-001"
                  />
                </label>

                <label>
                  Customer
                  <select
                    required
                    value={cnCustomerId}
                    onChange={(e) => setCnCustomerId(e.target.value)}
                  >
                    <option value="">Select customer</option>
                    {customers
                      .filter((c) => c.isActive)
                      .map((c) => (
                        <option key={c.customerId} value={c.customerId}>
                          {c.customerCode} · {c.name}
                        </option>
                      ))}
                  </select>
                </label>

                <div style={{ display: "flex", gap: "1rem" }}>
                  <label style={{ flex: "1" }}>
                    Subtotal (PKR)
                    <input
                      type="number"
                      min="1"
                      step="0.01"
                      required
                      value={cnSubtotal}
                      onChange={(e) => setCnSubtotal(e.target.value)}
                    />
                  </label>
                  <label style={{ flex: "1" }}>
                    Tax Reversal (PKR)
                    <input
                      type="number"
                      min="0"
                      step="0.01"
                      value={cnTax}
                      onChange={(e) => setCnTax(e.target.value)}
                    />
                  </label>
                </div>

                <label>
                  Reason
                  <input
                    placeholder="e.g. Approved price discount or stock return"
                    value={cnReason}
                    onChange={(e) => setCnReason(e.target.value)}
                  />
                </label>

                <button disabled={!enabled || isSaving || !cnCustomerId} type="submit">
                  Create Credit Note
                </button>
              </form>
            </article>
          </div>

          <div>
            <article className="inventory-card" style={{ marginBottom: "1.5rem" }}>
              <h3>Customer Returns ({returns.length})</h3>
              {returns.length === 0 ? (
                <p className="inventory-empty">No returns recorded yet.</p>
              ) : (
                returns.map((r) => (
                  <div
                    key={r.returnId}
                    style={{
                      borderBottom: "1px solid var(--border)",
                      paddingBottom: "0.75rem",
                      marginBottom: "0.75rem",
                    }}
                  >
                    <div className="inventory-list-row">
                      <strong>{r.returnNumber}</strong>
                      <span className={`tag ${r.status === "approved" ? "tag-starter" : "tag-progress"}`}>
                        {r.status}
                      </span>
                    </div>
                    <small>
                      {r.customerName} · Total: PKR {r.totalAmount.toLocaleString()} · Cost: PKR{" "}
                      {r.totalCost.toLocaleString()}
                    </small>
                    {r.status === "draft" && (
                      <div style={{ marginTop: "0.5rem" }}>
                        <button
                          className="secondary-button"
                          style={{ fontSize: "0.75rem" }}
                          disabled={isSaving}
                          onClick={() => void handleApproveReturn(r.returnId, r.returnNumber)}
                          type="button"
                        >
                          ✓ Approve Return &amp; Restock FIFO Layer
                        </button>
                      </div>
                    )}
                  </div>
                ))
              )}
            </article>

            <article className="inventory-card">
              <h3>Sales Credit Notes ({creditNotes.length})</h3>
              {creditNotes.length === 0 ? (
                <p className="inventory-empty">No credit notes issued yet.</p>
              ) : (
                creditNotes.map((cn) => (
                  <div
                    key={cn.creditNoteId}
                    style={{
                      borderBottom: "1px solid var(--border)",
                      paddingBottom: "0.75rem",
                      marginBottom: "0.75rem",
                    }}
                  >
                    <div className="inventory-list-row">
                      <strong>{cn.creditNoteNumber}</strong>
                      <span className={`tag ${cn.status === "approved" ? "tag-starter" : "tag-progress"}`}>
                        {cn.status}
                      </span>
                    </div>
                    <small>
                      {cn.customerName} · Total: PKR {cn.totalAmount.toLocaleString()} ({cn.creditNoteDate})
                    </small>
                    {cn.status === "draft" && (
                      <div style={{ marginTop: "0.5rem" }}>
                        <button
                          className="secondary-button"
                          style={{ fontSize: "0.75rem" }}
                          disabled={isSaving}
                          onClick={() => void handleApproveCreditNote(cn.creditNoteId, cn.creditNoteNumber)}
                          type="button"
                        >
                          ✓ Approve Credit Note &amp; Reverse Revenue
                        </button>
                      </div>
                    )}
                  </div>
                ))
              )}
            </article>
          </div>
        </div>
      )}

      {/* ── TAB 8: AR AGING & STATEMENTS ─────────────────────────────────── */}
      {activeTab === "ar" && (
        <div>
          <article className="inventory-card" style={{ marginBottom: "1.5rem" }}>
            <h3>Accounts Receivable Subledger Aging Breakdown</h3>
            <p style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
              Real-time classification of outstanding customer receivables into 0-30, 31-60, 61-90, and
              91+ day overdue aging buckets.
            </p>
            {arAging.length === 0 ? (
              <p className="inventory-empty">No outstanding accounts receivable found.</p>
            ) : (
              <table className="stock-table" style={{ width: "100%", marginTop: "1rem" }}>
                <thead>
                  <tr>
                    <th>Customer</th>
                    <th>0–30 Days</th>
                    <th>31–60 Days</th>
                    <th>61–90 Days</th>
                    <th>91+ Days</th>
                    <th>Total Invoiced</th>
                    <th>Credit Notes</th>
                    <th>Receipts</th>
                    <th>Net Outstanding</th>
                    <th>Open Invoices</th>
                  </tr>
                </thead>
                <tbody>
                  {arAging.map((a) => (
                    <tr key={a.customerId}>
                      <td>
                        <strong>{a.customerName}</strong> <small>({a.customerCode})</small>
                      </td>
                      <td>PKR {a.currentBalance.toLocaleString()}</td>
                      <td style={{ color: a.days31To60 > 0 ? "#e65100" : "inherit" }}>
                        PKR {a.days31To60.toLocaleString()}
                      </td>
                      <td style={{ color: a.days61To90 > 0 ? "#c2185b" : "inherit" }}>
                        PKR {a.days61To90.toLocaleString()}
                      </td>
                      <td style={{ color: a.over90Days > 0 ? "var(--color-danger)" : "inherit" }}>
                        PKR {a.over90Days.toLocaleString()}
                      </td>
                      <td>PKR {a.totalReceivable.toLocaleString()}</td>
                      <td>PKR {a.totalCreditNotes.toLocaleString()}</td>
                      <td>PKR {a.totalReceipts.toLocaleString()}</td>
                      <td>
                        <strong
                          style={{
                            color: a.netOutstanding > 0 ? "var(--color-danger)" : "inherit",
                          }}
                        >
                          PKR {a.netOutstanding.toLocaleString()}
                        </strong>
                      </td>
                      <td>{a.openInvoiceCount}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </article>

          <article className="inventory-card">
            <h3>Customer Account Statement Generator</h3>
            <div style={{ display: "flex", gap: "1rem", flexWrap: "wrap", alignItems: "flex-end" }}>
              <label style={{ flex: "2" }}>
                Select Customer
                <select
                  value={stmtCustomerId}
                  onChange={(e) => setStmtCustomerId(e.target.value)}
                >
                  <option value="">Select customer</option>
                  {customers.map((c) => (
                    <option key={c.customerId} value={c.customerId}>
                      {c.customerCode} · {c.name}
                    </option>
                  ))}
                </select>
              </label>

              <label style={{ flex: "1" }}>
                From Date
                <input
                  type="date"
                  value={stmtFromDate}
                  onChange={(e) => setStmtFromDate(e.target.value)}
                />
              </label>

              <label style={{ flex: "1" }}>
                To Date
                <input
                  type="date"
                  value={stmtToDate}
                  onChange={(e) => setStmtToDate(e.target.value)}
                />
              </label>

              <button
                type="button"
                disabled={!stmtCustomerId || isLoadingStatement}
                onClick={() => void handleLoadStatement()}
                style={{ height: "2.4rem" }}
              >
                {isLoadingStatement ? "Generating…" : "Generate Statement"}
              </button>
            </div>

            {statement && (
              <div style={{ marginTop: "1.5rem" }}>
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    padding: "1rem",
                    background: "rgba(0,0,0,0.02)",
                    borderRadius: "6px",
                    marginBottom: "1rem",
                  }}
                >
                  <div>
                    <h4>{statement.customerName}</h4>
                    <small>
                      Code: {statement.customerCode} · Period: {statement.fromDate} to{" "}
                      {statement.toDate}
                    </small>
                  </div>
                  <div style={{ textAlign: "right", fontSize: "0.9rem" }}>
                    <div>
                      Opening Balance:{" "}
                      <strong>PKR {statement.openingBalance.toLocaleString()}</strong>
                    </div>
                    <div>
                      Total Invoiced (Debits):{" "}
                      <strong>PKR {statement.totalDebits.toLocaleString()}</strong>
                    </div>
                    <div>
                      Total Receipts/Credits:{" "}
                      <strong>PKR {statement.totalCredits.toLocaleString()}</strong>
                    </div>
                    <div style={{ fontSize: "1.1rem", marginTop: "0.25rem" }}>
                      Closing Balance:{" "}
                      <strong style={{ color: "var(--color-primary)" }}>
                        PKR {statement.closingBalance.toLocaleString()}
                      </strong>
                    </div>
                  </div>
                </div>

                <table className="stock-table" style={{ width: "100%", fontSize: "0.85rem" }}>
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>Type</th>
                      <th>Ref Number</th>
                      <th>Description</th>
                      <th>Debit (Invoiced)</th>
                      <th>Credit (Paid)</th>
                      <th>Running Balance</th>
                    </tr>
                  </thead>
                  <tbody>
                    {statement.items.map((item, idx) => (
                      <tr key={idx}>
                        <td>{item.transactionDate}</td>
                        <td>
                          <span className="tag">{item.transactionType.toUpperCase()}</span>
                        </td>
                        <td>
                          <strong>{item.referenceNumber}</strong>
                        </td>
                        <td>{item.description ?? "—"}</td>
                        <td>
                          {item.debitAmount > 0
                            ? `PKR ${item.debitAmount.toLocaleString()}`
                            : "—"}
                        </td>
                        <td>
                          {item.creditAmount > 0
                            ? `PKR ${item.creditAmount.toLocaleString()}`
                            : "—"}
                        </td>
                        <td>
                          <strong>PKR {item.runningBalance.toLocaleString()}</strong>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </article>
        </div>
      )}

      {/* ── TAB 9: CUSTOMERS & CREDIT LIMITS ─────────────────────────────── */}
      {activeTab === "customers" && (
        <div className="inventory-grid">
          <div>
            <article className="inventory-card" style={{ marginBottom: "1.5rem" }}>
              <h3>Add Customer</h3>
              <form className="inventory-form" onSubmit={(e) => void handleCreateCustomer(e)}>
                <label>
                  Customer code
                  <input
                    required
                    maxLength={64}
                    value={custCode}
                    onChange={(e) => setCustCode(e.target.value)}
                    placeholder="e.g. CUST-002"
                  />
                </label>

                <label>
                  Customer / Business name
                  <input
                    required
                    maxLength={200}
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
                  <input
                    maxLength={100}
                    value={custContact}
                    onChange={(e) => setCustContact(e.target.value)}
                    placeholder="Optional"
                  />
                </label>

                <label>
                  Phone number
                  <input
                    maxLength={30}
                    value={custPhone}
                    onChange={(e) => setCustPhone(e.target.value)}
                    placeholder="e.g. +92 300 1234567"
                  />
                </label>

                <label>
                  Email
                  <input
                    type="email"
                    maxLength={100}
                    value={custEmail}
                    onChange={(e) => setCustEmail(e.target.value)}
                    placeholder="Optional"
                  />
                </label>

                <div style={{ display: "flex", gap: "1rem" }}>
                  <label style={{ flex: "1" }}>
                    Credit limit (PKR)
                    <input
                      type="number"
                      min="0"
                      step="1000"
                      value={custCreditLimit}
                      onChange={(e) => setCustCreditLimit(e.target.value)}
                    />
                  </label>
                  <label style={{ flex: "1" }}>
                    Payment terms (days)
                    <input
                      type="number"
                      min="0"
                      max="365"
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

            {/* Credit Limit Simulator */}
            <article className="inventory-card">
              <h3>Live Credit Exposure Simulator</h3>
              <p style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
                Test projected credit checks against customer credit limits, outstanding balances, and
                unfulfilled order commitments.
              </p>
              <div style={{ display: "flex", gap: "0.5rem", marginTop: "0.5rem" }}>
                <select
                  value={simCustomerId}
                  onChange={(e) => {
                    setSimCustomerId(e.target.value);
                    setSimResult(null);
                  }}
                  style={{ flex: "2" }}
                >
                  <option value="">Choose customer</option>
                  {customers.map((c) => (
                    <option key={c.customerId} value={c.customerId}>
                      {c.name} (Limit: PKR {c.creditLimit})
                    </option>
                  ))}
                </select>
                <input
                  type="number"
                  min="0"
                  step="100"
                  placeholder="Order PKR"
                  value={simOrderAmount}
                  onChange={(e) => setSimOrderAmount(e.target.value)}
                  style={{ flex: "1" }}
                />
                <button
                  type="button"
                  disabled={!simCustomerId}
                  onClick={() => void handleSimulateCreditCheck()}
                >
                  Check
                </button>
              </div>

              {simResult && (
                <div
                  style={{
                    marginTop: "1rem",
                    padding: "0.75rem",
                    borderRadius: "6px",
                    background: simResult.isAllowed ? "rgba(46, 125, 50, 0.08)" : "rgba(211, 47, 47, 0.08)",
                    border: `1px solid ${
                      simResult.isAllowed ? "rgba(46, 125, 50, 0.3)" : "rgba(211, 47, 47, 0.3)"
                    }`,
                    fontSize: "0.85rem",
                  }}
                >
                  <div style={{ fontWeight: "bold", marginBottom: "0.3rem" }}>
                    {simResult.isAllowed ? "✓ CREDIT CHECK PASSED" : "⚠ CREDIT CHECK FAILED"}
                  </div>
                  <div>{simResult.message}</div>
                  <div style={{ marginTop: "0.4rem" }}>
                    Current Exposure: PKR {simResult.currentExposure.toLocaleString()} · Projected
                    Exposure: PKR {simResult.projectedExposure.toLocaleString()}
                  </div>
                  {simResult.excessAmount > 0 && (
                    <div style={{ color: "var(--color-danger)", fontWeight: "bold" }}>
                      Excess Amount over Limit: PKR {simResult.excessAmount.toLocaleString()}
                    </div>
                  )}
                </div>
              )}
            </article>
          </div>

          <article className="inventory-card">
            <h3>Customer Directory · {customers.length}</h3>
            {customers.length === 0 ? (
              <p className="inventory-empty">No customers registered yet.</p>
            ) : (
              customers.map((c) => (
                <div
                  key={c.customerId}
                  style={{
                    borderBottom: "1px solid var(--border)",
                    paddingBottom: "0.75rem",
                    marginBottom: "0.75rem",
                  }}
                >
                  <div className="inventory-list-row">
                    <strong>{c.name}</strong>
                    <span className="tag">{c.customerType}</span>
                  </div>
                  <div style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
                    Code: <code>{c.customerCode}</code> · Credit Limit: PKR{" "}
                    {c.creditLimit.toLocaleString()}
                  </div>
                  <div style={{ fontSize: "0.85rem", marginTop: "0.2rem" }}>
                    Outstanding Balance:{" "}
                    <strong
                      style={{
                        color: c.outstandingBalance > 0 ? "var(--color-danger, #d32f2f)" : "inherit",
                      }}
                    >
                      PKR {c.outstandingBalance.toLocaleString()}
                    </strong>
                    {c.paymentTermsDays > 0 && ` (${c.paymentTermsDays} days terms)`}
                  </div>
                  {c.phone && (
                    <small style={{ color: "var(--text-muted)" }}>Phone: {c.phone}</small>
                  )}
                </div>
              ))
            )}
          </article>
        </div>
      )}
    </section>
  );
}
