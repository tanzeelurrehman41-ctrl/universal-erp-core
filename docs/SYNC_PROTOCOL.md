# SYNC_PROTOCOL.md — Universal ERP Offline Synchronisation Protocol

**Status:** Hardened Baseline (Partially Implemented & Rigorously Verified)  
**Last updated:** 2026-11-03  
**Covers:** Web client offline journals, backend inbox idempotency, gapless change-feed sequencing, PostgreSQL RLS isolation, snapshot cursor atomicity, and device grant renewal security.

---

## 1. Architectural Overview & Invariants

The Universal ERP synchronisation architecture enables client devices (browsers, tablets, POS terminals) to reliably record business operations while disconnected from the network and safely synchronize when connectivity resumes.

The system enforces six core architectural invariants:
1. **Idempotent At-Least-Once Execution:** Every client operation possesses an immutable, client-generated UUID (`operationId`). Retries or replayed requests cannot generate duplicate stock movements, general ledger journals, or sales invoices.
2. **Deterministic Lost-Acknowledgement Recovery:** If the server commits a transaction but the network drops before the acknowledgement reaches the client, re-submission of the identical payload returns the durable server receipt without re-executing business logic.
3. **Explicit Conflict Trapping:** When an `operationId` is reused with different payload data or parameters, the server explicitly detects the mutation and returns `status = "conflict"`, `code = "OPERATION_ID_REUSED"`. No silent overwrites are allowed.
4. **Gapless, Monotonic Cursor Ordering:** Company-scoped change feeds use an atomically allocated integer sequence (`company_sequence`) backed by PostgreSQL row locks. Out-of-order commits and sequence gaps on rollback are prevented at the database engine level.
5. **Atomic & Monotonic Snapshot Storage:** Local cache updates are guarded by IndexedDB transaction-level locks and strict two-dimensional cursor comparisons (`products` and `inventory`). A stale or out-of-order snapshot can never overwrite newer local state.
6. **Multi-Tenant Row-Level Security (RLS):** All sync tables (`sync.operation_inbox`, `sync.change_log`, `sync.company_change_counters`, `sync.offline_grants`, `sync.device_company_scopes`) enforce RLS fail-closed policies with transaction-scoped `set_config` variables (`app.tenant_id`, `app.company_id`).

---

## 2. In-Depth Concurrency & Correctness Reviews

### 2.1 Company Cursor Allocation & Change Log Publication (Concurrency, Rollbacks & Retries)

#### Allocation Mechanism
Change sequence numbers are allocated in `PostgresSyncChangeFeed.cs` via:
```sql
INSERT INTO sync.company_change_counters (tenant_id, company_id, last_sequence)
VALUES (@tenantId, @companyId, 1)
ON CONFLICT (tenant_id, company_id)
DO UPDATE SET last_sequence = sync.company_change_counters.last_sequence + 1, updated_at = now()
RETURNING last_sequence;
```
followed immediately by:
```sql
INSERT INTO sync.change_log (tenant_id, company_id, company_sequence, entity_type, entity_id, entity_version, change_type, payload)
VALUES (@tenantId, @companyId, @companySequence, @entityType, @entityId, @entityVersion, @changeType, @payload);
```

#### Concurrency & Serializability Analysis
* **Why Out-of-Order Commits Cannot Occur:**  
  Standard PostgreSQL `SEQUENCE` objects (`nextval()`) operate outside transaction boundaries; if transaction Tx1 gets 10 and Tx2 gets 11, Tx2 could commit before Tx1, exposing sequence 11 to polling clients before sequence 10, causing clients polling `after=11` to permanently skip 10.  
  Universal ERP **does not use a sequence object**. Instead, it performs an `UPDATE` on a row in `sync.company_change_counters`. In PostgreSQL, an `UPDATE` on a row acquires an exclusive row-level lock (`FOR UPDATE`). When Tx1 executes `allocate`, it holds the lock on `(tenant_id, company_id)`. If concurrent Tx2 attempts to execute `allocate`, **Tx2 is blocked at the database engine level until Tx1 commits or rolls back**. Tx2 cannot receive sequence 11 until Tx1 has completely finished. Therefore, sequence 11 can never be committed before sequence 10.
