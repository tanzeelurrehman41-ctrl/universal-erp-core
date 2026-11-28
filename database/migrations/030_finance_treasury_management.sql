-- 030_finance_treasury_management.sql
-- Universal ERP Phase 8: Finance & Treasury Management
-- Multi-tenant isolation with Row Level Security (RLS) on all tables.

-- ── 1. Extend Fiscal Periods with Reopening Metadata ─────────────────────────

ALTER TABLE finance.fiscal_periods
    ADD COLUMN IF NOT EXISTS reopened_at timestamptz,
    ADD COLUMN IF NOT EXISTS reopened_by uuid REFERENCES identity.users(id),
    ADD COLUMN IF NOT EXISTS reopen_reason text;

-- ── 2. Bank & Cash Accounts ───────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS finance.bank_accounts (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    account_number text NOT NULL,
    account_name text NOT NULL,
    bank_name text NOT NULL,
    branch_name text,
    swift_bic text,
    iban text,
    currency_code char(3) NOT NULL DEFAULT 'PKR' REFERENCES finance.currencies(code),
    account_type text NOT NULL CHECK (account_type IN ('checking', 'savings', 'cash_drawer', 'clearing')),
    gl_account_id uuid NOT NULL,
    current_balance numeric(20,4) NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT bank_accounts_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT bank_accounts_account_number_key UNIQUE (tenant_id, company_id, account_number),
    CONSTRAINT bank_accounts_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT bank_accounts_gl_fkey FOREIGN KEY (tenant_id, company_id, gl_account_id) REFERENCES finance.accounts(tenant_id, company_id, id)
);

ALTER TABLE finance.bank_accounts ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.bank_accounts FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON finance.bank_accounts;
CREATE POLICY tenant_company_scope ON finance.bank_accounts
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 3. Bank Statements & Statement Lines ──────────────────────────────────────

CREATE TABLE IF NOT EXISTS finance.bank_statements (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    bank_account_id uuid NOT NULL,
    statement_number text NOT NULL,
    statement_date date NOT NULL,
    opening_balance numeric(20,4) NOT NULL,
    closing_balance numeric(20,4) NOT NULL,
    status text NOT NULL DEFAULT 'imported' CHECK (status IN ('imported', 'partially_reconciled', 'reconciled')),
    imported_at timestamptz NOT NULL DEFAULT now(),
    reconciled_at timestamptz,
    reconciled_by uuid REFERENCES identity.users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT bank_statements_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT bank_statements_number_key UNIQUE (tenant_id, company_id, bank_account_id, statement_number),
    CONSTRAINT bank_statements_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT bank_statements_account_fkey FOREIGN KEY (tenant_id, company_id, bank_account_id) REFERENCES finance.bank_accounts(tenant_id, company_id, id)
);

ALTER TABLE finance.bank_statements ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.bank_statements FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON finance.bank_statements;
CREATE POLICY tenant_company_scope ON finance.bank_statements
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

CREATE TABLE IF NOT EXISTS finance.bank_statement_lines (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    statement_id uuid NOT NULL,
    line_no integer NOT NULL CHECK (line_no > 0),
    transaction_date date NOT NULL,
    value_date date,
    reference_number text,
    description text NOT NULL,
    amount numeric(20,4) NOT NULL CHECK (amount <> 0),
    balance_after numeric(20,4),
    reconciled_status text NOT NULL DEFAULT 'unmatched' CHECK (reconciled_status IN ('unmatched', 'matched', 'manually_cleared')),
    matched_journal_line_id uuid,
    matched_at timestamptz,
    CONSTRAINT bank_statement_lines_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT bank_statement_lines_number_key UNIQUE (tenant_id, company_id, statement_id, line_no),
    CONSTRAINT bank_statement_lines_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT bank_statement_lines_stmt_fkey FOREIGN KEY (tenant_id, company_id, statement_id) REFERENCES finance.bank_statements(tenant_id, company_id, id)
);

