-- Universal ERP — Migration 044 Down: Rollback Global Production Readiness, Multi-Cloud HA & Disaster Recovery

-- 1. Drop Tables
DROP TABLE IF EXISTS platform.disaster_recovery_drills CASCADE;
DROP TABLE IF EXISTS platform.tenant_data_residency_routes CASCADE;
DROP TABLE IF EXISTS platform.regional_deployment_nodes CASCADE;

-- 2. Revert hr.payroll_country_packages
UPDATE hr.payroll_country_packages
SET
    version = '2026.1',
    statutory_authority = 'Canada Revenue Agency (CRA) & Revenu Québec',
    rules_metadata = jsonb_build_object(
        'cpp_base_rate_ee', 0.0595,
        'cpp_base_rate_er', 0.0595,
        'cpp_basic_exemption', 3500.00,
        'cpp_ympe_2026', 71300.00,
        'cpp2_enhanced_rate_ee', 0.040,
        'cpp2_enhanced_rate_er', 0.040,
        'cpp2_yampe_2026', 81200.00,
        'ei_rate_ee', 0.0163,
        'ei_rate_er_multiplier', 1.40,
        'ei_max_insurable_earnings_2026', 65700.00
    )
WHERE country_code = 'CA' AND package_name = 'Canada CRA & RQ Statutory Payroll Package';

UPDATE hr.payroll_country_packages
SET
    version = '2026.2',
    statutory_authority = 'Australian Taxation Office (ATO)',
    rules_metadata = jsonb_build_object(
        'superannuation_guarantee_rate', 0.12,
        'max_contribution_base_quarterly', 65070.00,
        'payday_super_clearance_business_days', 7,
        'stp_version', 'Phase 2'
    )
WHERE country_code = 'AU' AND package_name = 'Australia ATO STP2 & Payday Super Package';

UPDATE hr.payroll_country_packages
SET
    version = '2026.1',
    statutory_authority = 'CBIC, EPFO, ESIC & Income Tax Dept',
    rules_metadata = jsonb_build_object(
        'epf_employee_rate', 0.12,
        'epf_employer_rate', 0.12,
        'eps_pension_ceiling_inr', 15000.00,
        'esi_employee_rate', 0.0075,
        'esi_employer_rate', 0.0325,
        'esi_wage_ceiling_inr', 21000.00,
        'new_tax_regime_std_deduction', 75000.00
    )
WHERE country_code = 'IN' AND package_name = 'India CBIC GST & Statutory Payroll Package';
