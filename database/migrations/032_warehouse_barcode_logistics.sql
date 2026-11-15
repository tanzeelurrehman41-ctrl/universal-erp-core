-- ==============================================================================
-- Universal ERP — Database Migration 032: Warehouse Management & Barcode Logistics
-- Phase 10: Warehouse Master Data, Barcode Scanning, Directed Putaway,
--           Wave Picking, Packing/Cartonization, Replenishment, and Quarantine
-- ==============================================================================

CREATE SCHEMA IF NOT EXISTS warehouse;

-- ── 1. Warehouse Zones Master Table ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouse.zones (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    zone_code text NOT NULL,
    name text NOT NULL,
    zone_type text NOT NULL CHECK (zone_type IN ('receiving', 'picking', 'bulk_reserve', 'quarantine', 'dispatch', 'staging', 'packing')),
    storage_type text NOT NULL DEFAULT 'ambient' CHECK (storage_type IN ('ambient', 'cold_chain', 'hazardous', 'high_value', 'flammable')),
    default_putaway_strategy text NOT NULL DEFAULT 'nearest_empty' CHECK (default_putaway_strategy IN ('nearest_empty', 'fixed_location', 'zone_preferred', 'capacity_optimized')),
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT zones_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT zones_warehouse_fkey FOREIGN KEY (tenant_id, company_id, warehouse_id) REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id) ON DELETE CASCADE,
    CONSTRAINT zones_unique_code UNIQUE (tenant_id, company_id, warehouse_id, zone_code)
);

-- ── 2. Extend erp_inventory.stock_bin with Warehouse Topology ────────────────
ALTER TABLE erp_inventory.stock_bin ADD COLUMN IF NOT EXISTS zone_id uuid REFERENCES warehouse.zones(id) ON DELETE SET NULL;
ALTER TABLE erp_inventory.stock_bin ADD COLUMN IF NOT EXISTS aisle text;
ALTER TABLE erp_inventory.stock_bin ADD COLUMN IF NOT EXISTS rack text;
ALTER TABLE erp_inventory.stock_bin ADD COLUMN IF NOT EXISTS shelf text;
ALTER TABLE erp_inventory.stock_bin ADD COLUMN IF NOT EXISTS bin_position text;
ALTER TABLE erp_inventory.stock_bin ADD COLUMN IF NOT EXISTS max_weight_kg numeric(12,3);
ALTER TABLE erp_inventory.stock_bin ADD COLUMN IF NOT EXISTS max_volume_cbm numeric(12,3);
ALTER TABLE erp_inventory.stock_bin ADD COLUMN IF NOT EXISTS max_units numeric(12,3);
ALTER TABLE erp_inventory.stock_bin ADD COLUMN IF NOT EXISTS is_pick_face boolean NOT NULL DEFAULT false;
ALTER TABLE erp_inventory.stock_bin ADD COLUMN IF NOT EXISTS storage_restriction text NOT NULL DEFAULT 'standard';

-- ── 3. Putaway Strategies Master Table ───────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouse.putaway_strategies (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    product_variant_id uuid,
    product_category_id uuid,
    preferred_zone_id uuid REFERENCES warehouse.zones(id) ON DELETE CASCADE,
    preferred_bin_id uuid,
    strategy_type text NOT NULL CHECK (strategy_type IN ('fixed_bin', 'zone_preferred', 'velocity_based', 'capacity_optimized')),
    priority integer NOT NULL DEFAULT 1,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT putaway_strat_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT putaway_strat_warehouse_fkey FOREIGN KEY (tenant_id, company_id, warehouse_id) REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id) ON DELETE CASCADE
);

-- ── 4. Replenishment Rules Master Table ──────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouse.replenishment_rules (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    product_variant_id uuid NOT NULL,
    pick_bin_id uuid NOT NULL,
    reserve_zone_id uuid REFERENCES warehouse.zones(id) ON DELETE CASCADE,
    min_quantity numeric(12,3) NOT NULL,
    max_quantity numeric(12,3) NOT NULL,
    reorder_point numeric(12,3) NOT NULL,
    replenish_increment_qty numeric(12,3) NOT NULL DEFAULT 1,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT replen_rule_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT replen_rule_warehouse_fkey FOREIGN KEY (tenant_id, company_id, warehouse_id) REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id) ON DELETE CASCADE,
    CONSTRAINT replen_rule_bin_fkey FOREIGN KEY (tenant_id, company_id, pick_bin_id) REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id) ON DELETE CASCADE,
    CONSTRAINT replen_rule_unique UNIQUE (tenant_id, company_id, warehouse_id, product_variant_id, pick_bin_id)
);

