-- Migration: 027_complete_inventory_management.sql
-- Module: inventory (Phase 5 - Complete Inventory Management)
-- Purpose: Add stock reservation enhancements, picking/dispatch lists, cycle count sessions,
--          returns workflows, and register complete inventory management permissions.
-- Safety: Additive DDL, RLS-enforced (app.tenant_id & app.company_id), idempotent, zero historical data mutations.

BEGIN;

-- 1. Register Phase 5 Inventory Permissions
INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
  ('inventory.reservation.create', 'Create stock reservations against confirmed sales demands.', 'inventory'),
  ('inventory.reservation.release', 'Release or cancel active stock reservations.', 'inventory'),
  ('inventory.reservation.view', 'View stock reservations and Available-To-Promise calculations.', 'inventory'),
  ('inventory.pick.create', 'Generate warehouse inventory pick lists.', 'inventory'),
  ('inventory.pick.dispatch', 'Confirm picking and dispatch inventory issues.', 'inventory'),
  ('inventory.pick.view', 'View inventory pick lists and dispatch status.', 'inventory'),
  ('inventory.count.create', 'Initiate cycle count and physical stock count sessions.', 'inventory'),
  ('inventory.count.record', 'Record physical count quantities per bin and SKU.', 'inventory'),
  ('inventory.count.approve', 'Approve stock count sessions and post reconciliation variances.', 'inventory'),
  ('inventory.count.view', 'View stock count sessions and variance reports.', 'inventory'),
  ('inventory.adjustment.approve', 'Approve controlled stock adjustments exceeding routine thresholds.', 'inventory'),
  ('inventory.returns.create', 'Create customer and supplier return authorizations.', 'inventory'),
  ('inventory.returns.post', 'Receive or dispatch inventory returns and post movements.', 'inventory'),
  ('inventory.history.view', 'View bin-level inventory movement history and transaction audit logs.', 'inventory'),
  ('inventory.valuation.report', 'Generate real-time FIFO valuation and general ledger reconciliation reports.', 'inventory')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. Enhance stock_reservation with fulfillment tracking and notes
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns 
    WHERE table_schema = 'erp_inventory' AND table_name = 'stock_reservation' AND column_name = 'fulfilled_qty'
  ) THEN
    ALTER TABLE erp_inventory.stock_reservation ADD COLUMN fulfilled_qty numeric(24,9) NOT NULL DEFAULT 0 CHECK (fulfilled_qty >= 0);
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns 
    WHERE table_schema = 'erp_inventory' AND table_name = 'stock_reservation' AND column_name = 'released_at'
  ) THEN
    ALTER TABLE erp_inventory.stock_reservation ADD COLUMN released_at timestamptz;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns 
    WHERE table_schema = 'erp_inventory' AND table_name = 'stock_reservation' AND column_name = 'note'
  ) THEN
    ALTER TABLE erp_inventory.stock_reservation ADD COLUMN note text;
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_reservation_atp_lookup
  ON erp_inventory.stock_reservation (tenant_id, company_id, variant_id, warehouse_id, bin_id)
  WHERE reservation_status IN ('active', 'partially_fulfilled');

-- Ensure RLS on stock_reservation
ALTER TABLE erp_inventory.stock_reservation ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_inventory.stock_reservation FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON erp_inventory.stock_reservation;
CREATE POLICY tenant_company_scope ON erp_inventory.stock_reservation
  USING (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  )
  WITH CHECK (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  );

