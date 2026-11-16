-- Universal ERP — Rollback Migration 039: Jurisdiction-Specific Tariff Mappings & Balochistan IT Tax Hardening

-- Restore Migration 038 baseline for Balochistan
DELETE FROM platform.billing_tax_rules WHERE province_state = 'BALOCHISTAN';

INSERT INTO platform.billing_tax_rules (
  country_code, province_state, tariff_code, service_category, tax_name, rate_percentage,
  tax_regime, customer_type, statutory_authority, statutory_reference, eligibility_conditions,
  input_tax_allowable, withholding_rate_percentage, effective_from, effective_until, is_active
) VALUES
  ('PK', 'BALOCHISTAN', '9815.6000', 'saas_cloud_erp', 'BRA Balochistan Sales Tax on Services (Standard)', 15.0000,
   'standard_with_input_tax', 'all', 'BRA',
   'Balochistan Sales Tax on Services Act, 2015, Second Schedule, Heading 9815.6000',
   'Standard rate with input tax credit.',
   true, 3.0000, '2015-07-01 00:00:00+00', NULL, true),

  ('PK', 'BALOCHISTAN', '9815.6000', 'saas_cloud_erp', 'BRA Reduced Rate for IT/Software (No Input Credit)', 3.0000,
   'reduced_without_input_tax', 'all', 'BRA',
   'BRA Act 2015, Second Schedule, Heading 9815.6000',
   'Conditional on no input tax credit claimed.',
   false, 3.0000, '2015-07-01 00:00:00+00', NULL, true);

-- Restore Migration 038 baseline for KP
DELETE FROM platform.billing_tax_rules WHERE province_state = 'KP';

INSERT INTO platform.billing_tax_rules (
  country_code, province_state, tariff_code, service_category, tax_name, rate_percentage,
  tax_regime, customer_type, statutory_authority, statutory_reference, eligibility_conditions,
  input_tax_allowable, withholding_rate_percentage, effective_from, effective_until, is_active
) VALUES
  ('PK', 'KP', '9815.6000', 'saas_cloud_erp', 'KPRA Khyber Pakhtunkhwa Sales Tax on Services (Standard)', 15.0000,
   'standard_with_input_tax', 'all', 'KPRA',
   'Khyber Pakhtunkhwa Sales Tax on Services Act, 2022, Second Schedule, Heading 9815.6000',
   'Standard rate with input tax credit.',
   true, 3.0000, '2022-07-01 00:00:00+00', NULL, true),

  ('PK', 'KP', '9815.6000', 'saas_cloud_erp', 'KPRA Reduced Rate for IT/Software (No Input Credit)', 2.0000,
   'reduced_without_input_tax', 'all', 'KPRA',
   'KPRA Act 2022, Second Schedule, Heading 9815.6000 Reduced Column',
   'Conditional on no input tax credit claimed.',
   false, 2.0000, '2022-07-01 00:00:00+00', NULL, true);
