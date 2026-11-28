-- Migration 037: SaaS Platform, Tenant Provisioning, Entitlements & Subscription Management
-- Implements Phase 14 enterprise conglomerate multi-tenant SaaS infrastructure.

BEGIN;

-- ── 1. Update platform.tenants with expanded lifecycle states & metadata ──

ALTER TABLE platform.tenants DROP CONSTRAINT IF EXISTS tenants_status_check;
ALTER TABLE platform.tenants ADD CONSTRAINT tenants_status_check 
  CHECK (status = ANY (ARRAY['pending'::text, 'provisioning'::text, 'active'::text, 'suspended'::text, 'cancelled'::text, 'closed'::text, 'archived'::text]));

ALTER TABLE platform.tenants
  ADD COLUMN IF NOT EXISTS suspended_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS suspension_reason TEXT,
  ADD COLUMN IF NOT EXISTS archived_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS metadata JSONB NOT NULL DEFAULT '{}'::jsonb;

-- ── 2. Update platform.subscription_plans & effective-dated pricing ────────

ALTER TABLE platform.subscription_plans
  ADD COLUMN IF NOT EXISTS tier_rank INT NOT NULL DEFAULT 1,
  ADD COLUMN IF NOT EXISTS is_public BOOLEAN NOT NULL DEFAULT true;

-- Ensure Free, Starter, Professional, Enterprise plans exist
INSERT INTO platform.subscription_plans (id, plan_code, name, description, status, tier_rank)
VALUES
  ('11111111-1111-1111-1111-111111111100'::uuid, 'free', 'Free Plan', 'Basic evaluation plan for micro organizations.', 'active', 0),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'starter', 'Starter Plan', 'Essential operations for small single-company businesses.', 'active', 1),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'professional', 'Professional Plan', 'Multi-location, offline-sync, and advanced inventory for growing businesses.', 'active', 2),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'enterprise', 'Enterprise Plan', 'Unrestricted multi-company, intercompany consolidation, and advanced compliance.', 'active', 3)
ON CONFLICT (plan_code) DO UPDATE
SET status = 'active', tier_rank = EXCLUDED.tier_rank;

-- Effective-dated pricing table
CREATE TABLE IF NOT EXISTS platform.plan_pricing (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  plan_id UUID NOT NULL REFERENCES platform.subscription_plans(id) ON DELETE CASCADE,
  billing_interval TEXT NOT NULL CHECK (billing_interval IN ('month', 'year')),
  currency_code CHAR(3) NOT NULL DEFAULT 'PKR',
  base_price NUMERIC(14,2) NOT NULL DEFAULT 0.00 CHECK (base_price >= 0),
  effective_from TIMESTAMPTZ NOT NULL DEFAULT now(),
  effective_until TIMESTAMPTZ,
  is_active BOOLEAN NOT NULL DEFAULT true,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT plan_pricing_period_check CHECK (effective_until IS NULL OR effective_until > effective_from)
);

CREATE INDEX IF NOT EXISTS ix_plan_pricing_lookup 
  ON platform.plan_pricing(plan_id, billing_interval, currency_code, is_active);

-- Seed plan pricing
INSERT INTO platform.plan_pricing (plan_id, billing_interval, currency_code, base_price, effective_from)
VALUES
  ('11111111-1111-1111-1111-111111111100'::uuid, 'month', 'PKR', 0.00, '2026-01-01 00:00:00+00'),
  ('11111111-1111-1111-1111-111111111100'::uuid, 'year',  'PKR', 0.00, '2026-01-01 00:00:00+00'),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'month', 'PKR', 15000.00, '2026-01-01 00:00:00+00'),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'year',  'PKR', 150000.00, '2026-01-01 00:00:00+00'),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'month', 'PKR', 45000.00, '2026-01-01 00:00:00+00'),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'year',  'PKR', 450000.00, '2026-01-01 00:00:00+00'),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'month', 'PKR', 120000.00, '2026-01-01 00:00:00+00'),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'year',  'PKR', 1200000.00, '2026-01-01 00:00:00+00')
ON CONFLICT DO NOTHING;

-- ── 3. Seed Plan Features and Limits ────────────────────────────────────────

