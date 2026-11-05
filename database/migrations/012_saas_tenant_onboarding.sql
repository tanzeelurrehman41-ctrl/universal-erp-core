-- Migration 012 – SaaS tenant onboarding, subscription plans, and provisioning foundation
-- Implements Phase 1 & 2 baseline according to MASTER-ERP-001.

-- ── 1. Default subscription plans and features ───────────────────────────────

INSERT INTO platform.subscription_plans (id, plan_code, name, description, status)
VALUES
  ('11111111-1111-1111-1111-111111111101'::uuid, 'starter', 'Starter Plan', 'Essential operations for small single-company businesses.', 'active'),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'professional', 'Professional Plan', 'Multi-location, offline-sync, and advanced inventory for growing businesses.', 'active'),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'enterprise', 'Enterprise Plan', 'Unrestricted multi-company, dedicated customization, and advanced compliance.', 'active')
ON CONFLICT (plan_code) DO NOTHING;

-- Starter plan features
INSERT INTO platform.plan_features (plan_id, feature_code, enabled, quantity_limit, limit_unit)
VALUES
  ('11111111-1111-1111-1111-111111111101'::uuid, 'max_companies', true, 1, 'companies'),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'max_locations', true, 2, 'locations'),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'max_users', true, 10, 'users'),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'catalog.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'inventory.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'purchasing.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'offline.sync', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'finance.basic', true, NULL, NULL)
ON CONFLICT (plan_id, feature_code) DO NOTHING;

-- Professional plan features
INSERT INTO platform.plan_features (plan_id, feature_code, enabled, quantity_limit, limit_unit)
VALUES
  ('11111111-1111-1111-1111-111111111102'::uuid, 'max_companies', true, 5, 'companies'),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'max_locations', true, 10, 'locations'),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'max_users', true, 50, 'users'),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'catalog.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'inventory.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'inventory.advanced', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'purchasing.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'offline.sync', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'finance.core', true, NULL, NULL)
ON CONFLICT (plan_id, feature_code) DO NOTHING;

-- ── 2. Tenant Provisioning Jobs table ────────────────────────────────────────

CREATE TABLE IF NOT EXISTS platform.tenant_provisioning_jobs (
  job_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
  idempotency_key uuid NOT NULL UNIQUE,
  job_type text NOT NULL CHECK (job_type IN ('create_tenant','suspend_tenant','resume_tenant','close_tenant')),
  status text NOT NULL DEFAULT 'succeeded' CHECK (status IN ('queued','running','succeeded','failed')),
  payload jsonb NOT NULL,
  result_metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  started_at timestamptz NOT NULL DEFAULT now(),
  finished_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_prov_jobs_tenant ON platform.tenant_provisioning_jobs(tenant_id, created_at DESC);

-- ── 3. Least-privilege Grants for SaaS Onboarding ────────────────────────────

GRANT USAGE ON SCHEMA platform, identity, organization, audit, "authorization" TO erp_app;

GRANT SELECT ON
  platform.subscription_plans,
  platform.plan_features
TO erp_app;

GRANT SELECT, INSERT, UPDATE ON
  platform.tenants,
  platform.subscriptions,
  platform.subscription_items,
  platform.tenant_entitlements,
  platform.tenant_provisioning_jobs
TO erp_app;

GRANT SELECT, INSERT, UPDATE ON
  organization.companies,
  organization.company_groups,
  organization.locations
TO erp_app;

GRANT SELECT, INSERT, UPDATE ON
  identity.users,
  identity.tenant_memberships,
  identity.company_memberships
TO erp_app;

GRANT SELECT, INSERT, UPDATE ON
  "authorization".roles,
  "authorization".membership_role_assignments
TO erp_app;

GRANT SELECT, INSERT ON
  audit.audit_events
TO erp_app;
