import { apiFetch, type ApiWorkspaceScope } from "./auth";

export type InventoryReceiptPayload = {
  variantId: string;
  warehouseId: string;
  binId: string;
  quantity: number;
  unitOfMeasureId: string;
  sourceDocumentType: string | null;
  sourceDocumentId: string | null;
  note: string | null;
};

export type ReceiptQueueScope = {
  tenantId?: string;
  companyId?: string;
  companyName?: string;
  ownerKey?: string;
};

export type ServerReceipt = {
  transactionId: string;
  movementId: string;
  variantId: string;
  warehouseId: string;
  binId: string;
  quantity: number;
  unitOfMeasureId: string;
  occurredAt: string;
  postedAt: string;
};

export type OfflineReceiptOperation = {
  operationId: string;
  deviceSequence: number;
  operationType: "inventory.receipt.post";
  payload: InventoryReceiptPayload;
  status: "queued" | "syncing" | "accepted" | "conflict" | "rejected";
  createdAt: string;
  tenantId?: string;
  companyId?: string;
  companyName?: string;
  ownerKey?: string;
  serverReceipt?: ServerReceipt;
  message?: string;
};

type EncryptedRecord = {
  operationId: string;
  deviceSequence: number;
  scopeHash: string;
  status: OfflineReceiptOperation["status"];
  createdAt: string;
  revision: number;
  iv: Uint8Array;
  ciphertext: ArrayBuffer;
};

type StoredCryptoKey = { id: string; key: CryptoKey };
type StoredCounter = { id: string; value: number };
type StoredCursor = { id: string; value: string };
type EncryptedSnapshot = { scopeHash: string; iv: Uint8Array; ciphertext: ArrayBuffer };
type EncryptedSecret = { id: string; iv: Uint8Array; ciphertext: ArrayBuffer };
type OfflineDeviceGrant = { deviceId: string; grantId: string; grantToken: string; expiresAt: string };

export type InventoryChangeCursors = { products: string; inventory: string };

const DATABASE_NAME = "universal-erp-secure-offline";
const DATABASE_VERSION = 1;
const OPERATIONS_STORE = "receiptOperations";
const KEYS_STORE = "encryptionKeys";
const METADATA_STORE = "metadata";
const SNAPSHOTS_STORE = "inventorySnapshots";

let databasePromise: Promise<IDBDatabase> | null = null;
const activeSyncs = new Map<string, Promise<OfflineReceiptOperation[]>>();
const updateLocks = new Map<string, Promise<void>>();
const snapshotLocks = new Map<string, Promise<void>>();

function openDatabase(): Promise<IDBDatabase> {
  if (databasePromise) return databasePromise;
  const opening = new Promise<IDBDatabase>((resolve, reject) => {
    let blocked = false;
    const request = indexedDB.open(DATABASE_NAME, DATABASE_VERSION);
    request.onupgradeneeded = () => {
      const database = request.result;
      if (!database.objectStoreNames.contains(OPERATIONS_STORE)) {
        const operations = database.createObjectStore(OPERATIONS_STORE, { keyPath: "operationId" });
        operations.createIndex("scopeHash", "scopeHash");
        operations.createIndex("deviceSequence", "deviceSequence", { unique: true });
        operations.createIndex("status", "status");
      }
      if (!database.objectStoreNames.contains(KEYS_STORE)) database.createObjectStore(KEYS_STORE, { keyPath: "id" });
      if (!database.objectStoreNames.contains(METADATA_STORE)) database.createObjectStore(METADATA_STORE, { keyPath: "id" });
      if (!database.objectStoreNames.contains(SNAPSHOTS_STORE)) database.createObjectStore(SNAPSHOTS_STORE, { keyPath: "scopeHash" });
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
    request.onerror = () => reject(request.error ?? new Error("Could not open secure offline storage."));
    request.onblocked = () => {
      blocked = true;
      reject(new Error("Secure offline storage is open in another tab. Close or refresh the other ERP tab, then retry."));
    };
  });
  databasePromise = opening.catch((error: unknown) => {
    databasePromise = null;
    throw error;
  });
  return databasePromise;
}

function transactionComplete(transaction: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    transaction.oncomplete = () => resolve();
    transaction.onerror = () => reject(transaction.error ?? new Error("Local storage transaction failed."));
    transaction.onabort = () => reject(transaction.error ?? new Error("Local storage transaction was interrupted."));
  });
}

