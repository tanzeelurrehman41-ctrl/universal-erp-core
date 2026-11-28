import { decryptOfflineValue, encryptOfflineValue } from "./offlineReceiptQueue";
import { apiUrl } from "./auth";

export type ProductCreatePayload = {
  productCode: string;
  name: string;
  productType: string;
  description: string;
};

export type ProductSummary = {
  tenantId: string;
  productId: string;
  productCode: string;
  name: string;
  description: string | null;
  productType: string;
  isActive: boolean;
  createdAt: string;
};

export type QueueWorkspace = {
  tenantId: string;
  companyId: string;
  companyName?: string;
  ownerKey?: string;
};

export type SyncAuthorization = QueueWorkspace & {
  accessToken: string;
  ownerKey: string;
};

export type QueueStatus = "queued" | "syncing" | "accepted" | "conflict" | "rejected";

export type OfflineProductOperation = {
  operationId: string;
  deviceSequence: number;
  operationType: "product.create";
  payload: ProductCreatePayload;
  status: QueueStatus;
  createdAt: string;
  tenantId?: string;
  companyId?: string;
  companyName?: string;
  ownerKey?: string;
  serverProduct?: ProductSummary;
  message?: string;
};

type EncryptedProductOperation = {
  deviceSequence: number;
  operationId: string;
  iv: Uint8Array;
  ciphertext: ArrayBuffer;
};

type StoredCounter = { id: string; value: number };

const DATABASE_NAME = "universal-erp-offline";
const DATABASE_VERSION = 2;
const OPERATIONS_STORE = "productOperations";
const ENCRYPTED_OPERATIONS_STORE = "encryptedProductOperations";
const METADATA_STORE = "metadata";

let databasePromise: Promise<IDBDatabase> | null = null;
let migrationPromise: Promise<void> | null = null;
const syncPromises = new Map<string, Promise<OfflineProductOperation[]>>();
const updateLocks = new Map<string, Promise<void>>();

function transactionComplete(transaction: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    transaction.oncomplete = () => resolve();
    transaction.onerror = () => reject(transaction.error ?? new Error("Local operation storage failed."));
    transaction.onabort = () => reject(transaction.error ?? new Error("Local operation storage was interrupted."));
  });
}

function requestResult<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error("Could not read local operation storage."));
  });
}

function canonicalJson(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
  if (value !== null && typeof value === "object") {
    const record = value as Record<string, unknown>;
    return `{${Object.keys(record).filter((key) => record[key] !== undefined).sort()
      .map((key) => `${JSON.stringify(key)}:${canonicalJson(record[key])}`).join(",")}}`;
  }
  return JSON.stringify(value) ?? "null";
}

function openDatabase(): Promise<IDBDatabase> {
  if (databasePromise) return databasePromise;

  const opening = new Promise<IDBDatabase>((resolve, reject) => {
    let blocked = false;
    const request = indexedDB.open(DATABASE_NAME, DATABASE_VERSION);
    request.onupgradeneeded = () => {
      const database = request.result;
      if (!database.objectStoreNames.contains(OPERATIONS_STORE)) {
        const store = database.createObjectStore(OPERATIONS_STORE, {
          keyPath: "deviceSequence",
          autoIncrement: true,
        });
        store.createIndex("operationId", "operationId", { unique: true });
        store.createIndex("status", "status");
      }
      if (!database.objectStoreNames.contains(ENCRYPTED_OPERATIONS_STORE)) {
        const encrypted = database.createObjectStore(ENCRYPTED_OPERATIONS_STORE, { keyPath: "deviceSequence" });
        encrypted.createIndex("operationId", "operationId", { unique: true });
      }
      if (!database.objectStoreNames.contains(METADATA_STORE)) database.createObjectStore(METADATA_STORE, { keyPath: "id" });
    };
    request.onsuccess = () => {
      const database = request.result;
      if (blocked) {
        database.close();
        return;
      }
      database.onversionchange = () => database.close();
      resolve(database);
    };
    request.onerror = () => reject(request.error ?? new Error("Could not open local operation storage."));
    request.onblocked = () => {
      blocked = true;
      reject(new Error("Local operation storage is open in another tab. Close or refresh the other ERP tab, then retry."));
    };
  });
  databasePromise = opening.catch((error: unknown) => {
    databasePromise = null;
    throw error;
  });

  return databasePromise;
}

