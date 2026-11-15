-- ============================================================================
-- Migration 034: Enterprise Analytics, Business Intelligence & HR Compliance Hardening
-- Universal ERP Platform
--
-- Features:
-- 1. Hardened HR Statutory Rules: Effective-dated Tax Year 2027 tax slabs,
--    provincial jurisdiction tagging (Federal, Punjab, Sindh, KPK, Balochistan),
--    surcharge thresholds, authoritative source tracking, and salary audit logs.
-- 2. Dedicated 'analytics' schema with KPI definitions, KPI historical snapshots,
--    and executive dashboard aggregate caches.
-- 3. Row-Level Security (RLS) forced across all new tables.
-- 4. 6 fine-grained analytics permissions registered and granted to erp_app.
-- ============================================================================

-- ── 1. Enhance HR Statutory Tables ──────────────────────────────────────────

ALTER TABLE hr.employees
    ADD COLUMN IF NOT EXISTS work_jurisdiction TEXT NOT NULL DEFAULT 'FEDERAL';

ALTER TABLE hr.tax_slabs 
    ADD COLUMN IF NOT EXISTS jurisdiction TEXT NOT NULL DEFAULT 'FEDERAL',
    ADD COLUMN IF NOT EXISTS authoritative_source TEXT NOT NULL DEFAULT 'Income Tax Ordinance, 2001 / Finance Act 2026',
    ADD COLUMN IF NOT EXISTS approval_reference TEXT NOT NULL DEFAULT 'FBR-CIRCULAR-2026-SALARIED',
    ADD COLUMN IF NOT EXISTS surcharge_rate_percentage NUMERIC(8,4) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS surcharge_threshold_pkr NUMERIC(14,4) DEFAULT NULL;

ALTER TABLE hr.statutory_configurations
    ADD COLUMN IF NOT EXISTS jurisdiction TEXT NOT NULL DEFAULT 'FEDERAL',
    ADD COLUMN IF NOT EXISTS authoritative_source TEXT NOT NULL DEFAULT 'EOBI Act 1976 / Provincial Labour Gazette',
    ADD COLUMN IF NOT EXISTS approval_reference TEXT NOT NULL DEFAULT 'STAT-REG-2026-01';

-- Salary and compensation audit log table
CREATE TABLE IF NOT EXISTS hr.salary_audit_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    company_id UUID NOT NULL,
    employee_id UUID NOT NULL REFERENCES hr.employees(id) ON DELETE CASCADE,
    action_type TEXT NOT NULL,
    previous_gross_salary_pkr NUMERIC(14,4),
    new_gross_salary_pkr NUMERIC(14,4) NOT NULL,
    changed_by UUID NOT NULL,
    change_reason TEXT,
    changed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT salary_audit_tenant_company_fkey FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_sal_audit_emp ON hr.salary_audit_logs (tenant_id, company_id, employee_id, changed_at DESC);

ALTER TABLE hr.salary_audit_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.salary_audit_logs FORCE ROW LEVEL SECURITY;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policies WHERE schemaname = 'hr' AND tablename = 'salary_audit_logs' AND policyname = 'tenant_company_scope'
    ) THEN
        CREATE POLICY tenant_company_scope ON hr.salary_audit_logs
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
            );
    END IF;
END $$;

-- ── 2. Create Analytics Schema ──────────────────────────────────────────────

CREATE SCHEMA IF NOT EXISTS analytics;

