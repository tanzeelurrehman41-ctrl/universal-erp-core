-- Universal ERP -- Read-Only Inspection for Warehouse & Bin Permissions
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
\echo '3. Table Privileges on erp_inventory.warehouse & stock_bin for erp_app'
\echo '=========================================================================='
SELECT table_name, privilege_type, is_grantable
FROM information_schema.table_privileges
WHERE grantee = 'erp_app'
  AND table_schema = 'erp_inventory'
  AND table_name IN ('warehouse', 'stock_bin')
ORDER BY table_name, privilege_type;

\echo '=========================================================================='
\echo '4. Current Warehouses in DEMO-CLOUD / CLOUD-LOGISTICS'
\echo '=========================================================================='
SELECT warehouse_id, code, name, is_active
FROM erp_inventory.warehouse
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo '=========================================================================='
\echo '5. Current Product & Variant in DEMO-CLOUD'
\echo '=========================================================================='
SELECT p.product_code, p.name AS product_name, v.sku, u.code AS uom_code
FROM erp_inventory.product p
JOIN erp_inventory.product_variant v ON v.tenant_id = p.tenant_id AND v.product_id = p.product_id
JOIN erp_inventory.unit_of_measure u ON u.tenant_id = v.tenant_id AND u.uom_id = v.base_uom_id
WHERE p.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid;