ALTER TABLE finance.bank_statement_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.bank_statement_lines FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON finance.bank_statement_lines;
CREATE POLICY tenant_company_scope ON finance.bank_statement_lines
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 4. Treasury Payments ──────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS finance.treasury_payments (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    payment_number text NOT NULL,
    payment_type text NOT NULL CHECK (payment_type IN ('vendor_disbursement', 'customer_collection', 'inter_bank_transfer', 'direct_expense')),
    payment_date date NOT NULL,
    bank_account_id uuid NOT NULL,
    destination_bank_account_id uuid,
    amount numeric(20,4) NOT NULL CHECK (amount > 0),
    currency_code char(3) NOT NULL DEFAULT 'PKR' REFERENCES finance.currencies(code),
    payment_method text NOT NULL CHECK (payment_method IN ('wire', 'ach', 'cheque', 'cash', 'internal', 'card')),
    reference_number text,
    payee_payer_name text NOT NULL,
    gl_journal_id uuid,
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'approved', 'cleared', 'void')),
    created_by uuid NOT NULL REFERENCES identity.users(id),
    approved_by uuid REFERENCES identity.users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT treasury_payments_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT treasury_payments_number_key UNIQUE (tenant_id, company_id, payment_number),
    CONSTRAINT treasury_payments_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT treasury_payments_bank_fkey FOREIGN KEY (tenant_id, company_id, bank_account_id) REFERENCES finance.bank_accounts(tenant_id, company_id, id)
);

ALTER TABLE finance.treasury_payments ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.treasury_payments FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON finance.treasury_payments;
CREATE POLICY tenant_company_scope ON finance.treasury_payments
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 5. Tax Codes & Tax Filings ────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS finance.tax_codes (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    code text NOT NULL,
    name text NOT NULL,
    tax_type text NOT NULL CHECK (tax_type IN ('sales_tax_output', 'withholding_tax_input', 'vat', 'customs')),
    rate_percent numeric(8,4) NOT NULL CHECK (rate_percent >= 0),
    gl_payable_account_id uuid NOT NULL,
    gl_receivable_account_id uuid,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT tax_codes_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT tax_codes_code_key UNIQUE (tenant_id, company_id, code),
    CONSTRAINT tax_codes_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT tax_codes_payable_gl_fkey FOREIGN KEY (tenant_id, company_id, gl_payable_account_id) REFERENCES finance.accounts(tenant_id, company_id, id)
);

ALTER TABLE finance.tax_codes ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.tax_codes FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON finance.tax_codes;
CREATE POLICY tenant_company_scope ON finance.tax_codes
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

CREATE TABLE IF NOT EXISTS finance.tax_filings (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    filing_number text NOT NULL,
    period_start date NOT NULL,
    period_end date NOT NULL CHECK (period_end >= period_start),
    tax_type text NOT NULL,
    total_taxable_amount numeric(20,4) NOT NULL DEFAULT 0,
    output_tax_amount numeric(20,4) NOT NULL DEFAULT 0,
    input_tax_amount numeric(20,4) NOT NULL DEFAULT 0,
    net_tax_payable numeric(20,4) NOT NULL DEFAULT 0,
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'filed', 'settled')),
    filed_at timestamptz,
    gl_settlement_journal_id uuid,
    notes text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT tax_filings_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT tax_filings_number_key UNIQUE (tenant_id, company_id, filing_number),
    CONSTRAINT tax_filings_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id)
);

ALTER TABLE finance.tax_filings ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.tax_filings FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON finance.tax_filings;
CREATE POLICY tenant_company_scope ON finance.tax_filings
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 6. Accruals, Prepayments & Recurring Schedules ────────────────────────────