-- ── 5. Barcode Definitions Master Table ──────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouse.barcode_definitions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    barcode_value text NOT NULL,
    barcode_format text NOT NULL DEFAULT 'code128' CHECK (barcode_format IN ('code128', 'qr_code', 'gs1_128')),
    entity_type text NOT NULL CHECK (entity_type IN ('product_variant', 'stock_bin', 'lot', 'serial', 'work_order', 'pick_wave', 'shipping_carton')),
    entity_id uuid NOT NULL,
    gs1_gtin text,
    gs1_lot text,
    gs1_serial text,
    gs1_expiry_date text,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT barcode_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT barcode_unique_value UNIQUE (tenant_id, barcode_value)
);

-- ── 6. Scan Audit Logs ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouse.scan_audit_logs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    scanned_barcode text NOT NULL,
    barcode_format text NOT NULL,
    parsed_entity_type text,
    parsed_entity_id uuid,
    operation_context text NOT NULL CHECK (operation_context IN ('putaway', 'pick', 'pack', 'transfer', 'replenishment', 'quarantine', 'lookup')),
    device_id text,
    scanner_user_id text,
    scanned_at timestamptz NOT NULL DEFAULT now(),
    status text NOT NULL CHECK (status IN ('valid', 'invalid', 'duplicate')),
    idempotency_key text,
    error_details text,
    CONSTRAINT scan_audit_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

-- ── 7. Putaway Tasks ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouse.putaway_tasks (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    task_number text NOT NULL,
    warehouse_id uuid NOT NULL,
    source_type text NOT NULL CHECK (source_type IN ('grn', 'work_order_fg', 'customer_return', 'manual')),
    source_document_id uuid,
    product_variant_id uuid NOT NULL,
    source_bin_id uuid NOT NULL,
    suggested_bin_id uuid NOT NULL,
    confirmed_bin_id uuid,
    quantity numeric(12,3) NOT NULL CHECK (quantity > 0),
    putaway_status text NOT NULL DEFAULT 'pending' CHECK (putaway_status IN ('pending', 'in_progress', 'completed', 'cancelled')),
    assigned_to text,
    scanned_bin_barcode text,
    completed_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT putaway_task_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT putaway_task_warehouse_fkey FOREIGN KEY (tenant_id, company_id, warehouse_id) REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id) ON DELETE CASCADE,
    CONSTRAINT putaway_task_source_bin_fkey FOREIGN KEY (tenant_id, company_id, source_bin_id) REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id) ON DELETE CASCADE,
    CONSTRAINT putaway_task_suggested_bin_fkey FOREIGN KEY (tenant_id, company_id, suggested_bin_id) REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id) ON DELETE CASCADE,
    CONSTRAINT putaway_task_unique_num UNIQUE (tenant_id, company_id, task_number)
);

-- ── 8. Picking Waves ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouse.picking_waves (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    wave_number text NOT NULL,
    warehouse_id uuid NOT NULL,
    wave_type text NOT NULL DEFAULT 'sales_order' CHECK (wave_type IN ('sales_order', 'work_order_issue', 'mixed')),
    status text NOT NULL DEFAULT 'created' CHECK (status IN ('created', 'released', 'in_progress', 'picked', 'cancelled')),
    total_lines integer NOT NULL DEFAULT 0,
    completed_lines integer NOT NULL DEFAULT 0,
    priority integer NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz,
    CONSTRAINT picking_wave_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT picking_wave_warehouse_fkey FOREIGN KEY (tenant_id, company_id, warehouse_id) REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id) ON DELETE CASCADE,
    CONSTRAINT picking_wave_unique_num UNIQUE (tenant_id, company_id, wave_number)
);

-- ── 9. Wave Pick Items ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouse.wave_pick_items (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    wave_id uuid NOT NULL REFERENCES warehouse.picking_waves(id) ON DELETE CASCADE,
    source_document_type text NOT NULL CHECK (source_document_type IN ('sales_order', 'work_order')),
    source_document_id uuid NOT NULL,
    source_line_id uuid NOT NULL,
    product_variant_id uuid NOT NULL,
    source_bin_id uuid NOT NULL,
    target_pack_station_id uuid,
    requested_quantity numeric(12,3) NOT NULL CHECK (requested_quantity > 0),
    picked_quantity numeric(12,3) NOT NULL DEFAULT 0 CHECK (picked_quantity >= 0),
    status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'picked', 'shortage')),
    picker_id text,
    scanned_bin_barcode text,
    scanned_product_barcode text,
    picked_at timestamptz,
    CONSTRAINT wave_item_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT wave_item_bin_fkey FOREIGN KEY (tenant_id, company_id, source_bin_id) REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id) ON DELETE CASCADE
);

