-- Universal ERP -- Read-Only Inspection for Inventory Permissions & Operator Preconditions
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)

\echo '=========================================================================='
\echo '1. Database & Cluster Verification'
\echo '=========================================================================='
SELECT current_database() AS database_name,
       current_user AS connected_user,
       inet_server_addr() AS server_ip,
       version() AS postgres_version;

\echo '=========================================================================='
\echo '2. Current Inventory Permissions in authorization.permissions'
\echo '=========================================================================='
SELECT permission_code, description, module_code
FROM "authorization".permissions
WHERE module_code = 'inventory' OR permission_code LIKE 'inventory%'
ORDER BY permission_code;

\echo '=========================================================================='
\echo '3. Target Transfer & Adjustment Permissions Existence Check'
\echo '=========================================================================='
SELECT p.code AS searched_permission,
       EXISTS (
           SELECT 1 FROM "authorization".permissions ap WHERE ap.permission_code = p.code
       ) AS is_registered_in_catalog
FROM (
    VALUES ('inventory.transfer.create'), ('inventory.adjustment.create')
) AS p(code);

\echo '=========================================================================='
\echo '4. Verify Rollback: Check for Partial DEMO_INVENTORY_OPERATOR Role'
\echo '=========================================================================='
SELECT id AS role_id, tenant_id, role_code, name, is_system_role, created_at
FROM "authorization".roles
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND role_code = 'DEMO_INVENTORY_OPERATOR';

\echo '=========================================================================='
\echo '5. Verify Rollback: Check for Partial DEMO_INVENTORY_OPERATOR Assignments'
\echo '=========================================================================='
SELECT mra.id AS assignment_id, mra.membership_id, r.role_code, mra.effective_from
FROM "authorization".membership_role_assignments mra
JOIN "authorization".roles r ON r.id = mra.role_id AND r.tenant_id = mra.tenant_id
WHERE mra.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND r.role_code = 'DEMO_INVENTORY_OPERATOR';

\echo '=========================================================================='
\echo '6. Verify DEMO_INVENTORY_VIEWER Role Integrity (Expected exactly 11 perms)'
\echo '=========================================================================='
SELECT rp.permission_code, rp.scope_type
FROM "authorization".role_permissions rp
JOIN "authorization".roles r ON r.id = rp.role_id AND r.tenant_id = rp.tenant_id
WHERE r.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND r.role_code = 'DEMO_INVENTORY_VIEWER'
ORDER BY rp.permission_code;

\echo '=========================================================================='
\echo '7. Verify Zeelman Account Context Invariant'
\echo '=========================================================================='
SELECT tenant_id, tenant_code, company_id, company_code, company_name
FROM identity.list_user_company_contexts(
    'https://securetoken.google.com/universal-erp-staging',
    'g5UVBoX1RFODIQ3suHZtN1DUiam2'
);

\echo '=========================================================================='
\echo '8. Applied Schema Migrations in public.schema_migrations (Latest 10)'
\echo '=========================================================================='
SELECT version, applied_at
FROM public.schema_migrations
ORDER BY applied_at DESC
LIMIT 10;
