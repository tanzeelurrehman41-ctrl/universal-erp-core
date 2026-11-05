-- Universal ERP SaaS foundation — PostgreSQL migration 001
-- Review and apply only to a disposable development database initially.
-- Migration runner should wrap this file in a transaction.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE SCHEMA IF NOT EXISTS platform;
CREATE SCHEMA IF NOT EXISTS identity;
CREATE SCHEMA IF NOT EXISTS organization;
CREATE SCHEMA IF NOT EXISTS "authorization";
CREATE SCHEMA IF NOT EXISTS audit;

CREATE TABLE platform.tenants (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_code text NOT NULL UNIQUE,
    display_name text NOT NULL,
    legal_name text,
    status text NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending','active','suspended','closed')),
    default_country_code char(2) NOT NULL DEFAULT 'PK',
    default_currency_code char(3) NOT NULL DEFAULT 'PKR',
    default_timezone text NOT NULL DEFAULT 'Asia/Karachi',
    default_language_code text NOT NULL DEFAULT 'en',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (id, tenant_code)
);

CREATE TABLE platform.subscription_plans (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    plan_code text NOT NULL UNIQUE,
    name text NOT NULL,
    description text,
    status text NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft','active','retired')),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE platform.plan_features (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    plan_id uuid NOT NULL REFERENCES platform.subscription_plans(id),
    feature_code text NOT NULL,
    enabled boolean NOT NULL DEFAULT true,
    quantity_limit bigint,
    limit_unit text,
    configuration jsonb NOT NULL DEFAULT '{}'::jsonb,
    UNIQUE (plan_id, feature_code),
    CHECK (quantity_limit IS NULL OR quantity_limit >= 0)
);

CREATE TABLE platform.subscriptions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    plan_id uuid NOT NULL REFERENCES platform.subscription_plans(id),
    status text NOT NULL DEFAULT 'trial'
        CHECK (status IN ('trial','active','past_due','grace','suspended','cancelled','expired')),
    billing_interval text NOT NULL CHECK (billing_interval IN ('month','year','custom')),
    currency_code char(3) NOT NULL DEFAULT 'PKR',
    starts_at timestamptz NOT NULL,
    current_period_start timestamptz NOT NULL,
    current_period_end timestamptz NOT NULL,
    trial_ends_at timestamptz,
    grace_ends_at timestamptz,
    cancel_at_period_end boolean NOT NULL DEFAULT false,
    cancelled_at timestamptz,
    external_customer_ref text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, id),
    CHECK (current_period_end > current_period_start)
);
CREATE INDEX subscriptions_tenant_status_idx ON platform.subscriptions(tenant_id, status);

CREATE TABLE platform.subscription_items (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    subscription_id uuid NOT NULL,
    item_type text NOT NULL CHECK (item_type IN ('module','quantity','addon','discount')),
    item_code text NOT NULL,
    quantity numeric(20,4) NOT NULL DEFAULT 1 CHECK (quantity >= 0),
    unit_price numeric(20,4),
    currency_code char(3) NOT NULL DEFAULT 'PKR',
    effective_from timestamptz NOT NULL,
    effective_until timestamptz,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    FOREIGN KEY (tenant_id, subscription_id)
        REFERENCES platform.subscriptions(tenant_id, id),
    CHECK (effective_until IS NULL OR effective_until > effective_from)
);

CREATE TABLE platform.tenant_entitlements (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    feature_code text NOT NULL,
    enabled boolean NOT NULL,
    quantity_limit bigint,
    source_type text NOT NULL CHECK (source_type IN ('plan','contract','promotion','override')),
    source_id uuid,
    effective_from timestamptz NOT NULL,
    effective_until timestamptz,
    policy_version integer NOT NULL DEFAULT 1 CHECK (policy_version > 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    CHECK (quantity_limit IS NULL OR quantity_limit >= 0),
    CHECK (effective_until IS NULL OR effective_until > effective_from)
);
CREATE INDEX tenant_entitlements_lookup_idx
    ON platform.tenant_entitlements(tenant_id, feature_code, effective_from, effective_until);

CREATE TABLE platform.usage_records (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    metric_code text NOT NULL,
    quantity numeric(24,6) NOT NULL CHECK (quantity >= 0),
    unit text NOT NULL,
    period_start timestamptz NOT NULL,
    period_end timestamptz NOT NULL,
    source_event_id uuid,
    recorded_at timestamptz NOT NULL DEFAULT now(),
    CHECK (period_end > period_start)
);
CREATE INDEX usage_records_tenant_metric_period_idx
    ON platform.usage_records(tenant_id, metric_code, period_start, period_end);

CREATE TABLE identity.users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    email_normalized text UNIQUE,
    phone_e164 text UNIQUE,
    display_name text NOT NULL,
    status text NOT NULL DEFAULT 'active'
        CHECK (status IN ('invited','active','locked','disabled')),
    preferred_language_code text NOT NULL DEFAULT 'en',
    mfa_required boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CHECK (email_normalized IS NOT NULL OR phone_e164 IS NOT NULL)
);