function requestResult<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error("Local storage request failed."));
  });
}

async function encryptionKey(): Promise<CryptoKey> {
  const database = await openDatabase();
  const read = database.transaction(KEYS_STORE, "readonly");
  const readDone = transactionComplete(read);
  const existing = await requestResult(read.objectStore(KEYS_STORE).get("device-aes-key") as IDBRequest<StoredCryptoKey | undefined>);
  await readDone;
  if (existing?.key) return existing.key;

  if (!globalThis.crypto?.subtle) throw new Error("This browser cannot encrypt offline receipt data.");
  const candidate = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
  const write = database.transaction(KEYS_STORE, "readwrite");
  const writeDone = transactionComplete(write);
  let selectedKey: CryptoKey = candidate;
  const getRequest = write.objectStore(KEYS_STORE).get("device-aes-key") as IDBRequest<StoredCryptoKey | undefined>;
  getRequest.onsuccess = () => {
    if (getRequest.result?.key) selectedKey = getRequest.result.key;
    else write.objectStore(KEYS_STORE).put({ id: "device-aes-key", key: candidate } satisfies StoredCryptoKey);
  };
  await writeDone;
  return selectedKey;
}

async function hashScope(scope: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(scope));
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
}

async function seal(value: unknown): Promise<{ iv: Uint8Array; ciphertext: ArrayBuffer }> {
  const key = await encryptionKey();
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const ciphertext = await crypto.subtle.encrypt(
    { name: "AES-GCM", iv },
    key,
    new TextEncoder().encode(JSON.stringify(value)),
  );
  return { iv, ciphertext };
}

async function unseal<T>(iv: Uint8Array, ciphertext: ArrayBuffer): Promise<T> {
  const key = await encryptionKey();
  const plaintext = await crypto.subtle.decrypt({ name: "AES-GCM", iv }, key, ciphertext);
  return JSON.parse(new TextDecoder().decode(plaintext)) as T;
}

export async function encryptOfflineValue(value: unknown): Promise<{ iv: Uint8Array; ciphertext: ArrayBuffer }> {
  return seal(value);
}

export async function decryptOfflineValue<T>(iv: Uint8Array, ciphertext: ArrayBuffer): Promise<T> {
  return unseal<T>(iv, ciphertext);
}

async function nextDeviceSequence(database: IDBDatabase): Promise<number> {
  const transaction = database.transaction(METADATA_STORE, "readwrite");
  const complete = transactionComplete(transaction);
  const store = transaction.objectStore(METADATA_STORE);
  const getRequest = store.get("device-sequence") as IDBRequest<StoredCounter | undefined>;
  let sequence = 0;
  getRequest.onsuccess = () => {
    sequence = (getRequest.result?.value ?? 0) + 1;
    store.put({ id: "device-sequence", value: sequence } satisfies StoredCounter);
  };
  await complete;
  return sequence;
}

async function readRecord(record: EncryptedRecord): Promise<OfflineReceiptOperation> {
  return unseal<OfflineReceiptOperation>(record.iv, record.ciphertext);
}

async function writeRecord(operation: OfflineReceiptOperation, scopeHash: string, revision: number): Promise<void> {
  const { iv, ciphertext } = await seal(operation);
  const database = await openDatabase();
  const transaction = database.transaction(OPERATIONS_STORE, "readwrite");
  const complete = transactionComplete(transaction);
  transaction.objectStore(OPERATIONS_STORE).put({
    operationId: operation.operationId,
    deviceSequence: operation.deviceSequence,
    scopeHash,
    status: operation.status,
    createdAt: operation.createdAt,
    revision,
    iv,
    ciphertext,
  } satisfies EncryptedRecord);
  await complete;
}

