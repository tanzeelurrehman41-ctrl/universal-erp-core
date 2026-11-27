-- universal-erp-core/deploy/docker/test_init/provision_current_test_privileges.sql
-- Current-Schema Disposable Test Lane (Migrations 001..046)
-- Executed under test_bootstrap_admin in universal_erp_current_disposable_test ONLY.
--
-- Security Architecture (Condition 1):
--   1. Dedicated erp_schema_owner (NOLOGIN, NOSUPERUSER, NOBYPASSRLS) owns schemas, tables,
--      sequences, trigger functions, types, domains, and non-bypass SECURITY DEFINER routines.
--   2. erp_dev (BYPASSRLS) is NOT granted blanket object ownership. It receives only test-fixture
--      DML privileges plus ownership of the three individually justified pre-auth / unscoped RBAC
--      SECURITY DEFINER functions that execute without session app.tenant_id.
--   3. erp_app (NOBYPASSRLS) receives strict least-privilege table/column/sequence privileges and
--      explicit per-signature EXECUTE on the 14 approved identity.* SECURITY DEFINER routines.

\connect universal_erp_current_disposable_test

-- 1. Safety Guard: Fail-closed if connected to wrong database
DO $$
BEGIN
    IF current_database() <> 'universal_erp_current_disposable_test' THEN
        RAISE EXCEPTION 'Refusing to provision current-schema permissions: current database is % (expected universal_erp_current_disposable_test)', current_database();
    END IF;
END $$;

-- 2. Ensure Non-Bypass Schema Owner Role Exists
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_schema_owner') THEN
        CREATE ROLE erp_schema_owner WITH NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT;
    ELSE
        ALTER ROLE erp_schema_owner WITH NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT;
    END IF;
END $$;

-- Preserve erp_schema_owner as database & public schema owner and restrict public/database defaults
ALTER DATABASE universal_erp_current_disposable_test OWNER TO erp_schema_owner;
ALTER SCHEMA public OWNER TO erp_schema_owner;
REVOKE CREATE, TEMP ON DATABASE universal_erp_current_disposable_test FROM PUBLIC;
REVOKE CREATE, TEMP ON DATABASE universal_erp_current_disposable_test FROM erp_app;
GRANT CONNECT ON DATABASE universal_erp_current_disposable_test TO erp_app;
GRANT CONNECT ON DATABASE universal_erp_current_disposable_test TO erp_dev;

-- 3. Neutralize PUBLIC & Clear Inherited erp_app / erp_dev Routine Grants
REVOKE ALL ON SCHEMA public FROM PUBLIC, erp_app;
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM PUBLIC, erp_app CASCADE;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM PUBLIC, erp_app CASCADE;
REVOKE ALL ON ALL ROUTINES IN SCHEMA public FROM PUBLIC, erp_app CASCADE;

DO $$
DECLARE
    s text;
    r RECORD;
    rol text;
    all_app_schemas CONSTANT text[] := ARRAY[
        'analytics', 'audit', 'authorization', 'consolidation',
        'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance',
        'hr', 'identity', 'integrations', 'manufacturing',
        'organization', 'platform', 'security', 'sync', 'warehouse'
    ];
    sensitive_schemas CONSTANT text[] := ARRAY['audit', 'authorization', 'security'];
    operational_schemas CONSTANT text[] := ARRAY[
        'analytics', 'consolidation', 'erp_inventory', 'erp_purchasing',
        'erp_sales', 'finance', 'hr', 'identity', 'integrations',
        'manufacturing', 'organization', 'platform', 'sync', 'warehouse'
    ];
