-- ============================================================================
-- Migration 036: Multi-Branch Operations, Intercompany Consolidation & External Integrations Hub
-- Universal ERP Platform — Phase 13
--
-- Features:
-- 1. Multi-Branch Operations:
--    - Branch master with hierarchy, branch types, addresses, and manager links.
--    - Branch references on warehouses, sales orders/invoices, purchase orders, and employees.
-- 2. Intercompany Trading & Clearing:
--    - Intercompany relationships with Due-To / Due-From clearing accounts and transfer pricing policies.
--    - Intercompany stock transfer pipeline (draft -> dispatched -> in_transit -> received -> reconciled).
-- 3. Financial Consolidation Engine:
--    - Multi-entity consolidation runs with automated elimination calculations.
--    - Intercompany balances, revenue/cogs, and unrealized inventory profit eliminations.
-- 4. External Integrations Hub:
--    - HMAC-SHA256 authenticated webhook subscriptions with secret hashing & rotation.
--    - Outbound event delivery log with bounded exponential backoff & dead-letter state.
--    - API Partner registry with credential hashing and fine-grained permissions.
-- 5. Row-Level Security (RLS) forced across all tables.
-- 6. Role permissions registered in "authorization".permissions and granted to erp_app.
-- ============================================================================

-- ── 1. Create Schemas ─────────────────────────────────────────────────────────

CREATE SCHEMA IF NOT EXISTS consolidation;
CREATE SCHEMA IF NOT EXISTS integrations;

-- ── 2. Branch Hierarchy & Operating Locations ────────────────────────────────

