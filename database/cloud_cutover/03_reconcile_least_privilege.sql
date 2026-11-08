-- Universal ERP -- Least-Privilege Role Reconciliation & Default ACL Neutralization (Neon Cloud Deployment)

DO $$
BEGIN
    EXECUTE format('REVOKE CREATE, TEMP ON DATABASE %I FROM PUBLIC;', current_database());
    EXECUTE format('REVOKE CREATE, TEMP ON DATABASE %I FROM erp_app;', current_database());
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO erp_app;', current_database());
END $$;

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

    -- Explicitly REVOKE ALL from erp_app on existing objects in all 10 application schemas + public
    FOR s IN SELECT unnest(ARRAY['sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit', 'public'])
    LOOP
        EXECUTE format('REVOKE ALL ON SCHEMA %I FROM erp_app CASCADE;', s);
        EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA %I FROM erp_app CASCADE;', s);
        EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA %I FROM erp_app CASCADE;', s);
        EXECUTE format('REVOKE ALL ON ALL ROUTINES IN SCHEMA %I FROM erp_app CASCADE;', s);
    END LOOP;

    -- Revoke existing PUBLIC & erp_app USAGE on custom types and domains across all 10 schemas + public
    FOR r IN 
        SELECT n.nspname, t.typname
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE (n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit') OR n.nspname = 'public')
          AND t.typisdefined
          AND t.typelem = 0
    LOOP
        EXECUTE format('REVOKE ALL ON TYPE %I.%I FROM PUBLIC, erp_app CASCADE;', r.nspname, r.typname);
        IF r.nspname NOT IN ('authorization', 'audit') THEN
            EXECUTE format('GRANT USAGE ON TYPE %I.%I TO erp_app;', r.nspname, r.typname);
        END IF;
    END LOOP;

    -- Neutralize default privileges for all roles that can login and are not erp_app
    FOR rol IN SELECT DISTINCT rolname FROM pg_roles WHERE rolcanlogin = true AND rolname <> 'erp_app'
    LOOP
        BEGIN
            -- Global Default Privilege Neutralization (defaclnamespace = 0)
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON TABLES FROM PUBLIC, erp_app;', rol);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON SEQUENCES FROM PUBLIC, erp_app;', rol);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON ROUTINES FROM PUBLIC, erp_app;', rol);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON TYPES FROM PUBLIC, erp_app;', rol);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT ALL ON ROUTINES TO %I;', rol, rol);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT ALL ON TYPES TO %I;', rol, rol);

            -- Per-Schema Default Privileges in Operational Domain Schemas + public
            FOR s IN SELECT unnest(ARRAY['sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'public'])
            LOOP
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON TABLES FROM PUBLIC, erp_app;', rol, s);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON SEQUENCES FROM PUBLIC, erp_app;', rol, s);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON ROUTINES FROM PUBLIC, erp_app;', rol, s);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON TYPES FROM PUBLIC, erp_app;', rol, s);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON ROUTINES TO %I;', rol, s, rol);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON TYPES TO %I;', rol, s, rol);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT USAGE ON TYPES TO erp_app;', rol, s);
            END LOOP;

            -- Sensitive Schemas Default Privileges: in authorization and audit
            FOR s IN SELECT unnest(ARRAY['authorization', 'audit'])
            LOOP
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON TABLES FROM PUBLIC, erp_app;', rol, s);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON SEQUENCES FROM PUBLIC, erp_app;', rol, s);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON ROUTINES FROM PUBLIC, erp_app;', rol, s);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON TYPES FROM PUBLIC, erp_app;', rol, s);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON ROUTINES TO %I;', rol, s, rol);
                EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON TYPES TO %I;', rol, s, rol);
            END LOOP;
        EXCEPTION WHEN OTHERS THEN
            -- Skip roles that cannot be altered
        END;
    END LOOP;
END $$;

-- Revoke administrative routines in pg_catalog from PUBLIC and erp_app
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

-- Configure erp_app Least-Privilege Permissions (Authoritative 38-Table Reconciled Matrix)
GRANT USAGE ON SCHEMA sync, erp_inventory, erp_purchasing, erp_sales, finance, platform, identity, organization TO erp_app;

-- sync schema (7 tables + 1 sequence)
GRANT SELECT, INSERT, UPDATE ON sync.operation_inbox, sync.operation_conflicts, sync.company_change_counters TO erp_app;
GRANT SELECT, INSERT ON sync.change_log TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.device_company_scopes, sync.offline_grants TO erp_app;
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
GRANT SELECT, INSERT, UPDATE ON identity.devices TO erp_app;
GRANT SELECT, INSERT, UPDATE ON identity.company_memberships TO erp_app;

-- Column-Restricted Tables (4 tables)
GRANT SELECT (id, status) ON platform.tenants TO erp_app;
GRANT SELECT (id, status) ON identity.users TO erp_app;
GRANT SELECT (id, tenant_id, status) ON organization.companies TO erp_app;
GRANT SELECT (id, tenant_id, company_id, status) ON organization.locations TO erp_app;

-- Runtime Functions
GRANT EXECUTE ON FUNCTION set_config(text, text, boolean) TO erp_app;
GRANT EXECUTE ON FUNCTION pg_advisory_xact_lock(bigint) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.list_user_company_contexts(text, text) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.resolve_authenticated_user_context(uuid, uuid, text, text) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.user_has_permission(uuid, uuid, uuid, text) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.get_user_profile(uuid, uuid, uuid) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.get_company_settings(uuid, uuid) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.update_company_settings(uuid, uuid, text, text, text, text) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.list_company_users(uuid, uuid) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.invite_or_assign_company_user(uuid, uuid, text, text, text) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.list_roles_and_permissions(uuid) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.record_audit_event(uuid, uuid, uuid, text, text, uuid, jsonb) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.list_audit_events(uuid, uuid, int) TO erp_app;