async function readEncryptedOperations(database: IDBDatabase): Promise<OfflineProductOperation[]> {
  const transaction = database.transaction(ENCRYPTED_OPERATIONS_STORE, "readonly");
  const complete = transactionComplete(transaction);
  const records = await requestResult(
    transaction.objectStore(ENCRYPTED_OPERATIONS_STORE).getAll() as IDBRequest<EncryptedProductOperation[]>,
  );
  await complete;
  const operations = await Promise.all(records.map((record) =>
    decryptOfflineValue<OfflineProductOperation>(record.iv, record.ciphertext),
  ));
  return operations.sort((left, right) => left.deviceSequence - right.deviceSequence);
}

async function writeEncryptedOperation(database: IDBDatabase, operation: OfflineProductOperation): Promise<void> {
  const { iv, ciphertext } = await encryptOfflineValue(operation);
  const transaction = database.transaction(ENCRYPTED_OPERATIONS_STORE, "readwrite");
  const complete = transactionComplete(transaction);
  transaction.objectStore(ENCRYPTED_OPERATIONS_STORE).put({
    deviceSequence: operation.deviceSequence,
    operationId: operation.operationId,
    iv,
    ciphertext,
  } satisfies EncryptedProductOperation);
  await complete;
}

async function migratePlaintextQueue(): Promise<void> {
  const database = await openDatabase();
  const legacyRead = database.transaction(OPERATIONS_STORE, "readonly");
  const legacyReadDone = transactionComplete(legacyRead);
  const legacyOperations = await requestResult(
    legacyRead.objectStore(OPERATIONS_STORE).getAll() as IDBRequest<OfflineProductOperation[]>,
  );
  await legacyReadDone;

  const encryptedOperations = await readEncryptedOperations(database);
  const encryptedIds = new Set(encryptedOperations.map((operation) => operation.operationId));
  for (const operation of legacyOperations) {
    if (!encryptedIds.has(operation.operationId)) await writeEncryptedOperation(database, operation);
  }

  const migrated = await readEncryptedOperations(database);
  const migratedById = new Map(migrated.map((operation) => [operation.operationId, operation]));
  for (const original of legacyOperations) {
    const saved = migratedById.get(original.operationId);
    if (!saved || saved.deviceSequence !== original.deviceSequence || canonicalJson(saved) !== canonicalJson(original)) {
      throw new Error("The existing product queue could not be verified after encryption; original entries were preserved.");
    }
  }

  const maxLegacySequence = legacyOperations.reduce((maximum, operation) => Math.max(maximum, operation.deviceSequence), 0);
  const counterTransaction = database.transaction(METADATA_STORE, "readwrite");
  const counterComplete = transactionComplete(counterTransaction);
  const metadata = counterTransaction.objectStore(METADATA_STORE);
  const counterRequest = metadata.get("product-device-sequence") as IDBRequest<StoredCounter | undefined>;
  counterRequest.onsuccess = () => {
    const current = counterRequest.result?.value ?? 0;
    metadata.put({ id: "product-device-sequence", value: Math.max(current, maxLegacySequence) } satisfies StoredCounter);
  };
  await counterComplete;

  if (legacyOperations.length > 0) {
    const clearTransaction = database.transaction(OPERATIONS_STORE, "readwrite");
    const clearComplete = transactionComplete(clearTransaction);
    clearTransaction.objectStore(OPERATIONS_STORE).clear();
    await clearComplete;
  }
}

async function ensureEncryptedQueue(): Promise<void> {
  if (!migrationPromise) {
    migrationPromise = migratePlaintextQueue().catch((error: unknown) => {
      migrationPromise = null;
      throw error;
    });
  }
  await migrationPromise;
}