CREATE TABLE IF NOT EXISTS finance.recurring_schedules (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    schedule_name text NOT NULL,
    schedule_type text NOT NULL CHECK (schedule_type IN ('accrual', 'prepayment', 'recurring_expense', 'amortization')),
    frequency text NOT NULL CHECK (frequency IN ('monthly', 'quarterly', 'annual')),
    start_date date NOT NULL,
    end_date date CHECK (end_date IS NULL OR end_date >= start_date),
    total_amount numeric(20,4) NOT NULL CHECK (total_amount > 0),
    period_amount numeric(20,4) NOT NULL CHECK (period_amount > 0),
    debit_account_id uuid NOT NULL,
    credit_account_id uuid NOT NULL,
    periods_count integer NOT NULL DEFAULT 1 CHECK (periods_count > 0),
    posted_periods_count integer NOT NULL DEFAULT 0 CHECK (posted_periods_count >= 0),
    next_run_date date NOT NULL,
    auto_reverse_accrual boolean NOT NULL DEFAULT false,
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'completed', 'paused')),
    description_template text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT recurring_schedules_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT recurring_schedules_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT recurring_schedules_debit_gl_fkey FOREIGN KEY (tenant_id, company_id, debit_account_id) REFERENCES finance.accounts(tenant_id, company_id, id),
    CONSTRAINT recurring_schedules_credit_gl_fkey FOREIGN KEY (tenant_id, company_id, credit_account_id) REFERENCES finance.accounts(tenant_id, company_id, id)
);

ALTER TABLE finance.recurring_schedules ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.recurring_schedules FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON finance.recurring_schedules;
CREATE POLICY tenant_company_scope ON finance.recurring_schedules
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 7. Fixed Assets & Depreciation ────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS finance.fixed_assets (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    asset_code text NOT NULL,
    asset_name text NOT NULL,
    asset_category text NOT NULL CHECK (asset_category IN ('equipment', 'machinery', 'vehicles', 'buildings', 'it_hardware', 'furniture')),
    acquisition_date date NOT NULL,
    acquisition_cost numeric(20,4) NOT NULL CHECK (acquisition_cost > 0),
    salvage_value numeric(20,4) NOT NULL DEFAULT 0 CHECK (salvage_value >= 0),
    useful_life_months integer NOT NULL CHECK (useful_life_months > 0),
    depreciation_method text NOT NULL DEFAULT 'straight_line' CHECK (depreciation_method IN ('straight_line', 'reducing_balance')),
    asset_gl_account_id uuid NOT NULL,
    accumulated_depr_account_id uuid NOT NULL,
    depreciation_expense_account_id uuid NOT NULL,
    current_book_value numeric(20,4) NOT NULL,
    accumulated_depreciation numeric(20,4) NOT NULL DEFAULT 0,
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'fully_depreciated', 'disposed')),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fixed_assets_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT fixed_assets_code_key UNIQUE (tenant_id, company_id, asset_code),
    CONSTRAINT fixed_assets_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT fixed_assets_asset_gl_fkey FOREIGN KEY (tenant_id, company_id, asset_gl_account_id) REFERENCES finance.accounts(tenant_id, company_id, id),
    CONSTRAINT fixed_assets_accum_gl_fkey FOREIGN KEY (tenant_id, company_id, accumulated_depr_account_id) REFERENCES finance.accounts(tenant_id, company_id, id),
    CONSTRAINT fixed_assets_exp_gl_fkey FOREIGN KEY (tenant_id, company_id, depreciation_expense_account_id) REFERENCES finance.accounts(tenant_id, company_id, id)
);

ALTER TABLE finance.fixed_assets ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.fixed_assets FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON finance.fixed_assets;
CREATE POLICY tenant_company_scope ON finance.fixed_assets
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

