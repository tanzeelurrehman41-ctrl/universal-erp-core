-- Return only active workspaces belonging to a validated external identity.
-- The API supplies issuer/subject from the already-validated bearer token.
CREATE OR REPLACE FUNCTION identity.list_user_company_contexts(
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

REVOKE ALL ON FUNCTION identity.list_user_company_contexts(text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.list_user_company_contexts(text, text) TO erp_app;