export async function enqueueReceiptOperation(
  payload: InventoryReceiptPayload,
  scope: ReceiptQueueScope,
  storageScope: string,
): Promise<OfflineReceiptOperation> {
  await encryptionKey();
  const database = await openDatabase();
  const sequence = await nextDeviceSequence(database);
  const operation: OfflineReceiptOperation = {
    operationId: crypto.randomUUID(),
    deviceSequence: sequence,
    operationType: "inventory.receipt.post",
    payload,
    status: "queued",
    createdAt: new Date().toISOString(),
    ...scope,
  };
  await writeRecord(operation, await hashScope(storageScope), 1);
  return operation;
}

export async function loadReceiptOperations(storageScope: string): Promise<OfflineReceiptOperation[]> {
  const database = await openDatabase();
  const scopeHash = await hashScope(storageScope);
  const transaction = database.transaction(OPERATIONS_STORE, "readonly");
  const complete = transactionComplete(transaction);
  const records = await requestResult(
    transaction.objectStore(OPERATIONS_STORE).index("scopeHash").getAll(scopeHash) as IDBRequest<EncryptedRecord[]>,
  );
  await complete;
  const operations = await Promise.all(records.map(readRecord));
  const ordered = operations.sort((left, right) => left.deviceSequence - right.deviceSequence);
  for (const operation of ordered) {
    if (operation.status === "syncing") {
      await updateReceiptOperation(operation, scopeHash, { status: "queued", message: "Recovered after an interrupted sync; safe to retry." });
    }
  }
  return ordered.map((operation) => operation.status === "syncing"
    ? { ...operation, status: "queued" as const, message: "Recovered after an interrupted sync; safe to retry." }
    : operation);
}

async function updateReceiptOperation(
  operation: OfflineReceiptOperation,
  scopeHash: string,
  updates: Partial<Pick<OfflineReceiptOperation, "status" | "serverReceipt" | "message">>,
): Promise<void> {
  const prior = updateLocks.get(operation.operationId) ?? Promise.resolve();
  const next = prior.then(async () => {
    const database = await openDatabase();
    const transaction = database.transaction(OPERATIONS_STORE, "readonly");
    const complete = transactionComplete(transaction);
    const currentRecord = await requestResult(
      transaction.objectStore(OPERATIONS_STORE).get(operation.operationId) as IDBRequest<EncryptedRecord | undefined>,
    );
    await complete;
    if (!currentRecord) return;
    const current = await readRecord(currentRecord);
    await writeRecord({ ...current, ...updates }, scopeHash, currentRecord.revision + 1);
  });
  updateLocks.set(operation.operationId, next);
  try {
    await next;
  } finally {
    if (updateLocks.get(operation.operationId) === next) updateLocks.delete(operation.operationId);
  }
}

export async function requeueReceiptOperation(
  operationId: string,
  storageScope: string,
): Promise<void> {
  const scopeHash = await hashScope(storageScope);
  const database = await openDatabase();
  const transaction = database.transaction(OPERATIONS_STORE, "readonly");
  const complete = transactionComplete(transaction);
  const record = await requestResult(
    transaction.objectStore(OPERATIONS_STORE).get(operationId) as IDBRequest<EncryptedRecord | undefined>,
  );
  await complete;
  if (!record) return;
  const operation = await readRecord(record);
  await updateReceiptOperation(operation, scopeHash, {
    status: "queued",
    message: "Re-queued by user for synchronization retry.",
  });
}

export async function loadInventoryChangeCursors(storageScope: string): Promise<InventoryChangeCursors> {
  const scopeHash = await hashScope(storageScope);
  const database = await openDatabase();
  const transaction = database.transaction(METADATA_STORE, "readonly");
  const complete = transactionComplete(transaction);
  const store = transaction.objectStore(METADATA_STORE);
  const productsRequest = store.get(`change-cursor:${scopeHash}:products`) as IDBRequest<StoredCursor | undefined>;
  const inventoryRequest = store.get(`change-cursor:${scopeHash}:inventory`) as IDBRequest<StoredCursor | undefined>;
  const [products, inventory] = await Promise.all([requestResult(productsRequest), requestResult(inventoryRequest)]);
  await complete;
  return { products: products?.value ?? "0", inventory: inventory?.value ?? "0" };
}

