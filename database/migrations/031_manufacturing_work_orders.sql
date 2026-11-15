-- 031_manufacturing_work_orders.sql
-- Universal ERP Phase 9: Manufacturing, BOM & Work Orders
-- Multi-tenant isolation with Row Level Security (RLS) on all tables.

CREATE SCHEMA IF NOT EXISTS manufacturing;

-- ── 1. Work Centers ───────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS manufacturing.work_centers (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    code text NOT NULL,
    name text NOT NULL,
    hourly_labor_rate numeric(20,4) NOT NULL DEFAULT 0 CHECK (hourly_labor_rate >= 0),
    hourly_overhead_rate numeric(20,4) NOT NULL DEFAULT 0 CHECK (hourly_overhead_rate >= 0),
    daily_capacity_hours numeric(10,2) NOT NULL DEFAULT 8.00 CHECK (daily_capacity_hours > 0),
    efficiency_percent numeric(5,2) NOT NULL DEFAULT 100.00 CHECK (efficiency_percent > 0 AND efficiency_percent <= 200),
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT work_centers_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT work_centers_code_key UNIQUE (tenant_id, company_id, code),
    CONSTRAINT work_centers_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id)
);

ALTER TABLE manufacturing.work_centers ENABLE ROW LEVEL SECURITY;
ALTER TABLE manufacturing.work_centers FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON manufacturing.work_centers;
CREATE POLICY tenant_company_scope ON manufacturing.work_centers
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 2. Production Routings & Operations ────────────────────────────────────────

CREATE TABLE IF NOT EXISTS manufacturing.routings (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    routing_number text NOT NULL,
    name text NOT NULL,
    product_variant_id uuid NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    description text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT routings_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT routings_number_key UNIQUE (tenant_id, company_id, routing_number),
    CONSTRAINT routings_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT routings_variant_fkey FOREIGN KEY (tenant_id, product_variant_id) REFERENCES erp_inventory.product_variant(tenant_id, variant_id)
);

ALTER TABLE manufacturing.routings ENABLE ROW LEVEL SECURITY;
ALTER TABLE manufacturing.routings FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON manufacturing.routings;
CREATE POLICY tenant_company_scope ON manufacturing.routings
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

CREATE TABLE IF NOT EXISTS manufacturing.routing_operations (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    routing_id uuid NOT NULL,
    operation_sequence integer NOT NULL CHECK (operation_sequence > 0),
    operation_name text NOT NULL,
    work_center_id uuid NOT NULL,
    setup_time_minutes numeric(10,2) NOT NULL DEFAULT 0 CHECK (setup_time_minutes >= 0),
    run_time_minutes_per_unit numeric(10,2) NOT NULL DEFAULT 0 CHECK (run_time_minutes_per_unit >= 0),
    description text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT routing_operations_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT routing_operations_seq_key UNIQUE (tenant_id, company_id, routing_id, operation_sequence),
    CONSTRAINT routing_operations_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT routing_operations_routing_fkey FOREIGN KEY (tenant_id, company_id, routing_id) REFERENCES manufacturing.routings(tenant_id, company_id, id) ON DELETE CASCADE,
    CONSTRAINT routing_operations_wc_fkey FOREIGN KEY (tenant_id, company_id, work_center_id) REFERENCES manufacturing.work_centers(tenant_id, company_id, id)
);

ALTER TABLE manufacturing.routing_operations ENABLE ROW LEVEL SECURITY;
ALTER TABLE manufacturing.routing_operations FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON manufacturing.routing_operations;
CREATE POLICY tenant_company_scope ON manufacturing.routing_operations
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 3. Bill of Materials (BOM) & Revisions ─────────────────────────────────────

CREATE TABLE IF NOT EXISTS manufacturing.boms (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    bom_number text NOT NULL,
    product_variant_id uuid NOT NULL,
    name text NOT NULL,
    base_quantity numeric(20,4) NOT NULL DEFAULT 1.0000 CHECK (base_quantity > 0),
    uom_id uuid,
    yield_percent numeric(5,2) NOT NULL DEFAULT 100.00 CHECK (yield_percent > 0 AND yield_percent <= 100),
    current_revision text NOT NULL DEFAULT 'A',
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'active', 'obsolete')),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT boms_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT boms_number_key UNIQUE (tenant_id, company_id, bom_number),
    CONSTRAINT boms_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT boms_variant_fkey FOREIGN KEY (tenant_id, product_variant_id) REFERENCES erp_inventory.product_variant(tenant_id, variant_id)
);

