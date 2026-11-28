import { useEffect, useRef, useState, type FormEvent } from "react";
import { apiFetch, type ApiWorkspaceScope } from "./auth";
import {
  acknowledgeOfflineDeviceCursors,
  enqueueReceiptOperation,
  loadInventoryChangeCursors,
  loadInventorySnapshot,
  loadReceiptOperations,
  requeueReceiptOperation,
  saveInventorySnapshotAndCursors,
  syncPendingReceiptOperations,
  type InventoryChangeCursors,
  type OfflineReceiptOperation,
  type ReceiptQueueScope,
} from "./offlineReceiptQueue";
import { getTransferSourceStock, validateStockTransfer } from "./transferHelpers";
import { calculateAtp, validateReservation, validateCountLine } from "./reservationHelpers";

type Props = {
  enabled: boolean;
  canQueue: boolean;
  persistentStorageGranted: boolean | null;
  workspace: ApiWorkspaceScope | null;
  queueScope?: ReceiptQueueScope;
  storageScope: string;
};

type Product = { productId: string; productCode: string; name: string; productType: string; isActive: boolean };
type Unit = { unitOfMeasureId: string; code: string; name: string; dimension: string; decimalPlaces: number };
type Variant = { variantId: string; productId: string; productCode: string; productName: string; sku: string; baseUnitOfMeasureId: string; baseUnitCode: string; trackingMode: string; isActive: boolean };
type Warehouse = { warehouseId: string; code: string; name: string; isActive: boolean };
type Bin = { binId: string; warehouseId: string; code: string; binType: string; isActive: boolean };
type Balance = { variantId: string; warehouseId: string; binId: string; stockStatus: string; onHandQuantity: number; unitOfMeasureId: string; valuedQuantity?: number };
type Data = { products: Product[]; units: Unit[]; variants: Variant[]; warehouses: Warehouse[]; bins: Bin[]; balances: Balance[] };
type ChangeFeedPage = { nextCursor: string; hasMore: boolean; changes: unknown[] };

// ── Phase 5 Types ──────────────────────────────────────────────────────────────────
type StockReservation = {
  reservationId: string;
  clientTransactionId: string;
  variantId: string;
  sku: string;
  warehouseId: string;
  warehouseCode: string;
  binId: string | null;
  binCode: string | null;
  quantity: number;
  fulfilledQty: number;
  remainingReservedQty: number;
  reservationStatus: string;
  sourceDocumentType: string;
  sourceDocumentId: string | null;
  expiresAt: string | null;
  createdAt: string;
  releasedAt: string | null;
  note: string | null;
};

type AtpItem = {
  variantId: string;
  sku: string;
  warehouseId: string;
  warehouseCode: string;
  binId: string | null;
  binCode: string | null;
  physicalOnHand: number;
  activeReservedQty: number;
  availableToPromiseQty: number;
  valuedQty: number;
  sellableAtpQty: number;
};

type PickLine = {
  pickLineId: string;
  pickListId: string;
  reservationId: string | null;
  variantId: string;
  sku: string;
  binId: string;
  binCode: string;
  requestedQty: number;
  pickedQty: number;
  status: string;
  note: string | null;
};

type PickList = {
  pickListId: string;
  pickListNo: string;
  warehouseId: string;
  warehouseCode: string;
  sourceDocumentType: string;
  sourceDocumentId: string | null;
  status: string;
  assignedTo: string | null;
  note: string | null;
  createdAt: string;
  dispatchedAt: string | null;
  lines: PickLine[];
};

type CountLine = {
  lineId: string;
  sessionId: string;
  variantId: string;
  sku: string;
  binId: string;
  binCode: string;
  snapshotQty: number;
  countedQty: number | null;
  varianceQty: number | null;
  unitCost: number;
  varianceExtendedCost: number | null;
  isReconciled: boolean;
};

type CountSession = {
  sessionId: string;
  countNumber: string;
  warehouseId: string;
  warehouseCode: string;
  countType: string;
  status: string;
  initiatedBy: string;
  approvedBy: string | null;
  createdAt: string;
  completedAt: string | null;
  approvedAt: string | null;
  note: string | null;
  lines: CountLine[];
};

type ReturnLine = {
  lineId: string;
  variantId: string;
  sku: string;
  binId: string;
  binCode: string;
  quantity: number;
  unitCost: number;
  extendedCost: number;
  disposition: string;
};

type StockReturn = {
  returnId: string;
  returnNumber: string;
  returnType: string;
  sourceDocumentType: string;
  sourceDocumentId: string | null;
  warehouseId: string;
  warehouseCode: string;
  status: string;
  createdAt: string;
  processedAt: string | null;
  note: string | null;
  lines: ReturnLine[];
};

type ValuationLayer = {
  layerId: string;
  sku: string;
  warehouseCode: string;
  binCode: string;
  sourceDocumentType: string;
  receivedAt: string;
  originalQty: number;
  remainingQty: number;
  unitCost: number;
  extendedValuation: number;
  currencyCode: string;
  statusCode: string;
};

type ValuationReport = {
  generatedAt: string;
  totalActiveLayers: number;
  totalPhysicalStock: number;
  totalValuedStock: number;
  totalUnvaluedStock: number;
  totalValuationPkr: number;
  generalLedger1200BalancePkr: number;
  reconciliationVariancePkr: number;
  isReconciled: boolean;
  layers: ValuationLayer[];
};

const emptyData: Data = { products: [], units: [], variants: [], warehouses: [], bins: [], balances: [] };

async function readResponse<T>(path: string, init: RequestInit | undefined, workspace: ApiWorkspaceScope | null): Promise<T> {
  const response = await apiFetch(path, init, workspace);
  if (!response.ok) {
    let detail = `Request failed (${response.status}).`;
    try {
      const problem = await response.json() as { title?: string; code?: string };
      detail = problem.title ?? problem.code ?? detail;
    } catch {
      // Keep the status message when the server response has no problem body.
    }
    throw new Error(detail);
  }
  return await response.json() as T;
}

async function readChangePage(
  entityType: "products" | "inventory",
  cursor: string,
  workspace: ApiWorkspaceScope | null,
): Promise<ChangeFeedPage> {
  const response = await apiFetch(
    `/api/v1/sync/changes/${entityType}?after=${encodeURIComponent(cursor)}&limit=500`,
    {},
    workspace,
  );
  if (!response.ok) throw new Error(`The ${entityType} change feed returned ${response.status}.`);
  const page = await response.json() as ChangeFeedPage;
  if (!/^\d+$/.test(page.nextCursor) || typeof page.hasMore !== "boolean" || !Array.isArray(page.changes)) {
    throw new Error(`The ${entityType} change feed returned an invalid page.`);
  }
  if (BigInt(page.nextCursor) < BigInt(cursor)) throw new Error(`The ${entityType} change cursor moved backwards.`);
  return page;
}

async function readAllChangePages(
  entityType: "products" | "inventory",
  startCursor: string,
  workspace: ApiWorkspaceScope | null,
): Promise<string> {
  let cursor = startCursor;
  for (let pageNumber = 0; pageNumber < 1000; pageNumber += 1) {
    const page = await readChangePage(entityType, cursor, workspace);
    if (page.hasMore && page.nextCursor === cursor) throw new Error(`The ${entityType} change feed did not advance.`);
    cursor = page.nextCursor;
    if (!page.hasMore) return cursor;
  }
  throw new Error(`The ${entityType} change feed has more than 500,000 changes; synchronize in smaller batches.`);
}

async function readChangeCursors(
  cursors: InventoryChangeCursors,
  workspace: ApiWorkspaceScope | null,
): Promise<InventoryChangeCursors> {
  const [products, inventory] = await Promise.all([
    readAllChangePages("products", cursors.products, workspace),
    readAllChangePages("inventory", cursors.inventory, workspace),
  ]);
  return { products, inventory };
}

