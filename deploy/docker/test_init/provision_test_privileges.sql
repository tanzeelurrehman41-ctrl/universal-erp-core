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
REVOKE ALL ON erp_sales.sales_order, erp_sales.sales_order_line, 
              erp_sales.sales_invoice, erp_sales.sales_invoice_line, 
              erp_sales.outbox_event FROM erp_app;
REVOKE UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON erp_sales.customer FROM erp_app;
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

-- 6. Configure erp_app Least-Privilege Permissions (Authoritative 35-Table Matrix)
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
GRANT SELECT ON erp_inventory.warehouse, erp_inventory.stock_bin, erp_inventory.product, 
                erp_inventory.product_variant, erp_inventory.unit_of_measure, erp_inventory.stock_status TO erp_app;

-- erp_purchasing schema (2 tables)
GRANT SELECT, INSERT, UPDATE ON erp_purchasing.outbox_event TO erp_app;
GRANT SELECT ON erp_purchasing.goods_received_note TO erp_app;

-- erp_sales schema (Customer write is strictly disposable test-fixture adjustment; orders denied)
GRANT SELECT, INSERT ON erp_sales.customer TO erp_app; -- Disposable test-fixture seed adjustment only (Migration 015 unapplied)

-- finance schema (4 tables)
GRANT SELECT ON finance.fiscal_periods, finance.accounts TO erp_app;
GRANT SELECT, INSERT ON finance.journals, finance.journal_lines TO erp_app;

-- identity schema (Table-level grants)
GRANT SELECT, INSERT, UPDATE ON identity.devices TO erp_app; -- Migration 010 L6-7
GRANT SELECT, INSERT, UPDATE ON identity.company_memberships TO erp_app; -- Migration 012 L80-84

-- Column-Restricted Tables (4 tables)
GRANT SELECT (id, status) ON platform.tenants TO erp_app;
GRANT SELECT (id, status) ON identity.users TO erp_app;
GRANT SELECT (id, tenant_id, status) ON organization.companies TO erp_app;
GRANT SELECT (id, tenant_id, company_id, status) ON organization.locations TO erp_app;

-- Runtime Functions
GRANT EXECUTE ON FUNCTION set_config(text, text, boolean) TO erp_app;
GRANT EXECUTE ON FUNCTION pg_advisory_xact_lock(bigint) TO erp_app;
