DO $block$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
BEGIN
    INSERT INTO platform.tenants (
        id, tenant_code, display_name, legal_name, status,
        default_country_code, default_currency_code, default_timezone
    )
    VALUES (
        v_tenant_id, 'DEMO-CLOUD', 'Universal Cloud Demo Org', 'Universal Cloud Demo Org Inc', 'active',
        'US', 'USD', 'UTC'
    )
    ON CONFLICT (tenant_code) DO NOTHING;

    -- Set tenant context for RLS insertion into organization.companies
    PERFORM set_config('app.tenant_id', v_tenant_id::text, true);

    INSERT INTO organization.companies (
        id, tenant_id, company_code, name, legal_name,
        country_code, base_currency_code, timezone, status
    )
    VALUES (
        v_company_id, v_tenant_id, 'CLOUD-LOGISTICS', 'Cloud Global Logistics Ltd', 'Cloud Global Logistics Ltd',
        'US', 'USD', 'UTC', 'active'
    )
    ON CONFLICT (id) DO NOTHING;
END $block$;
