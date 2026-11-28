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

async function syncWithAuthenticatedServer(\n  pending: OfflineProductOperation[],\n  authorization: SyncAuthorization,\n): Promise<void> {\n  const eligible = pending.filter((operation) =>\n    operation.tenantId === authorization.tenantId &&\n    operation.companyId === authorization.companyId &&\n    operation.ownerKey === authorization.ownerKey,\n  );\n  const inOtherWorkspace = pending.filter((operation) => !eligible.includes(operation));\n  for (const operation of inOtherWorkspace) {\n    await updateOperation(operation.operationId, {\n      message: "Saved for a different company. Switch to that company to sync this entry.",\n    });\n  }\n\n  for (let offset = 0; offset < eligible.length; offset += 50) {\n    const batch = eligible.slice(offset, offset + 50);\n    for (const operation of batch) {\n      await updateOperation(operation.operationId, { status: "syncing", message: undefined });\n    }\n\n    try {\n      const response = await fetch(apiUrl("/api/v1/sync/products:batch"), {\n        method: "POST",\n        headers: {\n          "Content-Type": "application/json",\n          Authorization: `Bearer ${authorization.accessToken}`,\n          "X-Tenant-Id": authorization.tenantId,\n          "X-Company-Id": authorization.companyId,\n        },\n        body: JSON.stringify({\n          batchId,\n          operations: batch.map((operation) => ({\n            operationId: operation.operationId,\n            deviceSequence: operation.deviceSequence,\n            operationType: operation.operationType,\n            payload: operation.payload,\n          })),\n        }),\n      });\n\n      if (!response.ok) {\n        const problem = await response.json().catch(() => ({})) as { title?: string; code?: string };\n        const message = problem.title ?? problem.code ?? `The server returned ${response.status}.`;\n        if (response.status === 401 || response.status === 403) {\n          const accessMessage = response.status === 401\n            ? "Sign in again to sync these saved entries. They remain on this device."\n            : "This account lacks product-create permission. These entries remain saved on this device."; \n          for (const operation of batch) {\n            await updateOperation(operation.operationId, { status: "queued", message: accessMessage });\n          }\n          break;\n        }\n\n        if (response.status === 429 || response.status >= 500) {\n          for (const operation of batch) {\n            await updateOperation(operation.operationId, {\n              status: "queued",\n              message: "The server is temporarily unavailable. These saved entries will be retried.",\n            });\n          }\n          break;\n        }\n\n        for (const operation of batch) {\n          await updateOperation(operation.operationId, { status: "rejected", message });\n        }\n        continue;\n      }\n\n      const result = await response.json() as {\n        batchId: string;\n        results: Array<{\n          operationId: string;\n          deviceSequence: number;\n          status: string;\n          product?: ProductSummary | null;\n          code?: string | null;\n        }>;\n      };\n      if (result.batchId !== batchId || !Array.isArray(result.results)) {\n        throw new Error("The server returned an invalid product sync receipt.");\n      }\n\n      const resultsById = new Map(result.results.map((item) => [item.operationId, item]));\n      if (resultsById.size !== result.results.length) {\n        throw new Error("The server returned duplicate product sync receipts.");\n      }\n\n      for (const operation of batch) {\n        const item = resultsById.get(operation.operationId);\n        if (!item || item.deviceSequence !== operation.deviceSequence) {\n          await updateOperation(operation.operationId, {\n            status: "queued",\n            message: "The server receipt was incomplete. This operation will retry with the same ID.",\n          });\n          continue;\n        }\n\n        if (item.status === "accepted" && item.product) {\n          await updateOperation(operation.operationId, {\n            status: "accepted",\n            serverProduct: item.product,\n            message: "Confirmed by the shared server.",\n          });\n        } else if (item.status === "conflict") {\n          await updateOperation(operation.operationId, {\n            status: "conflict",\n            message: item.code ?? "The server detected a product sync conflict.",\n          });\n        } else if (item.status === "rejected") {\n          await updateOperation(operation.operationId, {\n            status: "rejected",\n            message: item.code ?? "The server rejected this product operation.",\n          });\n        } else {\n          await updateOperation(operation.operationId, {\n            status: "queued",\n            message: "The server did not confirm this operation. It will retry with the same ID.",\n          });\n        }\n      }\n    } catch {\n      for (const operation of batch) {\n        await updateOperation(operation.operationId, {\n          status: "queued",\n          message: "Connection interrupted. This saved batch will retry with the same operation IDs.",\n        });\n      }\n      break;\n    }\n  }\n}\nasync function syncQueue(authorization?: SyncAuthorization): Promise<OfflineProductOperation[]> {
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
