-- =============================================================================
-- Dedicated Row Level Security (RLS) Configuration & Policy Audit (Revision 15)
-- Audits pg_class.relrowsecurity, pg_class.relforcerowsecurity, and pg_policy
-- definitions against migrations 001-019 baseline specifications across all domain schemas.
-- Detects missing tables, missing policies, unexpected extra permissive policies,
-- divergent command/mode/role settings, and invalid tenant session expressions.
-- Returns 0 rows on pass. Any returned row represents an RLS defect.
-- =============================================================================

WITH 
-- 1. Complete Expected Baseline RLS Table Inventory (Reconciled 42 Tables from Migrations 001-019)
expected_rls_tables (schemaname, tablename, require_rls, require_force, expected_polname, expected_cmd, expected_permissive, expected_roles, expected_using, expected_check) AS (
    VALUES
    -- organization domain (M001 L343-345)
    ('organization', 'locations',       true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),
    ('organization', 'companies',       true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),
    ('organization', 'company_groups',  true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),

    -- platform domain (M001 L336-339)
    ('platform', 'tenant_entitlements', true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),
    ('platform', 'usage_records',       true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),
    ('platform', 'subscriptions',       true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),
    ('platform', 'subscription_items',  true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),

    -- identity domain (M001 L340-342)
    ('identity', 'tenant_memberships',  true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),
    ('identity', 'company_memberships', true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),
    ('identity', 'devices',             true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),

    -- authorization domain (M001 L346-349)
    ('authorization', 'roles',                        true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),
    ('authorization', 'role_permissions',             true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),
    ('authorization', 'membership_role_assignments',  true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),
    ('authorization', 'location_access',              true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),

    -- audit domain (M001 L350)
    ('audit', 'audit_events',           true, true, 'tenant_isolation', '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),

    -- erp_inventory domain (M003 L13-14)
    ('erp_inventory', 'command_idempotency', true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),

    -- sync domain (M004 L137-138, M008 L15-17, M018 L30-35)
    ('sync', 'device_company_scopes',   true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('sync', 'device_location_scopes',  true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('sync', 'offline_grants',          true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('sync', 'operation_inbox',         true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('sync', 'device_cursors',          true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('sync', 'change_log',              true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('sync', 'company_change_counters', true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('sync', 'operation_conflicts',     true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),

    -- erp_purchasing domain (M011 L170-171)
    ('erp_purchasing', 'supplier',                 true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('erp_purchasing', 'purchase_order',           true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('erp_purchasing', 'purchase_order_line',      true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('erp_purchasing', 'goods_received_note',      true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('erp_purchasing', 'goods_received_note_line', true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('erp_purchasing', 'outbox_event',             true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),

    -- finance domain (M013 L266, L281-284)
    ('finance', 'fiscal_years',   true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('finance', 'fiscal_periods', true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('finance', 'accounts',       true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('finance', 'journals',       true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('finance', 'journal_lines',  true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('finance', 'exchange_rates', true, true, 'tenant_scope',         '*', true, '{0}', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)', '(tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid)'),

    -- erp_sales domain (M015 L183-188: all 6 sales tables)
    ('erp_sales', 'customer',           true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('erp_sales', 'sales_order',        true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('erp_sales', 'sales_order_line',   true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('erp_sales', 'sales_invoice',      true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('erp_sales', 'sales_invoice_line', true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))'),
    ('erp_sales', 'outbox_event',       true, true, 'tenant_company_scope', '*', true, '{0}', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))', '((tenant_id = (NULLIF(current_setting(''app.tenant_id''::text, true), ''''::text))::uuid) AND (company_id = (NULLIF(current_setting(''app.company_id''::text, true), ''''::text))::uuid))')
),

rls_violations AS (
    -- CHECK 1: Missing Expected Tables
    SELECT 'RlsTableMissing' AS category, format('%I.%I', exp.schemaname, exp.tablename) AS object_name,
           'TABLE_EXISTS' AS attribute, 'Expected EXISTS' AS expected, 'Got NULL' AS actual,
           'Mandatory multi-tenant RLS table is missing from catalog' AS detail
    FROM expected_rls_tables exp
    LEFT JOIN pg_namespace n ON n.nspname = exp.schemaname
    LEFT JOIN pg_class c ON c.relname = exp.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    WHERE c.oid IS NULL

    -- CHECK 2: relrowsecurity Enforcement
    UNION ALL
    SELECT 'RlsStatusMismatch', format('%I.%I', exp.schemaname, exp.tablename),
           'relrowsecurity', format('Expected %s', exp.require_rls), format('Got %s', c.relrowsecurity),
           'Row Level Security flag mismatch on multi-tenant table'
    FROM expected_rls_tables exp
    JOIN pg_namespace n ON n.nspname = exp.schemaname
    JOIN pg_class c ON c.relname = exp.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    WHERE c.relrowsecurity <> exp.require_rls

    -- CHECK 3: relforcerowsecurity Enforcement
    UNION ALL
    SELECT 'RlsForceMismatch', format('%I.%I', exp.schemaname, exp.tablename),
           'relforcerowsecurity', format('Expected %s', exp.require_force), format('Got %s', c.relforcerowsecurity),
           'FORCE Row Level Security flag mismatch on multi-tenant table'
    FROM expected_rls_tables exp
    JOIN pg_namespace n ON n.nspname = exp.schemaname
    JOIN pg_class c ON c.relname = exp.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    WHERE c.relforcerowsecurity <> exp.require_force

    -- CHECK 4: Policy Count & Unexpected Policy Detection
    UNION ALL
    SELECT 'UnexpectedPolicyCount', format('%I.%I', exp.schemaname, exp.tablename),
           'POLICY_COUNT', 'Expected 1', format('Got %s', count(p.oid)),
           'Multiple or additional permissive policies detected; threatens tenant isolation'
    FROM expected_rls_tables exp
    JOIN pg_namespace n ON n.nspname = exp.schemaname
    JOIN pg_class c ON c.relname = exp.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    LEFT JOIN pg_policy p ON p.polrelid = c.oid
    GROUP BY exp.schemaname, exp.tablename
    HAVING count(p.oid) <> 1

    -- CHECK 5: Policy Definitions (Name, Command, Mode, Roles, Expressions)
    UNION ALL
    SELECT 'PolicyDefinitionMismatch', format('%I.%I.%I', exp.schemaname, exp.tablename, p.polname),
           'POLICY_NAME', format('Expected %I', exp.expected_polname), format('Got %I', p.polname),
           'Policy name on table diverges from migration specification'
    FROM expected_rls_tables exp
    JOIN pg_namespace n ON n.oid = (SELECT oid FROM pg_namespace WHERE nspname = exp.schemaname)
    JOIN pg_class c ON c.relname = exp.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    JOIN pg_policy p ON p.polrelid = c.oid
    WHERE p.polname <> exp.expected_polname

    UNION ALL
    SELECT 'PolicyDefinitionMismatch', format('%I.%I.%I', exp.schemaname, exp.tablename, p.polname),
           'POLCMD', format('Expected %s', exp.expected_cmd), format('Got %s', p.polcmd),
           'Policy command setting diverges from migration specification'
    FROM expected_rls_tables exp
    JOIN pg_namespace n ON n.oid = (SELECT oid FROM pg_namespace WHERE nspname = exp.schemaname)
    JOIN pg_class c ON c.relname = exp.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    JOIN pg_policy p ON p.polrelid = c.oid
    WHERE p.polcmd <> exp.expected_cmd

    UNION ALL
    SELECT 'PolicyDefinitionMismatch', format('%I.%I.%I', exp.schemaname, exp.tablename, p.polname),
           'POLPERMISSIVE', format('Expected %s', exp.expected_permissive), format('Got %s', p.polpermissive),
           'Policy permissive/restrictive mode diverges from migration specification'
    FROM expected_rls_tables exp
    JOIN pg_namespace n ON n.oid = (SELECT oid FROM pg_namespace WHERE nspname = exp.schemaname)
    JOIN pg_class c ON c.relname = exp.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    JOIN pg_policy p ON p.polrelid = c.oid
    WHERE p.polpermissive IS DISTINCT FROM exp.expected_permissive

    UNION ALL
    SELECT 'PolicyDefinitionMismatch', format('%I.%I.%I', exp.schemaname, exp.tablename, p.polname),
           'POLROLES', format('Expected %s', exp.expected_roles), format('Got %s', p.polroles::text),
           'Policy role array diverges from migration specification'
    FROM expected_rls_tables exp
    JOIN pg_namespace n ON n.oid = (SELECT oid FROM pg_namespace WHERE nspname = exp.schemaname)
    JOIN pg_class c ON c.relname = exp.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    JOIN pg_policy p ON p.polrelid = c.oid
    WHERE p.polroles::text IS DISTINCT FROM exp.expected_roles

    UNION ALL
    SELECT 'PolicyDefinitionMismatch', format('%I.%I.%I', exp.schemaname, exp.tablename, p.polname),
           'USING_EXPR', format('Expected %s', exp.expected_using), format('Got %s', coalesce(pg_get_expr(p.polqual, p.polrelid), '<NULL>')),
           'Policy USING expression does not match approved deparsed tenant session predicate'
    FROM expected_rls_tables exp
    JOIN pg_namespace n ON n.oid = (SELECT oid FROM pg_namespace WHERE nspname = exp.schemaname)
    JOIN pg_class c ON c.relname = exp.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    JOIN pg_policy p ON p.polrelid = c.oid
    WHERE pg_get_expr(p.polqual, p.polrelid) IS DISTINCT FROM exp.expected_using

    UNION ALL
    SELECT 'PolicyDefinitionMismatch', format('%I.%I.%I', exp.schemaname, exp.tablename, p.polname),
           'WITH_CHECK_EXPR', format('Expected %s', exp.expected_check), format('Got %s', coalesce(pg_get_expr(p.polwithcheck, p.polrelid), '<NULL>')),
           'Policy WITH CHECK expression does not match approved deparsed tenant session predicate'
    FROM expected_rls_tables exp
    JOIN pg_namespace n ON n.oid = (SELECT oid FROM pg_namespace WHERE nspname = exp.schemaname)
    JOIN pg_class c ON c.relname = exp.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    JOIN pg_policy p ON p.polrelid = c.oid
    WHERE pg_get_expr(p.polwithcheck, p.polrelid) IS DISTINCT FROM exp.expected_check

    -- CHECK 6: Role Bypass Attributes
    UNION ALL
    SELECT 'RoleBypassRls', 'erp_app', 'rolbypassrls', 'Expected FALSE', 'Got TRUE',
           'CRITICAL: erp_app has BYPASSRLS enabled! Tenant isolation completely defeated.'
    FROM pg_roles WHERE rolname = 'erp_app' AND rolbypassrls = true
    UNION ALL
    SELECT 'RoleBypassRls', 'erp_dev', 'rolbypassrls', 'Expected TRUE', 'Got FALSE',
           'erp_dev missing BYPASSRLS; fixture teardown will be blocked by RLS policies.'
    FROM pg_roles WHERE rolname = 'erp_dev' AND rolbypassrls = false
)
SELECT category, object_name, attribute, expected, actual, detail
FROM rls_violations
ORDER BY category, object_name;