CREATE TABLE IF NOT EXISTS organization.branches (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    company_id UUID NOT NULL,
    branch_code TEXT NOT NULL,
    branch_name TEXT NOT NULL,
    branch_type TEXT NOT NULL CHECK (branch_type IN ('headquarters', 'plant', 'warehouse_hub', 'retail_store', 'sales_office')),
    parent_branch_id UUID REFERENCES organization.branches(id) ON DELETE SET NULL,
    tax_registration_number TEXT,
    address_line TEXT,
    city TEXT NOT NULL,
    province_or_state TEXT NOT NULL,
    country_code CHAR(2) NOT NULL DEFAULT 'PK',
    manager_id UUID REFERENCES hr.employees(id) ON DELETE SET NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT branches_unique_code UNIQUE (tenant_id, company_id, branch_code),
    CONSTRAINT branches_tenant_company_fkey FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_branches_company ON organization.branches (tenant_id, company_id);
CREATE INDEX IF NOT EXISTS idx_branches_parent ON organization.branches (tenant_id, parent_branch_id);

-- ── 3. Branch Scoping on Inventory, Sales, Purchasing & HR ──────────────────

ALTER TABLE erp_inventory.warehouse
    ADD COLUMN IF NOT EXISTS branch_id UUID REFERENCES organization.branches(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_warehouse_branch ON erp_inventory.warehouse (tenant_id, company_id, branch_id);

ALTER TABLE erp_sales.sales_order
    ADD COLUMN IF NOT EXISTS branch_id UUID REFERENCES organization.branches(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_sales_order_branch ON erp_sales.sales_order (tenant_id, company_id, branch_id);

ALTER TABLE erp_sales.sales_invoice
    ADD COLUMN IF NOT EXISTS branch_id UUID REFERENCES organization.branches(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_sales_invoice_branch ON erp_sales.sales_invoice (tenant_id, company_id, branch_id);

ALTER TABLE erp_purchasing.purchase_order
    ADD COLUMN IF NOT EXISTS branch_id UUID REFERENCES organization.branches(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_purchase_order_branch ON erp_purchasing.purchase_order (tenant_id, company_id, branch_id);

ALTER TABLE hr.employees
    ADD COLUMN IF NOT EXISTS branch_id UUID REFERENCES organization.branches(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_employees_branch ON hr.employees (tenant_id, company_id, branch_id);

-- ── 4. Intercompany Relationship & Clearing Accounts ─────────────────────────

CREATE TABLE IF NOT EXISTS consolidation.intercompany_relationships (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    from_company_id UUID NOT NULL,
    to_company_id UUID NOT NULL,
    due_to_account_id UUID NOT NULL,
    due_from_account_id UUID NOT NULL,
    clearing_currency_code CHAR(3) NOT NULL REFERENCES finance.currencies(code),
    transfer_pricing_rule TEXT NOT NULL DEFAULT 'at_cost' CHECK (transfer_pricing_rule IN ('at_cost', 'cost_plus', 'market_price')),
    markup_percentage NUMERIC(6,3) NOT NULL DEFAULT 0.000,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ic_rel_unique UNIQUE (tenant_id, from_company_id, to_company_id),
    CONSTRAINT ic_rel_no_self CHECK (from_company_id <> to_company_id),
    CONSTRAINT ic_rel_from_comp_fkey FOREIGN KEY (tenant_id, from_company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT ic_rel_to_comp_fkey FOREIGN KEY (tenant_id, to_company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT ic_rel_due_from_fkey FOREIGN KEY (tenant_id, from_company_id, due_from_account_id)
        REFERENCES finance.accounts(tenant_id, company_id, id),
    CONSTRAINT ic_rel_due_to_fkey FOREIGN KEY (tenant_id, to_company_id, due_to_account_id)
        REFERENCES finance.accounts(tenant_id, company_id, id)
);

CREATE INDEX IF NOT EXISTS idx_ic_rel_tenant ON consolidation.intercompany_relationships (tenant_id, from_company_id, to_company_id);

-- ── 5. Intercompany Transfers & Lines ───────────────────────────────────────

CREATE TABLE IF NOT EXISTS consolidation.intercompany_transfers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    transfer_number TEXT NOT NULL,
    from_company_id UUID NOT NULL,
    from_branch_id UUID REFERENCES organization.branches(id) ON DELETE SET NULL,
    to_company_id UUID NOT NULL,
    to_branch_id UUID REFERENCES organization.branches(id) ON DELETE SET NULL,
    status TEXT NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'dispatched', 'in_transit', 'received', 'reconciled', 'cancelled')),
    dispatch_date DATE NOT NULL,
    receipt_date DATE,
    currency_code CHAR(3) NOT NULL REFERENCES finance.currencies(code),
    total_amount_pkr NUMERIC(14,4) NOT NULL DEFAULT 0,
    source_sales_invoice_id UUID,
    target_purchase_invoice_id UUID,
    from_gl_journal_id UUID,
    to_gl_journal_id UUID,
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ic_transfers_unique_num UNIQUE (tenant_id, transfer_number),
    CONSTRAINT ic_transfers_no_self CHECK (from_company_id <> to_company_id),
    CONSTRAINT ic_transfers_from_comp_fkey FOREIGN KEY (tenant_id, from_company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT ic_transfers_to_comp_fkey FOREIGN KEY (tenant_id, to_company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT ic_transfers_from_journal_fkey FOREIGN KEY (tenant_id, from_company_id, from_gl_journal_id)
        REFERENCES finance.journals(tenant_id, company_id, id) ON DELETE SET NULL,
    CONSTRAINT ic_transfers_to_journal_fkey FOREIGN KEY (tenant_id, to_company_id, to_gl_journal_id)
        REFERENCES finance.journals(tenant_id, company_id, id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_ic_transfers_tenant_status ON consolidation.intercompany_transfers (tenant_id, status);

CREATE TABLE IF NOT EXISTS consolidation.intercompany_transfer_lines (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    transfer_id UUID NOT NULL REFERENCES consolidation.intercompany_transfers(id) ON DELETE CASCADE,
    product_id UUID NOT NULL,
    quantity_dispatched NUMERIC(14,4) NOT NULL CHECK (quantity_dispatched > 0),
    quantity_received NUMERIC(14,4) NOT NULL DEFAULT 0 CHECK (quantity_received >= 0),
    unit_cost_pkr NUMERIC(14,4) NOT NULL DEFAULT 0,
    transfer_price_pkr NUMERIC(14,4) NOT NULL DEFAULT 0,
    line_total_pkr NUMERIC(14,4) NOT NULL DEFAULT 0,
    source_layer_id UUID,
    source_warehouse_id UUID,
    source_bin_id UUID,
    dest_warehouse_id UUID,
    dest_bin_id UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ic_transfer_lines_product_fkey FOREIGN KEY (tenant_id, product_id)
        REFERENCES erp_inventory.product(tenant_id, product_id)
);

CREATE INDEX IF NOT EXISTS idx_ic_transfer_lines_transfer ON consolidation.intercompany_transfer_lines (tenant_id, transfer_id);

-- ── 6. Financial Consolidation Runs & Elimination Lines ─────────────────────

CREATE TABLE IF NOT EXISTS consolidation.consolidation_runs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    holding_company_id UUID REFERENCES organization.companies(id) ON DELETE SET NULL,
    run_code TEXT NOT NULL,
    fiscal_period_id UUID NOT NULL,
    reporting_currency_code CHAR(3) NOT NULL DEFAULT 'PKR' REFERENCES finance.currencies(code),
    status TEXT NOT NULL DEFAULT 'in_progress' CHECK (status IN ('in_progress', 'completed', 'failed', 'approved')),
    total_consolidated_assets_pkr NUMERIC(16,4) NOT NULL DEFAULT 0,
    total_consolidated_liabilities_pkr NUMERIC(16,4) NOT NULL DEFAULT 0,
    total_consolidated_equity_pkr NUMERIC(16,4) NOT NULL DEFAULT 0,
    total_intercompany_eliminations_pkr NUMERIC(16,4) NOT NULL DEFAULT 0,
    unrealized_profit_elimination_pkr NUMERIC(16,4) NOT NULL DEFAULT 0,
    elimination_journal_id UUID,
    participating_companies JSONB NOT NULL DEFAULT '[]'::jsonb,
    created_by UUID NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    approved_at TIMESTAMPTZ,
    approved_by UUID,
    CONSTRAINT consolidation_runs_unique UNIQUE (tenant_id, run_code)
);

CREATE INDEX IF NOT EXISTS idx_consolidation_runs_tenant ON consolidation.consolidation_runs (tenant_id, fiscal_period_id);

CREATE TABLE IF NOT EXISTS consolidation.elimination_lines (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    consolidation_run_id UUID NOT NULL REFERENCES consolidation.consolidation_runs(id) ON DELETE CASCADE,
    elimination_type TEXT NOT NULL CHECK (elimination_type IN ('intercompany_balance', 'intercompany_revenue', 'unrealized_inventory_profit')),
    from_company_id UUID NOT NULL,
    to_company_id UUID NOT NULL,
    account_code TEXT NOT NULL,
    debit_amount_pkr NUMERIC(14,4) NOT NULL DEFAULT 0,
    credit_amount_pkr NUMERIC(14,4) NOT NULL DEFAULT 0,
    description TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT elim_from_comp_fkey FOREIGN KEY (tenant_id, from_company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT elim_to_comp_fkey FOREIGN KEY (tenant_id, to_company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_elim_lines_run ON consolidation.elimination_lines (tenant_id, consolidation_run_id);

-- ── 7. External Integrations Hub: Webhooks & API Partners ───────────────────

CREATE TABLE IF NOT EXISTS integrations.webhook_subscriptions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    company_id UUID NOT NULL,
    event_type TEXT NOT NULL,
    target_url TEXT NOT NULL,
    secret_hash TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT webhooks_tenant_company_fkey FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_webhooks_company_event ON integrations.webhook_subscriptions (tenant_id, company_id, event_type);

CREATE TABLE IF NOT EXISTS integrations.outbound_event_deliveries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    subscription_id UUID NOT NULL REFERENCES integrations.webhook_subscriptions(id) ON DELETE CASCADE,
    event_type TEXT NOT NULL,
    event_payload JSONB NOT NULL,
    delivery_status TEXT NOT NULL DEFAULT 'pending' CHECK (delivery_status IN ('pending', 'delivered', 'failed', 'exhausted')),
    attempt_count INT NOT NULL DEFAULT 0,
    last_response_code INT,
    last_error TEXT,
    next_retry_at TIMESTAMPTZ,
    delivered_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_deliveries_pending ON integrations.outbound_event_deliveries (tenant_id, delivery_status, next_retry_at);

CREATE TABLE IF NOT EXISTS integrations.api_partners (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    company_id UUID NOT NULL,
    partner_name TEXT NOT NULL,
    api_key_hash TEXT NOT NULL,
    permissions JSONB NOT NULL DEFAULT '[]'::jsonb,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT api_partners_unique_name UNIQUE (tenant_id, company_id, partner_name),
    CONSTRAINT api_partners_tenant_company_fkey FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_api_partners_company ON integrations.api_partners (tenant_id, company_id);

-- ── 8. Row-Level Security (RLS) Configuration ───────────────────────────────

ALTER TABLE organization.branches ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization.branches FORCE ROW LEVEL SECURITY;

ALTER TABLE consolidation.intercompany_relationships ENABLE ROW LEVEL SECURITY;
ALTER TABLE consolidation.intercompany_relationships FORCE ROW LEVEL SECURITY;

ALTER TABLE consolidation.intercompany_transfers ENABLE ROW LEVEL SECURITY;
ALTER TABLE consolidation.intercompany_transfers FORCE ROW LEVEL SECURITY;

ALTER TABLE consolidation.intercompany_transfer_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE consolidation.intercompany_transfer_lines FORCE ROW LEVEL SECURITY;

ALTER TABLE consolidation.consolidation_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE consolidation.consolidation_runs FORCE ROW LEVEL SECURITY;

ALTER TABLE consolidation.elimination_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE consolidation.elimination_lines FORCE ROW LEVEL SECURITY;

ALTER TABLE integrations.webhook_subscriptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE integrations.webhook_subscriptions FORCE ROW LEVEL SECURITY;

ALTER TABLE integrations.outbound_event_deliveries ENABLE ROW LEVEL SECURITY;
ALTER TABLE integrations.outbound_event_deliveries FORCE ROW LEVEL SECURITY;

ALTER TABLE integrations.api_partners ENABLE ROW LEVEL SECURITY;
ALTER TABLE integrations.api_partners FORCE ROW LEVEL SECURITY;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policies WHERE schemaname = 'organization' AND tablename = 'branches' AND policyname = 'tenant_company_scope'
    ) THEN
        CREATE POLICY tenant_company_scope ON organization.branches
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND (
                    NULLIF(current_setting('app.company_id', true), '') IS NULL
                    OR company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
                )
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_policies WHERE schemaname = 'consolidation' AND tablename = 'intercompany_relationships' AND policyname = 'tenant_company_scope'
    ) THEN
        CREATE POLICY tenant_company_scope ON consolidation.intercompany_relationships
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND (
                    NULLIF(current_setting('app.company_id', true), '') IS NULL
                    OR from_company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
                    OR to_company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
                )
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_policies WHERE schemaname = 'consolidation' AND tablename = 'intercompany_transfers' AND policyname = 'tenant_company_scope'
    ) THEN
        CREATE POLICY tenant_company_scope ON consolidation.intercompany_transfers
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND (
                    NULLIF(current_setting('app.company_id', true), '') IS NULL
                    OR from_company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
                    OR to_company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
                )
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_policies WHERE schemaname = 'consolidation' AND tablename = 'intercompany_transfer_lines' AND policyname = 'tenant_scope'
    ) THEN
        CREATE POLICY tenant_scope ON consolidation.intercompany_transfer_lines
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_policies WHERE schemaname = 'consolidation' AND tablename = 'consolidation_runs' AND policyname = 'tenant_scope'
    ) THEN
        CREATE POLICY tenant_scope ON consolidation.consolidation_runs
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_policies WHERE schemaname = 'consolidation' AND tablename = 'elimination_lines' AND policyname = 'tenant_scope'
    ) THEN
        CREATE POLICY tenant_scope ON consolidation.elimination_lines
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_policies WHERE schemaname = 'integrations' AND tablename = 'webhook_subscriptions' AND policyname = 'tenant_company_scope'
    ) THEN
        CREATE POLICY tenant_company_scope ON integrations.webhook_subscriptions
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND (
                    NULLIF(current_setting('app.company_id', true), '') IS NULL
                    OR company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
                )
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_policies WHERE schemaname = 'integrations' AND tablename = 'outbound_event_deliveries' AND policyname = 'tenant_scope'
    ) THEN
        CREATE POLICY tenant_scope ON integrations.outbound_event_deliveries
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_policies WHERE schemaname = 'integrations' AND tablename = 'api_partners' AND policyname = 'tenant_company_scope'
    ) THEN
        CREATE POLICY tenant_company_scope ON integrations.api_partners
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND (
                    NULLIF(current_setting('app.company_id', true), '') IS NULL
                    OR company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
                )
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
            );
    END IF;
END $$;

-- ── 9. Register Permissions ──────────────────────────────────────────────────

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
    ('branch.view', 'View branch master data and hierarchical structures', 'branch'),
    ('branch.manage', 'Create, update and configure operating branches', 'branch'),
    ('branch.switch', 'Switch active operating branch context in user session', 'branch'),
    ('intercompany.transfer.create', 'Create and dispatch intercompany stock transfers', 'consolidation'),
    ('intercompany.transfer.receive', 'Receive and reconcile incoming intercompany shipments', 'consolidation'),
    ('consolidation.run', 'Execute multi-entity financial consolidation and elimination runs', 'consolidation'),
    ('consolidation.view', 'View consolidated financial statements and elimination vouchers', 'consolidation'),
    ('integrations.webhook.manage', 'Configure webhook subscriptions and rotate signing secrets', 'integrations'),
    ('integrations.partner.manage', 'Register and manage external API partner credentials', 'integrations')
ON CONFLICT (permission_code) DO NOTHING;

-- ── 10. Grant Privileges to erp_app Role ────────────────────────────────────

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_app') THEN
        GRANT USAGE ON SCHEMA consolidation TO erp_app;
        GRANT USAGE ON SCHEMA integrations TO erp_app;
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA consolidation TO erp_app;
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA integrations TO erp_app;
        GRANT SELECT, INSERT, UPDATE, DELETE ON organization.branches TO erp_app;
    END IF;
END $$;

-- ── 11. Register Migration ──────────────────────────────────────────────────

INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('036_multi_branch_and_intercompany_consolidation', now())
ON CONFLICT (version) DO NOTHING;