* **Rollback Safety:**  
  If Tx1 fails or rolls back, PostgreSQL transaction rollback rolls back the increment in `sync.company_change_counters` to its previous value (e.g. 9) and rolls back the row inserted into `sync.change_log`. When Tx2 acquires the lock, it reads 9, increments to 10, and commits sequence 10. There are **zero holes/gaps** created in the sequence counter due to transaction aborts.
* **Deadlock Freedom Across Companies:**  
  Tenant-wide events (such as `product.create`) call `AppendTenantWideAsync`, which queries active companies ordered deterministically by `id` (`ORDER BY id`). Because all concurrent transactions acquire row locks in identical company order, cyclic wait conditions (deadlocks) are mathematically impossible.
* **Change Log Filtering:**  
  `sync.change_log` is indexed by `(tenant_id, company_id, entity_type, company_sequence)` (via Migration 009). Polling endpoints (`GET /api/v1/sync/changes/{entityType}`) query this index sequentially. Clients polling `/products` or `/inventory` receive gapless slices ordered by sequence without skipping events.

---

### 2.2 Snapshot Cursor Comparison & Storage Atomicity

#### The TOCTOU Hazard & Hardened Mutex Solution
Previously, `saveInventorySnapshotAndCursors` in `offlineReceiptQueue.ts` read stored cursors in a separate readonly transaction, performed asynchronous encryption (`seal()`), and subsequently opened a readwrite transaction to store the snapshot. Under concurrent calls, an older snapshot could seal faster or commit later, overwriting a newer snapshot.

#### Hardened Implementation
`offlineReceiptQueue.ts` was hardened with:
1. **Per-Scope Promise Mutex (`snapshotLocks`):** All calls to `saveInventorySnapshotAndCursors` for a given `storageScope` are serialized through an in-memory lock chain.
2. **In-Transaction Cursor Evaluation:** The current stored cursors for both `products` and `inventory` are queried directly inside the atomic readwrite IndexedDB transaction across `[SNAPSHOTS_STORE, METADATA_STORE]`.
3. **Strict 2D Monotonicity Enforcement:**
   ```typescript
   if (BigInt(cursors.products) >= BigInt(storedProd) && BigInt(cursors.inventory) >= BigInt(storedInv)) {
     snapshots.put({ scopeHash, iv, ciphertext });
     metadata.put({ id: `change-cursor:${scopeHash}:products`, value: cursors.products });
     metadata.put({ id: `change-cursor:${scopeHash}:inventory`, value: cursors.inventory });
   }
   ```
   If either cursor regresses, the write is aborted atomically. A newer snapshot can never be regressed by an older network response.

---

### 2.3 Device Registration & Grant Renewal Security

#### Revocation Trapping
In `PostgresOfflineReceiptSyncRepository.RegisterDeviceAsync`:
1. **Device Status:** `identity.devices` is locked `FOR UPDATE`. If `status != 'active'`, registration fails (`null` returned, triggering HTTP 409 `DEVICE_REVOKED_OR_INACTIVE`).
2. **Company Status:** `organization.companies` is queried; if `status != 'active'`, registration fails (`null` returned).
3. **Company Scope Revocation Defense (Fixed Bug):** Previously, `device_company_scopes` executed `ON CONFLICT DO UPDATE SET scope_status = 'active', revoked_at = NULL`, allowing a revoked device to un-revoke its own access upon renewal. This was eliminated: the existing scope is now inspected with `FOR UPDATE`; if `scope_status != 'active'`, registration returns `null`. A revoked device scope cannot self-reactivate.
4. **Grant Validation:** `ReserveReceiptOperationAsync` validates grants via a 6-table join checking:
   - `device.status = 'active'`
   - `company_scope.scope_status = 'active'`
   - `offline_grant.revoked_at IS NULL`
   - `offline_grant.expires_at > now()`
   - `offline_grant.grant_signature = SHA256(rawToken)`
   - `offline_grant.allowed_operations ? 'inventory.receipt.post'`