-- ── 3. KPI Definitions ──────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS analytics.kpi_definitions (
    tenant_id UUID NOT NULL,
    company_id UUID NOT NULL,
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    code TEXT NOT NULL,
    name TEXT NOT NULL,
    category TEXT NOT NULL, -- 'financial', 'sales', 'purchasing', 'inventory', 'manufacturing', 'workforce'
    description TEXT,
    unit TEXT NOT NULL DEFAULT 'PKR',
    target_value NUMERIC(16,4) NOT NULL DEFAULT 0,
    calculation_method TEXT NOT NULL,
    frequency TEXT NOT NULL DEFAULT 'monthly', -- 'daily', 'weekly', 'monthly', 'quarterly', 'annual'
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT kpi_definitions_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT kpi_definitions_unique_code UNIQUE (tenant_id, company_id, code),
    CONSTRAINT kpi_definitions_tenant_company_fkey FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_kpi_def_cat ON analytics.kpi_definitions (tenant_id, company_id, category);

-- ── 4. KPI Historical Snapshots ─────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS analytics.kpi_snapshots (
    tenant_id UUID NOT NULL,
    company_id UUID NOT NULL,
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    snapshot_date DATE NOT NULL,
    kpi_code TEXT NOT NULL,
    actual_value NUMERIC(16,4) NOT NULL,
    target_value NUMERIC(16,4) NOT NULL,
    variance NUMERIC(16,4) NOT NULL,
    variance_percentage NUMERIC(8,4) NOT NULL,
    status TEXT NOT NULL, -- 'on_track', 'warning', 'critical'
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT kpi_snapshots_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT kpi_snapshots_unique_date UNIQUE (tenant_id, company_id, kpi_code, snapshot_date),
    CONSTRAINT kpi_snapshots_tenant_company_fkey FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_kpi_snap_date ON analytics.kpi_snapshots (tenant_id, company_id, snapshot_date DESC);

-- ── 5. Executive Dashboard Cache ────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS analytics.executive_dashboard_cache (
    tenant_id UUID NOT NULL,
    company_id UUID NOT NULL,
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    metric_group TEXT NOT NULL, -- 'executive_summary', 'financial', 'supply_chain', 'workforce'
    period_code TEXT NOT NULL,
    payload_json JSONB NOT NULL,
    calculated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT exec_dash_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT exec_dash_unique_group UNIQUE (tenant_id, company_id, metric_group, period_code),
    CONSTRAINT exec_dash_tenant_company_fkey FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

-- ── 6. Row-Level Security for Analytics Tables ──────────────────────────────

ALTER TABLE analytics.kpi_definitions ENABLE ROW LEVEL SECURITY;
ALTER TABLE analytics.kpi_definitions FORCE ROW LEVEL SECURITY;

ALTER TABLE analytics.kpi_snapshots ENABLE ROW LEVEL SECURITY;
ALTER TABLE analytics.kpi_snapshots FORCE ROW LEVEL SECURITY;

ALTER TABLE analytics.executive_dashboard_cache ENABLE ROW LEVEL SECURITY;
ALTER TABLE analytics.executive_dashboard_cache FORCE ROW LEVEL SECURITY;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'analytics' AND tablename = 'kpi_definitions' AND policyname = 'tenant_company_scope') THEN
        CREATE POLICY tenant_company_scope ON analytics.kpi_definitions
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
            );
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'analytics' AND tablename = 'kpi_snapshots' AND policyname = 'tenant_company_scope') THEN
        CREATE POLICY tenant_company_scope ON analytics.kpi_snapshots
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
            );
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'analytics' AND tablename = 'executive_dashboard_cache' AND policyname = 'tenant_company_scope') THEN
        CREATE POLICY tenant_company_scope ON analytics.executive_dashboard_cache
            FOR ALL TO erp_app
            USING (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
            )
            WITH CHECK (
                tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
            );
    END IF;
END $$;

-- ── 7. Seed Standard KPI Definitions ────────────────────────────────────────

DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN SELECT tenant_id, id AS company_id FROM organization.companies LOOP
        -- Financial KPIs
        INSERT INTO analytics.kpi_definitions (tenant_id, company_id, code, name, category, unit, target_value, calculation_method, frequency)
        VALUES
            (r.tenant_id, r.company_id, 'NET_PROFIT_MARGIN', 'Net Profit Margin', 'financial', '%', 15.0, 'Net Income / Total Revenue * 100', 'monthly'),
            (r.tenant_id, r.company_id, 'GROSS_PROFIT_MARGIN', 'Gross Profit Margin', 'financial', '%', 35.0, 'Gross Profit / Total Revenue * 100', 'monthly'),
            (r.tenant_id, r.company_id, 'OPERATING_CASH_FLOW', 'Operating Cash Flow', 'financial', 'PKR', 5000000.0, 'Inflow from Customers - Outflow for Ops', 'monthly'),
            (r.tenant_id, r.company_id, 'DSO', 'Days Sales Outstanding (DSO)', 'financial', 'Days', 30.0, 'Accounts Receivable / Total Credit Sales * Period Days', 'monthly'),
            (r.tenant_id, r.company_id, 'DPO', 'Days Payable Outstanding (DPO)', 'financial', 'Days', 45.0, 'Accounts Payable / Total Cost of Sales * Period Days', 'monthly')
        ON CONFLICT (tenant_id, company_id, code) DO NOTHING;

        -- Supply Chain & Inventory KPIs
        INSERT INTO analytics.kpi_definitions (tenant_id, company_id, code, name, category, unit, target_value, calculation_method, frequency)
        VALUES
            (r.tenant_id, r.company_id, 'INVENTORY_TURNOVER', 'Inventory Turnover Ratio', 'inventory', 'Ratio', 6.0, 'COGS / Average Inventory Valuation', 'monthly'),
            (r.tenant_id, r.company_id, 'PO_FULFILLMENT_RATE', 'Purchase Order Fulfillment Rate', 'purchasing', '%', 95.0, 'Completed POs / Total Issued POs * 100', 'monthly'),
            (r.tenant_id, r.company_id, 'ON_TIME_DELIVERY', 'Supplier On-Time Delivery Rate', 'purchasing', '%', 90.0, 'On-Time GRNs / Total GRNs * 100', 'monthly'),
            (r.tenant_id, r.company_id, 'BIN_UTILIZATION_PCT', 'Warehouse Bin Utilization', 'inventory', '%', 80.0, 'Occupied Bins / Total Active Bins * 100', 'monthly')
        ON CONFLICT (tenant_id, company_id, code) DO NOTHING;

        -- Manufacturing & Workforce KPIs
        INSERT INTO analytics.kpi_definitions (tenant_id, company_id, code, name, category, unit, target_value, calculation_method, frequency)
        VALUES
            (r.tenant_id, r.company_id, 'WO_COMPLETION_RATE', 'Work Order Completion Rate', 'manufacturing', '%', 92.0, 'Completed Work Orders / Released Work Orders * 100', 'monthly'),
            (r.tenant_id, r.company_id, 'SCRAP_RATE_PCT', 'Manufacturing Scrap Rate', 'manufacturing', '%', 2.5, 'Scrap Cost / Total Production Cost * 100', 'monthly'),
            (r.tenant_id, r.company_id, 'WORKFORCE_COST_RATIO', 'Workforce Cost Ratio', 'workforce', '%', 20.0, 'Total Gross Payroll / Total Revenue * 100', 'monthly'),
            (r.tenant_id, r.company_id, 'ABSENTEEISM_RATE', 'Employee Absenteeism Rate', 'workforce', '%', 3.0, 'Unplanned Absences / Total Standard Working Days * 100', 'monthly')
        ON CONFLICT (tenant_id, company_id, code) DO NOTHING;

        -- Seed Tax Year 2027 statutory tax slabs (Finance Act 2026, Effective 2026-07-01 to 2027-06-30)
        -- 8 progressive brackets per official FBR Withholding Rate Card (First Schedule, Part I, Division I - Salaried)
        INSERT INTO hr.tax_slabs (
            tenant_id, company_id, tax_year, effective_from, effective_to, slab_order,
            min_annual_income_pkr, max_annual_income_pkr, fixed_tax_amount_pkr,
            marginal_tax_rate_percentage, surcharge_rate_percentage, surcharge_threshold_pkr,
            jurisdiction, authoritative_source, approval_reference, description
        )
        VALUES
            (r.tenant_id, r.company_id, 2027, '2026-07-01', '2027-06-30', 1, 0, 600000, 0, 0.0, 0, NULL, 'FEDERAL', 'Finance Act 2026 / First Schedule Part I Div I', 'FBR-TY2027-SALARIED', 'Up to PKR 600,000 (0%)'),
            (r.tenant_id, r.company_id, 2027, '2026-07-01', '2027-06-30', 2, 600000, 1200000, 0, 1.0, 0, NULL, 'FEDERAL', 'Finance Act 2026 / First Schedule Part I Div I', 'FBR-TY2027-SALARIED', 'PKR 600,001 to 1,200,000 (1% exceeding 600k)'),
            (r.tenant_id, r.company_id, 2027, '2026-07-01', '2027-06-30', 3, 1200000, 2200000, 6000, 11.0, 0, NULL, 'FEDERAL', 'Finance Act 2026 / First Schedule Part I Div I', 'FBR-TY2027-SALARIED', 'PKR 1,200,001 to 2,200,000 (PKR 6k + 11%)'),
            (r.tenant_id, r.company_id, 2027, '2026-07-01', '2027-06-30', 4, 2200000, 3200000, 116000, 20.0, 0, NULL, 'FEDERAL', 'Finance Act 2026 / First Schedule Part I Div I', 'FBR-TY2027-SALARIED', 'PKR 2,200,001 to 3,200,000 (PKR 116k + 20%)'),
            (r.tenant_id, r.company_id, 2027, '2026-07-01', '2027-06-30', 5, 3200000, 4100000, 316000, 25.0, 0, NULL, 'FEDERAL', 'Finance Act 2026 / First Schedule Part I Div I', 'FBR-TY2027-SALARIED', 'PKR 3,200,001 to 4,100,000 (PKR 316k + 25%)'),
            (r.tenant_id, r.company_id, 2027, '2026-07-01', '2027-06-30', 6, 4100000, 5600000, 541000, 29.0, 0, NULL, 'FEDERAL', 'Finance Act 2026 / First Schedule Part I Div I', 'FBR-TY2027-SALARIED', 'PKR 4,100,001 to 5,600,000 (PKR 541k + 29%)'),
            (r.tenant_id, r.company_id, 2027, '2026-07-01', '2027-06-30', 7, 5600000, 7000000, 976000, 32.0, 0, NULL, 'FEDERAL', 'Finance Act 2026 / First Schedule Part I Div I', 'FBR-TY2027-SALARIED', 'PKR 5,600,001 to 7,000,000 (PKR 976k + 32%)'),
            (r.tenant_id, r.company_id, 2027, '2026-07-01', '2027-06-30', 8, 7000000, NULL, 1424000, 35.0, 0, NULL, 'FEDERAL', 'Finance Act 2026 / First Schedule Part I Div I', 'FBR-TY2027-SALARIED', 'Exceeding PKR 7,000,000 (PKR 1,424k + 35%)')
        ON CONFLICT (tenant_id, company_id, tax_year, slab_order) DO UPDATE
            SET min_annual_income_pkr = EXCLUDED.min_annual_income_pkr,
                max_annual_income_pkr = EXCLUDED.max_annual_income_pkr,
                fixed_tax_amount_pkr = EXCLUDED.fixed_tax_amount_pkr,
                marginal_tax_rate_percentage = EXCLUDED.marginal_tax_rate_percentage,
                jurisdiction = EXCLUDED.jurisdiction,
                authoritative_source = EXCLUDED.authoritative_source,
                approval_reference = EXCLUDED.approval_reference,
                surcharge_rate_percentage = EXCLUDED.surcharge_rate_percentage,
                surcharge_threshold_pkr = EXCLUDED.surcharge_threshold_pkr,
                description = EXCLUDED.description;

        -- Seed effective-dated jurisdiction-specific minimum wages & statutory configurations
        INSERT INTO hr.statutory_configurations (
            tenant_id, company_id, config_key, config_name, effective_from, effective_to,
            rate_percentage, threshold_amount_pkr, employer_rate_percentage,
            jurisdiction, authoritative_source, approval_reference
        )
        VALUES
            -- Current TY2027 Period (2026-07-01 onwards)
            (r.tenant_id, r.company_id, 'min_wage_federal', 'Federal Minimum Wage Benchmark', '2026-07-01', NULL, NULL, 37000.0, NULL, 'FEDERAL', 'Ministry of Overseas Pakistanis Notification 2026', 'FED-MOP-2026'),
            (r.tenant_id, r.company_id, 'min_wage_ict', 'ICT Minimum Wage Benchmark', '2026-07-01', NULL, NULL, 37000.0, NULL, 'ICT', 'ICT Administration Notification 2026', 'ICT-ADMIN-2026'),
            (r.tenant_id, r.company_id, 'min_wage_punjab', 'Punjab Minimum Wage Benchmark', '2026-07-01', NULL, NULL, 37000.0, NULL, 'PUNJAB', 'Punjab Minimum Wages Board Notification 2026', 'PMWB-NOTIF-2026'),
            (r.tenant_id, r.company_id, 'min_wage_sindh', 'Sindh Minimum Wage Benchmark', '2026-07-01', NULL, NULL, 37000.0, NULL, 'SINDH', 'Sindh Minimum Wages Board Gazette 2026', 'SMWB-GAZ-2026'),
            (r.tenant_id, r.company_id, 'min_wage_kpk', 'KP Minimum Wage Benchmark', '2026-07-01', NULL, NULL, 36000.0, NULL, 'KPK', 'KP Minimum Wages Board Notification 2026', 'KPMWB-NOTIF-2026'),
            (r.tenant_id, r.company_id, 'min_wage_balochistan', 'Balochistan Minimum Wage Benchmark', '2026-07-01', NULL, NULL, 37000.0, NULL, 'BALOCHISTAN', 'Balochistan Labour Board Notification 2026', 'BLB-NOTIF-2026'),
            (r.tenant_id, r.company_id, 'eobi_contribution_base_2027', 'EOBI Statutory Contribution Base TY27', '2026-07-01', NULL, 1.0, 37000.0, 5.0, 'FEDERAL', 'EOBI Act 1976 / Statutory Notification 2026', 'EOBI-STAT-2026'),
            (r.tenant_id, r.company_id, 'social_security_punjab', 'Punjab PESSI Contribution', '2026-07-01', NULL, 0.0, 37000.0, 6.0, 'PUNJAB', 'PESSI Act 1965 / Notification 2026', 'PESSI-NOTIF-2026'),
            (r.tenant_id, r.company_id, 'social_security_sindh', 'Sindh SESSI Contribution', '2026-07-01', NULL, 0.0, 37000.0, 6.0, 'SINDH', 'SESSI Act 2016 / Notification 2026', 'SESSI-NOTIF-2026'),
            (r.tenant_id, r.company_id, 'social_security_kpk', 'KP ESSI Contribution', '2026-07-01', NULL, 0.0, 36000.0, 6.0, 'KPK', 'KP ESSI Act / Notification 2026', 'KPESSI-NOTIF-2026'),
            (r.tenant_id, r.company_id, 'social_security_balochistan', 'Balochistan ESSI Contribution', '2026-07-01', NULL, 0.0, 37000.0, 6.0, 'BALOCHISTAN', 'BESSI Act / Notification 2026', 'BESSI-NOTIF-2026'),
            (r.tenant_id, r.company_id, 'social_security_federal', 'Federal ICT Social Security', '2026-07-01', NULL, 0.0, 37000.0, 6.0, 'FEDERAL', 'Federal Labour Board Notification 2026', 'FED-SS-2026'),
            (r.tenant_id, r.company_id, 'provident_fund_policy', 'Company Model Provident Fund Policy', '2026-07-01', NULL, 8.33, NULL, 8.33, 'COMPANY', 'Company HR Policy (Non-Statutory/Voluntary)', 'CO-PF-POLICY'),
            -- Retained Historical Period (2024-07-01 to 2026-06-30)
            (r.tenant_id, r.company_id, 'min_wage_federal', 'Federal Minimum Wage Benchmark (Prior)', '2024-07-01', '2026-06-30', NULL, 32000.0, NULL, 'FEDERAL', 'Ministry of Overseas Pakistanis Notification 2024', 'FED-MOP-2024'),
            (r.tenant_id, r.company_id, 'min_wage_punjab', 'Punjab Minimum Wage Benchmark (Prior)', '2024-07-01', '2026-06-30', NULL, 32000.0, NULL, 'PUNJAB', 'Punjab Minimum Wages Board Gazette 2024', 'PMWB-NOTIF-2024'),
            (r.tenant_id, r.company_id, 'min_wage_sindh', 'Sindh Minimum Wage Benchmark (Prior)', '2024-07-01', '2026-06-30', NULL, 32000.0, NULL, 'SINDH', 'Sindh Minimum Wages Board Gazette 2024', 'SMWB-GAZ-2024'),
            (r.tenant_id, r.company_id, 'min_wage_kpk', 'KP Minimum Wage Benchmark (Prior)', '2024-07-01', '2026-06-30', NULL, 30000.0, NULL, 'KPK', 'KP Minimum Wages Board Gazette 2024', 'KPMWB-GAZ-2024'),
            (r.tenant_id, r.company_id, 'min_wage_balochistan', 'Balochistan Minimum Wage Benchmark (Prior)', '2024-07-01', '2026-06-30', NULL, 32000.0, NULL, 'BALOCHISTAN', 'Balochistan Labour Gazette 2024', 'BLB-NOTIF-2024'),
            (r.tenant_id, r.company_id, 'eobi_contribution_base_2025_2026', 'EOBI Contribution Base Prior Period', '2024-07-01', '2026-06-30', 1.0, 32000.0, 5.0, 'FEDERAL', 'EOBI Act 1976 / Statutory Notification 2024', 'EOBI-STAT-2024')
        ON CONFLICT (tenant_id, company_id, config_key, effective_from) DO UPDATE
            SET threshold_amount_pkr = EXCLUDED.threshold_amount_pkr,
                rate_percentage = EXCLUDED.rate_percentage,
                employer_rate_percentage = EXCLUDED.employer_rate_percentage,
                effective_to = EXCLUDED.effective_to,
                jurisdiction = EXCLUDED.jurisdiction,
                authoritative_source = EXCLUDED.authoritative_source,
                approval_reference = EXCLUDED.approval_reference,
                config_name = EXCLUDED.config_name;
    END LOOP;
