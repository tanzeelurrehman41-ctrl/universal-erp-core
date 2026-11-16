-- Universal ERP — Migration 038: SaaS Statutory Tax Classification, Rate Hardening & Place-of-Provision Rules
-- Implements verified SRB standard 15% rate, conditional reduced rates, export zero-rating, and effective-dated schedules.

-- 1. Extend platform.billing_tax_rules with service classification & regime attributes
ALTER TABLE platform.billing_tax_rules
  ALTER COLUMN country_code TYPE VARCHAR(10),
  ADD COLUMN IF NOT EXISTS tariff_code TEXT NOT NULL DEFAULT '9815.6000',
  ADD COLUMN IF NOT EXISTS service_category TEXT NOT NULL DEFAULT 'saas_cloud_erp',
  ADD COLUMN IF NOT EXISTS tax_regime TEXT NOT NULL DEFAULT 'standard_with_input_tax'
    CHECK (tax_regime IN ('standard_with_input_tax', 'reduced_without_input_tax', 'export_zero_rated', 'exempt')),
  ADD COLUMN IF NOT EXISTS customer_type TEXT NOT NULL DEFAULT 'all'
    CHECK (customer_type IN ('all', 'b2b_registered', 'b2c_unregistered', 'export_foreign')),
  ADD COLUMN IF NOT EXISTS statutory_authority TEXT NOT NULL DEFAULT 'FBR_ICT',
  ADD COLUMN IF NOT EXISTS statutory_reference TEXT NOT NULL DEFAULT 'Statutory Schedule',
  ADD COLUMN IF NOT EXISTS eligibility_conditions TEXT,
  ADD COLUMN IF NOT EXISTS input_tax_allowable BOOLEAN NOT NULL DEFAULT true,
  ADD COLUMN IF NOT EXISTS withholding_rate_percentage NUMERIC(6,4) NOT NULL DEFAULT 0.0000;

-- 2. Clean up seed data in platform.billing_tax_rules to install authoritative statutory matrix
DELETE FROM platform.billing_tax_rules WHERE country_code = 'PK' OR country_code = 'ALL';