export async function saveInventorySnapshotAndCursors(
  storageScope: string,
  snapshot: unknown,
  cursors: InventoryChangeCursors,
): Promise<void> {
  const scopeHash = await hashScope(storageScope);
  const prior = snapshotLocks.get(scopeHash) ?? Promise.resolve();
  const next = prior.then(async () => {
    const { iv, ciphertext } = await seal(snapshot);
    const database = await openDatabase();
    const transaction = database.transaction([SNAPSHOTS_STORE, METADATA_STORE], "readwrite");
    const complete = transactionComplete(transaction);
    const metadata = transaction.objectStore(METADATA_STORE);
    const snapshots = transaction.objectStore(SNAPSHOTS_STORE);

    const [prodReq, invReq] = await Promise.all([
      requestResult(metadata.get(`change-cursor:${scopeHash}:products`) as IDBRequest<StoredCursor | undefined>),
      requestResult(metadata.get(`change-cursor:${scopeHash}:inventory`) as IDBRequest<StoredCursor | undefined>),
    ]);

    const storedProd = prodReq?.value ?? "0";
    const storedInv = invReq?.value ?? "0";

    // Enforce cursor monotonicity inside the serialized lock and transaction
    if (BigInt(cursors.products) >= BigInt(storedProd) && BigInt(cursors.inventory) >= BigInt(storedInv)) {
      snapshots.put({ scopeHash, iv, ciphertext } satisfies EncryptedSnapshot);
      metadata.put({ id: `change-cursor:${scopeHash}:products`, value: cursors.products } satisfies StoredCursor);
      metadata.put({ id: `change-cursor:${scopeHash}:inventory`, value: cursors.inventory } satisfies StoredCursor);
    }
    await complete;
  });
  snapshotLocks.set(scopeHash, next);
  try {
    await next;
  } finally {
    if (snapshotLocks.get(scopeHash) === next) snapshotLocks.delete(scopeHash);
  }
}

export async function loadInventorySnapshot<T>(storageScope: string): Promise<T | null> {
  const database = await openDatabase();
  const scopeHash = await hashScope(storageScope);
  const transaction = database.transaction(SNAPSHOTS_STORE, "readonly");
  const complete = transactionComplete(transaction);
  const snapshot = await requestResult(
    transaction.objectStore(SNAPSHOTS_STORE).get(scopeHash) as IDBRequest<EncryptedSnapshot | undefined>,
  );
  await complete;
  return snapshot ? unseal<T>(snapshot.iv, snapshot.ciphertext) : null;
}

async function stableReceiptDeviceId(): Promise<string> {
  const database = await openDatabase();
  const transaction = database.transaction(METADATA_STORE, "readwrite");
  const complete = transactionComplete(transaction);
  const store = transaction.objectStore(METADATA_STORE);
  const existingRequest = store.get("receipt-client-device-id") as IDBRequest<StoredCursor | undefined>;
  let deviceId = "";
  existingRequest.onsuccess = () => {
    deviceId = existingRequest.result?.value ?? crypto.randomUUID();
    if (!existingRequest.result) store.put({ id: "receipt-client-device-id", value: deviceId } satisfies StoredCursor);
  };
  await complete;
  return deviceId;
}

async function loadDeviceGrant(storageScope: string): Promise<OfflineDeviceGrant | null> {
  const database = await openDatabase();
  const scopeHash = await hashScope(storageScope);
  const transaction = database.transaction(METADATA_STORE, "readonly");
  const complete = transactionComplete(transaction);
  const encrypted = await requestResult(
    transaction.objectStore(METADATA_STORE).get(`receipt-device-grant:${scopeHash}`) as IDBRequest<EncryptedSecret | undefined>,
  );
  await complete;
  return encrypted ? unseal<OfflineDeviceGrant>(encrypted.iv, encrypted.ciphertext) : null;
}

