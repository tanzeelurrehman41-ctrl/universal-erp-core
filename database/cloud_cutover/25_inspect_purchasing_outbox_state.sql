-- Universal ERP -- Read-Only Inspection: Purchasing Outbox & Inventory Sync State
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)

SELECT current_database() AS connected_database, current_user AS connected_user, version() AS pg_version;

\echo '=========================================================================='
\echo '1. Current Stock Balances in erp_inventory.stock_balance'
\echo '=========================================================================='
SELECT 
    sb.variant_id,
    pv.sku,
    p.name AS product_name,
    w.code AS warehouse_code,
    b.code AS bin_code,
    sb.status_code,
    sb.on_hand_qty,
    sb.updated_at
FROM erp_inventory.stock_balance sb
JOIN erp_inventory.product_variant pv ON pv.variant_id = sb.variant_id AND pv.tenant_id = sb.tenant_id
JOIN erp_inventory.product p ON p.product_id = pv.product_id AND p.tenant_id = sb.tenant_id
JOIN erp_inventory.warehouse w ON w.warehouse_id = sb.warehouse_id AND w.tenant_id = sb.tenant_id
JOIN erp_inventory.stock_bin b ON b.bin_id = sb.bin_id AND b.tenant_id = sb.tenant_id
WHERE sb.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND sb.company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo '=========================================================================='
\echo '2. Inventory Movements in erp_inventory.inventory_movement'
\echo '=========================================================================='
SELECT 
    m.movement_id,
    m.movement_type,
    m.source_document_type,
    m.source_document_id,
    m.client_transaction_id,
    m.occurred_at,
    m.note,
    count(l.leg_number) AS leg_count,
    COALESCE(sum(l.quantity_delta), 0) AS total_delta
FROM erp_inventory.inventory_movement m
LEFT JOIN erp_inventory.inventory_movement_leg l 
  ON l.movement_id = m.movement_id AND l.tenant_id = m.tenant_id AND l.company_id = m.company_id
WHERE m.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND m.company_id = '22222222-2222-4222-8222-222222222223'::uuid
GROUP BY m.movement_id, m.movement_type, m.source_document_type, m.source_document_id, m.client_transaction_id, m.occurred_at, m.note
ORDER BY m.occurred_at ASC;

\echo '=========================================================================='
\echo '3. Purchase Orders and Lines in erp_purchasing'
\echo '=========================================================================='
SELECT 
    po.purchase_order_id,
    po.po_number,
    s.name AS supplier_name,
    w.code AS warehouse_code,
    po.order_status,
    (SELECT COALESCE(sum(pol2.ordered_qty * pol2.unit_price), 0) FROM erp_purchasing.purchase_order_line pol2 WHERE pol2.purchase_order_id = po.purchase_order_id) AS total_order_amount,
    po.created_at
FROM erp_purchasing.purchase_order po
JOIN erp_purchasing.supplier s ON s.supplier_id = po.supplier_id AND s.tenant_id = po.tenant_id
JOIN erp_inventory.warehouse w ON w.warehouse_id = po.warehouse_id AND w.tenant_id = po.tenant_id
WHERE po.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND po.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY po.created_at ASC;

SELECT 
    pol.purchase_order_id,
    pol.line_number,
    pv.sku,
    pol.ordered_qty,
    pol.received_qty,
    pol.unit_price,
    pol.line_status
FROM erp_purchasing.purchase_order_line pol
JOIN erp_inventory.product_variant pv ON pv.variant_id = pol.variant_id AND pv.tenant_id = pol.tenant_id
WHERE pol.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND pol.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY pol.purchase_order_id, pol.line_number;

\echo '=========================================================================='
\echo '4. Goods Received Notes in erp_purchasing.goods_received_note'
\echo '=========================================================================='
SELECT 
    grn.grn_id,
    grn.grn_number,
    grn.purchase_order_id,
    grn.grn_status,
    grn.received_date,
    grn.client_transaction_id,
    grn.created_at
FROM erp_purchasing.goods_received_note grn
WHERE grn.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND grn.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY grn.created_at ASC;

SELECT 
    grnl.grn_id,
    grnl.line_number,
    grnl.po_line_number,
    pv.sku,
    b.code AS bin_code,
    grnl.received_qty,
    grnl.unit_price
FROM erp_purchasing.goods_received_note_line grnl
JOIN erp_inventory.product_variant pv ON pv.variant_id = grnl.variant_id AND pv.tenant_id = grnl.tenant_id
JOIN erp_inventory.stock_bin b ON b.bin_id = grnl.bin_id AND b.tenant_id = grnl.tenant_id
WHERE grnl.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND grnl.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY grnl.grn_id, grnl.line_number;

\echo '=========================================================================='
\echo '5. Outbox Events in erp_purchasing.outbox_event'
\echo '=========================================================================='
SELECT 
    oe.event_id,
    oe.grn_id,
    oe.event_type,
    oe.status,
    oe.attempt_count,
    oe.last_error,
    oe.next_retry_at,
    oe.published_at,
    oe.occurred_at,
    oe.payload
FROM erp_purchasing.outbox_event oe
WHERE oe.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND oe.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY oe.occurred_at ASC;

\echo '=========================================================================='
\echo '6. Accounting & GL Configuration (finance.fiscal_periods & finance.accounts)'
\echo '=========================================================================='
SELECT 
    fp.id AS fiscal_period_id,
    fp.tenant_id,
    fp.company_id,
    fp.name,
    fp.status,
    fp.starts_on,
    fp.ends_on
FROM finance.fiscal_periods fp
WHERE fp.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND fp.company_id = '22222222-2222-4222-8222-222222222223'::uuid;

SELECT 
    a.id AS account_id,
    a.tenant_id,
    a.company_id,
    a.account_code,
    a.account_name,
    a.account_type,
    a.is_active
FROM finance.accounts a
WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY a.account_code;

\echo '=========================================================================='
\echo '7. Journals in finance.journals'
\echo '=========================================================================='
SELECT 
    j.id,
    j.journal_no,
    j.journal_date,
    j.source_module,
    j.source_type,
    j.source_id,
    j.status,
    j.posted_at
FROM finance.journals j
WHERE j.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND j.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY j.journal_date;
