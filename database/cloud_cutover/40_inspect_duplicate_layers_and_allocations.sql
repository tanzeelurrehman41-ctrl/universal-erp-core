-- Universal ERP -- Read-Only Inspection for Duplicate Cost Layers and Allocations
-- Target Database: Neon PostgreSQL Cloud (neondb)
-- Target Tenant  : DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company : CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)

BEGIN;

SELECT set_config('app.tenant_id', '22222222-2222-4222-8222-222222222222', true);
SELECT set_config('app.company_id', '22222222-2222-4222-8222-222222222223', true);

-- 1. Check duplicate receipt cost layers
SELECT 
    'Duplicate Receipt Layers' AS check_name,
    source_document_type,
    source_document_id,
    source_line_number,
    bin_id,
    COUNT(*) AS duplicate_count
FROM erp_inventory.inventory_cost_layer
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND parent_layer_id IS NULL
GROUP BY source_document_type, source_document_id, source_line_number, bin_id
HAVING COUNT(*) > 1;

-- 2. Check duplicate transfer child layers
SELECT 
    'Duplicate Transfer Child Layers' AS check_name,
    parent_layer_id,
    source_movement_id,
    COUNT(*) AS duplicate_count
FROM erp_inventory.inventory_cost_layer
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND parent_layer_id IS NOT NULL
GROUP BY parent_layer_id, source_movement_id
HAVING COUNT(*) > 1;

-- 3. Check duplicate cost allocations
SELECT 
    'Duplicate Cost Allocations' AS check_name,
    source_document_type,
    source_document_id,
    source_line_number,
    layer_id,
    COUNT(*) AS duplicate_count
FROM erp_inventory.inventory_cost_allocation
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
GROUP BY source_document_type, source_document_id, source_line_number, layer_id
HAVING COUNT(*) > 1;

-- 4. Check duplicate valuation entries
SELECT 
    'Duplicate Valuation Entries' AS check_name,
    movement_id,
    entry_type,
    COUNT(*) AS duplicate_count
FROM erp_inventory.valuation_entry
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
GROUP BY movement_id, entry_type
HAVING COUNT(*) > 1;

-- 5. Complete Inventory Cost Layers Inventory
SELECT 
    layer_id,
    warehouse_id,
    bin_id,
    source_document_type,
    received_at,
    original_qty,
    remaining_qty,
    unit_cost,
    remaining_qty * unit_cost AS extended_cost,
    status_code
FROM erp_inventory.inventory_cost_layer
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY received_at;

-- 6. Complete Inventory Cost Allocations Inventory
SELECT 
    allocation_id,
    layer_id,
    movement_id,
    source_document_type,
    source_document_id,
    source_line_number,
    consumed_qty,
    unit_cost,
    extended_cost,
    allocated_at
FROM erp_inventory.inventory_cost_allocation
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY allocated_at;

-- 7. Assert Zero Duplicates Invariant
DO $$
DECLARE
    v_dup_layers integer;
    v_dup_transfers integer;
    v_dup_allocs integer;
    v_dup_val_entries integer;
BEGIN
    SELECT COUNT(*) INTO v_dup_layers
    FROM (
        SELECT 1
        FROM erp_inventory.inventory_cost_layer
        WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
          AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
          AND parent_layer_id IS NULL
        GROUP BY source_document_type, source_document_id, source_line_number, bin_id
        HAVING COUNT(*) > 1
    ) sub;

    SELECT COUNT(*) INTO v_dup_transfers
    FROM (
        SELECT 1
        FROM erp_inventory.inventory_cost_layer
        WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
          AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
          AND parent_layer_id IS NOT NULL
        GROUP BY parent_layer_id, source_movement_id
        HAVING COUNT(*) > 1
    ) sub;

    SELECT COUNT(*) INTO v_dup_allocs
    FROM (
        SELECT 1
        FROM erp_inventory.inventory_cost_allocation
        WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
          AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
        GROUP BY source_document_type, source_document_id, source_line_number, layer_id
        HAVING COUNT(*) > 1
    ) sub;

    SELECT COUNT(*) INTO v_dup_val_entries
    FROM (
        SELECT 1
        FROM erp_inventory.valuation_entry
        WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
          AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
        GROUP BY movement_id, entry_type
        HAVING COUNT(*) > 1
    ) sub;

    IF v_dup_layers > 0 OR v_dup_transfers > 0 OR v_dup_allocs > 0 OR v_dup_val_entries > 0 THEN
        RAISE EXCEPTION 'DUPLICATE_CHECK_FAILED: dup_layers=%, dup_transfers=%, dup_allocs=%, dup_val_entries=%',
            v_dup_layers, v_dup_transfers, v_dup_allocs, v_dup_val_entries;
    END IF;

    RAISE NOTICE '[PASS] Zero duplicate cost layers, transfers, allocations, or valuation entries detected.';
END $$;

COMMIT;
