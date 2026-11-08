-- Migration 021: Grant Context Listing and Authentication Resolution Routine Privileges for erp_app
-- Authoritative least-privileged grants for identity.list_user_company_contexts(text, text)
-- and identity.resolve_authenticated_user_context(uuid, uuid, text, text).
-- Enables authenticated OIDC and Firebase users to query their company and tenant memberships
-- via GET /api/v1/contexts and securely resolves active user tenant context in middleware.
-- Preserves Forced Row-Level Security (RLS) and strict tenant/company isolation.

CREATE OR REPLACE FUNCTION identity.resolve_authenticated_user_context(
    p_tenant_id uuid,
    p_company_id uuid,
    p_issuer text,
    p_subject text
)
RETURNS uuid
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'identity', 'platform', 'organization'
AS $$
    SELECT u.id
    FROM identity.external_identities AS external_identity
    JOIN identity.users AS u ON u.id = external_identity.user_id
    JOIN identity.tenant_memberships AS tenant_membership
      ON tenant_membership.tenant_id = p_tenant_id
     AND tenant_membership.user_id = u.id
    JOIN identity.company_memberships AS company_membership
      ON company_membership.tenant_id = p_tenant_id
     AND company_membership.company_id = p_company_id
     AND company_membership.user_id = u.id
    JOIN platform.tenants AS tenant ON tenant.id = p_tenant_id
    JOIN organization.companies AS company
      ON company.tenant_id = p_tenant_id AND company.id = p_company_id
    WHERE external_identity.issuer = p_issuer
      AND external_identity.subject = p_subject
      AND u.status = 'active'
      AND tenant_membership.status = 'active'
      AND company_membership.status = 'active'
      AND tenant.status = 'active'
      AND company.status = 'active'
    LIMIT 1;
$$;

ALTER FUNCTION identity.resolve_authenticated_user_context(uuid, uuid, text, text) OWNER TO erp_dev;

GRANT EXECUTE ON FUNCTION identity.list_user_company_contexts(text, text) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.resolve_authenticated_user_context(uuid, uuid, text, text) TO erp_app;
GRANT INSERT ON erp_inventory.product TO erp_app;

CREATE OR REPLACE FUNCTION identity.user_has_permission(
    p_tenant_id uuid,
    p_company_id uuid,
    p_user_id uuid,
    p_permission_code text
)
RETURNS boolean
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'identity', 'authorization'
AS $$
    SELECT EXISTS (
      SELECT 1
      FROM identity.company_memberships AS cm
      JOIN identity.tenant_memberships AS tm
        ON tm.tenant_id = cm.tenant_id
       AND tm.user_id = cm.user_id
      JOIN "authorization".membership_role_assignments AS assignment
        ON assignment.tenant_id = tm.tenant_id
       AND assignment.membership_id = tm.id
      JOIN "authorization".role_permissions AS permission
        ON permission.tenant_id = assignment.tenant_id
       AND permission.role_id = assignment.role_id
      WHERE cm.tenant_id = p_tenant_id
        AND cm.company_id = p_company_id
        AND cm.user_id = p_user_id
        AND cm.status = 'active'
        AND tm.status = 'active'
        AND assignment.effective_from <= now()
        AND (assignment.effective_until IS NULL OR assignment.effective_until > now())
        AND permission.permission_code = p_permission_code
        AND permission.scope_type IN ('tenant','company')
    );
$$;

ALTER FUNCTION identity.user_has_permission(uuid, uuid, uuid, text) OWNER TO erp_dev;

GRANT EXECUTE ON FUNCTION identity.user_has_permission(uuid, uuid, uuid, text) TO erp_app;
