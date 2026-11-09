-- Universal ERP -- Read-Only Schema & RBAC Inspection on Neon (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target User: tanzeelurrehman41@gmail.com

\echo '=========================================================================='
\echo '1. Database & Cluster Verification'
\echo '=========================================================================='
SELECT current_database() AS database_name,
       current_user AS connected_user,
       inet_server_addr() AS server_ip,
       version() AS postgres_version;

\echo '=========================================================================='
\echo '2. Table Definitions & Constraints for RBAC Tables'
\echo '=========================================================================='
SELECT table_schema, table_name, column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_schema = 'authorization'
  AND table_name IN ('roles', 'role_permissions', 'membership_role_assignments', 'permissions')
ORDER BY table_name, ordinal_position;

\echo '=========================================================================='
\echo '3. Foreign Keys & Unique Constraints on RBAC Tables'
\echo '=========================================================================='
SELECT tc.table_schema, tc.table_name, tc.constraint_name, tc.constraint_type,
       kcu.column_name, ccu.table_schema AS foreign_table_schema,
       ccu.table_name AS foreign_table_name, ccu.column_name AS foreign_column_name
FROM information_schema.table_constraints AS tc
JOIN information_schema.key_column_usage AS kcu
  ON tc.constraint_name = kcu.constraint_name
 AND tc.table_schema = kcu.table_schema
LEFT JOIN information_schema.constraint_column_usage AS ccu
  ON ccu.constraint_name = tc.constraint_name
 AND ccu.table_schema = tc.table_schema
WHERE tc.table_schema = 'authorization'
ORDER BY tc.table_name, tc.constraint_type, tc.constraint_name;

\echo '=========================================================================='
\echo '4. Existing Permission Definitions (View vs Write)'
\echo '=========================================================================='
SELECT permission_code, module_code, description
FROM "authorization".permissions
WHERE permission_code IN (
    'inventory.product.view',
    'inventory.uom.view',
    'inventory.variant.view',
    'inventory.warehouse.view',
    'inventory.availability.view',
    'inventory.product.create',
    'inventory.uom.create',
    'inventory.variant.create',
    'inventory.warehouse.create',
    'inventory.receipt.create',
    'inventory.receipt.post'
)
ORDER BY module_code, permission_code;

\echo '=========================================================================='
\echo '5. Existing Roles in Tenant DEMO-CLOUD'
\echo '=========================================================================='
SELECT id AS role_id, tenant_id, role_code, name, is_system_role, created_at
FROM "authorization".roles
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid;

\echo '=========================================================================='
\echo '6. Existing Role Permissions in Tenant DEMO-CLOUD'
\echo '=========================================================================='
SELECT rp.tenant_id, r.role_code, rp.permission_code, rp.scope_type
FROM "authorization".role_permissions rp
JOIN "authorization".roles r ON r.id = rp.role_id AND r.tenant_id = rp.tenant_id
WHERE rp.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
ORDER BY r.role_code, rp.permission_code;

\echo '=========================================================================='
\echo '7. Tanzeel Tenant & Company Membership Records'
\echo '=========================================================================='
SELECT u.id AS user_id, u.email_normalized, u.display_name, u.status AS user_status,
       tm.id AS tenant_membership_id, tm.tenant_id, tm.status AS tm_status,
       cm.id AS company_membership_id, cm.company_id, cm.status AS cm_status
FROM identity.users u
JOIN identity.tenant_memberships tm ON tm.user_id = u.id
JOIN identity.company_memberships cm ON cm.user_id = u.id AND cm.tenant_id = tm.tenant_id
WHERE u.email_normalized = 'tanzeelurrehman41@gmail.com'
  AND tm.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND cm.company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo '=========================================================================='
\echo '8. Existing Membership Role Assignments for DEMO-CLOUD'
\echo '=========================================================================='
SELECT mra.id AS assignment_id, mra.tenant_id, mra.membership_id, u.email_normalized,
       r.role_code, mra.effective_from, mra.effective_until
FROM "authorization".membership_role_assignments mra
JOIN identity.tenant_memberships tm ON tm.id = mra.membership_id AND tm.tenant_id = mra.tenant_id
JOIN identity.users u ON u.id = tm.user_id
JOIN "authorization".roles r ON r.id = mra.role_id AND r.tenant_id = mra.tenant_id
WHERE mra.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid;

\echo '=========================================================================='
\echo '9. Current user_has_permission Diagnostic for tanzeelurrehman41@gmail.com'
\echo '=========================================================================='
SELECT 
    p.permission_code,
    identity.user_has_permission(
        '22222222-2222-4222-8222-222222222222'::uuid,
        '22222222-2222-4222-8222-222222222223'::uuid,
        u.id,
        p.permission_code
    ) AS currently_has_permission
FROM (
    VALUES 
        ('inventory.product.view'),
        ('inventory.uom.view'),
        ('inventory.variant.view'),
        ('inventory.warehouse.view'),
        ('inventory.availability.view'),
        ('inventory.product.create'),
        ('inventory.receipt.post')
) AS p(permission_code)
CROSS JOIN identity.users u
WHERE u.email_normalized = 'tanzeelurrehman41@gmail.com';
