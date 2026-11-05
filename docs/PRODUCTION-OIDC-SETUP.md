# Shared Sign-In Setup

The API accepts JWT access tokens from any OIDC provider that supports standard issuer discovery. The web client uses Authorization Code with PKCE. Provider credentials and tenant identity links are deployment data and must not be committed to the source tree.

## OIDC application

Register the web app as a public SPA client:

- Enable Authorization Code with PKCE. Do not create a client secret for the browser app.
- Add the exact ERP origin as the sign-in and sign-out redirect URI, such as `https://erp.example.com/`.
- Configure the provider to issue a JWT access token for the ERP API audience and grant the API scope required by the project.
- Allow the ERP origin to use the provider's discovery, authorization, and token endpoints from a browser.

Set these web build variables in the deployment environment, or in a local ignored `.env.local` file for a private setup:

```text
VITE_OIDC_AUTHORITY=https://identity.example.com
VITE_OIDC_CLIENT_ID=<public-spa-client-id>
VITE_OIDC_SCOPE=openid profile email <provider-api-scope>
```

The API also needs these environment settings:

```text
Authentication__Authority=https://identity.example.com
Authentication__Audience=<api-audience>
ConnectionStrings__UniversalErp=<least-privileged-database-connection>
```

The API validates token signature, issuer, audience, and expiry. Production refuses to start without an HTTPS authority, audience, and database connection. Product reads and writes also require current active membership and a database-assigned catalog permission.

## Link an ERP user to an OIDC identity

An administrator first provisions an active row in `identity.users`, tenant membership, and company membership. Then use the database administration account to link the exact `iss` and `sub` values from a validated provider token:

```sql
INSERT INTO identity.external_identities (issuer, subject, user_id)
SELECT 'https://identity.example.com/', 'provider-subject', id
FROM identity.users
WHERE email_normalized = lower('user@example.com')
  AND status = 'active'
ON CONFLICT (issuer, subject) DO NOTHING;
```

Do not link accounts from an unverified email address. The issuer and subject must match the API access token exactly. The current project has no user invitation or identity-linking screen; this is a controlled provisioning step for now.

## Grant inventory access

Create a tenant role, attach the intended inventory permissions, and assign it to an active company membership. This example role can maintain the catalog, locations, and receipts; create narrower roles for other staff. Substitute tenant, company, and user UUIDs:

```sql
BEGIN;

INSERT INTO "authorization".roles (tenant_id, role_code, name)
VALUES ('<tenant-uuid>', 'INVENTORY_OPERATOR', 'Inventory operator')
ON CONFLICT (tenant_id, role_code) DO NOTHING;

WITH inventory_role AS (
  SELECT id FROM "authorization".roles
  WHERE tenant_id = '<tenant-uuid>' AND role_code = 'INVENTORY_OPERATOR'
),
membership AS (
  SELECT id FROM identity.company_memberships
  WHERE tenant_id = '<tenant-uuid>'
    AND company_id = '<company-uuid>'
    AND user_id = '<user-uuid>'
    AND status = 'active'
)
INSERT INTO "authorization".role_permissions (tenant_id, role_id, permission_code, scope_type)
SELECT '<tenant-uuid>', inventory_role.id, permission.permission_code, 'company'
FROM inventory_role
CROSS JOIN (VALUES
  ('inventory.product.view'), ('inventory.product.create'),
  ('inventory.uom.view'), ('inventory.uom.create'),
  ('inventory.variant.view'), ('inventory.variant.create'),
  ('inventory.warehouse.view'), ('inventory.warehouse.create'),
  ('inventory.receipt.create'), ('inventory.receipt.post'),
  ('inventory.availability.view')
) AS permission(permission_code)
ON CONFLICT DO NOTHING;

WITH inventory_role AS (
  SELECT id FROM "authorization".roles
  WHERE tenant_id = '<tenant-uuid>' AND role_code = 'INVENTORY_OPERATOR'
),
membership AS (
  SELECT id FROM identity.company_memberships
  WHERE tenant_id = '<tenant-uuid>'
    AND company_id = '<company-uuid>'
    AND user_id = '<user-uuid>'
    AND status = 'active'
)
INSERT INTO "authorization".membership_role_assignments
  (tenant_id, membership_id, role_id, effective_from)
SELECT '<tenant-uuid>', membership.id, inventory_role.id, now()
FROM membership CROSS JOIN inventory_role
WHERE NOT EXISTS (
  SELECT 1 FROM "authorization".membership_role_assignments AS existing
  WHERE existing.tenant_id = '<tenant-uuid>'
    AND existing.membership_id = membership.id
    AND existing.role_id = inventory_role.id
    AND existing.effective_from <= now()
    AND (existing.effective_until IS NULL OR existing.effective_until > now())
);

COMMIT;
```

The app gets the signed-in user's active company list from `/api/v1/contexts`. It sends the selected tenant and company with each request; the API checks current memberships and permission assignments again before accessing catalog data.

## Current limits

- Provider registration, identity linking, and role assignment are not automated by the ERP yet.
- The web queue stores product-create payloads in browser IndexedDB without encryption. It keeps each item until the server confirms it and scopes each item to the signed-in subject and selected company.
- Authenticated product sync is implemented through the idempotent product API. Device registration, signed offline grants, cursor-based change downloads, and native client sync remain unfinished.
- Inventory units, variants, warehouses, bins, receipts, and availability are connected to the API. Receipt posting is atomic and idempotent, but the browser currently requires a live server for inventory writes; receipt commands are not yet in the offline product queue.
- A local development server is only reachable on this computer. Multi-location use needs a shared HTTPS deployment and a configured provider.