-- Free plan features
INSERT INTO platform.plan_features (plan_id, feature_code, enabled, quantity_limit, limit_unit)
VALUES
  ('11111111-1111-1111-1111-111111111100'::uuid, 'max_users', true, 2, 'users'),
  ('11111111-1111-1111-1111-111111111100'::uuid, 'max_companies', true, 1, 'companies'),
  ('11111111-1111-1111-1111-111111111100'::uuid, 'max_branches', true, 1, 'branches'),
  ('11111111-1111-1111-1111-111111111100'::uuid, 'max_monthly_transactions', true, 100, 'transactions'),
  ('11111111-1111-1111-1111-111111111100'::uuid, 'max_storage_mb', true, 500, 'megabytes'),
  ('11111111-1111-1111-1111-111111111100'::uuid, 'max_api_calls_per_month', true, 1000, 'calls'),
  ('11111111-1111-1111-1111-111111111100'::uuid, 'max_integrations', true, 0, 'integrations'),
  ('11111111-1111-1111-1111-111111111100'::uuid, 'catalog.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111100'::uuid, 'inventory.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111100'::uuid, 'sales.core', true, NULL, NULL)
ON CONFLICT (plan_id, feature_code) DO UPDATE
SET enabled = EXCLUDED.enabled, quantity_limit = EXCLUDED.quantity_limit, limit_unit = EXCLUDED.limit_unit;

-- Starter plan additional limits
INSERT INTO platform.plan_features (plan_id, feature_code, enabled, quantity_limit, limit_unit)
VALUES
  ('11111111-1111-1111-1111-111111111101'::uuid, 'max_branches', true, 2, 'branches'),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'max_monthly_transactions', true, 1000, 'transactions'),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'max_storage_mb', true, 2048, 'megabytes'),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'max_api_calls_per_month', true, 10000, 'calls'),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'max_integrations', true, 2, 'integrations'),
  ('11111111-1111-1111-1111-111111111101'::uuid, 'sales.core', true, NULL, NULL)
ON CONFLICT (plan_id, feature_code) DO UPDATE
SET enabled = EXCLUDED.enabled, quantity_limit = EXCLUDED.quantity_limit, limit_unit = EXCLUDED.limit_unit;

-- Professional plan additional limits & modules
INSERT INTO platform.plan_features (plan_id, feature_code, enabled, quantity_limit, limit_unit)
VALUES
  ('11111111-1111-1111-1111-111111111102'::uuid, 'max_branches', true, 10, 'branches'),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'max_monthly_transactions', true, 10000, 'transactions'),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'max_storage_mb', true, 10240, 'megabytes'),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'max_api_calls_per_month', true, 50000, 'calls'),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'max_integrations', true, 5, 'integrations'),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'sales.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'manufacturing.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'warehouse.advanced', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'hr.payroll', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'analytics.bi', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111102'::uuid, 'integrations.webhooks', true, NULL, NULL)
ON CONFLICT (plan_id, feature_code) DO UPDATE
SET enabled = EXCLUDED.enabled, quantity_limit = EXCLUDED.quantity_limit, limit_unit = EXCLUDED.limit_unit;

-- Enterprise plan unlimited/high limits & consolidation
INSERT INTO platform.plan_features (plan_id, feature_code, enabled, quantity_limit, limit_unit)
VALUES
  ('11111111-1111-1111-1111-111111111103'::uuid, 'max_users', true, 500, 'users'),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'max_companies', true, 50, 'companies'),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'max_locations', true, 100, 'locations'),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'max_branches', true, 100, 'branches'),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'max_monthly_transactions', true, 100000, 'transactions'),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'max_storage_mb', true, 102400, 'megabytes'),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'max_api_calls_per_month', true, 500000, 'calls'),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'max_integrations', true, 25, 'integrations'),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'catalog.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'inventory.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'inventory.advanced', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'purchasing.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'sales.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'finance.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'manufacturing.core', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'warehouse.advanced', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'hr.payroll', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'analytics.bi', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'offline.sync', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'integrations.webhooks', true, NULL, NULL),
  ('11111111-1111-1111-1111-111111111103'::uuid, 'consolidation.core', true, NULL, NULL)
ON CONFLICT (plan_id, feature_code) DO UPDATE
SET enabled = EXCLUDED.enabled, quantity_limit = EXCLUDED.quantity_limit, limit_unit = EXCLUDED.limit_unit;

-- ── 4. Update platform.subscriptions with pricing & overage configuration ─

ALTER TABLE platform.subscriptions DROP CONSTRAINT IF EXISTS subscriptions_status_check;
ALTER TABLE platform.subscriptions ADD CONSTRAINT subscriptions_status_check 
  CHECK (status = ANY (ARRAY['trial'::text, 'active'::text, 'past_due'::text, 'grace'::text, 'grace_period'::text, 'suspended'::text, 'cancelled'::text, 'expired'::text]));

