-- 030_finance_treasury_management.down.sql
-- Rollback for Universal ERP Phase 8: Finance & Treasury Management

DROP TABLE IF EXISTS finance.financial_audit_logs CASCADE;
DROP TABLE IF EXISTS finance.period_close_checklists CASCADE;
DROP TABLE IF EXISTS finance.asset_depreciation_runs CASCADE;
DROP TABLE IF EXISTS finance.fixed_assets CASCADE;
DROP TABLE IF EXISTS finance.recurring_schedules CASCADE;
DROP TABLE IF EXISTS finance.tax_filings CASCADE;
DROP TABLE IF EXISTS finance.tax_codes CASCADE;
DROP TABLE IF EXISTS finance.treasury_payments CASCADE;
DROP TABLE IF EXISTS finance.bank_statement_lines CASCADE;
DROP TABLE IF EXISTS finance.bank_statements CASCADE;
DROP TABLE IF EXISTS finance.bank_accounts CASCADE;

ALTER TABLE finance.fiscal_periods
    DROP COLUMN IF EXISTS reopened_at,
    DROP COLUMN IF EXISTS reopened_by,
    DROP COLUMN IF EXISTS reopen_reason;

DELETE FROM authorization.permissions WHERE name IN (
    'finance.account.view',
    'finance.account.manage',
    'finance.journal.view',
    'finance.journal.post',
    'finance.journal.reverse',
    'finance.period.manage',
    'finance.period.close',
    'finance.period.reopen',
    'finance.bank.manage',
    'finance.bank.reconcile',
    'finance.tax.manage',
    'finance.asset.manage',
    'finance.asset.depreciate',
    'finance.reports.view'
);

DELETE FROM public.schema_migrations WHERE version = '030_finance_treasury_management';