CREATE TABLE IF NOT EXISTS finance.asset_depreciation_runs (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    run_number text NOT NULL,
    run_date date NOT NULL,
    fiscal_period_id uuid NOT NULL,
    asset_id uuid NOT NULL,
    depreciation_amount numeric(20,4) NOT NULL CHECK (depreciation_amount > 0),
    gl_journal_id uuid,
    status text NOT NULL DEFAULT 'posted' CHECK (status IN ('posted', 'reversed')),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT asset_depreciation_runs_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT asset_depreciation_runs_number_key UNIQUE (tenant_id, company_id, run_number),
    CONSTRAINT asset_depreciation_runs_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT asset_depreciation_runs_period_fkey FOREIGN KEY (tenant_id, company_id, fiscal_period_id) REFERENCES finance.fiscal_periods(tenant_id, company_id, id),
    CONSTRAINT asset_depreciation_runs_asset_fkey FOREIGN KEY (tenant_id, company_id, asset_id) REFERENCES finance.fixed_assets(tenant_id, company_id, id)
);

ALTER TABLE finance.asset_depreciation_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.asset_depreciation_runs FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON finance.asset_depreciation_runs;
CREATE POLICY tenant_company_scope ON finance.asset_depreciation_runs
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 8. Period Close Checklists ────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS finance.period_close_checklists (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    fiscal_period_id uuid NOT NULL,
    task_code text NOT NULL,
    task_name text NOT NULL,
    is_completed boolean NOT NULL DEFAULT false,
    completed_at timestamptz,
    completed_by uuid REFERENCES identity.users(id),
    notes text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT period_close_checklists_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT period_close_checklists_task_key UNIQUE (tenant_id, company_id, fiscal_period_id, task_code),
    CONSTRAINT period_close_checklists_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id),
    CONSTRAINT period_close_checklists_period_fkey FOREIGN KEY (tenant_id, company_id, fiscal_period_id) REFERENCES finance.fiscal_periods(tenant_id, company_id, id)
);

ALTER TABLE finance.period_close_checklists ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.period_close_checklists FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON finance.period_close_checklists;
CREATE POLICY tenant_company_scope ON finance.period_close_checklists
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 9. Financial Audit Logs ───────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS finance.financial_audit_logs (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    event_type text NOT NULL,
    entity_name text NOT NULL,
    entity_id text NOT NULL,
    actor_user_id uuid NOT NULL,
    description text NOT NULL,
    metadata_json text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT financial_audit_logs_pkey PRIMARY KEY (tenant_id, company_id, id),
    CONSTRAINT financial_audit_logs_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id)
);

ALTER TABLE finance.financial_audit_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.financial_audit_logs FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_company_scope ON finance.financial_audit_logs;
CREATE POLICY tenant_company_scope ON finance.financial_audit_logs
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 10. Register Permissions in authorization Schema ──────────────────────────

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
    ('finance.account.view', 'View Chart of Accounts and account ledgers', 'finance'),
    ('finance.account.manage', 'Create and modify General Ledger accounts', 'finance'),
    ('finance.journal.view', 'View posted and draft GL journals', 'finance'),
    ('finance.journal.post', 'Post balanced double-entry GL journals', 'finance'),
    ('finance.journal.reverse', 'Reverse previously posted GL journals', 'finance'),
    ('finance.period.manage', 'Create and manage fiscal years and periods', 'finance'),
    ('finance.period.close', 'Execute period-end and year-end close checklists', 'finance'),
    ('finance.period.reopen', 'Reopen closed fiscal periods with audit authorization', 'finance'),
    ('finance.bank.manage', 'Manage bank and cash accounts and import statements', 'finance'),
    ('finance.bank.reconcile', 'Perform bank reconciliation and match statement lines', 'finance'),
    ('finance.tax.manage', 'Manage tax codes, calculate liabilities and submit tax filings', 'finance'),
    ('finance.asset.manage', 'Manage fixed asset register and acquisition costs', 'finance'),
    ('finance.asset.depreciate', 'Execute straight-line and reducing-balance asset depreciation', 'finance'),
    ('finance.reports.view', 'View trial balance, financial statements and control dashboards', 'finance')
ON CONFLICT (permission_code) DO NOTHING;

-- Grant table privileges to application runtime user
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA finance TO erp_dev;

-- ── 11. Migration Registry ───────────────────────────────────────────────────

INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('030_finance_treasury_management', now())
ON CONFLICT (version) DO NOTHING;
