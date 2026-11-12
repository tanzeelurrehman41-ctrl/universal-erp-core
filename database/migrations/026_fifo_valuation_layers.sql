-- Migration: 026_fifo_valuation_layers.sql
-- Module: inventory / sales / valuation
-- Purpose: Persist server-side FIFO receipt valuation layers and consumption allocations,
--          grant valuation_entry ledger privileges to erp_app, and register inventory.valuation.view permission.
-- Safety: Idempotent DDL, RLS-enforced (app.tenant_id & app.company_id), zero historical data mutations.

BEGIN;

-- 1. Register valuation view permission for authorized finance/inventory roles (never cashiers by default)
INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
  ('inventory.valuation.view', 'View server-side FIFO inventory valuation layers and unit cost details.', 'inventory')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. FIFO Cost Layers (one row per inbound receipt lot/layer)
CREATE TABLE IF NOT EXISTS erp_inventory.inventory_cost_layer (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  layer_id uuid NOT NULL DEFAULT gen_random_uuid(),
  variant_id uuid NOT NULL,
  warehouse_id uuid NOT NULL,
  bin_id uuid NOT NULL,
  status_code text NOT NULL DEFAULT 'available',
  source_document_type text NOT NULL,
  source_document_id uuid,
  source_line_number integer,
  source_movement_id uuid,
  received_at timestamptz NOT NULL DEFAULT now(),
  original_qty numeric(24,9) NOT NULL CHECK (original_qty > 0),
  remaining_qty numeric(24,9) NOT NULL CHECK (remaining_qty >= 0 AND remaining_qty <= original_qty),
  unit_cost numeric(24,9) NOT NULL CHECK (unit_cost > 0),
  currency_code char(3) NOT NULL DEFAULT 'PKR',
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, layer_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
  FOREIGN KEY (tenant_id, company_id, warehouse_id)
    REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id),
  FOREIGN KEY (tenant_id, company_id, bin_id)
    REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id),
  FOREIGN KEY (tenant_id, company_id, source_movement_id)
    REFERENCES erp_inventory.inventory_movement(tenant_id, company_id, movement_id)
);

CREATE INDEX IF NOT EXISTS ix_cost_layer_fifo_open
  ON erp_inventory.inventory_cost_layer (tenant_id, company_id, variant_id, received_at ASC, created_at ASC, layer_id ASC)
  WHERE remaining_qty > 0 AND status_code = 'available';

CREATE UNIQUE INDEX IF NOT EXISTS uq_cost_layer_source_line
  ON erp_inventory.inventory_cost_layer (tenant_id, company_id, source_document_type, source_document_id, source_line_number, bin_id)
  WHERE source_document_id IS NOT NULL AND source_line_number IS NOT NULL;

-- 3. FIFO Cost Layer Consumption Allocations (immutable audit trail per outbound line)
CREATE TABLE IF NOT EXISTS erp_inventory.inventory_cost_allocation (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  allocation_id uuid NOT NULL DEFAULT gen_random_uuid(),
  layer_id uuid NOT NULL,
  movement_id uuid NOT NULL,
  source_document_type text NOT NULL,
  source_document_id uuid NOT NULL,
  source_line_number integer NOT NULL,
  variant_id uuid NOT NULL,
  consumed_qty numeric(24,9) NOT NULL CHECK (consumed_qty > 0),
  unit_cost numeric(24,9) NOT NULL CHECK (unit_cost > 0),
  extended_cost numeric(24,9) NOT NULL CHECK (extended_cost > 0),
  currency_code char(3) NOT NULL DEFAULT 'PKR',
  allocated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, allocation_id),
  UNIQUE (tenant_id, company_id, source_document_type, source_document_id, source_line_number, layer_id),
  FOREIGN KEY (tenant_id, company_id, layer_id)
    REFERENCES erp_inventory.inventory_cost_layer(tenant_id, company_id, layer_id),
  FOREIGN KEY (tenant_id, company_id, movement_id)
    REFERENCES erp_inventory.inventory_movement(tenant_id, company_id, movement_id),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id)
);

CREATE INDEX IF NOT EXISTS ix_cost_allocation_source
  ON erp_inventory.inventory_cost_allocation (tenant_id, company_id, source_document_type, source_document_id);

-- 4. Force Row Level Security on new tables
ALTER TABLE erp_inventory.inventory_cost_layer ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_inventory.inventory_cost_layer FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON erp_inventory.inventory_cost_layer;
CREATE POLICY tenant_company_scope ON erp_inventory.inventory_cost_layer
  USING (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  )
  WITH CHECK (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  );

ALTER TABLE erp_inventory.inventory_cost_allocation ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_inventory.inventory_cost_allocation FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON erp_inventory.inventory_cost_allocation;
CREATE POLICY tenant_company_scope ON erp_inventory.inventory_cost_allocation
  USING (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  )
  WITH CHECK (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  );

-- 5. Least-privilege table grants for erp_app
GRANT SELECT, INSERT, UPDATE ON erp_inventory.inventory_cost_layer TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.inventory_cost_allocation TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.valuation_entry TO erp_app;

-- 6. Record migration version
INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('026_fifo_valuation_layers', now())
ON CONFLICT (version) DO NOTHING;

COMMIT;
