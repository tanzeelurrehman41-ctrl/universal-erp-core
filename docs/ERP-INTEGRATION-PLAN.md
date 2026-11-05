# Universal ERP Integration Plan

## Architecture

Use the core application as a modular monolith: web, Windows, and Android clients call one authenticated API; PostgreSQL is the system of record. Modules own their tables and expose application services or versioned events. Offline clients synchronize idempotent operations. Reporting reads curated, permission-filtered projections.

## Nine extracted packages

| Package | Responsibility | Integration boundary |
| --- | --- | --- |
| `universal-erp-architecture-foundation` | Tenant, company, location, identity, and platform baseline | Canonical identifiers, schemas, and foundation migrations |
| `universal-erp-core` | API, application/domain/infrastructure layers, and client shells | Runtime host and shared contracts |
| `universal-erp-inventory-milestone` | Catalog, units, warehouses, stock movements, reservations, valuation | Inventory-owned commands, tables, and events |
| `purchasing-module-files` | Suppliers, requisitions, purchase orders, receipts, inspections, invoice matching | Purchasing-owned workflows; receipt events feed inventory |
| `erp-finance` | Ledger, journals, AP/AR, payments, currencies, tax-code foundations | Finance-owned posting service; consumes approved source events |
| `erp-security` | Memberships, roles, permissions, devices, sessions, approvals, audit | Server-side identity and authorization for every module |
| `erp-offline` | Device grants, operation inbox, cursors, retry and conflict protocol | Idempotent sync API and device-scoped authorization |
| `erp-reporting` | Approved datasets, dashboards, report runs, exports and schedules | Read-only projections populated from module events |
| `saas9` | Plans, entitlements, tenant provisioning, subscriptions and billing | Control-plane services separated from tenant business ledgers |

## Required integration order

1. Reconcile tenant/company identifiers and schemas against the foundation migration. The integrated inventory migration now references `platform.tenants` and `organization.companies`.
2. Maintain one ordered, versioned migration chain. Migrations 001–009 establish platform, inventory, local development, offline sync, OIDC identity mapping and lookup, inventory workflow permissions, ordered per-company product/receipt change feeds, and their paging index.
3. Configure a production OIDC issuer and audience, provision external identity links, and add the remaining user/device administration and audit workflows. The API now validates bearer tokens and resolves active tenant/company membership and catalog permissions; the local fixed tenant remains Development-only.
4. The product catalog, OIDC authorization boundary, units, stock variants, warehouses, bins, and atomic idempotent receipts are integrated for Development and OIDC deployments. The web client stores product and receipt commands and inventory snapshots in browser-local AES-GCM encrypted IndexedDB journals, then retries operations with stable IDs. Product creation fans its tenant-wide change entry into every active company feed, while receipts append to their own company feed; both writes share the business transaction. Permission-checked paged feed endpoints use commit-ordered company cursors. The web client polls both feeds, refreshes full snapshots after changes, and writes the snapshot and local cursors atomically. Extend this slice with server-issued device grants, operation-inbox integration, event-level merge, receipt reversals, inventory adjustments, and concurrency validation.
5. Connect purchasing receipts to inventory through an idempotent event; connect supplier invoices to finance only after matching rules exist.
6. Add finance posting and reversals, then SaaS provisioning and entitlements.
7. Connect the authenticated local queues to device-granted operation-inbox sync, add server-persisted device cursors and event-level merge, and complete explicit conflict resolution; then add reporting projections and exports.
8. Complete client workflows, operational monitoring, recovery, security, and release validation.

## Offline synchronization data-safety rules

- Persist each offline action to the device queue before showing it as saved. Keep its operation ID, device sequence, payload, and hash stable across retries.
- Upload operations idempotently. A repeated ID with the same payload returns the recorded result; the same ID with different data is rejected.
- Keep queued or conflicted work on the device. Mark it accepted only after the server confirms the transaction committed; never silently discard it on logout, revocation, or sync failure.
- Return explicit `accepted`, `duplicate`, `conflict`, `rejected`, or `deferred` outcomes. Preserve both local and server versions for conflicts; do not use silent last-write-wins for inventory or finance.
- Apply downloaded changes and save the new cursor in one local transaction. If applying the page fails, retry that page without advancing the cursor.
- A device that is lost or destroyed before its first successful upload has no server copy of its unsynced work. Encrypted local storage and a protected device-backup/recovery plan are required to reduce that risk.