-- 3. Inventory Pick Lists & Pick Lines
CREATE TABLE IF NOT EXISTS erp_inventory.stock_pick_list (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  pick_list_id uuid NOT NULL DEFAULT gen_random_uuid(),
  pick_list_no text NOT NULL,
  warehouse_id uuid NOT NULL,
  source_document_type text NOT NULL,
  source_document_id uuid,
  status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'assigned', 'in_progress', 'picked', 'dispatched', 'cancelled')),
  assigned_to uuid,
  note text,
  created_at timestamptz NOT NULL DEFAULT now(),
  dispatched_at timestamptz,
  PRIMARY KEY (tenant_id, company_id, pick_list_id),
  UNIQUE (tenant_id, company_id, pick_list_no),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, company_id, warehouse_id)
    REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE TABLE IF NOT EXISTS erp_inventory.stock_pick_line (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  pick_line_id uuid NOT NULL DEFAULT gen_random_uuid(),
  pick_list_id uuid NOT NULL,
  reservation_id uuid,
  variant_id uuid NOT NULL,
  bin_id uuid NOT NULL,
  requested_qty numeric(24,9) NOT NULL CHECK (requested_qty > 0),
  picked_qty numeric(24,9) NOT NULL DEFAULT 0 CHECK (picked_qty >= 0),
  status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'picked', 'short')),
  note text,
  PRIMARY KEY (tenant_id, company_id, pick_line_id),
  FOREIGN KEY (tenant_id, company_id, pick_list_id)
    REFERENCES erp_inventory.stock_pick_list(tenant_id, company_id, pick_list_id) ON DELETE CASCADE,
  FOREIGN KEY (tenant_id, company_id, reservation_id)
    REFERENCES erp_inventory.stock_reservation(tenant_id, company_id, reservation_id),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
  FOREIGN KEY (tenant_id, company_id, bin_id)
    REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id)
);

CREATE INDEX IF NOT EXISTS ix_pick_list_status
  ON erp_inventory.stock_pick_list (tenant_id, company_id, warehouse_id, status);

ALTER TABLE erp_inventory.stock_pick_list ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_inventory.stock_pick_list FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON erp_inventory.stock_pick_list;
CREATE POLICY tenant_company_scope ON erp_inventory.stock_pick_list
  USING (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  )
  WITH CHECK (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  );

ALTER TABLE erp_inventory.stock_pick_line ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_inventory.stock_pick_line FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON erp_inventory.stock_pick_line;
CREATE POLICY tenant_company_scope ON erp_inventory.stock_pick_line
  USING (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  )
  WITH CHECK (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  );

-- 4. Stock Count Sessions & Count Lines (Cycle Counts & Full Counts)
CREATE TABLE IF NOT EXISTS erp_inventory.stock_count_session (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  session_id uuid NOT NULL DEFAULT gen_random_uuid(),
  count_number text NOT NULL,
  warehouse_id uuid NOT NULL,
  count_type text NOT NULL CHECK (count_type IN ('full', 'cycle', 'spot')),
  status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'in_progress', 'completed', 'approved', 'cancelled')),
  initiated_by uuid NOT NULL,
  approved_by uuid,
  created_at timestamptz NOT NULL DEFAULT now(),
  completed_at timestamptz,
  approved_at timestamptz,
  note text,
  PRIMARY KEY (tenant_id, company_id, session_id),
  UNIQUE (tenant_id, company_id, count_number),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, company_id, warehouse_id)
    REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE TABLE IF NOT EXISTS erp_inventory.stock_count_line (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  line_id uuid NOT NULL DEFAULT gen_random_uuid(),
  session_id uuid NOT NULL,
  variant_id uuid NOT NULL,
  bin_id uuid NOT NULL,
  snapshot_qty numeric(24,9) NOT NULL DEFAULT 0,
  counted_qty numeric(24,9),
  variance_qty numeric(24,9),
  unit_cost numeric(24,9) NOT NULL DEFAULT 0,
  is_reconciled boolean NOT NULL DEFAULT FALSE,
  reconciled_movement_id uuid,
  counted_by uuid,
  counted_at timestamptz,
  note text,
  PRIMARY KEY (tenant_id, company_id, line_id),
  UNIQUE (tenant_id, company_id, session_id, variant_id, bin_id),
  FOREIGN KEY (tenant_id, company_id, session_id)
    REFERENCES erp_inventory.stock_count_session(tenant_id, company_id, session_id) ON DELETE CASCADE,
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
  FOREIGN KEY (tenant_id, company_id, bin_id)
    REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id)
);

