-- Universal ERP — Migration 043: Worldwide Localization Expansion & Audit Checkpoints
-- Establishes full statutory metadata, subnational jurisdictions, pluggable payroll packages,
-- and external audit-chain immutable checkpoint ledger for Phase 16.

-- 1. Currencies Extension (Ensure all global currencies exist)
INSERT INTO finance.currencies (code, name, minor_unit, is_active)
VALUES
    ('PLN', 'Polish Zloty', 2, true),
    ('SEK', 'Swedish Krona', 2, true),
    ('BRL', 'Brazilian Real', 2, true),
    ('ZAR', 'South African Rand', 2, true)
ON CONFLICT (code) DO NOTHING;

-- 2. Expand Platform Countries Registry
INSERT INTO platform.countries (
    country_code, alpha3_code, numeric_code, country_name, default_currency_code,
    default_timezone, default_locale, fiscal_year_start_month, fiscal_year_start_day,
    address_format_type, tax_system_type, einvoicing_mandate_type, is_supported
) VALUES
    ('ES', 'ESP', 724, 'Spain', 'EUR', 'Europe/Madrid', 'es-ES', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('IT', 'ITA', 380, 'Italy', 'EUR', 'Europe/Rome', 'it-IT', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('NL', 'NLD', 528, 'Netherlands', 'EUR', 'Europe/Amsterdam', 'nl-NL', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('BE', 'BEL', 056, 'Belgium', 'EUR', 'Europe/Brussels', 'nl-BE', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('AT', 'AUT', 040, 'Austria', 'EUR', 'Europe/Vienna', 'de-AT', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('SE', 'SWE', 752, 'Sweden', 'SEK', 'Europe/Stockholm', 'sv-SE', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('PL', 'POL', 616, 'Poland', 'PLN', 'Europe/Warsaw', 'pl-PL', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('JP', 'JPN', 392, 'Japan', 'JPY', 'Asia/Tokyo', 'ja-JP', 4, 1, 'standard', 'vat', 'none', true),
    ('SG', 'SGP', 702, 'Singapore', 'SGD', 'Asia/Singapore', 'en-SG', 1, 1, 'standard', 'gst', 'peppol_bis3', true),
    ('CH', 'CHE', 756, 'Switzerland', 'CHF', 'Europe/Zurich', 'de-CH', 1, 1, 'standard', 'vat', 'none', true),
    ('BR', 'BRA', 076, 'Brazil', 'BRL', 'America/Sao_Paulo', 'pt-BR', 1, 1, 'standard', 'vat', 'none', true),
    ('ZA', 'ZAF', 710, 'South Africa', 'ZAR', 'Africa/Johannesburg', 'en-ZA', 3, 1, 'standard', 'vat', 'none', true)
ON CONFLICT (country_code) DO UPDATE SET
    country_name = EXCLUDED.country_name,
    default_currency_code = EXCLUDED.default_currency_code,
    default_timezone = EXCLUDED.default_timezone,
    tax_system_type = EXCLUDED.tax_system_type;

-- 3. Comprehensive Sub-National Jurisdictions
INSERT INTO platform.jurisdictions (country_code, jurisdiction_code, name, jurisdiction_type, standard_tax_rate, reduced_tax_rate, statutory_authority)
VALUES
    -- Canada: Complete Provinces and Territories
    ('CA', 'ON', 'Ontario (HST 13%)', 'province', 13.0000, NULL, 'Canada Revenue Agency (CRA) / Excise Tax Act'),
    ('CA', 'QC', 'Quebec (GST 5% + QST 9.975%)', 'province', 14.9750, NULL, 'Revenu Québec / CRA'),
    ('CA', 'BC', 'British Columbia (GST 5% + PST 7%)', 'province', 12.0000, NULL, 'CRA / BC Ministry of Finance'),
    ('CA', 'AB', 'Alberta (GST 5% only)', 'province', 5.0000, NULL, 'Canada Revenue Agency (CRA)'),
    ('CA', 'SK', 'Saskatchewan (GST 5% + PST 6%)', 'province', 11.0000, NULL, 'CRA / SK Ministry of Finance'),
    ('CA', 'MB', 'Manitoba (GST 5% + PST 7%)', 'province', 12.0000, NULL, 'CRA / MB Finance Taxation Division'),
    ('CA', 'NS', 'Nova Scotia (HST 15%)', 'province', 15.0000, NULL, 'Canada Revenue Agency (CRA)'),
    ('CA', 'NB', 'New Brunswick (HST 15%)', 'province', 15.0000, NULL, 'Canada Revenue Agency (CRA)'),
    ('CA', 'NL', 'Newfoundland and Labrador (HST 15%)', 'province', 15.0000, NULL, 'Canada Revenue Agency (CRA)'),
    ('CA', 'PE', 'Prince Edward Island (HST 15%)', 'province', 15.0000, NULL, 'Canada Revenue Agency (CRA)'),
    ('CA', 'YT', 'Yukon (GST 5% only)', 'territory', 5.0000, NULL, 'Canada Revenue Agency (CRA)'),
    ('CA', 'NT', 'Northwest Territories (GST 5% only)', 'territory', 5.0000, NULL, 'Canada Revenue Agency (CRA)'),
    ('CA', 'NU', 'Nunavut (GST 5% only)', 'territory', 5.0000, NULL, 'Canada Revenue Agency (CRA)'),

    -- Australia: All States and Territories with Payroll Tax & GST
    ('AU', 'NSW', 'New South Wales (ATO GST & Revenue NSW)', 'state', 10.0000, NULL, 'Australian Taxation Office / Revenue NSW'),
    ('AU', 'VIC', 'Victoria (ATO GST & State Revenue Office VIC)', 'state', 10.0000, NULL, 'Australian Taxation Office / SRO VIC'),
    ('AU', 'QLD', 'Queensland (ATO GST & Queensland Revenue Office)', 'state', 10.0000, NULL, 'Australian Taxation Office / QRO'),
    ('AU', 'WA', 'Western Australia (ATO GST & RevenueWA)', 'state', 10.0000, NULL, 'Australian Taxation Office / RevenueWA'),
    ('AU', 'SA', 'South Australia (ATO GST & RevenueSA)', 'state', 10.0000, NULL, 'Australian Taxation Office / RevenueSA'),
    ('AU', 'TAS', 'Tasmania (ATO GST & State Revenue Office TAS)', 'state', 10.0000, NULL, 'Australian Taxation Office / SRO TAS'),
    ('AU', 'ACT', 'Australian Capital Territory (ATO GST & ACT Revenue Office)', 'territory', 10.0000, NULL, 'Australian Taxation Office / ACT Revenue'),
    ('AU', 'NT', 'Northern Territory (ATO GST & Territory Revenue Office)', 'territory', 10.0000, NULL, 'Australian Taxation Office / TRO'),

    -- India: Strategic Industrial and Commercial States
    ('IN', 'MH', 'Maharashtra (State Code 27)', 'state', 18.0000, 5.0000, 'Central Board of Indirect Taxes and Customs (CBIC) / Maharashtra GST'),
    ('IN', 'KA', 'Karnataka (State Code 29)', 'state', 18.0000, 5.0000, 'CBIC / Karnataka Commercial Taxes Dept'),
    ('IN', 'DL', 'Delhi (State Code 07)', 'state', 18.0000, 5.0000, 'CBIC / Delhi Dept of Trade and Taxes'),
    ('IN', 'TN', 'Tamil Nadu (State Code 33)', 'state', 18.0000, 5.0000, 'CBIC / Tamil Nadu Commercial Taxes'),
    ('IN', 'GJ', 'Gujarat (State Code 24)', 'state', 18.0000, 5.0000, 'CBIC / Gujarat Commercial Tax Dept'),
    ('IN', 'UP', 'Uttar Pradesh (State Code 09)', 'state', 18.0000, 5.0000, 'CBIC / UP Commercial Tax Dept'),
    ('IN', 'TG', 'Telangana (State Code 36)', 'state', 18.0000, 5.0000, 'CBIC / Telangana Commercial Taxes'),

    -- European Union: National Jurisdictions
    ('NL', 'NL_ALL', 'Netherlands (Belastingdienst)', 'federal', 21.0000, 9.0000, 'Belastingdienst'),
    ('ES', 'ES_ALL', 'Spain (Agencia Tributaria - AEAT)', 'federal', 21.0000, 10.0000, 'Agencia Estatal de Administración Tributaria (AEAT)'),
    ('IT', 'IT_ALL', 'Italy (Agenzia delle Entrate)', 'federal', 22.0000, 10.0000, 'Agenzia delle Entrate'),
    ('BE', 'BE_ALL', 'Belgium (SPF Finances)', 'federal', 21.0000, 6.0000, 'Service Public Fédéral Finances'),
    ('AT', 'AT_ALL', 'Austria (Bundesministerium für Finanzen)', 'federal', 20.0000, 10.0000, 'Bundesministerium für Finanzen (BMF)'),
    ('SE', 'SE_ALL', 'Sweden (Skatteverket)', 'federal', 25.0000, 12.0000, 'Skatteverket'),
    ('PL', 'PL_ALL', 'Poland (Krajowa Administracja Skarbowa)', 'federal', 23.0000, 8.0000, 'Krajowa Administracja Skarbowa (KAS)')
ON CONFLICT (country_code, jurisdiction_code) DO NOTHING;

-- 4. Register Pluggable Payroll Packages
INSERT INTO hr.payroll_country_packages (
    country_code, package_name, adapter_class_name, version, statutory_authority,
    currency_code, effective_from, is_active, rules_metadata
) VALUES
    ('SA', 'Saudi Arabia GOSI & EOSA Package', 'SaudiArabiaPayrollAdapter', '2026.1', 'General Organization for Social Insurance (GOSI)',
     'SAR', '2026-01-01', true, jsonb_build_object(
        'gosi_annuities_ee', 0.09,
        'gosi_annuities_er', 0.09,
        'saned_unemployment_ee', 0.0075,
        'saned_unemployment_er', 0.0075,
        'occupational_hazards_er', 0.02,
        'max_contribution_base_sar', 45000.00,
        'min_nitaqat_salary_sar', 4000.00
     )),

    ('CA', 'Canada CRA & RQ Statutory Payroll Package', 'CanadaPayrollAdapter', '2026.1', 'Canada Revenue Agency (CRA) & Revenu Québec',
     'CAD', '2026-01-01', true, jsonb_build_object(
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
     )),

    ('AU', 'Australia ATO STP2 & Payday Super Package', 'AustraliaPayrollAdapter', '2026.2', 'Australian Taxation Office (ATO)',
     'AUD', '2026-07-01', true, jsonb_build_object(
        'superannuation_guarantee_rate', 0.12,
        'max_contribution_base_quarterly', 65070.00,
        'payday_super_clearance_business_days', 7,
        'stp_version', 'Phase 2'
     )),

    ('IN', 'India CBIC GST & Statutory Payroll Package', 'IndiaPayrollAdapter', '2026.1', 'CBIC, EPFO, ESIC & Income Tax Dept',
     'INR', '2026-04-01', true, jsonb_build_object(
        'epf_employee_rate', 0.12,
        'epf_employer_rate', 0.12,
        'eps_pension_ceiling_inr', 15000.00,
        'esi_employee_rate', 0.0075,
        'esi_employer_rate', 0.0325,
        'esi_wage_ceiling_inr', 21000.00,
        'new_tax_regime_std_deduction', 75000.00
     )),

    ('NL', 'Netherlands Belastingdienst Payroll Package', 'NetherlandsPayrollAdapter', '2026.1', 'Belastingdienst',
     'EUR', '2026-01-01', true, jsonb_build_object('standard_vat', 0.21, 'reduced_vat', 0.09)),

    ('ES', 'Spain AEAT & Seguridad Social Package', 'SpainPayrollAdapter', '2026.1', 'Agencia Tributaria & Seguridad Social',
     'EUR', '2026-01-01', true, jsonb_build_object('standard_vat', 0.21, 'reduced_vat', 0.10, 'super_reduced_vat', 0.04)),

    ('IT', 'Italy Agenzia delle Entrate & INPS Package', 'ItalyPayrollAdapter', '2026.1', 'Agenzia delle Entrate & INPS',
     'EUR', '2026-01-01', true, jsonb_build_object('standard_vat', 0.22, 'reduced_vat', 0.10, 'sdi_mandated', true)),

    ('PL', 'Poland KAS & ZUS Package', 'PolandPayrollAdapter', '2026.1', 'Krajowa Administracja Skarbowa & ZUS',
     'PLN', '2026-01-01', true, jsonb_build_object('standard_vat', 0.23, 'ksef_mandated', true))
ON CONFLICT (country_code, package_name) DO UPDATE SET
    version = EXCLUDED.version,
    rules_metadata = EXCLUDED.rules_metadata;

-- 5. External Audit-Chain Immutable Checkpoint Ledger
CREATE TABLE IF NOT EXISTS audit.audit_chain_checkpoints (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    checkpoint_sequence BIGSERIAL NOT NULL,
    from_event_id UUID NOT NULL REFERENCES audit.tamper_evident_event_log(event_id),
    to_event_id UUID NOT NULL REFERENCES audit.tamper_evident_event_log(event_id),
    event_count INT NOT NULL CHECK (event_count > 0),
    head_record_hash VARCHAR(64) NOT NULL,
    merkle_root_hash VARCHAR(64) NOT NULL,
    external_storage_provider VARCHAR(50) NOT NULL, -- 's3_object_lock', 'cloudflare_r2_worm', 'rfc3161_tsa', 'azure_immutable_blob'
    external_anchor_uri TEXT NOT NULL,
    external_receipt_signature TEXT NOT NULL,
    anchored_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    verified_at TIMESTAMPTZ,
    status VARCHAR(30) NOT NULL DEFAULT 'ANCHORED'
);

CREATE INDEX IF NOT EXISTS idx_audit_checkpoints_seq ON audit.audit_chain_checkpoints (checkpoint_sequence DESC);
CREATE INDEX IF NOT EXISTS idx_audit_checkpoints_head_hash ON audit.audit_chain_checkpoints (head_record_hash);

-- Enforce append-only immutability on audit checkpoints
CREATE OR REPLACE FUNCTION audit.prevent_checkpoint_modification()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION '[FAIL-CLOSED] audit.audit_chain_checkpoints is strictly append-only. UPDATE and DELETE operations are forbidden.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_audit_checkpoints_immutable ON audit.audit_chain_checkpoints;
CREATE TRIGGER trg_audit_checkpoints_immutable
    BEFORE UPDATE OR DELETE ON audit.audit_chain_checkpoints
    FOR EACH ROW
    EXECUTE FUNCTION audit.prevent_checkpoint_modification();
