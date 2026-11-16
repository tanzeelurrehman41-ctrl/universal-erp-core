-- Universal ERP — Migration 041: Global Localization Hardening & Roadmap Foundation
-- Expands ISO 4217 currencies, adds Canada, Australia, India, and EU member states to platform.countries,
-- updates US 2026 OASDI ($184,500) and UK 2026-27 Employer NIC (15%, £417 secondary threshold) metadata,
-- adds destination OSS and non-resident digital services tax rules, and records statutory jurisdiction coverage.

-- 1. Additional ISO 4217 Currencies
INSERT INTO finance.currencies (code, name, minor_unit, is_active)
VALUES
    ('CAD', 'Canadian Dollar', 2, true),
    ('AUD', 'Australian Dollar', 2, true),
    ('INR', 'Indian Rupee', 2, true),
    ('JPY', 'Japanese Yen', 0, true),
    ('BHD', 'Bahraini Dinar', 3, true),
    ('KWD', 'Kuwaiti Dinar', 3, true),
    ('OMR', 'Omani Rial', 3, true),
    ('CHF', 'Swiss Franc', 2, true),
    ('SGD', 'Singapore Dollar', 2, true)
ON CONFLICT (code) DO NOTHING;

-- 2. Expand Platform Countries (Canada, Australia, India, and EU Member States)
INSERT INTO platform.countries (
    country_code, alpha3_code, numeric_code, country_name, default_currency_code,
    default_timezone, default_locale, fiscal_year_start_month, fiscal_year_start_day,
    address_format_type, tax_system_type, einvoicing_mandate_type, is_supported
) VALUES
    ('CA', 'CAN', 124, 'Canada', 'CAD', 'America/Toronto', 'en-CA', 1, 1, 'us_state_zip', 'gst', 'none', true),
    ('AU', 'AUS', 036, 'Australia', 'AUD', 'Australia/Sydney', 'en-AU', 7, 1, 'uk_postcode', 'gst', 'peppol_bis3', true),
    ('IN', 'IND', 356, 'India', 'INR', 'Asia/Kolkata', 'en-IN', 4, 1, 'standard', 'gst', 'none', true),
    ('NL', 'NLD', 528, 'Netherlands', 'EUR', 'Europe/Amsterdam', 'nl-NL', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('IT', 'ITA', 380, 'Italy', 'EUR', 'Europe/Rome', 'it-IT', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('ES', 'ESP', 724, 'Spain', 'EUR', 'Europe/Madrid', 'es-ES', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('BE', 'BEL', 056, 'Belgium', 'EUR', 'Europe/Brussels', 'nl-BE', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('AT', 'AUT', 040, 'Austria', 'EUR', 'Europe/Vienna', 'de-AT', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('SE', 'SWE', 752, 'Sweden', 'EUR', 'Europe/Stockholm', 'sv-SE', 1, 1, 'standard', 'vat', 'peppol_bis3', true),
    ('PL', 'POL', 616, 'Poland', 'EUR', 'Europe/Warsaw', 'pl-PL', 1, 1, 'standard', 'vat', 'peppol_bis3', true)
ON CONFLICT (country_code) DO UPDATE SET
    country_name = EXCLUDED.country_name,
    default_currency_code = EXCLUDED.default_currency_code,
    default_timezone = EXCLUDED.default_timezone,
    tax_system_type = EXCLUDED.tax_system_type;

-- 3. Additional Sub-National Jurisdictions
INSERT INTO platform.jurisdictions (country_code, jurisdiction_code, name, jurisdiction_type, standard_tax_rate, reduced_tax_rate, statutory_authority)
VALUES
    -- UK Regions
    ('GB', 'SCOTLAND', 'Scotland (HMRC / Scottish Parliament)', 'country', 20.0000, 5.0000, 'Scottish Income Tax & HMRC VAT'),
    ('GB', 'WALES', 'Wales (HMRC / Welsh Government)', 'country', 20.0000, 5.0000, 'Welsh Rates of Income Tax & HMRC VAT'),
    ('GB', 'NI', 'Northern Ireland (HMRC)', 'country', 20.0000, 5.0000, 'HMRC VAT Act 1994'),

    -- Canadian Provinces
    ('CA', 'ON', 'Ontario (HST 13%)', 'province', 13.0000, NULL, 'Canada Revenue Agency (CRA) / Excise Tax Act'),
    ('CA', 'BC', 'British Columbia (GST 5% + PST 7%)', 'province', 12.0000, NULL, 'CRA / BC Ministry of Finance'),
    ('CA', 'QC', 'Quebec (GST 5% + QST 9.975%)', 'province', 14.9750, NULL, 'Revenu Québec / CRA'),
    ('CA', 'AB', 'Alberta (GST 5% only)', 'province', 5.0000, NULL, 'Canada Revenue Agency (CRA)'),

    -- Australian States
    ('AU', 'NSW', 'New South Wales (ATO GST)', 'state', 10.0000, NULL, 'Australian Taxation Office (ATO)'),
    ('AU', 'VIC', 'Victoria (ATO GST)', 'state', 10.0000, NULL, 'Australian Taxation Office (ATO)'),
    ('AU', 'QLD', 'Queensland (ATO GST)', 'state', 10.0000, NULL, 'Australian Taxation Office (ATO)'),

    -- Indian States
    ('IN', 'MH', 'Maharashtra (GST 18%)', 'state', 18.0000, 5.0000, 'Central Board of Indirect Taxes and Customs (CBIC)'),
    ('IN', 'KA', 'Karnataka (GST 18%)', 'state', 18.0000, 5.0000, 'Central Board of Indirect Taxes and Customs (CBIC)'),
    ('IN', 'DL', 'Delhi (GST 18%)', 'state', 18.0000, 5.0000, 'Central Board of Indirect Taxes and Customs (CBIC)')
ON CONFLICT (country_code, jurisdiction_code) DO NOTHING;

-- 4. Additional Global Billing Tax Rules (OSS, UK Overseas B2C, AU B2C, CA B2C)
INSERT INTO platform.billing_tax_rules (
    country_code, province_state, tax_name, rate_percentage, customer_type,
    tax_regime, tariff_code, statutory_authority, statutory_reference, input_tax_allowable, is_active
) VALUES
    ('GB', 'UK', 'UK Overseas Vendor Digital Services VAT (B2C)', 20.0000, 'b2c',
     'standard_with_input_tax', 'CPA-62.01', 'HMRC', 'VATA 1994 Section 2(1) & Schedule 4A Overseas Vendor Rules', true, true),

    ('AU', 'AU', 'Australia Inbound Digital Services GST (B2C)', 10.0000, 'b2c',
     'standard_with_input_tax', 'DIGITAL-SVC', 'ATO', 'A New Tax System (Goods and Services Tax) Act 1999 Subdiv 84-B', true, true),

    ('CA', 'ON', 'Canada Non-Resident Digital Services GST (B2C)', 13.0000, 'b2c',
     'standard_with_input_tax', 'DIGITAL-SVC', 'CRA', 'Excise Tax Act (Canada) Part IX Simplified Non-Resident Digital Regime', true, true),

    ('DE', 'DE', 'EU One Stop Shop Destination VAT - Germany (B2C)', 19.0000, 'b2c',
     'standard_with_input_tax', 'CPA-62.01', 'EU_REVENUE_DE', 'EU VAT Directive 2006/112/EC Art 58 OSS Destination Principle', true, true),

    ('FR', 'FR', 'EU One Stop Shop Destination VAT - France (B2C)', 20.0000, 'b2c',
     'standard_with_input_tax', 'CPA-62.01', 'EU_REVENUE_FR', 'EU VAT Directive 2006/112/EC Art 58 OSS Destination Principle', true, true)
ON CONFLICT DO NOTHING;

-- 5. Update Payroll Country Packages Metadata (2026 US OASDI $184,500, UK 2026-27 15% ER NIC)
UPDATE hr.payroll_country_packages
SET rules_metadata = jsonb_build_object(
    'social_security_wage_base_2026', 184500.00,
    'social_security_rate_ee', 0.062,
    'social_security_rate_er', 0.062,
    'medicare_rate_ee', 0.0145,
    'medicare_rate_er', 0.0145,
    'additional_medicare_rate', 0.009,
    'additional_medicare_threshold_single', 200000.00,
    'additional_medicare_threshold_mfj', 250000.00,
    'standard_deduction_single_2026', 15700.00,
    'standard_deduction_mfj_2026', 31400.00,
    'futa_tax_rate_effective', 0.006,
    'futa_annual_wage_base', 7000.00
),
version = '2026.1.0'
WHERE country_code = 'US';

UPDATE hr.payroll_country_packages
SET rules_metadata = jsonb_build_object(
    'employer_nic_rate_2026_27', 0.150,
    'secondary_threshold_monthly', 417.00,
    'secondary_threshold_annual', 5000.00,
    'primary_threshold_monthly', 1048.00,
    'upper_earnings_limit_monthly', 4189.00,
    'employee_nic_main_rate', 0.080,
    'employee_nic_higher_rate', 0.020,
    'personal_allowance_annual', 12570.00,
    'scottish_tax_schedule_supported', true,
    'student_loan_plans_supported', true,
    'pension_auto_enrolment_qualifying_min', 520.00
),
version = '2026.2.0'
WHERE country_code = 'GB';

-- 6. Add Architecture-Only Payroll Packages (Inactive / Roadmap Only)
INSERT INTO hr.payroll_country_packages (
    country_code, package_name, adapter_class_name, version,
    statutory_authority, currency_code, effective_from, is_active, rules_metadata
) VALUES
    ('CA', 'Canada Statutory Payroll (Roadmap)', 'CanadaPayrollAdapter', '0.1.0-draft',
     'Canada Revenue Agency (CRA)', 'CAD', '2026-01-01', false,
     jsonb_build_object('status', 'architecture_configured_only', 'cpp_max_pensionable_earnings', 73200.00)),
    ('AU', 'Australia Superannuation & PAYG (Roadmap)', 'AustraliaPayrollAdapter', '0.1.0-draft',
     'Australian Taxation Office (ATO)', 'AUD', '2026-07-01', false,
     jsonb_build_object('status', 'architecture_configured_only', 'superannuation_guarantee_rate', 0.12)),
    ('IN', 'India PF, ESI & TDS (Roadmap)', 'IndiaPayrollAdapter', '0.1.0-draft',
     'Income Tax Department / EPFO', 'INR', '2026-04-01', false,
     jsonb_build_object('status', 'architecture_configured_only', 'epf_rate', 0.12))
ON CONFLICT (country_code, package_name) DO NOTHING;
