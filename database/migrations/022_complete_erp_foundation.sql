-- Migration 022: Complete ERP Foundation Routines
-- Provides encapsulated SECURITY DEFINER routines for User Profile, Company Settings,
-- User Memberships, Roles/Permissions, and Audit Logging under Forced Row-Level Security.

-- 1. User Profile Routine
CREATE OR REPLACE FUNCTION identity.get_user_profile(
    p_tenant_id uuid,
    p_company_id uuid,
    p_user_id uuid
)
RETURNS jsonb
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'identity', 'platform', 'organization', 'authorization'
AS $$
    SELECT jsonb_build_object(
        'userId', u.id,
        'email', u.email_normalized,
        'displayName', u.display_name,
        'status', u.status,
        'createdAt', u.created_at,
        'tenantId', t.id,
        'tenantCode', t.tenant_code,
        'tenantName', t.display_name,
        'companyId', c.id,
        'companyCode', c.company_code,
        'companyName', c.name,
        'legalName', c.legal_name,
        'registrationNumber', c.registration_number,
        'currencyCode', c.base_currency_code,
        'timezone', c.timezone,
        'roles', COALESCE((
            SELECT jsonb_agg(jsonb_build_object('roleCode', r.role_code, 'name', r.name))
            FROM "authorization".membership_role_assignments mra
            JOIN identity.tenant_memberships tm ON tm.id = mra.membership_id
            JOIN "authorization".roles r ON r.id = mra.role_id
            WHERE tm.tenant_id = p_tenant_id AND tm.user_id = p_user_id
        ), '[]'::jsonb),
        'permissions', COALESCE((
            SELECT jsonb_agg(DISTINCT rp.permission_code)
            FROM "authorization".membership_role_assignments mra
            JOIN identity.tenant_memberships tm ON tm.id = mra.membership_id
            JOIN "authorization".role_permissions rp ON rp.role_id = mra.role_id AND rp.tenant_id = p_tenant_id
            WHERE tm.tenant_id = p_tenant_id AND tm.user_id = p_user_id
        ), '[]'::jsonb)
    )
    FROM identity.users u
    JOIN platform.tenants t ON t.id = p_tenant_id
    JOIN organization.companies c ON c.id = p_company_id AND c.tenant_id = p_tenant_id
    WHERE u.id = p_user_id;
$$;

ALTER FUNCTION identity.get_user_profile(uuid, uuid, uuid) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.get_user_profile(uuid, uuid, uuid) TO erp_app;

-- 2. Company Settings Routine (Get and Update)
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

ALTER FUNCTION identity.get_company_settings(uuid, uuid) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.get_company_settings(uuid, uuid) TO erp_app;

