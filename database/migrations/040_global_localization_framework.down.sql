-- Universal ERP — Migration 040 Rollback Companion
-- Reverts global localization framework tables and restore default constraints.

-- 1. Restore defaults on platform.tenants and organization.branches
ALTER TABLE platform.tenants
    ALTER COLUMN default_country_code SET DEFAULT 'PK'::bpchar,
    ALTER COLUMN default_currency_code SET DEFAULT 'PKR'::bpchar,
    ALTER COLUMN default_timezone SET DEFAULT 'Asia/Karachi'::text;

ALTER TABLE organization.branches
    ALTER COLUMN country_code SET DEFAULT 'PK'::bpchar;

-- 2. Remove seeded international billing tax rules
DELETE FROM platform.billing_tax_rules
WHERE country_code IN ('US', 'GB', 'DE', 'FR', 'EU', 'AE', 'SA');

-- 3. Drop pluggable payroll country packages table
DROP TABLE IF EXISTS hr.payroll_country_packages CASCADE;

-- 4. Drop jurisdictions table
DROP TABLE IF EXISTS platform.jurisdictions CASCADE;

-- 5. Drop countries table
DROP TABLE IF EXISTS platform.countries CASCADE;
