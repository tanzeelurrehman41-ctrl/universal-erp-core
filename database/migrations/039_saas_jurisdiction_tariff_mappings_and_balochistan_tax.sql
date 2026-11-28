-- Universal ERP — Migration 039: Jurisdiction-Specific Tariff Mappings & Balochistan IT Tax Hardening
-- Corrects Balochistan IT tariff heading to 9868.0000 and reduced rate to 6% (amended by Balochistan Finance Act 2026).
-- Corrects KP digital/IT service tariff heading to 9850.0000 under KP Finance Act 2026.
-- Enforces explicit reduced-rate eligibility and non-automatic application rules.

-- 1. Update Balochistan (BRA) Statutory Tax Rules
DELETE FROM platform.billing_tax_rules WHERE province_state = 'BALOCHISTAN';

INSERT INTO platform.billing_tax_rules (
  country_code, province_state, tariff_code, service_category, tax_name, rate_percentage,
  tax_regime, customer_type, statutory_authority, statutory_reference, eligibility_conditions,
  input_tax_allowable, withholding_rate_percentage, effective_from, effective_until, is_active
) VALUES
  -- Modern Balochistan Standard Rate (15.0% under Heading 9868.0000)
  ('PK', 'BALOCHISTAN', '9868.0000', 'saas_cloud_erp', 'BRA Balochistan Sales Tax on Services (Standard SaaS)', 15.0000,
   'standard_with_input_tax', 'all', 'BRA',
   'Balochistan Sales Tax on Services Act, 2015, Second Schedule, Heading 9868.0000 as amended by Balochistan Finance Act 2026',
   'Standard statutory rate with full input tax credit.',
   true, 3.0000, '2026-07-01 00:00:00+00', NULL, true),

  -- Modern Balochistan Reduced Rate (6.0% without input credit under Heading 9868.0000)
  ('PK', 'BALOCHISTAN', '9868.0000', 'saas_cloud_erp', 'BRA Reduced Rate for IT/Software (No Input Credit)', 6.0000,
   'reduced_without_input_tax', 'all', 'BRA',
   'Balochistan Finance Act 2026 & Second Schedule Heading 9868.0000 Reduced Rate Column',
   'Applies strictly to qualifying IT/software services under tariff heading 9868.0000 where provider formally elects no input tax adjustment. Input tax credit is strictly barred.',
   false, 6.0000, '2026-07-01 00:00:00+00', NULL, true),

  -- Historical Balochistan Reduced Rate (3.0% pre-Finance Act 2026)
  ('PK', 'BALOCHISTAN', '9868.0000', 'saas_cloud_erp', 'BRA Historical Reduced Rate for IT/Software (Pre-2026)', 3.0000,
   'reduced_without_input_tax', 'all', 'BRA',
   'Balochistan Sales Tax on Services Act, 2015, Second Schedule (historical interval prior to Finance Act 2026)',
   'Applicable strictly to historical period recalculations prior to July 1, 2026.',
   false, 3.0000, '2015-07-01 00:00:00+00', '2026-06-30 23:59:59+00', false);

-- 2. Update Khyber Pakhtunkhwa (KPRA) Statutory Tax Rules
DELETE FROM platform.billing_tax_rules WHERE province_state = 'KP';

INSERT INTO platform.billing_tax_rules (
  country_code, province_state, tariff_code, service_category, tax_name, rate_percentage,
  tax_regime, customer_type, statutory_authority, statutory_reference, eligibility_conditions,
  input_tax_allowable, withholding_rate_percentage, effective_from, effective_until, is_active
) VALUES
  -- Standard KP Rate (15.0% under Heading 9850.0000 for Digital/IT-based Services)
  ('PK', 'KP', '9850.0000', 'saas_cloud_erp', 'KPRA Khyber Pakhtunkhwa Sales Tax on Services (Standard)', 15.0000,
   'standard_with_input_tax', 'all', 'KPRA',
   'Khyber Pakhtunkhwa Sales Tax on Services Act, 2022, Second Schedule, Heading 9850.0000 as amended by KP Finance Act 2026',
   'Standard statutory rate with full input tax credit.',
   true, 3.0000, '2022-07-01 00:00:00+00', NULL, true),

  -- Reduced KP Rate (2.0% under Heading 9850.0000 without input credit)
  ('PK', 'KP', '9850.0000', 'saas_cloud_erp', 'KPRA Reduced Rate for IT/Software (No Input Credit)', 2.0000,
   'reduced_without_input_tax', 'all', 'KPRA',
   'KP Finance Act 2026 & Second Schedule Heading 9850.0000 Reduced Column',
   'Applies strictly to qualifying digital and IT-based services under tariff heading 9850.0000 without input tax credit. Not automatically assigned.',
   false, 2.0000, '2022-07-01 00:00:00+00', NULL, true);

-- 3. Update Federal / ICT Eligibility Descriptions
UPDATE platform.billing_tax_rules
SET eligibility_conditions = 'Applies strictly to qualifying software/IT development consultants and PSEB-registered entities who forgo input tax credit. Does NOT apply automatically to general SaaS subscriptions.'
WHERE province_state = 'FEDERAL' AND tax_regime = 'reduced_without_input_tax';

-- 4. Update Sindh Eligibility Descriptions
UPDATE platform.billing_tax_rules
SET eligibility_conditions = 'Requires formal election on monthly tax return and complete surrender of input tax adjustment and refund rights under SRB Special Procedure Rules. Does NOT apply automatically to every SaaS subscription.'
WHERE province_state = 'SINDH' AND tax_regime = 'reduced_without_input_tax';
