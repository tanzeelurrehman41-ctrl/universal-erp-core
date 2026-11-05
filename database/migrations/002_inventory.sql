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