ALTER TABLE manufacturing.boms ENABLE ROW LEVEL SECURITY;
ALTER TABLE manufacturing.boms FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON manufacturing.boms;
CREATE POLICY tenant_company_scope ON manufacturing.boms
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

CREATE TABLE IF NOT EXISTS manufacturing.bom_revisions (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    bom_id uuid NOT NULL,
    revision_code text NOT NULL,
    effective_from date NOT NULL,
    effective_to date,
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'approved', 'active', 'superseded')),
    change_reason text,
    approved_by uuid REFERENCES identity.users(id),
    approved_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT bom_revisions_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT bom_revisions_code_key UNIQUE (tenant_id, company_id, bom_id, revision_code),
    CONSTRAINT bom_revisions_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT bom_revisions_bom_fkey FOREIGN KEY (tenant_id, company_id, bom_id) REFERENCES manufacturing.boms(tenant_id, company_id, id) ON DELETE CASCADE
);

ALTER TABLE manufacturing.bom_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE manufacturing.bom_revisions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON manufacturing.bom_revisions;
CREATE POLICY tenant_company_scope ON manufacturing.bom_revisions
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

CREATE TABLE IF NOT EXISTS manufacturing.bom_items (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    bom_revision_id uuid NOT NULL,
    line_number integer NOT NULL CHECK (line_number > 0),
    component_variant_id uuid NOT NULL,
    component_type text NOT NULL DEFAULT 'raw_material' CHECK (component_type IN ('raw_material', 'subassembly', 'phantom', 'packaging')),
    quantity_per numeric(20,4) NOT NULL CHECK (quantity_per > 0),
    scrap_percentage numeric(5,2) NOT NULL DEFAULT 0.00 CHECK (scrap_percentage >= 0 AND scrap_percentage < 100),
    notes text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT bom_items_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT bom_items_line_key UNIQUE (tenant_id, company_id, bom_revision_id, line_number),
    CONSTRAINT bom_items_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT bom_items_revision_fkey FOREIGN KEY (tenant_id, company_id, bom_revision_id) REFERENCES manufacturing.bom_revisions(tenant_id, company_id, id) ON DELETE CASCADE,
    CONSTRAINT bom_items_variant_fkey FOREIGN KEY (tenant_id, component_variant_id) REFERENCES erp_inventory.product_variant(tenant_id, variant_id)
);

ALTER TABLE manufacturing.bom_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE manufacturing.bom_items FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON manufacturing.bom_items;
CREATE POLICY tenant_company_scope ON manufacturing.bom_items
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 4. Approved Material Substitutions ─────────────────────────────────────────

CREATE TABLE IF NOT EXISTS manufacturing.material_substitutions (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    original_variant_id uuid NOT NULL,
    substitute_variant_id uuid NOT NULL,
    conversion_factor numeric(20,6) NOT NULL DEFAULT 1.000000 CHECK (conversion_factor > 0),
    priority integer NOT NULL DEFAULT 1 CHECK (priority > 0),
    is_active boolean NOT NULL DEFAULT true,
    notes text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT material_substitutions_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT material_substitutions_pair_key UNIQUE (tenant_id, company_id, original_variant_id, substitute_variant_id),
    CONSTRAINT material_substitutions_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT material_substitutions_orig_fkey FOREIGN KEY (tenant_id, original_variant_id) REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
    CONSTRAINT material_substitutions_sub_fkey FOREIGN KEY (tenant_id, substitute_variant_id) REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
    CONSTRAINT chk_diff_variants CHECK (original_variant_id <> substitute_variant_id)
);