END $$;

-- ── 8. Register Analytics Permissions ───────────────────────────────────────

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
    ('analytics.dashboard.view', 'View high-level executive dashboard and KPI trends', 'analytics'),
    ('analytics.financial.view', 'Access financial P&L, balance sheet and cash flow analytics', 'analytics'),
    ('analytics.supply_chain.view', 'Access sales, purchasing, inventory and logistics analytics', 'analytics'),
    ('analytics.workforce.view', 'Access workforce, payroll and HR cost analytics', 'analytics'),
    ('analytics.kpi.manage', 'Configure KPI definitions, targets and snapshot frequencies', 'analytics'),
    ('analytics.export', 'Export analytics datasets in CSV, Excel and PDF formats', 'analytics')
ON CONFLICT (permission_code) DO NOTHING;

-- ── 9. Grant Privileges to erp_app Role ─────────────────────────────────────

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_app') THEN
        GRANT USAGE ON SCHEMA analytics TO erp_app;
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA analytics TO erp_app;
        GRANT SELECT, INSERT, UPDATE, DELETE ON hr.salary_audit_logs TO erp_app;
    END IF;
END $$;

-- ── 10. Register Migration ──────────────────────────────────────────────────

INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('034_enterprise_analytics_bi', now())
ON CONFLICT (version) DO NOTHING;