-- 3. Insert Authoritative Effective-Dated Tax Rules Matrix
INSERT INTO platform.billing_tax_rules (
  country_code, province_state, tariff_code, service_category, tax_name, rate_percentage,
  tax_regime, customer_type, statutory_authority, statutory_reference, eligibility_conditions,
  input_tax_allowable, withholding_rate_percentage, effective_from, effective_until, is_active
) VALUES
  -- ── SINDH (SRB) ─────────────────────────────────────────────────────────────
  -- Current Standard Rate (15.0% under Sindh Finance Act 2024, effective 2024-07-01)
  ('PK', 'SINDH', '9815.6000', 'saas_cloud_erp', 'SRB Sindh Sales Tax on Services (Standard SaaS)', 15.0000,
   'standard_with_input_tax', 'all', 'SRB',
   'Sindh Sales Tax on Services Act, 2011, Sec 8(1) as amended by Sindh Finance Act 2024',
   'Standard statutory rate with full input tax credit and adjustment rights under SRB rules.',
   true, 3.0000, '2024-07-01 00:00:00+00', NULL, true),

  -- Historical Standard Rate (13.0% under SSTS Act 2011, 2016-07-01 to 2024-06-30)
  ('PK', 'SINDH', '9815.6000', 'saas_cloud_erp', 'SRB Sindh Sales Tax on Services (Historical Standard)', 13.0000,
   'standard_with_input_tax', 'all', 'SRB',
   'Sindh Sales Tax on Services Act, 2011, Sec 8(1) (historical interval prior to Sindh Finance Act 2024)',
   'Applicable strictly to historical period recalculation.',
   true, 2.6000, '2016-07-01 00:00:00+00', '2024-06-30 23:59:59+00', false),

  -- Conditional Reduced Rate (3.0% without input tax credit)
  ('PK', 'SINDH', '9815.6000', 'saas_cloud_erp', 'SRB Reduced Rate for IT/Software (No Input Credit)', 3.0000,
   'reduced_without_input_tax', 'all', 'SRB',
   'SRB Notification No. SRB-3-4/7/2013 as amended & Second Schedule Heading 9815.6000',
   'Mandatory conditions: Service provider must formally elect this regime and surrender all input tax credit, adjustments, and refunds. Cannot be automatically assigned.',
   false, 3.0000, '2013-07-01 00:00:00+00', NULL, true),

  -- ── PUNJAB (PRA) ────────────────────────────────────────────────────────────
  -- Standard Rate (16.0% under Punjab Sales Tax on Services Act, 2012)
  ('PK', 'PUNJAB', '9815.6000', 'saas_cloud_erp', 'PRA Punjab Sales Tax on Services (Standard SaaS)', 16.0000,
   'standard_with_input_tax', 'all', 'PRA',
   'Punjab Sales Tax on Services Act, 2012, Second Schedule, Heading 9815.6000',
   'Standard statutory rate with full input tax credit.',
   true, 3.2000, '2012-07-01 00:00:00+00', NULL, true),

  -- Conditional Reduced Rate (5.0% without input tax credit)
  ('PK', 'PUNJAB', '9815.6000', 'saas_cloud_erp', 'PRA Reduced Rate for IT/Software (No Input Credit)', 5.0000,
   'reduced_without_input_tax', 'all', 'PRA',
   'PRA Notification No. SO(TAX)1-1/2020 & Second Schedule Heading 9815.6000',
   'Conditional upon formal election to forgo input tax credit under PRA Special Procedure Rules.',
   false, 5.0000, '2020-07-01 00:00:00+00', NULL, true),

  -- ── FEDERAL / ICT (FBR) ─────────────────────────────────────────────────────
  -- Standard Rate (15.0% under ICT Tax on Services Ordinance, 2001)
  ('PK', 'FEDERAL', '9815.6000', 'saas_cloud_erp', 'ICT Sales Tax on IT/SaaS Services (Standard)', 15.0000,
   'standard_with_input_tax', 'all', 'FBR_ICT',
   'Islamabad Capital Territory (Tax on Services) Ordinance, 2001, Schedule Table, Heading 9815.6000',
   'Standard rate for services rendered or consumed in Islamabad Capital Territory.',
   true, 3.0000, '2023-07-01 00:00:00+00', NULL, true),

  -- Conditional Reduced Rate (5.0% for PSEB-registered IT services without input credit)
  ('PK', 'FEDERAL', '9815.6000', 'saas_cloud_erp', 'ICT Reduced Rate for IT Services (PSEB Certified)', 5.0000,
   'reduced_without_input_tax', 'all', 'FBR_ICT',
   'ICT Ordinance 2001, Table-II & S.R.O. Notifications for IT-enabled services',
   'Requires valid Pakistan Software Export Board (PSEB) registration and surrender of input tax adjustments.',
   false, 5.0000, '2023-07-01 00:00:00+00', NULL, true),

  -- ── KHYBER PAKHTUNKHWA (KPRA) ───────────────────────────────────────────────
  -- Standard Rate (15.0% under KP Sales Tax on Services Act, 2022)
  ('PK', 'KP', '9815.6000', 'saas_cloud_erp', 'KPRA Khyber Pakhtunkhwa Sales Tax on Services (Standard)', 15.0000,
   'standard_with_input_tax', 'all', 'KPRA',
   'Khyber Pakhtunkhwa Sales Tax on Services Act, 2022, Second Schedule, Heading 9815.6000',
   'Standard rate with input tax credit.',
   true, 3.0000, '2022-07-01 00:00:00+00', NULL, true),

  -- Conditional Reduced Rate (2.0% without input credit)
  ('PK', 'KP', '9815.6000', 'saas_cloud_erp', 'KPRA Reduced Rate for IT/Software (No Input Credit)', 2.0000,
   'reduced_without_input_tax', 'all', 'KPRA',
   'KPRA Act 2022, Second Schedule, Heading 9815.6000 Reduced Column',
   'Conditional on no input tax credit claimed.',
   false, 2.0000, '2022-07-01 00:00:00+00', NULL, true),

  -- ── BALOCHISTAN (BRA) ───────────────────────────────────────────────────────
  -- Standard Rate (15.0% under Balochistan Sales Tax on Services Act, 2015)
  ('PK', 'BALOCHISTAN', '9815.6000', 'saas_cloud_erp', 'BRA Balochistan Sales Tax on Services (Standard)', 15.0000,
   'standard_with_input_tax', 'all', 'BRA',
   'Balochistan Sales Tax on Services Act, 2015, Second Schedule, Heading 9815.6000',
   'Standard rate with input tax credit.',
   true, 3.0000, '2015-07-01 00:00:00+00', NULL, true),

  -- Conditional Reduced Rate (3.0% without input credit)
  ('PK', 'BALOCHISTAN', '9815.6000', 'saas_cloud_erp', 'BRA Reduced Rate for IT/Software (No Input Credit)', 3.0000,
   'reduced_without_input_tax', 'all', 'BRA',
   'BRA Act 2015, Second Schedule, Heading 9815.6000',
   'Conditional on no input tax credit claimed.',
   false, 3.0000, '2015-07-01 00:00:00+00', NULL, true),

  -- ── EXPORT OF SERVICES (ZERO-RATED / EXEMPT) ────────────────────────────────
  ('ALL', 'EXPORT', '9815.6000', 'saas_cloud_erp', 'Export of IT/SaaS Services (Zero-Rated / Exempt)', 0.0000,
   'export_zero_rated', 'export_foreign', 'FBR_PROVINCIAL_HARMONIZED',
   'SRB / PRA / KPRA / FBR Place of Provision of Services Rules (Export of Services)',
   'Recipient located outside Pakistan, benefit consumed outside Pakistan, and proceeds received via banking channel in convertible foreign exchange (SBP e-PRC).',
   true, 0.0000, '2020-01-01 00:00:00+00', NULL, true);

-- 4. Create index for fast tariff & classification resolution
CREATE INDEX IF NOT EXISTS ix_billing_tax_rules_tariff 
  ON platform.billing_tax_rules(country_code, province_state, tariff_code, tax_regime, is_active);
