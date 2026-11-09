-- Universal ERP -- Read-Only Preconditions Inspection for Inventory Write Testing
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target User: tanzeelurrehman41@gmail.com

\echo '=========================================================================='
\echo '1. Database Identity & Cluster Verification'
\echo '=========================================================================='
SELECT current_database() AS database_name,
       current_user AS connected_user,
       inet_server_addr() AS server_ip,
       version() AS postgres_version;

\echo '=========================================================================='
\echo '2. Role DEMO_INVENTORY_VIEWER Existence & Assignment to Tanzeel'
\echo '=========================================================================='
SELECT r.id AS role_id,
       r.tenant_id,
       r.role_code,
       r.name AS role_name,
       mra.id AS assignment_id,
       mra.membership_id,
       u.email_normalized,
       mra.effective_from,
       mra.effective_until
FROM "authorization".roles r
JOIN "authorization".membership_role_assignments mra 
  ON mra.role_id = r.id AND mra.tenant_id = r.tenant_id
JOIN identity.tenant_memberships tm 
  ON tm.id = mra.membership_id AND tm.tenant_id = mra.tenant_id
JOIN identity.users u 
  ON u.id = tm.user_id
WHERE r.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND r.role_code = 'DEMO_INVENTORY_VIEWER';

\echo '=========================================================================='
\echo '3. Currently Granted Permissions on DEMO_INVENTORY_VIEWER'
\echo '=========================================================================='
SELECT rp.permission_code,
       rp.scope_type,
       p.module_code,
       p.description
FROM "authorization".role_permissions rp
JOIN "authorization".roles r 
  ON r.id = rp.role_id AND r.tenant_id = rp.tenant_id
JOIN "authorization".permissions p 
  ON p.permission_code = rp.permission_code
WHERE r.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND r.role_code = 'DEMO_INVENTORY_VIEWER'
ORDER BY rp.permission_code;

\echo '=========================================================================='
\echo '4. Current identity.user_has_permission Evaluation for Tanzeel'
\echo '=========================================================================='
SELECT 
    p.permission_code,
    identity.user_has_permission(
        '22222222-2222-4222-8222-222222222222'::uuid,
        '22222222-2222-4222-8222-222222222223'::uuid,
        u.id,
        p.permission_code
    ) AS currently_effective
FROM (
    VALUES 
        ('inventory.product.view'),
        ('inventory.uom.view'),
        ('inventory.variant.view'),
        ('inventory.warehouse.view'),
        ('inventory.availability.view'),
        ('inventory.product.create'),
        ('inventory.uom.create'),
        ('inventory.variant.create'),
        ('inventory.warehouse.create'),
        ('inventory.receipt.create'),
        ('inventory.receipt.post')
) AS p(permission_code)
CROSS JOIN identity.users u
WHERE u.email_normalized = 'tanzeelurrehman41@gmail.com';

\echo '=========================================================================='
\echo '5. Product Catalog & Idempotency State for "idtem-02" / "scissor"'
\echo '=========================================================================='
SELECT tenant_id, product_id, product_code, name, product_type, is_active, created_at
FROM erp_inventory.product
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND (product_code ILIKE '%idtem-02%' OR name ILIKE '%scissor%');

SELECT tenant_id, idempotency_key, command_code, request_hash, resource_id, created_at
FROM erp_inventory.command_idempotency
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND command_code = 'product.create'
ORDER BY created_at DESC
LIMIT 5;

\echo '=========================================================================='
\echo '6. Runtime Role erp_app Table Privileges (Inventory Domain)'
\echo '=========================================================================='
SELECT table_name,
       privilege_type,
       is_grantable
FROM information_schema.table_privileges
WHERE grantee = 'erp_app'
  AND table_schema = 'erp_inventory'
  AND table_name IN ('product', 'command_idempotency', 'unit_of_measure', 'product_variant', 'warehouse', 'stock_bin', 'stock_balance')
ORDER BY table_name, privilege_type;

\echo '=========================================================================='
\echo '7. Safety Check: Zeelman Membership & Roles'
\echo '=========================================================================='
SELECT u.email_normalized,
       tm.id AS tenant_membership_id,
       tm.status AS tm_status,
       COALESCE(r.role_code, '(no role assigned)') AS assigned_role
FROM identity.users u
JOIN identity.tenant_memberships tm 
  ON tm.user_id = u.id AND tm.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
LEFT JOIN "authorization".membership_role_assignments mra 
  ON mra.membership_id = tm.id AND mra.tenant_id = tm.tenant_id
LEFT JOIN "authorization".roles r 
  ON r.id = mra.role_id
WHERE u.email_normalized = 'zeelman49@gmail.com';
