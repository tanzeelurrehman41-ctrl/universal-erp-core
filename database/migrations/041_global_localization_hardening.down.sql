-- Universal ERP — Rollback Migration 041: Global Localization Hardening
-- Reverts additional roadmap payroll packages, OSS rules, subnational jurisdictions, countries and currencies.

DELETE FROM hr.payroll_country_packages WHERE country_code IN ('CA', 'AU', 'IN');
DELETE FROM platform.billing_tax_rules WHERE tax_name IN (
    'UK Overseas Vendor Digital Services VAT (B2C)',
    'Australia Inbound Digital Services GST (B2C)',
    'Canada Non-Resident Digital Services GST (B2C)',
    'EU One Stop Shop Destination VAT - Germany (B2C)',
    'EU One Stop Shop Destination VAT - France (B2C)'
);
DELETE FROM platform.jurisdictions WHERE country_code IN ('CA', 'AU', 'IN') OR jurisdiction_code IN ('SCOTLAND', 'WALES', 'NI');
DELETE FROM platform.countries WHERE country_code IN ('CA', 'AU', 'IN', 'NL', 'IT', 'ES', 'BE', 'AT', 'SE', 'PL');
DELETE FROM finance.currencies WHERE code IN ('CAD', 'AUD', 'INR', 'JPY', 'BHD', 'KWD', 'OMR', 'CHF', 'SGD');
