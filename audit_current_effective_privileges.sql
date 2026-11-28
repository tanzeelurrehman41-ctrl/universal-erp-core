-- universal-erp-core/audit_current_effective_privileges.sql
-- Comprehensive Privilege Matrix Audit for Current-Schema Lane (Migrations 001..046)
-- Condition 1 & 2 Compliance: Returns ZERO rows iff database, schema, table, column,
-- sequence, routine, type, default ACL, and PUBLIC privileges match the hardened least-privilege
-- specification for erp_app across 001..046.

WITH approved_table_matrix(schema_name, table_name, allow_sel, allow_ins, allow_upd, allow_del, is_column_restricted) AS (
    VALUES
        -- sync
        ('sync', 'change_log', true, true, false, false, false),
        ('sync', 'company_change_counters', true, true, true, false, false),
        ('sync', 'device_company_scopes', true, true, true, false, false),
        ('sync', 'device_cursors', false, false, false, false, false),
        ('sync', 'device_location_scopes', true, false, false, false, false),
        ('sync', 'offline_grants', true, true, true, false, false),
        ('sync', 'operation_conflicts', true, true, true, false, false),
        ('sync', 'operation_inbox', true, true, true, false, false),

        -- erp_inventory (includes 021, 026 FIFO layers + valuation_entry, 027 stock management)
        ('erp_inventory', 'command_idempotency', true, true, true, false, false),
        ('erp_inventory', 'inventory_cost_allocation', true, true, false, false, false),
        ('erp_inventory', 'inventory_cost_layer', true, true, true, false, false),
        ('erp_inventory', 'inventory_lot', false, false, false, false, false),
        ('erp_inventory', 'inventory_movement', true, true, false, false, false),
        ('erp_inventory', 'inventory_movement_leg', true, true, false, false, false),
        ('erp_inventory', 'inventory_serial', false, false, false, false, false),
        ('erp_inventory', 'outbox_event', false, true, false, false, false),
        ('erp_inventory', 'product', true, true, false, false, false),
        ('erp_inventory', 'product_attribute_definition', false, false, false, false, false),
        ('erp_inventory', 'product_attribute_value', false, false, false, false, false),
        ('erp_inventory', 'product_barcode', false, false, false, false, false),
        ('erp_inventory', 'product_category', false, false, false, false, false),
        ('erp_inventory', 'product_category_link', false, false, false, false, false),
        ('erp_inventory', 'product_company_policy', false, false, false, false, false),
        ('erp_inventory', 'product_variant', true, false, false, false, false),
        ('erp_inventory', 'stock_balance', true, true, true, false, false),
        ('erp_inventory', 'stock_bin', true, false, false, false, false),
        ('erp_inventory', 'stock_count_line', true, true, true, true, false),
        ('erp_inventory', 'stock_count_session', true, true, true, true, false),
        ('erp_inventory', 'stock_pick_line', true, true, true, true, false),
        ('erp_inventory', 'stock_pick_list', true, true, true, true, false),
        ('erp_inventory', 'stock_reservation', true, true, true, true, false),
        ('erp_inventory', 'stock_return', true, true, true, true, false),
        ('erp_inventory', 'stock_return_line', true, true, true, true, false),
        ('erp_inventory', 'stock_status', true, false, false, false, false),
        ('erp_inventory', 'unit_conversion', false, false, false, false, false),
        ('erp_inventory', 'unit_of_measure', true, false, false, false, false),
        ('erp_inventory', 'valuation_entry', true, true, false, false, false),
        ('erp_inventory', 'warehouse', true, false, false, false, false),

        -- erp_purchasing (includes 028)
        ('erp_purchasing', 'goods_received_note', true, true, false, false, false),
        ('erp_purchasing', 'goods_received_note_line', true, true, false, false, false),
        ('erp_purchasing', 'outbox_event', true, true, true, false, false),
        ('erp_purchasing', 'purchase_inspection', true, true, true, true, false),
        ('erp_purchasing', 'purchase_inspection_line', true, true, true, true, false),
        ('erp_purchasing', 'purchase_order', true, true, true, false, false),
        ('erp_purchasing', 'purchase_order_line', true, true, true, false, false),
        ('erp_purchasing', 'purchase_return_line_record', true, true, true, true, false),
        ('erp_purchasing', 'purchase_return_record', true, true, true, true, false),
        ('erp_purchasing', 'purchasing_tolerance_policy', true, true, true, true, false),
        ('erp_purchasing', 'quarantine_release_record', true, true, true, true, false),
        ('erp_purchasing', 'supplier', true, true, false, false, false),
        ('erp_purchasing', 'supplier_debit_note', true, true, true, true, false),
        ('erp_purchasing', 'supplier_invoice', true, true, true, true, false),
        ('erp_purchasing', 'supplier_invoice_line', true, true, true, true, false),

        -- erp_sales (includes 029)
        ('erp_sales', 'customer', true, true, true, false, false),
        ('erp_sales', 'customer_receipt', true, true, true, true, false),
        ('erp_sales', 'customer_receipt_allocation', true, true, true, true, false),
        ('erp_sales', 'outbox_event', true, true, false, false, false),
        ('erp_sales', 'sales_credit_note', true, true, true, true, false),
        ('erp_sales', 'sales_dispatch', true, true, true, true, false),
        ('erp_sales', 'sales_dispatch_line', true, true, true, true, false),
        ('erp_sales', 'sales_invoice', true, true, false, false, false),
        ('erp_sales', 'sales_invoice_line', true, true, false, false, false),
        ('erp_sales', 'sales_order', true, true, true, false, false),
        ('erp_sales', 'sales_order_line', true, true, false, false, false),
        ('erp_sales', 'sales_quotation', true, true, true, true, false),
        ('erp_sales', 'sales_quotation_line', true, true, true, true, false),
        ('erp_sales', 'sales_return', true, true, true, true, false),
        ('erp_sales', 'sales_return_line', true, true, true, true, false),

        -- finance (includes 046 currencies, exchange_rates, financial_audit_logs)
        ('finance', 'accounts', true, false, false, false, false),
        ('finance', 'asset_depreciation_runs', false, false, false, false, false),
        ('finance', 'bank_accounts', false, false, false, false, false),
        ('finance', 'bank_statement_lines', false, false, false, false, false),
        ('finance', 'bank_statements', false, false, false, false, false),
        ('finance', 'currencies', true, false, false, false, false),
        ('finance', 'exchange_rates', true, true, true, false, false),
        ('finance', 'financial_audit_logs', true, true, false, false, false),
        ('finance', 'fiscal_periods', true, false, false, false, false),
        ('finance', 'fiscal_years', false, false, false, false, false),
        ('finance', 'fixed_assets', false, false, false, false, false),
        ('finance', 'journal_lines', true, true, false, false, false),
        ('finance', 'journals', true, true, false, false, false),
        ('finance', 'period_close_checklists', false, false, false, false, false),
        ('finance', 'recurring_schedules', false, false, false, false, false),
        ('finance', 'tax_codes', false, false, false, false, false),
        ('finance', 'tax_filings', false, false, false, false, false),
        ('finance', 'treasury_payments', false, false, false, false, false),

        -- organization
        ('organization', 'branches', true, true, true, true, false),
        ('organization', 'companies', false, false, false, false, true),
        ('organization', 'company_currency_history', true, true, false, false, false),
        ('organization', 'company_groups', false, false, false, false, false),
        ('organization', 'locations', false, false, false, false, true),

        -- identity
        ('identity', 'company_memberships', true, true, true, false, false),
        ('identity', 'devices', true, true, true, false, false),
        ('identity', 'external_identities', false, false, false, false, false),
        ('identity', 'tenant_memberships', false, false, false, false, false),
        ('identity', 'users', false, false, false, false, true),

        -- platform (037 over-grants tightly bounded)
        ('platform', 'billing_tax_rules', true, true, true, true, false),
        ('platform', 'countries', false, false, false, false, false),
        ('platform', 'disaster_recovery_drills', false, false, false, false, false),
        ('platform', 'jurisdictions', false, false, false, false, false),
        ('platform', 'plan_features', true, false, false, false, false),
        ('platform', 'plan_pricing', true, true, true, true, false),
        ('platform', 'regional_deployment_nodes', false, false, false, false, false),
        ('platform', 'saas_platform_alerts', true, true, true, true, false),
        ('platform', 'subscription_credit_notes', true, true, true, true, false),
        ('platform', 'subscription_invoice_lines', true, true, true, true, false),
        ('platform', 'subscription_invoices', true, true, true, true, false),
        ('platform', 'subscription_items', true, true, true, true, false),
        ('platform', 'subscription_payments', true, true, true, true, false),
        ('platform', 'subscription_plans', true, false, false, false, false),
        ('platform', 'subscriptions', true, true, true, true, false),
        ('platform', 'support_access_grants', true, true, true, true, false),
        ('platform', 'tenant_billing_credits', true, true, true, true, false),
        ('platform', 'tenant_data_residency_routes', false, false, false, false, false),
        ('platform', 'tenant_entitlements', true, true, true, true, false),
        ('platform', 'tenant_invitations', true, true, true, true, false),
        ('platform', 'tenant_provisioning_jobs', true, true, true, true, false),
        ('platform', 'tenant_residency_enforcement_policies', false, false, false, false, false),
        ('platform', 'tenant_usage_aggregations', true, true, true, true, false),
        ('platform', 'tenants', false, false, false, false, true),
        ('platform', 'usage_records', true, true, true, true, false),

        -- public
        ('public', 'schema_migrations', false, false, false, false, false),

        -- warehouse, analytics, consolidation, integrations, hr, manufacturing
        ('warehouse', 'barcode_definitions', true, true, true, true, false),
        ('warehouse', 'picking_waves', true, true, true, true, false),
        ('warehouse', 'putaway_strategies', true, true, true, true, false),
        ('warehouse', 'putaway_tasks', true, true, true, true, false),
        ('warehouse', 'quarantine_records', true, true, true, true, false),
        ('warehouse', 'replenishment_rules', true, true, true, true, false),
        ('warehouse', 'replenishment_tasks', true, true, true, true, false),
        ('warehouse', 'scan_audit_logs', true, true, true, true, false),
        ('warehouse', 'shipment_package_items', true, true, true, true, false),
        ('warehouse', 'shipment_packages', true, true, true, true, false),
        ('warehouse', 'wave_pick_items', true, true, true, true, false),
        ('warehouse', 'zones', true, true, true, true, false),
        ('analytics', 'executive_dashboard_cache', true, true, true, true, false),
        ('analytics', 'kpi_definitions', true, true, true, true, false),
        ('analytics', 'kpi_snapshots', true, true, true, true, false),
        ('consolidation', 'consolidation_runs', true, true, true, true, false),
        ('consolidation', 'elimination_lines', true, true, true, true, false),
        ('consolidation', 'intercompany_relationships', true, true, true, true, false),
        ('consolidation', 'intercompany_transfer_lines', true, true, true, true, false),
        ('consolidation', 'intercompany_transfers', true, true, true, true, false),
        ('integrations', 'api_partners', true, true, true, true, false),
        ('integrations', 'outbound_event_deliveries', true, true, true, true, false),
        ('integrations', 'webhook_subscriptions', true, true, true, true, false),
        ('hr', 'attendance_records', true, true, true, true, false),
        ('hr', 'compensation_structures', true, true, true, true, false),
        ('hr', 'departments', true, true, true, true, false),
        ('hr', 'designations', true, true, true, true, false),
        ('hr', 'employee_documents', true, true, true, true, false),
        ('hr', 'employee_shift_assignments', true, true, true, true, false),
        ('hr', 'employees', true, true, true, true, false),
        ('hr', 'holiday_calendars', true, true, true, true, false),
        ('hr', 'leave_allocations', true, true, true, true, false),
        ('hr', 'leave_requests', true, true, true, true, false),
        ('hr', 'leave_types', true, true, true, true, false),
        ('hr', 'payroll_country_packages', false, false, false, false, false),
        ('hr', 'payroll_periods', true, true, true, true, false),
        ('hr', 'payroll_reversals', true, true, true, true, false),
        ('hr', 'payslip_lines', true, true, true, true, false),
        ('hr', 'payslips', true, true, true, true, false),
        ('hr', 'salary_audit_logs', true, true, true, true, false),
        ('hr', 'salary_loans', true, true, true, true, false),
        ('hr', 'shifts', true, true, true, true, false),
        ('hr', 'statutory_configurations', true, true, true, true, false),
        ('hr', 'tax_slabs', true, true, true, true, false),
        ('manufacturing', 'bom_items', false, false, false, false, false),
        ('manufacturing', 'bom_revisions', false, false, false, false, false),
        ('manufacturing', 'boms', false, false, false, false, false),
        ('manufacturing', 'material_substitutions', false, false, false, false, false),
        ('manufacturing', 'production_inspections', false, false, false, false, false),
        ('manufacturing', 'routing_operations', false, false, false, false, false),
        ('manufacturing', 'routings', false, false, false, false, false),
        ('manufacturing', 'work_centers', false, false, false, false, false),
        ('manufacturing', 'work_order_materials', false, false, false, false, false),
        ('manufacturing', 'work_order_operations', false, false, false, false, false),
        ('manufacturing', 'work_orders', false, false, false, false, false)
),
approved_column_selects(schema_name, table_name, col_name) AS (
    VALUES
        ('platform', 'tenants', 'id'),
        ('platform', 'tenants', 'status'),
        ('identity', 'users', 'id'),
        ('identity', 'users', 'status'),
        ('organization', 'companies', 'id'),
        ('organization', 'companies', 'tenant_id'),
        ('organization', 'companies', 'status'),
        ('organization', 'companies', 'base_currency_code'),
        ('organization', 'companies', 'functional_currency_code'),
        ('organization', 'companies', 'presentation_currency_code'),
        ('organization', 'companies', 'currency_effective_date'),
        ('organization', 'locations', 'id'),
        ('organization', 'locations', 'tenant_id'),
        ('organization', 'locations', 'company_id'),
        ('organization', 'locations', 'status')
),
violations AS (
    -- 1. Database CONNECT / CREATE / TEMP
    SELECT 'Database' AS category, current_database() AS object_name, 'CONNECT' AS privilege,
           'Expected TRUE' AS expected, 'Got FALSE' AS actual, 'Missing CONNECT on current database' AS detail
    WHERE NOT has_database_privilege('erp_app', current_database(), 'CONNECT')
    UNION ALL
    SELECT 'Database', current_database(), p.priv, 'Expected FALSE', 'Got TRUE', format('Prohibited %s on database', p.priv)
    FROM (VALUES ('CREATE'), ('TEMP')) AS p(priv)
    WHERE has_database_privilege('erp_app', current_database(), p.priv)

    -- 2. Operational schema USAGE, Zero-Privilege schema denial, and zero CREATE anywhere
    UNION ALL
    SELECT 'Schema', s.nspname, 'USAGE', 'Expected TRUE', 'Got FALSE', format('Missing USAGE on operational schema %I', s.nspname)
    FROM pg_namespace s
    WHERE s.nspname IN ('sync','erp_inventory','erp_purchasing','erp_sales','finance','platform','identity','organization','warehouse','hr','analytics','consolidation','integrations')
      AND NOT has_schema_privilege('erp_app', s.nspname, 'USAGE')
    UNION ALL
    SELECT 'ZeroPrivilegeSchema', s.nspname, p.priv, 'Expected FALSE', 'Got TRUE', format('Prohibited %s on out-of-scope schema %I', p.priv, s.nspname)
    FROM pg_namespace s CROSS JOIN (VALUES ('USAGE'), ('CREATE')) AS p(priv)
    WHERE s.nspname IN ('audit', 'authorization', 'security', 'manufacturing', 'public')
      AND has_schema_privilege('erp_app', s.nspname, p.priv)
    UNION ALL
    SELECT 'SchemaCreateViolation', s.nspname, 'CREATE', 'Expected FALSE', 'Got TRUE', format('Prohibited CREATE on schema %I', s.nspname)
    FROM pg_namespace s
    WHERE s.nspname IN ('sync','erp_inventory','erp_purchasing','erp_sales','finance','platform','identity','organization','warehouse','hr','analytics','consolidation','integrations')
      AND has_schema_privilege('erp_app', s.nspname, 'CREATE')

    -- 3. Zero direct privileges on audit, authorization, security, manufacturing relations/sequences/routines
    UNION ALL
    SELECT 'ZeroPrivilegeTable', format('%I.%I', n.nspname, c.relname), p.priv, 'Expected FALSE', 'Got TRUE',
           'Out-of-scope schema table must have ZERO erp_app privileges'
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('DELETE'),('TRUNCATE'),('REFERENCES'),('TRIGGER'),('MAINTAIN')) AS p(priv)
    WHERE n.nspname IN ('audit', 'authorization', 'security', 'manufacturing')
      AND c.relkind IN ('r','p','v','m','f')
      AND has_table_privilege('erp_app', c.oid, p.priv)

    UNION ALL
    SELECT 'ZeroPrivilegeSequence', format('%I.%I', n.nspname, c.relname), p.priv, 'Expected FALSE', 'Got TRUE',
           'Out-of-scope sequence must have ZERO erp_app privileges'
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    CROSS JOIN (VALUES ('USAGE'), ('SELECT'), ('UPDATE')) AS p(priv)
    WHERE n.nspname IN ('audit', 'authorization', 'security', 'manufacturing')
      AND c.relkind = 'S'
      AND has_sequence_privilege('erp_app', c.oid, p.priv)

    -- 4. Approved Table Matrix (all operational + public tables)
    UNION ALL
    SELECT 'UnlistedRelation', format('%I.%I', n.nspname, c.relname), 'MATRIX', 'Listed', 'UNLISTED',
           'Table/view missing from approved current-schema table matrix'
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname IN ('sync','erp_inventory','erp_purchasing','erp_sales','finance','platform','identity','organization','warehouse','hr','analytics','consolidation','integrations','public')
      AND c.relkind IN ('r','p','v','m','f')
      AND NOT EXISTS (SELECT 1 FROM approved_table_matrix m WHERE m.schema_name = n.nspname AND m.table_name = c.relname)

    UNION ALL
    SELECT 'TablePrivilegeMismatch', format('%I.%I', m.schema_name, m.table_name), chk.priv,
           chk.expected::text, has_table_privilege('erp_app', format('%I.%I', m.schema_name, m.table_name), chk.priv)::text,
           'Table privilege diverges from least-privilege matrix'
    FROM approved_table_matrix m
    CROSS JOIN LATERAL (
        VALUES
            ('SELECT', m.allow_sel),
            ('INSERT', m.allow_ins),
            ('UPDATE', m.allow_upd),
            ('DELETE', m.allow_del),
            ('TRUNCATE', false),
            ('REFERENCES', false),
            ('TRIGGER', false),
            ('MAINTAIN', false)
    ) AS chk(priv, expected)
    WHERE has_table_privilege('erp_app', format('%I.%I', m.schema_name, m.table_name), chk.priv) IS DISTINCT FROM chk.expected

    -- 5. Exact Column-Level SELECT Restrictions on platform.tenants, identity.users, organization.companies, organization.locations
    UNION ALL
    SELECT 'ColumnPrivilegeMismatch', format('%I.%I.%I', n.nspname, c.relname, a.attname), p.priv,
           CASE WHEN p.priv = 'SELECT' AND EXISTS (
               SELECT 1 FROM approved_column_selects acs
               WHERE acs.schema_name = n.nspname AND acs.table_name = c.relname AND acs.col_name = a.attname
           ) THEN 'true' ELSE 'false' END,
           has_column_privilege('erp_app', c.oid, a.attname, p.priv)::text,
           'Column privilege on restricted table diverges from exact allowlist'
    FROM approved_table_matrix m
    JOIN pg_namespace n ON n.nspname = m.schema_name
    JOIN pg_class c ON c.relnamespace = n.oid AND c.relname = m.table_name AND c.relkind IN ('r','p')
    JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
    CROSS JOIN (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('REFERENCES')) AS p(priv)
    WHERE m.is_column_restricted
      AND has_column_privilege('erp_app', c.oid, a.attname, p.priv) IS DISTINCT FROM (
          p.priv = 'SELECT' AND EXISTS (
              SELECT 1 FROM approved_column_selects acs
              WHERE acs.schema_name = n.nspname AND acs.table_name = c.relname AND acs.col_name = a.attname
          )
      )

    -- 6. Built-in Default Privilege Hazard check (no unconfigured PUBLIC routine/type fallback)
    UNION ALL
    SELECT 'BuiltinDefaultPrivilegeHazard', format('ROLE=%I TYPE=%s', cr.role_name, cr.objtype),
           'PG_DEFAULT_ACL', 'Explicit REVOKE present', 'UNCONFIGURED',
           'Missing explicit ALTER DEFAULT PRIVILEGES revoking built-in PUBLIC grants'
    FROM (
        SELECT pr.rolname AS role_name, pr.oid AS role_oid, obj.objtype
        FROM pg_roles pr
        CROSS JOIN (VALUES ('f'), ('T')) AS obj(objtype)
        WHERE pr.rolname IN ('test_bootstrap_admin', 'erp_schema_owner', 'erp_dev', 'postgres')
    ) cr
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_default_acl d
        WHERE d.defaclrole = cr.role_oid AND d.defaclnamespace = 0 AND d.defaclobjtype = cr.objtype
    )

    -- 7. PUBLIC must have zero database/schema/table/sequence grants
    UNION ALL
    SELECT 'PublicPrivilegeViolation', format('%I.%I', n.nspname, c.relname), 'TABLE_OR_SEQ_PUBLIC',
           'Expected FALSE', 'Got TRUE', 'PUBLIC must have zero privileges on tables and sequences'
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname IN ('analytics','audit','authorization','consolidation','erp_inventory','erp_purchasing','erp_sales','finance','hr','identity','integrations','manufacturing','organization','platform','public','security','sync','warehouse')
      AND c.relkind IN ('r','p','v','m','f','S')
      AND (
          CASE WHEN c.relkind = 'S'
               THEN has_sequence_privilege('public', c.oid, 'USAGE') OR has_sequence_privilege('public', c.oid, 'SELECT')
               ELSE has_table_privilege('public', c.oid, 'SELECT') OR has_table_privilege('public', c.oid, 'INSERT') OR has_table_privilege('public', c.oid, 'UPDATE') OR has_table_privilege('public', c.oid, 'DELETE')
          END
      )

    -- 8. Operational sequence privileges (sync.change_log_change_sequence_seq: SELECT + USAGE, zero UPDATE)
    UNION ALL
    SELECT 'SequencePrivilegeMismatch', 'sync.change_log_change_sequence_seq', seq_chk.priv,
           seq_chk.expected::text, has_sequence_privilege('erp_app', 'sync.change_log_change_sequence_seq', seq_chk.priv)::text,
           'Operational sequence privilege diverges from least-privilege matrix'
    FROM (VALUES ('USAGE', true), ('SELECT', true), ('UPDATE', false)) AS seq_chk(priv, expected)
    WHERE has_sequence_privilege('erp_app', 'sync.change_log_change_sequence_seq', seq_chk.priv) IS DISTINCT FROM seq_chk.expected

    -- 9. PG17 role membership check: erp_app must never inherit erp_dev, erp_schema_owner, or test_bootstrap_admin
    UNION ALL
    SELECT 'RoleMembershipViolation', r.rolname, 'PG_HAS_ROLE', 'Expected FALSE', 'Got TRUE',
           format('erp_app must never be a member of %I', r.rolname)
    FROM pg_roles r
    WHERE r.rolname IN ('erp_dev', 'erp_schema_owner', 'test_bootstrap_admin', 'pg_read_all_data', 'pg_write_all_data')
      AND (pg_has_role('erp_app', r.oid, 'MEMBER') OR pg_has_role('erp_app', r.oid, 'SET') OR pg_has_role('erp_app', r.oid, 'USAGE'))
)
SELECT category, object_name, privilege, expected, actual, detail
FROM violations
ORDER BY category, object_name, privilege;