async function nextDeviceSequence(database: IDBDatabase): Promise<number> {
  const transaction = database.transaction(METADATA_STORE, "readwrite");
  const complete = transactionComplete(transaction);
  const store = transaction.objectStore(METADATA_STORE);
  const request = store.get("product-device-sequence") as IDBRequest<StoredCounter | undefined>;
  let sequence = 0;
  request.onsuccess = () => {
    sequence = (request.result?.value ?? 0) + 1;
    store.put({ id: "product-device-sequence", value: sequence } satisfies StoredCounter);
  };
  await complete;
  return sequence;
}

export async function requestPersistentBrowserStorage(): Promise<boolean> {
  try {
    if (!navigator.storage?.persist) return false;
    return await navigator.storage.persist();
  } catch {
    return false;
  }
}

export async function enqueueProductOperation(
  payload: ProductCreatePayload,
  workspace?: QueueWorkspace,
): Promise<OfflineProductOperation> {
  await ensureEncryptedQueue();
  const database = await openDatabase();
  const newOperation: OfflineProductOperation = {
    operationId: crypto.randomUUID(),
    deviceSequence: await nextDeviceSequence(database),
    operationType: "product.create",
    payload,
    status: "queued",
    createdAt: new Date().toISOString(),
    ...workspace,
  };
  await writeEncryptedOperation(database, newOperation);
  return newOperation;
}

export async function loadProductOperations(): Promise<OfflineProductOperation[]> {
  await ensureEncryptedQueue();
  const database = await openDatabase();
  const operations = await readEncryptedOperations(database);
  for (const operation of operations) {
    if (operation.status === "syncing") {
      await updateOperation(operation.operationId, {
        status: "queued",
        message: "Recovered after an interrupted sync; the same operation ID will be retried.",
      });
    }
  }
  return readEncryptedOperations(database);
}

async function updateOperation(
  operationId: string,
  updates: Partial<Pick<OfflineProductOperation, "status" | "serverProduct" | "message">>,
): Promise<void> {
  await ensureEncryptedQueue();
  const database = await openDatabase();
  const prior = updateLocks.get(operationId) ?? Promise.resolve();
  const next = prior.then(async () => {
    const transaction = database.transaction(ENCRYPTED_OPERATIONS_STORE, "readonly");
    const complete = transactionComplete(transaction);
    const record = await requestResult(
      transaction.objectStore(ENCRYPTED_OPERATIONS_STORE).index("operationId").get(operationId) as IDBRequest<EncryptedProductOperation | undefined>,
    );
    await complete;
    if (!record) return;
    const current = await decryptOfflineValue<OfflineProductOperation>(record.iv, record.ciphertext);
    await writeEncryptedOperation(database, { ...current, ...updates });
  });
  updateLocks.set(operationId, next);
  try {
    await next;
  } finally {
    if (updateLocks.get(operationId) === next) updateLocks.delete(operationId);
  }
}