The Development web product queue implements the local-write-first, stable-ID retry, and retained-conflict parts for product creation only. Inventory master data requires the server to be online. Product and receipt commands use browser-local encrypted journals and stable operation IDs, with idempotent server retries and retained local outcomes. The existing plaintext product queue is encrypted and verified during migration before its old rows are cleared. Product creation and receipts write company-scoped change records in their business transactions, and the API exposes permission-checked paged feeds using commit-ordered company cursors. The web client polls those feeds, refreshes full snapshots, and persists each snapshot with its cursors atomically; it does not merge individual event payloads. A conflicted or rejected product can be copied into the form for correction; saving creates a new operation while preserving the original. Server-issued device grants, operation-inbox integration, native clients, cross-device recovery, and event-level conflict handling remain to be built.

## Migration blockers

- The foundation migration defines tenant/company objects in `platform` and `organization` schemas. Inventory now uses those canonical objects; purchasing and other package SQL still need the same reconciliation.
- The foundation already owns users, memberships, devices, roles, and audit records under `identity`, `authorization`, and `audit`. The security package defines overlapping `security.*` tables, so those must be reconciled individually rather than applied wholesale. Offline sync migration 004 reuses the foundation device and membership IDs.
- OIDC API and browser flows are provider-neutral, but a deployment still needs provider configuration, provisioned identity links, and permission assignments. The UI does not yet provide user invitations or role administration.
- Purchasing SQL refers to the company key as `company_id` in foreign keys, while other starter schemas use `(tenant_id, id)`. These keys must be normalized before migrations can run together.
- The SaaS package creates control-plane tenant/company records. Decide whether these are authoritative platform records or projections linked to the foundation IDs; do not create two independent tenant identities.
- The milestone SQL is architecture starter material. Do not concatenate or apply it blindly; add dependency ordering, migration ownership, rollback strategy, and tenant-isolation validation first.

## Current implementation status

- The API supports provider-neutral JWT validation outside Development, resolves active memberships, and checks product/inventory permissions in PostgreSQL. Development retains the local idempotent product API and batch sync endpoint. OIDC workspace lookup, browser Authorization Code with PKCE, and authenticated idempotent product uploads are implemented. Inventory supports units, untracked stock variants, warehouses, bins, immutable receipts, atomic on-hand projection updates, outbox events, and same-transaction change-log writes. Paged product and receipt change feeds are available behind the relevant read permissions. Device enrollment, signed grants, inbox processing, client cursor application, and user/role administration are pending.
- The React client saves product-create actions in an AES-GCM encrypted IndexedDB store before sync, binds them to the signed-in user and selected company, retains pending/conflicted/rejected entries, and retries stable operation IDs. Inventory master-data writes require a live API connection. Receipts are written to a browser-local AES-GCM encrypted journal before upload, use stable operation IDs for idempotent retries, and remain in the journal after acknowledgment; encrypted inventory snapshots support offline viewing. The old plaintext product queue is only cleared after encrypted records are verified. The client polls product and inventory change feeds, refreshes full snapshots, and commits each snapshot with its local cursors atomically; it does not merge event payloads. The encryption key is browser-local, persistent browser storage is not guaranteed, and these queues do not yet use server-issued device grants or the `sync.operation_inbox` protocol. Tokens live in session storage, and Windows and Android are still starter clients. This implementation reduces accidental overwrite and retry-duplication risk but cannot guarantee zero data loss if local storage or the device is lost before upload.
- The milestone packages primarily contain architecture, contracts, validation plans, and SQL starters rather than runnable module implementations.
- Local startup requires a supported .NET SDK, installed web dependencies, and a running Docker engine for PostgreSQL. The compose file exposes its development database on `127.0.0.1:5433` to avoid conflicts with a host PostgreSQL service; readiness confirms connectivity but does not apply migrations automatically.

## First release acceptance criteria

- A user can authenticate and select an authorized tenant/company.
- A product and warehouse can be created through the API with server-derived tenant scope.
- A receipt posts an immutable stock movement exactly once and updates a rebuildable balance projection.
- Unauthorized cross-tenant reads and writes are rejected and tested.
- The web UI displays persisted data from the API, and the API health check and database health are observable.
- Migrations apply from an empty development database in order and can be restored from backup.
