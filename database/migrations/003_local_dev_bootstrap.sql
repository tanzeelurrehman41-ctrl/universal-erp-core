-- Local-only bootstrap. Do not apply this migration to a shared environment.
-- The API uses a non-superuser role and a fixed Development tenant context.

CREATE TABLE erp_inventory.command_idempotency (
  tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
  idempotency_key uuid NOT NULL,
  command_code text NOT NULL,
  request_hash char(64) NOT NULL,
  resource_id uuid,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, idempotency_key)
);
ALTER TABLE erp_inventory.command_idempotency ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_inventory.command_idempotency FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_scope ON erp_inventory.command_idempotency
  USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
  WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

CREATE ROLE erp_app
  WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT
  PASSWORD 'local_erp_app_change_me';

GRANT CONNECT ON DATABASE universal_erp TO erp_app;
GRANT USAGE ON SCHEMA erp_inventory TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.product TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_inventory.command_idempotency TO erp_app;

INSERT INTO platform.tenants (
  id, tenant_code, display_name, status
) VALUES (
  '00000000-0000-4000-8000-000000000001',
  'LOCAL-DEMO',
  'Local Development Tenant',
  'active'
);

INSERT INTO identity.users (
  id, email_normalized, display_name, status
) VALUES (
  '00000000-0000-4000-8000-000000000003',
  'local.dev@universal-erp.invalid',
  'Local Development User',
  'active'
);

INSERT INTO identity.tenant_memberships (
  id, tenant_id, user_id, status, joined_at
) VALUES (
  '00000000-0000-4000-8000-000000000004',
  '00000000-0000-4000-8000-000000000001',
  '00000000-0000-4000-8000-000000000003',
  'active',
  now()
);

INSERT INTO organization.companies (
  id, tenant_id, company_code, name, country_code,
  base_currency_code, timezone
) VALUES (
  '00000000-0000-4000-8000-000000000002',
  '00000000-0000-4000-8000-000000000001',
  'LOCAL',
  'Local Development Company',
  'PK',
  'PKR',
  'Asia/Karachi'
);

INSERT INTO identity.company_memberships (
  id, tenant_id, company_id, user_id, status
) VALUES (
  '00000000-0000-4000-8000-000000000005',
  '00000000-0000-4000-8000-000000000001',
  '00000000-0000-4000-8000-000000000002',
  '00000000-0000-4000-8000-000000000003',
  'active'
);