async function syncWithAuthenticatedServer(
  pending: OfflineProductOperation[],
  authorization: SyncAuthorization,
): Promise<void> {
  const eligible = pending.filter((operation) =>
    operation.tenantId === authorization.tenantId &&
    operation.companyId === authorization.companyId &&
    operation.ownerKey === authorization.ownerKey,
  );
  const inOtherWorkspace = pending.filter((operation) => !eligible.includes(operation));
  for (const operation of inOtherWorkspace) {
    await updateOperation(operation.operationId, {
      message: "Saved for a different company. Switch to that company to sync this entry.",
    });
  }

  for (let offset = 0; offset < eligible.length; offset += 50) {
    const batch = eligible.slice(offset, offset + 50);
    for (const operation of batch) {
      await updateOperation(operation.operationId, { status: "syncing", message: undefined });
    }

    try {
      const batchId = crypto.randomUUID();
      const response = await fetch(apiUrl("/api/v1/sync/products:batch"), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${authorization.accessToken}`,
          "X-Tenant-Id": authorization.tenantId,
          "X-Company-Id": authorization.companyId,
        },
        body: JSON.stringify({
          batchId,
          operations: batch.map((operation) => ({
            operationId: operation.operationId,
            deviceSequence: operation.deviceSequence,
            operationType: operation.operationType,
            payload: operation.payload,
          })),
        }),
      });

      if (!response.ok) {
        const problem = await response.json().catch(() => ({})) as { title?: string; code?: string };
        const message = problem.title ?? problem.code ?? `The server returned ${response.status}.`;
        if (response.status === 401 || response.status === 403) {
          const accessMessage = response.status === 401
            ? "Sign in again to sync these saved entries. They remain on this device."
            : "This account lacks product-create permission. These entries remain saved on this device."; 
          for (const operation of batch) {
            await updateOperation(operation.operationId, { status: "queued", message: accessMessage });
          }
          break;
        }

        if (response.status === 429 || response.status >= 500) {
          for (const operation of batch) {
            await updateOperation(operation.operationId, {
              status: "queued",
              message: "The server is temporarily unavailable. These saved entries will be retried.",
            });
          }
          break;
        }

        for (const operation of batch) {
          await updateOperation(operation.operationId, { status: "rejected", message });
        }
        continue;
      }

      const result = await response.json() as {
        batchId: string;
        results: Array<{
          operationId: string;
          deviceSequence: number;
          status: string;
          product?: ProductSummary | null;
          code?: string | null;
        }>;
      };
      if (result.batchId !== batchId || !Array.isArray(result.results)) {
        throw new Error("The server returned an invalid product sync receipt.");
      }

      const resultsById = new Map(result.results.map((item) => [item.operationId, item]));
      if (resultsById.size !== result.results.length) {
        throw new Error("The server returned duplicate product sync receipts.");
      }

      for (const operation of batch) {
        const item = resultsById.get(operation.operationId);
        if (!item || item.deviceSequence !== operation.deviceSequence) {
          await updateOperation(operation.operationId, {
            status: "queued",
            message: "The server receipt was incomplete. This operation will retry with the same ID.",
          });
          continue;
        }

        if (item.status === "accepted" && item.product) {
          await updateOperation(operation.operationId, {
            status: "accepted",
            serverProduct: item.product,
            message: "Confirmed by the shared server.",
          });
        } else if (item.status === "conflict") {
          await updateOperation(operation.operationId, {
            status: "conflict",
            message: item.code ?? "The server detected a product sync conflict.",
          });
        } else if (item.status === "rejected") {
          await updateOperation(operation.operationId, {
            status: "rejected",
            message: item.code ?? "The server rejected this product operation.",
          });
        } else {
          await updateOperation(operation.operationId, {
            status: "queued",
            message: "The server did not confirm this operation. It will retry with the same ID.",
          });
        }
      }
    } catch {
      for (const operation of batch) {
        await updateOperation(operation.operationId, {
          status: "queued",
          message: "Connection interrupted. This saved batch will retry with the same operation IDs.",
        });
      }
      break;
    }
  }
}
async function syncQueue(authorization?: SyncAuthorization): Promise<OfflineProductOperation[]> {
  let operations = await loadProductOperations();
  const pending = operations.filter((operation) => operation.status === "queued");

  if (authorization) {
    await syncWithAuthenticatedServer(pending, authorization);
    return loadProductOperations();
  }

  // NOTE: The previous unauthenticated dev-mode batch path that called
  // /api/v1/sync/operations:batch has been removed. That endpoint never existed in
  // the backend and every attempt silently failed with HTTP 404. All product sync
  // now uses the authenticated single-operation path (syncWithAuthenticatedServer)
  // regardless of environment mode. Callers without a SyncAuthorization must obtain
  // one before syncing.
  for (const operation of pending) {
    await updateOperation(operation.operationId, {
      status: "queued",
      message: "Sign in to sync this product to the server. The record is safely stored on this device.",
    });
  }

  operations = await loadProductOperations();
  return operations;
}

export function syncPendingProductOperations(
  authorization?: SyncAuthorization,
): Promise<OfflineProductOperation[]> {
  const syncKey = authorization
    ? `${authorization.tenantId}:${authorization.companyId}`
    : "local-development";
  const existingSync = syncPromises.get(syncKey);
  if (existingSync) return existingSync;
  const promise = syncQueue(authorization).finally(() => {
    if (syncPromises.get(syncKey) === promise) syncPromises.delete(syncKey);
  });
  syncPromises.set(syncKey, promise);
  return promise;
}
