-- Rollback Migration 035: Statutory Wage Matrix Hardening & Jurisdictional Resolution

-- 1. Remove new config keys introduced in 035
DELETE FROM hr.statutory_configurations
WHERE config_key IN (
    'min_wage_sindh_unskilled',
    'min_wage_sindh_semi_skilled',
    'min_wage_sindh_skilled',
    'min_wage_sindh_highly_skilled',
    'social_security_ict'
);

-- 2. Drop skill category constraint and column from employees
ALTER TABLE hr.employees DROP CONSTRAINT IF EXISTS employees_skill_category_check;
ALTER TABLE hr.employees DROP COLUMN IF EXISTS skill_category;

-- 3. Remove migration record
DELETE FROM public.schema_migrations
WHERE version = '035_statutory_wage_matrix_hardening';