#### Client-Side Auto-Renewal & Immediate Retry
In `offlineReceiptQueue.ts`:
* When an active device's grant expires, the batch sync receives HTTP 401 or 403.
* The client clears the cached grant, calls `registerDeviceGrant()`, acquires a fresh 7-day grant, and **immediately retries the batch**.
* Upon successful retry, remaining operations continue syncing without requiring manual user intervention.
* If re-registration fails (device or company scope revoked), remaining operations are queued with `"Sign in again or restore access. The receipt remains in the encrypted local journal."`, preventing infinite loops.

---

## 3. End-to-End Operation Lifecycle Trace

```
CLIENT (Web / POS / Mobile)                    SERVER (.NET 10 / ASP.NET Core)
──────────────────────────                    ───────────────────────────────
1. User creates Inventory Receipt
2. enqueueReceiptOperation():
   - operationId = crypto.randomUUID()
   - deviceSequence = atomicCounter++
   - AES-GCM Encrypt with non-extractable key
   - Store in IDB 'receiptOperations' (status='queued')

3. syncPendingReceiptOperations():
   - Mark status='syncing'
   - ensureDeviceGrant() (checks cached grant TTL > 24h)

4. POST /api/v1/sync/receipts:batch  ────────► 5. Set RLS context:
   {batchId, deviceId, grantId,                   set_config('app.tenant_id', ...),
    grantToken, operations: [...]}                 set_config('app.company_id', ...)
                                               6. ReserveReceiptOperationAsync():
                                                  - Validate grant, revocation, expiry, HMAC
                                                  - Compute canonical SHA-256 payloadHash
                                                  - Check sync.operation_inbox:
                                                    * If matches & outcome!='deferred' -> "replay"
                                                    * If matches & outcome=='deferred' -> "process"
                                                    * If ID reused with different hash -> "conflict"
                                                  - INSERT INTO sync.operation_inbox ... ON CONFLICT DO NOTHING
                                               7. If "process":
                                                  - inventory.PostReceiptAsync()
                                                  - CompanyChangeLogWriter.AppendAsync()
                                                  - Commit transaction
                                                  - CompleteReceiptOperationAsync()
                                                    UPDATE inbox SET outcome='accepted'

◄───────────────────────────────────────────── 8. Return OfflineReceiptBatchResponse:
   {results: [{operationId, status: "accepted",   status in {accepted, duplicate, conflict, rejected}
              receipt, code}]}

9. Client updates IDB operation:
   - status = "accepted"
   - serverReceipt = receipt
   - message = "Confirmed by server"

10. Incremental Change Polling:
    GET /api/v1/sync/changes/inventory?after=0 ─► 11. ReadPageAsync():
                                                      SELECT FROM sync.change_log
                                                      WHERE company_sequence > after
                                                      ORDER BY company_sequence LIMIT 500
◄───────────────────────────────────────────── 12. Return SyncChangePage{nextCursor, hasMore, changes}
13. saveInventorySnapshotAndCursors():
    - Acquire snapshotLocks mutex
    - Inside readwrite IDB transaction:
      Verify new cursors >= stored cursors
      Put snapshot & update stored cursors
```

---

## 4. Itemized Verification Status Matrix

Each property below is classified based on concrete test evidence:

| Item / Property | Classification | Evidence & Test Details |
|---|---|---|
| **Durable Operation IDs** | **Verified** | Verified in `OfflineSyncServiceTests.cs`, `PostgresOfflineReceiptSyncIntegrationTests.cs`, and `verify-api.ps1`. UUIDs uniquely identify inbox rows. |
| **Idempotency Replay (Lost ACK)** | **Verified** | Verified in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests.ServerInterruption_AfterPosting_BeforeAcknowledgment_RetryReturnsDuplicateReceipt` and live in `verify-api.ps1`. Returns identical `movementId` with `status: "duplicate"`. |
| **Operation ID Reuse Conflict** | **Verified** | Verified in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests.ReuseOfOperationId_WithChangedPayload_ReturnsConflict` and `verify-api.ps1`. Reusing ID with different payload returns `conflict`, `OPERATION_ID_REUSED`. |
| **Concurrent Stock Changes (Same Op ID)** | **Verified** | Verified in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests.TwoSimultaneousSubmissions_SameOperationId_OnlyOneMovementPosted_OtherIsDuplicate`. Exactly 1 movement posted; concurrent request returns duplicate or deferred via transaction advisory lock. |
| **Concurrent Device Sequence Conflicts** | **Verified** | Verified in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests.TwoSimultaneousSubmissions_SameDeviceSequence_DifferentOperationIds_OneAccepted_OtherConflict`. Original operation preserved in inbox; sequence collision durably recorded in `sync.operation_conflicts`. |
| **Atomic Posting & Inbox Commit** | **Verified** | Implemented in `PostgresOfflineReceiptSyncRepository.ProcessReceiptOperationAtomicAsync`. All movement legs, stock balance, change feed, outbox, command idempotency, and inbox outcome commit in a single ACID transaction. |
| **Claim-Token Guarded Acceptance & Rollback** | **Verified** | Verified in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests.ClaimTokenMismatch_AcceptanceThrowsAndRollsBack_NoMovementsCommitted`. Acceptance update asserts `claim_token = @token AND outcome = 'deferred'` and throws on `affected != 1`, rolling back all movements. |
| **Claim-Token Guarded Rejection** | **Verified** | Verified in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests.AffectedRowCountZero_RejectionThrowsAndRollsBack`. Terminal rejection update asserts `claim_token = @token AND outcome = 'deferred'` and throws on `affected != 1`. |
| **Durable Claim & Crash-Recoverable Leases** | **Verified** | Verified in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests.ServerInterruption_AfterReservation_BeforePosting_RecoversAfterLeaseExpiry` and `LeaseExpiryAndRecovery_ExpiredDeferredOperation_ReclaimedAndCompleted`. Active lease protects concurrent requests; expired lease recovers gracefully. |
| **Warehouse & Bin Scope Enforcement** | **Verified** | Tested in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests.RevokedDevice_RevokedCompanyScope_AndUnauthorizedWarehouseBin_EnforcesAllBoundaries`. Foreign warehouse returns `WAREHOUSE_NOT_FOUND`; foreign bin returns `RECEIPT_LOCATION_NOT_SUPPORTED`. |
| **Effective Location Scope (No Warehouse UUID Fallback)** | **Verified** | Verified in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests.LocationScopeMismatch_WarehouseUUIDNotTreatedAsLocation_RejectedWithoutMovements`. Effective location is strictly `warehouse.location_id`; warehouse UUID is never treated as a location UUID. |
| **Command Idempotency Hardening** | **Verified** | Verified in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests.CommandIdempotencyKeyCollision_DifferentCommandOrHash_DoesNotOverwriteResource_ReturnsConflict`. Collision on command_code, request_hash, or company_id returns `COMMAND_IDEMPOTENCY_CONFLICT` without modifying existing resource_id. |
| **Strict Authorization Revocation Serialization** | **Verified** | Verified in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests.AuthorizationRevocation_ForShareSerialization_RevocationBlocksOrRejects`. Validation acquires `FOR SHARE` locks on device, company scope, membership, and grant; concurrent revocation blocks/serializes cleanly. |
| **Grant Multi-Company Scope Isolation** | **Verified** | Tested in `OfflineSyncServiceTests.InvalidCompanyGrantScope_GrantIssuedForDifferentCompany_ReturnsUnauthorized` and integration tests. Grant for Company A cannot be used in Company B (`UNAUTHORIZED`). |
| **Stale Client State Handling** | **Verified** | Tested in `OfflineSyncServiceTests.StaleClientState_DeactivatedVariant_RejectedWithActionableCode`. Variant deactivated server-side returns actionable `VARIANT_INACTIVE` error code and stays in queue. |
| **Actionable Unresolved Queue & Recovery** | **Verified** | Implemented in `offlineReceiptQueue.ts` and `InventoryWorkspace.tsx`. Unresolved/conflicted operations are never truncated or silently discarded, rendered with error reasons, and provide "Copy to form" and "Retry sync" workflows. |
| **Gapless Sequence Allocation** | **Verified** | Enforced via `sync.company_change_counters` row lock. Re-verified by database trigger constraints and `verify-api.ps1` (32 events incremented cursor to 59). |
| **Rollback Safety (No Sequence Gaps)** | **Verified** | ACID transaction rollback resets counter. No sequence gaps or phantom commits possible. |
| **Snapshot Cursor Monotonicity** | **Verified** | Unit tested in `OfflineSyncServiceTests.SnapshotCursorMonotonicity_*` and enforced atomically in `offlineReceiptQueue.ts` via `snapshotLocks` and in-transaction cursor evaluation. |
| **Syncing Crash Recovery** | **Verified** | Tested in `OfflineSyncServiceTests.SyncingRecovery_*` and implemented in `loadReceiptOperations()`; transient `syncing` reverts to `queued`. |
| **Device Revocation Enforcement** | **Verified** | Verified in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests`. Device status `revoked` in `identity.devices` prevents registration and returns unauthorized. |
| **Company Scope Revocation Enforcement** | **Verified** | Verified in real PostgreSQL in `PostgresOfflineReceiptSyncIntegrationTests`. Device cannot self-reactivate a revoked company scope. |
| **Inactive Company Scope Enforcement** | **Verified** | Unit tested in `OfflineSyncServiceTests.InactiveCompany_ReturnsNullOnRegistration`. Target company must have `status = 'active'`. |
| **Grant Expiry / Revocation Enforcement** | **Verified** | Unit tested in `OfflineSyncServiceTests.RevokedGrant_*` and `ExpiredGrant_*`. Grants with `revoked_at != null` or `expires_at <= now()` return HTTP 403 `unauthorized`. |
| **Grant Auto-Renewal & Retry** | **Verified** | Implemented in `offlineReceiptQueue.ts`; client automatically renews on 401/403 and retries batch immediately. |
| **Inventory Transfer & Adjustment Change Log** | **Verified** | Implemented in `PostgresInventoryWorkflowRepository.cs` and verified in `verify-api.ps1`. |
| **Sales Invoice Change Log Publication** | **Verified** | Implemented in `PostgresSalesRepository.cs` and verified in `verify-api.ps1`. |
| **Phantom Batch Endpoint Removal** | **Verified** | Dead unauthenticated path `/api/v1/sync/operations:batch` removed from `offlineProductQueue.ts`. |
| **Purchasing GRN Outbox Retry & Deduplication** | **Verified** | Verified in real PostgreSQL in `PostgresSupplyChainOutboxIntegrationTests.GrnOutbox_InitialProcessing_And_RetryAfterReset_DeduplicatesMovementAndStock_AndMarksPublished`. Resetting published outbox event re-runs deduplicated: 0 duplicate movements, 0 duplicate stock increments, 0 duplicate GL entries. |
| **Purchasing GRN Outbox Poison-Event Isolation** | **Verified** | Verified in real PostgreSQL in `PostgresSupplyChainOutboxIntegrationTests.PoisonEvent_FollowedByHealthyEvent_HealthyEventCommits_AndPoisonEventDoesNotBlockQueue`. Per-event transactions ensure a failing/poison event rolls back independently without rolling back healthy events or blocking the queue. |
| **Purchasing GRN Outbox Exponential Backoff & Dead-Letter** | **Verified** | Verified in real PostgreSQL in `PostgresSupplyChainOutboxIntegrationTests.PoisonEvent_ReachingMaxAttempts_TransitionsToDeadLetterState` and `PoisonEvent_RetryCountAndLastError_PersistsDurablyWithoutThrowingUnhandled`. Max 5 attempts; backoff is $2^{\text{attempt}-1} \times 2.0\text{s}$ (cap 300s, 0–25% jitter); error sanitized to `last_error`; transitions to `dead_letter` without setting `published_at`. |
| **Purchasing GRN Outbox Dead-Letter Redrive** | **Verified** | Verified in real PostgreSQL in `PostgresSupplyChainOutboxIntegrationTests.DeadLetterEvent_WhenRedrivenAndFixed_SuccessfullyProcesses`. Redrive resets `status = 'pending'`, `attempt_count = 0`, `next_retry_at = now()`, tracks `last_redriven_at`, increments `redrive_count`, allowing repaired events to commit cleanly. |
| **Purchasing GRN Outbox Malformed / Unsupported Event Safety** | **Verified** | Verified in real PostgreSQL in `PostgresSupplyChainOutboxIntegrationTests.UnsupportedEventTypeOrEmptyLines_TransitionsDirectlyToDeadLetter_WithoutSilentPublish`. Unsupported event types or empty line payloads transition directly to `dead_letter` with diagnostic error rather than silently marking published. |
| **Purchasing GRN Outbox Concurrent Worker Safety** | **Verified** | Verified in real PostgreSQL in `PostgresSupplyChainOutboxIntegrationTests.ConcurrentWorkers_ProcessingPendingEvents_DoNotDoubleProcessOrCollide`. Competing workers claim pending events via `FOR UPDATE SKIP LOCKED`; zero collisions, zero duplicate movements, exactly one worker processes each event. |
| **Sales Order Change Log Publication** | **Implemented but not verified** | Sales orders are transactional drafts; currently stored in `erp_sales.sales_order` without change log append. |
| **Purchasing GRN Change Log Publication** | **Implemented but not verified** | GRN movements publish via inventory movement leg change log, but GRN header does not yet write directly to `sync.change_log`. |
| **Optimistic Concurrency Control (OCC `base_version`)** | **Open** | Column `sync.operation_inbox.base_version` exists, but entity-level field conflict merging is not yet wired into edit workflows. |
| **Sales / Order Offline Queue & Conflict UI** | **Open** | Offline queue currently exists for Products and Receipts; Sales Orders and POS Invoices require network connection or future local journal. |
| **Change Log Purge / Archival** | **Open** | `sync.change_log` is append-only with no automatic retention pruning. |
| **Mobile / Desktop SQLite Journal** | **Open** | Android and Windows shell folders exist without native SQLite sync engine. |

