-- Universal ERP -- Read-Only Rollback & Schema State Verification on Neon
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Purpose: Verify exact state after the failed execution of Script 34:
--          1. Check whether transaction rolled back completely (permissions, schema_migrations, roles, assignments).
--          2. Inspect exact column definitions of authorization tables from information_schema.
-- Safety: Strictly read-only queries. Zero database mutations.

\echo '=========================================================================='
\echo 'UNIVERSAL ERP -- ROLLBACK STATE & AUTHORIZATION SCHEMA INSPECTION'
\echo '=========================================================================='

SELECT current_database() AS connected_database, current_user AS connected_user, version() AS pg_version;

\echo ''
\echo '=========================================================================='
\echo '1. Check Sales Permissions in authorization.permissions (Rollback Check)'
\echo '=========================================================================='
SELECT permission_code, description, module_code
FROM "authorization".permissions
WHERE permission_code LIKE 'sales.%'
ORDER BY permission_code;

SELECT count(*) AS sales_permissions_found
FROM "authorization".permissions
WHERE permission_code LIKE 'sales.%';

\echo ''
\echo '=========================================================================='
\echo '2. Check Migration 025 in public.schema_migrations (Rollback Check)'
\echo '=========================================================================='
SELECT version, applied_at
FROM public.schema_migrations
WHERE version = '025_register_sales_permissions';

SELECT count(*) AS migration_025_found
FROM public.schema_migrations
WHERE version = '025_register_sales_permissions';

\echo ''
\echo '=========================================================================='
\echo '3. Check DEMO_SALES_OPERATOR in authorization.roles (Rollback Check)'
\echo '=========================================================================='
SELECT id, tenant_id, role_code, name, is_system_role, created_at
FROM "authorization".roles
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND role_code = 'DEMO_SALES_OPERATOR';

SELECT count(*) AS demo_sales_operator_count
FROM "authorization".roles
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND role_code = 'DEMO_SALES_OPERATOR';

\echo ''
\echo '=========================================================================='
\echo '4. Check Role Assignments for DEMO-CLOUD (Rollback Check)'
\echo '=========================================================================='
SELECT 
    mra.id AS assignment_id,
    mra.tenant_id,
    mra.membership_id,
    u.email_normalized,
    r.role_code,
    mra.effective_from,
    mra.effective_until
FROM "authorization".membership_role_assignments mra
JOIN identity.tenant_memberships tm ON tm.id = mra.membership_id AND tm.tenant_id = mra.tenant_id
JOIN identity.users u ON u.id = tm.user_id
JOIN "authorization".roles r ON r.id = mra.role_id AND r.tenant_id = mra.tenant_id
WHERE mra.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
ORDER BY r.role_code;

\echo ''
\echo '=========================================================================='
\echo '5. Actual Column Definitions in authorization.roles'
\echo '=========================================================================='
SELECT 
    column_name,
    data_type,
    is_nullable,
    column_default,
    ordinal_position
FROM information_schema.columns
WHERE table_schema = 'authorization'
  AND table_name = 'roles'
ORDER BY ordinal_position;

\echo ''
\echo '=========================================================================='
\echo '6. Actual Column Definitions in authorization.role_permissions'
\echo '=========================================================================='
SELECT 
    column_name,
    data_type,
    is_nullable,
    column_default,
    ordinal_position
FROM information_schema.columns
WHERE table_schema = 'authorization'
  AND table_name = 'role_permissions'
ORDER BY ordinal_position;

\echo ''
\echo '=========================================================================='
\echo '7. Actual Column Definitions in authorization.membership_role_assignments'
\echo '=========================================================================='
SELECT 
    column_name,
    data_type,
    is_nullable,
    column_default,
    ordinal_position
FROM information_schema.columns
WHERE table_schema = 'authorization'
  AND table_name = 'membership_role_assignments'
ORDER BY ordinal_position;

\echo ''
\echo '=========================================================================='
\echo '8. Actual Column Definitions in authorization.permissions'
\echo '=========================================================================='
SELECT 
    column_name,
    data_type,
    is_nullable,
    column_default,
    ordinal_position
FROM information_schema.columns
WHERE table_schema = 'authorization'
  AND table_name = 'permissions'
ORDER BY ordinal_position;