ALTER TABLE platform.subscriptions
  ADD COLUMN IF NOT EXISTS current_price NUMERIC(14,2) NOT NULL DEFAULT 0.00,
  ADD COLUMN IF NOT EXISTS overage_policy TEXT NOT NULL DEFAULT 'block' CHECK (overage_policy IN ('block', 'allow_with_rate', 'soft_warning')),
  ADD COLUMN IF NOT EXISTS overage_rate NUMERIC(14,4) NOT NULL DEFAULT 0.00,
  ADD COLUMN IF NOT EXISTS last_billed_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS next_billing_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS proration_credit NUMERIC(14,2) NOT NULL DEFAULT 0.00;

-- ── 5. Administrative Invitations ───────────────────────────────────────────

CREATE TABLE IF NOT EXISTS platform.tenant_invitations (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES platform.tenants(id) ON DELETE CASCADE,
  email TEXT NOT NULL,
  full_name TEXT NOT NULL,
  role_code TEXT NOT NULL DEFAULT 'tenant_admin',
  invitation_token_hash TEXT NOT NULL,
  expires_at TIMESTAMPTZ NOT NULL,
  status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'accepted', 'revoked', 'expired')),
  invited_by UUID,
  accepted_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_tenant_invitations_lookup 
  ON platform.tenant_invitations(tenant_id, email, status);

