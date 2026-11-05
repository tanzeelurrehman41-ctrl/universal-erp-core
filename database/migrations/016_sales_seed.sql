-- Migration 016 – Default Walk-in Customer Seed for Local Development
INSERT INTO erp_sales.customer (
    tenant_id,
    company_id,
    customer_id,
    customer_code,
    name,
    customer_type,
    contact_name,
    currency_code,
    credit_limit,
    payment_terms_days,
    is_active
) VALUES (
    '00000000-0000-4000-8000-000000000001'::uuid,
    '00000000-0000-4000-8000-000000000002'::uuid,
    '00000000-0000-4000-8000-000000000301'::uuid,
    'CUST-WALKIN',
    'Walk-in Retail Customer',
    'retail',
    'Walk-in POS Customer',
    'PKR',
    0,
    0,
    true
) ON CONFLICT (tenant_id, company_id, customer_code) DO NOTHING;
