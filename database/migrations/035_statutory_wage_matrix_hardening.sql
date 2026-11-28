-- Universal ERP Migration 035: Statutory Wage Matrix Hardening & Jurisdictional Resolution
-- Implements official Pakistani Federal & Provincial statutory wage configurations, skill tiers, and decoupled contribution bases
-- Complies with Finance Act 2026, FBR TY2027 Withholding Rate Card, and Provincial Labour Gazettes

-- ── 1. Employees Skill Category Support ────────────────────────────────────────

ALTER TABLE hr.employees ADD COLUMN IF NOT EXISTS skill_category TEXT NOT NULL DEFAULT 'unskilled';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'employees_skill_category_check'
    ) THEN
        ALTER TABLE hr.employees
            ADD CONSTRAINT employees_skill_category_check
            CHECK (skill_category = ANY (ARRAY['unskilled'::text, 'semi_skilled'::text, 'skilled'::text, 'highly_skilled'::text]));
    END IF;
END $$;

-- ── 2. Seed Verified Jurisdictional Statutory Wage Matrix Across All Companies ──

DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN SELECT DISTINCT tenant_id, id AS company_id FROM organization.companies LOOP

        -- Seed / Update verified effective-dated statutory configurations
        INSERT INTO hr.statutory_configurations (
            tenant_id, company_id, config_key, config_name, effective_from, effective_to,
            rate_percentage, threshold_amount_pkr, employer_rate_percentage,
            jurisdiction, authoritative_source, approval_reference
        )
        VALUES
            -- ── Federal & ICT (Finance Division OM & ICT Notification ADLW 8(20)-ICT/2024-934) ──
            (r.tenant_id, r.company_id, 'min_wage_federal', 'Federal Civil Employee Minimum Gross Wage', '2026-07-01', NULL, NULL, 40700.0, NULL, 'FEDERAL', 'Finance Division OM / Federal Wage Notification 2026', 'FED-FD-2026-OM'),
            (r.tenant_id, r.company_id, 'min_wage_ict', 'ICT Industrial/Commercial Unskilled Minimum Wage', '2026-07-01', NULL, NULL, 40700.0, NULL, 'ICT', 'ICT Notification ADLW 8(20)-ICT/2024-934', 'ADLW-8(20)-ICT-2024-934'),
            (r.tenant_id, r.company_id, 'min_wage_federal', 'Federal Minimum Wage Benchmark (Prior)', '2024-07-01', '2026-06-30', NULL, 37000.0, NULL, 'FEDERAL', 'Ministry of Overseas Pakistanis Notification 2024', 'FED-MOP-2024'),
            (r.tenant_id, r.company_id, 'min_wage_ict', 'ICT Minimum Wage Benchmark (Prior)', '2024-07-01', '2026-06-30', NULL, 37000.0, NULL, 'ICT', 'ICT Administration Notification 2024', 'ICT-ADMIN-2024'),

            -- ── Punjab (Punjab Minimum Wages Board Gazette - Legal PKR 40,000 retained) ──
            (r.tenant_id, r.company_id, 'min_wage_punjab', 'Punjab Minimum Wage Benchmark (Prior 2024)', '2024-07-01', '2025-06-30', NULL, 37000.0, NULL, 'PUNJAB', 'Punjab Minimum Wages Board Gazette 2024', 'PMWB-NOTIF-2024'),
            (r.tenant_id, r.company_id, 'min_wage_punjab', 'Punjab Minimum Wage Benchmark (2025)', '2025-07-01', '2026-06-30', NULL, 40000.0, NULL, 'PUNJAB', 'Punjab Minimum Wages Board Gazette 2025', 'PMWB-NOTIF-2025'),
            (r.tenant_id, r.company_id, 'min_wage_punjab', 'Punjab Minimum Wage Benchmark (Current)', '2026-07-01', NULL, NULL, 40000.0, NULL, 'PUNJAB', 'Punjab Minimum Wages Board Gazette (Retained Legal Rate)', 'PMWB-GAZ-2025-RETAINED'),

            -- ── Sindh (Sindh Minimum Wages Board Gazette 2026 with Skill Categories) ──
            (r.tenant_id, r.company_id, 'min_wage_sindh', 'Sindh Minimum Wage Benchmark (Prior 2024)', '2024-07-01', '2025-06-30', NULL, 37000.0, NULL, 'SINDH', 'Sindh Minimum Wages Board Gazette 2024', 'SMWB-GAZ-2024'),
            (r.tenant_id, r.company_id, 'min_wage_sindh', 'Sindh Minimum Wage Benchmark (2025)', '2025-07-01', '2026-06-30', NULL, 40000.0, NULL, 'SINDH', 'Sindh Minimum Wages Board Gazette 2025', 'SMWB-GAZ-2025'),
            (r.tenant_id, r.company_id, 'min_wage_sindh', 'Sindh Minimum Wage Unskilled Default', '2026-07-01', NULL, NULL, 43000.0, NULL, 'SINDH', 'Sindh Minimum Wages Board Gazette 2026', 'SMWB-GAZ-2026-UNSKILLED'),
            (r.tenant_id, r.company_id, 'min_wage_sindh_unskilled', 'Sindh Minimum Wage - Unskilled', '2026-07-01', NULL, NULL, 43000.0, NULL, 'SINDH', 'Sindh Minimum Wages Board Gazette 2026', 'SMWB-GAZ-2026-UNSKILLED'),
            (r.tenant_id, r.company_id, 'min_wage_sindh_semi_skilled', 'Sindh Minimum Wage - Semi-Skilled', '2026-07-01', NULL, NULL, 44484.0, NULL, 'SINDH', 'Sindh Minimum Wages Board Gazette 2026', 'SMWB-GAZ-2026-SEMISKILLED'),
            (r.tenant_id, r.company_id, 'min_wage_sindh_skilled', 'Sindh Minimum Wage - Skilled', '2026-07-01', NULL, NULL, 53350.0, NULL, 'SINDH', 'Sindh Minimum Wages Board Gazette 2026', 'SMWB-GAZ-2026-SKILLED'),
            (r.tenant_id, r.company_id, 'min_wage_sindh_highly_skilled', 'Sindh Minimum Wage - Highly Skilled', '2026-07-01', NULL, NULL, 55626.0, NULL, 'SINDH', 'Sindh Minimum Wages Board Gazette 2026', 'SMWB-GAZ-2026-HIGHSKILLED'),

            -- ── Khyber Pakhtunkhwa (KP Minimum Wages Board Notification - Legal PKR 40,000 retained) ──
            (r.tenant_id, r.company_id, 'min_wage_kpk', 'KP Minimum Wage Benchmark (Prior 2024)', '2024-07-01', '2025-06-30', NULL, 36000.0, NULL, 'KPK', 'KP Minimum Wages Board Gazette 2024', 'KPMWB-GAZ-2024'),
            (r.tenant_id, r.company_id, 'min_wage_kpk', 'KP Minimum Wage Benchmark (2025)', '2025-07-01', '2026-06-30', NULL, 40000.0, NULL, 'KPK', 'KP Minimum Wages Board Notification 2025', 'KPMWB-NOTIF-2025'),
            (r.tenant_id, r.company_id, 'min_wage_kpk', 'KP Minimum Wage Benchmark (Current)', '2026-07-01', NULL, NULL, 40000.0, NULL, 'KPK', 'KP Minimum Wages Board Notification (Verified Legal Rate)', 'KPMWB-NOTIF-2025-RETAINED'),

            -- ── Balochistan (Balochistan Labour Board - PKR 37,000 retained) ──
            (r.tenant_id, r.company_id, 'min_wage_balochistan', 'Balochistan Minimum Wage Benchmark (Prior)', '2024-07-01', '2026-06-30', NULL, 37000.0, NULL, 'BALOCHISTAN', 'Balochistan Labour Gazette 2024', 'BLB-NOTIF-2024'),
            (r.tenant_id, r.company_id, 'min_wage_balochistan', 'Balochistan Minimum Wage Benchmark (Current)', '2026-07-01', NULL, NULL, 37000.0, NULL, 'BALOCHISTAN', 'Balochistan Labour Department Verified Legal Benchmark', 'BLB-NOTIF-2026-RETAINED'),

            -- ── EOBI Statutory Contribution Base (Decoupled from Provincial Wages) ──
            (r.tenant_id, r.company_id, 'eobi_contribution_base_2025_2026', 'EOBI Statutory Contribution Base (Prior)', '2024-07-01', '2026-06-30', 1.0, 32000.0, 5.0, 'FEDERAL', 'EOBI Act 1976 / Statutory Notification 2024', 'EOBI-STAT-2024'),
            (r.tenant_id, r.company_id, 'eobi_contribution_base_2027', 'EOBI Statutory Contribution Base TY27', '2026-07-01', NULL, 1.0, 37000.0, 5.0, 'FEDERAL', 'EOBI Act 1976 / Statutory Notification 2026', 'EOBI-STAT-2026'),

            -- ── Provincial Social Security Wage Ceilings (6% Employer, 0% Employee) ──
            (r.tenant_id, r.company_id, 'social_security_punjab', 'Punjab PESSI Contribution Ceiling', '2026-07-01', NULL, 0.0, 40000.0, 6.0, 'PUNJAB', 'PESSI Act 1965 / Notification 2025/2026', 'PESSI-NOTIF-2025-2026'),
            (r.tenant_id, r.company_id, 'social_security_sindh', 'Sindh SESSI Contribution Ceiling', '2026-07-01', NULL, 0.0, 43000.0, 6.0, 'SINDH', 'SESSI Act 2016 / Notification 2026', 'SESSI-NOTIF-2026'),
            (r.tenant_id, r.company_id, 'social_security_kpk', 'KP ESSI Contribution Ceiling', '2026-07-01', NULL, 0.0, 40000.0, 6.0, 'KPK', 'KP ESSI Act / Notification 2025/2026', 'KPESSI-NOTIF-2025-2026'),
            (r.tenant_id, r.company_id, 'social_security_balochistan', 'Balochistan ESSI Contribution Ceiling', '2026-07-01', NULL, 0.0, 37000.0, 6.0, 'BALOCHISTAN', 'BESSI Act / Notification 2024/2026', 'BESSI-NOTIF-2026'),
            (r.tenant_id, r.company_id, 'social_security_federal', 'Federal ICT Social Security Ceiling', '2026-07-01', NULL, 0.0, 40700.0, 6.0, 'FEDERAL', 'Federal Labour Board Notification 2026', 'FED-SS-2026'),
            (r.tenant_id, r.company_id, 'social_security_ict', 'ICT Administration Social Security Ceiling', '2026-07-01', NULL, 0.0, 40700.0, 6.0, 'ICT', 'ICT Administration Notification ADLW 8(20)-ICT/2024-934', 'ICT-SS-2026'),

            -- ── Company Model Provident Fund Policy (Voluntary / Non-Statutory) ──
            (r.tenant_id, r.company_id, 'provident_fund_policy', 'Company Model Provident Fund Policy', '2026-07-01', NULL, 8.33, NULL, 8.33, 'COMPANY', 'Company HR Policy (Non-Statutory/Voluntary)', 'CO-PF-POLICY')
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

-- ── 3. Register Migration ──────────────────────────────────────────────────

INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('035_statutory_wage_matrix_hardening', now())
ON CONFLICT (version) DO NOTHING;