ALTER TABLE manufacturing.material_substitutions ENABLE ROW LEVEL SECURITY;
ALTER TABLE manufacturing.material_substitutions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON manufacturing.material_substitutions;
CREATE POLICY tenant_company_scope ON manufacturing.material_substitutions
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 5. Work Orders ────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS manufacturing.work_orders (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    work_order_number text NOT NULL,
    product_variant_id uuid NOT NULL,
    bom_id uuid,
    bom_revision_id uuid,
    routing_id uuid,
    target_warehouse_id uuid NOT NULL,
    target_bin_id uuid NOT NULL,
    planned_quantity numeric(20,4) NOT NULL CHECK (planned_quantity > 0),
    completed_quantity numeric(20,4) NOT NULL DEFAULT 0 CHECK (completed_quantity >= 0),
    scrapped_quantity numeric(20,4) NOT NULL DEFAULT 0 CHECK (scrapped_quantity >= 0),
    rejected_quantity numeric(20,4) NOT NULL DEFAULT 0 CHECK (rejected_quantity >= 0),
    start_date date NOT NULL,
    due_date date NOT NULL,
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'planned', 'approved', 'released', 'in_progress', 'partially_completed', 'completed', 'cancelled')),
    wip_gl_account_id uuid,
    total_material_cost numeric(20,4) NOT NULL DEFAULT 0 CHECK (total_material_cost >= 0),
    total_labor_cost numeric(20,4) NOT NULL DEFAULT 0 CHECK (total_labor_cost >= 0),
    total_overhead_cost numeric(20,4) NOT NULL DEFAULT 0 CHECK (total_overhead_cost >= 0),
    total_production_cost numeric(20,4) NOT NULL DEFAULT 0 CHECK (total_production_cost >= 0),
    unit_cost_final numeric(20,4) NOT NULL DEFAULT 0 CHECK (unit_cost_final >= 0),
    origin_demand_type text,
    origin_demand_id uuid,
    notes text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    released_at timestamptz,
    completed_at timestamptz,
    cancelled_at timestamptz,
    CONSTRAINT work_orders_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT work_orders_number_key UNIQUE (tenant_id, company_id, work_order_number),
    CONSTRAINT work_orders_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT work_orders_variant_fkey FOREIGN KEY (tenant_id, product_variant_id) REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
    CONSTRAINT work_orders_warehouse_fkey FOREIGN KEY (tenant_id, company_id, target_warehouse_id) REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id),
    CONSTRAINT work_orders_bin_fkey FOREIGN KEY (tenant_id, company_id, target_bin_id) REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id),
    CONSTRAINT work_orders_wip_gl_fkey FOREIGN KEY (tenant_id, company_id, wip_gl_account_id) REFERENCES finance.accounts(tenant_id, company_id, id)
);

ALTER TABLE manufacturing.work_orders ENABLE ROW LEVEL SECURITY;
ALTER TABLE manufacturing.work_orders FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON manufacturing.work_orders;
CREATE POLICY tenant_company_scope ON manufacturing.work_orders
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 6. Work Order Materials (Component Requirements & FIFO Issues) ────────────

CREATE TABLE IF NOT EXISTS manufacturing.work_order_materials (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    work_order_id uuid NOT NULL,
    line_number integer NOT NULL CHECK (line_number > 0),
    component_variant_id uuid NOT NULL,
    required_quantity numeric(20,4) NOT NULL CHECK (required_quantity > 0),
    reserved_quantity numeric(20,4) NOT NULL DEFAULT 0 CHECK (reserved_quantity >= 0),
    issued_quantity numeric(20,4) NOT NULL DEFAULT 0 CHECK (issued_quantity >= 0),
    returned_quantity numeric(20,4) NOT NULL DEFAULT 0 CHECK (returned_quantity >= 0),
    reservation_id uuid,
    consumed_cost numeric(20,4) NOT NULL DEFAULT 0 CHECK (consumed_cost >= 0),
    status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'reserved', 'partially_issued', 'issued', 'closed')),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT work_order_materials_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT work_order_materials_line_key UNIQUE (tenant_id, company_id, work_order_id, line_number),
    CONSTRAINT work_order_materials_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT work_order_materials_wo_fkey FOREIGN KEY (tenant_id, company_id, work_order_id) REFERENCES manufacturing.work_orders(tenant_id, company_id, id) ON DELETE CASCADE,
    CONSTRAINT work_order_materials_variant_fkey FOREIGN KEY (tenant_id, component_variant_id) REFERENCES erp_inventory.product_variant(tenant_id, variant_id)
);

