# Implementation Backlog — First Integrated Release

## Foundation
- [ ] Pin supported SDK/toolchain versions and create CI build.
- [ ] Define tenant, company, location and user identity contracts.
- [ ] Integrate OIDC identity provider and server-side authorization.
- [x] Add provider-neutral JWT validation, live membership checks, workspace lookup, and product catalog permission checks.
- [x] Add the browser Authorization Code with PKCE flow and workspace-bound product queue.
- [ ] Configure a real OIDC issuer/audience and provision user identity links and role assignments.
- [x] Resolve authenticated tenant/company membership and set transaction-local RLS scope for API reads and writes.
- [x] Build a single ordered database migration chain for the platform and inventory schemas.
- [x] Apply canonical foundation and inventory schemas to the local development database.
- [x] Integrate offline device scopes, grants, operation inbox, cursors, and change-log schema using canonical identity records.
- [x] Write inventory receipt outbox events in the same transaction as stock posting.
- [ ] Add an outbox delivery/retry worker and complete audit event coverage.

## SaaS platform
- [ ] Tenant provisioning and onboarding.
- [ ] Plans, entitlements, subscription lifecycle and billing.
- [ ] Platform admin/customer admin separation.

## Product and inventory
- [x] Tenant-scoped product create/list API and web form, with provider-neutral authenticated access outside Development.
- [x] Create/list units of measure and untracked stock variants.
- [ ] Barcodes, product attributes, and variant options.
- [x] Create/list company warehouses and storage/receiving bins.
- [x] Post idempotent untracked-item receipts with an immutable movement, on-hand projection, and outbox event in one transaction.
- [ ] Receipt reversals, adjustments, transfers, reservations, lot/serial tracking, and valuation.

## Purchasing
- [ ] Supplier records.
- [ ] Requisitions, approvals and purchase orders.
- [ ] Goods receipts and quality inspection.
- [ ] Invoice matching and supplier returns.

## Finance
- [ ] Chart of accounts.
- [ ] Journal draft/approve/post/reverse.
- [ ] AP/AR and payment allocation.
- [ ] Trial balance and financial statements.
- [ ] Period close and reconciliation.

## Clients and offline
- [x] Development web queue and sync batch for product creation with stable retries and retained pending/conflict outcomes.
- [x] Encrypt the browser product queue and migrate existing plaintext rows only after verification.
- [x] Write product and inventory receipt changes inside their business transactions and expose permission-checked paged change feeds with commit-ordered company cursors.
- [x] Poll product and inventory change feeds; atomically store refreshed encrypted snapshots with local per-company feed cursors.
- [x] Allow a conflicted or rejected product to be corrected as a new operation while keeping the original queued record.
- [x] Web OIDC sign-in and active tenant/company selection.
- [ ] Windows sign-in and encrypted local data.
- [ ] Android sign-in and encrypted local data.
- [ ] Durable offline operation queue; retain work until server acknowledgment and expose conflicts without overwriting either version.
- [x] Add encrypted local-first queueing for inventory receipts with stable idempotency IDs, retained outcomes, and cached inventory snapshots.
- [ ] Extend the queue to other business commands and integrate receipts with server-issued device grants and the sync operation inbox.
- [ ] Device registration and offline capability grants.
- [ ] Authenticated grant issuance, device enrollment, and sync API integration for the offline sync schema.
- [ ] Idempotent sync inbox and conflict workflow.

## Quality and release
- [ ] Unit, integration and end-to-end tests.
- [ ] Security review and penetration test.
- [ ] Performance and concurrency tests.
- [ ] Backup/restore and disaster recovery rehearsal.
- [ ] Pilot acceptance and operational runbooks.