-- ── 10. Shipment Packages (Cartonization & Packing) ──────────────────────────
CREATE TABLE IF NOT EXISTS warehouse.shipment_packages (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    package_number text NOT NULL,
    warehouse_id uuid NOT NULL,
    sales_order_id uuid,
    sales_dispatch_id uuid,
    package_type text NOT NULL DEFAULT 'carton' CHECK (package_type IN ('carton', 'box', 'pallet', 'crate')),
    tracking_number text,
    carrier_name text,
    gross_weight_kg numeric(12,3),
    net_weight_kg numeric(12,3),
    status text NOT NULL DEFAULT 'packing' CHECK (status IN ('packing', 'sealed', 'staged', 'shipped')),
    staged_bin_id uuid,
    sealed_at timestamptz,
    shipped_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT package_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT package_warehouse_fkey FOREIGN KEY (tenant_id, company_id, warehouse_id) REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id) ON DELETE CASCADE,
    CONSTRAINT package_unique_num UNIQUE (tenant_id, company_id, package_number)
);

-- ── 11. Shipment Package Items ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouse.shipment_package_items (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    package_id uuid NOT NULL REFERENCES warehouse.shipment_packages(id) ON DELETE CASCADE,
    product_variant_id uuid NOT NULL,
    quantity numeric(12,3) NOT NULL CHECK (quantity > 0),
    lot_number text,
    serial_number text,
    CONSTRAINT pkg_item_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

-- ── 12. Replenishment Tasks ──────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouse.replenishment_tasks (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    task_number text NOT NULL,
    warehouse_id uuid NOT NULL,
    rule_id uuid REFERENCES warehouse.replenishment_rules(id) ON DELETE SET NULL,
    product_variant_id uuid NOT NULL,
    from_bin_id uuid NOT NULL,
    to_bin_id uuid NOT NULL,
    requested_quantity numeric(12,3) NOT NULL CHECK (requested_quantity > 0),
    transferred_quantity numeric(12,3) NOT NULL DEFAULT 0 CHECK (transferred_quantity >= 0),
    status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'in_progress', 'completed', 'cancelled')),
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz,
    CONSTRAINT replen_task_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT replen_task_warehouse_fkey FOREIGN KEY (tenant_id, company_id, warehouse_id) REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id) ON DELETE CASCADE,
    CONSTRAINT replen_task_from_bin_fkey FOREIGN KEY (tenant_id, company_id, from_bin_id) REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id) ON DELETE CASCADE,
    CONSTRAINT replen_task_to_bin_fkey FOREIGN KEY (tenant_id, company_id, to_bin_id) REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id) ON DELETE CASCADE,
    CONSTRAINT replen_task_unique_num UNIQUE (tenant_id, company_id, task_number)
);

-- ── 13. Quarantine Records ───────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS warehouse.quarantine_records (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    record_number text NOT NULL,
    warehouse_id uuid NOT NULL,
    product_variant_id uuid NOT NULL,
    source_type text NOT NULL CHECK (source_type IN ('purchasing_inspection', 'mfg_qc', 'customer_return', 'damaged_found')),
    source_reference_id uuid,
    quarantine_bin_id uuid NOT NULL,
    quantity numeric(12,3) NOT NULL CHECK (quantity > 0),
    reason text NOT NULL,
    disposition text NOT NULL DEFAULT 'pending' CHECK (disposition IN ('pending', 'released_to_stock', 'reworked', 'scrapped', 'returned_to_vendor')),
    target_disposition_bin_id uuid,
    inspected_by text,
    disposed_at timestamptz,
    disposition_notes text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT quarantine_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT quarantine_warehouse_fkey FOREIGN KEY (tenant_id, company_id, warehouse_id) REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id) ON DELETE CASCADE,
    CONSTRAINT quarantine_bin_fkey FOREIGN KEY (tenant_id, company_id, quarantine_bin_id) REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id) ON DELETE CASCADE,
    CONSTRAINT quarantine_unique_num UNIQUE (tenant_id, company_id, record_number)
);

