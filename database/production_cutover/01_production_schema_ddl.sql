-- ===========================================================================
-- Universal ERP Production Database Schema Deployment Package
-- Generated: 2026-11-08 06:50:46 UTC
-- Fully hardened: Forced RLS, least-privilege erp_app, zero development seeds
-- ===========================================================================
BEGIN;

-- 0. Schema migrations tracking ledger
CREATE TABLE IF NOT EXISTS public.schema_migrations (
    version text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------------
-- Migration: 001_platform_foundation.sql
-- ---------------------------------------------------------------------------
-- Universal ERP SaaS foundation â€” PostgreSQL migration 001
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


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('001_platform_foundation', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 002_inventory.sql
-- ---------------------------------------------------------------------------
-- Integrated inventory schema based on INV-ARCH-001.
-- Tenant and company foreign keys point to the canonical platform tables.

CREATE SCHEMA IF NOT EXISTS erp_inventory;

CREATE TABLE erp_inventory.product (
  tenant_id UUID NOT NULL,
  product_id UUID NOT NULL,
  product_code TEXT NOT NULL,
  name TEXT NOT NULL,
  description TEXT,
  product_type TEXT NOT NULL CHECK (product_type IN ('stock','service','non_stock')),
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, product_id),
  UNIQUE (tenant_id, product_code),
  FOREIGN KEY (tenant_id) REFERENCES platform.tenants(id)
);

CREATE TABLE erp_inventory.unit_of_measure (
  tenant_id UUID NOT NULL,
  uom_id UUID NOT NULL,
  code TEXT NOT NULL,
  name TEXT NOT NULL,
  dimension TEXT NOT NULL,
  decimal_places SMALLINT NOT NULL DEFAULT 3 CHECK (decimal_places BETWEEN 0 AND 9),
  PRIMARY KEY (tenant_id, uom_id),
  UNIQUE (tenant_id, code),
  FOREIGN KEY (tenant_id) REFERENCES platform.tenants(id)
);

CREATE TABLE erp_inventory.product_variant (
  tenant_id UUID NOT NULL,
  variant_id UUID NOT NULL,
  product_id UUID NOT NULL,
  sku TEXT NOT NULL,
  variant_name TEXT,
  base_uom_id UUID NOT NULL,
  tracking_mode TEXT NOT NULL DEFAULT 'none'
    CHECK (tracking_mode IN ('none','lot','serial','lot_serial')),
  costing_method TEXT NOT NULL DEFAULT 'fifo'
    CHECK (costing_method IN ('fifo','moving_average','specific_identification')),
  negative_stock_allowed BOOLEAN NOT NULL DEFAULT FALSE,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  PRIMARY KEY (tenant_id, variant_id),
  UNIQUE (tenant_id, sku),
  FOREIGN KEY (tenant_id, product_id)
    REFERENCES erp_inventory.product(tenant_id, product_id),
  FOREIGN KEY (tenant_id, base_uom_id)
    REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id)
);

CREATE TABLE erp_inventory.product_category (
  tenant_id UUID NOT NULL,
  category_id UUID NOT NULL,
  parent_category_id UUID,
  code TEXT NOT NULL,
  name TEXT NOT NULL,
  PRIMARY KEY (tenant_id, category_id),
  UNIQUE (tenant_id, code),
  FOREIGN KEY (tenant_id) REFERENCES platform.tenants(id),
  FOREIGN KEY (tenant_id, parent_category_id)
    REFERENCES erp_inventory.product_category(tenant_id, category_id)
);

CREATE TABLE erp_inventory.product_category_link (
  tenant_id UUID NOT NULL,
  product_id UUID NOT NULL,
  category_id UUID NOT NULL,
  PRIMARY KEY (tenant_id, product_id, category_id),
  FOREIGN KEY (tenant_id, product_id)
    REFERENCES erp_inventory.product(tenant_id, product_id),
  FOREIGN KEY (tenant_id, category_id)
    REFERENCES erp_inventory.product_category(tenant_id, category_id)
);

CREATE TABLE erp_inventory.product_barcode (
  tenant_id UUID NOT NULL,
  barcode_id UUID NOT NULL,
  variant_id UUID NOT NULL,
  barcode_value TEXT NOT NULL,
  symbology TEXT NOT NULL,
  is_primary BOOLEAN NOT NULL DEFAULT FALSE,
  valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
  valid_to TIMESTAMPTZ,
  PRIMARY KEY (tenant_id, barcode_id),
  CHECK (valid_to IS NULL OR valid_to > valid_from),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id)
);
CREATE UNIQUE INDEX uq_active_tenant_barcode
  ON erp_inventory.product_barcode(tenant_id, barcode_value)
  WHERE valid_to IS NULL;

CREATE TABLE erp_inventory.product_attribute_definition (
  tenant_id UUID NOT NULL,
  attribute_id UUID NOT NULL,
  attribute_code TEXT NOT NULL,
  display_name TEXT NOT NULL,
  data_type TEXT NOT NULL CHECK (data_type IN ('text','number','boolean','date','option')),
  applies_to TEXT NOT NULL CHECK (applies_to IN ('product','variant','both')),
  is_filterable BOOLEAN NOT NULL DEFAULT FALSE,
  PRIMARY KEY (tenant_id, attribute_id),
  UNIQUE (tenant_id, attribute_code),
  FOREIGN KEY (tenant_id) REFERENCES platform.tenants(id)
);

CREATE TABLE erp_inventory.product_attribute_value (
  tenant_id UUID NOT NULL,
  attribute_value_id UUID NOT NULL,
  product_id UUID NOT NULL,
  variant_id UUID,
  attribute_id UUID NOT NULL,
  value_text TEXT,
  value_number NUMERIC(24,9),
  value_boolean BOOLEAN,
  value_date DATE,
  PRIMARY KEY (tenant_id, attribute_value_id),
  FOREIGN KEY (tenant_id, product_id)
    REFERENCES erp_inventory.product(tenant_id, product_id),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
  FOREIGN KEY (tenant_id, attribute_id)
    REFERENCES erp_inventory.product_attribute_definition(tenant_id, attribute_id),
  CHECK (num_nonnulls(value_text, value_number, value_boolean, value_date) = 1)
);

CREATE UNIQUE INDEX uq_product_attribute_value_scope
  ON erp_inventory.product_attribute_value
  (tenant_id, product_id, COALESCE(variant_id, '00000000-0000-0000-0000-000000000000'::uuid), attribute_id);

CREATE TABLE erp_inventory.product_company_policy (
  tenant_id UUID NOT NULL,
  company_id UUID NOT NULL,
  product_id UUID NOT NULL,
  is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
  default_sales_enabled BOOLEAN NOT NULL DEFAULT TRUE,
  default_purchase_enabled BOOLEAN NOT NULL DEFAULT TRUE,
  PRIMARY KEY (tenant_id, company_id, product_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, product_id)
    REFERENCES erp_inventory.product(tenant_id, product_id)
);

CREATE TABLE erp_inventory.unit_conversion (
  tenant_id UUID NOT NULL,
  conversion_id UUID NOT NULL,
  from_uom_id UUID NOT NULL,
  to_uom_id UUID NOT NULL,
  factor NUMERIC(24,12) NOT NULL CHECK (factor > 0),
  effective_from DATE NOT NULL,
  effective_to DATE,
  CHECK (effective_to IS NULL OR effective_to >= effective_from),
  PRIMARY KEY (tenant_id, conversion_id),
  FOREIGN KEY (tenant_id, from_uom_id)
    REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id),
  FOREIGN KEY (tenant_id, to_uom_id)
    REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id),
  CHECK (from_uom_id <> to_uom_id)
);

CREATE TABLE erp_inventory.warehouse (
  tenant_id UUID NOT NULL,
  company_id UUID NOT NULL,
  warehouse_id UUID NOT NULL,
  code TEXT NOT NULL,
  name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  PRIMARY KEY (tenant_id, company_id, warehouse_id),
  UNIQUE (tenant_id, company_id, code),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id)
);

CREATE TABLE erp_inventory.stock_bin (
  tenant_id UUID NOT NULL,
  company_id UUID NOT NULL,
  warehouse_id UUID NOT NULL,
  bin_id UUID NOT NULL,
  code TEXT NOT NULL,
  bin_type TEXT NOT NULL DEFAULT 'storage'
    CHECK (bin_type IN ('storage','receiving','dispatch','production','transit','virtual')),
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  PRIMARY KEY (tenant_id, company_id, bin_id),
  UNIQUE (tenant_id, company_id, warehouse_id, code),
  FOREIGN KEY (tenant_id, company_id, warehouse_id)
    REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE TABLE erp_inventory.stock_status (
  tenant_id UUID NOT NULL,
  status_code TEXT NOT NULL,
  description TEXT NOT NULL,
  is_reservable BOOLEAN NOT NULL DEFAULT FALSE,
  is_pickable BOOLEAN NOT NULL DEFAULT FALSE,
  PRIMARY KEY (tenant_id, status_code),
  FOREIGN KEY (tenant_id) REFERENCES platform.tenants(id)
);

CREATE TABLE erp_inventory.inventory_lot (
  tenant_id UUID NOT NULL,
  company_id UUID NOT NULL,
  lot_id UUID NOT NULL,
  variant_id UUID NOT NULL,
  lot_code TEXT NOT NULL,
  manufacture_date DATE,
  expiry_date DATE,
  supplier_lot_reference TEXT,
  quality_status TEXT NOT NULL DEFAULT 'pending'
    CHECK (quality_status IN ('pending','approved','rejected','quarantined')),
  PRIMARY KEY (tenant_id, company_id, lot_id),
  UNIQUE (tenant_id, company_id, variant_id, lot_code),
  CHECK (expiry_date IS NULL OR manufacture_date IS NULL OR expiry_date >= manufacture_date),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id)
);

CREATE TABLE erp_inventory.inventory_serial (
  tenant_id UUID NOT NULL,
  company_id UUID NOT NULL,
  serial_id UUID NOT NULL,
  variant_id UUID NOT NULL,
  serial_number TEXT NOT NULL,
  lot_id UUID,
  serial_status TEXT NOT NULL DEFAULT 'active'
    CHECK (serial_status IN ('active','sold','returned','quarantined','scrapped')),
  PRIMARY KEY (tenant_id, company_id, serial_id),
  UNIQUE (tenant_id, variant_id, serial_number),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
  FOREIGN KEY (tenant_id, company_id, lot_id)
    REFERENCES erp_inventory.inventory_lot(tenant_id, company_id, lot_id)
);

CREATE TABLE erp_inventory.inventory_movement (
  tenant_id UUID NOT NULL,
  company_id UUID NOT NULL,
  movement_id UUID NOT NULL,
  client_transaction_id UUID NOT NULL,
  movement_type TEXT NOT NULL CHECK (movement_type IN
    ('receipt','issue','transfer','return_in','return_out','adjustment','production_in','production_out')),
  source_document_type TEXT,
  source_document_id UUID,
  status TEXT NOT NULL DEFAULT 'posted' CHECK (status IN ('posted','reversed')),
  occurred_at TIMESTAMPTZ NOT NULL,
  posted_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  created_by UUID NOT NULL,
  reversal_of_movement_id UUID,
  note TEXT,
  PRIMARY KEY (tenant_id, company_id, movement_id),
  UNIQUE (tenant_id, client_transaction_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, company_id, reversal_of_movement_id)
    REFERENCES erp_inventory.inventory_movement(tenant_id, company_id, movement_id)
);