ALTER TABLE erp_inventory.stock_count_session ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_inventory.stock_count_session FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON erp_inventory.stock_count_session;
CREATE POLICY tenant_company_scope ON erp_inventory.stock_count_session
  USING (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  )
  WITH CHECK (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  );

ALTER TABLE erp_inventory.stock_count_line ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_inventory.stock_count_line FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON erp_inventory.stock_count_line;
CREATE POLICY tenant_company_scope ON erp_inventory.stock_count_line
  USING (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  )
  WITH CHECK (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  );

-- 5. Stock Returns (Customer & Supplier Returns)
CREATE TABLE IF NOT EXISTS erp_inventory.stock_return (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  return_id uuid NOT NULL DEFAULT gen_random_uuid(),
  return_number text NOT NULL,
  return_type text NOT NULL CHECK (return_type IN ('customer_return', 'supplier_return')),
  source_document_type text NOT NULL,
  source_document_id uuid,
  warehouse_id uuid NOT NULL,
  status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'approved', 'processed', 'cancelled')),
  created_by uuid NOT NULL,
  approved_by uuid,
  created_at timestamptz NOT NULL DEFAULT now(),
  processed_at timestamptz,
  note text,
  PRIMARY KEY (tenant_id, company_id, return_id),
  UNIQUE (tenant_id, company_id, return_number),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, company_id, warehouse_id)
    REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE TABLE IF NOT EXISTS erp_inventory.stock_return_line (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  line_id uuid NOT NULL DEFAULT gen_random_uuid(),
  return_id uuid NOT NULL,
  variant_id uuid NOT NULL,
  bin_id uuid NOT NULL,
  quantity numeric(24,9) NOT NULL CHECK (quantity > 0),
  unit_cost numeric(24,9) NOT NULL CHECK (unit_cost >= 0),
  disposition text NOT NULL CHECK (disposition IN ('available', 'quarantine', 'scrap', 'vendor_credit')),
  movement_id uuid,
  note text,
  PRIMARY KEY (tenant_id, company_id, line_id),
  FOREIGN KEY (tenant_id, company_id, return_id)
    REFERENCES erp_inventory.stock_return(tenant_id, company_id, return_id) ON DELETE CASCADE,
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
  FOREIGN KEY (tenant_id, company_id, bin_id)
    REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id)
);

ALTER TABLE erp_inventory.stock_return ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_inventory.stock_return FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON erp_inventory.stock_return;
CREATE POLICY tenant_company_scope ON erp_inventory.stock_return
  USING (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  )
  WITH CHECK (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  );

ALTER TABLE erp_inventory.stock_return_line ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_inventory.stock_return_line FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON erp_inventory.stock_return_line;
CREATE POLICY tenant_company_scope ON erp_inventory.stock_return_line
  USING (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  )
  WITH CHECK (
    tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    AND company_id = nullif(current_setting('app.company_id', true), '')::uuid
  );

-- 6. Table Grants for erp_app
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_inventory.stock_reservation TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_inventory.stock_pick_list TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_inventory.stock_pick_line TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_inventory.stock_count_session TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_inventory.stock_count_line TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_inventory.stock_return TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_inventory.stock_return_line TO erp_app;

-- 7. Record Migration
DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = 'public' AND table_name = 'schema_migrations' AND column_name = 'checksum'
  ) THEN
    INSERT INTO public.schema_migrations (version, checksum, applied_at)
    VALUES ('027_complete_inventory_management', 'manual_migration_027', now())
    ON CONFLICT (version) DO NOTHING;
  ELSE
    INSERT INTO public.schema_migrations (version, applied_at)
    VALUES ('027_complete_inventory_management', now())
    ON CONFLICT (version) DO NOTHING;
  END IF;
END $$;

COMMIT;
