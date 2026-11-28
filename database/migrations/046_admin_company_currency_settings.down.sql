-- Rollback Migration 046: Admin Company Currency Settings & Exchange-Rate Hardening

DROP FUNCTION IF EXISTS identity.list_company_exchange_rates(uuid, uuid, char(3), char(3), text);
DROP FUNCTION IF EXISTS identity.list_company_currency_history(uuid, uuid, int);
DROP FUNCTION IF EXISTS identity.update_company_currency_settings(uuid, uuid, uuid, char(3), char(3), date, boolean, text);

CREATE OR REPLACE FUNCTION identity.get_company_settings(
    p_tenant_id uuid,
    p_company_id uuid
)
RETURNS jsonb
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'organization'
AS $$
    SELECT jsonb_build_object(
        'companyId', c.id,
        'tenantId', c.tenant_id,
        'companyCode', c.company_code,
        'name', c.name,
        'legalName', c.legal_name,
        'registrationNumber', c.registration_number,
        'countryCode', c.country_code,
        'currencyCode', c.base_currency_code,
        'timezone', c.timezone,
        'fiscalYearStartMonth', c.fiscal_year_start_month,
        'status', c.status,
        'updatedAt', c.updated_at
    )
    FROM organization.companies c
    WHERE c.tenant_id = p_tenant_id AND c.id = p_company_id;
$$;

ALTER FUNCTION identity.get_company_settings(uuid, uuid) OWNER TO CURRENT_USER;
GRANT EXECUTE ON FUNCTION identity.get_company_settings(uuid, uuid) TO erp_app;

DROP FUNCTION IF EXISTS identity.list_user_company_contexts(text, text);

CREATE FUNCTION identity.list_user_company_contexts(
    p_issuer text,
    p_subject text
)
RETURNS TABLE (
    tenant_id uuid,
    tenant_code text,
    tenant_name text,
    company_id uuid,
    company_code text,
    company_name text,
    base_currency_code char(3),
    timezone text
)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, identity, platform, organization
AS $$
    SELECT tenant.id,
           tenant.tenant_code,
           tenant.display_name,
           company.id,
           company.company_code,
           company.name,
           company.base_currency_code,
           company.timezone
    FROM identity.external_identities AS external_identity
    JOIN identity.users AS erp_user
      ON erp_user.id = external_identity.user_id
    JOIN identity.tenant_memberships AS tenant_membership
      ON tenant_membership.user_id = erp_user.id
    JOIN identity.company_memberships AS company_membership
      ON company_membership.tenant_id = tenant_membership.tenant_id
     AND company_membership.user_id = erp_user.id
    JOIN platform.tenants AS tenant
      ON tenant.id = tenant_membership.tenant_id
    JOIN organization.companies AS company
      ON company.tenant_id = tenant.id
     AND company.id = company_membership.company_id
    WHERE external_identity.issuer = p_issuer
      AND external_identity.subject = p_subject
      AND erp_user.status = 'active'
      AND tenant_membership.status = 'active'
      AND company_membership.status = 'active'
      AND tenant.status = 'active'
      AND company.status = 'active'
    ORDER BY tenant.display_name, company.name;
$$;

ALTER FUNCTION identity.list_user_company_contexts(text, text) OWNER TO CURRENT_USER;
REVOKE ALL ON FUNCTION identity.list_user_company_contexts(text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.list_user_company_contexts(text, text) TO erp_app;

DO $$
DECLARE
    t_rec record;
BEGIN
    FOR t_rec IN SELECT DISTINCT tenant_id FROM "authorization".role_permissions LOOP
        PERFORM set_config('app.tenant_id', t_rec.tenant_id::text, true);
        DELETE FROM "authorization".role_permissions
        WHERE tenant_id = t_rec.tenant_id
          AND permission_code IN ('admin.currency_settings.view', 'admin.currency_settings.manage', 'finance.exchange_rate.manage');
    END LOOP;
END $$;

DELETE FROM "authorization".permissions
WHERE permission_code IN ('admin.currency_settings.view', 'admin.currency_settings.manage', 'finance.exchange_rate.manage');

DROP POLICY IF EXISTS tenant_scope ON finance.exchange_rates;
CREATE POLICY tenant_scope ON finance.exchange_rates
    USING ((tenant_id = (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid))
    WITH CHECK ((tenant_id = (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid));

DROP INDEX IF EXISTS finance.ix_finance_exchange_rates_company_date;
DROP INDEX IF EXISTS finance.ux_finance_exchange_rates_pair_date_type;

ALTER TABLE finance.exchange_rates
    DROP CONSTRAINT IF EXISTS exchange_rates_tenant_company_fkey,
    DROP CONSTRAINT IF EXISTS exchange_rates_rate_type_check,
    DROP COLUMN IF EXISTS updated_at,
    DROP COLUMN IF EXISTS updated_by,
    DROP COLUMN IF EXISTS created_by,
    DROP COLUMN IF EXISTS reference,
    DROP COLUMN IF EXISTS rate_type,
    DROP COLUMN IF EXISTS effective_date,
    DROP COLUMN IF EXISTS company_id;

DROP TABLE IF EXISTS organization.company_currency_history;

DROP TRIGGER IF EXISTS trg_companies_default_currencies ON organization.companies;
DROP FUNCTION IF EXISTS organization.set_default_company_currencies();

ALTER TABLE organization.companies
    DROP COLUMN IF EXISTS currency_effective_date,
    DROP COLUMN IF EXISTS presentation_currency_code,
    DROP COLUMN IF EXISTS functional_currency_code;