CREATE TABLE erp_inventory.inventory_movement_leg (
  tenant_id UUID NOT NULL,
  company_id UUID NOT NULL,
  movement_id UUID NOT NULL,
  leg_number SMALLINT NOT NULL CHECK (leg_number > 0),
  variant_id UUID NOT NULL,
  warehouse_id UUID,
  bin_id UUID,
  status_code TEXT NOT NULL,
  lot_id UUID,
  serial_id UUID,
  quantity_delta NUMERIC(24,9) NOT NULL CHECK (quantity_delta <> 0),
  base_uom_id UUID NOT NULL,
  PRIMARY KEY (tenant_id, company_id, movement_id, leg_number),
  FOREIGN KEY (tenant_id, company_id, movement_id)
    REFERENCES erp_inventory.inventory_movement(tenant_id, company_id, movement_id),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
  FOREIGN KEY (tenant_id, company_id, warehouse_id)
    REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id),
  FOREIGN KEY (tenant_id, company_id, bin_id)
    REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id),
  FOREIGN KEY (tenant_id, status_code)
    REFERENCES erp_inventory.stock_status(tenant_id, status_code),
  FOREIGN KEY (tenant_id, company_id, lot_id)
    REFERENCES erp_inventory.inventory_lot(tenant_id, company_id, lot_id),
  FOREIGN KEY (tenant_id, company_id, serial_id)
    REFERENCES erp_inventory.inventory_serial(tenant_id, company_id, serial_id),
  FOREIGN KEY (tenant_id, base_uom_id)
    REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id)
);

-- Projection only: rebuildable from posted movement legs. Application posting
-- logic must update it transactionally or maintain it through a controlled projector.
CREATE TABLE erp_inventory.stock_balance (
  tenant_id UUID NOT NULL,
  company_id UUID NOT NULL,
  variant_id UUID NOT NULL,
  warehouse_id UUID NOT NULL,
  bin_id UUID NOT NULL,
  status_code TEXT NOT NULL,
  lot_id UUID,
  on_hand_qty NUMERIC(24,9) NOT NULL DEFAULT 0,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  balance_id UUID NOT NULL,
  PRIMARY KEY (tenant_id, company_id, balance_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id)
);

CREATE TABLE erp_inventory.stock_reservation (
  tenant_id UUID NOT NULL,
  company_id UUID NOT NULL,
  reservation_id UUID NOT NULL,
  client_transaction_id UUID NOT NULL,
  variant_id UUID NOT NULL,
  warehouse_id UUID NOT NULL,
  bin_id UUID,
  status_code TEXT NOT NULL,
  lot_id UUID,
  quantity NUMERIC(24,9) NOT NULL CHECK (quantity > 0),
  reservation_status TEXT NOT NULL DEFAULT 'active'
    CHECK (reservation_status IN ('active','partially_fulfilled','fulfilled','released','expired','rejected')),
  source_document_type TEXT,
  source_document_id UUID,
  expires_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, reservation_id),
  UNIQUE (tenant_id, client_transaction_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id)
);

CREATE TABLE erp_inventory.valuation_entry (
  tenant_id UUID NOT NULL,
  company_id UUID NOT NULL,
  valuation_entry_id UUID NOT NULL,
  movement_id UUID NOT NULL,
  variant_id UUID NOT NULL,
  entry_type TEXT NOT NULL CHECK (entry_type IN ('receipt_cost','issue_cost','revaluation','variance','reversal')),
  quantity NUMERIC(24,9) NOT NULL,
  unit_cost NUMERIC(24,9) NOT NULL CHECK (unit_cost >= 0),
  extended_cost NUMERIC(24,9) NOT NULL,
  currency_code CHAR(3) NOT NULL,
  posted_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, valuation_entry_id),
  FOREIGN KEY (tenant_id, company_id, movement_id)
    REFERENCES erp_inventory.inventory_movement(tenant_id, company_id, movement_id),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id)
);

CREATE TABLE erp_inventory.outbox_event (
  tenant_id UUID NOT NULL,
  company_id UUID NOT NULL,
  event_id UUID NOT NULL,
  aggregate_type TEXT NOT NULL,
  aggregate_id UUID NOT NULL,
  event_type TEXT NOT NULL,
  payload JSONB NOT NULL,
  occurred_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  published_at TIMESTAMPTZ,
  attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
  PRIMARY KEY (tenant_id, company_id, event_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id)
);

CREATE UNIQUE INDEX uq_stock_balance_bucket ON erp_inventory.stock_balance
  (tenant_id, company_id, variant_id, warehouse_id, bin_id, status_code, COALESCE(lot_id, '00000000-0000-0000-0000-000000000000'::uuid));

CREATE INDEX ix_variant_product ON erp_inventory.product_variant(tenant_id, product_id);
CREATE INDEX ix_movement_occurred ON erp_inventory.inventory_movement(tenant_id, company_id, occurred_at DESC);
CREATE INDEX ix_movement_leg_variant ON erp_inventory.inventory_movement_leg(tenant_id, company_id, variant_id);
CREATE INDEX ix_balance_variant ON erp_inventory.stock_balance(tenant_id, company_id, variant_id);
CREATE INDEX ix_reservation_active ON erp_inventory.stock_reservation(tenant_id, company_id, variant_id, warehouse_id)
  WHERE reservation_status IN ('active','partially_fulfilled');
CREATE INDEX ix_lot_expiry ON erp_inventory.inventory_lot(tenant_id, company_id, variant_id, expiry_date)
  WHERE expiry_date IS NOT NULL;
CREATE INDEX ix_outbox_unpublished ON erp_inventory.outbox_event(occurred_at)
  WHERE published_at IS NULL;

-- Fail-closed RLS. Set context with SET LOCAL inside each transaction.
-- Example: SELECT set_config('app.tenant_id', '<uuid>', true);
-- Example: SELECT set_config('app.company_id', '<uuid>', true);
-- The policy is generated according to whether each table has company_id.
DO $$
DECLARE
  inventory_table_name TEXT;
  has_company_id BOOLEAN;
  policy_expr TEXT;
