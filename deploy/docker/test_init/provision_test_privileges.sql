-- universal-erp-core/deploy/docker/test_init/provision_test_privileges.sql
-- Executed under test_bootstrap_admin in universal_erp_disposable_test ONLY

\connect universal_erp_disposable_test

-- 1. Safety Guard: Fail if connected to wrong database
DO $$
BEGIN
    IF current_database() <> 'universal_erp_disposable_test' THEN
        RAISE EXCEPTION 'Refusing to provision permissions: current database is % (expected universal_erp_disposable_test)', current_database();
    END IF;
END $$;

-- 2. Revoke PUBLIC Defaults on Database, Schema public, and Application Schemas
REVOKE CREATE, TEMP ON DATABASE universal_erp_disposable_test FROM PUBLIC;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM PUBLIC;
REVOKE ALL ON ALL ROUTINES IN SCHEMA public FROM PUBLIC;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM PUBLIC;

DO $$
DECLARE 
    s text;
    r RECORD;
    rol text;
BEGIN
    FOR s IN SELECT unnest(ARRAY['sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit'])
    LOOP
        EXECUTE format('REVOKE ALL ON SCHEMA %I FROM PUBLIC;', s);
        EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA %I FROM PUBLIC;', s);
        EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA %I FROM PUBLIC;', s);
        EXECUTE format('REVOKE ALL ON ALL ROUTINES IN SCHEMA %I FROM PUBLIC;', s);
    END LOOP;

    -- Explicitly REVOKE ALL from erp_app on existing objects in all 10 application schemas + public to clear dump-inherited over-grants
    FOR s IN SELECT unnest(ARRAY['sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit', 'public'])
    LOOP
        EXECUTE format('REVOKE ALL ON SCHEMA %I FROM erp_app CASCADE;', s);
        EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA %I FROM erp_app CASCADE;', s);
        EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA %I FROM erp_app CASCADE;', s);
        EXECUTE format('REVOKE ALL ON ALL ROUTINES IN SCHEMA %I FROM erp_app CASCADE;', s);
    END LOOP;

    -- Revoke existing PUBLIC & erp_app USAGE on custom types and domains across all 10 schemas + public.
    FOR r IN 
        SELECT n.nspname, t.typname
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE (n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit') OR n.nspname = 'public')
          AND t.typisdefined
          AND t.typelem = 0
    LOOP
        EXECUTE format('REVOKE ALL ON TYPE %I.%I FROM PUBLIC, erp_app CASCADE;', r.nspname, r.typname);
        EXECUTE format('GRANT USAGE ON TYPE %I.%I TO erp_dev;', r.nspname, r.typname);
        IF r.nspname NOT IN ('authorization', 'audit') THEN
            EXECUTE format('GRANT USAGE ON TYPE %I.%I TO erp_app;', r.nspname, r.typname);
        END IF;
    END LOOP;

    -- A. Global Default Privilege Neutralization (defaclnamespace = 0)
    FOR rol IN SELECT unnest(ARRAY['test_bootstrap_admin', 'erp_dev', 'postgres'])
    LOOP
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON TABLES FROM PUBLIC, erp_app;', rol);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON SEQUENCES FROM PUBLIC, erp_app;', rol);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON ROUTINES FROM PUBLIC, erp_app;', rol);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON TYPES FROM PUBLIC, erp_app;', rol);

        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT ALL ON TABLES TO erp_dev;', rol);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT ALL ON SEQUENCES TO erp_dev;', rol);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT ALL ON ROUTINES TO erp_dev;', rol);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT ALL ON TYPES TO erp_dev;', rol);
    END LOOP;

    -- B. Per-Schema Default Privileges in Operational Domain Schemas + public
    FOR s IN SELECT unnest(ARRAY['sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'public'])
    LOOP
        FOR rol IN SELECT unnest(ARRAY['test_bootstrap_admin', 'erp_dev', 'postgres'])
        LOOP
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON TABLES FROM PUBLIC, erp_app;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON SEQUENCES FROM PUBLIC, erp_app;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON ROUTINES FROM PUBLIC, erp_app;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON TYPES FROM PUBLIC, erp_app;', rol, s);

            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON TABLES TO erp_dev;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON SEQUENCES TO erp_dev;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON ROUTINES TO erp_dev;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT USAGE ON TYPES TO erp_dev, erp_app;', rol, s);
        END LOOP;
    END LOOP;

    -- C. Sensitive Schemas Default Privileges: in 'authorization' and 'audit'
    FOR s IN SELECT unnest(ARRAY['authorization', 'audit'])
    LOOP
        FOR rol IN SELECT unnest(ARRAY['test_bootstrap_admin', 'erp_dev', 'postgres'])
        LOOP
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON TABLES FROM PUBLIC, erp_app;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON SEQUENCES FROM PUBLIC, erp_app;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON ROUTINES FROM PUBLIC, erp_app;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON TYPES FROM PUBLIC, erp_app;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON TABLES TO erp_dev;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON SEQUENCES TO erp_dev;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON ROUTINES TO erp_dev;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON TYPES TO erp_dev;', rol, s);
        END LOOP;
    END LOOP;
END $$;

-- 3. Grant Database CONNECT to erp_dev and erp_app
GRANT CONNECT ON DATABASE universal_erp_disposable_test TO erp_dev, erp_app;

-- 4. Exhaustive Ownership Transfer to erp_dev (Application Schemas Only)
DO $$
DECLARE
    s text;
    r RECORD;
BEGIN
    -- A. Transfer Schema Namespace Ownership
    FOR s IN SELECT unnest(ARRAY['sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit'])
    LOOP
        EXECUTE format('GRANT USAGE, CREATE ON SCHEMA %I TO erp_dev;', s);
        EXECUTE format('ALTER SCHEMA %I OWNER TO erp_dev;', s);
        EXECUTE format('GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA %I TO erp_dev;', s);
        EXECUTE format('GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA %I TO erp_dev;', s);
        EXECUTE format('GRANT ALL PRIVILEGES ON ALL ROUTINES IN SCHEMA %I TO erp_dev;', s);
    END LOOP;

    -- B. Transfer Ownership of Ordinary and Partitioned Tables
    FOR r IN 
        SELECT n.nspname, c.relname
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit')
          AND c.relkind IN ('r', 'p')
    LOOP
        EXECUTE format('ALTER TABLE %I.%I OWNER TO erp_dev;', r.nspname, r.relname);
    END LOOP;

    -- C. Transfer Ownership of Views
    FOR r IN 
        SELECT n.nspname, c.relname
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit')
          AND c.relkind = 'v'
    LOOP
        EXECUTE format('ALTER VIEW %I.%I OWNER TO erp_dev;', r.nspname, r.relname);
    END LOOP;

    -- D. Transfer Ownership of Materialized Views
    FOR r IN 
        SELECT n.nspname, c.relname
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit')
          AND c.relkind = 'm'
    LOOP
        EXECUTE format('ALTER MATERIALIZED VIEW %I.%I OWNER TO erp_dev;', r.nspname, r.relname);
    END LOOP;

    -- E. Transfer Ownership of Foreign Tables
    FOR r IN 
        SELECT n.nspname, c.relname
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit')
          AND c.relkind = 'f'
    LOOP
        EXECUTE format('ALTER FOREIGN TABLE %I.%I OWNER TO erp_dev;', r.nspname, r.relname);
    END LOOP;

    -- F. Transfer Ownership of Sequences
    FOR r IN 
        SELECT n.nspname, c.relname
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit')
          AND c.relkind = 'S'
    LOOP
        EXECUTE format('ALTER SEQUENCE %I.%I OWNER TO erp_dev;', r.nspname, r.relname);
    END LOOP;

    -- G. Transfer Ownership of Routines (Functions & Procedures)
    FOR r IN 
        SELECT n.nspname, p.proname, pg_get_function_identity_arguments(p.oid) AS args
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit')
    LOOP
        EXECUTE format('ALTER ROUTINE %I.%I(%s) OWNER TO erp_dev;', r.nspname, r.proname, r.args);
    END LOOP;

    -- H. Transfer Ownership of Custom Types
    FOR r IN 
        SELECT n.nspname, t.typname
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit')
          AND (
              t.typtype IN ('e', 'r', 'm') OR 
              (t.typtype = 'c' AND NOT EXISTS (
                  SELECT 1 FROM pg_class c WHERE c.oid = t.typrelid AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
              ))
          )
    LOOP
        EXECUTE format('ALTER TYPE %I.%I OWNER TO erp_dev;', r.nspname, r.typname);
    END LOOP;

    -- I. Transfer Ownership of Domains
    FOR r IN 
        SELECT n.nspname, t.typname
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit')
          AND t.typtype = 'd'
    LOOP
        EXECUTE format('ALTER DOMAIN %I.%I OWNER TO erp_dev;', r.nspname, r.typname);
    END LOOP;
END $$;

-- 5. Revoke Over-Grants from Baseline Migrations & Enforce Zero-Privilege Baseline for Out-of-Scope Schemas
REVOKE DELETE, TRUNCATE, REFERENCES, TRIGGER ON ALL TABLES IN SCHEMA erp_sales FROM erp_app;
REVOKE DELETE, TRUNCATE, REFERENCES, TRIGGER ON ALL TABLES IN SCHEMA erp_purchasing FROM erp_app;
REVOKE INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON platform.subscription_plans, platform.plan_features FROM erp_app;
REVOKE INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON erp_inventory.warehouse FROM erp_app;

-- Explicitly enforce ZERO privileges for erp_app in authorization and audit schemas during Phase 5 / Rev23
REVOKE ALL ON ALL TABLES IN SCHEMA "authorization", audit FROM erp_app;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA "authorization", audit FROM erp_app;
REVOKE ALL ON ALL ROUTINES IN SCHEMA "authorization", audit FROM erp_app;
REVOKE ALL ON SCHEMA "authorization", audit FROM erp_app;

-- Explicitly revoke administrative routines in pg_catalog from PUBLIC and erp_app
DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN 
        SELECT p.proname, pg_get_function_identity_arguments(p.oid) AS args
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'pg_catalog'
          AND p.proname IN ('pg_terminate_backend', 'pg_cancel_backend', 'pg_reload_conf', 'pg_rotate_logfile', 'pg_read_file', 'pg_read_binary_file', 'pg_write_file')
    LOOP
        EXECUTE format('REVOKE ALL ON FUNCTION pg_catalog.%I(%s) FROM PUBLIC, erp_app;', r.proname, r.args);
    END LOOP;
END $$;

-- 6. Configure erp_app Least-Privilege Permissions (Authoritative 38-Table Reconciled Matrix)
GRANT USAGE ON SCHEMA sync, erp_inventory, erp_purchasing, erp_sales, finance, platform, identity, organization TO erp_app;

-- sync schema (7 tables + 1 sequence)
GRANT SELECT, INSERT, UPDATE ON sync.operation_inbox, sync.operation_conflicts, sync.company_change_counters TO erp_app;
GRANT SELECT, INSERT ON sync.change_log TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.device_company_scopes, sync.offline_grants TO erp_app; -- Migration 010 L6-7
GRANT SELECT ON sync.device_location_scopes TO erp_app;
GRANT USAGE, SELECT ON SEQUENCE sync.change_log_change_sequence_seq TO erp_app;

-- erp_inventory schema (11 tables)
GRANT SELECT, INSERT, UPDATE ON erp_inventory.stock_balance, erp_inventory.command_idempotency TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.inventory_movement, erp_inventory.inventory_movement_leg TO erp_app;
GRANT INSERT ON erp_inventory.outbox_event TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.product TO erp_app;
GRANT SELECT ON erp_inventory.warehouse, erp_inventory.stock_bin, 
                erp_inventory.product_variant, erp_inventory.unit_of_measure, erp_inventory.stock_status TO erp_app;

-- erp_purchasing schema (6 tables)
GRANT SELECT, INSERT ON erp_purchasing.supplier TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_purchasing.purchase_order TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_purchasing.purchase_order_line TO erp_app;
GRANT SELECT, INSERT ON erp_purchasing.goods_received_note TO erp_app;
GRANT SELECT, INSERT ON erp_purchasing.goods_received_note_line TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_purchasing.outbox_event TO erp_app;

-- erp_sales schema (6 tables)
GRANT SELECT, INSERT, UPDATE ON erp_sales.customer TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_sales.sales_order TO erp_app;
GRANT SELECT, INSERT ON erp_sales.sales_order_line TO erp_app;
GRANT SELECT, INSERT ON erp_sales.sales_invoice TO erp_app;
GRANT SELECT, INSERT ON erp_sales.sales_invoice_line TO erp_app;
GRANT SELECT, INSERT ON erp_sales.outbox_event TO erp_app;

-- finance schema (4 tables)
GRANT SELECT ON finance.fiscal_periods, finance.accounts TO erp_app;
GRANT SELECT, INSERT ON finance.journals, finance.journal_lines TO erp_app;

-- platform schema (2 global read-only catalog tables)
GRANT SELECT ON platform.subscription_plans TO erp_app;
GRANT SELECT ON platform.plan_features TO erp_app;

-- identity schema (Table-level grants)
GRANT SELECT, INSERT, UPDATE ON identity.devices TO erp_app; -- Migration 010 L6-7
GRANT SELECT, INSERT, UPDATE ON identity.company_memberships TO erp_app; -- Migration 012 L80-84

-- Column-Restricted Tables (4 tables)
GRANT SELECT (id, status) ON platform.tenants TO erp_app;
GRANT SELECT (id, status) ON identity.users TO erp_app;
GRANT SELECT (id, tenant_id, status) ON organization.companies TO erp_app;
GRANT SELECT (id, tenant_id, company_id, status) ON organization.locations TO erp_app;

-- Runtime Functions
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

GRANT EXECUTE ON FUNCTION set_config(text, text, boolean) TO erp_app;
GRANT EXECUTE ON FUNCTION pg_advisory_xact_lock(bigint) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.list_user_company_contexts(text, text) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.resolve_authenticated_user_context(uuid, uuid, text, text) TO erp_app;

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


-- Migration 022 Reconciliation
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
