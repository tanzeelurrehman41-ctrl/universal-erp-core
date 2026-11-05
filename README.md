# Universal ERP — Core Application Foundation

**Milestone 8 · Integrated inventory starter · Not production-ready**

This repository is the implementation starting point for the Universal ERP. It uses a modular-monolith backend and separate client applications.

## Repository layout

```text
src/
  backend/
    UniversalErp.sln
    src/UniversalErp.Api/
    src/UniversalErp.Domain/
    src/UniversalErp.Application/
    src/UniversalErp.Infrastructure/
    src/Modules/                 # future module projects
    tests/                       # automated tests
  web/
    src/
  windows/
  android/
contracts/
  openapi/
  events/
database/
  migrations/
deploy/
  docker/
docs/
```

## Technology decisions

- Backend: ASP.NET Core / C#.
- Database: PostgreSQL.
- Data access: Npgsql repositories with transaction-local tenant scope and PostgreSQL RLS.
- Web: React + TypeScript.
- Windows: .NET desktop client (WPF starter).
- Android: Kotlin starter.
- API: REST + OpenAPI.
- Local development: Docker Compose.
- Tests: xUnit backend tests and frontend test setup to be added.

The backend targets .NET 10 and Npgsql 10.0.0. Pin the SDK in CI and select supported Node, Android Gradle Plugin and Kotlin versions before implementation freeze.

## Architecture rules

1. `Domain` contains business concepts and invariants; it does not reference EF Core, HTTP or UI.
2. `Application` contains use cases, policies and ports/interfaces.
3. `Infrastructure` implements persistence, messaging and external integrations.
4. `Api` handles transport, authentication middleware, validation and dependency wiring.
5. Modules communicate through application interfaces or explicit integration events. Do not write directly into another module's tables.
6. Tenant/company context comes from authenticated server identity, never from an untrusted request body.
7. Posted finance and inventory records are immutable; corrections use reversal/adjustment workflows.
8. Database RLS is defense in depth, not a replacement for application authorization.
9. Never run the application using a PostgreSQL superuser or table-owner role.
10. All mutating API requests need authorization, validation, audit and idempotency where retries can duplicate effects.

## Run the local database

From `deploy/docker`:

```bash
docker compose up -d postgres
```

The Compose file is for local development only. It listens on `127.0.0.1:5433` to avoid colliding with PostgreSQL already installed on the host. It uses a local development password; change it before any shared environment and never reuse it in production.

## Run the API

Install a supported .NET SDK, then:

```bash
cd src/backend
dotnet restore UniversalErp.sln
```

