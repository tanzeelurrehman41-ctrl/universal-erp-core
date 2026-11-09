-- Universal ERP -- Read-Only Inspection for Offline Receipt Sync & Device Authorization
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
        ('inventory.receipt.post'),
        ('inventory.transfer.create'),
        ('inventory.adjustment.create')
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
\echo '3. Current Device Registration State in identity.devices'
\echo '=========================================================================='
SELECT id, platform_code, device_name, status, enrolled_at, last_seen_at
FROM identity.devices
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid;

\echo '=========================================================================='
\echo '4. Current Device Company Scopes in sync.device_company_scopes'
\echo '=========================================================================='
SELECT device_id, company_id, scope_status, all_locations, revoked_at
FROM sync.device_company_scopes
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo '=========================================================================='
\echo '5. Current Offline Grants in sync.offline_grants'
\echo '=========================================================================='
SELECT id, device_id, user_id, allowed_operations, expires_at, revoked_at
FROM sync.offline_grants
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo '=========================================================================='
\echo '6. Operation Inbox in sync.operation_inbox'
\echo '=========================================================================='
SELECT operation_id, operation_type, device_id, outcome, processed_at
FROM sync.operation_inbox
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo '=========================================================================='
\echo '7. Target Receipt Entities: Variant, Warehouse, Bin'
\echo '=========================================================================='
SELECT 
    w.code AS warehouse_code,
    w.name AS warehouse_name,
    w.is_active AS warehouse_active,
    b.code AS bin_code,
    b.bin_type,
    b.is_active AS bin_active,
    v.sku,
    v.is_active AS variant_active
FROM erp_inventory.warehouse w
JOIN erp_inventory.stock_bin b 
  ON b.tenant_id = w.tenant_id AND b.company_id = w.company_id AND b.warehouse_id = w.warehouse_id
CROSS JOIN erp_inventory.product_variant v
WHERE w.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND w.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND w.code = 'MAIN-W1'
  AND b.code = 'RECEIVE-01'
  AND v.sku = 'SCISSOR-EA-01';
