import { useEffect, useRef, useState, type FormEvent } from "react";
import { apiFetch, type ApiWorkspaceScope } from "./auth";
import {
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
type Balance = { variantId: string; warehouseId: string; binId: string; stockStatus: string; onHandQuantity: number; unitOfMeasureId: string };
type Data = { products: Product[]; units: Unit[]; variants: Variant[]; warehouses: Warehouse[]; bins: Bin[]; balances: Balance[] };
type ChangeFeedPage = { nextCursor: string; hasMore: boolean; changes: unknown[] };

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
  const [data, setData] = useState<Data>(emptyData);
  const [isUsingCachedData, setIsUsingCachedData] = useState(false);
  const [receiptOperations, setReceiptOperations] = useState<OfflineReceiptOperation[]>([]);
  const [reloadVersion, setReloadVersion] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [message, setMessage] = useState("");
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
  const [receiptVariantId, setReceiptVariantId] = useState("");
  const [receiptWarehouseId, setReceiptWarehouseId] = useState("");
  const [receiptBinId, setReceiptBinId] = useState("");
  const [receiptQuantity, setReceiptQuantity] = useState("");
  const [receiptNote, setReceiptNote] = useState("");

  const [transferVariantId, setTransferVariantId] = useState("");
  const [transferSourceWarehouseId, setTransferSourceWarehouseId] = useState("");
  const [transferSourceBinId, setTransferSourceBinId] = useState("");
  const [transferDestWarehouseId, setTransferDestWarehouseId] = useState("");
  const [transferDestBinId, setTransferDestBinId] = useState("");
  const [transferQuantity, setTransferQuantity] = useState("");
  const [transferNote, setTransferNote] = useState("");

  const [adjustVariantId, setAdjustVariantId] = useState("");
  const [adjustWarehouseId, setAdjustWarehouseId] = useState("");
  const [adjustBinId, setAdjustBinId] = useState("");
  const [adjustQuantityDelta, setAdjustQuantityDelta] = useState("");
  const [adjustReasonCode, setAdjustReasonCode] = useState("cycle_count");
  const [adjustNote, setAdjustNote] = useState("");
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
          // The live snapshot can still be shown if local cursor storage is unavailable.
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
      try {
        await saveInventorySnapshotAndCursors(storageScope, snapshot, cursors);
      } catch {
        snapshotSaved = false;
      }
      if (!mounted) return;

      setProductId((current) => products.some((item) => item.productId === current && item.isActive && item.productType === "stock")
        ? current
        : products.find((item) => item.isActive && item.productType === "stock")?.productId ?? "");
      setUnitId((current) => units.some((item) => item.unitOfMeasureId === current)
        ? current
        : units[0]?.unitOfMeasureId ?? "");
      setWarehouseId((current) => warehouses.some((item) => item.warehouseId === current && item.isActive)
        ? current
        : warehouses.find((item) => item.isActive)?.warehouseId ?? "");
      setReceiptVariantId((current) => variants.some((item) => item.variantId === current && item.isActive)
        ? current
        : variants.find((item) => item.isActive)?.variantId ?? "");
      setReceiptWarehouseId((current) => warehouses.some((item) => item.warehouseId === current && item.isActive)
        ? current
        : warehouses.find((item) => item.isActive)?.warehouseId ?? "");
      setMessage(!snapshotSaved
        ? "Inventory loaded, but this browser could not save the encrypted snapshot and change cursors."
        : !feedCaughtUp
          ? "Inventory loaded. The change feed could not be checked; it will retry without advancing its saved cursor."
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
          setMessage(error instanceof Error ? error.message : "Inventory information could not be loaded.");
        }
      }).catch(() => {
        if (mounted) setMessage(error instanceof Error ? error.message : "Inventory information could not be loaded.");
      });
    }).finally(() => {
      if (mounted) setIsLoading(false);
    });
    return () => { mounted = false; };
  }, [enabled, storageScope, workspace?.tenantId, workspace?.companyId, reloadVersion]);

  useEffect(() => {
    if (!enabled) return;
    let mounted = true;
    let checking = false;
    const checkChanges = async () => {
      if (checking) return;
      checking = true;
      try {
        const cursors = await loadInventoryChangeCursors(storageScope);
        const [products, inventory] = await Promise.all([
          readChangePage("products", cursors.products, workspace),
          readChangePage("inventory", cursors.inventory, workspace),
        ]);
        if (mounted && (products.changes.length > 0 || products.hasMore || inventory.changes.length > 0 || inventory.hasMore)) {
          setReloadVersion((value) => value + 1);
        }
      } catch {
        // A failed feed read does not move the durable cursor; the next poll retries it.
      } finally {
        checking = false;
      }
    };

    const intervalId = window.setInterval(() => void checkChanges(), 15000);
    return () => {
      mounted = false;
      window.clearInterval(intervalId);
    };
  }, [enabled, storageScope, workspace?.tenantId, workspace?.companyId]);

  useEffect(() => {
    let mounted = true;
    let syncing = false;
    const refreshQueue = async (shouldSync: boolean) => {
      if (syncing) return;
      syncing = true;
      try {
        const before = await loadReceiptOperations(storageScope);
        if (!mounted) return;
        setReceiptOperations(before);
        if (!shouldSync) return;
        const after = await syncPendingReceiptOperations(storageScope, workspace);
        if (!mounted) return;
        setReceiptOperations(after);
        const confirmedNewReceipt = after.some((operation) => operation.status === "accepted" &&
          !before.some((previous) => previous.operationId === operation.operationId && previous.status === "accepted"));
        if (confirmedNewReceipt) setReloadVersion((value) => value + 1);
      } catch {
        if (mounted) setMessage("The encrypted receipt journal could not be opened or synced. Saved operations remain on this device.");
      } finally {
        syncing = false;
      }
    };

    void refreshQueue(enabled);
    if (!enabled) return () => { mounted = false; };
    const intervalId = window.setInterval(() => void refreshQueue(true), 15000);
    const handleOnline = () => void refreshQueue(true);
    window.addEventListener("online", handleOnline);
    return () => {
      mounted = false;
      window.clearInterval(intervalId);
      window.removeEventListener("online", handleOnline);
    };
  }, [enabled, storageScope, workspace?.tenantId, workspace?.companyId]);

  useEffect(() => {
    const availableBins = data.bins.filter((bin) => bin.warehouseId === receiptWarehouseId && bin.isActive && ["storage", "receiving"].includes(bin.binType));
    if (!availableBins.some((bin) => bin.binId === receiptBinId)) setReceiptBinId(availableBins[0]?.binId ?? "");
  }, [data.bins, receiptWarehouseId, receiptBinId]);

  const submitMaster = async (event: FormEvent<HTMLFormElement>, path: string, body: object, successMessage: string) => {
    event.preventDefault();
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse<unknown>(path, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      }, workspace);
      setMessage(successMessage);
      setReloadVersion((value) => value + 1);
      return true;
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "The change could not be saved.");
      return false;
    } finally {
      setIsSaving(false);
    }
  };

  const postReceipt = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const variant = data.variants.find((item) => item.variantId === receiptVariantId);
    if (!variant || !receiptWarehouseId || !receiptBinId || Number(receiptQuantity) <= 0) {
      setMessage("Choose a supported stock item, warehouse, bin, and positive quantity.");
      return;
    }
    const body = {
      variantId: receiptVariantId,
      warehouseId: receiptWarehouseId,
      binId: receiptBinId,
      quantity: Number(receiptQuantity),
      unitOfMeasureId: variant.baseUnitOfMeasureId,
      sourceDocumentType: null,
      sourceDocumentId: null,
      note: receiptNote.trim() || null,
    };
    if (!canQueue) {
      setMessage("Sign in and select a company before saving an offline receipt.");
      return;
    }
    setIsSaving(true);
    setMessage("");
    try {
      const queued = await enqueueReceiptOperation(body, queueScope ?? {}, storageScope);
      setReceiptOperations(await loadReceiptOperations(storageScope));
      setReceiptQuantity("");
      setReceiptNote("");
      setMessage(`Receipt saved securely on this device (item ${queued.deviceSequence}); waiting for server confirmation.`);
      if (enabled) {
        const operations = await syncPendingReceiptOperations(storageScope, workspace);
        setReceiptOperations(operations);
        const savedOperation = operations.find((operation) => operation.operationId === queued.operationId);
        if (savedOperation?.status === "accepted") {
          setMessage("Receipt confirmed by the server. Its stock movement and balance were saved together; the local journal copy is retained.");
          setReloadVersion((value) => value + 1);
        } else if (savedOperation?.status === "conflict") {
          setMessage("The server found a conflict. Your receipt remains saved locally for review.");
        } else if (savedOperation?.status === "rejected") {
          setMessage(`The server rejected this receipt: ${savedOperation.message ?? "review the saved details"}. The local record is retained.`);
        } else {
          setMessage("Receipt is saved on this device and will retry with the same ID when the server is available.");
        }
      }
    } catch (error) {
      setMessage(error instanceof Error
        ? `Could not save the receipt to encrypted browser storage, so it was not submitted. ${error.message}`
        : "Could not save the receipt to encrypted browser storage, so it was not submitted.");
    } finally {
      setIsSaving(false);
    }
  };

  const copyReceiptToForm = (operation: OfflineReceiptOperation) => {
    setReceiptVariantId(operation.payload.variantId);
    setReceiptWarehouseId(operation.payload.warehouseId);
    setReceiptBinId(operation.payload.binId);
    setReceiptQuantity(operation.payload.quantity.toString());
    setReceiptNote(operation.payload.note ?? "");
    setMessage(`Receipt details copied to form from entry #${operation.deviceSequence}. Review and save.`);
  };

  const retryUnresolvedReceipt = async (operationId: string) => {
    try {
      setIsSaving(true);
      await requeueReceiptOperation(operationId, storageScope);
      const updated = await syncPendingReceiptOperations(storageScope, workspace);
      setReceiptOperations(updated);
      setMessage("Synchronization retried for unresolved receipt.");
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Retry failed.");
    } finally {
      setIsSaving(false);
    }
  };

  const postTransfer = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const variant = data.variants.find((item) => item.variantId === transferVariantId);
    if (!variant || !transferSourceWarehouseId || !transferSourceBinId ||
        !transferDestWarehouseId || !transferDestBinId || Number(transferQuantity) <= 0) {
      setMessage("Choose variant, source/destination warehouse and bins, and a positive quantity.");
      return;
    }
    if (transferSourceBinId === transferDestBinId) {
      setMessage("Source and destination bins must differ.");
      return;
    }
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse<unknown>("/api/v1/inventory/transfers", {
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
          unitOfMeasureId: variant.baseUnitOfMeasureId,
          note: transferNote.trim() || null,
        }),
      }, workspace);
      setTransferQuantity("");
      setTransferNote("");
      setMessage("Stock transfer posted successfully.");
      setReloadVersion((v) => v + 1);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Transfer failed.");
    } finally {
      setIsSaving(false);
    }
  };

  const postAdjustment = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const variant = data.variants.find((item) => item.variantId === adjustVariantId);
    const delta = Number(adjustQuantityDelta);
    if (!variant || !adjustWarehouseId || !adjustBinId || delta === 0) {
      setMessage("Choose variant, warehouse, bin, and a non-zero adjustment delta.");
      return;
    }
    setIsSaving(true);
    setMessage("");
    try {
      await readResponse<unknown>("/api/v1/inventory/adjustments", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": crypto.randomUUID(),
        },
        body: JSON.stringify({
          variantId: adjustVariantId,
          warehouseId: adjustWarehouseId,
          binId: adjustBinId,
          quantityDelta: delta,
          unitOfMeasureId: variant.baseUnitOfMeasureId,
          reasonCode: adjustReasonCode,
          note: adjustNote.trim() || null,
        }),
      }, workspace);
      setAdjustQuantityDelta("");
      setAdjustNote("");
      setMessage(`Stock adjustment of ${delta > 0 ? "+" : ""}${delta} posted successfully.`);
      setReloadVersion((v) => v + 1);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Adjustment failed.");
    } finally {
      setIsSaving(false);
    }
  };

  const binsForSelectedWarehouse = data.bins.filter((bin) => bin.warehouseId === receiptWarehouseId && bin.isActive && ["storage", "receiving"].includes(bin.binType));
  const binsForTransferSource = data.bins.filter((bin) => bin.warehouseId === transferSourceWarehouseId && bin.isActive);
  const binsForTransferDest = data.bins.filter((bin) => bin.warehouseId === transferDestWarehouseId && bin.isActive);
  const binsForAdjust = data.bins.filter((bin) => bin.warehouseId === adjustWarehouseId && bin.isActive);
  const unitsById = new Map(data.units.map((unit) => [unit.unitOfMeasureId, unit.code]));
  const variantsById = new Map(data.variants.map((variant) => [variant.variantId, variant]));
  const warehousesById = new Map(data.warehouses.map((warehouse) => [warehouse.warehouseId, warehouse]));
  const binsById = new Map(data.bins.map((bin) => [bin.binId, bin]));
  const unresolvedReceipts = receiptOperations.filter((operation) => operation.status !== "accepted");
  const confirmedReceipts = receiptOperations.filter((operation) => operation.status === "accepted");

  return (
    <section className="inventory-workspace" aria-labelledby="inventory-workflow-title">
      <div className="catalog-heading">
        <div>
          <p className="eyebrow">INVENTORY WORKFLOW</p>
          <h2 id="inventory-workflow-title">Stock setup and receiving</h2>
        </div>
        <button className="secondary-button" disabled={!enabled || isLoading} onClick={() => setReloadVersion((value) => value + 1)} type="button">
          {isLoading ? "Loading…" : "Refresh"}
        </button>
      </div>
      <p className="catalog-note">
        Create stock variants and storage locations. Receipts are saved in an encrypted local journal first, then sent with the same ID until the server confirms them.
      </p>
      {!enabled && <p className="catalog-storage-note">API is offline or sign-in is unavailable. Setup changes are paused; receipts can be queued against the last saved inventory snapshot.</p>}
      {isUsingCachedData && <p className="catalog-storage-note" role="status">Showing the last saved inventory snapshot. Stock balances may have changed while this device was offline.</p>}
      {persistentStorageGranted === false && <p className="catalog-storage-note" role="status">This browser did not grant persistent storage. Keep the device available and reconnect so queued receipts reach the server.</p>}
      {message && <p className="catalog-message" role="status">{message}</p>}

      <div className="inventory-grid">
        <article className="inventory-card">
          <h3>Units and stock variants</h3>
          <form className="inventory-form" onSubmit={(event) => {
            void submitMaster(event, "/api/v1/inventory/units-of-measure", {
              code: unitCode, name: unitName, dimension: unitDimension, decimalPlaces: Number(unitDecimalPlaces),
            }, "Unit of measure created.").then((saved) => {
              if (saved) { setUnitCode(""); setUnitName(""); }
            });
          }}>
            <label>Unit code<input maxLength={64} required value={unitCode} onChange={(event) => setUnitCode(event.target.value)} placeholder="e.g. KG" /></label>
            <label>Unit name<input maxLength={100} required value={unitName} onChange={(event) => setUnitName(event.target.value)} placeholder="Kilogram" /></label>
            <label>Dimension<select value={unitDimension} onChange={(event) => setUnitDimension(event.target.value)}><option value="count">Count</option><option value="length">Length</option><option value="mass">Mass</option><option value="volume">Volume</option><option value="time">Time</option><option value="area">Area</option><option value="other">Other</option></select></label>
            <label>Decimal places<input max="9" min="0" required type="number" value={unitDecimalPlaces} onChange={(event) => setUnitDecimalPlaces(event.target.value)} /></label>
            <button disabled={!enabled || isSaving} type="submit">Create unit</button>
          </form>
          <div className="inventory-form-divider">
            <h4 className="inventory-subheading">New stock variant</h4>
          <form className="inventory-form" onSubmit={(event) => void submitMaster(event, `/api/v1/products/${productId}/variants`, {
            productId, sku, variantName: null, baseUnitOfMeasureId: unitId, trackingMode: "none", costingMethod: "fifo",
          }, "Stock variant created.")}>
            <label>Stock product<select required value={productId} onChange={(event) => setProductId(event.target.value)}>
              <option value="">Choose a product</option>
              {data.products.filter((product) => product.isActive && product.productType === "stock").map((product) => <option key={product.productId} value={product.productId}>{product.productCode} · {product.name}</option>)}
            </select></label>
            <label>SKU<input maxLength={64} required value={sku} onChange={(event) => setSku(event.target.value)} placeholder="e.g. ITEM-001-A" /></label>
            <label>Base unit<select required value={unitId} onChange={(event) => setUnitId(event.target.value)}>
              {data.units.map((unit) => <option key={unit.unitOfMeasureId} value={unit.unitOfMeasureId}>{unit.code} · {unit.name}</option>)}
            </select></label>
            <button disabled={!enabled || isSaving || !productId || !unitId} type="submit">Create variant</button>
          </form>
          </div>
          {data.variants.length === 0 && <p className="inventory-empty">No variants created yet.</p>}
          {data.variants.map((variant) => <div className="inventory-list-row" key={variant.variantId}><strong>{variant.sku}</strong><span>{variant.productName} · {variant.baseUnitCode}</span></div>)}
        </article>

        <article className="inventory-card">
          <h3>Warehouse and bin</h3>
          <form className="inventory-form" onSubmit={(event) => {
            void submitMaster(event, "/api/v1/inventory/warehouses", { code: warehouseCode, name: warehouseName }, "Warehouse created.")
              .then((saved) => { if (saved) { setWarehouseCode(""); setWarehouseName(""); } });
          }}>
            <label>Warehouse code<input maxLength={64} required value={warehouseCode} onChange={(event) => setWarehouseCode(event.target.value)} placeholder="e.g. MAIN" /></label>
            <label>Warehouse name<input maxLength={160} required value={warehouseName} onChange={(event) => setWarehouseName(event.target.value)} placeholder="Main warehouse" /></label>
            <button disabled={!enabled || isSaving} type="submit">Create warehouse</button>
          </form>
          <form className="inventory-form inventory-form-divider" onSubmit={(event) => {
            void submitMaster(event, `/api/v1/inventory/warehouses/${warehouseId}/bins`, { code: binCode, binType }, "Storage bin created.")
              .then((saved) => { if (saved) setBinCode(""); });
          }}>
            <label>Warehouse<select required value={warehouseId} onChange={(event) => setWarehouseId(event.target.value)}>
              <option value="">Choose a warehouse</option>
              {data.warehouses.filter((warehouse) => warehouse.isActive).map((warehouse) => <option key={warehouse.warehouseId} value={warehouse.warehouseId}>{warehouse.code} · {warehouse.name}</option>)}
            </select></label>
            <label>Bin code<input maxLength={64} required value={binCode} onChange={(event) => setBinCode(event.target.value)} placeholder="e.g. A-01" /></label>
            <label>Bin use<select value={binType} onChange={(event) => setBinType(event.target.value)}><option value="storage">Storage</option><option value="receiving">Receiving</option></select></label>
            <button disabled={!enabled || isSaving || !warehouseId} type="submit">Create bin</button>
          </form>
          {data.warehouses.length === 0 && <p className="inventory-empty">No warehouses created yet.</p>}
          {data.warehouses.map((warehouse) => <div className="inventory-list-row" key={warehouse.warehouseId}><strong>{warehouse.code}</strong><span>{warehouse.name} · {data.bins.filter((bin) => bin.warehouseId === warehouse.warehouseId).length} bins</span></div>)}
        </article>

        <article className="inventory-card inventory-receipt-card">
          <h3>Receive stock</h3>
          <form className="inventory-form" onSubmit={postReceipt}>
            <label>Variant<select required value={receiptVariantId} onChange={(event) => setReceiptVariantId(event.target.value)}>
              <option value="">Choose a variant</option>
              {data.variants.filter((variant) => variant.isActive && variant.trackingMode === "none").map((variant) => <option key={variant.variantId} value={variant.variantId}>{variant.sku} · {variant.productName}</option>)}
            </select></label>
            <label>Warehouse<select required value={receiptWarehouseId} onChange={(event) => setReceiptWarehouseId(event.target.value)}>
              <option value="">Choose a warehouse</option>
              {data.warehouses.filter((warehouse) => warehouse.isActive).map((warehouse) => <option key={warehouse.warehouseId} value={warehouse.warehouseId}>{warehouse.code} · {warehouse.name}</option>)}
            </select></label>
            <label>Receiving bin<select required value={receiptBinId} onChange={(event) => setReceiptBinId(event.target.value)}>
              <option value="">Choose a bin</option>
              {binsForSelectedWarehouse.map((bin) => <option key={bin.binId} value={bin.binId}>{bin.code} · {bin.binType}</option>)}
            </select></label>
            <label>Quantity<input inputMode="decimal" min="0.000000001" step="0.000000001" required type="number" value={receiptQuantity} onChange={(event) => setReceiptQuantity(event.target.value)} /></label>
            <label>Note<input maxLength={2000} value={receiptNote} onChange={(event) => setReceiptNote(event.target.value)} placeholder="Optional" /></label>
            <button disabled={!canQueue || isSaving || !receiptVariantId || !receiptWarehouseId || !receiptBinId} type="submit">{isSaving ? "Saving…" : enabled ? "Save and post receipt" : "Save receipt offline"}</button>
          </form>
          <p className="inventory-footnote">Only untracked stock variants are enabled for this first receipt workflow. Each confirmed receipt is an immutable movement; corrections need a separate adjustment workflow.</p>

          {unresolvedReceipts.length > 0 && (
            <div className="receipt-unresolved-card" aria-label="Unresolved offline receipts">
              <h4>Unresolved offline receipts ({unresolvedReceipts.length})</h4>
              <p className="inventory-footnote">These offline receipts could not be fully confirmed by the server. They are preserved securely in your encrypted device journal.</p>
              {unresolvedReceipts.map((operation) => {
                const variant = variantsById.get(operation.payload.variantId);
                const warehouse = warehousesById.get(operation.payload.warehouseId);
                const bin = binsById.get(operation.payload.binId);
                return (
                  <div className="receipt-unresolved-row" key={operation.operationId}>
                    <div className="receipt-unresolved-header">
                      <strong>#{operation.deviceSequence} · {operation.payload.quantity} {unitsById.get(operation.payload.unitOfMeasureId) ?? "units"}</strong>
                      <span className={`receipt-status receipt-status-${operation.status}`}>
                        {operation.status === "conflict" ? "Conflict (Needs Review)" : operation.status === "rejected" ? "Needs Correction" : operation.status === "syncing" ? "Syncing" : "Waiting to Sync"}
                      </span>
                    </div>
                    <span className="receipt-unresolved-meta">
                      {variant ? `${variant.sku} (${variant.productName})` : "Variant"} · {warehouse ? warehouse.name : "Warehouse"} · Bin {bin ? bin.code : operation.payload.binId}
                    </span>
                    {operation.payload.note && <span className="receipt-unresolved-meta">Note: {operation.payload.note}</span>}
                    {operation.message && <small className="receipt-error-detail">{operation.message}</small>}
                    <div className="receipt-unresolved-actions">
                      <button type="button" className="receipt-action-btn" onClick={() => copyReceiptToForm(operation)}>
                        Copy to form
                      </button>
                      {(operation.status === "conflict" || operation.status === "rejected") && (
                        <button type="button" className="receipt-action-btn receipt-retry-btn" disabled={isSaving} onClick={() => retryUnresolvedReceipt(operation.operationId)}>
                          Retry sync
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}

          {confirmedReceipts.length > 0 && (
            <div className="receipt-journal" aria-label="Receipt sync journal">
              <h4>Confirmed receipts ({confirmedReceipts.length} total)</h4>
              {confirmedReceipts.slice(-8).reverse().map((operation) => (
                <div className="receipt-journal-row" key={operation.operationId}>
                  <strong>#{operation.deviceSequence} · {operation.payload.quantity} {unitsById.get(operation.payload.unitOfMeasureId) ?? "units"}</strong>
                  <span className="receipt-status receipt-status-accepted">Confirmed</span>
                  {operation.serverReceipt && <small>Movement ID: {operation.serverReceipt.movementId}</small>}
                </div>
              ))}
            </div>
          )}
        </article>

        <article className="inventory-card">
          <h3>Transfer stock</h3>
          <form className="inventory-form" onSubmit={postTransfer}>
            <label>Variant<select required value={transferVariantId} onChange={(e) => setTransferVariantId(e.target.value)}>
              <option value="">Choose a variant</option>
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
            <label>Note<input maxLength={2000} value={transferNote} onChange={(e) => setTransferNote(e.target.value)} placeholder="e.g. Relocation" /></label>
            <button disabled={!enabled || isSaving || !transferVariantId || !transferSourceBinId || !transferDestBinId} type="submit">{isSaving ? "Transferring…" : "Post transfer"}</button>
          </form>
        </article>

        <article className="inventory-card">
          <h3>Stock adjustment</h3>
          <form className="inventory-form" onSubmit={postAdjustment}>
            <label>Variant<select required value={adjustVariantId} onChange={(e) => setAdjustVariantId(e.target.value)}>
              <option value="">Choose a variant</option>
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

            <label>Adjustment delta (+/-)<input step="0.000000001" required type="number" value={adjustQuantityDelta} onChange={(e) => setAdjustQuantityDelta(e.target.value)} placeholder="e.g. +5 or -2" /></label>
            <label>Reason<select value={adjustReasonCode} onChange={(e) => setAdjustReasonCode(e.target.value)}>
              <option value="cycle_count">Cycle count discrepancy</option>
              <option value="damaged_goods">Damaged goods write-off</option>
              <option value="theft_or_loss">Theft / unexplainable loss</option>
              <option value="correction">Data correction</option>
            </select></label>
            <label>Note<input maxLength={2000} value={adjustNote} onChange={(e) => setAdjustNote(e.target.value)} placeholder="Reason details" /></label>
            <button disabled={!enabled || isSaving || !adjustVariantId || !adjustWarehouseId || !adjustBinId || !adjustQuantityDelta} type="submit">{isSaving ? "Adjusting…" : "Post adjustment"}</button>
          </form>
        </article>
      </div>

      <article className="inventory-card availability-card">
        <h3>On-hand stock</h3>
        {data.balances.length === 0 ? <p className="inventory-empty">No stock receipts have been posted yet.</p> : (
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