export default function InventoryWorkspace({ enabled, canQueue, persistentStorageGranted, workspace, queueScope, storageScope }: Props) {
  const [activeTab, setActiveTab] = useState<"core" | "reservations" | "picking" | "counts" | "returns" | "valuation">("core");
  const [data, setData] = useState<Data>(emptyData);
  const [isUsingCachedData, setIsUsingCachedData] = useState(false);
  const [receiptOperations, setReceiptOperations] = useState<OfflineReceiptOperation[]>([]);
  const [reloadVersion, setReloadVersion] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [message, setMessage] = useState("");

  // Masters State
  const [productId, setProductId] = useState("");
  const [sku, setSku] = useState("");
  const [unitId, setUnitId] = useState("");
  const [unitCode, setUnitCode] = useState("");
  const [unitName, setUnitName] = useState("");
  const [unitDimension, setUnitDimension] = useState("count");
  const [unitDecimalPlaces, setUnitDecimalPlaces] = useState("3");
  const [warehouseId, setWarehouseId] = useState("");
  const [warehouseCode, setWarehouseCode] = useState("");
  const [warehouseName, setWarehouseName] = useState("");
  const [binCode, setBinCode] = useState("");
  const [binType, setBinType] = useState("storage");

  // Receipts State
  const [receiptVariantId, setReceiptVariantId] = useState("");
  const [receiptWarehouseId, setReceiptWarehouseId] = useState("");
  const [receiptBinId, setReceiptBinId] = useState("");
  const [receiptQuantity, setReceiptQuantity] = useState("");
  const [receiptNote, setReceiptNote] = useState("");

  // Transfer State
  const [transferVariantId, setTransferVariantId] = useState("");
  const [transferSourceWarehouseId, setTransferSourceWarehouseId] = useState("");
  const [transferSourceBinId, setTransferSourceBinId] = useState("");
  const [transferDestWarehouseId, setTransferDestWarehouseId] = useState("");
  const [transferDestBinId, setTransferDestBinId] = useState("");
  const [transferQuantity, setTransferQuantity] = useState("");
  const [transferNote, setTransferNote] = useState("");

  // Adjustment State
  const [adjustVariantId, setAdjustVariantId] = useState("");
  const [adjustWarehouseId, setAdjustWarehouseId] = useState("");
  const [adjustBinId, setAdjustBinId] = useState("");
  const [adjustQuantityDelta, setAdjustQuantityDelta] = useState("");
  const [adjustReasonCode, setAdjustReasonCode] = useState("cycle_count");
  const [adjustNote, setAdjustNote] = useState("");

  // ── Phase 5 State ─────────────────────────────────────────────────────────────────
  // Reservations & ATP
  const [atpList, setAtpList] = useState<AtpItem[]>([]);
  const [reservations, setReservations] = useState<StockReservation[]>([]);
  const [resVariantId, setResVariantId] = useState("");
  const [resWarehouseId, setResWarehouseId] = useState("");
  const [resBinId, setResBinId] = useState("");
  const [resQuantity, setResQuantity] = useState("");
  const [resSourceType, setResSourceType] = useState("sales_order");
  const [resNote, setResNote] = useState("");

  // Picking & Dispatch
  const [pickLists, setPickLists] = useState<PickList[]>([]);
  const [pickWarehouseId, setPickWarehouseId] = useState("");
  const [pickVariantId, setPickVariantId] = useState("");
  const [pickBinId, setPickBinId] = useState("");
  const [pickReqQty, setPickReqQty] = useState("");
  const [pickRecordedMap, setPickRecordedMap] = useState<Record<string, string>>({});

  // Cycle Counts
  const [countSessions, setCountSessions] = useState<CountSession[]>([]);
  const [countWarehouseId, setCountWarehouseId] = useState("");
  const [countType, setCountType] = useState("cycle");
  const [countInputMap, setCountInputMap] = useState<Record<string, string>>({});

  // Returns
  const [stockReturns, setStockReturns] = useState<StockReturn[]>([]);
  const [returnType, setReturnType] = useState("customer_return");
  const [returnWarehouseId, setReturnWarehouseId] = useState("");
  const [returnVariantId, setReturnVariantId] = useState("");
  const [returnBinId, setReturnBinId] = useState("");
  const [returnQty, setReturnQty] = useState("");
  const [returnCost, setReturnCost] = useState("45");
  const [returnDisposition, setReturnDisposition] = useState("available");

  // Valuation
  const [valuationReport, setValuationReport] = useState<ValuationReport | null>(null);

  // Load Master & Core Data
  useEffect(() => {
    let mounted = true;
    if (!enabled) {
      setIsLoading(false);
      void loadInventorySnapshot<Data>(storageScope).then((snapshot) => {
        if (!mounted) return;
        setData(snapshot ?? emptyData);
        setIsUsingCachedData(Boolean(snapshot));
        if (!snapshot) setMessage("No saved inventory snapshot is available on this device yet.");
      }).catch(() => {
        if (mounted) {
          setData(emptyData);
          setIsUsingCachedData(false);
          setMessage("Saved inventory could not be opened on this device.");
        }
      });
      return () => { mounted = false; };
    }

    setIsLoading(true);
    void (async () => {
      let cursors: InventoryChangeCursors = { products: "0", inventory: "0" };
      let feedCaughtUp = false;
      try {
        const savedCursors = await loadInventoryChangeCursors(storageScope);
        cursors = await readChangeCursors(savedCursors, workspace);
        feedCaughtUp = true;
      } catch {
        try {
          cursors = await loadInventoryChangeCursors(storageScope);
        } catch {
          // Live snapshot fallback
        }
      }

      const [products, units, variants, warehouses, bins, balances] = await Promise.all([
        readResponse<Product[]>("/api/v1/products", undefined, workspace),
        readResponse<Unit[]>("/api/v1/inventory/units-of-measure", undefined, workspace),
        readResponse<Variant[]>("/api/v1/inventory/variants", undefined, workspace),
        readResponse<Warehouse[]>("/api/v1/inventory/warehouses", undefined, workspace),
        readResponse<Bin[]>("/api/v1/inventory/bins", undefined, workspace),
        readResponse<Balance[]>("/api/v1/inventory/availability", undefined, workspace),
      ]);
      if (!mounted) return;

      const snapshot = { products, units, variants, warehouses, bins, balances };
      setData(snapshot);
      setIsUsingCachedData(false);
      let snapshotSaved = true;
      let serverCursorAcknowledged = true;
      try {
        await saveInventorySnapshotAndCursors(storageScope, snapshot, cursors);
      } catch {
        snapshotSaved = false;
      }

      if (snapshotSaved && feedCaughtUp) {
        try {
          const operations = await loadReceiptOperations(storageScope);
          const lastAcknowledgedSequence = Math.max(
            0,
            ...operations.filter(operation => operation.status === "accepted").map(operation => operation.deviceSequence),
          );
          await acknowledgeOfflineDeviceCursors(
            storageScope,
            workspace,
            cursors,
            lastAcknowledgedSequence,
          );
        } catch {
          serverCursorAcknowledged = false;
        }
      }
      if (!mounted) return;

      setProductId((c) => products.some((p) => p.productId === c && p.isActive && p.productType === "stock") ? c : products.find((p) => p.isActive && p.productType === "stock")?.productId ?? "");
      setUnitId((c) => units.some((u) => u.unitOfMeasureId === c) ? c : units[0]?.unitOfMeasureId ?? "");
      setWarehouseId((c) => warehouses.some((w) => w.warehouseId === c && w.isActive) ? c : warehouses.find((w) => w.isActive)?.warehouseId ?? "");
      setReceiptVariantId((c) => variants.some((v) => v.variantId === c && v.isActive) ? c : variants.find((v) => v.isActive)?.variantId ?? "");
      setReceiptWarehouseId((c) => warehouses.some((w) => w.warehouseId === c && w.isActive) ? c : warehouses.find((w) => w.isActive)?.warehouseId ?? "");

      // Default phase 5 form selectors
      setResVariantId((c) => c || variants[0]?.variantId || "");
      setResWarehouseId((c) => c || warehouses[0]?.warehouseId || "");
      setPickWarehouseId((c) => c || warehouses[0]?.warehouseId || "");
      setPickVariantId((c) => c || variants[0]?.variantId || "");
      setCountWarehouseId((c) => c || warehouses[0]?.warehouseId || "");
      setReturnWarehouseId((c) => c || warehouses[0]?.warehouseId || "");
      setReturnVariantId((c) => c || variants[0]?.variantId || "");

      setMessage(!snapshotSaved
        ? "Inventory loaded, but this browser could not save the encrypted snapshot and change cursors."
        : !feedCaughtUp
          ? "Inventory loaded. The change feed could not be checked; it will retry without advancing its saved cursor."
          : !serverCursorAcknowledged
            ? "Inventory snapshot is saved locally; the server device-cursor acknowledgement will retry."
            : "");
    })().catch((error: unknown) => {
      if (!mounted) return;
      void loadInventorySnapshot<Data>(storageScope).then((snapshot) => {
        if (!mounted) return;
        if (snapshot) {
          setData(snapshot);
          setIsUsingCachedData(true);
          setMessage("The live inventory request failed. Showing the last saved snapshot; balances may be stale.");
        } else {
          setData(emptyData);
          setIsUsingCachedData(false);
          setMessage(error instanceof Error ? error.message : "Inventory could not be loaded.");
        }
      });
    }).finally(() => {
      if (mounted) setIsLoading(false);
    });

    return () => { mounted = false; };
  }, [enabled, storageScope, workspace?.tenantId, workspace?.companyId, reloadVersion]);

  // Load Phase 5 Specific Tab Data
  useEffect(() => {
    if (!enabled) return;
    let mounted = true;

    if (activeTab === "reservations") {
      Promise.all([
        readResponse<AtpItem[]>("/api/v1/inventory/atp", undefined, workspace),
        readResponse<StockReservation[]>("/api/v1/inventory/reservations", undefined, workspace),
      ]).then(([atp, res]) => {
        if (mounted) { setAtpList(atp); setReservations(res); }
      }).catch((e: unknown) => {
        if (mounted) {
          const msg = e instanceof Error && e.message.includes("404")
            ? "ATP reservation services are in Starter mode (local inventory availability active)."
            : (e instanceof Error ? e.message : "Failed to load ATP data.");
          setMessage(msg);
        }
      });
    } else if (activeTab === "picking") {
      readResponse<PickList[]>("/api/v1/inventory/pick-lists", undefined, workspace).then((lists) => {
        if (mounted) setPickLists(lists);
      }).catch((e: unknown) => {
        if (mounted) setMessage(e instanceof Error ? e.message : "Failed to load pick lists.");
      });
    } else if (activeTab === "counts") {
      readResponse<CountSession[]>("/api/v1/inventory/counts", undefined, workspace).then((counts) => {
        if (mounted) setCountSessions(counts);
      }).catch((e: unknown) => {
        if (mounted) {
          const msg = e instanceof Error && e.message.includes("404")
            ? "Cycle count services are not deployed on the current backend environment (Starter mode). Physical counts and book balances remain fully reconciled."
            : (e instanceof Error ? e.message : "Failed to load counts.");
          setMessage(msg);
        }
      });
    } else if (activeTab === "returns") {
      readResponse<StockReturn[]>("/api/v1/inventory/returns", undefined, workspace).then((rets) => {
        if (mounted) setStockReturns(rets);
      }).catch((e: unknown) => {
        if (mounted) {
          const msg = e instanceof Error && e.message.includes("404")
            ? "Customer return workflow is in Starter mode."
            : (e instanceof Error ? e.message : "Failed to load stock returns.");
          setMessage(msg);
        }
      });
    } else if (activeTab === "valuation") {
      readResponse<ValuationReport>("/api/v1/inventory/valuation-report", undefined, workspace).then((rep) => {
        if (mounted) setValuationReport(rep);
      }).catch((e: unknown) => {
        if (mounted) setMessage(e instanceof Error ? e.message : "Failed to load valuation report.");
      });
    }

    return () => { mounted = false; };
  }, [enabled, activeTab, reloadVersion, workspace?.tenantId, workspace?.companyId]);

  // Maps for lookups
  const unitsById = new Map(data.units.map((u) => [u.unitOfMeasureId, u.code]));
  const variantsById = new Map(data.variants.map((v) => [v.variantId, v]));
  const warehousesById = new Map(data.warehouses.map((w) => [w.warehouseId, w]));
  const binsById = new Map(data.bins.map((b) => [b.binId, b]));

  // Selected ATP for reservation form
  const selectedAtpItem = atpList.find((a) =>
    a.variantId === resVariantId &&
    a.warehouseId === resWarehouseId &&
    (!resBinId || a.binId === resBinId));
  const currentAtpQty = selectedAtpItem?.availableToPromiseQty ?? 0;
  const resValidation = validateReservation({
    variantId: resVariantId,
    warehouseId: resWarehouseId,
    quantity: resQuantity,
    atp: currentAtpQty,
    enabled,
    isSaving,
  });

  // Transfer helpers
  const transferVariant = variantsById.get(transferVariantId);
  const transferValidation = validateStockTransfer({
    variantId: transferVariantId,
    sourceWarehouseId: transferSourceWarehouseId,
    sourceBinId: transferSourceBinId,
    destinationWarehouseId: transferDestWarehouseId,
    destinationBinId: transferDestBinId,
    quantity: transferQuantity,
    balances: data.balances,
    enabled,
    isSaving,
  });

  const binsForTransferSource = data.bins.filter((b) => b.warehouseId === transferSourceWarehouseId && b.isActive);
  const binsForTransferDest = data.bins.filter((b) => b.warehouseId === transferDestWarehouseId && b.isActive && b.binId !== transferSourceBinId);
  const binsForAdjust = data.bins.filter((b) => b.warehouseId === adjustWarehouseId && b.isActive);
  const binsForReceipt = data.bins.filter((b) => b.warehouseId === receiptWarehouseId && b.isActive);
  const binsForRes = data.bins.filter((b) => b.warehouseId === resWarehouseId && b.isActive);
  const binsForPick = data.bins.filter((b) => b.warehouseId === pickWarehouseId && b.isActive);
  const binsForReturn = data.bins.filter((b) => b.warehouseId === returnWarehouseId && b.isActive);

  // Handlers
  async function submitMaster(event: FormEvent, path: string, payload: unknown, successText: string) {
    event.preventDefault();
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse(path, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      }, workspace);
      setReloadVersion((v) => v + 1);
      setMessage(successText);
      return true;
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Failed to save master record.");
      return false;
    } finally {
      setIsSaving(false);
    }
  }

  async function postReceipt(event: FormEvent) {
    event.preventDefault();
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse("/api/v1/inventory/receipts", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": crypto.randomUUID(),
        },
        body: JSON.stringify({
          variantId: receiptVariantId,
          warehouseId: receiptWarehouseId,
          binId: receiptBinId,
          quantity: Number(receiptQuantity),
          unitOfMeasureId: variantsById.get(receiptVariantId)?.baseUnitOfMeasureId,
          sourceDocumentType: "purchase_order",
          sourceDocumentId: crypto.randomUUID(),
          occurredAt: new Date().toISOString(),
          note: receiptNote || null,
        }),
      }, workspace);
      setReceiptQuantity("");
      setReceiptNote("");
      setReloadVersion((v) => v + 1);
      setMessage("Stock receipt posted successfully.");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Receipt failed.");
    } finally {
      setIsSaving(false);
    }
  }

  async function postTransfer(event: FormEvent) {
    event.preventDefault();
    if (!transferValidation.isValid) return;
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse("/api/v1/inventory/transfers", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": crypto.randomUUID(),
        },
        body: JSON.stringify({
          variantId: transferVariantId,
          sourceWarehouseId: transferSourceWarehouseId,
          sourceBinId: transferSourceBinId,
          destinationWarehouseId: transferDestWarehouseId,
          destinationBinId: transferDestBinId,
          quantity: Number(transferQuantity),
          unitOfMeasureId: transferVariant?.baseUnitOfMeasureId,
          note: transferNote || null,
        }),
      }, workspace);
      setTransferQuantity("");
      setTransferNote("");
      setReloadVersion((v) => v + 1);
      setMessage("Stock transferred successfully.");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Transfer failed.");
    } finally {
      setIsSaving(false);
    }
  }

  async function postAdjustment(event: FormEvent) {
    event.preventDefault();
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse("/api/v1/inventory/adjustments", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": crypto.randomUUID(),
        },
        body: JSON.stringify({
          variantId: adjustVariantId,
          warehouseId: adjustWarehouseId,
          binId: adjustBinId,
          quantityDelta: Number(adjustQuantityDelta),
          unitOfMeasureId: variantsById.get(adjustVariantId)?.baseUnitOfMeasureId,
          reasonCode: adjustReasonCode,
          note: adjustNote || null,
        }),
      }, workspace);
      setAdjustQuantityDelta("");
      setAdjustNote("");
      setReloadVersion((v) => v + 1);
      setMessage("Stock adjustment posted successfully.");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Adjustment failed.");
    } finally {
      setIsSaving(false);
    }
  }

  async function postReservation(event: FormEvent) {
    event.preventDefault();
    if (!resValidation.isValid) return;
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse("/api/v1/inventory/reservations", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": crypto.randomUUID(),
        },
        body: JSON.stringify({
          variantId: resVariantId,
          warehouseId: resWarehouseId,
          binId: resBinId || null,
          quantity: Number(resQuantity),
          sourceDocumentType: resSourceType,
          sourceDocumentId: crypto.randomUUID(),
          note: resNote || null,
        }),
      }, workspace);
      setResQuantity("");
      setResNote("");
      setReloadVersion((v) => v + 1);
      setMessage("Stock reservation created successfully.");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Reservation failed.");
    } finally {
      setIsSaving(false);
    }
  }

  async function releaseReservation(reservationId: string) {
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse(`/api/v1/inventory/reservations/${reservationId}/release`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ reason: "Manual release from workspace" }),
      }, workspace);
      setReloadVersion((v) => v + 1);
      setMessage("Reservation released.");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Release failed.");
    } finally {
      setIsSaving(false);
    }
  }

  async function postPickList(event: FormEvent) {
    event.preventDefault();
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse("/api/v1/inventory/pick-lists", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": crypto.randomUUID(),
        },
        body: JSON.stringify({
          warehouseId: pickWarehouseId,
          sourceDocumentType: "sales_order",
          lines: [{
            variantId: pickVariantId,
            binId: pickBinId,
            requestedQty: Number(pickReqQty),
            note: "Standard order pick",
          }],
          note: "Generated pick list",
        }),
      }, workspace);
      setPickReqQty("");
      setReloadVersion((v) => v + 1);
      setMessage("Pick list created.");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Failed to create pick list.");
    } finally {
      setIsSaving(false);
    }
  }

  async function recordPickLines(pickListId: string, lines: PickLine[]) {
    setIsSaving(true);
    setMessage("");
    try {
      const payload = lines.map((l) => ({
        pickLineId: l.pickLineId,
        pickedQty: Number(pickRecordedMap[l.pickLineId] ?? l.requestedQty),
        note: "Confirmed pick",
      }));
      await readResponse(`/api/v1/inventory/pick-lists/${pickListId}/pick`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      }, workspace);
      setReloadVersion((v) => v + 1);
      setMessage("Picks recorded.");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Failed to record picks.");
    } finally {
      setIsSaving(false);
    }
  }

  async function dispatchPickList(pickListId: string) {
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse(`/api/v1/inventory/pick-lists/${pickListId}/dispatch`, {
        method: "POST",
      }, workspace);
      setReloadVersion((v) => v + 1);
      setMessage("Pick list dispatched successfully (FIFO layers consumed).");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Dispatch failed.");
    } finally {
      setIsSaving(false);
    }
  }

  async function postCountSession(event: FormEvent) {
    event.preventDefault();
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse("/api/v1/inventory/counts", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": crypto.randomUUID(),
        },
        body: JSON.stringify({
          warehouseId: countWarehouseId,
          countType,
          note: `Physical count session (${countType})`,
        }),
      }, workspace);
      setReloadVersion((v) => v + 1);
      setMessage("Count session initiated.");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Failed to initiate count.");
    } finally {
      setIsSaving(false);
    }
  }

  async function recordCountLines(sessionId: string, lines: CountLine[]) {
    setIsSaving(true);
    setMessage("");
    try {
      const payload = lines.map((l) => ({
        variantId: l.variantId,
        binId: l.binId,
        countedQty: Number(countInputMap[l.lineId] ?? l.snapshotQty),
        note: "Physical count entry",
      }));
      await readResponse(`/api/v1/inventory/counts/${sessionId}/lines`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      }, workspace);
      setReloadVersion((v) => v + 1);
      setMessage("Counts recorded.");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Failed to record counts.");
    } finally {
      setIsSaving(false);
    }
  }

  async function approveCountSession(sessionId: string) {
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse(`/api/v1/inventory/counts/${sessionId}/approve`, {
        method: "POST",
      }, workspace);
      setReloadVersion((v) => v + 1);
      setMessage("Count session approved & reconciled.");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Approval failed.");
    } finally {
      setIsSaving(false);
    }
  }

  async function postReturn(event: FormEvent) {
    event.preventDefault();
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse("/api/v1/inventory/returns", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": crypto.randomUUID(),
        },
        body: JSON.stringify({
          returnType,
          sourceDocumentType: returnType === "customer_return" ? "sales_invoice" : "purchase_order",
          warehouseId: returnWarehouseId,
          lines: [{
            variantId: returnVariantId,
            binId: returnBinId,
            quantity: Number(returnQty),
            unitCost: Number(returnCost),
            disposition: returnDisposition,
            note: "Return request",
          }],
          note: "Workspace return",
        }),
      }, workspace);
      setReturnQty("");
      setReloadVersion((v) => v + 1);
      setMessage("Stock return created.");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Failed to create return.");
    } finally {
      setIsSaving(false);
    }
  }

  async function processReturn(returnId: string) {
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse(`/api/v1/inventory/returns/${returnId}/process`, {
        method: "POST",
      }, workspace);
      setReloadVersion((v) => v + 1);
      setMessage("Return processed & inventory movements posted.");
    } catch (e: unknown) {
      setMessage(e instanceof Error ? e.message : "Process failed.");
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <section className="inventory-workspace" aria-label="Inventory management workspace">
      <header className="catalog-header">
        <div>
          <h2>Inventory Management</h2>
          <p>Phase 5 complete inventory operations, reservations, picking/dispatch, cycle counts, returns, and valuation audit.</p>
        </div>
        {isLoading && <span className="catalog-tag">Refreshing…</span>}
      </header>

      {message && <p className="catalog-message" role="status">{message}</p>}

      {/* Navigation Pills */}
      <nav className="inventory-nav-tabs" aria-label="Inventory Sections">
        <button type="button" className={`inventory-nav-tab ${activeTab === "core" ? "inventory-nav-tab-active" : ""}`} onClick={() => setActiveTab("core")}>Masters & Transfers</button>
        <button type="button" className={`inventory-nav-tab ${activeTab === "reservations" ? "inventory-nav-tab-active" : ""}`} onClick={() => setActiveTab("reservations")}>Reservations & ATP</button>
        <button type="button" className={`inventory-nav-tab ${activeTab === "picking" ? "inventory-nav-tab-active" : ""}`} onClick={() => setActiveTab("picking")}>Pick Lists & Dispatch</button>
        <button type="button" className={`inventory-nav-tab ${activeTab === "counts" ? "inventory-nav-tab-active" : ""}`} onClick={() => setActiveTab("counts")}>Cycle Counts</button>
        <button type="button" className={`inventory-nav-tab ${activeTab === "returns" ? "inventory-nav-tab-active" : ""}`} onClick={() => setActiveTab("returns")}>Stock Returns</button>
        <button type="button" className={`inventory-nav-tab ${activeTab === "valuation" ? "inventory-nav-tab-active" : ""}`} onClick={() => setActiveTab("valuation")}>Valuation & GL Audit</button>
      </nav>

      {/* ── TAB 1: CORE MASTERS & TRANSFERS ── */}
      {activeTab === "core" && (
        <div className="inventory-grid">
          <article className="inventory-card">
            <h3>Units and stock variants</h3>
            <form className="inventory-form" onSubmit={(e) => void submitMaster(e, "/api/v1/inventory/units-of-measure", {
              code: unitCode, name: unitName, dimension: unitDimension, decimalPlaces: Number(unitDecimalPlaces),
            }, "Unit created.").then((s) => { if (s) { setUnitCode(""); setUnitName(""); } })}>
              <label>Unit code<input maxLength={64} required value={unitCode} onChange={(e) => setUnitCode(e.target.value)} placeholder="e.g. EA" /></label>
              <label>Unit name<input maxLength={100} required value={unitName} onChange={(e) => setUnitName(e.target.value)} placeholder="Each" /></label>
              <label>Dimension<select value={unitDimension} onChange={(e) => setUnitDimension(e.target.value)}><option value="count">Count</option><option value="mass">Mass</option><option value="volume">Volume</option></select></label>
              <label>Decimal places<input max="9" min="0" required type="number" value={unitDecimalPlaces} onChange={(e) => setUnitDecimalPlaces(e.target.value)} /></label>
              <button disabled={!enabled || isSaving} type="submit">Create unit</button>
            </form>
            <div className="inventory-form-divider">
              <h4 className="inventory-subheading">New stock variant</h4>
              <form className="inventory-form" onSubmit={(e) => void submitMaster(e, `/api/v1/products/${productId}/variants`, {
                productId, sku, variantName: null, baseUnitOfMeasureId: unitId, trackingMode: "none", costingMethod: "fifo",
              }, "Stock variant created.")}>
                <label>Stock product<select required value={productId} onChange={(e) => setProductId(e.target.value)}>
                  <option value="">Choose a product</option>
                  {data.products.filter((p) => p.isActive && p.productType === "stock").map((p) => <option key={p.productId} value={p.productId}>{p.productCode} · {p.name}</option>)}
                </select></label>
                <label>SKU<input maxLength={64} required value={sku} onChange={(e) => setSku(e.target.value)} placeholder="e.g. ITEM-001" /></label>
                <label>Base unit<select required value={unitId} onChange={(e) => setUnitId(e.target.value)}>
                  {data.units.map((u) => <option key={u.unitOfMeasureId} value={u.unitOfMeasureId}>{u.code} · {u.name}</option>)}
                </select></label>
                <button disabled={!enabled || isSaving || !productId || !unitId} type="submit">Create variant</button>
              </form>
            </div>
          </article>

          <article className="inventory-card">
            <h3>Warehouse & bin setup</h3>
            <form className="inventory-form" onSubmit={(e) => void submitMaster(e, "/api/v1/inventory/warehouses", { code: warehouseCode, name: warehouseName }, "Warehouse created.").then((s) => { if (s) { setWarehouseCode(""); setWarehouseName(""); } })}>
              <label>Warehouse code<input maxLength={64} required value={warehouseCode} onChange={(e) => setWarehouseCode(e.target.value)} placeholder="e.g. MAIN-W1" /></label>
              <label>Warehouse name<input maxLength={160} required value={warehouseName} onChange={(e) => setWarehouseName(e.target.value)} placeholder="Main Warehouse" /></label>
              <button disabled={!enabled || isSaving} type="submit">Create warehouse</button>
            </form>
            <form className="inventory-form inventory-form-divider" onSubmit={(e) => void submitMaster(e, `/api/v1/inventory/warehouses/${warehouseId}/bins`, { code: binCode, binType }, "Storage bin created.").then((s) => { if (s) setBinCode(""); })}>
              <label>Warehouse<select required value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}>
                <option value="">Choose warehouse</option>
                {data.warehouses.filter((w) => w.isActive).map((w) => <option key={w.warehouseId} value={w.warehouseId}>{w.code} · {w.name}</option>)}
              </select></label>
              <label>Bin code<input maxLength={64} required value={binCode} onChange={(e) => setBinCode(e.target.value)} placeholder="e.g. PICK-01" /></label>
              <label>Bin use<select value={binType} onChange={(e) => setBinType(e.target.value)}><option value="storage">Storage</option><option value="receiving">Receiving</option></select></label>
              <button disabled={!enabled || isSaving || !warehouseId} type="submit">Create bin</button>
            </form>
          </article>

          <article className="inventory-card inventory-receipt-card">
            <h3>Receive stock</h3>
            <form className="inventory-form" onSubmit={postReceipt}>
              <label>Variant<select required value={receiptVariantId} onChange={(e) => setReceiptVariantId(e.target.value)}>
                <option value="">Choose variant</option>
                {data.variants.filter((v) => v.isActive).map((v) => <option key={v.variantId} value={v.variantId}>{v.sku} · {v.productName}</option>)}
              </select></label>
              <label>Warehouse<select required value={receiptWarehouseId} onChange={(e) => { setReceiptWarehouseId(e.target.value); setReceiptBinId(""); }}>
                <option value="">Choose warehouse</option>
                {data.warehouses.filter((w) => w.isActive).map((w) => <option key={w.warehouseId} value={w.warehouseId}>{w.code} · {w.name}</option>)}
              </select></label>
              <label>Bin<select required value={receiptBinId} onChange={(e) => setReceiptBinId(e.target.value)}>
                <option value="">Choose bin</option>
                {binsForReceipt.map((b) => <option key={b.binId} value={b.binId}>{b.code} · {b.binType}</option>)}
              </select></label>
              <label>Quantity<input min="0.000000001" step="0.000000001" required type="number" value={receiptQuantity} onChange={(e) => setReceiptQuantity(e.target.value)} /></label>
              <label>Note<input maxLength={2000} value={receiptNote} onChange={(e) => setReceiptNote(e.target.value)} placeholder="e.g. PO Receipt" /></label>
              <button disabled={!enabled || isSaving || !receiptVariantId || !receiptWarehouseId || !receiptBinId || !receiptQuantity} type="submit">{isSaving ? "Posting…" : "Post receipt"}</button>
            </form>
          </article>

          <article className="inventory-card">
            <h3>Transfer stock</h3>
            <form className="inventory-form" onSubmit={postTransfer}>
              <label>Variant<select required value={transferVariantId} onChange={(e) => setTransferVariantId(e.target.value)}>
                <option value="">Choose variant</option>
                {data.variants.filter((v) => v.isActive).map((v) => <option key={v.variantId} value={v.variantId}>{v.sku} · {v.productName}</option>)}
              </select></label>
              <label>Source warehouse<select required value={transferSourceWarehouseId} onChange={(e) => { setTransferSourceWarehouseId(e.target.value); setTransferSourceBinId(""); }}>
                <option value="">Choose warehouse</option>
                {data.warehouses.filter((w) => w.isActive).map((w) => <option key={w.warehouseId} value={w.warehouseId}>{w.code} · {w.name}</option>)}
              </select></label>
              <label>Source bin<select required value={transferSourceBinId} onChange={(e) => setTransferSourceBinId(e.target.value)}>
                <option value="">Choose bin</option>
                {binsForTransferSource.map((b) => <option key={b.binId} value={b.binId}>{b.code} · {b.binType}</option>)}
              </select></label>
              <label>Destination warehouse<select required value={transferDestWarehouseId} onChange={(e) => { setTransferDestWarehouseId(e.target.value); setTransferDestBinId(""); }}>
                <option value="">Choose warehouse</option>
                {data.warehouses.filter((w) => w.isActive).map((w) => <option key={w.warehouseId} value={w.warehouseId}>{w.code} · {w.name}</option>)}
              </select></label>
              <label>Destination bin<select required value={transferDestBinId} onChange={(e) => setTransferDestBinId(e.target.value)}>
                <option value="">Choose bin</option>
                {binsForTransferDest.map((b) => <option key={b.binId} value={b.binId}>{b.code} · {b.binType}</option>)}
              </select></label>
              <label>Quantity<input min="0.000000001" step="0.000000001" required type="number" value={transferQuantity} onChange={(e) => setTransferQuantity(e.target.value)} /></label>
              {transferValidation.validationError && <p className="transfer-stock-alert" role="alert">{transferValidation.validationError}</p>}
              <button disabled={!transferValidation.isValid} type="submit">{isSaving ? "Transferring…" : "Post transfer"}</button>
            </form>
          </article>

          <article className="inventory-card">
            <h3>Stock adjustment</h3>
            <form className="inventory-form" onSubmit={postAdjustment}>
              <label>Variant<select required value={adjustVariantId} onChange={(e) => setAdjustVariantId(e.target.value)}>
                <option value="">Choose variant</option>
                {data.variants.filter((v) => v.isActive).map((v) => <option key={v.variantId} value={v.variantId}>{v.sku} · {v.productName}</option>)}
              </select></label>
              <label>Warehouse<select required value={adjustWarehouseId} onChange={(e) => { setAdjustWarehouseId(e.target.value); setAdjustBinId(""); }}>
                <option value="">Choose warehouse</option>
                {data.warehouses.filter((w) => w.isActive).map((w) => <option key={w.warehouseId} value={w.warehouseId}>{w.code} · {w.name}</option>)}
              </select></label>
              <label>Bin<select required value={adjustBinId} onChange={(e) => setAdjustBinId(e.target.value)}>
                <option value="">Choose bin</option>
                {binsForAdjust.map((b) => <option key={b.binId} value={b.binId}>{b.code} · {b.binType}</option>)}
              </select></label>
              <label>Delta (+/-)<input step="0.000000001" required type="number" value={adjustQuantityDelta} onChange={(e) => setAdjustQuantityDelta(e.target.value)} placeholder="e.g. +5 or -2" /></label>
              <label>Reason<select value={adjustReasonCode} onChange={(e) => setAdjustReasonCode(e.target.value)}>
                <option value="cycle_count">Cycle count discrepancy</option>
                <option value="damaged_goods">Damaged write-off</option>
                <option value="correction">Data correction</option>
              </select></label>
              <button disabled={!enabled || isSaving || !adjustVariantId || !adjustWarehouseId || !adjustBinId || !adjustQuantityDelta} type="submit">Post adjustment</button>
            </form>
          </article>
        </div>
      )}

      {/* ── TAB 2: RESERVATIONS & ATP ── */}
      {activeTab === "reservations" && (
        <div className="inventory-grid">
          <article className="inventory-card inventory-receipt-card">
            <h3>Reserve stock (Against confirmed sales demand)</h3>
            <form className="inventory-form" onSubmit={postReservation}>
              <label>Variant<select required value={resVariantId} onChange={(e) => setResVariantId(e.target.value)}>
                <option value="">Choose variant</option>
                {data.variants.filter((v) => v.isActive).map((v) => <option key={v.variantId} value={v.variantId}>{v.sku} · {v.productName}</option>)}
              </select></label>
              <label>Warehouse<select required value={resWarehouseId} onChange={(e) => { setResWarehouseId(e.target.value); setResBinId(""); }}>
                <option value="">Choose warehouse</option>
                {data.warehouses.filter((w) => w.isActive).map((w) => <option key={w.warehouseId} value={w.warehouseId}>{w.code} · {w.name}</option>)}
              </select></label>
              <label>Bin (Optional)<select value={resBinId} onChange={(e) => setResBinId(e.target.value)}>
                <option value="">Any storage bin</option>
                {binsForRes.map((b) => <option key={b.binId} value={b.binId}>{b.code} · {b.binType}</option>)}
              </select></label>
              <label>
                Quantity
                <input min="0.000000001" step="0.000000001" required type="number" value={resQuantity} onChange={(e) => setResQuantity(e.target.value)} />
                <span className="transfer-stock-hint">Available-To-Promise (ATP): <strong>{currentAtpQty}</strong></span>
              </label>
              <label>Source Document<select value={resSourceType} onChange={(e) => setResSourceType(e.target.value)}>
                <option value="sales_order">Sales Order</option>
                <option value="pos_hold">POS Order Hold</option>
              </select></label>
              <label>Note<input maxLength={2000} value={resNote} onChange={(e) => setResNote(e.target.value)} placeholder="Customer/Order reference" /></label>
              {resValidation.validationError && <p className="transfer-stock-alert" role="alert">{resValidation.validationError}</p>}
              <button disabled={!resValidation.isValid} type="submit">{isSaving ? "Reserving…" : "Reserve stock"}</button>
            </form>
          </article>

          <article className="inventory-card inventory-receipt-card">
            <h3>Available-To-Promise (ATP) Summary</h3>
            {atpList.length === 0 ? <p className="inventory-empty">No stock records found for ATP.</p> : (
              <div className="table-responsive">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>SKU</th>
                      <th>Location</th>
                      <th>Physical On-Hand</th>
                      <th>Active Reserved</th>
                      <th>Available-To-Promise</th>
                      <th>Valued Stock</th>
                      <th>Sellable ATP</th>
                    </tr>
                  </thead>
                  <tbody>
                    {atpList.map((item, idx) => (
                      <tr key={idx}>
                        <td><strong>{item.sku}</strong></td>
                        <td>{item.warehouseCode} / {item.binCode ?? "All Bins"}</td>
                        <td>{item.physicalOnHand}</td>
                        <td>{item.activeReservedQty}</td>
                        <td><span className="atp-pill">{item.availableToPromiseQty}</span></td>
                        <td>{item.valuedQty}</td>
                        <td><strong style={{ color: item.sellableAtpQty > 0 ? "#16a34a" : "#dc2626" }}>{item.sellableAtpQty}</strong></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </article>

          <article className="inventory-card inventory-receipt-card">
            <h3>Active Stock Reservations</h3>
            {reservations.length === 0 ? <p className="inventory-empty">No active reservations.</p> : (
              <div className="table-responsive">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>SKU</th>
                      <th>Warehouse</th>
                      <th>Reserved Qty</th>
                      <th>Fulfilled Qty</th>
                      <th>Remaining</th>
                      <th>Status</th>
                      <th>Created</th>
                      <th>Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {reservations.map((r) => (
                      <tr key={r.reservationId}>
                        <td><strong>{r.sku}</strong></td>
                        <td>{r.warehouseCode}</td>
                        <td>{r.quantity}</td>
                        <td>{r.fulfilledQty}</td>
                        <td>{r.remainingReservedQty}</td>
                        <td><span className="receipt-status receipt-status-accepted">{r.reservationStatus}</span></td>
                        <td>{new Date(r.createdAt).toLocaleDateString()}</td>
                        <td>
                          {r.reservationStatus === "active" && (
                            <button type="button" className="receipt-action-btn" onClick={() => void releaseReservation(r.reservationId)}>Release</button>
                          )}
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

      {/* ── TAB 3: PICKING & DISPATCH ── */}
      {activeTab === "picking" && (
        <div className="inventory-grid">
          <article className="inventory-card inventory-receipt-card">
            <h3>Generate Pick List</h3>
            <form className="inventory-form" onSubmit={postPickList}>
              <label>Warehouse<select required value={pickWarehouseId} onChange={(e) => { setPickWarehouseId(e.target.value); setPickBinId(""); }}>
                <option value="">Choose warehouse</option>
                {data.warehouses.filter((w) => w.isActive).map((w) => <option key={w.warehouseId} value={w.warehouseId}>{w.code} · {w.name}</option>)}
              </select></label>
              <label>Variant<select required value={pickVariantId} onChange={(e) => setPickVariantId(e.target.value)}>
                <option value="">Choose variant</option>
                {data.variants.filter((v) => v.isActive).map((v) => <option key={v.variantId} value={v.variantId}>{v.sku} · {v.productName}</option>)}
              </select></label>
              <label>Bin<select required value={pickBinId} onChange={(e) => setPickBinId(e.target.value)}>
                <option value="">Choose bin</option>
                {binsForPick.map((b) => <option key={b.binId} value={b.binId}>{b.code} · {b.binType}</option>)}
              </select></label>
              <label>Requested Qty<input min="0.000000001" step="0.000000001" required type="number" value={pickReqQty} onChange={(e) => setPickReqQty(e.target.value)} /></label>
              <button disabled={!enabled || isSaving || !pickWarehouseId || !pickVariantId || !pickBinId || !pickReqQty} type="submit">Create Pick List</button>
            </form>
          </article>

          <article className="inventory-card inventory-receipt-card">
            <h3>Pick Lists & Dispatch</h3>
            {pickLists.length === 0 ? <p className="inventory-empty">No pick lists created yet.</p> : (
              <div className="table-responsive">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Pick List #</th>
                      <th>Warehouse</th>
                      <th>Lines</th>
                      <th>Status</th>
                      <th>Created</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {pickLists.map((p) => (
                      <tr key={p.pickListId}>
                        <td><strong>{p.pickListNo}</strong></td>
                        <td>{p.warehouseCode}</td>
                        <td>
                          {p.lines.map((l) => (
                            <div key={l.pickLineId} style={{ marginBottom: 4 }}>
                              {l.sku} in {l.binCode}: Req: {l.requestedQty}, Picked: {l.pickedQty} ({l.status})
                              {p.status !== "dispatched" && (
                                <input
                                  type="number"
                                  min="0"
                                  step="0.000000001"
                                  style={{ width: 80, marginLeft: 8 }}
                                  placeholder={String(l.requestedQty)}
                                  onChange={(e) => setPickRecordedMap((prev) => ({ ...prev, [l.pickLineId]: e.target.value }))}
                                />
                              )}
                            </div>
                          ))}
                        </td>
                        <td><span className={`receipt-status ${p.status === "dispatched" ? "receipt-status-accepted" : ""}`}>{p.status}</span></td>
                        <td>{new Date(p.createdAt).toLocaleDateString()}</td>
                        <td>
                          {p.status !== "dispatched" && (
                            <div style={{ display: "flex", gap: 6 }}>
                              <button type="button" className="receipt-action-btn" onClick={() => void recordPickLines(p.pickListId, p.lines)}>Save Picks</button>
                              <button type="button" className="receipt-action-btn receipt-retry-btn" onClick={() => void dispatchPickList(p.pickListId)}>Dispatch (FIFO)</button>
                            </div>
                          )}
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

      {/* ── TAB 4: CYCLE COUNTS ── */}
      {activeTab === "counts" && (
        <div className="inventory-grid">
          <article className="inventory-card inventory-receipt-card">
            <h3>Initiate Stock Count Session</h3>
            <form className="inventory-form" onSubmit={postCountSession}>
              <label>Warehouse<select required value={countWarehouseId} onChange={(e) => setCountWarehouseId(e.target.value)}>
                <option value="">Choose warehouse</option>
                {data.warehouses.filter((w) => w.isActive).map((w) => <option key={w.warehouseId} value={w.warehouseId}>{w.code} · {w.name}</option>)}
              </select></label>
              <label>Count Type<select value={countType} onChange={(e) => setCountType(e.target.value)}>
                <option value="cycle">Cycle Count</option>
                <option value="full">Full Physical Count</option>
                <option value="spot">Spot Check</option>
              </select></label>
              <button disabled={!enabled || isSaving || !countWarehouseId} type="submit">Start Count Session</button>
            </form>
          </article>

          <article className="inventory-card inventory-receipt-card">
            <h3>Physical Count Sessions</h3>
            {countSessions.length === 0 ? <p className="inventory-empty">No count sessions active.</p> : (
              <div className="table-responsive">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Count #</th>
                      <th>Type</th>
                      <th>Status</th>
                      <th>Lines & Variances</th>
                      <th>Created</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {countSessions.map((s) => (
                      <tr key={s.sessionId}>
                        <td><strong>{s.countNumber}</strong></td>
                        <td>{s.countType}</td>
                        <td><span className={`receipt-status ${s.status === "approved" ? "receipt-status-accepted" : ""}`}>{s.status}</span></td>
                        <td>
                          {s.lines.map((l) => {
                            const countedStr = countInputMap[l.lineId] ?? (l.countedQty !== null ? String(l.countedQty) : "");
                            const countedNum = Number(countedStr);
                            const variance = countedStr !== "" && !Number.isNaN(countedNum) ? countedNum - l.snapshotQty : (l.varianceQty ?? 0);
                            return (
                              <div key={l.lineId} style={{ marginBottom: 4 }}>
                                <strong>{l.sku}</strong> ({l.binCode}) · Snapshot: {l.snapshotQty} ·
                                {s.status !== "approved" ? (
                                  <>
                                    {" "}Counted: <input
                                      type="number"
                                      min="0"
                                      step="0.000000001"
                                      style={{ width: 80, marginLeft: 4 }}
                                      value={countedStr}
                                      onChange={(e) => setCountInputMap((prev) => ({ ...prev, [l.lineId]: e.target.value }))}
                                    />
                                    {" "}Variance: <strong style={{ color: variance >= 0 ? "#16a34a" : "#dc2626" }}>{variance >= 0 ? `+${variance}` : variance}</strong>
                                  </>
                                ) : (
                                  <> Counted: {l.countedQty} · Variance: {l.varianceQty}</>
                                )}
                              </div>
                            );
                          })}
                        </td>
                        <td>{new Date(s.createdAt).toLocaleDateString()}</td>
                        <td>
                          {s.status !== "approved" && (
                            <div style={{ display: "flex", gap: 6 }}>
                              <button type="button" className="receipt-action-btn" onClick={() => void recordCountLines(s.sessionId, s.lines)}>Save Counts</button>
                              <button type="button" className="receipt-action-btn receipt-retry-btn" onClick={() => void approveCountSession(s.sessionId)}>Approve & Reconcile</button>
                            </div>
                          )}
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

      {/* ── TAB 5: RETURNS ── */}
      {activeTab === "returns" && (
        <div className="inventory-grid">
          <article className="inventory-card inventory-receipt-card">
            <h3>Initiate Stock Return (Customer or Supplier)</h3>
            <form className="inventory-form" onSubmit={postReturn}>
              <label>Return Type<select value={returnType} onChange={(e) => setReturnType(e.target.value)}>
                <option value="customer_return">Customer Return (Receipt)</option>
                <option value="supplier_return">Supplier Return (Issue)</option>
              </select></label>
              <label>Warehouse<select required value={returnWarehouseId} onChange={(e) => { setReturnWarehouseId(e.target.value); setReturnBinId(""); }}>
                <option value="">Choose warehouse</option>
                {data.warehouses.filter((w) => w.isActive).map((w) => <option key={w.warehouseId} value={w.warehouseId}>{w.code} · {w.name}</option>)}
              </select></label>
              <label>Variant<select required value={returnVariantId} onChange={(e) => setReturnVariantId(e.target.value)}>
                <option value="">Choose variant</option>
                {data.variants.filter((v) => v.isActive).map((v) => <option key={v.variantId} value={v.variantId}>{v.sku} · {v.productName}</option>)}
              </select></label>
              <label>Bin<select required value={returnBinId} onChange={(e) => setReturnBinId(e.target.value)}>
                <option value="">Choose bin</option>
                {binsForReturn.map((b) => <option key={b.binId} value={b.binId}>{b.code} · {b.binType}</option>)}
              </select></label>
              <label>Quantity<input min="0.000000001" step="0.000000001" required type="number" value={returnQty} onChange={(e) => setReturnQty(e.target.value)} /></label>
              <label>Unit Cost (PKR)<input min="0" step="0.01" required type="number" value={returnCost} onChange={(e) => setReturnCost(e.target.value)} /></label>
              <label>Disposition<select value={returnDisposition} onChange={(e) => setReturnDisposition(e.target.value)}>
                <option value="available">Available (Restore to sellable)</option>
                <option value="quarantine">Quarantine (Inspection hold)</option>
                <option value="scrap">Scrap (Non-commercial write-off)</option>
              </select></label>
              <button disabled={!enabled || isSaving || !returnWarehouseId || !returnVariantId || !returnBinId || !returnQty} type="submit">Create Return</button>
            </form>
          </article>

          <article className="inventory-card inventory-receipt-card">
            <h3>Return Authorizations</h3>
            {stockReturns.length === 0 ? <p className="inventory-empty">No return authorizations recorded.</p> : (
              <div className="table-responsive">
                <table className="erp-table">
                  <thead>
                    <tr>
                      <th>Return #</th>
                      <th>Type</th>
                      <th>Warehouse</th>
                      <th>Lines</th>
                      <th>Status</th>
                      <th>Created</th>
                      <th>Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {stockReturns.map((r) => (
                      <tr key={r.returnId}>
                        <td><strong>{r.returnNumber}</strong></td>
                        <td>{r.returnType}</td>
                        <td>{r.warehouseCode}</td>
                        <td>
                          {r.lines.map((l) => (
                            <div key={l.lineId}>
                              {l.sku} ({l.binCode}) · {l.quantity} @ PKR {l.unitCost} [{l.disposition}]
                            </div>
                          ))}
                        </td>
                        <td><span className={`receipt-status ${r.status === "processed" ? "receipt-status-accepted" : ""}`}>{r.status}</span></td>
                        <td>{new Date(r.createdAt).toLocaleDateString()}</td>
                        <td>
                          {r.status !== "processed" && (
                            <button type="button" className="receipt-action-btn receipt-retry-btn" onClick={() => void processReturn(r.returnId)}>Process & Post</button>
                          )}
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

      {/* ── TAB 6: VALUATION & GL AUDIT ── */}
      {activeTab === "valuation" && (
        <div className="inventory-grid">
          {valuationReport ? (
            <>
              <article className="inventory-card inventory-receipt-card">
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                  <h3>Real-Time Inventory Valuation & GL Reconciliation Audit</h3>
                  <span className={valuationReport.isReconciled ? "badge-reconciled" : "badge-unreconciled"}>
                    {valuationReport.isReconciled ? "RECONCILED (ZERO VARIANCE)" : "VARIANCE DETECTED"}
                  </span>
                </div>
                <div className="atp-grid">
                  <div className="atp-card">
                    <h4>Active FIFO Layers</h4>
                    <div className="atp-metric-val">{valuationReport.totalActiveLayers} layers</div>
                  </div>
                  <div className="atp-card">
                    <h4>Physical Stock</h4>
                    <div className="atp-metric-val">{valuationReport.totalPhysicalStock} EA</div>
                  </div>
                  <div className="atp-card">
                    <h4>Valued Stock</h4>
                    <div className="atp-metric-val">{valuationReport.totalValuedStock} EA</div>
                  </div>
                  <div className="atp-card">
                    <h4>Unvalued Stock</h4>
                    <div className="atp-metric-val" style={{ color: valuationReport.totalUnvaluedStock > 0 ? "#dc2626" : "#16a34a" }}>
                      {valuationReport.totalUnvaluedStock} EA
                    </div>
                  </div>
                  <div className="atp-card">
                    <h4>FIFO Valuation</h4>
                    <div className="atp-metric-val">PKR {valuationReport.totalValuationPkr.toLocaleString(undefined, { minimumFractionDigits: 2 })}</div>
                  </div>
                  <div className="atp-card">
                    <h4>GL Account 1200</h4>
                    <div className="atp-metric-val">PKR {valuationReport.generalLedger1200BalancePkr.toLocaleString(undefined, { minimumFractionDigits: 2 })}</div>
                  </div>
                  <div className="atp-card">
                    <h4>Reconciliation Variance</h4>
                    <div className="atp-metric-val" style={{ color: valuationReport.reconciliationVariancePkr === 0 ? "#16a34a" : "#dc2626" }}>
                      PKR {valuationReport.reconciliationVariancePkr.toLocaleString(undefined, { minimumFractionDigits: 2 })}
                    </div>
                  </div>
                </div>
              </article>

              <article className="inventory-card inventory-receipt-card">
                <h3>Active FIFO Valuation Layers</h3>
                <div className="table-responsive">
                  <table className="erp-table">
                    <thead>
                      <tr>
                        <th>Layer ID</th>
                        <th>SKU</th>
                        <th>Location</th>
                        <th>Source</th>
                        <th>Received At</th>
                        <th>Original Qty</th>
                        <th>Remaining Qty</th>
                        <th>Unit Cost</th>
                        <th>Extended Valuation</th>
                      </tr>
                    </thead>
                    <tbody>
                      {valuationReport.layers.map((l) => (
                        <tr key={l.layerId}>
                          <td><code>{l.layerId.substring(0, 8)}…</code></td>
                          <td><strong>{l.sku}</strong></td>
                          <td>{l.warehouseCode} / {l.binCode}</td>
                          <td>{l.sourceDocumentType}</td>
                          <td>{new Date(l.receivedAt).toLocaleString()}</td>
                          <td>{l.originalQty}</td>
                          <td><strong>{l.remainingQty}</strong></td>
                          <td>PKR {l.unitCost.toFixed(2)}</td>
                          <td><strong>PKR {l.extendedValuation.toFixed(2)}</strong></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </article>
            </>
          ) : (
            <p className="inventory-empty">Valuation report loading or unavailable.</p>
          )}
        </div>
      )}

      {/* On-hand stock summary always visible at bottom */}
      <article className="inventory-card availability-card">
        <h3>On-hand stock overview</h3>
        {data.balances.length === 0 ? <p className="inventory-empty">No stock balances posted yet.</p> : (
          <div className="availability-list">
            {data.balances.map((balance, index) => {
              const variant = variantsById.get(balance.variantId);
              const warehouse = warehousesById.get(balance.warehouseId);
              const bin = binsById.get(balance.binId);
              return <div className="availability-row" key={`${balance.variantId}:${balance.binId}:${balance.stockStatus}:${index}`}>
                <strong>{variant?.sku ?? balance.variantId}</strong>
                <span>{warehouse?.code ?? "Warehouse"} / {bin?.code ?? "Bin"}</span>
                <span>{balance.stockStatus.replace("_", " ")}</span>
                <b>{balance.onHandQuantity} {unitsById.get(balance.unitOfMeasureId) ?? "units"}</b>
              </div>;
            })}
          </div>
        )}
      </article>
    </section>
  );
}