BEGIN
  FOREACH inventory_table_name IN ARRAY ARRAY[
    'product','product_category','product_category_link','product_barcode',
    'product_attribute_definition','product_attribute_value',
    'unit_of_measure','product_variant','product_company_policy',
    'unit_conversion','warehouse','stock_bin','stock_status','inventory_lot',
    'inventory_serial','inventory_movement','inventory_movement_leg',
    'stock_balance','stock_reservation','valuation_entry','outbox_event'
  ] LOOP
    SELECT EXISTS (
      SELECT 1 FROM information_schema.columns c
      WHERE c.table_schema = 'erp_inventory'
        AND c.table_name = inventory_table_name
        AND c.column_name = 'company_id'
    ) INTO has_company_id;

    IF has_company_id THEN
      policy_expr := 'tenant_id = nullif(current_setting(''app.tenant_id'', true), '''')::uuid AND company_id = nullif(current_setting(''app.company_id'', true), '''')::uuid';
    ELSE
      policy_expr := 'tenant_id = nullif(current_setting(''app.tenant_id'', true), '''')::uuid';
    END IF;

    EXECUTE format('ALTER TABLE erp_inventory.%I ENABLE ROW LEVEL SECURITY', inventory_table_name);
    EXECUTE format('ALTER TABLE erp_inventory.%I FORCE ROW LEVEL SECURITY', inventory_table_name);
    EXECUTE format('CREATE POLICY tenant_company_scope ON erp_inventory.%I USING (%s) WITH CHECK (%s)', inventory_table_name, policy_expr, policy_expr);
  END LOOP;
END $$;

-- Operational security requirement: the application role must not own these
-- tables and must not have BYPASSRLS. Grant only required table/sequence rights.
-- This schema is still a starter draft: validate against your selected PostgreSQL
-- version and the final platform tenant/company tables before production.


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('002_inventory', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 003_local_dev_bootstrap.sql
-- ---------------------------------------------------------------------------
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

DO $role_block$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'erp_app') THEN
        CREATE ROLE erp_app WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
    END IF;
END $role_block$;

GRANT CONNECT ON DATABASE universal_erp TO erp_app;
GRANT USAGE ON SCHEMA erp_inventory TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.product TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_inventory.command_idempotency TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('003_local_dev_bootstrap', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 004_offline_sync_foundation.sql
-- ---------------------------------------------------------------------------
-- Offline sync foundation adapted to the canonical identity and organization schemas.
-- identity.devices, identity.company_memberships, and organization locations remain
-- the authoritative device and membership records; this migration does not duplicate them.

CREATE SCHEMA IF NOT EXISTS sync;

CREATE TABLE sync.device_company_scopes (
  tenant_id uuid NOT NULL,
  device_id uuid NOT NULL,
  company_id uuid NOT NULL,
  scope_status text NOT NULL DEFAULT 'active'
    CHECK (scope_status IN ('active','revoked')),
  all_locations boolean NOT NULL DEFAULT false,
  granted_at timestamptz NOT NULL DEFAULT now(),
  revoked_at timestamptz,
  PRIMARY KEY (tenant_id, device_id, company_id),
  FOREIGN KEY (tenant_id, device_id)
    REFERENCES identity.devices(tenant_id, id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  CHECK ((scope_status = 'active' AND revoked_at IS NULL)
      OR (scope_status = 'revoked' AND revoked_at IS NOT NULL))
);

CREATE TABLE sync.device_location_scopes (
  tenant_id uuid NOT NULL,
  device_id uuid NOT NULL,
  company_id uuid NOT NULL,
  location_id uuid NOT NULL,
  granted_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, device_id, company_id, location_id),
  FOREIGN KEY (tenant_id, device_id, company_id)
    REFERENCES sync.device_company_scopes(tenant_id, device_id, company_id),
  FOREIGN KEY (tenant_id, company_id, location_id)
    REFERENCES organization.locations(tenant_id, company_id, id)
);

CREATE TABLE sync.offline_grants (
  tenant_id uuid NOT NULL,
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  device_id uuid NOT NULL,
  company_id uuid NOT NULL,
  user_id uuid NOT NULL,
  grant_version integer NOT NULL DEFAULT 1 CHECK (grant_version > 0),
  allowed_operations jsonb NOT NULL,
  allowed_location_ids jsonb NOT NULL DEFAULT '[]'::jsonb,
  limits jsonb NOT NULL DEFAULT '{}'::jsonb,
  issued_at timestamptz NOT NULL DEFAULT now(),
  expires_at timestamptz NOT NULL,
  revoked_at timestamptz,
  revoked_by uuid,
  revocation_reason text,
  grant_signature text NOT NULL,
  PRIMARY KEY (tenant_id, id),
  UNIQUE (tenant_id, company_id, device_id, id),
  FOREIGN KEY (tenant_id, device_id, company_id)
    REFERENCES sync.device_company_scopes(tenant_id, device_id, company_id),
  FOREIGN KEY (tenant_id, company_id, user_id)
    REFERENCES identity.company_memberships(tenant_id, company_id, user_id),
  CHECK (expires_at > issued_at),
  CHECK (jsonb_typeof(allowed_operations) = 'array'),
  CHECK (jsonb_typeof(allowed_location_ids) = 'array'),
  CHECK (jsonb_typeof(limits) = 'object')
);

CREATE TABLE sync.operation_inbox (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  operation_id uuid NOT NULL,
  batch_id uuid NOT NULL,
  device_id uuid NOT NULL,
  grant_id uuid NOT NULL,
  device_sequence bigint NOT NULL CHECK (device_sequence > 0),
  operation_type text NOT NULL,
  schema_version integer NOT NULL CHECK (schema_version > 0),
  aggregate_type text NOT NULL,
  aggregate_id uuid,
  base_version bigint CHECK (base_version IS NULL OR base_version >= 0),
  client_created_at timestamptz,
  dependency_operation_ids uuid[] NOT NULL DEFAULT '{}',
  payload_hash text NOT NULL CHECK (payload_hash ~ '^[0-9a-fA-F]{64}$'),
  outcome text NOT NULL CHECK (
    outcome IN ('accepted','duplicate','conflict','rejected','deferred')
  ),
  server_record_type text,
  server_record_id uuid,
  response_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
  received_at timestamptz NOT NULL DEFAULT now(),
  processed_at timestamptz,
  PRIMARY KEY (tenant_id, company_id, operation_id),
  UNIQUE (tenant_id, device_id, device_sequence),
  FOREIGN KEY (tenant_id, device_id, company_id)
    REFERENCES sync.device_company_scopes(tenant_id, device_id, company_id),
  FOREIGN KEY (tenant_id, company_id, device_id, grant_id)
    REFERENCES sync.offline_grants(tenant_id, company_id, device_id, id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  CHECK (jsonb_typeof(response_payload) = 'object')
);

CREATE TABLE sync.device_cursors (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  device_id uuid NOT NULL,
  download_cursor text,
  last_acknowledged_sequence bigint NOT NULL DEFAULT 0
    CHECK (last_acknowledged_sequence >= 0),
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, device_id),
  FOREIGN KEY (tenant_id, device_id, company_id)
    REFERENCES sync.device_company_scopes(tenant_id, device_id, company_id)
);

CREATE TABLE sync.change_log (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  change_sequence bigint GENERATED ALWAYS AS IDENTITY,
  change_id uuid NOT NULL DEFAULT gen_random_uuid(),
  entity_type text NOT NULL,
  entity_id uuid NOT NULL,
  entity_version bigint NOT NULL CHECK (entity_version > 0),
  change_type text NOT NULL CHECK (change_type IN ('upsert','delete')),
  payload jsonb NOT NULL DEFAULT '{}'::jsonb,
  occurred_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, change_sequence),
  UNIQUE (tenant_id, company_id, change_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  CHECK (jsonb_typeof(payload) = 'object')
);

-- Fail-closed company-scoped isolation; the API must set both values transaction-locally.
DO $$
DECLARE table_name text;
BEGIN
  FOREACH table_name IN ARRAY ARRAY[
    'device_company_scopes','device_location_scopes','offline_grants',
    'operation_inbox','device_cursors','change_log'
  ] LOOP
    EXECUTE format('ALTER TABLE sync.%I ENABLE ROW LEVEL SECURITY', table_name);
    EXECUTE format('ALTER TABLE sync.%I FORCE ROW LEVEL SECURITY', table_name);
    EXECUTE format(
      'CREATE POLICY tenant_company_scope ON sync.%I
       USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid
          AND company_id = NULLIF(current_setting(''app.company_id'', true), '''')::uuid)
       WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid
          AND company_id = NULLIF(current_setting(''app.company_id'', true), '''')::uuid)',
      table_name
    );
  END LOOP;
END $$;

CREATE INDEX offline_grants_active_idx
  ON sync.offline_grants(tenant_id, device_id, company_id, expires_at)
  WHERE revoked_at IS NULL;
CREATE INDEX operation_inbox_received_idx
  ON sync.operation_inbox(tenant_id, company_id, received_at DESC);
CREATE INDEX device_scopes_status_idx
  ON sync.device_company_scopes(tenant_id, company_id, scope_status);
CREATE INDEX change_log_entity_idx
  ON sync.change_log(tenant_id, company_id, entity_type, entity_id, entity_version);

-- The API can inspect grants and record/replay operation outcomes. Grant issuance,
-- revocation, device enrollment, and change-log writes stay unavailable until their
-- authenticated services are implemented.
GRANT USAGE ON SCHEMA sync TO erp_app;
GRANT SELECT ON sync.device_company_scopes, sync.device_location_scopes,
  sync.offline_grants TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.operation_inbox, sync.device_cursors TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('004_offline_sync_foundation', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 005_oidc_identity_and_permissions.sql
-- ---------------------------------------------------------------------------
-- Map externally authenticated OIDC identities to provisioned ERP users.
-- Subject values are accepted only after issuer/signature/audience checks in the API.

CREATE TABLE identity.external_identities (
  issuer text NOT NULL,
  subject text NOT NULL,
  user_id uuid NOT NULL REFERENCES identity.users(id),
  linked_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (issuer, subject),
  UNIQUE (issuer, user_id),
  CHECK (length(issuer) BETWEEN 1 AND 2048),
  CHECK (length(subject) BETWEEN 1 AND 1024)
);

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
  ('inventory.product.view', 'View the product catalog.', 'inventory'),
  ('inventory.product.create', 'Create products in the tenant catalog.', 'inventory')
ON CONFLICT (permission_code) DO NOTHING;

-- The least-privileged API can resolve a signed-in subject and verify its live
-- memberships and permission assignments. It cannot create identity links or grants.
GRANT USAGE ON SCHEMA platform, identity, organization, "authorization" TO erp_app;
GRANT SELECT ON identity.external_identities TO erp_app;
GRANT SELECT (id, status) ON identity.users TO erp_app;
GRANT SELECT (tenant_id, user_id, status) ON identity.tenant_memberships TO erp_app;
GRANT SELECT (tenant_id, company_id, user_id, id, status)
  ON identity.company_memberships TO erp_app;
GRANT SELECT (id, status) ON platform.tenants TO erp_app;
GRANT SELECT (tenant_id, id, status) ON organization.companies TO erp_app;
GRANT SELECT (tenant_id, membership_id, role_id, effective_from, effective_until)
  ON "authorization".membership_role_assignments TO erp_app;
GRANT SELECT (tenant_id, role_id, permission_code, scope_type)
  ON "authorization".role_permissions TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('005_oidc_identity_and_permissions', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 006_oidc_workspace_lookup.sql
-- ---------------------------------------------------------------------------
-- Return only active workspaces belonging to a validated external identity.
-- The API supplies issuer/subject from the already-validated bearer token.
CREATE OR REPLACE FUNCTION identity.list_user_company_contexts(
  p_issuer text,
  p_subject text
)
RETURNS TABLE (
  tenant_id uuid,
  tenant_code text,
  tenant_name text,
  company_id uuid,
  company_code text,
  company_name text,
  base_currency_code char(3),
  timezone text
)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, identity, platform, organization
AS $$
  SELECT tenant.id,
         tenant.tenant_code,
         tenant.display_name,
         company.id,
         company.company_code,
         company.name,
         company.base_currency_code,
         company.timezone
  FROM identity.external_identities AS external_identity
  JOIN identity.users AS erp_user
    ON erp_user.id = external_identity.user_id
  JOIN identity.tenant_memberships AS tenant_membership
    ON tenant_membership.user_id = erp_user.id
  JOIN identity.company_memberships AS company_membership
    ON company_membership.tenant_id = tenant_membership.tenant_id
   AND company_membership.user_id = erp_user.id
  JOIN platform.tenants AS tenant
    ON tenant.id = tenant_membership.tenant_id
  JOIN organization.companies AS company
    ON company.tenant_id = tenant.id
   AND company.id = company_membership.company_id
  WHERE external_identity.issuer = p_issuer
    AND external_identity.subject = p_subject
    AND erp_user.status = 'active'
    AND tenant_membership.status = 'active'
    AND company_membership.status = 'active'
    AND tenant.status = 'active'
    AND company.status = 'active'
  ORDER BY tenant.display_name, company.name;
$$;

REVOKE ALL ON FUNCTION identity.list_user_company_contexts(text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.list_user_company_contexts(text, text) TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('006_oidc_workspace_lookup', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 007_inventory_workflow_permissions.sql
-- ---------------------------------------------------------------------------
-- Seed essential development inventory reference data and least-privilege API rights.
INSERT INTO erp_inventory.stock_status
  (tenant_id, status_code, description, is_reservable, is_pickable)
SELECT id, 'available', 'Available for normal inventory use.', true, true
FROM platform.tenants
ON CONFLICT (tenant_id, status_code) DO NOTHING;

INSERT INTO erp_inventory.stock_status
  (tenant_id, status_code, description, is_reservable, is_pickable)
SELECT id, 'quality_hold', 'Held for inspection or quality review.', false, false
FROM platform.tenants
ON CONFLICT (tenant_id, status_code) DO NOTHING;

INSERT INTO erp_inventory.stock_status
  (tenant_id, status_code, description, is_reservable, is_pickable)
SELECT id, 'blocked', 'Blocked from normal use.', false, false
FROM platform.tenants
ON CONFLICT (tenant_id, status_code) DO NOTHING;

INSERT INTO erp_inventory.unit_of_measure
  (tenant_id, uom_id, code, name, dimension, decimal_places)
SELECT id, '00000000-0000-4000-8000-000000000006', 'EA', 'Each', 'count', 0
FROM platform.tenants
ON CONFLICT (tenant_id, code) DO NOTHING;

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
  ('inventory.uom.view', 'View inventory units of measure.', 'inventory'),
  ('inventory.uom.create', 'Create inventory units of measure.', 'inventory'),
  ('inventory.variant.view', 'View stock variants.', 'inventory'),
  ('inventory.variant.create', 'Create stock variants.', 'inventory'),
  ('inventory.warehouse.view', 'View warehouses and storage bins.', 'inventory'),
  ('inventory.warehouse.create', 'Create warehouses and storage bins.', 'inventory'),
  ('inventory.receipt.create', 'Create inventory receipts.', 'inventory'),
  ('inventory.receipt.post', 'Post stock into inventory.', 'inventory'),
  ('inventory.availability.view', 'View inventory availability.', 'inventory')
ON CONFLICT (permission_code) DO NOTHING;

GRANT SELECT, INSERT ON erp_inventory.unit_of_measure TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.product_variant TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.warehouse TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.stock_bin TO erp_app;
GRANT SELECT ON erp_inventory.stock_status TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.inventory_movement,
  erp_inventory.inventory_movement_leg TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_inventory.stock_balance TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.outbox_event TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('007_inventory_workflow_permissions', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 008_sync_change_feed.sql
-- ---------------------------------------------------------------------------
-- Add a commit-ordered, company-scoped cursor for incremental offline downloads.
-- Updating the counter row inside the caller's business transaction ensures that
-- a later committed change cannot be skipped behind an earlier sequence value.

CREATE TABLE sync.company_change_counters (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  last_sequence bigint NOT NULL DEFAULT 0 CHECK (last_sequence >= 0),
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id)
);

ALTER TABLE sync.company_change_counters ENABLE ROW LEVEL SECURITY;
ALTER TABLE sync.company_change_counters FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_company_scope ON sync.company_change_counters
  USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
     AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
  WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
          AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE sync.change_log ADD COLUMN company_sequence bigint;

WITH sequenced AS (
  SELECT ctid,
         row_number() OVER (
           PARTITION BY tenant_id, company_id
           ORDER BY change_sequence
         )::bigint AS next_company_sequence
  FROM sync.change_log
)
UPDATE sync.change_log AS change
SET company_sequence = sequenced.next_company_sequence
FROM sequenced
WHERE change.ctid = sequenced.ctid;

ALTER TABLE sync.change_log ALTER COLUMN company_sequence SET NOT NULL;
ALTER TABLE sync.change_log
  ADD CONSTRAINT change_log_company_sequence_positive CHECK (company_sequence > 0);
CREATE UNIQUE INDEX change_log_company_sequence_idx
  ON sync.change_log(tenant_id, company_id, company_sequence);

INSERT INTO sync.company_change_counters(tenant_id, company_id, last_sequence)
SELECT tenant_id, company_id, max(company_sequence)
FROM sync.change_log
GROUP BY tenant_id, company_id
ON CONFLICT (tenant_id, company_id) DO UPDATE
SET last_sequence = GREATEST(sync.company_change_counters.last_sequence, EXCLUDED.last_sequence),
    updated_at = now();

GRANT SELECT, INSERT ON sync.change_log TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.company_change_counters TO erp_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA sync TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('008_sync_change_feed', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 009_sync_change_feed_index.sql
-- ---------------------------------------------------------------------------
-- Keep filtered company feed pages bounded and ordered as each tenant grows.
CREATE INDEX sync_change_feed_page_idx
  ON sync.change_log(tenant_id, company_id, entity_type, company_sequence);


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('009_sync_change_feed_index', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 010_offline_device_enrollment.sql
-- ---------------------------------------------------------------------------
-- Allow the API to enroll an authenticated device, issue company-scoped receipt
-- grants, and reserve/replay receipt outcomes in the durable operation inbox.
-- Application RLS policies continue to restrict these writes to the active
-- tenant and company context.

GRANT SELECT, INSERT, UPDATE ON identity.devices TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.device_company_scopes, sync.offline_grants TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.operation_inbox TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('010_offline_device_enrollment', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 011_purchasing.sql
-- ---------------------------------------------------------------------------
-- Migration 011 â€“ Purchasing module foundation
-- Suppliers, purchase orders, order lines, goods received notes (GRNs),
-- and GRN lines. GRN posting raises an outbox event consumed by inventory.
-- All tables are tenant+company scoped with RLS identical to the inventory schema.

CREATE SCHEMA IF NOT EXISTS erp_purchasing;

-- â”€â”€ Suppliers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE erp_purchasing.supplier (
  tenant_id   UUID    NOT NULL,
  company_id  UUID    NOT NULL,
  supplier_id UUID    NOT NULL DEFAULT gen_random_uuid(),
  supplier_code TEXT  NOT NULL,
  name          TEXT  NOT NULL,
  contact_name  TEXT,
  email         TEXT,
  phone         TEXT,
  address       TEXT,
  currency_code CHAR(3) NOT NULL DEFAULT 'PKR',
  payment_terms_days SMALLINT NOT NULL DEFAULT 30 CHECK (payment_terms_days >= 0),
  is_active     BOOLEAN NOT NULL DEFAULT TRUE,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, supplier_id),
  UNIQUE (tenant_id, company_id, supplier_code),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id)
);

-- â”€â”€ Purchase Orders â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE erp_purchasing.purchase_order (
  tenant_id        UUID    NOT NULL,
  company_id       UUID    NOT NULL,
  purchase_order_id UUID   NOT NULL DEFAULT gen_random_uuid(),
  po_number        TEXT    NOT NULL,
  supplier_id      UUID    NOT NULL,
  warehouse_id     UUID    NOT NULL,   -- receiving warehouse
  order_status     TEXT    NOT NULL DEFAULT 'draft'
    CHECK (order_status IN ('draft','confirmed','partially_received','received','cancelled')),
  currency_code    CHAR(3) NOT NULL DEFAULT 'PKR',
  order_date       DATE    NOT NULL DEFAULT CURRENT_DATE,
  expected_date    DATE,
  note             TEXT,
  created_by       UUID    NOT NULL,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, purchase_order_id),
  UNIQUE (tenant_id, company_id, po_number),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, company_id, supplier_id)
    REFERENCES erp_purchasing.supplier(tenant_id, company_id, supplier_id),
  FOREIGN KEY (tenant_id, company_id, warehouse_id)
    REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE TABLE erp_purchasing.purchase_order_line (
  tenant_id         UUID       NOT NULL,
  company_id        UUID       NOT NULL,
  purchase_order_id UUID       NOT NULL,
  line_number       SMALLINT   NOT NULL CHECK (line_number > 0),
  variant_id        UUID       NOT NULL,
  ordered_qty       NUMERIC(24,9) NOT NULL CHECK (ordered_qty > 0),
  received_qty      NUMERIC(24,9) NOT NULL DEFAULT 0 CHECK (received_qty >= 0),
  unit_of_measure_id UUID      NOT NULL,
  unit_price        NUMERIC(24,4) NOT NULL DEFAULT 0 CHECK (unit_price >= 0),
  line_status       TEXT       NOT NULL DEFAULT 'open'
    CHECK (line_status IN ('open','partially_received','received','cancelled')),
  PRIMARY KEY (tenant_id, company_id, purchase_order_id, line_number),
  FOREIGN KEY (tenant_id, company_id, purchase_order_id)
    REFERENCES erp_purchasing.purchase_order(tenant_id, company_id, purchase_order_id),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
  FOREIGN KEY (tenant_id, unit_of_measure_id)
    REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id)
);

-- â”€â”€ Goods Received Notes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE erp_purchasing.goods_received_note (
  tenant_id             UUID    NOT NULL,
  company_id            UUID    NOT NULL,
  grn_id                UUID    NOT NULL DEFAULT gen_random_uuid(),
  client_transaction_id UUID    NOT NULL,   -- idempotency; maps to inventory movement
  grn_number            TEXT    NOT NULL,
  purchase_order_id     UUID    NOT NULL,
  supplier_id           UUID    NOT NULL,
  warehouse_id          UUID    NOT NULL,
  grn_status            TEXT    NOT NULL DEFAULT 'draft'
    CHECK (grn_status IN ('draft','posted','reversed')),
  received_date         DATE    NOT NULL DEFAULT CURRENT_DATE,
  supplier_reference    TEXT,
  note                  TEXT,
  created_by            UUID    NOT NULL,
  posted_at             TIMESTAMPTZ,
  created_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, grn_id),
  UNIQUE (tenant_id, company_id, grn_number),
  UNIQUE (tenant_id, client_transaction_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, company_id, purchase_order_id)
    REFERENCES erp_purchasing.purchase_order(tenant_id, company_id, purchase_order_id),
  FOREIGN KEY (tenant_id, company_id, supplier_id)
    REFERENCES erp_purchasing.supplier(tenant_id, company_id, supplier_id),
  FOREIGN KEY (tenant_id, company_id, warehouse_id)
    REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE TABLE erp_purchasing.goods_received_note_line (
  tenant_id    UUID       NOT NULL,
  company_id   UUID       NOT NULL,
  grn_id       UUID       NOT NULL,
  line_number  SMALLINT   NOT NULL CHECK (line_number > 0),
  po_line_number SMALLINT NOT NULL,
  purchase_order_id UUID  NOT NULL,
  variant_id   UUID       NOT NULL,
  bin_id       UUID       NOT NULL,
  received_qty NUMERIC(24,9) NOT NULL CHECK (received_qty > 0),
  unit_of_measure_id UUID NOT NULL,
  unit_price   NUMERIC(24,4) NOT NULL DEFAULT 0 CHECK (unit_price >= 0),
  PRIMARY KEY (tenant_id, company_id, grn_id, line_number),
  FOREIGN KEY (tenant_id, company_id, grn_id)
    REFERENCES erp_purchasing.goods_received_note(tenant_id, company_id, grn_id),
  FOREIGN KEY (tenant_id, company_id, purchase_order_id, po_line_number)
    REFERENCES erp_purchasing.purchase_order_line(tenant_id, company_id, purchase_order_id, line_number),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
  FOREIGN KEY (tenant_id, company_id, bin_id)
    REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id),
  FOREIGN KEY (tenant_id, unit_of_measure_id)
    REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id)
);

-- â”€â”€ Outbox (purchasing â†’ inventory events) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE erp_purchasing.outbox_event (
  tenant_id    UUID    NOT NULL,
  company_id   UUID    NOT NULL,
  event_id     UUID    NOT NULL DEFAULT gen_random_uuid(),
  grn_id       UUID    NOT NULL,
  event_type   TEXT    NOT NULL,
  payload      JSONB   NOT NULL,
  occurred_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  published_at TIMESTAMPTZ,
  attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
  PRIMARY KEY (tenant_id, company_id, event_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, company_id, grn_id)
    REFERENCES erp_purchasing.goods_received_note(tenant_id, company_id, grn_id)
);

-- â”€â”€ Indexes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE INDEX ix_po_supplier   ON erp_purchasing.purchase_order(tenant_id, company_id, supplier_id);
CREATE INDEX ix_po_status     ON erp_purchasing.purchase_order(tenant_id, company_id, order_status);
CREATE INDEX ix_grn_po        ON erp_purchasing.goods_received_note(tenant_id, company_id, purchase_order_id);
CREATE INDEX ix_grn_status    ON erp_purchasing.goods_received_note(tenant_id, company_id, grn_status);
CREATE INDEX ix_outbox_unpub  ON erp_purchasing.outbox_event(occurred_at) WHERE published_at IS NULL;

-- â”€â”€ Row-Level Security â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

DO $$
DECLARE tname TEXT;
BEGIN
  FOREACH tname IN ARRAY ARRAY[
    'supplier','purchase_order','purchase_order_line',
    'goods_received_note','goods_received_note_line','outbox_event'
  ] LOOP
    EXECUTE format('ALTER TABLE erp_purchasing.%I ENABLE ROW LEVEL SECURITY', tname);
    EXECUTE format('ALTER TABLE erp_purchasing.%I FORCE ROW LEVEL SECURITY', tname);
    EXECUTE format(
      $p$CREATE POLICY tenant_company_scope ON erp_purchasing.%I
         USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
         WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)$p$,
      tname
    );
  END LOOP;
END $$;

-- â”€â”€ Grants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

GRANT USAGE ON SCHEMA erp_purchasing TO erp_app;
GRANT SELECT, INSERT, UPDATE ON
  erp_purchasing.supplier,
  erp_purchasing.purchase_order,
  erp_purchasing.purchase_order_line,
  erp_purchasing.goods_received_note,
  erp_purchasing.goods_received_note_line,
  erp_purchasing.outbox_event
TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('011_purchasing', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 012_saas_tenant_onboarding.sql
-- ---------------------------------------------------------------------------
-- Migration 012 â€“ SaaS tenant onboarding, subscription plans, and provisioning foundation
-- Implements Phase 1 & 2 baseline according to MASTER-ERP-001.

-- â”€â”€ 1. Default subscription plans and features â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

-- â”€â”€ 2. Tenant Provisioning Jobs table â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

-- â”€â”€ 3. Least-privilege Grants for SaaS Onboarding â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('012_saas_tenant_onboarding', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 013_double_entry_accounting.sql
-- ---------------------------------------------------------------------------
-- Migration 013 â€“ Finance & Double-Entry Accounting Foundation
-- PostgreSQL schema for Chart of Accounts, Fiscal Years/Periods, Journals, and Ledger Posting.

CREATE SCHEMA IF NOT EXISTS finance;

-- â”€â”€ 1. Currencies â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE IF NOT EXISTS finance.currencies (
    code char(3) PRIMARY KEY,
    name text NOT NULL,
    minor_unit smallint NOT NULL CHECK (minor_unit BETWEEN 0 AND 4),
    is_active boolean NOT NULL DEFAULT true
);

INSERT INTO finance.currencies (code, name, minor_unit)
VALUES
    ('PKR', 'Pakistani Rupee', 2),
    ('USD', 'US Dollar', 2),
    ('EUR', 'Euro', 2),
    ('GBP', 'Pound Sterling', 2),
    ('AED', 'UAE Dirham', 2),
    ('SAR', 'Saudi Riyal', 2)
ON CONFLICT (code) DO NOTHING;

-- â”€â”€ 2. Fiscal Years & Periods â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE IF NOT EXISTS finance.fiscal_years (
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    code text NOT NULL,
    starts_on date NOT NULL,
    ends_on date NOT NULL,
    status text NOT NULL DEFAULT 'open' CHECK (status IN ('open','closed')),
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, id),
    UNIQUE (tenant_id, company_id, code),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    CHECK (ends_on >= starts_on)
);

CREATE TABLE IF NOT EXISTS finance.fiscal_periods (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    fiscal_year_id uuid NOT NULL,
    period_no smallint NOT NULL CHECK (period_no BETWEEN 1 AND 16),
    name text NOT NULL,
    starts_on date NOT NULL,
    ends_on date NOT NULL,
    status text NOT NULL DEFAULT 'open' CHECK (status IN ('open','soft_closed','closed')),
    closed_at timestamptz,
    closed_by uuid REFERENCES identity.users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, id),
    UNIQUE (tenant_id, company_id, fiscal_year_id, period_no),
    FOREIGN KEY (tenant_id, company_id, fiscal_year_id)
        REFERENCES finance.fiscal_years(tenant_id, company_id, id),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    CHECK (ends_on >= starts_on)
);

-- â”€â”€ 3. Chart of Accounts â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE IF NOT EXISTS finance.accounts (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    account_code text NOT NULL,
    account_name text NOT NULL,
    account_type text NOT NULL CHECK (
        account_type IN ('asset','liability','equity','revenue','expense')
    ),
    normal_balance text NOT NULL CHECK (normal_balance IN ('debit','credit')),
    parent_account_id uuid,
    currency_code char(3) REFERENCES finance.currencies(code),
    is_control_account boolean NOT NULL DEFAULT false,
    allow_manual_posting boolean NOT NULL DEFAULT true,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, id),
    UNIQUE (tenant_id, company_id, account_code),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, parent_account_id)
        REFERENCES finance.accounts(tenant_id, company_id, id)
);

CREATE INDEX IF NOT EXISTS ix_finance_accounts_code ON finance.accounts(tenant_id, company_id, account_code);
CREATE INDEX IF NOT EXISTS ix_finance_accounts_type ON finance.accounts(tenant_id, company_id, account_type);

-- â”€â”€ 4. Exchange Rates â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE IF NOT EXISTS finance.exchange_rates (
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    from_currency char(3) NOT NULL REFERENCES finance.currencies(code),
    to_currency char(3) NOT NULL REFERENCES finance.currencies(code),
    effective_at timestamptz NOT NULL,
    rate numeric(20,10) NOT NULL CHECK (rate > 0),
    source text NOT NULL DEFAULT 'manual',
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, id),
    CHECK (from_currency <> to_currency)
);

-- â”€â”€ 5. General Ledger Journals & Lines â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE IF NOT EXISTS finance.journals (
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    journal_no text NOT NULL,
    journal_date date NOT NULL,
    fiscal_period_id uuid NOT NULL,
    description text NOT NULL,
    functional_currency char(3) NOT NULL REFERENCES finance.currencies(code),
    source_module text NOT NULL,
    source_type text NOT NULL,
    source_id text NOT NULL,
    idempotency_key text NOT NULL,
    status text NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft','submitted','approved','posted','reversed')),
    created_by uuid NOT NULL REFERENCES identity.users(id),
    approved_by uuid REFERENCES identity.users(id),
    posted_by uuid REFERENCES identity.users(id),
    posted_at timestamptz,
    reverses_journal_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, id),
    UNIQUE (tenant_id, company_id, journal_no),
    UNIQUE (tenant_id, company_id, idempotency_key),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, fiscal_period_id)
        REFERENCES finance.fiscal_periods(tenant_id, company_id, id),
    FOREIGN KEY (tenant_id, company_id, reverses_journal_id)
        REFERENCES finance.journals(tenant_id, company_id, id),
    CHECK ((status IN ('posted','reversed') AND posted_at IS NOT NULL AND posted_by IS NOT NULL)
        OR status NOT IN ('posted','reversed'))
);

CREATE TABLE IF NOT EXISTS finance.journal_lines (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    journal_id uuid NOT NULL,
    line_no integer NOT NULL CHECK (line_no > 0),
    account_id uuid NOT NULL,
    description text,
    transaction_currency char(3) NOT NULL REFERENCES finance.currencies(code),
    transaction_amount numeric(20,6) NOT NULL CHECK (transaction_amount > 0),
    exchange_rate numeric(20,10) NOT NULL CHECK (exchange_rate > 0),
    debit_functional numeric(20,4) NOT NULL DEFAULT 0 CHECK (debit_functional >= 0),
    credit_functional numeric(20,4) NOT NULL DEFAULT 0 CHECK (credit_functional >= 0),
    department_id uuid,
    location_id uuid,
    cost_center_id uuid,
    PRIMARY KEY (tenant_id, company_id, journal_id, line_no),
    FOREIGN KEY (tenant_id, company_id, journal_id)
        REFERENCES finance.journals(tenant_id, company_id, id),
    FOREIGN KEY (tenant_id, company_id, account_id)
        REFERENCES finance.accounts(tenant_id, company_id, id),
    FOREIGN KEY (tenant_id, company_id, location_id)
        REFERENCES organization.locations(tenant_id, company_id, id),
    CHECK (
        (debit_functional > 0 AND credit_functional = 0)
        OR (credit_functional > 0 AND debit_functional = 0)
    )
);

CREATE INDEX IF NOT EXISTS ix_journals_posting_date ON finance.journals(tenant_id, company_id, journal_date);
CREATE INDEX IF NOT EXISTS ix_journals_source ON finance.journals(tenant_id, company_id, source_module, source_type, source_id);
CREATE INDEX IF NOT EXISTS ix_journal_lines_account ON finance.journal_lines(tenant_id, company_id, account_id, journal_id);

-- â”€â”€ 6. Balance Verification Constraint Trigger â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE OR REPLACE FUNCTION finance.assert_posted_journal_balanced()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_tenant uuid;
    v_company uuid;
    v_journal uuid;
    v_status text;
    v_lines bigint;
    v_debits numeric(24,4);
    v_credits numeric(24,4);
BEGIN
    IF TG_TABLE_NAME = 'journal_lines' THEN
        v_tenant := COALESCE(NEW.tenant_id, OLD.tenant_id);
        v_company := COALESCE(NEW.company_id, OLD.company_id);
        v_journal := COALESCE(NEW.journal_id, OLD.journal_id);
    ELSE
        v_tenant := COALESCE(NEW.tenant_id, OLD.tenant_id);
        v_company := COALESCE(NEW.company_id, OLD.company_id);
        v_journal := COALESCE(NEW.id, OLD.id);
    END IF;

    SELECT status INTO v_status
    FROM finance.journals
    WHERE tenant_id = v_tenant AND company_id = v_company AND id = v_journal;

    IF v_status = 'posted' OR v_status = 'reversed' THEN
        SELECT count(*),
               COALESCE(sum(debit_functional), 0),
               COALESCE(sum(credit_functional), 0)
          INTO v_lines, v_debits, v_credits
        FROM finance.journal_lines
        WHERE tenant_id = v_tenant AND company_id = v_company AND journal_id = v_journal;

        IF v_lines < 2 OR v_debits <> v_credits THEN
            RAISE EXCEPTION 'Posted journal % is not balanced (lines %, debits %, credits %)',
                v_journal, v_lines, v_debits, v_credits;
        END IF;
    END IF;
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS journals_balanced_at_commit ON finance.journals;
CREATE CONSTRAINT TRIGGER journals_balanced_at_commit
AFTER INSERT OR UPDATE ON finance.journals
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION finance.assert_posted_journal_balanced();

DROP TRIGGER IF EXISTS journal_lines_balanced_at_commit ON finance.journal_lines;
CREATE CONSTRAINT TRIGGER journal_lines_balanced_at_commit
AFTER INSERT OR UPDATE OR DELETE ON finance.journal_lines
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION finance.assert_posted_journal_balanced();

-- Prevent mutation of lines belonging to posted journals
CREATE OR REPLACE FUNCTION finance.reject_posted_journal_line_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_status text;
BEGIN
    SELECT status INTO v_status
    FROM finance.journals
    WHERE tenant_id = COALESCE(OLD.tenant_id, NEW.tenant_id)
      AND company_id = COALESCE(OLD.company_id, NEW.company_id)
      AND id = COALESCE(OLD.journal_id, NEW.journal_id);

    IF v_status IN ('posted','reversed') THEN
        RAISE EXCEPTION 'Cannot mutate lines of a posted/reversed journal; reverse it instead';
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS journal_lines_immutable_after_post ON finance.journal_lines;
CREATE TRIGGER journal_lines_immutable_after_post
BEFORE UPDATE OR DELETE ON finance.journal_lines
FOR EACH ROW EXECUTE FUNCTION finance.reject_posted_journal_line_mutation();

-- â”€â”€ 7. Row-Level Security â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

DO $$
DECLARE t text;
BEGIN
  FOREACH t IN ARRAY ARRAY[
    'fiscal_years','fiscal_periods','accounts','journals','journal_lines'
  ] LOOP
    EXECUTE format('ALTER TABLE finance.%I ENABLE ROW LEVEL SECURITY', t);
    EXECUTE format('ALTER TABLE finance.%I FORCE ROW LEVEL SECURITY', t);
    EXECUTE format('DROP POLICY IF EXISTS tenant_company_scope ON finance.%I', t);
    EXECUTE format(
      'CREATE POLICY tenant_company_scope ON finance.%I
       USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid
          AND company_id = NULLIF(current_setting(''app.company_id'', true), '''')::uuid)
       WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid
          AND company_id = NULLIF(current_setting(''app.company_id'', true), '''')::uuid)', t
    );
  END LOOP;
END $$;

ALTER TABLE finance.exchange_rates ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.exchange_rates FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_scope ON finance.exchange_rates;
CREATE POLICY tenant_scope ON finance.exchange_rates
USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- â”€â”€ 8. Permissions & Least-Privilege Grants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

GRANT USAGE ON SCHEMA finance TO erp_app;
GRANT SELECT ON finance.currencies TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON
    finance.fiscal_years,
    finance.fiscal_periods,
    finance.accounts,
    finance.exchange_rates,
    finance.journals,
    finance.journal_lines
TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('013_double_entry_accounting', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 014_finance_chart_of_accounts_seed.sql
-- ---------------------------------------------------------------------------
-- [SKIPPED FOR PROD] Development demo tenant seed (014_finance_chart_of_accounts_seed.sql). Legitimate tenant setup is provisioned via SaaS onboarding.

INSERT INTO public.schema_migrations (version, applied_at) VALUES ('014_finance_chart_of_accounts_seed', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 015_sales_and_pos.sql
-- ---------------------------------------------------------------------------
-- Migration 015 â€“ Sales, POS, Customers & Outbox Events
-- Schema: erp_sales

CREATE SCHEMA IF NOT EXISTS erp_sales;

-- â”€â”€ 1. Customers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE IF NOT EXISTS erp_sales.customer (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    customer_id uuid NOT NULL DEFAULT gen_random_uuid(),
    customer_code text NOT NULL,
    name text NOT NULL,
    customer_type text NOT NULL DEFAULT 'retail' CHECK (customer_type IN ('retail', 'wholesale', 'distributor', 'corporate')),
    contact_name text,
    email text,
    phone text,
    address text,
    ntn_number text,
    strn_number text,
    credit_limit numeric(20,4) NOT NULL DEFAULT 0 CHECK (credit_limit >= 0),
    outstanding_balance numeric(20,4) NOT NULL DEFAULT 0,
    currency_code char(3) NOT NULL DEFAULT 'PKR',
    payment_terms_days integer NOT NULL DEFAULT 0 CHECK (payment_terms_days >= 0),
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, customer_id),
    UNIQUE (tenant_id, company_id, customer_code),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id)
);

CREATE INDEX IF NOT EXISTS ix_sales_customer_code ON erp_sales.customer(tenant_id, company_id, customer_code);
CREATE INDEX IF NOT EXISTS ix_sales_customer_name ON erp_sales.customer(tenant_id, company_id, name);

-- â”€â”€ 2. Sales Orders â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE IF NOT EXISTS erp_sales.sales_order (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    order_id uuid NOT NULL DEFAULT gen_random_uuid(),
    order_number text NOT NULL,
    customer_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    order_type text NOT NULL DEFAULT 'standard' CHECK (order_type IN ('standard', 'pos', 'wholesale')),
    order_status text NOT NULL DEFAULT 'draft' CHECK (order_status IN ('draft', 'confirmed', 'fulfilled', 'cancelled')),
    currency_code char(3) NOT NULL DEFAULT 'PKR',
    order_date date NOT NULL DEFAULT CURRENT_DATE,
    subtotal_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (subtotal_amount >= 0),
    tax_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    discount_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    total_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    note text,
    created_by uuid REFERENCES identity.users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, order_id),
    UNIQUE (tenant_id, company_id, order_number),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, customer_id)
        REFERENCES erp_sales.customer(tenant_id, company_id, customer_id),
    FOREIGN KEY (tenant_id, company_id, warehouse_id)
        REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE INDEX IF NOT EXISTS ix_sales_order_status ON erp_sales.sales_order(tenant_id, company_id, order_status);

CREATE TABLE IF NOT EXISTS erp_sales.sales_order_line (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    order_id uuid NOT NULL,
    line_number integer NOT NULL CHECK (line_number > 0),
    variant_id uuid NOT NULL,
    bin_id uuid NOT NULL,
    ordered_qty numeric(20,4) NOT NULL CHECK (ordered_qty > 0),
    fulfilled_qty numeric(20,4) NOT NULL DEFAULT 0 CHECK (fulfilled_qty >= 0),
    unit_of_measure_id uuid NOT NULL,
    unit_price numeric(20,4) NOT NULL CHECK (unit_price >= 0),
    tax_rate numeric(6,4) NOT NULL DEFAULT 0 CHECK (tax_rate >= 0),
    line_total numeric(20,4) NOT NULL CHECK (line_total >= 0),
    line_status text NOT NULL DEFAULT 'pending' CHECK (line_status IN ('pending', 'fulfilled', 'cancelled')),
    PRIMARY KEY (tenant_id, company_id, order_id, line_number),
    FOREIGN KEY (tenant_id, company_id, order_id)
        REFERENCES erp_sales.sales_order(tenant_id, company_id, order_id),
    FOREIGN KEY (tenant_id, variant_id)
        REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
    FOREIGN KEY (tenant_id, company_id, bin_id)
        REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id),
    FOREIGN KEY (tenant_id, unit_of_measure_id)
        REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id)
);

-- â”€â”€ 3. Sales Invoices & POS Receipts â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE IF NOT EXISTS erp_sales.sales_invoice (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    invoice_id uuid NOT NULL DEFAULT gen_random_uuid(),
    invoice_number text NOT NULL,
    client_transaction_id uuid NOT NULL,
    order_id uuid,
    customer_id uuid NOT NULL,
    payment_method text NOT NULL DEFAULT 'cash' CHECK (payment_method IN ('cash', 'credit', 'bank_transfer', 'card', 'mobile_wallet')),
    invoice_status text NOT NULL DEFAULT 'posted' CHECK (invoice_status IN ('posted', 'paid', 'reversed')),
    invoice_date date NOT NULL DEFAULT CURRENT_DATE,
    subtotal_amount numeric(20,4) NOT NULL CHECK (subtotal_amount >= 0),
    tax_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    discount_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    total_amount numeric(20,4) NOT NULL CHECK (total_amount >= 0),
    paid_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (paid_amount >= 0),
    note text,
    created_by uuid REFERENCES identity.users(id),
    posted_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, invoice_id),
    UNIQUE (tenant_id, company_id, invoice_number),
    UNIQUE (tenant_id, client_transaction_id),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, customer_id)
        REFERENCES erp_sales.customer(tenant_id, company_id, customer_id)
);

CREATE INDEX IF NOT EXISTS ix_sales_invoice_status ON erp_sales.sales_invoice(tenant_id, company_id, invoice_status);

CREATE TABLE IF NOT EXISTS erp_sales.sales_invoice_line (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    invoice_id uuid NOT NULL,
    line_number integer NOT NULL CHECK (line_number > 0),
    order_line_number integer,
    variant_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    bin_id uuid NOT NULL,
    quantity numeric(20,4) NOT NULL CHECK (quantity > 0),
    unit_of_measure_id uuid NOT NULL,
    unit_price numeric(20,4) NOT NULL CHECK (unit_price >= 0),
    unit_cost numeric(20,4) NOT NULL DEFAULT 0 CHECK (unit_cost >= 0),
    tax_rate numeric(6,4) NOT NULL DEFAULT 0 CHECK (tax_rate >= 0),
    line_total numeric(20,4) NOT NULL CHECK (line_total >= 0),
    PRIMARY KEY (tenant_id, company_id, invoice_id, line_number),
    FOREIGN KEY (tenant_id, company_id, invoice_id)
        REFERENCES erp_sales.sales_invoice(tenant_id, company_id, invoice_id),
    FOREIGN KEY (tenant_id, variant_id)
        REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
    FOREIGN KEY (tenant_id, company_id, warehouse_id)
        REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id),
    FOREIGN KEY (tenant_id, company_id, bin_id)
        REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id),
    FOREIGN KEY (tenant_id, unit_of_measure_id)
        REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id)
);

-- â”€â”€ 4. Outbox Events (Sales â†’ Inventory Relief & General Ledger) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

CREATE TABLE IF NOT EXISTS erp_sales.outbox_event (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    event_id uuid NOT NULL DEFAULT gen_random_uuid(),
    invoice_id uuid NOT NULL,
    event_type text NOT NULL,
    payload jsonb NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    PRIMARY KEY (tenant_id, company_id, event_id),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, invoice_id)
        REFERENCES erp_sales.sales_invoice(tenant_id, company_id, invoice_id)
);

CREATE INDEX IF NOT EXISTS ix_sales_outbox_unpub ON erp_sales.outbox_event(occurred_at) WHERE published_at IS NULL;

-- â”€â”€ 5. Row-Level Security â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

DO $$
DECLARE
    tbl text;
    approved_tables text[] := ARRAY['customer', 'sales_order', 'sales_order_line', 'sales_invoice', 'sales_invoice_line', 'outbox_event'];
    existing_pol RECORD;
    pol_count int;
    expected_expr text := '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))';
BEGIN
    -- 1. Assert all 6 approved sales tables exist prior to RLS processing
    FOREACH tbl IN ARRAY approved_tables LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'erp_sales' AND tablename = tbl) THEN
            RAISE EXCEPTION 'Mandatory sales table erp_sales.% does not exist before RLS processing!', tbl;
        END IF;
    END LOOP;

    -- 2. Assert no unauthorized tables exist in erp_sales
    FOR tbl IN SELECT tablename FROM pg_tables WHERE schemaname = 'erp_sales'
    LOOP
        IF tbl != ALL(approved_tables) THEN
            RAISE EXCEPTION 'Unauthorized table erp_sales.% detected outside approved sales inventory!', tbl;
        END IF;
    END LOOP;

    -- 3. Process each approved table safely
    FOREACH tbl IN ARRAY approved_tables
    LOOP
        EXECUTE format('ALTER TABLE erp_sales.%I ENABLE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('ALTER TABLE erp_sales.%I FORCE ROW LEVEL SECURITY;', tbl);

        SELECT count(*)
        INTO pol_count
        FROM pg_policy p
        JOIN pg_class c ON c.oid = p.polrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'erp_sales' AND c.relname = tbl;

        IF pol_count = 0 THEN
            EXECUTE format(
              $p$CREATE POLICY tenant_company_scope ON erp_sales.%I
                 USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                    AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
                 WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                    AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)$p$,
              tbl
            );
        ELSIF pol_count = 1 THEN
            SELECT p.polname, p.polcmd, p.polpermissive, p.polroles,
                   pg_get_expr(p.polqual, p.polrelid) AS using_expr,
                   pg_get_expr(p.polwithcheck, p.polrelid) AS check_expr
            INTO existing_pol
            FROM pg_policy p
            JOIN pg_class c ON c.oid = p.polrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'erp_sales' AND c.relname = tbl AND p.polname = 'tenant_company_scope';

            IF NOT FOUND THEN
                RAISE EXCEPTION 'Single existing policy on erp_sales.% is not named tenant_company_scope!', tbl;
            END IF;

            IF existing_pol.polcmd IS DISTINCT FROM '*' OR
               existing_pol.polpermissive IS DISTINCT FROM true OR
               existing_pol.polroles::text IS DISTINCT FROM '{0}' OR
               existing_pol.using_expr IS NULL OR
               existing_pol.check_expr IS NULL OR
               existing_pol.using_expr IS DISTINCT FROM expected_expr OR
               existing_pol.check_expr IS DISTINCT FROM expected_expr THEN
                RAISE EXCEPTION 'Existing policy tenant_company_scope on erp_sales.% diverges from mandatory security semantics!', tbl;
            END IF;
        ELSE
            RAISE EXCEPTION 'Multiple policies (% detected) on erp_sales.%; additional permissive policies threaten tenant isolation!', pol_count, tbl;
        END IF;
    END LOOP;
END $$;

-- â”€â”€ 6. Permissions Grant â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

GRANT USAGE ON SCHEMA erp_sales TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA erp_sales TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('015_sales_and_pos', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 016_sales_seed.sql
-- ---------------------------------------------------------------------------
-- [SKIPPED FOR PROD] Development demo tenant seed (016_sales_seed.sql). Legitimate tenant setup is provisioned via SaaS onboarding.

INSERT INTO public.schema_migrations (version, applied_at) VALUES ('016_sales_seed', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 017_offline_sync_hardening.sql
-- ---------------------------------------------------------------------------
-- Migration 017: Harden offline receipt sync for concurrent requests, crash-recoverable leases, and effective location enforcement.

-- 1. Add lease tracking columns to sync.operation_inbox
ALTER TABLE sync.operation_inbox
    ADD COLUMN IF NOT EXISTS claim_token uuid,
    ADD COLUMN IF NOT EXISTS claimed_at timestamptz,
    ADD COLUMN IF NOT EXISTS claim_expires_at timestamptz;

CREATE INDEX IF NOT EXISTS operation_inbox_lease_idx
    ON sync.operation_inbox(tenant_id, company_id, operation_id, outcome, claim_expires_at);

-- 2. Add location_id to erp_inventory.warehouse to support location-based authorization
ALTER TABLE erp_inventory.warehouse
    ADD COLUMN IF NOT EXISTS location_id uuid;

DO 'BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = ''warehouse_location_fkey''
    ) THEN
        ALTER TABLE erp_inventory.warehouse
            ADD CONSTRAINT warehouse_location_fkey
            FOREIGN KEY (tenant_id, company_id, location_id)
            REFERENCES organization.locations(tenant_id, company_id, id);
    END IF;
END;';

-- 3. Ensure permissions for erp_app
GRANT SELECT, INSERT, UPDATE ON sync.operation_inbox TO erp_app;
GRANT SELECT ON sync.device_company_scopes, sync.device_location_scopes, sync.offline_grants TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_inventory.warehouse TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('017_offline_sync_hardening', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 018_command_idempotency_company_scope_and_conflicts.sql
-- ---------------------------------------------------------------------------
-- Migration 018: Add company_id to command_idempotency and create sync.operation_conflicts table

-- 1. Add company_id to erp_inventory.command_idempotency
ALTER TABLE erp_inventory.command_idempotency
    ADD COLUMN IF NOT EXISTS company_id uuid;

DO 'BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = ''command_idempotency_company_fkey'') THEN
        ALTER TABLE erp_inventory.command_idempotency
            ADD CONSTRAINT command_idempotency_company_fkey
            FOREIGN KEY (tenant_id, company_id)
            REFERENCES organization.companies(tenant_id, id);
    END IF;
END;';

-- 2. Durable conflict store for out-of-order or reused device sequence conflicts
CREATE TABLE IF NOT EXISTS sync.operation_conflicts (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    device_id uuid NOT NULL,
    device_sequence bigint NOT NULL,
    conflict_code text NOT NULL,
    conflicting_operation_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, operation_id),
    FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id)
);

ALTER TABLE sync.operation_conflicts ENABLE ROW LEVEL SECURITY;
ALTER TABLE sync.operation_conflicts FORCE ROW LEVEL SECURITY;

DO 'BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = ''tenant_company_scope'' AND tablename = ''operation_conflicts'') THEN
        CREATE POLICY tenant_company_scope ON sync.operation_conflicts
            USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid
               AND company_id = NULLIF(current_setting(''app.company_id'', true), '''')::uuid)
            WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid
               AND company_id = NULLIF(current_setting(''app.company_id'', true), '''')::uuid);
    END IF;
END;';

GRANT SELECT, INSERT, UPDATE ON sync.operation_conflicts TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_inventory.command_idempotency TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('018_command_idempotency_company_scope_and_conflicts', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 019_purchasing_outbox_dead_letter.sql
-- ---------------------------------------------------------------------------
-- Migration 019: Purchasing Outbox Poison-Event Handling and Dead-Letter Queue
-- Adds explicit status, error tracking, exponential backoff timestamp, and redrive audit tracking.

ALTER TABLE erp_purchasing.outbox_event
  ADD COLUMN IF NOT EXISTS status TEXT NOT NULL DEFAULT 'pending'
    CHECK (status IN ('pending', 'published', 'dead_letter')),
  ADD COLUMN IF NOT EXISTS last_error TEXT,
  ADD COLUMN IF NOT EXISTS next_retry_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS redrive_count INTEGER NOT NULL DEFAULT 0 CHECK (redrive_count >= 0),
  ADD COLUMN IF NOT EXISTS last_redriven_at TIMESTAMPTZ;

-- Safe backfill for legacy published rows:
-- MUST NOT overwrite existing dead-letter, retry, redriven, or operator-triaged records.
UPDATE erp_purchasing.outbox_event
SET status = 'published'
WHERE published_at IS NOT NULL
  AND status = 'pending'
  AND status != 'dead_letter'
  AND last_error IS NULL
  AND next_retry_at IS NULL
  AND redrive_count = 0
  AND last_redriven_at IS NULL;

-- Index for pending events eligible for processing/retry
CREATE INDEX IF NOT EXISTS ix_purchasing_outbox_pending
  ON erp_purchasing.outbox_event(tenant_id, company_id, occurred_at)
  WHERE status = 'pending';

-- Index for dead-letter events awaiting audit or redrive
CREATE INDEX IF NOT EXISTS ix_purchasing_outbox_dead_letter
  ON erp_purchasing.outbox_event(tenant_id, company_id, occurred_at)
  WHERE status = 'dead_letter';

GRANT SELECT, INSERT, UPDATE ON erp_purchasing.outbox_event TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('019_purchasing_outbox_dead_letter', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 020_reconcile_application_runtime_privileges.sql
-- ---------------------------------------------------------------------------
-- Migration 020: Reconcile Application Runtime Privileges for erp_app
-- Authoritative reconciliation of least-privileged access for sales orders, POS checkout,
-- purchasing workflow, and public subscription plans exploration.
-- Preserves Forced Row-Level Security (RLS) and strict tenant/company isolation.

-- 1. erp_sales schema grants
GRANT USAGE ON SCHEMA erp_sales TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_sales.customer TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_sales.sales_order TO erp_app;
GRANT SELECT, INSERT ON erp_sales.sales_order_line TO erp_app;
GRANT SELECT, INSERT ON erp_sales.sales_invoice TO erp_app;
GRANT SELECT, INSERT ON erp_sales.sales_invoice_line TO erp_app;
GRANT SELECT, INSERT ON erp_sales.outbox_event TO erp_app;

-- 2. erp_purchasing schema grants
GRANT USAGE ON SCHEMA erp_purchasing TO erp_app;
GRANT SELECT, INSERT ON erp_purchasing.supplier TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_purchasing.purchase_order TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_purchasing.purchase_order_line TO erp_app;
GRANT SELECT, INSERT ON erp_purchasing.goods_received_note TO erp_app;
GRANT SELECT, INSERT ON erp_purchasing.goods_received_note_line TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_purchasing.outbox_event TO erp_app;

-- 3. platform schema grants (public plan exploration catalog)
GRANT USAGE ON SCHEMA platform TO erp_app;
GRANT SELECT ON platform.subscription_plans TO erp_app;
GRANT SELECT ON platform.plan_features TO erp_app;

-- 4. Fail-closed defense: Explicitly ensure DELETE, TRUNCATE, REFERENCES, TRIGGER remain strictly denied
REVOKE DELETE, TRUNCATE, REFERENCES, TRIGGER ON ALL TABLES IN SCHEMA erp_sales FROM erp_app;
REVOKE DELETE, TRUNCATE, REFERENCES, TRIGGER ON ALL TABLES IN SCHEMA erp_purchasing FROM erp_app;
REVOKE INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON platform.subscription_plans, platform.plan_features FROM erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('020_reconcile_application_runtime_privileges', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 021_grant_context_listing_routine_privilege.sql
-- ---------------------------------------------------------------------------
-- Migration 021: Grant Context Listing and Authentication Resolution Routine Privileges for erp_app
-- Authoritative least-privileged grants for identity.list_user_company_contexts(text, text)
-- and identity.resolve_authenticated_user_context(uuid, uuid, text, text).
-- Enables authenticated OIDC and Firebase users to query their company and tenant memberships
-- via GET /api/v1/contexts and securely resolves active user tenant context in middleware.
-- Preserves Forced Row-Level Security (RLS) and strict tenant/company isolation.

CREATE OR REPLACE FUNCTION identity.resolve_authenticated_user_context(
    p_tenant_id uuid,
    p_company_id uuid,
    p_issuer text,
    p_subject text
)
RETURNS uuid
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'identity', 'platform', 'organization'
AS $$
    SELECT u.id
    FROM identity.external_identities AS external_identity
    JOIN identity.users AS u ON u.id = external_identity.user_id
    JOIN identity.tenant_memberships AS tenant_membership
      ON tenant_membership.tenant_id = p_tenant_id
     AND tenant_membership.user_id = u.id
    JOIN identity.company_memberships AS company_membership
      ON company_membership.tenant_id = p_tenant_id
     AND company_membership.company_id = p_company_id
     AND company_membership.user_id = u.id
    JOIN platform.tenants AS tenant ON tenant.id = p_tenant_id
    JOIN organization.companies AS company
      ON company.tenant_id = p_tenant_id AND company.id = p_company_id
    WHERE external_identity.issuer = p_issuer
      AND external_identity.subject = p_subject
      AND u.status = 'active'
      AND tenant_membership.status = 'active'
      AND company_membership.status = 'active'
      AND tenant.status = 'active'
      AND company.status = 'active'
    LIMIT 1;
$$;

ALTER FUNCTION identity.resolve_authenticated_user_context(uuid, uuid, text, text) OWNER TO erp_dev;

GRANT EXECUTE ON FUNCTION identity.list_user_company_contexts(text, text) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.resolve_authenticated_user_context(uuid, uuid, text, text) TO erp_app;
GRANT INSERT ON erp_inventory.product TO erp_app;

CREATE OR REPLACE FUNCTION identity.user_has_permission(
    p_tenant_id uuid,
    p_company_id uuid,
    p_user_id uuid,
    p_permission_code text
)
RETURNS boolean
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'identity', 'authorization'
AS $$
    SELECT EXISTS (
      SELECT 1
      FROM identity.company_memberships AS cm
      JOIN identity.tenant_memberships AS tm
        ON tm.tenant_id = cm.tenant_id
       AND tm.user_id = cm.user_id
      JOIN "authorization".membership_role_assignments AS assignment
        ON assignment.tenant_id = tm.tenant_id
       AND assignment.membership_id = tm.id
      JOIN "authorization".role_permissions AS permission
        ON permission.tenant_id = assignment.tenant_id
       AND permission.role_id = assignment.role_id
      WHERE cm.tenant_id = p_tenant_id
        AND cm.company_id = p_company_id
        AND cm.user_id = p_user_id
        AND cm.status = 'active'
        AND tm.status = 'active'
        AND assignment.effective_from <= now()
        AND (assignment.effective_until IS NULL OR assignment.effective_until > now())
        AND permission.permission_code = p_permission_code
        AND permission.scope_type IN ('tenant','company')
    );
$$;

ALTER FUNCTION identity.user_has_permission(uuid, uuid, uuid, text) OWNER TO erp_dev;

GRANT EXECUTE ON FUNCTION identity.user_has_permission(uuid, uuid, uuid, text) TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('021_grant_context_listing_routine_privilege', now()) ON CONFLICT (version) DO NOTHING;

-- ---------------------------------------------------------------------------
-- Migration: 022_complete_erp_foundation.sql
-- ---------------------------------------------------------------------------
-- Migration 022: Complete ERP Foundation Routines
-- Provides encapsulated SECURITY DEFINER routines for User Profile, Company Settings,
-- User Memberships, Roles/Permissions, and Audit Logging under Forced Row-Level Security.

-- 1. User Profile Routine
CREATE OR REPLACE FUNCTION identity.get_user_profile(
    p_tenant_id uuid,
    p_company_id uuid,
    p_user_id uuid
)
RETURNS jsonb
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'identity', 'platform', 'organization', 'authorization'
AS $$
    SELECT jsonb_build_object(
        'userId', u.id,
        'email', u.email_normalized,
        'displayName', u.display_name,
        'status', u.status,
        'createdAt', u.created_at,
        'tenantId', t.id,
        'tenantCode', t.tenant_code,
        'tenantName', t.display_name,
        'companyId', c.id,
        'companyCode', c.company_code,
        'companyName', c.name,
        'legalName', c.legal_name,
        'registrationNumber', c.registration_number,
        'currencyCode', c.base_currency_code,
        'timezone', c.timezone,
        'roles', COALESCE((
            SELECT jsonb_agg(jsonb_build_object('roleCode', r.role_code, 'name', r.name))
            FROM "authorization".membership_role_assignments mra
            JOIN identity.tenant_memberships tm ON tm.id = mra.membership_id
            JOIN "authorization".roles r ON r.id = mra.role_id
            WHERE tm.tenant_id = p_tenant_id AND tm.user_id = p_user_id
        ), '[]'::jsonb),
        'permissions', COALESCE((
            SELECT jsonb_agg(DISTINCT rp.permission_code)
            FROM "authorization".membership_role_assignments mra
            JOIN identity.tenant_memberships tm ON tm.id = mra.membership_id
            JOIN "authorization".role_permissions rp ON rp.role_id = mra.role_id AND rp.tenant_id = p_tenant_id
            WHERE tm.tenant_id = p_tenant_id AND tm.user_id = p_user_id
        ), '[]'::jsonb)
    )
    FROM identity.users u
    JOIN platform.tenants t ON t.id = p_tenant_id
    JOIN organization.companies c ON c.id = p_company_id AND c.tenant_id = p_tenant_id
    WHERE u.id = p_user_id;
$$;

ALTER FUNCTION identity.get_user_profile(uuid, uuid, uuid) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.get_user_profile(uuid, uuid, uuid) TO erp_app;

-- 2. Company Settings Routine (Get and Update)
CREATE OR REPLACE FUNCTION identity.get_company_settings(
    p_tenant_id uuid,
    p_company_id uuid
)
RETURNS jsonb
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'organization'
AS $$
    SELECT jsonb_build_object(
        'companyId', c.id,
        'tenantId', c.tenant_id,
        'companyCode', c.company_code,
        'name', c.name,
        'legalName', c.legal_name,
        'registrationNumber', c.registration_number,
        'countryCode', c.country_code,
        'currencyCode', c.base_currency_code,
        'timezone', c.timezone,
        'fiscalYearStartMonth', c.fiscal_year_start_month,
        'status', c.status,
        'updatedAt', c.updated_at
    )
    FROM organization.companies c
    WHERE c.tenant_id = p_tenant_id AND c.id = p_company_id;
$$;

ALTER FUNCTION identity.get_company_settings(uuid, uuid) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.get_company_settings(uuid, uuid) TO erp_app;

CREATE OR REPLACE FUNCTION identity.update_company_settings(
    p_tenant_id uuid,
    p_company_id uuid,
    p_name text,
    p_legal_name text,
    p_registration_number text,
    p_timezone text
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path TO 'pg_catalog', 'organization'
AS $$
DECLARE
    v_result jsonb;
BEGIN
    UPDATE organization.companies
    SET name = COALESCE(NULLIF(TRIM(p_name), ''), name),
        legal_name = COALESCE(NULLIF(TRIM(p_legal_name), ''), legal_name),
        registration_number = COALESCE(NULLIF(TRIM(p_registration_number), ''), registration_number),
        timezone = COALESCE(NULLIF(TRIM(p_timezone), ''), timezone),
        updated_at = now()
    WHERE tenant_id = p_tenant_id AND id = p_company_id;

    SELECT identity.get_company_settings(p_tenant_id, p_company_id) INTO v_result;
    RETURN v_result;
END;
$$;

ALTER FUNCTION identity.update_company_settings(uuid, uuid, text, text, text, text) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.update_company_settings(uuid, uuid, text, text, text, text) TO erp_app;

-- 3. Company Users Listing Routine
CREATE OR REPLACE FUNCTION identity.list_company_users(
    p_tenant_id uuid,
    p_company_id uuid
)
RETURNS TABLE (
    user_id uuid,
    email text,
    display_name text,
    status text,
    roles jsonb,
    created_at timestamptz
)
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'identity', 'authorization'
AS $$
    SELECT 
        u.id AS user_id,
        u.email_normalized AS email,
        u.display_name,
        cm.status,
        COALESCE((
            SELECT jsonb_agg(jsonb_build_object('roleCode', r.role_code, 'name', r.name))
            FROM "authorization".membership_role_assignments mra
            JOIN identity.tenant_memberships tm ON tm.id = mra.membership_id
            JOIN "authorization".roles r ON r.id = mra.role_id
            WHERE tm.tenant_id = p_tenant_id AND tm.user_id = u.id
        ), '[]'::jsonb) AS roles,
        cm.created_at
    FROM identity.company_memberships cm
    JOIN identity.users u ON u.id = cm.user_id
    WHERE cm.tenant_id = p_tenant_id AND cm.company_id = p_company_id
    ORDER BY u.display_name;
$$;

ALTER FUNCTION identity.list_company_users(uuid, uuid) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.list_company_users(uuid, uuid) TO erp_app;

-- 4. User Invitation / Assignment Routine
CREATE OR REPLACE FUNCTION identity.invite_or_assign_company_user(
    p_tenant_id uuid,
    p_company_id uuid,
    p_email text,
    p_display_name text,
    p_role_code text
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path TO 'pg_catalog', 'identity', 'authorization'
AS $$
DECLARE
    v_user_id uuid;
    v_tm_id uuid;
    v_cm_id uuid;
    v_role_id uuid;
    v_clean_email text := LOWER(TRIM(p_email));
BEGIN
    SELECT id INTO v_user_id FROM identity.users WHERE email_normalized = v_clean_email;
    IF v_user_id IS NULL THEN
        INSERT INTO identity.users (email_normalized, display_name, status)
        VALUES (v_clean_email, TRIM(p_display_name), 'active')
        RETURNING id INTO v_user_id;
    END IF;

    SELECT id INTO v_tm_id FROM identity.tenant_memberships WHERE tenant_id = p_tenant_id AND user_id = v_user_id;
    IF v_tm_id IS NULL THEN
        INSERT INTO identity.tenant_memberships (tenant_id, user_id, status)
        VALUES (p_tenant_id, v_user_id, 'active')
        RETURNING id INTO v_tm_id;
    END IF;

    SELECT id INTO v_cm_id FROM identity.company_memberships WHERE tenant_id = p_tenant_id AND company_id = p_company_id AND user_id = v_user_id;
    IF v_cm_id IS NULL THEN
        INSERT INTO identity.company_memberships (tenant_id, company_id, user_id, status)
        VALUES (p_tenant_id, p_company_id, v_user_id, 'active')
        RETURNING id INTO v_cm_id;
    END IF;

    IF p_role_code IS NOT NULL AND p_role_code <> '' THEN
        SELECT id INTO v_role_id FROM "authorization".roles WHERE role_code = p_role_code AND (tenant_id = p_tenant_id OR tenant_id IS NULL) LIMIT 1;
        IF v_role_id IS NOT NULL THEN
            IF NOT EXISTS (
                SELECT 1 FROM "authorization".membership_role_assignments
                WHERE tenant_id = p_tenant_id AND membership_id = v_tm_id AND role_id = v_role_id
            ) THEN
                INSERT INTO "authorization".membership_role_assignments (tenant_id, membership_id, role_id, effective_from)
                VALUES (p_tenant_id, v_tm_id, v_role_id, now());
            END IF;
        END IF;
    END IF;

    RETURN jsonb_build_object(
        'userId', v_user_id,
        'email', v_clean_email,
        'displayName', p_display_name,
        'roleCode', p_role_code,
        'status', 'active'
    );
END;
$$;

ALTER FUNCTION identity.invite_or_assign_company_user(uuid, uuid, text, text, text) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.invite_or_assign_company_user(uuid, uuid, text, text, text) TO erp_app;

-- 5. List Roles and Permissions Routine
CREATE OR REPLACE FUNCTION identity.list_roles_and_permissions(
    p_tenant_id uuid
)
RETURNS TABLE (
    role_id uuid,
    role_code text,
    name text,
    is_system_role boolean,
    permissions jsonb
)
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'authorization'
AS $$
    SELECT 
        r.id AS role_id,
        r.role_code,
        r.name,
        r.is_system_role,
        COALESCE((
            SELECT jsonb_agg(jsonb_build_object('code', p.permission_code, 'description', p.description, 'module', p.module_code))
            FROM "authorization".role_permissions rp
            JOIN "authorization".permissions p ON p.permission_code = rp.permission_code
            WHERE rp.role_id = r.id AND (rp.tenant_id = p_tenant_id OR rp.tenant_id IS NULL)
        ), '[]'::jsonb) AS permissions
    FROM "authorization".roles r
    WHERE r.tenant_id = p_tenant_id OR r.tenant_id IS NULL
    ORDER BY r.role_code;
$$;

ALTER FUNCTION identity.list_roles_and_permissions(uuid) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.list_roles_and_permissions(uuid) TO erp_app;

-- 6. Audit Logging Routines
CREATE OR REPLACE FUNCTION identity.record_audit_event(
    p_tenant_id uuid,
    p_company_id uuid,
    p_actor_user_id uuid,
    p_event_code text,
    p_entity_type text,
    p_entity_id uuid,
    p_metadata jsonb
)
RETURNS uuid
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path TO 'pg_catalog', 'audit'
AS $$
DECLARE
    v_event_id uuid := gen_random_uuid();
BEGIN
    INSERT INTO audit.audit_events (
        id, tenant_id, company_id, actor_user_id, event_code,
        entity_type, entity_id, correlation_id, occurred_at, recorded_at, metadata
    )
    VALUES (
        v_event_id, p_tenant_id, p_company_id, p_actor_user_id, p_event_code,
        p_entity_type, p_entity_id, gen_random_uuid(), now(), now(), COALESCE(p_metadata, '{}'::jsonb)
    );
    RETURN v_event_id;
END;
$$;

ALTER FUNCTION identity.record_audit_event(uuid, uuid, uuid, text, text, uuid, jsonb) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.record_audit_event(uuid, uuid, uuid, text, text, uuid, jsonb) TO erp_app;

CREATE OR REPLACE FUNCTION identity.list_audit_events(
    p_tenant_id uuid,
    p_company_id uuid,
    p_limit int DEFAULT 50
)
RETURNS TABLE (
    event_id uuid,
    occurred_at timestamptz,
    actor_email text,
    actor_name text,
    event_code text,
    entity_type text,
    entity_id uuid,
    metadata jsonb
)
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'audit', 'identity'
AS $$
    SELECT 
        ae.id AS event_id,
        ae.occurred_at,
        u.email_normalized AS actor_email,
        u.display_name AS actor_name,
        ae.event_code,
        ae.entity_type,
        ae.entity_id,
        ae.metadata
    FROM audit.audit_events ae
    LEFT JOIN identity.users u ON u.id = ae.actor_user_id
    WHERE ae.tenant_id = p_tenant_id AND (ae.company_id = p_company_id OR ae.company_id IS NULL)
    ORDER BY ae.occurred_at DESC
    LIMIT LEAST(GREATEST(p_limit, 1), 200);
$$;

ALTER FUNCTION identity.list_audit_events(uuid, uuid, int) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.list_audit_events(uuid, uuid, int) TO erp_app;


INSERT INTO public.schema_migrations (version, applied_at) VALUES ('022_complete_erp_foundation', now()) ON CONFLICT (version) DO NOTHING;

COMMIT;
