-- Universal ERP -- Inspect Purchasing Preconditions and Schema State
-- Read-Only verification query for Neon neondb

SELECT current_database() AS connected_database, current_user AS connected_user, version() AS pg_version;

-- 1. Check purchasing catalog permissions in authorization.permissions
SELECT permission_code, module_code, description, created_at
FROM "authorization".permissions
WHERE permission_code LIKE 'purchasing%'
ORDER BY permission_code;

-- 2. Inspect DEMO_PURCHASING_OPERATOR, DEMO_INVENTORY_VIEWER, DEMO_INVENTORY_OPERATOR roles
SELECT id, tenant_id, role_code, name, is_system_role, created_at
FROM "authorization".roles
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND role_code IN ('DEMO_PURCHASING_OPERATOR', 'DEMO_INVENTORY_VIEWER', 'DEMO_INVENTORY_OPERATOR')
ORDER BY role_code;

-- 3. Check role assignments for Tanzeel Rehman
SELECT 
    mra.id AS assignment_id,
    r.role_code,
    r.name AS role_name,
    tm.tenant_id,
    u.email_normalized,
    mra.effective_from,
    mra.effective_until
FROM "authorization".membership_role_assignments mra
JOIN "authorization".roles r ON r.id = mra.role_id AND r.tenant_id = mra.tenant_id
JOIN identity.tenant_memberships tm ON tm.id = mra.membership_id AND tm.tenant_id = mra.tenant_id
JOIN identity.users u ON u.id = tm.user_id
WHERE tm.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND u.email_normalized = 'tanzeelurrehman41@gmail.com'
ORDER BY r.role_code;

-- 4. Check role permissions assigned to DEMO_PURCHASING_OPERATOR
SELECT 
    r.role_code,
    rp.permission_code,
    rp.scope_type,
    rp.created_at
FROM "authorization".role_permissions rp
JOIN "authorization".roles r ON r.id = rp.role_id AND r.tenant_id = rp.tenant_id
WHERE r.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND r.role_code = 'DEMO_PURCHASING_OPERATOR'
ORDER BY rp.permission_code;

-- 5. Check effective purchasing permissions for Tanzeel
SELECT 
    perm.permission_code,
    identity.user_has_permission(
        '22222222-2222-4222-8222-222222222222'::uuid,
        '22222222-2222-4222-8222-222222222223'::uuid,
        u.id,
        perm.permission_code
    ) AS has_permission
FROM (
    VALUES 
        ('purchasing.supplier.view'),
        ('purchasing.supplier.create'),
        ('purchasing.po.view'),
        ('purchasing.po.create'),
        ('purchasing.po.confirm'),
        ('purchasing.grn.view'),
        ('purchasing.grn.post'),
        ('sales.order.create'),
        ('finance.gl.post')
) AS perm(permission_code)
CROSS JOIN identity.users u
WHERE u.email_normalized = 'tanzeelurrehman41@gmail.com';

-- 6. Read-Only Counts in erp_purchasing for CLOUD-LOGISTICS
SELECT 
    (SELECT count(*) FROM erp_purchasing.supplier WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid AND company_id = '22222222-2222-4222-8222-222222222223'::uuid) AS supplier_count,
    (SELECT count(*) FROM erp_purchasing.purchase_order WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid AND company_id = '22222222-2222-4222-8222-222222222223'::uuid) AS po_count,
    (SELECT count(*) FROM erp_purchasing.goods_received_note WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid AND company_id = '22222222-2222-4222-8222-222222222223'::uuid) AS grn_count,
    (SELECT count(*) FROM erp_purchasing.outbox_event WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid) AS outbox_count;
