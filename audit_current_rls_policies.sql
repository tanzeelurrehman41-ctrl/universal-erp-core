-- universal-erp-core/audit_current_rls_policies.sql
-- Comprehensive Row-Level Security Audit for Current-Schema Lane (Migrations 001..046)
-- Condition 1 & 2 Compliance: Returns ZERO rows iff every multi-tenant table in 001..046
-- enforces ENABLE + FORCE RLS with exact migration-approved policies, while approved
-- exemption tables keep RLS disabled without hidden permissive policies.

WITH expected_rls_tables(schemaname, tablename, policy_mode) AS (
    VALUES
        -- tenant + company strict scope
        ('analytics', 'executive_dashboard_cache', 'tenant_company_scope'),
        ('analytics', 'kpi_definitions', 'tenant_company_scope'),
        ('analytics', 'kpi_snapshots', 'tenant_company_scope'),
        ('consolidation', 'intercompany_relationships', 'intercompany_tenant_write_company_read'),
        ('consolidation', 'intercompany_transfers', 'intercompany_tenant_write_company_read'),
        ('erp_inventory', 'inventory_cost_allocation', 'tenant_company_scope'),
        ('erp_inventory', 'inventory_cost_layer', 'tenant_company_scope'),
        ('erp_inventory', 'inventory_lot', 'tenant_company_scope'),
        ('erp_inventory', 'inventory_movement', 'tenant_company_scope'),
        ('erp_inventory', 'inventory_movement_leg', 'tenant_company_scope'),
        ('erp_inventory', 'inventory_serial', 'tenant_company_scope'),
        ('erp_inventory', 'outbox_event', 'tenant_company_scope'),
        ('erp_inventory', 'product_company_policy', 'tenant_company_scope'),
        ('erp_inventory', 'stock_balance', 'tenant_company_scope'),
        ('erp_inventory', 'stock_bin', 'tenant_company_scope'),
        ('erp_inventory', 'stock_count_line', 'tenant_company_scope'),
        ('erp_inventory', 'stock_count_session', 'tenant_company_scope'),
        ('erp_inventory', 'stock_pick_line', 'tenant_company_scope'),
        ('erp_inventory', 'stock_pick_list', 'tenant_company_scope'),
        ('erp_inventory', 'stock_reservation', 'tenant_company_scope'),
        ('erp_inventory', 'stock_return', 'tenant_company_scope'),
        ('erp_inventory', 'stock_return_line', 'tenant_company_scope'),
        ('erp_inventory', 'valuation_entry', 'tenant_company_scope'),
        ('erp_inventory', 'warehouse', 'tenant_company_scope'),
        ('erp_purchasing', 'goods_received_note', 'tenant_company_scope'),
        ('erp_purchasing', 'goods_received_note_line', 'tenant_company_scope'),
        ('erp_purchasing', 'outbox_event', 'tenant_company_scope'),
        ('erp_purchasing', 'purchase_inspection', 'tenant_company_scope'),
        ('erp_purchasing', 'purchase_inspection_line', 'tenant_company_scope'),
        ('erp_purchasing', 'purchase_order', 'tenant_company_scope'),
        ('erp_purchasing', 'purchase_order_line', 'tenant_company_scope'),
        ('erp_purchasing', 'purchase_return_line_record', 'tenant_company_scope'),
        ('erp_purchasing', 'purchase_return_record', 'tenant_company_scope'),
        ('erp_purchasing', 'purchasing_tolerance_policy', 'tenant_company_scope'),
        ('erp_purchasing', 'quarantine_release_record', 'tenant_company_scope'),
        ('erp_purchasing', 'supplier', 'tenant_company_scope'),
        ('erp_purchasing', 'supplier_debit_note', 'tenant_company_scope'),
        ('erp_purchasing', 'supplier_invoice', 'tenant_company_scope'),
        ('erp_purchasing', 'supplier_invoice_line', 'tenant_company_scope'),
        ('erp_sales', 'customer', 'tenant_company_scope'),
        ('erp_sales', 'customer_receipt', 'tenant_company_scope'),
        ('erp_sales', 'customer_receipt_allocation', 'tenant_company_scope'),
        ('erp_sales', 'outbox_event', 'tenant_company_scope'),
        ('erp_sales', 'sales_credit_note', 'tenant_company_scope'),
        ('erp_sales', 'sales_dispatch', 'tenant_company_scope'),
        ('erp_sales', 'sales_dispatch_line', 'tenant_company_scope'),
        ('erp_sales', 'sales_invoice', 'tenant_company_scope'),
        ('erp_sales', 'sales_invoice_line', 'tenant_company_scope'),
        ('erp_sales', 'sales_order', 'tenant_company_scope'),
        ('erp_sales', 'sales_order_line', 'tenant_company_scope'),
        ('erp_sales', 'sales_quotation', 'tenant_company_scope'),
        ('erp_sales', 'sales_quotation_line', 'tenant_company_scope'),
        ('erp_sales', 'sales_return', 'tenant_company_scope'),
        ('erp_sales', 'sales_return_line', 'tenant_company_scope'),
        ('finance', 'accounts', 'tenant_company_scope'),
        ('finance', 'asset_depreciation_runs', 'tenant_company_scope'),
        ('finance', 'bank_accounts', 'tenant_company_scope'),
        ('finance', 'bank_statement_lines', 'tenant_company_scope'),
        ('finance', 'bank_statements', 'tenant_company_scope'),
        ('finance', 'financial_audit_logs', 'tenant_company_scope'),
        ('finance', 'fiscal_periods', 'tenant_company_scope'),
        ('finance', 'fiscal_years', 'tenant_company_scope'),
        ('finance', 'fixed_assets', 'tenant_company_scope'),
        ('finance', 'journal_lines', 'tenant_company_scope'),
        ('finance', 'journals', 'tenant_company_scope'),
        ('finance', 'period_close_checklists', 'tenant_company_scope'),
        ('finance', 'recurring_schedules', 'tenant_company_scope'),
        ('finance', 'tax_codes', 'tenant_company_scope'),
        ('finance', 'tax_filings', 'tenant_company_scope'),
        ('finance', 'treasury_payments', 'tenant_company_scope'),
        ('hr', 'attendance_records', 'tenant_company_scope'),
        ('hr', 'compensation_structures', 'tenant_company_scope'),
        ('hr', 'departments', 'tenant_company_scope'),
        ('hr', 'designations', 'tenant_company_scope'),
        ('hr', 'employee_documents', 'tenant_company_scope'),
        ('hr', 'employee_shift_assignments', 'tenant_company_scope'),
        ('hr', 'employees', 'tenant_company_scope'),
        ('hr', 'holiday_calendars', 'tenant_company_scope'),
        ('hr', 'leave_allocations', 'tenant_company_scope'),
        ('hr', 'leave_requests', 'tenant_company_scope'),
        ('hr', 'leave_types', 'tenant_company_scope'),
        ('hr', 'payroll_periods', 'tenant_company_scope'),
        ('hr', 'payroll_reversals', 'tenant_company_scope'),
        ('hr', 'payslip_lines', 'tenant_company_scope'),
        ('hr', 'payslips', 'tenant_company_scope'),
        ('hr', 'salary_audit_logs', 'tenant_company_scope'),
        ('hr', 'salary_loans', 'tenant_company_scope'),
        ('hr', 'shifts', 'tenant_company_scope'),
        ('hr', 'statutory_configurations', 'tenant_company_scope'),
        ('hr', 'tax_slabs', 'tenant_company_scope'),
        ('integrations', 'api_partners', 'tenant_company_scope'),
        ('integrations', 'webhook_subscriptions', 'tenant_company_scope'),
        ('manufacturing', 'bom_items', 'tenant_company_scope'),
        ('manufacturing', 'bom_revisions', 'tenant_company_scope'),
        ('manufacturing', 'boms', 'tenant_company_scope'),
        ('manufacturing', 'material_substitutions', 'tenant_company_scope'),
        ('manufacturing', 'production_inspections', 'tenant_company_scope'),
        ('manufacturing', 'routing_operations', 'tenant_company_scope'),
        ('manufacturing', 'routings', 'tenant_company_scope'),
        ('manufacturing', 'work_centers', 'tenant_company_scope'),
        ('manufacturing', 'work_order_materials', 'tenant_company_scope'),
        ('manufacturing', 'work_order_operations', 'tenant_company_scope'),
        ('manufacturing', 'work_orders', 'tenant_company_scope'),
        ('organization', 'branches', 'tenant_company_scope'),
        ('sync', 'change_log', 'tenant_company_scope'),
        ('sync', 'company_change_counters', 'tenant_company_scope'),
        ('sync', 'device_company_scopes', 'tenant_company_scope'),
        ('sync', 'device_cursors', 'tenant_company_scope'),
        ('sync', 'device_location_scopes', 'tenant_company_scope'),
        ('sync', 'offline_grants', 'tenant_company_scope'),
        ('sync', 'operation_conflicts', 'tenant_company_scope'),
        ('sync', 'operation_inbox', 'tenant_company_scope'),
        ('warehouse', 'barcode_definitions', 'tenant_company_scope'),
        ('warehouse', 'picking_waves', 'tenant_company_scope'),
        ('warehouse', 'putaway_strategies', 'tenant_company_scope'),
        ('warehouse', 'putaway_tasks', 'tenant_company_scope'),
        ('warehouse', 'quarantine_records', 'tenant_company_scope'),
        ('warehouse', 'replenishment_rules', 'tenant_company_scope'),
        ('warehouse', 'replenishment_tasks', 'tenant_company_scope'),
        ('warehouse', 'scan_audit_logs', 'tenant_company_scope'),
        ('warehouse', 'shipment_package_items', 'tenant_company_scope'),
        ('warehouse', 'shipment_packages', 'tenant_company_scope'),
        ('warehouse', 'wave_pick_items', 'tenant_company_scope'),
        ('warehouse', 'zones', 'tenant_company_scope'),

        -- tenant-only scope (global to tenant across companies)
        ('audit', 'audit_events', 'tenant_only'),
        ('authorization', 'location_access', 'tenant_only'),
        ('authorization', 'membership_role_assignments', 'tenant_only'),
        ('authorization', 'role_permissions', 'tenant_only'),
        ('authorization', 'roles', 'tenant_only'),
        ('consolidation', 'consolidation_runs', 'tenant_only_erp_app'),
        ('consolidation', 'elimination_lines', 'tenant_only_erp_app'),
        ('consolidation', 'intercompany_transfer_lines', 'tenant_only_erp_app'),
        ('erp_inventory', 'command_idempotency', 'tenant_only'),
        ('erp_inventory', 'product', 'tenant_only'),
        ('erp_inventory', 'product_attribute_definition', 'tenant_only'),
        ('erp_inventory', 'product_attribute_value', 'tenant_only'),
        ('erp_inventory', 'product_barcode', 'tenant_only'),
        ('erp_inventory', 'product_category', 'tenant_only'),
        ('erp_inventory', 'product_category_link', 'tenant_only'),
        ('erp_inventory', 'product_variant', 'tenant_only'),
        ('erp_inventory', 'stock_status', 'tenant_only'),
        ('erp_inventory', 'unit_conversion', 'tenant_only'),
        ('erp_inventory', 'unit_of_measure', 'tenant_only'),
        ('identity', 'company_memberships', 'tenant_only'),
        ('identity', 'devices', 'tenant_only'),
        ('identity', 'tenant_memberships', 'tenant_only'),
        ('integrations', 'outbound_event_deliveries', 'tenant_only_erp_app'),
        ('organization', 'companies', 'tenant_only'),
        ('organization', 'company_groups', 'tenant_only'),
        ('organization', 'locations', 'tenant_only'),
        ('platform', 'subscription_credit_notes', 'tenant_only'),
        ('platform', 'subscription_invoices', 'tenant_only'),
        ('platform', 'subscription_items', 'tenant_only'),
        ('platform', 'subscription_payments', 'tenant_only'),
        ('platform', 'subscriptions', 'tenant_only'),
        ('platform', 'support_access_grants', 'tenant_only'),
        ('platform', 'tenant_billing_credits', 'tenant_only'),
        ('platform', 'tenant_entitlements', 'tenant_only'),
        ('platform', 'tenant_invitations', 'tenant_only'),
        ('platform', 'tenant_usage_aggregations', 'tenant_only'),
        ('platform', 'usage_records', 'tenant_only'),

        -- optional company_id within tenant scope (046)
        ('finance', 'exchange_rates', 'tenant_optional_company'),
        ('organization', 'company_currency_history', 'tenant_optional_company'),

        -- platform alerts (tenant_id IS NULL or matching tenant)
        ('platform', 'saas_platform_alerts', 'tenant_or_null')
),
approved_non_rls_tables(schemaname, tablename) AS (
    VALUES
        ('audit', 'audit_chain_checkpoints'),
        ('audit', 'tamper_evident_event_log'),
        ('authorization', 'permissions'),
        ('finance', 'currencies'),
        ('hr', 'payroll_country_packages'),
        ('identity', 'external_identities'),
        ('identity', 'users'),
        ('platform', 'billing_tax_rules'),
        ('platform', 'countries'),
        ('platform', 'disaster_recovery_drills'),
        ('platform', 'jurisdictions'),
        ('platform', 'plan_features'),
        ('platform', 'plan_pricing'),
        ('platform', 'regional_deployment_nodes'),
        ('platform', 'subscription_invoice_lines'),
        ('platform', 'subscription_plans'),
        ('platform', 'tenant_data_residency_routes'),
        ('platform', 'tenant_provisioning_jobs'),
        ('platform', 'tenant_residency_enforcement_policies'),
        ('platform', 'tenants'),
        ('public', 'schema_migrations'),
        ('security', 'api_rate_limit_states'),
        ('security', 'privileged_support_sessions'),
        ('security', 'webhook_endpoints')
),
app_schemas(nspname) AS (
    VALUES
        ('analytics'), ('audit'), ('authorization'), ('consolidation'),
        ('erp_inventory'), ('erp_purchasing'), ('erp_sales'), ('finance'),
        ('hr'), ('identity'), ('integrations'), ('manufacturing'),
        ('organization'), ('platform'), ('public'), ('security'), ('sync'), ('warehouse')
),
rls_violations AS (
    -- 1. Unlisted table
    SELECT 'UnlistedTable' AS category, format('%I.%I', n.nspname, c.relname) AS object_name,
           'INVENTORY' AS attribute, 'Expected in RLS or non-RLS catalog' AS expected, 'UNLISTED' AS actual,
           'Table missing from current-schema RLS audit specification' AS detail
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname IN (SELECT nspname FROM app_schemas)
      AND c.relkind IN ('r', 'p')
      AND NOT EXISTS (SELECT 1 FROM expected_rls_tables e WHERE e.schemaname = n.nspname AND e.tablename = c.relname)
      AND NOT EXISTS (SELECT 1 FROM approved_non_rls_tables a WHERE a.schemaname = n.nspname AND a.tablename = c.relname)

    -- 2. Missing expected RLS table
    UNION ALL
    SELECT 'MissingRlsTable', format('%I.%I', e.schemaname, e.tablename), 'EXISTS', 'TRUE', 'FALSE',
           'Expected multi-tenant RLS table does not exist'
    FROM expected_rls_tables e
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = e.schemaname AND c.relname = e.tablename AND c.relkind IN ('r', 'p')
    )

    -- 3. ENABLE + FORCE RLS required on all expected_rls_tables
    UNION ALL
    SELECT 'RlsDisabledViolation', format('%I.%I', e.schemaname, e.tablename), 'relrowsecurity',
           'Expected TRUE', c.relrowsecurity::text, 'Row Level Security disabled on multi-tenant table'
    FROM expected_rls_tables e
    JOIN pg_namespace n ON n.nspname = e.schemaname
    JOIN pg_class c ON c.relnamespace = n.oid AND c.relname = e.tablename AND c.relkind IN ('r', 'p')
    WHERE NOT c.relrowsecurity

    UNION ALL
    SELECT 'ForceRlsDisabledViolation', format('%I.%I', e.schemaname, e.tablename), 'relforcerowsecurity',
           'Expected TRUE', c.relforcerowsecurity::text, 'FORCE ROW LEVEL SECURITY missing on multi-tenant table'
    FROM expected_rls_tables e
    JOIN pg_namespace n ON n.nspname = e.schemaname
    JOIN pg_class c ON c.relnamespace = n.oid AND c.relname = e.tablename AND c.relkind IN ('r', 'p')
    WHERE NOT c.relforcerowsecurity

    -- 4. Policy count = 1, permissive = true, cmd = '*', expected USING/WITH CHECK semantics
    UNION ALL
    SELECT 'UnexpectedPolicyCount', format('%I.%I', e.schemaname, e.tablename), 'POLICY_COUNT',
           'Expected 1', format('Got %s', count(p.oid)), 'Table must have exactly 1 active RLS policy'
    FROM expected_rls_tables e
    JOIN pg_namespace n ON n.nspname = e.schemaname
    JOIN pg_class c ON c.relnamespace = n.oid AND c.relname = e.tablename AND c.relkind IN ('r', 'p')
    LEFT JOIN pg_policy p ON p.polrelid = c.oid
    GROUP BY e.schemaname, e.tablename
    HAVING count(p.oid) <> 1

    UNION ALL
    SELECT 'PolicyExpressionViolation', format('%I.%I.%I', e.schemaname, e.tablename, p.polname), 'POL_EXPR',
           e.policy_mode, coalesce(pg_get_expr(p.polqual, p.polrelid), '<NULL>'),
           'RLS policy USING/WITH CHECK does not enforce expected app.tenant_id / app.company_id predicate'
    FROM expected_rls_tables e
    JOIN pg_namespace n ON n.nspname = e.schemaname
    JOIN pg_class c ON c.relnamespace = n.oid AND c.relname = e.tablename AND c.relkind IN ('r', 'p')
    JOIN pg_policy p ON p.polrelid = c.oid
    WHERE p.polcmd <> '*'
       OR NOT p.polpermissive
       OR coalesce(pg_get_expr(p.polqual, p.polrelid), '') NOT LIKE '%app.tenant_id%'
       OR coalesce(pg_get_expr(p.polwithcheck, p.polrelid), pg_get_expr(p.polqual, p.polrelid), '') NOT LIKE '%app.tenant_id%'
       OR (e.policy_mode = 'tenant_company_scope' AND (
           coalesce(pg_get_expr(p.polqual, p.polrelid), '') NOT LIKE '%app.company_id%'
           OR coalesce(pg_get_expr(p.polwithcheck, p.polrelid), pg_get_expr(p.polqual, p.polrelid), '') NOT LIKE '%app.company_id%'
       ))
       OR (e.policy_mode = 'intercompany_tenant_write_company_read' AND (
           coalesce(pg_get_expr(p.polqual, p.polrelid), '') NOT LIKE '%app.company_id%'
       ))
       OR NOT (
           p.polroles::text = '{0}'
           OR (
               cardinality(p.polroles) = 1
               AND EXISTS (SELECT 1 FROM pg_roles r WHERE r.oid = ANY(p.polroles) AND r.rolname = 'erp_app')
           )
       )

    -- 5. Non-bypass check on erp_app and erp_schema_owner; fixture bypass check on erp_dev
    UNION ALL
    SELECT 'RoleBypassRls', rolname, 'rolbypassrls', 'Expected FALSE', 'Got TRUE',
           format('CRITICAL: Role %I must NOT have BYPASSRLS', rolname)
    FROM pg_roles
    WHERE rolname IN ('erp_app', 'erp_schema_owner') AND rolbypassrls = true

    UNION ALL
    SELECT 'RoleBypassRls', 'erp_dev', 'rolbypassrls', 'Expected TRUE', 'Got FALSE',
           'erp_dev requires BYPASSRLS strictly for test fixture setup/teardown and the 3 documented pre-auth routines'
    FROM pg_roles
    WHERE rolname = 'erp_dev' AND rolbypassrls = false
)
SELECT category, object_name, attribute, expected, actual, detail
FROM rls_violations
ORDER BY category, object_name;
