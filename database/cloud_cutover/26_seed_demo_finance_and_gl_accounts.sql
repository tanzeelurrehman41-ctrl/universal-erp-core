-- Universal ERP -- Seed Fiscal Calendar & Chart of Accounts for DEMO-CLOUD / CLOUD-LOGISTICS
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Purpose: Unblock Supply Chain Outbox processor and GL Journal Posting for GRNs
-- Safety: Strict assertion for neondb, idempotent (ON CONFLICT DO NOTHING), zero inventory mutations.

BEGIN;

DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_fiscal_year_id uuid := '22222222-2222-4222-8222-222222222225'::uuid;
    v_current_db text;
BEGIN
    -- 0. Database Safety Check: Strictly Neon neondb only
    SELECT current_database() INTO v_current_db;
    IF v_current_db <> 'neondb' THEN
        RAISE EXCEPTION 'SECURITY ABORT: Script must execute against Neon database "neondb". Current database: %', v_current_db;
    END IF;

    -- 1. Assert Target Tenant and Company exist and are active
    IF NOT EXISTS (
        SELECT 1 FROM platform.tenants
        WHERE id = v_tenant_id AND tenant_code = 'DEMO-CLOUD' AND status = 'active'
    ) THEN
        RAISE EXCEPTION 'ABORT: Target tenant DEMO-CLOUD not found or inactive.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM organization.companies
        WHERE id = v_company_id AND tenant_id = v_tenant_id AND company_code = 'CLOUD-LOGISTICS' AND status = 'active'
    ) THEN
        RAISE EXCEPTION 'ABORT: Target company CLOUD-LOGISTICS not found or inactive.';
    END IF;

    -- 2. Seed Fiscal Year FY2026-27 for CLOUD-LOGISTICS
    INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
    VALUES (
        v_tenant_id,
        v_company_id,
        v_fiscal_year_id,
        'FY2026-27',
        '2026-07-01'::date,
        '2027-06-30'::date,
        'open'
    )
    ON CONFLICT (tenant_id, company_id, code) DO NOTHING;

    -- 3. Seed Fiscal Periods for CLOUD-LOGISTICS (including open November 2026 period)
    INSERT INTO finance.fiscal_periods (
        tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status
    )
    VALUES
        (v_tenant_id, v_company_id, '22222222-2222-4222-8222-222222222231'::uuid, v_fiscal_year_id, 1, 'July 2026', '2026-07-01'::date, '2026-07-31'::date, 'closed'),
        (v_tenant_id, v_company_id, '22222222-2222-4222-8222-222222222232'::uuid, v_fiscal_year_id, 2, 'August 2026', '2026-08-01'::date, '2026-08-31'::date, 'closed'),
        (v_tenant_id, v_company_id, '22222222-2222-4222-8222-222222222233'::uuid, v_fiscal_year_id, 3, 'September 2026', '2026-09-01'::date, '2026-09-30'::date, 'closed'),
        (v_tenant_id, v_company_id, '22222222-2222-4222-8222-222222222234'::uuid, v_fiscal_year_id, 4, 'October 2026', '2026-10-01'::date, '2026-10-31'::date, 'closed'),
        (v_tenant_id, v_company_id, '22222222-2222-4222-8222-222222222235'::uuid, v_fiscal_year_id, 5, 'November 2026', '2026-11-01'::date, '2026-11-30'::date, 'open'),
        (v_tenant_id, v_company_id, '22222222-2222-4222-8222-222222222236'::uuid, v_fiscal_year_id, 6, 'December 2026', '2026-12-01'::date, '2026-12-31'::date, 'open')
    ON CONFLICT (tenant_id, company_id, fiscal_year_id, period_no) DO NOTHING;

    -- 4. Seed Standard Chart of Accounts for CLOUD-LOGISTICS
    INSERT INTO finance.accounts (
        tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, is_control_account, allow_manual_posting
    ) VALUES
        (v_tenant_id, v_company_id, gen_random_uuid(), '1010', 'Cash on Hand', 'asset', 'debit', false, true),
        (v_tenant_id, v_company_id, gen_random_uuid(), '1020', 'Operating Bank Account (PKR)', 'asset', 'debit', false, true),
        (v_tenant_id, v_company_id, gen_random_uuid(), '1100', 'Accounts Receivable (Trade Debtors)', 'asset', 'debit', true, false),
        (v_tenant_id, v_company_id, gen_random_uuid(), '1200', 'Merchandise Inventory Asset', 'asset', 'debit', true, false),
        (v_tenant_id, v_company_id, gen_random_uuid(), '2010', 'Accounts Payable (Trade Creditors)', 'liability', 'credit', true, false),
        (v_tenant_id, v_company_id, gen_random_uuid(), '2100', 'Sales Tax Output Payable', 'liability', 'credit', false, true),
        (v_tenant_id, v_company_id, gen_random_uuid(), '3010', 'Shareholder Capital', 'equity', 'credit', false, true),
        (v_tenant_id, v_company_id, gen_random_uuid(), '3020', 'Retained Earnings', 'equity', 'credit', false, true),
        (v_tenant_id, v_company_id, gen_random_uuid(), '4010', 'Product Sales Revenue', 'revenue', 'credit', false, true),
        (v_tenant_id, v_company_id, gen_random_uuid(), '5010', 'Cost of Goods Sold (COGS)', 'expense', 'debit', false, true),
        (v_tenant_id, v_company_id, gen_random_uuid(), '6010', 'Rent & Premises Expense', 'expense', 'debit', false, true),
        (v_tenant_id, v_company_id, gen_random_uuid(), '6020', 'Salaries & Staff Welfare', 'expense', 'debit', false, true)
    ON CONFLICT (tenant_id, company_id, account_code) DO NOTHING;

    RAISE NOTICE '[FINANCE SEEDED] Fiscal year, open periods, and Chart of Accounts seeded for CLOUD-LOGISTICS.';
END $$;

COMMIT;