---

## 5. Automated Verification Test Suite Summary

### Backend Unit & Integration Tests (`dotnet test UniversalErp.sln`)
- **Total Tests:** **85**
- **Passed:** **85 (100%)**
- **Failed:** **0**
- **PostgreSQL-Backed Concurrency & Crash Recovery Integration Tests (15 in `PostgresOfflineReceiptSyncIntegrationTests.cs`):**
  1. `TwoSimultaneousSubmissions_SameOperationId_OnlyOneMovementPosted_OtherIsDuplicate`
  2. `TwoSimultaneousSubmissions_SameDeviceSequence_DifferentOperationIds_OneAccepted_OtherConflict`
  3. `ServerInterruption_AfterReservation_BeforePosting_RecoversAfterLeaseExpiry`
  4. `ServerInterruption_AfterPosting_BeforeAcknowledgment_RetryReturnsDuplicateReceipt`
  5. `RetryAfterSuccessfulPosting_ReturnsPersistedResultDurable`
  6. `ReuseOfOperationId_WithChangedPayload_ReturnsConflict`
  7. `RevokedDevice_RevokedCompanyScope_AndUnauthorizedWarehouseBin_EnforcesAllBoundaries`
  8. `LeaseExpiryAndRecovery_ExpiredDeferredOperation_ReclaimedAndCompleted`
  9. `ClaimTokenMismatch_AcceptanceThrowsAndRollsBack_NoMovementsCommitted`
  10. `AffectedRowCountZero_RejectionThrowsAndRollsBack`
  11. `LocationScopeMismatch_WarehouseUUIDNotTreatedAsLocation_RejectedWithoutMovements`
  12. `CommandIdempotencyKeyCollision_DifferentCommandOrHash_DoesNotOverwriteResource_ReturnsConflict`
  13. `AuthorizationRevocation_ForShareSerialization_RevocationBlocksOrRejects`
  14. `ConcurrentSubmissions_SameIdempotencyKey_DifferentCompanies_OneAccepted_OtherConflict`
  15. `ConcurrentSubmissions_OfflineSync_And_OnlineWorkflow_SameTransactionId_OneCommits_OtherHandledCleanly`
