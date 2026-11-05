-- Migration 014 – Seed Chart of Accounts & Fiscal Year for Local Development Company

-- ── 1. Fiscal Year & Periods for LOCAL-DEMO ──────────────────────────────────

INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
VALUES (
    '00000000-0000-4000-8000-000000000001'::uuid,
    '00000000-0000-4000-8000-000000000002'::uuid,
    '00000000-0000-4000-8000-000000000020'::uuid,
    'FY2026-27',
    '2026-07-01'::date,
    '2027-06-30'::date,
    'open'
) ON CONFLICT (tenant_id, company_id, code) DO NOTHING;

INSERT INTO finance.fiscal_periods (tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status)
VALUES
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000021'::uuid, '00000000-0000-4000-8000-000000000020'::uuid, 1, 'July 2026', '2026-07-01'::date, '2026-07-31'::date, 'closed'),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000022'::uuid, '00000000-0000-4000-8000-000000000020'::uuid, 2, 'August 2026', '2026-08-01'::date, '2026-08-31'::date, 'closed'),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000023'::uuid, '00000000-0000-4000-8000-000000000020'::uuid, 3, 'September 2026', '2026-09-01'::date, '2026-09-30'::date, 'closed'),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000024'::uuid, '00000000-0000-4000-8000-000000000020'::uuid, 4, 'October 2026', '2026-10-01'::date, '2026-10-31'::date, 'closed'),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000025'::uuid, '00000000-0000-4000-8000-000000000020'::uuid, 5, 'November 2026', '2026-11-01'::date, '2026-11-30'::date, 'open'),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000026'::uuid, '00000000-0000-4000-8000-000000000020'::uuid, 6, 'December 2026', '2026-12-01'::date, '2026-12-31'::date, 'open')
ON CONFLICT (tenant_id, company_id, fiscal_year_id, period_no) DO NOTHING;

-- ── 2. Standard Chart of Accounts for LOCAL-DEMO ─────────────────────────────

INSERT INTO finance.accounts (
    tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, is_control_account, allow_manual_posting
) VALUES
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000101'::uuid, '1010', 'Cash on Hand', 'asset', 'debit', false, true),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000102'::uuid, '1020', 'Operating Bank Account (PKR)', 'asset', 'debit', false, true),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000110'::uuid, '1100', 'Accounts Receivable (Trade Debtors)', 'asset', 'debit', true, false),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000120'::uuid, '1200', 'Merchandise Inventory Asset', 'asset', 'debit', true, false),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000201'::uuid, '2010', 'Accounts Payable (Trade Creditors)', 'liability', 'credit', true, false),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000210'::uuid, '2100', 'Sales Tax Output Payable', 'liability', 'credit', false, true),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000301'::uuid, '3010', 'Shareholder Capital', 'equity', 'credit', false, true),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000302'::uuid, '3020', 'Retained Earnings', 'equity', 'credit', false, true),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000401'::uuid, '4010', 'Product Sales Revenue', 'revenue', 'credit', false, true),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000501'::uuid, '5010', 'Cost of Goods Sold (COGS)', 'expense', 'debit', false, true),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000601'::uuid, '6010', 'Rent & Premises Expense', 'expense', 'debit', false, true),
    ('00000000-0000-4000-8000-000000000001'::uuid, '00000000-0000-4000-8000-000000000002'::uuid, '00000000-0000-4000-8000-000000000602'::uuid, '6020', 'Salaries & Staff Welfare', 'expense', 'debit', false, true)
ON CONFLICT (tenant_id, company_id, account_code) DO NOTHING;