ALTER TABLE manufacturing.work_order_materials ENABLE ROW LEVEL SECURITY;
ALTER TABLE manufacturing.work_order_materials FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON manufacturing.work_order_materials;
CREATE POLICY tenant_company_scope ON manufacturing.work_order_materials
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 7. Work Order Operations & Execution ──────────────────────────────────────

CREATE TABLE IF NOT EXISTS manufacturing.work_order_operations (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    work_order_id uuid NOT NULL,
    operation_sequence integer NOT NULL CHECK (operation_sequence > 0),
    operation_name text NOT NULL,
    work_center_id uuid NOT NULL,
    setup_time_minutes numeric(10,2) NOT NULL DEFAULT 0 CHECK (setup_time_minutes >= 0),
    run_time_minutes_per_unit numeric(10,2) NOT NULL DEFAULT 0 CHECK (run_time_minutes_per_unit >= 0),
    actual_setup_minutes numeric(10,2) NOT NULL DEFAULT 0 CHECK (actual_setup_minutes >= 0),
    actual_run_minutes numeric(10,2) NOT NULL DEFAULT 0 CHECK (actual_run_minutes >= 0),
    completed_quantity numeric(20,4) NOT NULL DEFAULT 0 CHECK (completed_quantity >= 0),
    scrapped_quantity numeric(20,4) NOT NULL DEFAULT 0 CHECK (scrapped_quantity >= 0),
    allocated_labor_cost numeric(20,4) NOT NULL DEFAULT 0 CHECK (allocated_labor_cost >= 0),
    allocated_overhead_cost numeric(20,4) NOT NULL DEFAULT 0 CHECK (allocated_overhead_cost >= 0),
    status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'in_progress', 'completed')),
    completed_at timestamptz,
    notes text,
    CONSTRAINT work_order_operations_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT work_order_operations_seq_key UNIQUE (tenant_id, company_id, work_order_id, operation_sequence),
    CONSTRAINT work_order_operations_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT work_order_operations_wo_fkey FOREIGN KEY (tenant_id, company_id, work_order_id) REFERENCES manufacturing.work_orders(tenant_id, company_id, id) ON DELETE CASCADE,
    CONSTRAINT work_order_operations_wc_fkey FOREIGN KEY (tenant_id, company_id, work_center_id) REFERENCES manufacturing.work_centers(tenant_id, company_id, id)
);

ALTER TABLE manufacturing.work_order_operations ENABLE ROW LEVEL SECURITY;
ALTER TABLE manufacturing.work_order_operations FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON manufacturing.work_order_operations;
CREATE POLICY tenant_company_scope ON manufacturing.work_order_operations
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 8. Production Inspections & Quality Control ───────────────────────────────

CREATE TABLE IF NOT EXISTS manufacturing.production_inspections (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    work_order_id uuid NOT NULL,
    inspection_number text NOT NULL,
    inspection_type text NOT NULL CHECK (inspection_type IN ('in_process', 'finished_goods')),
    inspected_quantity numeric(20,4) NOT NULL CHECK (inspected_quantity > 0),
    accepted_quantity numeric(20,4) NOT NULL DEFAULT 0 CHECK (accepted_quantity >= 0),
    rejected_quantity numeric(20,4) NOT NULL DEFAULT 0 CHECK (rejected_quantity >= 0),
    quarantine_bin_id uuid,
    disposition text NOT NULL CHECK (disposition IN ('accepted', 'quarantine', 'scrap', 'rework')),
    inspector_user_id uuid NOT NULL,
    inspection_date timestamptz NOT NULL DEFAULT now(),
    notes text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT production_inspections_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT production_inspections_number_key UNIQUE (tenant_id, company_id, inspection_number),
    CONSTRAINT production_inspections_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT production_inspections_wo_fkey FOREIGN KEY (tenant_id, company_id, work_order_id) REFERENCES manufacturing.work_orders(tenant_id, company_id, id),
    CONSTRAINT production_inspections_quarantine_bin_fkey FOREIGN KEY (tenant_id, company_id, quarantine_bin_id) REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id)
);