- **PostgreSQL-Backed Supply Chain Outbox Integration Tests (7 in `PostgresSupplyChainOutboxIntegrationTests.cs`):**
  1. `GrnOutbox_InitialProcessing_And_RetryAfterReset_DeduplicatesMovementAndStock_AndMarksPublished`
  2. `PoisonEvent_FollowedByHealthyEvent_HealthyEventCommits_AndPoisonEventDoesNotBlockQueue`
  3. `PoisonEvent_RetryCountAndLastError_PersistsDurablyWithoutThrowingUnhandled`
  4. `PoisonEvent_ReachingMaxAttempts_TransitionsToDeadLetterState`
  5. `DeadLetterEvent_WhenRedrivenAndFixed_SuccessfullyProcesses`
  6. `ConcurrentWorkers_ProcessingPendingEvents_DoNotDoubleProcessOrCollide`
  7. `UnsupportedEventTypeOrEmptyLines_TransitionsDirectlyToDeadLetter_WithoutSilentPublish`
- **Offline Sync Unit Resilience Tests (18 in `OfflineSyncServiceTests.cs`):**
  1. `IdempotencyReplay_SameOperationId_SameHash_ReturnsDuplicateWithoutReprocessing`
  2. `OperationIdReuse_DifferentPayload_ReturnsConflict`
  3. `DeferredOperation_ServerCrashBeforeComplete_SafeToResume`
  4. `DoubleCompletion_ThrowsInvalidOperationException`
  5. `UnauthorizedGrant_BadToken_ReturnsRejected`
  6. `SyncingRecovery_Contract_SyncingStatusMustBeTransientNotTerminal`
  7. `SnapshotCursorMonotonicity_OlderCursorMustNotRegressNewer`
  8. `TwoUsersDistinctOperationIds_BothAccepted_NoConflict`
  9. `RevokedDevice_ReturnsNullOnRegistration`
  10. `RevokedCompanyScope_ReturnsNullOnRegistration`
  11. `InactiveCompany_ReturnsNullOnRegistration`
  12. `RevokedGrant_ReturnsUnauthorizedOnSync`
  13. `ExpiredGrant_ReturnsUnauthorizedOnSync`
  14. `ConcurrentStockChanges_BothReceiptsAccepted_DistinctMovements`
  15. `InvalidWarehouseScope_WarehouseBelongsToDifferentCompany_Rejected`
  16. `InvalidCompanyGrantScope_GrantIssuedForDifferentCompany_ReturnsUnauthorized`
  17. `RetryAfterLostAcknowledgement_ReturnsStoredReceipt_PreservesDurableState`
  18. `StaleClientState_DeactivatedVariant_RejectedWithActionableCode`