-- ── 14. Row-Level Security (RLS) Policies ────────────────────────────────────
ALTER TABLE warehouse.zones ENABLE ROW LEVEL SECURITY;
ALTER TABLE warehouse.zones FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON warehouse.zones;
CREATE POLICY tenant_company_scope ON warehouse.zones
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE warehouse.putaway_strategies ENABLE ROW LEVEL SECURITY;
ALTER TABLE warehouse.putaway_strategies FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON warehouse.putaway_strategies;
CREATE POLICY tenant_company_scope ON warehouse.putaway_strategies
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE warehouse.replenishment_rules ENABLE ROW LEVEL SECURITY;
ALTER TABLE warehouse.replenishment_rules FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON warehouse.replenishment_rules;
CREATE POLICY tenant_company_scope ON warehouse.replenishment_rules
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE warehouse.barcode_definitions ENABLE ROW LEVEL SECURITY;
ALTER TABLE warehouse.barcode_definitions FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON warehouse.barcode_definitions;
CREATE POLICY tenant_company_scope ON warehouse.barcode_definitions
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE warehouse.scan_audit_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE warehouse.scan_audit_logs FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON warehouse.scan_audit_logs;
CREATE POLICY tenant_company_scope ON warehouse.scan_audit_logs
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE warehouse.putaway_tasks ENABLE ROW LEVEL SECURITY;
ALTER TABLE warehouse.putaway_tasks FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON warehouse.putaway_tasks;
CREATE POLICY tenant_company_scope ON warehouse.putaway_tasks
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE warehouse.picking_waves ENABLE ROW LEVEL SECURITY;
ALTER TABLE warehouse.picking_waves FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON warehouse.picking_waves;
CREATE POLICY tenant_company_scope ON warehouse.picking_waves
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE warehouse.wave_pick_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE warehouse.wave_pick_items FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON warehouse.wave_pick_items;
CREATE POLICY tenant_company_scope ON warehouse.wave_pick_items
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE warehouse.shipment_packages ENABLE ROW LEVEL SECURITY;
ALTER TABLE warehouse.shipment_packages FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON warehouse.shipment_packages;
CREATE POLICY tenant_company_scope ON warehouse.shipment_packages
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE warehouse.shipment_package_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE warehouse.shipment_package_items FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON warehouse.shipment_package_items;
CREATE POLICY tenant_company_scope ON warehouse.shipment_package_items
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE warehouse.replenishment_tasks ENABLE ROW LEVEL SECURITY;
ALTER TABLE warehouse.replenishment_tasks FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON warehouse.replenishment_tasks;
CREATE POLICY tenant_company_scope ON warehouse.replenishment_tasks
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE warehouse.quarantine_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE warehouse.quarantine_records FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON warehouse.quarantine_records;
CREATE POLICY tenant_company_scope ON warehouse.quarantine_records
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 15. Register Warehouse Permissions ───────────────────────────────────────
INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
    ('warehouse.zone.manage', 'Manage warehouse zones and storage types', 'warehouse'),
    ('warehouse.bin.manage', 'Configure bin profiles, capacities and restrictions', 'warehouse'),
    ('warehouse.barcode.view', 'View barcode definitions and labels', 'warehouse'),
    ('warehouse.barcode.generate', 'Generate and print product/bin/package barcodes', 'warehouse'),
    ('warehouse.scan.execute', 'Execute barcode scanning operations and validations', 'warehouse'),
    ('warehouse.putaway.execute', 'Execute receiving and directed putaway tasks', 'warehouse'),
    ('warehouse.wave.manage', 'Create and manage picking waves', 'warehouse'),
    ('warehouse.pick.execute', 'Execute wave picking and pick confirmations', 'warehouse'),
    ('warehouse.pack.manage', 'Manage packing stations, cartonization, and shipment packages', 'warehouse'),
    ('warehouse.dispatch.manage', 'Confirm shipping and dispatch staging', 'warehouse'),
    ('warehouse.replenishment.manage', 'Configure replenishment rules and min/max triggers', 'warehouse'),
    ('warehouse.replenishment.execute', 'Execute replenishment movements from reserve to pick-face', 'warehouse'),
    ('warehouse.transfer.execute', 'Execute bin-to-bin stock transfers', 'warehouse'),
    ('warehouse.quarantine.manage', 'Manage quarantine routing and disposition', 'warehouse'),
    ('warehouse.reports.view', 'View warehouse utilization, wave metrics and scan logs', 'warehouse')
ON CONFLICT (permission_code) DO NOTHING;

-- ── 16. Grant Permissions to erp_app Role ───────────────────────────────────
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_app') THEN
        GRANT USAGE ON SCHEMA warehouse TO erp_app;
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA warehouse TO erp_app;
    END IF;
END $$;

-- ── 17. Register in Schema Migrations ────────────────────────────────────────
INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('032_warehouse_barcode_logistics', now())
ON CONFLICT (version) DO NOTHING;