CREATE TABLE identity.tenant_memberships (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    user_id uuid NOT NULL REFERENCES identity.users(id),
    status text NOT NULL DEFAULT 'invited'
        CHECK (status IN ('invited','active','suspended','revoked')),
    joined_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, user_id),
    UNIQUE (tenant_id, id)
);

CREATE TABLE organization.company_groups (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    group_code text NOT NULL,
    name text NOT NULL,
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('active','inactive')),
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, id),
    UNIQUE (tenant_id, group_code)
);

CREATE TABLE organization.companies (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    group_id uuid,
    company_code text NOT NULL,
    name text NOT NULL,
    legal_name text,
    registration_number text,
    country_code char(2) NOT NULL,
    base_currency_code char(3) NOT NULL,
    timezone text NOT NULL,
    fiscal_year_start_month smallint NOT NULL DEFAULT 7 CHECK (fiscal_year_start_month BETWEEN 1 AND 12),
    fiscal_year_start_day smallint NOT NULL DEFAULT 1 CHECK (fiscal_year_start_day BETWEEN 1 AND 31),
    default_language_code text NOT NULL DEFAULT 'en',
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('active','inactive','closed')),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, id),
    UNIQUE (tenant_id, company_code),
    FOREIGN KEY (tenant_id, group_id)
        REFERENCES organization.company_groups(tenant_id, id)
);
CREATE INDEX companies_tenant_status_idx ON organization.companies(tenant_id, status);

CREATE TABLE identity.company_memberships (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    user_id uuid NOT NULL,
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('invited','active','suspended','revoked')),
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, company_id, user_id),
    UNIQUE (tenant_id, company_id, id),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, user_id)
        REFERENCES identity.tenant_memberships(tenant_id, user_id)
);

CREATE TABLE organization.locations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    parent_location_id uuid,
    location_code text NOT NULL,
    location_type_code text NOT NULL,
    name text NOT NULL,
    country_code char(2) NOT NULL,
    timezone text NOT NULL,
    address jsonb NOT NULL DEFAULT '{}'::jsonb,
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('active','inactive','closed')),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, company_id, id),
    UNIQUE (tenant_id, company_id, location_code),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, parent_location_id)
        REFERENCES organization.locations(tenant_id, company_id, id),
    CHECK (parent_location_id IS NULL OR parent_location_id <> id)
);
CREATE INDEX locations_parent_idx ON organization.locations(tenant_id, company_id, parent_location_id);

CREATE TABLE "authorization".permissions (
    permission_code text PRIMARY KEY,
    description text NOT NULL,
    module_code text NOT NULL
);

CREATE TABLE "authorization".roles (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    role_code text NOT NULL,
    name text NOT NULL,
    is_system_role boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, id),
    UNIQUE (tenant_id, role_code)
);

CREATE TABLE "authorization".role_permissions (
    tenant_id uuid NOT NULL,
    role_id uuid NOT NULL,
    permission_code text NOT NULL REFERENCES "authorization".permissions(permission_code),
    scope_type text NOT NULL CHECK (scope_type IN ('tenant','company','location','self','assigned_records')),
    PRIMARY KEY (tenant_id, role_id, permission_code, scope_type),
    FOREIGN KEY (tenant_id, role_id) REFERENCES "authorization".roles(tenant_id, id)
);