BEGIN
    -- Revoke all PUBLIC & erp_app privileges across every schema, table, sequence, and routine
    FOREACH s IN ARRAY all_app_schemas LOOP
        EXECUTE format('REVOKE ALL ON SCHEMA %I FROM PUBLIC, erp_app CASCADE;', s);
        EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA %I FROM PUBLIC, erp_app CASCADE;', s);
        EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA %I FROM PUBLIC, erp_app CASCADE;', s);
        EXECUTE format('REVOKE ALL ON ALL ROUTINES IN SCHEMA %I FROM PUBLIC, erp_app CASCADE;', s);
    END LOOP;

    -- Explicitly neutralize built-in PostgreSQL PUBLIC grants on custom types/domains
    FOR r IN
        SELECT n.nspname, t.typname
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE (n.nspname = ANY(all_app_schemas) OR n.nspname = 'public')
          AND t.typisdefined
          AND t.typelem = 0
    LOOP
        EXECUTE format('REVOKE ALL ON TYPE %I.%I FROM PUBLIC, erp_app CASCADE;', r.nspname, r.typname);
        EXECUTE format('GRANT USAGE ON TYPE %I.%I TO erp_dev;', r.nspname, r.typname);
        IF NOT (r.nspname = ANY(sensitive_schemas)) THEN
            EXECUTE format('GRANT USAGE ON TYPE %I.%I TO erp_app;', r.nspname, r.typname);
        END IF;
    END LOOP;

    -- Global Default Privileges (defaclnamespace = 0): eliminate built-in PUBLIC grants
    FOR rol IN SELECT rolname FROM pg_roles WHERE rolname IN ('test_bootstrap_admin', 'erp_schema_owner', 'erp_dev', 'postgres') ORDER BY rolname LOOP
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON TABLES FROM PUBLIC, erp_app;', rol);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON SEQUENCES FROM PUBLIC, erp_app;', rol);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON ROUTINES FROM PUBLIC, erp_app;', rol);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON TYPES FROM PUBLIC, erp_app;', rol);

        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT ALL ON TABLES TO erp_dev;', rol);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT ALL ON SEQUENCES TO erp_dev;', rol);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT ALL ON ROUTINES TO erp_dev;', rol);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I GRANT ALL ON TYPES TO erp_dev;', rol);
    END LOOP;

    -- Per-Schema Default Privileges in Operational Schemas + public
    FOREACH s IN ARRAY (operational_schemas || ARRAY['public']) LOOP
        FOR rol IN SELECT rolname FROM pg_roles WHERE rolname IN ('test_bootstrap_admin', 'erp_schema_owner', 'erp_dev', 'postgres') ORDER BY rolname LOOP
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

    -- Per-Schema Default Privileges in Zero-Privilege Sensitive Schemas (audit, authorization, security)
    FOREACH s IN ARRAY sensitive_schemas LOOP
        FOR rol IN SELECT rolname FROM pg_roles WHERE rolname IN ('test_bootstrap_admin', 'erp_schema_owner', 'erp_dev', 'postgres') ORDER BY rolname LOOP
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON TABLES FROM PUBLIC, erp_app;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON SEQUENCES FROM PUBLIC, erp_app;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON ROUTINES FROM PUBLIC, erp_app;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON TYPES FROM PUBLIC, erp_app;', rol, s);

            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON TABLES TO erp_dev;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON SEQUENCES TO erp_dev;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT ALL ON ROUTINES TO erp_dev;', rol, s);
            EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT USAGE ON TYPES TO erp_dev;', rol, s);
        END LOOP;
    END LOOP;

    -- Verify erp_schema_owner owns all application schemas, tables, sequences, and SECURITY INVOKER routines
    -- (Migrations 020..046 run under SET ROLE erp_schema_owner after pg_restore --no-owner --role=erp_schema_owner.)
    FOREACH s IN ARRAY all_app_schemas LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO erp_dev;', s);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO erp_dev;', s);
        EXECUTE format('GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA %I TO erp_dev;', s);
        EXECUTE format('GRANT EXECUTE ON ALL ROUTINES IN SCHEMA %I TO erp_dev;', s);
    END LOOP;
    GRANT USAGE ON SCHEMA public TO erp_dev;
    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO erp_dev;
    GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO erp_dev;
    GRANT EXECUTE ON ALL ROUTINES IN SCHEMA public TO erp_dev;
END $$;

-- 4. Individually Justified SECURITY DEFINER Ownership Exceptions (Condition 1)
-- Only pre-tenant / unscoped RBAC SECURITY DEFINER routines that must query FORCE RLS tables
-- prior to session app.tenant_id assignment are assigned OWNER TO erp_dev (BYPASSRLS).
-- All other 11 SECURITY DEFINER routines remain owned by erp_schema_owner (NOBYPASSRLS).
ALTER FUNCTION identity.list_user_company_contexts(text, text) OWNER TO erp_dev;
ALTER FUNCTION identity.resolve_authenticated_user_context(uuid, uuid, text, text) OWNER TO erp_dev;
ALTER FUNCTION identity.user_has_permission(uuid, uuid, uuid, text) OWNER TO erp_dev;

-- 5. Least-Privilege Schema USAGE for erp_app (Strictly excludes audit, authorization, security, public)
GRANT USAGE ON SCHEMA
    sync, erp_inventory, erp_purchasing, erp_sales, finance,
    platform, identity, organization,
    warehouse, hr, analytics, consolidation, integrations
TO erp_app;

-- 6. Least-Privilege Table, Column, and Sequence Grants for erp_app (Migrations 001..046)

-- 6A. sync
GRANT SELECT, INSERT ON sync.change_log TO erp_app;
GRANT SELECT, USAGE ON SEQUENCE sync.change_log_change_sequence_seq TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.company_change_counters TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.device_company_scopes TO erp_app;
GRANT SELECT ON sync.device_location_scopes TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.offline_grants TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.operation_conflicts TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.operation_inbox TO erp_app;

-- 6B. erp_inventory (includes Migration 021, 026 FIFO layers & valuation_entry, 027 stock ops)
GRANT SELECT ON
    erp_inventory.warehouse,
    erp_inventory.stock_bin,
    erp_inventory.stock_status,
    erp_inventory.unit_of_measure,
    erp_inventory.product_variant
TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.product TO erp_app;
GRANT SELECT, INSERT, UPDATE ON
    erp_inventory.command_idempotency,
    erp_inventory.stock_balance,
    erp_inventory.inventory_cost_layer