async function saveDeviceGrant(storageScope: string, grant: OfflineDeviceGrant): Promise<void> {
  const scopeHash = await hashScope(storageScope);
  const encrypted = await seal(grant);
  const database = await openDatabase();
  const transaction = database.transaction(METADATA_STORE, "readwrite");
  const complete = transactionComplete(transaction);
  transaction.objectStore(METADATA_STORE).put({
    id: `receipt-device-grant:${scopeHash}`,
    ...encrypted,
  } satisfies EncryptedSecret);
  await complete;
}

async function registerDeviceGrant(
  storageScope: string,
  workspace: ApiWorkspaceScope | null,
): Promise<OfflineDeviceGrant> {
  const clientDeviceId = await stableReceiptDeviceId();
  const response = await apiFetch("/api/v1/sync/devices", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      clientDeviceId,
      platformCode: "web",
      deviceName: globalThis.navigator?.userAgent?.slice(0, 160) ?? "Web browser",
    }),
  }, workspace);
  if (!response.ok) throw new Error(`The server could not authorize this device for offline receipt sync (${response.status}).`);
  const grant = await response.json() as OfflineDeviceGrant;
  if (!grant.deviceId || !grant.grantId || !grant.grantToken || !Number.isFinite(Date.parse(grant.expiresAt))) {
    throw new Error("The server returned an incomplete offline device grant.");
  }
  await saveDeviceGrant(storageScope, grant);
  return grant;
}

async function ensureDeviceGrant(
  storageScope: string,
  workspace: ApiWorkspaceScope | null,
): Promise<OfflineDeviceGrant> {
  const cached = await loadDeviceGrant(storageScope);
  if (cached && Date.parse(cached.expiresAt) > Date.now() + 24 * 60 * 60 * 1000) return cached;
  // Cached grant is missing, expired, or expires within 24 hours — re-register.
  return registerDeviceGrant(storageScope, workspace);
}