CREATE OR REPLACE FUNCTION identity.update_company_settings(
    p_tenant_id uuid,
    p_company_id uuid,
    p_name text,
    p_legal_name text,
    p_registration_number text,
    p_timezone text
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path TO 'pg_catalog', 'organization'
AS $$
DECLARE
    v_result jsonb;
BEGIN
    UPDATE organization.companies
    SET name = COALESCE(NULLIF(TRIM(p_name), ''), name),
        legal_name = COALESCE(NULLIF(TRIM(p_legal_name), ''), legal_name),
        registration_number = COALESCE(NULLIF(TRIM(p_registration_number), ''), registration_number),
        timezone = COALESCE(NULLIF(TRIM(p_timezone), ''), timezone),
        updated_at = now()
    WHERE tenant_id = p_tenant_id AND id = p_company_id;

    SELECT identity.get_company_settings(p_tenant_id, p_company_id) INTO v_result;
    RETURN v_result;
END;
$$;

ALTER FUNCTION identity.update_company_settings(uuid, uuid, text, text, text, text) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.update_company_settings(uuid, uuid, text, text, text, text) TO erp_app;

-- 3. Company Users Listing Routine
CREATE OR REPLACE FUNCTION identity.list_company_users(
    p_tenant_id uuid,
    p_company_id uuid
)
RETURNS TABLE (
    user_id uuid,
    email text,
    display_name text,
    status text,
    roles jsonb,
    created_at timestamptz
)
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'identity', 'authorization'
AS $$
    SELECT 
        u.id AS user_id,
        u.email_normalized AS email,
        u.display_name,
        cm.status,
        COALESCE((
            SELECT jsonb_agg(jsonb_build_object('roleCode', r.role_code, 'name', r.name))
            FROM "authorization".membership_role_assignments mra
            JOIN identity.tenant_memberships tm ON tm.id = mra.membership_id
            JOIN "authorization".roles r ON r.id = mra.role_id
            WHERE tm.tenant_id = p_tenant_id AND tm.user_id = u.id
        ), '[]'::jsonb) AS roles,
        cm.created_at
    FROM identity.company_memberships cm
    JOIN identity.users u ON u.id = cm.user_id
    WHERE cm.tenant_id = p_tenant_id AND cm.company_id = p_company_id
    ORDER BY u.display_name;
$$;

ALTER FUNCTION identity.list_company_users(uuid, uuid) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.list_company_users(uuid, uuid) TO erp_app;

-- 4. User Invitation / Assignment Routine
CREATE OR REPLACE FUNCTION identity.invite_or_assign_company_user(
    p_tenant_id uuid,
    p_company_id uuid,
    p_email text,
    p_display_name text,
    p_role_code text
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path TO 'pg_catalog', 'identity', 'authorization'
AS $$
DECLARE
    v_user_id uuid;
    v_tm_id uuid;
    v_cm_id uuid;
    v_role_id uuid;
    v_clean_email text := LOWER(TRIM(p_email));
BEGIN
    SELECT id INTO v_user_id FROM identity.users WHERE email_normalized = v_clean_email;
    IF v_user_id IS NULL THEN
        INSERT INTO identity.users (email_normalized, display_name, status)
        VALUES (v_clean_email, TRIM(p_display_name), 'active')
        RETURNING id INTO v_user_id;
    END IF;

    SELECT id INTO v_tm_id FROM identity.tenant_memberships WHERE tenant_id = p_tenant_id AND user_id = v_user_id;
    IF v_tm_id IS NULL THEN
        INSERT INTO identity.tenant_memberships (tenant_id, user_id, status)
        VALUES (p_tenant_id, v_user_id, 'active')
        RETURNING id INTO v_tm_id;
    END IF;

    SELECT id INTO v_cm_id FROM identity.company_memberships WHERE tenant_id = p_tenant_id AND company_id = p_company_id AND user_id = v_user_id;
    IF v_cm_id IS NULL THEN
        INSERT INTO identity.company_memberships (tenant_id, company_id, user_id, status)
        VALUES (p_tenant_id, p_company_id, v_user_id, 'active')
        RETURNING id INTO v_cm_id;
    END IF;

    IF p_role_code IS NOT NULL AND p_role_code <> '' THEN
        SELECT id INTO v_role_id FROM "authorization".roles WHERE role_code = p_role_code AND (tenant_id = p_tenant_id OR tenant_id IS NULL) LIMIT 1;
        IF v_role_id IS NOT NULL THEN
            IF NOT EXISTS (
                SELECT 1 FROM "authorization".membership_role_assignments
                WHERE tenant_id = p_tenant_id AND membership_id = v_tm_id AND role_id = v_role_id
            ) THEN
                INSERT INTO "authorization".membership_role_assignments (tenant_id, membership_id, role_id, effective_from)
                VALUES (p_tenant_id, v_tm_id, v_role_id, now());
            END IF;
        END IF;
    END IF;

    RETURN jsonb_build_object(
        'userId', v_user_id,
        'email', v_clean_email,
        'displayName', p_display_name,
        'roleCode', p_role_code,
        'status', 'active'
    );
END;
$$;

ALTER FUNCTION identity.invite_or_assign_company_user(uuid, uuid, text, text, text) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.invite_or_assign_company_user(uuid, uuid, text, text, text) TO erp_app;

-- 5. List Roles and Permissions Routine
CREATE OR REPLACE FUNCTION identity.list_roles_and_permissions(
    p_tenant_id uuid
)
RETURNS TABLE (
    role_id uuid,
    role_code text,
    name text,
    is_system_role boolean,
    permissions jsonb
)
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'authorization'
AS $$
    SELECT 
        r.id AS role_id,
        r.role_code,
        r.name,
        r.is_system_role,
        COALESCE((
            SELECT jsonb_agg(jsonb_build_object('code', p.permission_code, 'description', p.description, 'module', p.module_code))
            FROM "authorization".role_permissions rp
            JOIN "authorization".permissions p ON p.permission_code = rp.permission_code
            WHERE rp.role_id = r.id AND (rp.tenant_id = p_tenant_id OR rp.tenant_id IS NULL)
        ), '[]'::jsonb) AS permissions
    FROM "authorization".roles r
    WHERE r.tenant_id = p_tenant_id OR r.tenant_id IS NULL
    ORDER BY r.role_code;
$$;

ALTER FUNCTION identity.list_roles_and_permissions(uuid) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.list_roles_and_permissions(uuid) TO erp_app;

-- 6. Audit Logging Routines
CREATE OR REPLACE FUNCTION identity.record_audit_event(
    p_tenant_id uuid,
    p_company_id uuid,
    p_actor_user_id uuid,
    p_event_code text,
    p_entity_type text,
    p_entity_id uuid,
    p_metadata jsonb
)
RETURNS uuid
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path TO 'pg_catalog', 'audit'
AS $$
DECLARE
    v_event_id uuid := gen_random_uuid();
BEGIN
    INSERT INTO audit.audit_events (
        id, tenant_id, company_id, actor_user_id, event_code,
        entity_type, entity_id, correlation_id, occurred_at, recorded_at, metadata
    )
    VALUES (
        v_event_id, p_tenant_id, p_company_id, p_actor_user_id, p_event_code,
        p_entity_type, p_entity_id, gen_random_uuid(), now(), now(), COALESCE(p_metadata, '{}'::jsonb)
    );
    RETURN v_event_id;
END;
$$;

ALTER FUNCTION identity.record_audit_event(uuid, uuid, uuid, text, text, uuid, jsonb) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.record_audit_event(uuid, uuid, uuid, text, text, uuid, jsonb) TO erp_app;

CREATE OR REPLACE FUNCTION identity.list_audit_events(
    p_tenant_id uuid,
    p_company_id uuid,
    p_limit int DEFAULT 50
)
RETURNS TABLE (
    event_id uuid,
    occurred_at timestamptz,
    actor_email text,
    actor_name text,
    event_code text,
    entity_type text,
    entity_id uuid,
    metadata jsonb
)
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'audit', 'identity'
AS $$
    SELECT 
        ae.id AS event_id,
        ae.occurred_at,
        u.email_normalized AS actor_email,
        u.display_name AS actor_name,
        ae.event_code,
        ae.entity_type,
        ae.entity_id,
        ae.metadata
    FROM audit.audit_events ae
    LEFT JOIN identity.users u ON u.id = ae.actor_user_id
    WHERE ae.tenant_id = p_tenant_id AND (ae.company_id = p_company_id OR ae.company_id IS NULL)
    ORDER BY ae.occurred_at DESC
    LIMIT LEAST(GREATEST(p_limit, 1), 200);
$$;

ALTER FUNCTION identity.list_audit_events(uuid, uuid, int) OWNER TO erp_dev;
GRANT EXECUTE ON FUNCTION identity.list_audit_events(uuid, uuid, int) TO erp_app;
