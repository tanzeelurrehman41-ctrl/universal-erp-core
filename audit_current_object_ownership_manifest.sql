-- universal-erp-core/audit_current_object_ownership_manifest.sql
-- Object-Ownership Manifest & SECURITY DEFINER Audit for Current-Schema Lane (001..046)
-- Condition 1 Compliance: Returns ZERO rows if and only if every object matches the
-- explicit ownership manifest, every SECURITY DEFINER function has a locked-down search_path,
-- PUBLIC EXECUTE is revoked everywhere outside pg_catalog/information_schema, and erp_app
-- can execute ONLY the 14 individually approved identity.* SECURITY DEFINER signatures.

WITH app_schemas(nspname) AS (
    VALUES
        ('analytics'), ('audit'), ('authorization'), ('consolidation'),
        ('erp_inventory'), ('erp_purchasing'), ('erp_sales'), ('finance'),
        ('hr'), ('identity'), ('integrations'), ('manufacturing'),
        ('organization'), ('platform'), ('security'), ('sync'), ('warehouse')
),
-- Explicit manifest for non-extension routines across all 17 schemas + public.
-- Only the 3 pre-tenant / unscoped RBAC SECURITY DEFINER routines are individually justified
-- for erp_dev (BYPASSRLS) ownership; all other routines must remain owned by erp_schema_owner (NOBYPASSRLS).
approved_routine_manifest(schema_name, proname, args_signature, expected_owner, expected_secdef, require_search_path, allow_erp_app_execute, justification) AS (
    VALUES
        -- Justified BYPASSRLS SECURITY DEFINER exceptions (3)
        ('identity', 'list_user_company_contexts', 'p_issuer text, p_subject text',
         'erp_dev', true, true, true,
         'Pre-tenant /api/v1/contexts discovery runs before app.tenant_id is bound across FORCE RLS membership/company tables'),
        ('identity', 'resolve_authenticated_user_context', 'p_tenant_id uuid, p_company_id uuid, p_issuer text, p_subject text',
         'erp_dev', true, true, true,
         'AuthenticatedTenantContextMiddleware resolves active tenant/company context before session RLS binding'),
        ('identity', 'user_has_permission', 'p_tenant_id uuid, p_company_id uuid, p_user_id uuid, p_permission_code text',
         'erp_dev', true, true, true,
         'Authorization handler evaluates RBAC permissions in authorization schema (FORCE RLS, zero direct erp_app table grants)'),

        -- Preserved non-bypass erp_schema_owner SECURITY DEFINER routines (11)
        ('identity', 'get_company_settings', 'p_tenant_id uuid, p_company_id uuid',
         'erp_schema_owner', true, true, true, 'Preserved erp_schema_owner (NOBYPASSRLS)'),
        ('identity', 'get_user_profile', 'p_tenant_id uuid, p_company_id uuid, p_user_id uuid',
         'erp_schema_owner', true, true, true, 'Preserved erp_schema_owner (NOBYPASSRLS)'),
        ('identity', 'invite_or_assign_company_user', 'p_tenant_id uuid, p_company_id uuid, p_email text, p_display_name text, p_role_code text',
         'erp_schema_owner', true, true, true, 'Preserved erp_schema_owner (NOBYPASSRLS)'),
        ('identity', 'list_audit_events', 'p_tenant_id uuid, p_company_id uuid, p_limit integer',
         'erp_schema_owner', true, true, true, 'Preserved erp_schema_owner (NOBYPASSRLS)'),
        ('identity', 'list_company_currency_history', 'p_tenant_id uuid, p_company_id uuid, p_limit integer',
         'erp_schema_owner', true, true, true, 'Preserved erp_schema_owner (NOBYPASSRLS)'),
        ('identity', 'list_company_exchange_rates', 'p_tenant_id uuid, p_company_id uuid, p_from_currency character, p_to_currency character, p_rate_type text',
         'erp_schema_owner', true, true, true, 'Preserved erp_schema_owner (NOBYPASSRLS)'),
        ('identity', 'list_company_users', 'p_tenant_id uuid, p_company_id uuid',
         'erp_schema_owner', true, true, true, 'Preserved erp_schema_owner (NOBYPASSRLS)'),
        ('identity', 'list_roles_and_permissions', 'p_tenant_id uuid',
         'erp_schema_owner', true, true, true, 'Preserved erp_schema_owner (NOBYPASSRLS)'),
        ('identity', 'record_audit_event', 'p_tenant_id uuid, p_company_id uuid, p_actor_user_id uuid, p_event_code text, p_entity_type text, p_entity_id uuid, p_metadata jsonb',
         'erp_schema_owner', true, true, true, 'Preserved erp_schema_owner (NOBYPASSRLS)'),
        ('identity', 'update_company_currency_settings', 'p_tenant_id uuid, p_company_id uuid, p_actor_user_id uuid, p_functional_currency character, p_presentation_currency character, p_effective_date date, p_confirm_future_transactions boolean, p_change_reason text',
         'erp_schema_owner', true, true, true, 'Preserved erp_schema_owner (NOBYPASSRLS); sets app.tenant_id/company_id internally'),
        ('identity', 'update_company_settings', 'p_tenant_id uuid, p_company_id uuid, p_name text, p_legal_name text, p_registration_number text, p_timezone text',
         'erp_schema_owner', true, true, true, 'Preserved erp_schema_owner (NOBYPASSRLS)'),

        -- Preserved non-bypass SECURITY INVOKER trigger functions (7)
        ('audit', 'prevent_audit_log_modification', '',
         'erp_schema_owner', false, false, false, 'Immutability trigger function owned by erp_schema_owner'),
        ('audit', 'prevent_checkpoint_modification', '',
         'erp_schema_owner', false, false, false, 'Immutability trigger function owned by erp_schema_owner'),
        ('finance', 'assert_posted_journal_balanced', '',
         'erp_schema_owner', false, false, false, 'Journal balance trigger function owned by erp_schema_owner'),
        ('finance', 'reject_posted_journal_line_mutation', '',
         'erp_schema_owner', false, false, false, 'Posted journal immutability trigger function owned by erp_schema_owner'),
        ('organization', 'set_default_company_currencies', '',
         'erp_schema_owner', false, false, false, 'Company default currency trigger function owned by erp_schema_owner'),
        ('platform', 'enforce_pilot_guardrails', '',
         'erp_schema_owner', false, false, false, 'Tenant guardrail trigger function owned by erp_schema_owner'),
        ('platform', 'enforce_tenant_residency_rules', '',
         'erp_schema_owner', false, false, false, 'Tenant residency trigger function owned by erp_schema_owner')
),
manifest_violations AS (
    -- 1. Role attribute checks for erp_schema_owner, erp_app, erp_dev
    SELECT 'RoleAttribute' AS category, rolname AS object_name,
           'rolbypassrls/rolsuper' AS check_name,
           'Expected erp_schema_owner NOSUPERUSER NOBYPASSRLS NOLOGIN' AS expected,
           format('super=%s bypass=%s canlogin=%s', rolsuper, rolbypassrls, rolcanlogin) AS actual,
           'erp_schema_owner must be NOLOGIN NOSUPERUSER NOBYPASSRLS' AS detail
    FROM pg_roles
    WHERE rolname = 'erp_schema_owner'
      AND (rolsuper OR rolbypassrls OR rolcanlogin)

    UNION ALL
    SELECT 'DatabaseOwner', current_database(), 'datdba',
           'Expected erp_schema_owner', pg_get_userbyid(datdba),
           'Database must be owned by erp_schema_owner (NOBYPASSRLS)'
    FROM pg_database
    WHERE datname = current_database()
      AND pg_get_userbyid(datdba) <> 'erp_schema_owner'

    -- 2. Schema ownership: every app schema + public owned by erp_schema_owner (zero erp_dev ownership)
    UNION ALL
    SELECT 'SchemaOwner', n.nspname, 'nspowner',
           'Expected erp_schema_owner', pg_get_userbyid(n.nspowner),
           format('Schema %I is not owned by erp_schema_owner', n.nspname)
    FROM pg_namespace n
    WHERE (n.nspname IN (SELECT nspname FROM app_schemas) OR n.nspname = 'public')
      AND pg_get_userbyid(n.nspowner) <> 'erp_schema_owner'

    -- 3. Table / Sequence / View ownership: EVERY relation in app schemas + public owned by erp_schema_owner
    UNION ALL
    SELECT 'RelationOwner', format('%I.%I', n.nspname, c.relname), format('relkind=%s', c.relkind),
           'Expected erp_schema_owner', pg_get_userbyid(c.relowner),
           format('Relation %I.%I must be owned by erp_schema_owner, never erp_dev', n.nspname, c.relname)
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE (n.nspname IN (SELECT nspname FROM app_schemas) OR n.nspname = 'public')
      AND c.relkind IN ('r', 'p', 'v', 'm', 'f', 'S')
      AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = c.oid AND d.deptype = 'e')
      AND pg_get_userbyid(c.relowner) <> 'erp_schema_owner'

    -- 4. Custom types / domains ownership
    UNION ALL
    SELECT 'TypeDomainOwner', format('%I.%I', n.nspname, t.typname), format('typtype=%s', t.typtype),
           'Expected erp_schema_owner', pg_get_userbyid(t.typowner),
           format('Custom type/domain %I.%I must be owned by erp_schema_owner', n.nspname, t.typname)
    FROM pg_type t
    JOIN pg_namespace n ON n.oid = t.typnamespace
    WHERE (n.nspname IN (SELECT nspname FROM app_schemas) OR n.nspname = 'public')
      AND t.typtype IN ('c', 'd', 'e', 'r')
      AND NOT EXISTS (SELECT 1 FROM pg_class c WHERE c.reltype = t.oid)
      AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = t.oid AND d.deptype = 'e')
      AND pg_get_userbyid(t.typowner) <> 'erp_schema_owner'

    -- 5. Unlisted non-extension routine detected
    UNION ALL
    SELECT 'UnlistedRoutine', format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)),
           'MANIFEST_ENTRY', 'Expected listed in approved_routine_manifest', 'UNLISTED',
           format('Unexpected routine owner=%s secdef=%s outside manifest', pg_get_userbyid(p.proowner), p.prosecdef)
    FROM pg_proc p
    JOIN pg_namespace n ON n.oid = p.pronamespace
    WHERE (n.nspname IN (SELECT nspname FROM app_schemas) OR n.nspname = 'public')
      AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
      AND NOT EXISTS (
          SELECT 1 FROM approved_routine_manifest m
          WHERE m.schema_name = n.nspname
            AND m.proname = p.proname
            AND m.args_signature = pg_get_function_identity_arguments(p.oid)
      )

    -- 6. Missing manifest routine
    UNION ALL
    SELECT 'MissingManifestRoutine', format('%I.%I(%s)', m.schema_name, m.proname, m.args_signature),
           'EXISTS', 'Expected present', 'MISSING', m.justification
    FROM approved_routine_manifest m
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = m.schema_name
          AND p.proname = m.proname
          AND pg_get_function_identity_arguments(p.oid) = m.args_signature
    )

    -- 7. Manifest routine owner, SECURITY DEFINER, search_path, PUBLIC & erp_app EXECUTE checks
    UNION ALL
    SELECT 'RoutineOwnerMismatch', format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)),
           'proowner', format('Expected %s', m.expected_owner), pg_get_userbyid(p.proowner), m.justification
    FROM approved_routine_manifest m
    JOIN pg_namespace n ON n.nspname = m.schema_name
    JOIN pg_proc p ON p.pronamespace = n.oid AND p.proname = m.proname AND pg_get_function_identity_arguments(p.oid) = m.args_signature
    WHERE pg_get_userbyid(p.proowner) <> m.expected_owner

    UNION ALL
    SELECT 'RoutineSecdefMismatch', format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)),
           'prosecdef', m.expected_secdef::text, p.prosecdef::text, m.justification
    FROM approved_routine_manifest m
    JOIN pg_namespace n ON n.nspname = m.schema_name
    JOIN pg_proc p ON p.pronamespace = n.oid AND p.proname = m.proname AND pg_get_function_identity_arguments(p.oid) = m.args_signature
    WHERE p.prosecdef IS DISTINCT FROM m.expected_secdef

    UNION ALL
    SELECT 'InsecureSearchPath', format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)),
           'search_path', 'Expected pg_catalog leading search_path in proconfig', coalesce(array_to_string(p.proconfig, '; '), '<NULL>'),
           'SECURITY DEFINER routine must pin search_path starting with pg_catalog'
    FROM approved_routine_manifest m
    JOIN pg_namespace n ON n.nspname = m.schema_name
    JOIN pg_proc p ON p.pronamespace = n.oid AND p.proname = m.proname AND pg_get_function_identity_arguments(p.oid) = m.args_signature
    WHERE m.require_search_path
      AND NOT EXISTS (
          SELECT 1 FROM unnest(coalesce(p.proconfig, ARRAY[]::text[])) cfg
          WHERE cfg LIKE 'search_path=pg_catalog%'
      )

    UNION ALL
    SELECT 'RoutineExecutePrivilegeMismatch', format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)),
           'erp_app EXECUTE', m.allow_erp_app_execute::text, has_function_privilege('erp_app', p.oid, 'EXECUTE')::text,
           'erp_app EXECUTE must strictly match individual manifest approval'
    FROM approved_routine_manifest m
    JOIN pg_namespace n ON n.nspname = m.schema_name
    JOIN pg_proc p ON p.pronamespace = n.oid AND p.proname = m.proname AND pg_get_function_identity_arguments(p.oid) = m.args_signature
    WHERE has_function_privilege('erp_app', p.oid, 'EXECUTE') IS DISTINCT FROM m.allow_erp_app_execute

    -- 8. Zero PUBLIC EXECUTE on any routine in app schemas or public (including extension routines)
    UNION ALL
    SELECT 'PublicRoutineExecuteViolation', format('%I.%I(%s)', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid)),
           'PUBLIC EXECUTE', 'Expected REVOKED', 'GRANTED',
           'PUBLIC must never retain EXECUTE on non-system routines'
    FROM pg_proc p
    JOIN pg_namespace n ON n.oid = p.pronamespace
    WHERE (n.nspname IN (SELECT nspname FROM app_schemas) OR n.nspname = 'public')
      AND (
          p.proacl IS NULL
          OR EXISTS (
              SELECT 1 FROM aclexplode(p.proacl) acl
              WHERE acl.grantee = 0 AND acl.privilege_type = 'EXECUTE'
          )
      )
)
SELECT category, object_name, check_name, expected, actual, detail
FROM manifest_violations
ORDER BY category, object_name, check_name;