TO erp_app;
GRANT SELECT, INSERT ON
    erp_inventory.inventory_movement,
    erp_inventory.inventory_movement_leg,
    erp_inventory.inventory_cost_allocation,
    erp_inventory.valuation_entry
TO erp_app;
GRANT INSERT ON erp_inventory.outbox_event TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON
    erp_inventory.stock_reservation,
    erp_inventory.stock_pick_list,
    erp_inventory.stock_pick_line,
    erp_inventory.stock_count_session,
    erp_inventory.stock_count_line,
    erp_inventory.stock_return,
    erp_inventory.stock_return_line
TO erp_app;

-- 6C. erp_purchasing (includes Migration 028)
GRANT SELECT, INSERT ON
    erp_purchasing.supplier,
    erp_purchasing.goods_received_note,
    erp_purchasing.goods_received_note_line
TO erp_app;
GRANT SELECT, INSERT, UPDATE ON
    erp_purchasing.purchase_order,
    erp_purchasing.purchase_order_line,
    erp_purchasing.outbox_event
TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON
    erp_purchasing.purchase_inspection,
    erp_purchasing.purchase_inspection_line,
    erp_purchasing.purchase_return_line_record,
    erp_purchasing.purchase_return_record,
    erp_purchasing.purchasing_tolerance_policy,
    erp_purchasing.quarantine_release_record,
    erp_purchasing.supplier_debit_note,
    erp_purchasing.supplier_invoice,
    erp_purchasing.supplier_invoice_line
TO erp_app;

-- 6D. erp_sales (includes Migration 029)
GRANT SELECT, INSERT, UPDATE ON
    erp_sales.customer,
    erp_sales.sales_order
TO erp_app;
GRANT SELECT, INSERT ON
    erp_sales.sales_order_line,
    erp_sales.sales_invoice,
    erp_sales.sales_invoice_line,
    erp_sales.outbox_event
TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON
    erp_sales.customer_receipt,
    erp_sales.customer_receipt_allocation,
    erp_sales.sales_credit_note,
    erp_sales.sales_dispatch,
    erp_sales.sales_dispatch_line,
    erp_sales.sales_quotation,
    erp_sales.sales_quotation_line,
    erp_sales.sales_return,
    erp_sales.sales_return_line
TO erp_app;

-- 6E. finance (includes Migration 046 currencies, exchange_rates, financial_audit_logs)
GRANT SELECT ON
    finance.accounts,
    finance.fiscal_periods,
    finance.currencies
TO erp_app;
GRANT SELECT, INSERT ON
    finance.journal_lines,
    finance.journals,
    finance.financial_audit_logs
TO erp_app;
GRANT SELECT, INSERT, UPDATE ON finance.exchange_rates TO erp_app;

-- 6F. organization (strict column-restricted SELECT on companies & locations + 036 branches + 046 history)
GRANT SELECT (id, tenant_id, status, base_currency_code, functional_currency_code, presentation_currency_code, currency_effective_date)
    ON organization.companies TO erp_app;
GRANT SELECT (id, tenant_id, company_id, status)
    ON organization.locations TO erp_app;
GRANT SELECT, INSERT ON organization.company_currency_history TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON organization.branches TO erp_app;

-- 6G. identity (strict column-restricted SELECT on users + membership/device tables)
GRANT SELECT (id, status) ON identity.users TO erp_app;
GRANT SELECT, INSERT, UPDATE ON
    identity.company_memberships,
    identity.devices
TO erp_app;

-- 6H. platform (reverses Migration 037 ALL TABLES over-grant; strict column-restricted tenants & read-only plans)
GRANT SELECT (id, status) ON platform.tenants TO erp_app;
GRANT SELECT ON
    platform.subscription_plans,
    platform.plan_features
TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON
    platform.billing_tax_rules,
    platform.plan_pricing,
    platform.saas_platform_alerts,
    platform.subscription_credit_notes,
    platform.subscription_invoice_lines,
    platform.subscription_invoices,
    platform.subscription_items,
    platform.subscription_payments,
    platform.subscriptions,
    platform.support_access_grants,
    platform.tenant_billing_credits,
    platform.tenant_entitlements,
    platform.tenant_invitations,
    platform.tenant_provisioning_jobs,
    platform.tenant_usage_aggregations,
    platform.usage_records
TO erp_app;

-- 6I. warehouse, hr, analytics, consolidation, integrations (Migrations 032..036)
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA warehouse TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA analytics TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA consolidation TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA integrations TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA hr TO erp_app;
REVOKE ALL ON hr.payroll_country_packages FROM erp_app;

-- 7. Per-Signature EXECUTE Grants on Individually Approved identity.* SECURITY DEFINER Routines
-- Exact signatures only; PUBLIC remains completely revoked.
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
GRANT EXECUTE ON FUNCTION identity.list_audit_events(uuid, uuid, integer) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.update_company_currency_settings(uuid, uuid, uuid, character, character, date, boolean, text) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.list_company_currency_history(uuid, uuid, integer) TO erp_app;
GRANT EXECUTE ON FUNCTION identity.list_company_exchange_rates(uuid, uuid, character, character, text) TO erp_app;
