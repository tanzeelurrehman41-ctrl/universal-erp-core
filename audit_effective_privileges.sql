-- =============================================================================
-- Comprehensive Effective Privilege Audit Query for erp_app (Revision 14)
-- Audits approved relations, unlisted relations (tables, partitions, views, foreign tables),
-- all 8 table privileges on all relations (including column-restricted tables),
-- column-level SELECT/write/references privileges on both core and restricted tables,
-- sequences, functions, database privileges (TEMP=false), schemas, PG17 dynamic role escalation
-- (including rolreplication), and PUBLIC defaults (with correct boolean grouping).
-- Returns 0 rows on pass. Any returned row represents a privilege violation.
-- =============================================================================

WITH RECURSIVE 
-- 1. Recursive Role Ancestry with PostgreSQL 17 Multi-Hop Delegation Semantics
role_ancestry AS (
    SELECT m.roleid, m.member, m.inherit_option, m.set_option, 1 AS depth,
           ARRAY[r_mem.rolname::text, r_role.rolname::text] AS path
    FROM pg_auth_members m
    JOIN pg_roles r_mem ON m.member = r_mem.oid
    JOIN pg_roles r_role ON m.roleid = r_role.oid
    WHERE r_mem.rolname = 'erp_app'
    UNION ALL
    SELECT m.roleid, m.member, (ra.inherit_option AND m.inherit_option), (ra.set_option AND m.set_option), ra.depth + 1,
           ra.path || r_role.rolname::text
    FROM pg_auth_members m
    JOIN pg_roles r_role ON m.roleid = r_role.oid
    JOIN role_ancestry ra ON m.member = ra.roleid
),

-- 2. Approved Table Privilege Allowlist (31 Core Application Tables)
approved_table_matrix (schemaname, tablename, allow_select, allow_insert, allow_update, allow_delete, allow_truncate, allow_references, allow_trigger, allow_maintain) AS (
    VALUES
    -- sync schema (7 tables)
    ('sync', 'operation_inbox',          true,  true,  true,  false, false, false, false, false),
    ('sync', 'operation_conflicts',      true,  true,  true,  false, false, false, false, false),
    ('sync', 'change_log',                true,  true,  false, false, false, false, false, false),
    ('sync', 'company_change_counters',  true,  true,  true,  false, false, false, false, false),
    ('sync', 'device_company_scopes',    true,  false, false, false, false, false, false, false),
    ('sync', 'device_location_scopes',   true,  false, false, false, false, false, false, false),
    ('sync', 'offline_grants',           true,  false, false, false, false, false, false, false),

    -- erp_inventory schema (11 tables)
    ('erp_inventory', 'stock_balance',          true,  true,  true,  false, false, false, false, false),
    ('erp_inventory', 'inventory_movement',      true,  true,  false, false, false, false, false, false),
    ('erp_inventory', 'inventory_movement_leg',  true,  true,  false, false, false, false, false, false),
    ('erp_inventory', 'command_idempotency',     true,  true,  true,  false, false, false, false, false),
    ('erp_inventory', 'outbox_event',            false, true,  false, false, false, false, false, false),
    ('erp_inventory', 'warehouse',               true,  false, false, false, false, false, false, false),
    ('erp_inventory', 'stock_bin',               true,  false, false, false, false, false, false, false),
    ('erp_inventory', 'product',                 true,  false, false, false, false, false, false, false),
    ('erp_inventory', 'product_variant',         true,  false, false, false, false, false, false, false),
    ('erp_inventory', 'unit_of_measure',         true,  false, false, false, false, false, false, false),
    ('erp_inventory', 'stock_status',            true,  false, false, false, false, false, false, false),

    -- erp_purchasing schema (2 tables)
    ('erp_purchasing', 'outbox_event',           true,  true,  true,  false, false, false, false, false),
    ('erp_purchasing', 'goods_received_note',    true,  false, false, false, false, false, false, false),

    -- erp_sales schema (6 tables: customer read-only; 5 order/invoice tables strictly denied)
    ('erp_sales', 'customer',                    true,  false, false, false, false, false, false, false),
    ('erp_sales', 'sales_order',                 false, false, false, false, false, false, false, false),
    ('erp_sales', 'sales_order_line',            false, false, false, false, false, false, false, false),
    ('erp_sales', 'sales_invoice',               false, false, false, false, false, false, false, false),
    ('erp_sales', 'sales_invoice_line',          false, false, false, false, false, false, false, false),
    ('erp_sales', 'outbox_event',                false, false, false, false, false, false, false, false),

    -- finance schema (4 tables)
    ('finance', 'fiscal_periods',                true,  false, false, false, false, false, false, false),
    ('finance', 'accounts',                      true,  false, false, false, false, false, false, false),
    ('finance', 'journals',                      true,  true,  false, false, false, false, false, false),
    ('finance', 'journal_lines',                  true,  true,  false, false, false, false, false, false),

    -- identity schema (1 table-level entry)
    ('identity', 'devices',                      true,  false, false, false, false, false, false, false)
),