In PowerShell, set the local Compose database connection and start the API:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__UniversalErp = "Host=127.0.0.1;Port=5433;Database=universal_erp;Username=erp_app;Password=local_erp_app_change_me;Timeout=3"
dotnet run --project src/UniversalErp.Api -- --urls http://127.0.0.1:5080
```

Apply the local schema and seed with `./database/migrations/apply.ps1` from the repository root. Migrations 001–009 establish the platform, inventory, development, offline sync, OIDC identity/permissions, workspace lookup, inventory workflow permissions, the ordered change feed, and its paging index. `GET /health` checks PostgreSQL connectivity, `GET /health/live` checks that the API process is responding, and `GET /api/modules` returns module status.

The inventory screen supports products, units of measure, stock variants, company warehouses and bins, receipts, and on-hand balances. A receipt uses one idempotency key and one database transaction for its immutable movement, stock balance projection, idempotency result, and outbox event. The API retries the original receipt result when the same key and request are sent again, and rejects key reuse with different content. The first receipt workflow supports untracked items in their base unit only.

In Development mode, `GET` and `POST /api/v1/products` provide a local product catalog workflow. The browser saves product-create actions to an AES-GCM encrypted IndexedDB store before upload and retries stable operation IDs. In an OIDC-configured build, users sign in with Authorization Code + PKCE, select an active company, and queued products upload to the authenticated, idempotent product API. Conflicts remain on the device; correcting one creates a new operation and preserves the original. The local API batch endpoint remains Development-only. Production builds cache the app shell for offline reopening, while API responses are never cached. Inventory setup requires a live API connection. Receipt commands are encrypted in a browser-local IndexedDB journal before upload and retried with the same operation ID; the inventory screen can use the last encrypted inventory snapshot while offline. The server applies a confirmed receipt as an immutable movement, balance update, idempotency result, outbox event, and change-feed entry in one transaction.

The server exposes paged product and inventory-movement change feeds at `/api/v1/sync/changes/products` and `/api/v1/sync/changes/inventory`. Each page uses a per-company cursor allocated under a transaction lock, so committed changes cannot be skipped because a later database sequence committed first. Tenant-wide product changes are written into each active company’s feed; inventory movements remain company-scoped. The web client polls both feeds, refreshes the full server snapshot when it finds changes, and stores the encrypted snapshot and local cursors in one IndexedDB transaction. If fetching or saving is interrupted, it leaves the cursor behind and replays those changes safely. It currently downloads full snapshots instead of applying individual event payloads.

The API can validate OIDC bearer tokens and active memberships/permissions, but shared sign-in is not ready until a provider is configured and user identities and roles are provisioned. See [the OIDC setup guide](docs/PRODUCTION-OIDC-SETUP.md). Product and receipt queues plus inventory snapshots use browser-local AES-GCM encryption, with the existing plaintext product queue migrated and verified before its old rows are cleared. The encryption key is stored in that browser's IndexedDB and is not a server-issued device key. Device enrollment/grants, server operation-inbox integration, server-persisted device cursors, event-by-event merge/conflict handling, and cross-device recovery remain unfinished. Persistent browser storage was not granted in the current browser, so pending work can still be removed by browser storage eviction; a device can also be lost before queued work reaches the server. This system cannot promise zero data loss. Do not store real company data in the local prototype. Development credentials must never be reused in a shared environment.

## Run the web starter

```bash
cd src/web
npm install
npm run dev
```

The Vite development server proxies `/health` and `/api` to the API at `http://127.0.0.1:5080`.

The web app includes a Development-only product queue and an OIDC-ready company workspace flow. Product and receipt commands save to encrypted browser-local journals first and sync when authorized and online. The API records and pages product and receipt changes; the client polls them and refreshes full snapshots, saving the snapshot and local cursors atomically. Authenticated device enrollment and grants, server-side operation inbox and device cursors, event-by-event merge, complete conflict handling, the remaining ERP workflows, and deployment configuration remain unfinished.

## Windows and Android

- Windows starter is a minimal WPF application. Open `src/windows/UniversalErp.Windows.sln` in Visual Studio with the appropriate .NET desktop workload.
- Android starter is a minimal Kotlin/Gradle project. Open `src/android` in Android Studio and allow Gradle sync. Configure the Android SDK and supported plugin versions locally.

## Next implementation order

1. Configure an HTTPS OIDC provider and shared API deployment; provision external identities and catalog roles.
2. Add tenant/company onboarding and subscription entitlements.
3. Complete inventory adjustments, transfers, reservations, lot/serial tracking, and valuation; harden the receipt queue with device grants and cross-device recovery.
4. Implement authenticated device enrollment, grants, the server operation inbox, and transactional change-download cursors.
5. Add purchasing, then accounting posting and reconciliation.
6. Add reporting read models and exports.
7. Add protected local storage to the Windows and Android clients, then complete backup/restore, security, and release validation.

## Security and readiness warning

This project is not ready for public deployment or real business records. The API security path requires configured OIDC, manually provisioned identities and permissions, and a hosted HTTPS boundary. Server-issued device grants, operation-inbox and cursor integration, complete audit coverage, rate limiting, production secret management, monitoring, and backup/restore automation are still incomplete. Browser-local encryption does not protect against loss of the device or browser storage and cannot guarantee zero data loss.
