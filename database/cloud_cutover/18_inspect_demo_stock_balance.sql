-- Universal ERP -- Read-Only Inspection for Persisted Demo Stock Balance
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)

\echo '=========================================================================='
\echo '1. Confirmed Stock Balances in erp_inventory.stock_balance'
\echo '=========================================================================='
SELECT 
    b.balance_id,
    p.product_code,
    p.name AS product_name,
    v.sku,
    w.code AS warehouse_code,
    bin.code AS bin_code,
    b.status_code,
    b.on_hand_qty,
    u.code AS uom_code,
    b.updated_at
FROM erp_inventory.stock_balance b
JOIN erp_inventory.product_variant v ON v.tenant_id = b.tenant_id AND v.variant_id = b.variant_id
JOIN erp_inventory.product p ON p.tenant_id = v.tenant_id AND p.product_id = v.product_id
JOIN erp_inventory.warehouse w ON w.tenant_id = b.tenant_id AND w.company_id = b.company_id AND w.warehouse_id = b.warehouse_id
JOIN erp_inventory.stock_bin bin ON bin.tenant_id = b.tenant_id AND bin.company_id = b.company_id AND bin.bin_id = b.bin_id
JOIN erp_inventory.unit_of_measure u ON u.tenant_id = v.tenant_id AND u.uom_id = v.base_uom_id
WHERE b.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND b.company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo '=========================================================================='
\echo '2. Confirmed Inventory Movements in erp_inventory.inventory_movement'
\echo '=========================================================================='
SELECT 
    m.movement_id,
    m.movement_type,
    m.client_transaction_id,
    m.occurred_at,
    m.posted_at,
    m.note
FROM erp_inventory.inventory_movement m
WHERE m.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND m.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY m.posted_at DESC;

\echo '=========================================================================='
\echo '3. Inventory Movement Legs in erp_inventory.inventory_movement_leg'
\echo '=========================================================================='
SELECT 
    l.movement_id,
    l.leg_number,
    v.sku,
    w.code AS warehouse_code,
    bin.code AS bin_code,
    l.status_code,
    l.quantity_delta
FROM erp_inventory.inventory_movement_leg l
JOIN erp_inventory.product_variant v ON v.tenant_id = l.tenant_id AND v.variant_id = l.variant_id
JOIN erp_inventory.warehouse w ON w.tenant_id = l.tenant_id AND w.company_id = l.company_id AND w.warehouse_id = l.warehouse_id
JOIN erp_inventory.stock_bin bin ON bin.tenant_id = l.tenant_id AND bin.company_id = l.company_id AND bin.bin_id = l.bin_id
WHERE l.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND l.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY l.movement_id, l.leg_number;

\echo '=========================================================================='
\echo '4. Command Idempotency Records in erp_inventory.command_idempotency'
\echo '=========================================================================='
SELECT 
    idempotency_key,
    command_code,
    resource_id,
    created_at
FROM erp_inventory.command_idempotency
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo '=========================================================================='
\echo '5. Acknowledged Operations in sync.operation_inbox'
\echo '=========================================================================='
SELECT 
    operation_id,
    operation_type,
    outcome,
    server_record_id,
    processed_at
FROM sync.operation_inbox
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo '=========================================================================='
\echo '6. Tenant Stock Statuses in erp_inventory.stock_status'
\echo '=========================================================================='
SELECT status_code, description, is_reservable, is_pickable
FROM erp_inventory.stock_status
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
ORDER BY status_code;