ALTER TABLE manufacturing.production_inspections ENABLE ROW LEVEL SECURITY;
ALTER TABLE manufacturing.production_inspections FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON manufacturing.production_inspections;
CREATE POLICY tenant_company_scope ON manufacturing.production_inspections
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 9. Register Manufacturing Permissions in authorization.permissions ────────

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
    ('mfg.bom.view', 'View Bills of Materials and revisions', 'manufacturing'),
    ('mfg.bom.manage', 'Create and manage BOMs and revisions', 'manufacturing'),
    ('mfg.work_center.manage', 'Create and manage Work Centers and capacities', 'manufacturing'),
    ('mfg.routing.manage', 'Create and manage production routings and operations', 'manufacturing'),
    ('mfg.work_order.view', 'View work orders and production schedule', 'manufacturing'),
    ('mfg.work_order.create', 'Create draft work orders and explode BOMs', 'manufacturing'),
    ('mfg.work_order.approve', 'Approve and release work orders for production', 'manufacturing'),
    ('mfg.material.reserve', 'Reserve raw materials in warehouse bins', 'manufacturing'),
    ('mfg.material.issue', 'Issue raw materials to WIP using FIFO valuation', 'manufacturing'),
    ('mfg.material.return', 'Return unconsumed materials to warehouse stock', 'manufacturing'),
    ('mfg.operation.report', 'Report operation progress, labor, and machine run-time', 'manufacturing'),
    ('mfg.qc.inspect', 'Perform quality control inspections and quarantine dispositions', 'manufacturing'),
    ('mfg.work_order.complete', 'Complete work order and capitalize finished goods', 'manufacturing'),
    ('mfg.reports.view', 'View manufacturing genealogy, WIP, and variance reports', 'manufacturing')
ON CONFLICT (permission_code) DO NOTHING;

-- ── 10. Seed Standard Manufacturing GL Accounts For All Existing Companies ───

DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN SELECT tenant_id, id AS company_id FROM organization.companies LOOP
        -- 1210: Finished Goods Inventory
        INSERT INTO finance.accounts (tenant_id, company_id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active)
        VALUES (r.tenant_id, r.company_id, '1210', 'Finished Goods Inventory', 'asset', 'debit', 'PKR', true, false, true)
        ON CONFLICT (tenant_id, company_id, account_code) DO NOTHING;

        -- 1250: Work-in-Process (WIP) Inventory
        INSERT INTO finance.accounts (tenant_id, company_id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active)
        VALUES (r.tenant_id, r.company_id, '1250', 'Work-in-Process (WIP) Inventory', 'asset', 'debit', 'PKR', true, false, true)
        ON CONFLICT (tenant_id, company_id, account_code) DO NOTHING;

        -- 2050: Direct Labor Clearing
        INSERT INTO finance.accounts (tenant_id, company_id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active)
        VALUES (r.tenant_id, r.company_id, '2050', 'Direct Labor Clearing', 'liability', 'credit', 'PKR', false, true, true)
        ON CONFLICT (tenant_id, company_id, account_code) DO NOTHING;

        -- 6060: Applied Manufacturing Overhead
        INSERT INTO finance.accounts (tenant_id, company_id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active)
        VALUES (r.tenant_id, r.company_id, '6060', 'Applied Manufacturing Overhead', 'expense', 'credit', 'PKR', false, true, true)
        ON CONFLICT (tenant_id, company_id, account_code) DO NOTHING;

        -- 5020: Scrap & Production Loss
        INSERT INTO finance.accounts (tenant_id, company_id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active)
        VALUES (r.tenant_id, r.company_id, '5020', 'Scrap & Production Loss', 'expense', 'debit', 'PKR', false, true, true)
        ON CONFLICT (tenant_id, company_id, account_code) DO NOTHING;

        -- 5030: Manufacturing Cost Variance
        INSERT INTO finance.accounts (tenant_id, company_id, account_code, account_name, account_type, normal_balance, currency_code, is_control_account, allow_manual_posting, is_active)
        VALUES (r.tenant_id, r.company_id, '5030', 'Manufacturing Cost Variance', 'expense', 'debit', 'PKR', false, true, true)
        ON CONFLICT (tenant_id, company_id, account_code) DO NOTHING;
    END LOOP;
END $$;

-- Grant table privileges to application runtime user
GRANT USAGE ON SCHEMA manufacturing TO erp_dev;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA manufacturing TO erp_dev;

-- ── 11. Migration Registry ───────────────────────────────────────────────────

INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('031_manufacturing_work_orders', now())
ON CONFLICT (version) DO NOTHING;