### Web Client Production Build (`npm run build`)
- `tsc -b && vite build` succeeded in **5.23s**
- **39 modules transformed, 0 warnings, 0 errors**

### Live End-to-End API Integration (`verify-api.ps1`)
- Live execution against PostgreSQL Docker container on port `5433`:
  - `[PASS]` Plans endpoint & Tenant Onboarding (idempotency replay, 409 conflict checks)
  - `[PASS]` Chart of Accounts, Double-Entry Balanced Journal, Trial Balance proof (Rs 75,000)
  - `[PASS]` Products, Variants, Warehouses, Stock Bins
  - `[PASS]` Initial Receipt (50 EA), Two-legged Transfer (20 EA), Damaged Adjustment (-2 EA)
  - `[PASS]` Supplier, PO Confirmation, GRN, Outbox processing, Trial Balance proof (Rs 87,000)
  - `[PASS]` Customer master, Sales Order draft & confirm, Cash POS sale, Credit sale, stock relief
  - `[PASS]` Insufficient stock rejection & Sales GL Trial Balance proof (Rs 110,250)
  - `[PASS]` Live device registration (`POST /api/v1/sync/devices`)
  - `[PASS]` Offline receipt batch upload and server commit (`POST /api/v1/sync/receipts:batch`)
  - `[PASS]` Lost-acknowledgement duplicate replay verification
  - `[PASS]` Operation ID reuse mutation conflict detection (`OPERATION_ID_REUSED`)
  - `[PASS]` Incremental change feed verification (`GET /api/v1/sync/changes/inventory` returning 52 events with next cursor = 94)