-- 3. Complete Per-Column Allowlist for the 4 Column-Restricted Tables
approved_column_matrix (schemaname, tablename, colname, allow_select, allow_insert, allow_update, allow_references) AS (
    VALUES
    ('platform', 'tenants', 'id',         true,  false, false, false),
    ('platform', 'tenants', 'status',     true,  false, false, false),
    ('identity', 'users',   'id',         true,  false, false, false),
    ('identity', 'users',   'status',     true,  false, false, false),
    ('organization', 'companies', 'id',        true,  false, false, false),
    ('organization', 'companies', 'tenant_id', true,  false, false, false),
    ('organization', 'companies', 'status',    true,  false, false, false),
    ('organization', 'locations', 'id',         true,  false, false, false),
    ('organization', 'locations', 'tenant_id',  true,  false, false, false),
    ('organization', 'locations', 'company_id', true,  false, false, false),
    ('organization', 'locations', 'status',     true,  false, false, false)
),

audit_violations AS (
    -- CATEGORY 1: Database Privileges (CONNECT allowed; CREATE & TEMP denied)
    SELECT 'Database' AS category, current_database() AS object_name, 'CONNECT' AS privilege,
           'Expected TRUE' AS expected, 'Got FALSE' AS actual, 'Missing required CONNECT privilege on database' AS detail
    WHERE NOT has_database_privilege('erp_app', current_database(), 'CONNECT')
    UNION ALL
    SELECT 'Database', current_database(), 'CREATE', 'Expected FALSE', 'Got TRUE', 'Prohibited CREATE privilege on database'
    WHERE has_database_privilege('erp_app', current_database(), 'CREATE')
    UNION ALL
    SELECT 'Database', current_database(), 'TEMP', 'Expected FALSE', 'Got TRUE', 'Prohibited TEMP privilege on database'
    WHERE has_database_privilege('erp_app', current_database(), 'TEMP')

    -- CATEGORY 2: Schema Privileges (USAGE required on 8 domain schemas; USAGE strictly denied on authorization and audit; CREATE denied on all 10 schemas + public)
    UNION ALL
    SELECT 'Schema', s.nspname, 'USAGE', 'Expected TRUE', 'Got FALSE', format('Missing required USAGE on schema %I', s.nspname)
    FROM pg_namespace s
    WHERE s.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization')
      AND NOT has_schema_privilege('erp_app', s.nspname, 'USAGE')
    UNION ALL
    SELECT 'Schema', s.nspname, 'USAGE', 'Expected FALSE', 'Got TRUE', format('Prohibited USAGE on out-of-scope schema %I (Zero-Privilege rule)', s.nspname)
    FROM pg_namespace s
    WHERE s.nspname IN ('authorization', 'audit')
      AND has_schema_privilege('erp_app', s.nspname, 'USAGE')
    UNION ALL
    SELECT 'Schema', s.nspname, 'CREATE', 'Expected FALSE', 'Got TRUE', format('Prohibited CREATE privilege on schema %I', s.nspname)
    FROM pg_namespace s
    WHERE s.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit', 'public')
      AND has_schema_privilege('erp_app', s.nspname, 'CREATE')

    -- CATEGORY 2B: Zero Privileges on authorization and audit Schemas (Comprehensive Security Audit)
    UNION ALL
    SELECT 'ZeroPrivilegeSchemaViolation', format('SCHEMA: %I', n.nspname), p.priv, 'Expected FALSE', 'Got TRUE',
           format('erp_app (or inherited role) has prohibited %s privilege on out-of-scope schema %I', p.priv, n.nspname)
    FROM pg_namespace n
    CROSS JOIN (VALUES ('USAGE'), ('CREATE')) AS p(priv)
    WHERE n.nspname IN ('authorization', 'audit')
      AND has_schema_privilege('erp_app', n.nspname, p.priv)
    UNION ALL
    SELECT 'ZeroPrivilegeTableViolation', format('TABLE: %I.%I', n.nspname, c.relname), p.priv, 'Expected FALSE', 'Got TRUE',
           format('erp_app (or inherited role) has prohibited %s privilege on out-of-scope table %I.%I', p.priv, n.nspname, c.relname)
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    CROSS JOIN LATERAL (
        VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('DELETE'), ('TRUNCATE'), ('REFERENCES'), ('TRIGGER'), ('MAINTAIN')
    ) AS p(priv)
    WHERE n.nspname IN ('authorization', 'audit')
      AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
      AND has_table_privilege('erp_app', c.oid, p.priv)
    UNION ALL
    SELECT 'ZeroPrivilegeSequenceViolation', format('SEQUENCE: %I.%I', n.nspname, c.relname), p.priv, 'Expected FALSE', 'Got TRUE',
           format('erp_app (or inherited role) has prohibited %s privilege on out-of-scope sequence %I.%I', p.priv, n.nspname, c.relname)
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    CROSS JOIN (VALUES ('USAGE'), ('SELECT'), ('UPDATE')) AS p(priv)
    WHERE n.nspname IN ('authorization', 'audit')
      AND c.relkind = 'S'
      AND has_sequence_privilege('erp_app', c.oid, p.priv)
    UNION ALL
    SELECT 'ZeroPrivilegeRoutineViolation', format('ROUTINE: %I.%I', n.nspname, pr.proname), 'EXECUTE', 'Expected FALSE', 'Got TRUE',
           format('erp_app (or inherited role) has prohibited EXECUTE on out-of-scope routine %I.%I', n.nspname, pr.proname)
    FROM pg_proc pr
    JOIN pg_namespace n ON n.oid = pr.pronamespace
    WHERE n.nspname IN ('authorization', 'audit')
      AND has_function_privilege('erp_app', pr.oid, 'EXECUTE')
    UNION ALL
    SELECT 'ZeroPrivilegeTypeViolation', format('TYPE: %I.%I', n.nspname, t.typname), 'USAGE', 'Expected FALSE', 'Got TRUE',
           format('erp_app (or inherited role) has prohibited USAGE on out-of-scope type %I.%I', n.nspname, t.typname)
    FROM pg_type t
    JOIN pg_namespace n ON n.oid = t.typnamespace
    WHERE n.nspname IN ('authorization', 'audit')
      AND t.typisdefined
      AND t.typelem = 0
      AND has_type_privilege('erp_app', t.oid, 'USAGE')
    UNION ALL
    SELECT 'ZeroPrivilegeColumnViolation', format('COLUMN: %I.%I.%I', n.nspname, c.relname, a.attname), p.priv, 'Expected FALSE', 'Got TRUE',
           format('erp_app (or inherited role) has prohibited %s on out-of-scope column %I.%I.%I', p.priv, n.nspname, c.relname, a.attname)
    FROM pg_attribute a
    JOIN pg_class c ON c.oid = a.attrelid
    JOIN pg_namespace n ON n.oid = c.relnamespace
    CROSS JOIN (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('REFERENCES')) AS p(priv)
    WHERE n.nspname IN ('authorization', 'audit')
      AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
      AND a.attnum > 0 AND NOT a.attisdropped
      AND has_column_privilege('erp_app', c.oid, a.attname, p.priv)

    -- Category 2B: Sensitive Schemas & Global Default Privileges Audit (pg_default_acl inspection)
    UNION ALL
    SELECT 'ZeroPrivilegeDefaultAclViolation',
           format('DEFAULT_ACL: %s IN %s (creator: %I)', 
                  CASE d.defaclobjtype 
                      WHEN 'r' THEN 'TABLES' 
                      WHEN 'S' THEN 'SEQUENCES' 
                      WHEN 'f' THEN 'ROUTINES' 
                      WHEN 'T' THEN 'TYPES' 
                      ELSE d.defaclobjtype::text 
                  END,
                  CASE WHEN d.defaclnamespace = 0 THEN 'GLOBAL (defaclnamespace=0)' ELSE format('SCHEMA %I', n.nspname) END,
                  r.rolname),
           acl.privilege_type,
           'Expected REVOKED',
           'Got GRANTED',
           format('Default privilege %s on %s in %s (creator: %I) granted to %s', 
                  acl.privilege_type, 
                  d.defaclobjtype, 
                  CASE WHEN d.defaclnamespace = 0 THEN 'GLOBAL' ELSE format('SCHEMA %I', n.nspname) END, 
                  r.rolname, 
                  coalesce(gr.rolname, 'PUBLIC'))
    FROM pg_default_acl d
    LEFT JOIN pg_namespace n ON n.oid = d.defaclnamespace
    JOIN pg_roles r ON r.oid = d.defaclrole
    CROSS JOIN LATERAL aclexplode(d.defaclacl) acl
    LEFT JOIN pg_roles gr ON gr.oid = acl.grantee
    WHERE (
        n.nspname IN ('authorization', 'audit')
        OR d.defaclnamespace = 0
    )
      AND (
        acl.grantee = 0 -- PUBLIC
        OR gr.rolname = 'erp_app'
        OR pg_has_role('erp_app', acl.grantee, 'MEMBER')
      )

    -- Category 2B-Builtin: PostgreSQL Built-in Default Privilege Hazard Audit
    UNION ALL
    SELECT 'BuiltinDefaultPrivilegeHazard',
           format('BUILTIN_DEFAULT_%s: %s (creator: %I)',
                  CASE cr.objtype WHEN 'f' THEN 'ROUTINES' WHEN 'T' THEN 'TYPES' END,
                  CASE WHEN cr.nspname IS NULL THEN 'GLOBAL (defaclnamespace=0)' ELSE format('SCHEMA %I', cr.nspname) END,
                  cr.role_name),
           CASE cr.objtype WHEN 'f' THEN 'EXECUTE' WHEN 'T' THEN 'USAGE' END,
           'Expected EXPLICIT REVOKE IN pg_default_acl',
           'Got UNCONFIGURED (FALLS BACK TO POSTGRESQL BUILT-IN PUBLIC GRANT)',
           format('Role %I lacks an explicit neutralizing default ACL for %s in %s; PostgreSQL built-in default will grant %s to PUBLIC on newly created objects!',
                  cr.role_name,
                  CASE cr.objtype WHEN 'f' THEN 'ROUTINES' WHEN 'T' THEN 'TYPES' END,
                  CASE WHEN cr.nspname IS NULL THEN 'GLOBAL' ELSE format('SCHEMA %I', cr.nspname) END,
                  CASE cr.objtype WHEN 'f' THEN 'EXECUTE' WHEN 'T' THEN 'USAGE' END)
    FROM (
        SELECT r.rolname AS role_name, r.oid AS role_oid, obj.objtype, sch.nspname, sch.nsp_oid
        FROM (VALUES ('test_bootstrap_admin'), ('erp_dev'), ('postgres')) AS r(rolname)
        JOIN pg_roles pr ON pr.rolname = r.rolname
        CROSS JOIN (VALUES ('f'), ('T')) AS obj(objtype)
        CROSS JOIN (
            VALUES (NULL::text, 0::oid),
                   ('authorization', (SELECT oid FROM pg_namespace WHERE nspname = 'authorization')),
                   ('audit', (SELECT oid FROM pg_namespace WHERE nspname = 'audit'))
        ) AS sch(nspname, nsp_oid)
        WHERE sch.nsp_oid IS NOT NULL
    ) cr
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_default_acl d
        WHERE d.defaclrole = cr.role_oid
          AND d.defaclnamespace = cr.nsp_oid
          AND d.defaclobjtype = cr.objtype
          AND NOT EXISTS (
              SELECT 1
              FROM aclexplode(d.defaclacl) x
              WHERE x.grantee = 0
                AND x.privilege_type = CASE cr.objtype WHEN 'f' THEN 'EXECUTE' WHEN 'T' THEN 'USAGE' END
          )
    )

    -- Direct PUBLIC Grant Audit on Sensitive Schema Objects
    UNION ALL
    SELECT 'ZeroPrivilegePublicGrantViolation', format('PUBLIC_%s: %I.%I', c.relkind, n.nspname, c.relname), acl.privilege_type, 'Expected REVOKED', 'Got GRANTED',
           format('Relation %I.%I in sensitive schema %I has privilege %s granted to PUBLIC', n.nspname, c.relname, n.nspname, acl.privilege_type)
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    CROSS JOIN LATERAL aclexplode(coalesce(c.relacl, acldefault(CASE c.relkind WHEN 'S' THEN 's' ELSE 'r' END, c.relowner))) acl
    WHERE n.nspname IN ('authorization', 'audit')
      AND acl.grantee = 0
    UNION ALL
    SELECT 'ZeroPrivilegePublicGrantViolation', format('PUBLIC_ROUTINE: %I.%I', n.nspname, p.proname), acl.privilege_type, 'Expected REVOKED', 'Got GRANTED',
           format('Routine %I.%I in sensitive schema %I has privilege %s granted to PUBLIC', n.nspname, p.proname, n.nspname, acl.privilege_type)
    FROM pg_proc p
    JOIN pg_namespace n ON n.oid = p.pronamespace
    CROSS JOIN LATERAL aclexplode(coalesce(p.proacl, acldefault('f', p.proowner))) acl
    WHERE n.nspname IN ('authorization', 'audit')
      AND acl.grantee = 0
    UNION ALL
    SELECT 'ZeroPrivilegePublicGrantViolation', format('PUBLIC_TYPE: %I.%I', n.nspname, t.typname), acl.privilege_type, 'Expected REVOKED', 'Got GRANTED',
           format('Type %I.%I in sensitive schema %I has privilege %s granted to PUBLIC', n.nspname, t.typname, n.nspname, acl.privilege_type)
    FROM pg_type t
    JOIN pg_namespace n ON n.oid = t.typnamespace
    CROSS JOIN LATERAL aclexplode(coalesce(t.typacl, acldefault('T', t.typowner))) acl
    WHERE n.nspname IN ('authorization', 'audit')
      AND t.typisdefined AND t.typelem = 0
      AND acl.grantee = 0

    -- CATEGORY 3: Approved Table Privileges (All 8 Privileges Evaluated on Core 31 Tables)
    UNION ALL
    SELECT 'ApprovedTable', format('%I.%I', m.schemaname, m.tablename), p.priv,
           format('Expected %s', p.expected), format('Got %s', has_table_privilege('erp_app', format('%I.%I', m.schemaname, m.tablename), p.priv)),
           format('Privilege %s mismatch on %I.%I', p.priv, m.schemaname, m.tablename)
    FROM approved_table_matrix m
    CROSS JOIN LATERAL (
        VALUES
        ('SELECT', m.allow_select), ('INSERT', m.allow_insert), ('UPDATE', m.allow_update),
        ('DELETE', m.allow_delete), ('TRUNCATE', m.allow_truncate), ('REFERENCES', m.allow_references),
        ('TRIGGER', m.allow_trigger), ('MAINTAIN', m.allow_maintain)
    ) AS p(priv, expected)
    WHERE has_table_privilege('erp_app', format('%I.%I', m.schemaname, m.tablename), p.priv) <> p.expected

    -- CATEGORY 3B: Table Existence & Privileges on the 4 Column-Restricted Tables
    UNION ALL
    SELECT 'ColumnRestrictedTableMissing', format('%I.%I', cr.schemaname, cr.tablename), 'TABLE_EXISTS',
           'Expected EXISTS', 'Got NULL', 'Mandatory column-restricted table is missing from catalog'
    FROM (VALUES 
        ('platform', 'tenants'),
        ('identity', 'users'),
        ('organization', 'companies'),
        ('organization', 'locations')
    ) AS cr(schemaname, tablename)
    LEFT JOIN pg_namespace n ON n.nspname = cr.schemaname
    LEFT JOIN pg_class c ON c.relname = cr.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    WHERE c.oid IS NULL

    UNION ALL
    SELECT 'ColumnRestrictedTableLevel', format('%I.%I', n.nspname, c.relname), p.priv,
           'Expected FALSE', 'Got TRUE', format('Table-level privilege %s prohibited on column-restricted table', p.priv)
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    CROSS JOIN (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('DELETE'), ('TRUNCATE'), ('REFERENCES'), ('TRIGGER'), ('MAINTAIN')) AS p(priv)
    WHERE format('%I.%I', n.nspname, c.relname) IN ('platform.tenants', 'identity.users', 'organization.companies', 'organization.locations')
      AND has_table_privilege('erp_app', c.oid, p.priv)

    -- CATEGORY 3C: Column-Level Privilege Audit on Core Tables
    UNION ALL
    SELECT 'CoreTableColumnSelect', format('%I.%I.%I', m.schemaname, m.tablename, a.attname), 'SELECT',
           'Expected FALSE', 'Got TRUE', 'Prohibited column-level SELECT on read-denied core table'
    FROM approved_table_matrix m
    JOIN pg_class c ON c.relname = m.tablename
    JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = m.schemaname
    JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
    WHERE m.allow_select = false AND has_column_privilege('erp_app', c.oid, a.attname, 'SELECT')

    UNION ALL
    SELECT 'CoreTableColumnWrite', format('%I.%I.%I', m.schemaname, m.tablename, a.attname), 'UPDATE',
           'Expected FALSE', 'Got TRUE', 'Prohibited column-level UPDATE on core table'
    FROM approved_table_matrix m
    JOIN pg_class c ON c.relname = m.tablename
    JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = m.schemaname
    JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
    WHERE m.allow_update = false AND has_column_privilege('erp_app', c.oid, a.attname, 'UPDATE')

    UNION ALL
    SELECT 'CoreTableColumnWrite', format('%I.%I.%I', m.schemaname, m.tablename, a.attname), 'INSERT',
           'Expected FALSE', 'Got TRUE', 'Prohibited column-level INSERT on core table'
    FROM approved_table_matrix m
    JOIN pg_class c ON c.relname = m.tablename
    JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = m.schemaname
    JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
    WHERE m.allow_insert = false AND has_column_privilege('erp_app', c.oid, a.attname, 'INSERT')

    UNION ALL
    SELECT 'CoreTableColumnRef', format('%I.%I.%I', m.schemaname, m.tablename, a.attname), 'REFERENCES',
           'Expected FALSE', 'Got TRUE', 'Prohibited column-level REFERENCES on core table'
    FROM approved_table_matrix m
    JOIN pg_class c ON c.relname = m.tablename
    JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = m.schemaname
    JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
    WHERE m.allow_references = false AND has_column_privilege('erp_app', c.oid, a.attname, 'REFERENCES')

    -- CATEGORY 3D: Unlisted Relations Inspection
    UNION ALL
    SELECT 'UnlistedRelation', format('%I.%I', n.nspname, c.relname), 'ACCESS', 'Expected FALSE', 'Got TRUE',
           format('erp_app possesses unauthorized table or column access to unlisted relation of kind %s', c.relkind)
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relkind IN ('r', 'p', 'v', 'm', 'f')
      AND n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'public')
      AND NOT EXISTS (
          SELECT 1 FROM approved_table_matrix m 
          WHERE m.schemaname = n.nspname AND m.tablename = c.relname
      )
      AND format('%I.%I', n.nspname, c.relname) NOT IN (
          'platform.tenants', 'identity.users', 'organization.companies', 'organization.locations'
      )
      AND (
          has_table_privilege('erp_app', c.oid, 'SELECT') OR
          has_table_privilege('erp_app', c.oid, 'INSERT') OR
          has_table_privilege('erp_app', c.oid, 'UPDATE') OR
          has_table_privilege('erp_app', c.oid, 'DELETE') OR
          has_table_privilege('erp_app', c.oid, 'TRUNCATE') OR
          has_table_privilege('erp_app', c.oid, 'REFERENCES') OR
          has_table_privilege('erp_app', c.oid, 'TRIGGER') OR
          has_table_privilege('erp_app', c.oid, 'MAINTAIN') OR
          has_any_column_privilege('erp_app', c.oid, 'SELECT') OR
          has_any_column_privilege('erp_app', c.oid, 'INSERT') OR
          has_any_column_privilege('erp_app', c.oid, 'UPDATE') OR
          has_any_column_privilege('erp_app', c.oid, 'REFERENCES')
      )

    -- CATEGORY 4: Exhaustive Column Existence & Privilege Checks on 4 Column-Restricted Tables
    UNION ALL
    SELECT 'ExpectedColumnMissing', format('%I.%I.%I', cm.schemaname, cm.tablename, cm.colname), 'COLUMN_EXISTS',
           'Expected EXISTS', 'Got NULL', 'Expected allowlisted column is missing from table definition'
    FROM approved_column_matrix cm
    LEFT JOIN pg_namespace n ON n.nspname = cm.schemaname
    LEFT JOIN pg_class c ON c.relname = cm.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    LEFT JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = cm.colname AND a.attnum > 0 AND NOT a.attisdropped
    WHERE a.attname IS NULL

    UNION ALL
    SELECT 'ColumnAudit', format('%I.%I.%I', cm.schemaname, cm.tablename, cm.colname), 'SELECT',
           'Expected TRUE', format('Got %s', has_column_privilege('erp_app', format('%I.%I', cm.schemaname, cm.tablename), cm.colname, 'SELECT')),
           'Allowlisted column lacks required SELECT privilege'
    FROM approved_column_matrix cm
    JOIN pg_namespace n ON n.nspname = cm.schemaname
    JOIN pg_class c ON c.relname = cm.tablename AND c.relnamespace = n.oid AND c.relkind IN ('r', 'p')
    JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = cm.colname AND a.attnum > 0 AND NOT a.attisdropped
    WHERE cm.allow_select = true AND NOT has_column_privilege('erp_app', c.oid, cm.colname, 'SELECT')

    UNION ALL
    SELECT 'ColumnAudit', format('%I.%I.%I', n.nspname, c.relname, a.attname), 'SELECT',
           'Expected FALSE', 'Got TRUE', 'Column SELECT privilege granted on unapproved column of restricted table'
    FROM pg_attribute a
    JOIN pg_class c ON c.oid = a.attrelid
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE format('%I.%I', n.nspname, c.relname) IN ('platform.tenants', 'identity.users', 'organization.companies', 'organization.locations')
      AND a.attnum > 0 AND NOT a.attisdropped
      AND NOT EXISTS (
          SELECT 1 FROM approved_column_matrix cm 
          WHERE cm.schemaname = n.nspname AND cm.tablename = c.relname AND cm.colname = a.attname AND cm.allow_select = true
      )
      AND has_column_privilege('erp_app', c.oid, a.attname, 'SELECT')

    UNION ALL
    SELECT 'ColumnAudit', format('%I.%I.%I', n.nspname, c.relname, a.attname), 'WRITE_OR_REF',
           'Expected FALSE', 'Got TRUE', 'Column write or references permission prohibited'
    FROM pg_attribute a
    JOIN pg_class c ON c.oid = a.attrelid
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE format('%I.%I', n.nspname, c.relname) IN ('platform.tenants', 'identity.users', 'organization.companies', 'organization.locations')
      AND a.attnum > 0 AND NOT a.attisdropped
      AND (
          has_column_privilege('erp_app', c.oid, a.attname, 'INSERT') OR
          has_column_privilege('erp_app', c.oid, a.attname, 'UPDATE') OR
          has_column_privilege('erp_app', c.oid, a.attname, 'REFERENCES')
      )

    -- CATEGORY 5: Sequence Privileges
    UNION ALL
    SELECT 'Sequence', 'sync.change_log_change_sequence_seq', 'USAGE', 'Expected TRUE', 'Got FALSE', 'Missing sequence USAGE'
    WHERE NOT has_sequence_privilege('erp_app', 'sync.change_log_change_sequence_seq', 'USAGE')
    UNION ALL
    SELECT 'Sequence', 'sync.change_log_change_sequence_seq', 'SELECT', 'Expected TRUE', 'Got FALSE', 'Missing sequence SELECT'
    WHERE NOT has_sequence_privilege('erp_app', 'sync.change_log_change_sequence_seq', 'SELECT')
    UNION ALL
    SELECT 'Sequence', 'sync.change_log_change_sequence_seq', 'UPDATE', 'Expected FALSE', 'Got TRUE', 'Prohibited sequence UPDATE'
    WHERE has_sequence_privilege('erp_app', 'sync.change_log_change_sequence_seq', 'UPDATE')
    UNION ALL
    SELECT 'Sequence', format('%I.%I', n.nspname, c.relname), 'ACCESSIBLE', 'Expected FALSE', 'Got TRUE', 'Unauthorized sequence access'
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relkind = 'S'
      AND format('%I.%I', n.nspname, c.relname) <> 'sync.change_log_change_sequence_seq'
      AND (
          has_sequence_privilege('erp_app', c.oid, 'USAGE') OR 
          has_sequence_privilege('erp_app', c.oid, 'SELECT') OR 
          has_sequence_privilege('erp_app', c.oid, 'UPDATE')
      )

    -- CATEGORY 6: Function & Procedure Privileges
    UNION ALL
    SELECT 'Function', 'set_config(text,text,boolean)', 'EXECUTE', 'Expected TRUE', 'Got FALSE', 'Missing EXECUTE on set_config'
    WHERE NOT has_function_privilege('erp_app', 'set_config(text,text,boolean)', 'EXECUTE')
    UNION ALL
    SELECT 'Function', 'pg_advisory_xact_lock(bigint)', 'EXECUTE', 'Expected TRUE', 'Got FALSE', 'Missing EXECUTE on pg_advisory_xact_lock'
    WHERE NOT has_function_privilege('erp_app', 'pg_advisory_xact_lock(bigint)', 'EXECUTE')
    UNION ALL
    SELECT 'Function', p_sig.sig, 'EXECUTE', 'Expected FALSE', 'Got TRUE', 'Prohibited administrative routine execution'
    FROM (
        VALUES
        ('pg_terminate_backend(integer)'),
        ('pg_cancel_backend(integer)'),
        ('pg_reload_conf()'),
        ('pg_rotate_logfile()'),
        ('pg_read_file(text)'),
        ('pg_read_file(text,bigint,bigint)'),
        ('pg_read_file(text,bigint,bigint,boolean)'),
        ('pg_read_binary_file(text)'),
        ('pg_read_binary_file(text,bigint,bigint)'),
        ('pg_read_binary_file(text,bigint,bigint,boolean)'),
        ('pg_write_file(text,text)'),
        ('pg_write_file(text,text,boolean)')
    ) AS p_sig(sig)
    WHERE has_function_privilege('erp_app', p_sig.sig, 'EXECUTE')
    UNION ALL
    SELECT 'DomainRoutine', format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)), 'EXECUTE',
           'Expected FALSE', 'Got TRUE', 'Unapproved routine execution in domain schema'
    FROM pg_proc p
    JOIN pg_namespace n ON n.oid = p.pronamespace
    WHERE n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization')
      AND has_function_privilege('erp_app', p.oid, 'EXECUTE')

    -- CATEGORY 7: PostgreSQL 17 Cluster-Wide Dynamic Role Membership & SET ROLE Audit
    UNION ALL
    SELECT 'RoleSetEscalation', r.rolname, 'SET_ROLE', 'Expected NOT SETTABLE', 'Got SETTABLE',
           format('erp_app has native PostgreSQL 17 privilege to execute SET ROLE %I', r.rolname)
    FROM pg_roles r
    WHERE r.rolname <> 'erp_app'
      AND (
          r.rolsuper = true OR r.rolbypassrls = true OR r.rolcreaterole = true OR r.rolcreatedb = true OR
          r.rolreplication = true OR r.rolname LIKE 'pg_%' OR r.rolname IN ('erp_dev', 'test_bootstrap_admin', 'postgres')
      )
      AND pg_has_role('erp_app', r.oid, 'SET')
    UNION ALL
    SELECT 'RoleMembership', r.rolname, 'ROLE_MEMBER', 'Expected NOT MEMBER', 'Got MEMBER',
           format('erp_app inherits or belongs to unauthorized elevated role %I', r.rolname)
    FROM pg_roles r
    WHERE r.rolname <> 'erp_app'
      AND (
          r.rolsuper = true OR r.rolbypassrls = true OR r.rolcreaterole = true OR r.rolcreatedb = true OR
          r.rolreplication = true OR r.rolname LIKE 'pg_%' OR r.rolname IN ('erp_dev', 'test_bootstrap_admin', 'postgres')
      )
      AND (pg_has_role('erp_app', r.oid, 'USAGE') OR pg_has_role('erp_app', r.oid, 'MEMBER'))

    -- CATEGORY 8: Permissions Available Through PUBLIC
    UNION ALL
    SELECT 'PublicOverGrant', 'DATABASE: universal_erp_disposable_test', acl.privilege_type, 'Expected REVOKED', 'Got GRANTED',
           format('Database permission %s is granted to PUBLIC', acl.privilege_type)
    FROM pg_database d, LATERAL aclexplode(coalesce(d.datacl, acldefault('d', d.datdba))) acl
    WHERE d.datname = current_database()
      AND acl.grantee = 0
      AND acl.privilege_type IN ('CREATE', 'TEMP')
    UNION ALL
    SELECT 'PublicOverGrant', format('SCHEMA: %I', n.nspname), acl.privilege_type, 'Expected REVOKED', 'Got GRANTED',
           format('Schema %I has privilege %s granted to PUBLIC', n.nspname, acl.privilege_type)
    FROM pg_namespace n, LATERAL aclexplode(coalesce(n.nspacl, acldefault('n', n.nspowner))) acl
    WHERE (
        (n.nspname = 'public' AND acl.privilege_type = 'CREATE') OR
        (n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit') AND acl.privilege_type IN ('CREATE', 'USAGE'))
    ) AND acl.grantee = 0
    UNION ALL
    SELECT 'PublicOverGrant', format('RELATION: %I.%I', n.nspname, c.relname), acl.privilege_type, 'Expected REVOKED', 'Got GRANTED',
           format('Relation %I.%I has privilege %s granted to PUBLIC', n.nspname, c.relname, acl.privilege_type)
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    CROSS JOIN LATERAL aclexplode(coalesce(c.relacl, acldefault('r', c.relowner))) acl
    WHERE (n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit') OR n.nspname = 'public')
      AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
      AND acl.grantee = 0
    UNION ALL
    SELECT 'PublicOverGrant', format('SEQUENCE: %I.%I', n.nspname, c.relname), acl.privilege_type, 'Expected REVOKED', 'Got GRANTED',
           format('Sequence %I.%I has privilege %s granted to PUBLIC', n.nspname, c.relname, acl.privilege_type)
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    CROSS JOIN LATERAL aclexplode(coalesce(c.relacl, acldefault('s', c.relowner))) acl
    WHERE (n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit') OR n.nspname = 'public')
      AND c.relkind = 'S'
      AND acl.grantee = 0
    UNION ALL
    SELECT 'PublicOverGrant', format('ROUTINE: %I.%I', n.nspname, p.proname), acl.privilege_type, 'Expected REVOKED', 'Got GRANTED',
           format('Routine %I.%I has privilege %s granted to PUBLIC', n.nspname, p.proname, acl.privilege_type)
    FROM pg_proc p
    JOIN pg_namespace n ON n.oid = p.pronamespace
    CROSS JOIN LATERAL aclexplode(coalesce(p.proacl, acldefault('f', p.proowner))) acl
    WHERE (n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit') OR n.nspname = 'public')
      AND acl.grantee = 0
    UNION ALL
    SELECT 'PublicOverGrant', format('TYPE: %I.%I', n.nspname, t.typname), acl.privilege_type, 'Expected REVOKED', 'Got GRANTED',
           format('Type %I.%I has privilege %s granted to PUBLIC', n.nspname, t.typname, acl.privilege_type)
    FROM pg_type t
    JOIN pg_namespace n ON n.oid = t.typnamespace
    CROSS JOIN LATERAL aclexplode(coalesce(t.typacl, acldefault('T', t.typowner))) acl
    WHERE (n.nspname IN ('sync', 'erp_inventory', 'erp_purchasing', 'erp_sales', 'finance', 'platform', 'identity', 'organization', 'authorization', 'audit') OR n.nspname = 'public')
      AND t.typisdefined
      AND t.typelem = 0
      AND acl.grantee = 0
)
SELECT category, object_name, privilege, expected, actual, detail
FROM audit_violations
ORDER BY category, object_name;
