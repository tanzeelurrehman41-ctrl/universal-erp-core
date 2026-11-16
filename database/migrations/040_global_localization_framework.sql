-- Universal ERP — Migration 040: Global Localization Framework & Multi-Country Infrastructure
-- Establishes Layer B Country Localization Packages and Layer C Company Configurations.
-- Provides ISO 3166-1 country registry, sub-national jurisdictions, multi-country tax rules,
-- and pluggable payroll country package adapters (US, UK, EU, UAE, KSA, Pakistan, Global).

-- 1. ISO 3166-1 Country Registry
CREATE TABLE IF NOT EXISTS platform.countries (
    country_code CHARACTER(2) PRIMARY KEY,              -- ISO 3166-1 alpha-2 (e.g. 'US', 'GB', 'PK', 'AE', 'SA', 'DE')
    alpha3_code CHARACTER(3) NOT NULL UNIQUE,          -- ISO 3166-1 alpha-3 (e.g. 'USA', 'GBR', 'PAK', 'ARE', 'SAU', 'DEU')
    numeric_code INTEGER NOT NULL UNIQUE,               -- ISO 3166-1 numeric (e.g. 840, 826, 586, 784, 682, 276)
    country_name TEXT NOT NULL,
    default_currency_code CHARACTER(3) NOT NULL REFERENCES finance.currencies(code),
    default_timezone TEXT NOT NULL,
    default_locale TEXT NOT NULL DEFAULT 'en-US',
    fiscal_year_start_month SMALLINT NOT NULL DEFAULT 1 CHECK (fiscal_year_start_month BETWEEN 1 AND 12),
    fiscal_year_start_day SMALLINT NOT NULL DEFAULT 1 CHECK (fiscal_year_start_day BETWEEN 1 AND 31),
    address_format_type TEXT NOT NULL DEFAULT 'standard', -- 'us_state_zip', 'uk_postcode', 'pk_province_district', 'uae_emirate', 'standard'
    tax_system_type TEXT NOT NULL DEFAULT 'vat',          -- 'sales_tax', 'vat', 'gst', 'hybrid', 'none'
    einvoicing_mandate_type TEXT NOT NULL DEFAULT 'none', -- 'peppol_bis3', 'zatca_ubl21', 'fbr_pos', 'none'
    is_supported BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Seed authoritatively verified country profiles
INSERT INTO platform.countries (
    country_code, alpha3_code, numeric_code, country_name, default_currency_code,
    default_timezone, default_locale, fiscal_year_start_month, fiscal_year_start_day,
    address_format_type, tax_system_type, einvoicing_mandate_type, is_supported
) VALUES
    ('US', 'USA', 840, 'United States', 'USD', 'America/New_York', 'en-US', 1, 1, 'us_state_zip', 'sales_tax', 'none', true),
    ('GB', 'GBR', 826, 'United Kingdom', 'GBP', 'Europe/London', 'en-GB', 4, 6, 'uk_postcode', 'vat', 'none', true),
    ('DE', 'DEU', 276, 'Germany', 'EUR', 'Europe/Berlin', 'de-DE', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('FR', 'FRA', 250, 'France', 'EUR', 'Europe/Paris', 'fr-FR', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('IE', 'IRL', 372, 'Ireland', 'EUR', 'Europe/Dublin', 'en-IE', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('AE', 'ARE', 784, 'United Arab Emirates', 'AED', 'Asia/Dubai', 'ar-AE', 1, 1, 'uae_emirate', 'vat', 'none', true),
    ('SA', 'SAU', 682, 'Saudi Arabia', 'SAR', 'Asia/Riyadh', 'ar-SA', 1, 1, 'standard', 'vat', 'zatca_ubl21', true),
    ('PK', 'PAK', 586, 'Pakistan', 'PKR', 'Asia/Karachi', 'en-PK', 7, 1, 'pk_province_district', 'sales_tax', 'fbr_pos', true)
ON CONFLICT (country_code) DO UPDATE SET
    country_name = EXCLUDED.country_name,
    default_currency_code = EXCLUDED.default_currency_code,
    default_timezone = EXCLUDED.default_timezone,
    tax_system_type = EXCLUDED.tax_system_type;

-- 2. Sub-National Tax & Administrative Jurisdictions
CREATE TABLE IF NOT EXISTS platform.jurisdictions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    country_code CHARACTER(2) NOT NULL REFERENCES platform.countries(country_code) ON DELETE CASCADE,
    jurisdiction_code TEXT NOT NULL,
    name TEXT NOT NULL,
    jurisdiction_type TEXT NOT NULL, -- 'state', 'province', 'emirate', 'federal', 'territory'
    standard_tax_rate NUMERIC(8,4) NOT NULL DEFAULT 0.0000,
    reduced_tax_rate NUMERIC(8,4),
    statutory_authority TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_country_jurisdiction UNIQUE (country_code, jurisdiction_code)
);

INSERT INTO platform.jurisdictions (country_code, jurisdiction_code, name, jurisdiction_type, standard_tax_rate, reduced_tax_rate, statutory_authority)
VALUES
    -- United States (State Sales Tax)
    ('US', 'NY', 'New York', 'state', 8.8750, 0.0000, 'NYS Department of Taxation and Finance'),
    ('US', 'TX', 'Texas', 'state', 6.6000, 0.0000, 'Texas Comptroller of Public Accounts'),
    ('US', 'CA', 'California', 'state', 0.0000, 0.0000, 'California Department of Tax and Fee Administration (CDTFA)'),
    ('US', 'WA', 'Washington', 'state', 6.5000, 0.0000, 'Washington Department of Revenue'),
    -- United Kingdom (HMRC)
    ('GB', 'UK_ALL', 'United Kingdom', 'federal', 20.0000, 5.0000, 'HM Revenue and Customs (HMRC)'),
    -- European Union (Sample Member States)
    ('DE', 'DE_ALL', 'Germany', 'federal', 19.0000, 7.0000, 'Bundeszentralamt für Steuern (BZSt)'),
    ('FR', 'FR_ALL', 'France', 'federal', 20.0000, 10.0000, 'Direction Générale des Finances Publiques (DGFiP)'),
    ('IE', 'IE_ALL', 'Ireland', 'federal', 23.0000, 13.5000, 'Office of the Revenue Commissioners'),
    -- United Arab Emirates (FTA)
    ('AE', 'AE_ALL', 'United Arab Emirates', 'federal', 5.0000, 0.0000, 'Federal Tax Authority (FTA)'),
    -- Saudi Arabia (ZATCA)
    ('SA', 'SA_ALL', 'Saudi Arabia', 'federal', 15.0000, 0.0000, 'Zakat, Tax and Customs Authority (ZATCA)'),
    -- Pakistan (Provincial Revenue Authorities)
    ('PK', 'SINDH', 'Sindh', 'province', 15.0000, 3.0000, 'Sindh Revenue Board (SRB)'),
    ('PK', 'PUNJAB', 'Punjab', 'province', 16.0000, 5.0000, 'Punjab Revenue Authority (PRA)'),
    ('PK', 'FEDERAL', 'Federal / ICT', 'federal', 15.0000, 5.0000, 'Federal Board of Revenue (FBR)'),
    ('PK', 'KP', 'Khyber Pakhtunkhwa', 'province', 15.0000, 2.0000, 'KP Revenue Authority (KPRA)'),
    ('PK', 'BALOCHISTAN', 'Balochistan', 'province', 15.0000, 6.0000, 'Balochistan Revenue Authority (BRA)')
ON CONFLICT (country_code, jurisdiction_code) DO NOTHING;

-- 3. Pluggable Payroll Country Packages
CREATE TABLE IF NOT EXISTS hr.payroll_country_packages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    country_code CHARACTER(2) NOT NULL REFERENCES platform.countries(country_code) ON DELETE CASCADE,
    package_name TEXT NOT NULL,
    adapter_class_name TEXT NOT NULL,
    version TEXT NOT NULL DEFAULT '1.0.0',
    statutory_authority TEXT NOT NULL,
    currency_code CHARACTER(3) NOT NULL REFERENCES finance.currencies(code),
    effective_from DATE NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT true,
    rules_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_country_payroll_pkg UNIQUE (country_code, package_name)
);

INSERT INTO hr.payroll_country_packages (country_code, package_name, adapter_class_name, version, statutory_authority, currency_code, effective_from, rules_metadata)
VALUES
    ('US', 'United States Payroll Package', 'UnitedStatesPayrollAdapter', '2026.1', 'IRS / SSA / DOL', 'USD', '2026-01-01', 
     '{"social_security_ee_rate": 0.062, "social_security_er_rate": 0.062, "social_security_wage_base": 168600, "medicare_ee_rate": 0.0145, "medicare_er_rate": 0.0145, "additional_medicare_rate": 0.009, "federal_minimum_wage_hourly": 7.25}'::jsonb),
    ('GB', 'United Kingdom Payroll Package', 'UnitedKingdomPayrollAdapter', '2026.1', 'HM Revenue and Customs (HMRC)', 'GBP', '2026-04-06',
     '{"personal_allowance": 12570, "basic_rate": 0.20, "higher_rate": 0.40, "additional_rate": 0.45, "nic_ee_main_rate": 0.08, "nic_ee_higher_rate": 0.02, "nic_er_rate": 0.138, "living_wage_hourly": 11.44}'::jsonb),
    ('AE', 'UAE Labour and Pension Package', 'UnitedArabEmiratesPayrollAdapter', '2026.1', 'Ministry of Human Resources & GPSSA', 'AED', '2026-01-01',
     '{"personal_income_tax_rate": 0.00, "gpssa_ee_rate": 0.05, "gpssa_er_rate": 0.125, "eosg_days_tier1": 21, "eosg_days_tier2": 30}'::jsonb),
    ('PK', 'Pakistan Statutory Payroll Package', 'PakistanPayrollAdapter', '2026.1', 'FBR / EOBI / Provincial Labour', 'PKR', '2026-07-01',
     '{"eobi_base": 37000, "eobi_ee_rate": 0.01, "eobi_er_rate": 0.05, "social_security_er_rate": 0.06, "min_wage_federal": 40700, "min_wage_sindh": 43000, "min_wage_punjab": 40000, "min_wage_kp": 40000, "min_wage_balochistan": 37000}'::jsonb)
ON CONFLICT (country_code, package_name) DO NOTHING;

-- 4. Adapt Billing Tax Rules Check Constraints for Global Support
ALTER TABLE platform.billing_tax_rules
    DROP CONSTRAINT IF EXISTS billing_tax_rules_customer_type_check,
    ADD CONSTRAINT billing_tax_rules_customer_type_check
    CHECK (customer_type = ANY (ARRAY['all'::text, 'b2b'::text, 'b2b_registered'::text, 'b2c'::text, 'b2c_unregistered'::text, 'export_foreign'::text]));

ALTER TABLE platform.billing_tax_rules
    DROP CONSTRAINT IF EXISTS billing_tax_rules_tax_regime_check,
    ADD CONSTRAINT billing_tax_rules_tax_regime_check
    CHECK (tax_regime = ANY (ARRAY['standard_with_input_tax'::text, 'reduced_without_input_tax'::text, 'export_zero_rated'::text, 'reverse_charge'::text, 'exempt'::text]));

-- 5. Seed Authoritative Global SaaS Tax Rules in platform.billing_tax_rules
INSERT INTO platform.billing_tax_rules (
    country_code, province_state, tariff_code, service_category, tax_name, rate_percentage,
    tax_regime, customer_type, statutory_authority, statutory_reference, eligibility_conditions,
    input_tax_allowable, withholding_rate_percentage, effective_from, effective_until, is_active
) VALUES
    -- United States: New York State & Local Sales Tax (SaaS Taxable at 8.875% combined)
    ('US', 'NY', 'NAICS-518210', 'saas_cloud_erp', 'NYS State and Local Sales Tax on SaaS', 8.8750,
     'standard_with_input_tax', 'all', 'NYS_TAX',
     'NY Tax Law § 1105(c)(9) and TSB-M-08(7)S (Prewritten Software / SaaS)',
     'Taxable in New York State and NYC when accessed by users located in NY jurisdiction. Exempt if valid Resale Certificate ST-120 provided.',
     true, 0.0000, '2024-01-01 00:00:00+00', NULL, true),

    -- United States: Texas Sales Tax (SaaS 80% Taxable Base -> effective 6.6%)
    ('US', 'TX', 'NAICS-518210', 'saas_cloud_erp', 'Texas Sales Tax on SaaS (80% Taxable Base)', 6.6000,
     'standard_with_input_tax', 'all', 'TX_COMPTROLLER',
     'Texas Tax Code § 151.0101(a)(12) and 34 TAC Rule 3.342 (Data Processing Services)',
     'Taxable on 80% of value for Texas customers. 20% statutory exemption applies automatically to data processing/SaaS.',
     true, 0.0000, '2024-01-01 00:00:00+00', NULL, true),

    -- United States: California (Pure SaaS Exempt from Sales Tax)
    ('US', 'CA', 'NAICS-518210', 'saas_cloud_erp', 'California Sales Tax on Pure SaaS (Exempt)', 0.0000,
     'exempt', 'all', 'CA_CDTFA',
     'CDTFA Regulation 1502 (Computers, Programs, and Data Processing) - Pure SaaS without tangible storage media transfer',
     'Exempt from CA sales and use tax provided no tangible personal property or physical media is transferred to the customer.',
     true, 0.0000, '2020-01-01 00:00:00+00', NULL, true),

    -- United States: Washington State (Digital Automated Services 6.5%)
    ('US', 'WA', 'NAICS-518210', 'saas_cloud_erp', 'Washington Digital Automated Services Sales Tax', 6.5000,
     'standard_with_input_tax', 'all', 'WA_DOR',
     'RCW 82.04.192 and WAC 458-20-15503 (Digital Automated Services)',
     'Standard state rate for automated cloud services consumed by Washington State residents/businesses.',
     true, 0.0000, '2024-01-01 00:00:00+00', NULL, true),

    -- United Kingdom: HMRC Standard Rate VAT on Electronic Services (20.0%)
    ('GB', 'ALL', 'CPA-62.01', 'saas_cloud_erp', 'UK Value Added Tax (Standard Rate)', 20.0000,
     'standard_with_input_tax', 'all', 'HMRC',
     'Value Added Tax Act 1994, Section 2(1) and Schedule 4A (Electronically Supplied Services)',
     'Standard rate for UK domestic SaaS subscriptions. Non-UK cross-border B2B supplies qualify for reverse-charge (0%).',
     true, 0.0000, '2020-01-01 00:00:00+00', NULL, true),

    -- United Kingdom: B2B Reverse Charge (0.0%)
    ('GB', 'OVERSEAS_B2B', 'CPA-62.01', 'saas_cloud_erp', 'UK VAT Cross-Border B2B Reverse Charge', 0.0000,
     'reverse_charge', 'b2b', 'HMRC',
     'VAT Act 1994, Section 55A & Place of Supply of Services Rules (B2B General Rule)',
     'Applies strictly to non-UK business customers who self-account for VAT under reverse charge.',
     true, 0.0000, '2020-01-01 00:00:00+00', NULL, true),

    -- European Union (Germany): B2C OSS VAT (19.0%)
    ('DE', 'ALL', 'CPA-62.01', 'saas_cloud_erp', 'German Mehrwertsteuer (MwSt - SaaS)', 19.0000,
     'standard_with_input_tax', 'b2c', 'BZSt',
     'Umsatzsteuergesetz (UStG) § 1 Abs. 1 Nr. 1 and EU VAT Directive 2006/112/EC (OSS Rules)',
     'Applies to German B2C customers under One Stop Shop (OSS) destination principle.',
     true, 0.0000, '2021-07-01 00:00:00+00', NULL, true),

    -- European Union (France): B2C OSS VAT (20.0%)
    ('FR', 'ALL', 'CPA-62.01', 'saas_cloud_erp', 'French Taxe sur la Valeur Ajoutée (TVA - SaaS)', 20.0000,
     'standard_with_input_tax', 'b2c', 'DGFiP',
     'Code Général des Impôts (CGI) Art. 256 and EU VAT Directive 2006/112/EC',
     'Applies to French B2C customers under One Stop Shop (OSS) destination principle.',
     true, 0.0000, '2021-07-01 00:00:00+00', NULL, true),

    -- European Union: Intra-Community B2B Reverse Charge (0.0%)
    ('EU', 'REVERSE_CHARGE', 'CPA-62.01', 'saas_cloud_erp', 'EU Intra-Community B2B Reverse Charge', 0.0000,
     'reverse_charge', 'b2b', 'EU_COMMISSION',
     'Council Directive 2006/112/EC, Article 196 (Reverse Charge Mechanism)',
     'Mandatory condition: Customer must provide a valid, VIES-verified EU VAT Identification Number. Customer self-assesses VAT.',
     true, 0.0000, '2020-01-01 00:00:00+00', NULL, true),

    -- United Arab Emirates: FTA Standard Rate VAT (5.0%)
    ('AE', 'ALL', 'UNSPSC-43231500', 'saas_cloud_erp', 'UAE Value Added Tax (Standard Rate)', 5.0000,
     'standard_with_input_tax', 'all', 'FTA',
     'Federal Decree-Law No. (8) of 2017 on Value Added Tax & Cabinet Decision No. 52/2017',
     'Standard 5% statutory rate for electronic services consumed in the United Arab Emirates.',
     true, 0.0000, '2018-01-01 00:00:00+00', NULL, true),

    -- Saudi Arabia: ZATCA Standard Rate VAT (15.0%)
    ('SA', 'ALL', 'UNSPSC-43231500', 'saas_cloud_erp', 'Saudi Arabia Value Added Tax (Standard Rate)', 15.0000,
     'standard_with_input_tax', 'all', 'ZATCA',
     'VAT Law Royal Decree No. (M/113) as amended by Royal Decree No. (A/638)',
     'Standard 15% rate for electronic services consumed in KSA with ZATCA Phase 2 E-Invoicing metadata.',
     true, 0.0000, '2020-07-01 00:00:00+00', NULL, true)
ON CONFLICT DO NOTHING;

-- 6. Decouple Hardcoded Database Schema Defaults
-- Allow international tenants and branches to specify their own country and currency without falling back to PK/PKR.
ALTER TABLE platform.tenants
    ALTER COLUMN default_country_code DROP DEFAULT,
    ALTER COLUMN default_currency_code DROP DEFAULT,
    ALTER COLUMN default_timezone DROP DEFAULT;

ALTER TABLE organization.branches
    ALTER COLUMN country_code DROP DEFAULT;