CREATE TABLE "authorization".membership_role_assignments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    role_id uuid NOT NULL,
    assigned_by_user_id uuid REFERENCES identity.users(id),
    effective_from timestamptz NOT NULL DEFAULT now(),
    effective_until timestamptz,
    FOREIGN KEY (tenant_id, membership_id)
        REFERENCES identity.tenant_memberships(tenant_id, id),
    FOREIGN KEY (tenant_id, role_id)
        REFERENCES "authorization".roles(tenant_id, id),
    CHECK (effective_until IS NULL OR effective_until > effective_from)
);

CREATE TABLE "authorization".location_access (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    company_membership_id uuid NOT NULL,
    location_id uuid NOT NULL,
    access_mode text NOT NULL DEFAULT 'direct' CHECK (access_mode IN ('direct','inherited')),
    granted_at timestamptz NOT NULL DEFAULT now(),
    granted_by_user_id uuid REFERENCES identity.users(id),
    UNIQUE (tenant_id, company_membership_id, location_id),
    FOREIGN KEY (tenant_id, company_id, company_membership_id)
        REFERENCES identity.company_memberships(tenant_id, company_id, id),
    FOREIGN KEY (tenant_id, company_id, location_id)
        REFERENCES organization.locations(tenant_id, company_id, id)
);

CREATE TABLE identity.devices (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    company_id uuid,
    location_id uuid,
    device_identifier_hash text NOT NULL,
    platform_code text NOT NULL CHECK (platform_code IN ('windows','android','web','linux_edge','ios')),
    device_name text,
    status text NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending','active','revoked','lost','retired')),
    last_seen_at timestamptz,
    enrolled_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, id),
    UNIQUE (tenant_id, device_identifier_hash),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, location_id)
        REFERENCES organization.locations(tenant_id, company_id, id),
    CHECK (location_id IS NULL OR company_id IS NOT NULL)
);

CREATE TABLE audit.audit_events (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    company_id uuid,
    location_id uuid,
    actor_user_id uuid REFERENCES identity.users(id),
    device_id uuid,
    event_code text NOT NULL,
    entity_type text NOT NULL,
    entity_id uuid,
    correlation_id uuid NOT NULL,
    occurred_at timestamptz NOT NULL,
    recorded_at timestamptz NOT NULL DEFAULT now(),
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, location_id)
        REFERENCES organization.locations(tenant_id, company_id, id),
    FOREIGN KEY (tenant_id, device_id)
        REFERENCES identity.devices(tenant_id, id),
    CHECK (location_id IS NULL OR company_id IS NOT NULL)
);
CREATE INDEX audit_events_tenant_recorded_idx ON audit.audit_events(tenant_id, recorded_at DESC);
CREATE INDEX audit_events_entity_idx ON audit.audit_events(tenant_id, entity_type, entity_id, recorded_at DESC);

-- RLS policies are enabled here as a defense-in-depth baseline.
-- Trusted API middleware must set app.tenant_id transaction-locally after authentication.
-- Application roles must not own these tables and must not have BYPASSRLS.
DO $$
DECLARE r record;
BEGIN
  FOR r IN
    SELECT * FROM (VALUES
      ('platform','tenant_entitlements'),
      ('platform','usage_records'),
      ('platform','subscriptions'),
      ('platform','subscription_items'),
      ('identity','tenant_memberships'),
      ('identity','company_memberships'),
      ('identity','devices'),
      ('organization','company_groups'),
      ('organization','companies'),
      ('organization','locations'),
      ('authorization','roles'),
      ('authorization','role_permissions'),
      ('authorization','membership_role_assignments'),
      ('authorization','location_access'),
      ('audit','audit_events')
    ) AS t(schema_name, table_name)
  LOOP
    EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', r.schema_name, r.table_name);
    EXECUTE format('ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY', r.schema_name, r.table_name);
    EXECUTE format(
      'CREATE POLICY tenant_isolation ON %I.%I USING (tenant_id = nullif(current_setting(''app.tenant_id'', true), '''')::uuid) WITH CHECK (tenant_id = nullif(current_setting(''app.tenant_id'', true), '''')::uuid)',
      r.schema_name, r.table_name
    );
  END LOOP;
END $$;

-- Platform-level tables (tenants, plan catalog, permissions, users) intentionally
-- require separate platform/identity access policies and grants in migration 002.
-- Do not grant broad schema/table privileges to application roles.
