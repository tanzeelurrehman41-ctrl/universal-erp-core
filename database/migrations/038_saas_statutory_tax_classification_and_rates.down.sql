-- Universal ERP — Rollback Migration 038: SaaS Statutory Tax Classification, Rate Hardening & Place-of-Provision Rules

DROP INDEX IF EXISTS platform.ix_billing_tax_rules_tariff;

-- Clean up enhanced tax rules
DELETE FROM platform.billing_tax_rules WHERE tariff_code = '9815.6000';

-- Restore Migration 037 baseline seed data
INSERT INTO platform.billing_tax_rules (country_code, province_state, tax_name, rate_percentage)
VALUES
  ('PK', 'FEDERAL', 'ICT Sales Tax on IT/SaaS Services', 15.0000),
  ('PK', 'PUNJAB', 'PRA Punjab Sales Tax on Services', 16.0000),
  ('PK', 'SINDH', 'SRB Sindh Sales Tax on Services', 13.0000),
  ('PK', 'KP', 'KPRA Khyber Pakhtunkhwa Sales Tax on Services', 15.0000),
  ('PK', 'BALOCHISTAN', 'BRA Balochistan Sales Tax on Services', 15.0000)
ON CONFLICT DO NOTHING;

-- Drop newly added columns
ALTER TABLE platform.billing_tax_rules
  DROP COLUMN IF EXISTS withholding_rate_percentage,
  DROP COLUMN IF EXISTS input_tax_allowable,
  DROP COLUMN IF EXISTS eligibility_conditions,
  DROP COLUMN IF EXISTS statutory_reference,
  DROP COLUMN IF EXISTS statutory_authority,
  DROP COLUMN IF EXISTS customer_type,
  DROP COLUMN IF EXISTS tax_regime,
  DROP COLUMN IF EXISTS service_category,
  DROP COLUMN IF EXISTS tariff_code;