ALTER TABLE platform.tenant_invitations ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.tenant_invitations FORCE ROW LEVEL SECURITY;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'tenant_invitations') THEN
    CREATE POLICY tenant_isolation ON platform.tenant_invitations
      USING (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
      WITH CHECK (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
  END IF;
END $$;

-- ── 6. Tenant Usage Aggregations ────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS platform.tenant_usage_aggregations (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES platform.tenants(id) ON DELETE CASCADE,
  metric_code TEXT NOT NULL,
  period_start TIMESTAMPTZ NOT NULL,
  period_end TIMESTAMPTZ NOT NULL,
  current_quantity NUMERIC(18,4) NOT NULL DEFAULT 0 CHECK (current_quantity >= 0),
  quantity_limit NUMERIC(18,4),
  is_exceeded BOOLEAN NOT NULL DEFAULT false,
  warning_threshold_pct INT NOT NULL DEFAULT 80,
  warning_issued BOOLEAN NOT NULL DEFAULT false,
  last_aggregated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT uq_tenant_metric_period UNIQUE (tenant_id, metric_code, period_start, period_end)
);

CREATE INDEX IF NOT EXISTS ix_usage_aggregations_metric 
  ON platform.tenant_usage_aggregations(tenant_id, metric_code, period_end);

ALTER TABLE platform.tenant_usage_aggregations ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.tenant_usage_aggregations FORCE ROW LEVEL SECURITY;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'tenant_usage_aggregations') THEN
    CREATE POLICY tenant_isolation ON platform.tenant_usage_aggregations
      USING (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
      WITH CHECK (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
  END IF;
END $$;

-- ── 7. Jurisdictional Tax Rules for Subscription Invoicing ──────────────────

CREATE TABLE IF NOT EXISTS platform.billing_tax_rules (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  country_code CHAR(2) NOT NULL DEFAULT 'PK',
  province_state TEXT NOT NULL,
  tax_name TEXT NOT NULL,
  rate_percentage NUMERIC(6,4) NOT NULL CHECK (rate_percentage >= 0),
  effective_from TIMESTAMPTZ NOT NULL DEFAULT now(),
  effective_until TIMESTAMPTZ,
  is_active BOOLEAN NOT NULL DEFAULT true,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT billing_tax_period_check CHECK (effective_until IS NULL OR effective_until > effective_from)
);

CREATE INDEX IF NOT EXISTS ix_billing_tax_rules_lookup 
  ON platform.billing_tax_rules(country_code, province_state, is_active);

-- Seed Pakistani Sales Tax on Services
INSERT INTO platform.billing_tax_rules (country_code, province_state, tax_name, rate_percentage)
VALUES
  ('PK', 'FEDERAL', 'ICT Sales Tax on IT/SaaS Services', 15.0000),
  ('PK', 'PUNJAB', 'PRA Punjab Sales Tax on Services', 16.0000),
  ('PK', 'SINDH', 'SRB Sindh Sales Tax on Services', 13.0000),
  ('PK', 'KP', 'KPRA Khyber Pakhtunkhwa Sales Tax on Services', 15.0000),
  ('PK', 'BALOCHISTAN', 'BRA Balochistan Sales Tax on Services', 15.0000)
ON CONFLICT DO NOTHING;

-- ── 8. Tenant Billing Credits & Discounts ───────────────────────────────────

CREATE TABLE IF NOT EXISTS platform.tenant_billing_credits (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES platform.tenants(id) ON DELETE CASCADE,
  credit_code TEXT NOT NULL,
  description TEXT NOT NULL,
  initial_amount NUMERIC(14,2) NOT NULL CHECK (initial_amount > 0),
  remaining_balance NUMERIC(14,2) NOT NULL CHECK (remaining_balance >= 0),
  currency_code CHAR(3) NOT NULL DEFAULT 'PKR',
  status TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'exhausted', 'expired', 'revoked')),
  expires_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_tenant_billing_credits 
  ON platform.tenant_billing_credits(tenant_id, status);

ALTER TABLE platform.tenant_billing_credits ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.tenant_billing_credits FORCE ROW LEVEL SECURITY;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'tenant_billing_credits') THEN
    CREATE POLICY tenant_isolation ON platform.tenant_billing_credits
      USING (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
      WITH CHECK (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
  END IF;
END $$;

-- ── 9. Subscription Invoices & Invoice Lines ────────────────────────────────

CREATE TABLE IF NOT EXISTS platform.subscription_invoices (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES platform.tenants(id) ON DELETE CASCADE,
  subscription_id UUID NOT NULL REFERENCES platform.subscriptions(id) ON DELETE CASCADE,
  invoice_number TEXT NOT NULL UNIQUE,
  billing_reason TEXT NOT NULL DEFAULT 'subscription_cycle' CHECK (billing_reason IN ('subscription_cycle', 'plan_upgrade', 'overage', 'manual')),
  period_start TIMESTAMPTZ NOT NULL,
  period_end TIMESTAMPTZ NOT NULL,
  subtotal NUMERIC(14,2) NOT NULL DEFAULT 0.00,
  discount_amount NUMERIC(14,2) NOT NULL DEFAULT 0.00,
  tax_amount NUMERIC(14,2) NOT NULL DEFAULT 0.00,
  total_amount NUMERIC(14,2) NOT NULL DEFAULT 0.00,
  paid_amount NUMERIC(14,2) NOT NULL DEFAULT 0.00,
  currency_code CHAR(3) NOT NULL DEFAULT 'PKR',
  status TEXT NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'open', 'paid', 'past_due', 'void', 'uncollectible')),
  due_date TIMESTAMPTZ NOT NULL,
  paid_at TIMESTAMPTZ,
  hosted_invoice_url TEXT,
  pdf_download_url TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_subscription_invoices_tenant 
  ON platform.subscription_invoices(tenant_id, status, created_at DESC);

ALTER TABLE platform.subscription_invoices ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.subscription_invoices FORCE ROW LEVEL SECURITY;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'subscription_invoices') THEN
    CREATE POLICY tenant_isolation ON platform.subscription_invoices
      USING (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
      WITH CHECK (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
  END IF;
END $$;

CREATE TABLE IF NOT EXISTS platform.subscription_invoice_lines (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  invoice_id UUID NOT NULL REFERENCES platform.subscription_invoices(id) ON DELETE CASCADE,
  line_number INT NOT NULL,
  description TEXT NOT NULL,
  line_type TEXT NOT NULL DEFAULT 'base_plan' CHECK (line_type IN ('base_plan', 'proration', 'overage', 'discount', 'tax', 'credit')),
  quantity NUMERIC(14,4) NOT NULL DEFAULT 1 CHECK (quantity > 0),
  unit_price NUMERIC(14,2) NOT NULL DEFAULT 0.00,
  amount NUMERIC(14,2) NOT NULL DEFAULT 0.00,
  CONSTRAINT uq_invoice_line_number UNIQUE (invoice_id, line_number)
);

CREATE INDEX IF NOT EXISTS ix_invoice_lines_invoice 
  ON platform.subscription_invoice_lines(invoice_id, line_number);

-- ── 10. Subscription Payments (Sandbox/Test Provider Abstraction) ────────────

CREATE TABLE IF NOT EXISTS platform.subscription_payments (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES platform.tenants(id) ON DELETE CASCADE,
  invoice_id UUID REFERENCES platform.subscription_invoices(id) ON DELETE SET NULL,
  payment_reference TEXT NOT NULL UNIQUE,
  provider_type TEXT NOT NULL DEFAULT 'sandbox' CHECK (provider_type IN ('sandbox', 'manual_transfer', 'stripe_mock')),
  amount NUMERIC(14,2) NOT NULL CHECK (amount > 0),
  currency_code CHAR(3) NOT NULL DEFAULT 'PKR',
  status TEXT NOT NULL DEFAULT 'succeeded' CHECK (status IN ('initiated', 'pending', 'succeeded', 'failed', 'refunded')),
  failure_reason TEXT,
  idempotency_key TEXT NOT NULL UNIQUE,
  provider_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
  processed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_subscription_payments_tenant 
  ON platform.subscription_payments(tenant_id, status);

ALTER TABLE platform.subscription_payments ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.subscription_payments FORCE ROW LEVEL SECURITY;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'subscription_payments') THEN
    CREATE POLICY tenant_isolation ON platform.subscription_payments
      USING (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
      WITH CHECK (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
  END IF;
END $$;

-- ── 11. Subscription Credit Notes & Refunds ────────────────────────────────

CREATE TABLE IF NOT EXISTS platform.subscription_credit_notes (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES platform.tenants(id) ON DELETE CASCADE,
  invoice_id UUID NOT NULL REFERENCES platform.subscription_invoices(id) ON DELETE CASCADE,
  credit_note_number TEXT NOT NULL UNIQUE,
  amount NUMERIC(14,2) NOT NULL CHECK (amount > 0),
  reason TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'issued' CHECK (status IN ('issued', 'applied', 'void')),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_credit_notes_tenant 
  ON platform.subscription_credit_notes(tenant_id, status);

ALTER TABLE platform.subscription_credit_notes ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.subscription_credit_notes FORCE ROW LEVEL SECURITY;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'subscription_credit_notes') THEN
    CREATE POLICY tenant_isolation ON platform.subscription_credit_notes
      USING (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
      WITH CHECK (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
  END IF;
END $$;

-- ── 12. Support Access Grants (Audit & Time-Bound Access) ────────────────────

CREATE TABLE IF NOT EXISTS platform.support_access_grants (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID NOT NULL REFERENCES platform.tenants(id) ON DELETE CASCADE,
  support_user_id UUID NOT NULL REFERENCES identity.users(id),
  authorized_by UUID NOT NULL,
  reason TEXT NOT NULL,
  granted_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at TIMESTAMPTZ NOT NULL,
  revoked_at TIMESTAMPTZ,
  status TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'expired', 'revoked')),
  CONSTRAINT support_access_period_check CHECK (expires_at > granted_at)
);

CREATE INDEX IF NOT EXISTS ix_support_access_tenant 
  ON platform.support_access_grants(tenant_id, status);

ALTER TABLE platform.support_access_grants ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.support_access_grants FORCE ROW LEVEL SECURITY;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'support_access_grants') THEN
    CREATE POLICY tenant_isolation ON platform.support_access_grants
      USING (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
      WITH CHECK (tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
  END IF;
END $$;

-- ── 13. System Monitoring Alerts & Exception Queue ──────────────────────────

CREATE TABLE IF NOT EXISTS platform.saas_platform_alerts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id UUID REFERENCES platform.tenants(id) ON DELETE CASCADE,
  alert_type TEXT NOT NULL,
  severity TEXT NOT NULL DEFAULT 'warning' CHECK (severity IN ('info', 'warning', 'error', 'critical')),
  title TEXT NOT NULL,
  message TEXT NOT NULL,
  metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
  is_acknowledged BOOLEAN NOT NULL DEFAULT false,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_platform_alerts_tenant 
  ON platform.saas_platform_alerts(tenant_id, is_acknowledged, created_at DESC);

ALTER TABLE platform.saas_platform_alerts ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.saas_platform_alerts FORCE ROW LEVEL SECURITY;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'saas_platform_alerts') THEN
    CREATE POLICY tenant_isolation ON platform.saas_platform_alerts
      USING (tenant_id IS NULL OR tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
      WITH CHECK (tenant_id IS NULL OR tenant_id = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
  END IF;
END $$;

-- ── 14. Grants for erp_app ──────────────────────────────────────────────────

GRANT USAGE ON SCHEMA platform TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA platform TO erp_app;

COMMIT;
