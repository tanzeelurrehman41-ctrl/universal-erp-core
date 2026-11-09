-- Universal ERP -- Read-Only Inspection for Variant Creation Permission
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target User: tanzeelurrehman41@gmail.com

\echo '=========================================================================='
\echo '1. Effective Permissions for Tanzeel in DEMO-CLOUD / CLOUD-LOGISTICS'
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
\echo '2. Role DEMO_INVENTORY_VIEWER Role Permissions'
\echo '=========================================================================='
SELECT rp.permission_code, rp.scope_type, p.module_code, p.description
FROM "authorization".role_permissions rp
JOIN "authorization".roles r ON r.id = rp.role_id AND r.tenant_id = rp.tenant_id
JOIN "authorization".permissions p ON p.permission_code = rp.permission_code
WHERE r.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND r.role_code = 'DEMO_INVENTORY_VIEWER'
ORDER BY rp.permission_code;

\echo '=========================================================================='
\echo '3. Table Privileges on erp_inventory.product_variant for erp_app'
\echo '=========================================================================='
SELECT table_name, privilege_type, is_grantable
FROM information_schema.table_privileges
WHERE grantee = 'erp_app'
  AND table_schema = 'erp_inventory'
  AND table_name = 'product_variant';

\echo '=========================================================================='
\echo '4. Current Product IDTEM-02 / scissor'
\echo '=========================================================================='
SELECT product_id, product_code, name, product_type, is_active, created_at
FROM erp_inventory.product
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND product_code = 'IDTEM-02';

\echo '=========================================================================='
\echo '5. Current Unit EA / Each'
\echo '=========================================================================='
SELECT uom_id, code, name, dimension, decimal_places
FROM erp_inventory.unit_of_measure
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND code = 'EA';
