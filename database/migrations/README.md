# Database migrations

The numbered SQL files in this directory are the integrated development migration chain. The extracted package files remain reference material and are not modified by this project.

`001_platform_foundation.sql` establishes the canonical `platform.tenants`, identity, authorization, and `organization.companies` tables. `002_inventory.sql` adapts inventory to those canonical tables. `003_local_dev_bootstrap.sql` adds the RLS-protected idempotency ledger, a non-superuser API role, and a local demo tenant/company/user for Development mode. `004_offline_sync_foundation.sql` adds device/company scopes, offline grants, an operation inbox, device cursors, and a change log, reusing canonical identity records. `005_oidc_identity_and_permissions.sql` adds external subject mapping and product catalog permission keys. `006_oidc_workspace_lookup.sql` adds a restricted function for listing the signed-in user's active companies. `007_inventory_workflow_permissions.sql` seeds stock statuses and a development unit, adds inventory permission keys, and grants the API role the limited access required by the inventory workflow.

Apply pending migrations in order to the local Compose database from the repository root:

```powershell
./database/migrations/apply.ps1
```

The runner records each successful version in `public.schema_migrations` and wraps each migration with its history update in one transaction. A failed migration rolls back and can be corrected before retrying.

The local `erp_dev` database role is a PostgreSQL superuser for schema setup only. The API connects as `erp_app`, which does not bypass row-level security and has limited grants on the implemented product and inventory tables. Both passwords are for local use only. See [the integration plan](../../docs/ERP-INTEGRATION-PLAN.md) for remaining package conflicts and the production authentication work.
