-- ============================================================================
-- Migration 034 Rollback: Enterprise Analytics, Business Intelligence & HR Compliance Hardening
-- Universal ERP Platform
-- ============================================================================

-- ── 1. Revoke Permissions ───────────────────────────────────────────────────

DELETE FROM "authorization".permissions
WHERE permission_code IN (
    'analytics.dashboard.view',
    'analytics.financial.view',
    'analytics.supply_chain.view',
    'analytics.workforce.view',
    'analytics.kpi.manage',
    'analytics.export'
);

-- ── 2. Drop Analytics Tables & Schema ───────────────────────────────────────

DROP TABLE IF EXISTS analytics.executive_dashboard_cache CASCADE;
DROP TABLE IF EXISTS analytics.kpi_snapshots CASCADE;
DROP TABLE IF EXISTS analytics.kpi_definitions CASCADE;

DROP SCHEMA IF EXISTS analytics CASCADE;

-- ── 3. Revert HR Schema Enhancements ────────────────────────────────────────

DROP TABLE IF EXISTS hr.salary_audit_logs CASCADE;

DELETE FROM hr.tax_slabs WHERE tax_year = 2027;
DELETE FROM hr.statutory_configurations WHERE config_key IN (
    'min_wage_federal', 'min_wage_ict', 'min_wage_punjab', 'min_wage_sindh',
    'min_wage_kpk', 'min_wage_balochistan', 'eobi_contribution_base_2027',
    'social_security_punjab', 'social_security_sindh', 'social_security_kpk',
    'social_security_balochistan', 'social_security_federal', 'provident_fund_policy',
    'eobi_contribution_base_2025_2026'
);

ALTER TABLE hr.employees
    DROP COLUMN IF EXISTS work_jurisdiction;

ALTER TABLE hr.tax_slabs
    DROP COLUMN IF EXISTS jurisdiction,
    DROP COLUMN IF EXISTS authoritative_source,
    DROP COLUMN IF EXISTS approval_reference,
    DROP COLUMN IF EXISTS surcharge_rate_percentage,
    DROP COLUMN IF EXISTS surcharge_threshold_pkr;

ALTER TABLE hr.statutory_configurations
    DROP COLUMN IF EXISTS jurisdiction,
    DROP COLUMN IF EXISTS authoritative_source,
    DROP COLUMN IF EXISTS approval_reference;

-- ── 4. Remove from Schema Migrations ────────────────────────────────────────

DELETE FROM public.schema_migrations WHERE version = '034_enterprise_analytics_bi';