export function syncPendingReceiptOperations(
  storageScope: string,
  workspace: ApiWorkspaceScope | null,
): Promise<OfflineReceiptOperation[]> {
  const existing = activeSyncs.get(storageScope);
  if (existing) return existing;
  const sync = (async () => {
    const scopeHash = await hashScope(storageScope);
    const loaded = await loadReceiptOperations(storageScope);
    const pending = loaded.filter((operation) => operation.status === "queued");
    if (pending.length === 0) return loaded;

    let grant: OfflineDeviceGrant;
    try {
      grant = await ensureDeviceGrant(storageScope, workspace);
    } catch (error) {
      const message = error instanceof Error ? error.message : "The device could not be authorized. The receipt remains in the encrypted local journal.";
      for (const operation of pending) {
        await updateReceiptOperation(operation, scopeHash, { status: "queued", message });
      }
      return loadReceiptOperations(storageScope);
    }

    for (const operation of pending) {
      await updateReceiptOperation(operation, scopeHash, { status: "syncing", message: undefined });
      try {
        const response = await apiFetch("/api/v1/sync/receipts:batch", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            batchId: crypto.randomUUID(),
            deviceId: grant.deviceId,
            grantId: grant.grantId,
            grantToken: grant.grantToken,
            operations: [{
              operationId: operation.operationId,
              deviceSequence: operation.deviceSequence,
              operationType: operation.operationType,
              createdAt: operation.createdAt,
              payload: { ...operation.payload, occurredAt: operation.createdAt },
            }],
          }),
        }, workspace);

        if (response.ok) {
          const batch = await response.json() as { results?: Array<{
            operationId: string;
            status: "accepted" | "duplicate" | "conflict" | "rejected" | "deferred";
            receipt: ServerReceipt | null;
            code: string | null;
          }> };
          const result = batch.results?.find((item) => item.operationId === operation.operationId);
          if (!result) throw new Error("The server did not return an acknowledgment for this receipt.");
          if (result.status === "deferred") {
            await updateReceiptOperation(operation, scopeHash, {
              status: "queued",
              message: "The server has reserved this receipt and will safely resume it. It remains in the encrypted local journal.",
            });
            break;
          }
          await updateReceiptOperation(operation, scopeHash, {
            status: result.status === "accepted" || result.status === "duplicate"
              ? "accepted"
              : result.status,
            serverReceipt: result.receipt ?? undefined,
            message: result.status === "accepted" || result.status === "duplicate"
              ? "Confirmed by the server. The local receipt is retained in the journal."
              : result.code ?? "The server kept this receipt for review.",
          });
          continue;
        }

        const problem = await response.json().catch(() => ({})) as { title?: string; code?: string };
        const message = problem.title ?? problem.code ?? `The server returned ${response.status}.`;
        if (response.status === 401 || response.status === 403) {
          // Grant has expired or was revoked. Re-register to obtain fresh credentials.
          try {
            grant = await registerDeviceGrant(storageScope, workspace);
            // Immediately retry the batch with the newly renewed grant
            const retryResponse = await apiFetch("/api/v1/sync/receipts:batch", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({
                batchId: crypto.randomUUID(),
                deviceId: grant.deviceId,
                grantId: grant.grantId,
                grantToken: grant.grantToken,
                operations: [{
                  operationId: operation.operationId,
                  deviceSequence: operation.deviceSequence,
                  operationType: operation.operationType,
                  createdAt: operation.createdAt,
                  payload: { ...operation.payload, occurredAt: operation.createdAt },
                }],
              }),
            }, workspace);

            if (retryResponse.ok) {
              const retryBatch = await retryResponse.json() as { results?: Array<{
                operationId: string;
                status: "accepted" | "duplicate" | "conflict" | "rejected" | "deferred";
                receipt: ServerReceipt | null;
                code: string | null;
              }> };
              const retryResult = retryBatch.results?.find((item) => item.operationId === operation.operationId);
              if (retryResult) {
                if (retryResult.status === "deferred") {
                  await updateReceiptOperation(operation, scopeHash, {
                    status: "queued",
                    message: "The server has reserved this receipt and will safely resume it. It remains in the encrypted local journal.",
                  });
                  break;
                }
                await updateReceiptOperation(operation, scopeHash, {
                  status: retryResult.status === "accepted" || retryResult.status === "duplicate"
                    ? "accepted"
                    : retryResult.status,
                  serverReceipt: retryResult.receipt ?? undefined,
                  message: retryResult.status === "accepted" || retryResult.status === "duplicate"
                    ? "Confirmed by the server. The local receipt is retained in the journal."
                    : retryResult.code ?? "The server kept this receipt for review.",
                });
                continue; // Successfully retried! Continue syncing next pending operation.
              }
            }
          } catch {
            // Re-registration or retry failed (e.g. device revoked or permissions removed).
          }

          // If retry did not succeed or re-registration failed:
          for (const remaining of pending) {
            await updateReceiptOperation(remaining, scopeHash, {
              status: "queued",
              message: "Sign in again or restore access. The receipt remains in the encrypted local journal.",
            });
          }
          break;
        }
        if (response.status === 429 || response.status >= 500) {
          await updateReceiptOperation(operation, scopeHash, {
            status: "queued",
            message: "The server did not confirm this receipt. It remains queued and will retry with the same ID.",
          });
          break;
        }
        await updateReceiptOperation(operation, scopeHash, {
          status: response.status === 409 ? "conflict" : "rejected",
          message,
        });
      } catch {
        await updateReceiptOperation(operation, scopeHash, {
          status: "queued",
          message: "Connection interrupted. This receipt remains encrypted locally and will retry with the same ID.",
        });
        break;
      }
    }
    return loadReceiptOperations(storageScope);
  })().finally(() => {
    activeSyncs.delete(storageScope);
  });
  activeSyncs.set(storageScope, sync);
  return sync;
}
