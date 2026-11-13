-- Universal ERP -- Verify Neon FIFO Valuation & Backfill State (Model A)
-- Target Database: Neon PostgreSQL Cloud (neondb)
-- Target Tenant  : DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company : CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target SKU     : SCISSOR-EA-01 (2871558f-e577-4d2d-b365-fa60be311111)

BEGIN;

SELECT set_config('app.tenant_id', '22222222-2222-4222-8222-222222222222', true);
SELECT set_config('app.company_id', '22222222-2222-4222-8222-222222222223', true);

-- 1. Schema Migration Version Check
SELECT version, applied_at
FROM public.schema_migrations
WHERE version = '026_fifo_valuation_layers';

-- 2. FIFO Cost Layers Summary by Bin
SELECT 
    w.code AS warehouse_code,
    b.code AS bin_code,
    cl.layer_id,
    cl.source_document_type,
    cl.received_at,
    cl.original_qty,
    cl.remaining_qty,
    cl.unit_cost,
    (cl.remaining_qty * cl.unit_cost) AS remaining_valuation,
    cl.currency_code,
    cl.status_code
FROM erp_inventory.inventory_cost_layer cl
JOIN erp_inventory.warehouse w
  ON w.tenant_id = cl.tenant_id AND w.company_id = cl.company_id AND w.warehouse_id = cl.warehouse_id
JOIN erp_inventory.stock_bin b
  ON b.tenant_id = cl.tenant_id AND b.company_id = cl.company_id AND b.bin_id = cl.bin_id
WHERE cl.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND cl.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND cl.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
ORDER BY w.code, b.code, cl.received_at;

-- 3. Stock Balance vs Valued Quantity by Bin (Model A Proof)
SELECT 
    w.code AS warehouse_code,
    b.code AS bin_code,
    sb.on_hand_qty AS physical_on_hand,
    COALESCE(SUM(cl.remaining_qty), 0) AS valued_fifo_qty,
    sb.on_hand_qty - COALESCE(SUM(cl.remaining_qty), 0) AS unvalued_qty,
    COALESCE(SUM(cl.remaining_qty * cl.unit_cost), 0) AS valued_amount_pkr,
    CASE 
        WHEN COALESCE(SUM(cl.remaining_qty), 0) = 0 AND sb.on_hand_qty > 0 THEN 'BLOCKED_UNVALUED'
        WHEN COALESCE(SUM(cl.remaining_qty), 0) < sb.on_hand_qty THEN 'PARTIALLY_VALUED'
        ELSE 'AVAILABLE_FOR_SALE'
    END AS pos_status
FROM erp_inventory.stock_balance sb
JOIN erp_inventory.warehouse w
  ON w.tenant_id = sb.tenant_id AND w.company_id = sb.company_id AND w.warehouse_id = sb.warehouse_id
JOIN erp_inventory.stock_bin b
  ON b.tenant_id = sb.tenant_id AND b.company_id = sb.company_id AND b.bin_id = sb.bin_id
LEFT JOIN erp_inventory.inventory_cost_layer cl
  ON cl.tenant_id = sb.tenant_id AND cl.company_id = sb.company_id
 AND cl.warehouse_id = sb.warehouse_id AND cl.bin_id = sb.bin_id
 AND cl.variant_id = sb.variant_id AND cl.status_code = 'available'
WHERE sb.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND sb.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND sb.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
  AND sb.status_code = 'available'
GROUP BY w.code, b.code, sb.on_hand_qty
ORDER BY w.code, b.code;

-- 4. Historical Consumption Allocations Audit
SELECT 
    ca.allocation_id,
    ca.source_document_type,
    si.invoice_number,
    ca.source_line_number,
    ca.consumed_qty,
    ca.unit_cost,
    ca.extended_cost,
    ca.allocated_at
FROM erp_inventory.inventory_cost_allocation ca
LEFT JOIN erp_sales.sales_invoice si
  ON si.tenant_id = ca.tenant_id AND si.company_id = ca.company_id AND si.invoice_id = ca.source_document_id
WHERE ca.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND ca.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY ca.allocated_at;

-- 5. Valuation Entries Ledger Summary
SELECT 
    entry_type,
    COUNT(*) AS entry_count,
    SUM(quantity) AS total_quantity,
    SUM(extended_cost) AS total_extended_cost
FROM erp_inventory.valuation_entry
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
GROUP BY entry_type
ORDER BY entry_type;

-- 6. General Ledger Account 1200 Current Balance vs FIFO Valuation
SELECT 
    'Account 1200 (GL Balance)' AS metric,
    COALESCE(SUM(jl.debit_functional) - SUM(jl.credit_functional), 0) AS amount_pkr
FROM finance.journal_lines jl
JOIN finance.accounts a
  ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
JOIN finance.journals j
  ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
WHERE jl.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND jl.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND a.account_code = '1200' AND j.status = 'posted'
UNION ALL
SELECT 
    'FIFO Physical Layer Valuation' AS metric,
    COALESCE(SUM(remaining_qty * unit_cost), 0) AS amount_pkr
FROM erp_inventory.inventory_cost_layer
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
  AND status_code = 'available'
UNION ALL
SELECT 
    'Accounting Variance (Unposted Proposal)' AS metric,
    COALESCE(SUM(remaining_qty * unit_cost), 0) - 1980.0000 AS amount_pkr
FROM erp_inventory.inventory_cost_layer
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
  AND status_code = 'available';

-- 7. Strict Assertion Verification
DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_variant_id uuid := '2871558f-e577-4d2d-b365-fa60be311111'::uuid;
    v_total_layers integer;
    v_total_valued_qty numeric(24,9);
    v_total_valued_cost numeric(24,9);
    v_alloc_count integer;
    v_val_entries_sum numeric(24,9);
    v_gl_1200 numeric(20,4);
BEGIN
    SELECT COUNT(*), COALESCE(SUM(remaining_qty), 0), COALESCE(SUM(remaining_qty * unit_cost), 0)
      INTO v_total_layers, v_total_valued_qty, v_total_valued_cost
    FROM erp_inventory.inventory_cost_layer
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND variant_id = v_variant_id AND status_code = 'available';

    IF v_total_layers <> 2 OR v_total_valued_qty <> 47 OR v_total_valued_cost <> 2115.000000000 THEN
        RAISE EXCEPTION 'VERIFICATION_FAILED: Expected 2 layers, 47 EA, PKR 2,115.00. Got layers=%, qty=%, cost=%',
            v_total_layers, v_total_valued_qty, v_total_valued_cost;
    END IF;

    SELECT COUNT(*) INTO v_alloc_count
    FROM erp_inventory.inventory_cost_allocation
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id;

    IF v_alloc_count <> 2 THEN
        RAISE EXCEPTION 'VERIFICATION_FAILED: Expected 2 allocations. Got %', v_alloc_count;
    END IF;

    SELECT COALESCE(SUM(extended_cost), 0) INTO v_val_entries_sum
    FROM erp_inventory.valuation_entry
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id AND variant_id = v_variant_id;

    IF v_val_entries_sum <> 2115.000000000 THEN
        RAISE EXCEPTION 'VERIFICATION_FAILED: Expected valuation entries sum 2,115.00. Got %', v_val_entries_sum;
    END IF;

    SELECT COALESCE(SUM(jl.debit_functional) - SUM(jl.credit_functional), 0) INTO v_gl_1200
    FROM finance.journal_lines jl
    JOIN finance.accounts a
      ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
    JOIN finance.journals j
      ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
    WHERE jl.tenant_id = v_tenant_id AND jl.company_id = v_company_id
      AND a.account_code = '1200' AND j.status = 'posted';

    IF v_gl_1200 <> 1980.0000 THEN
        RAISE EXCEPTION 'VERIFICATION_FAILED: Expected GL 1200 to remain 1,980.00. Got %', v_gl_1200;
    END IF;

    RAISE NOTICE '[SUCCESS] All Model A FIFO valuation invariants strictly verified on Neon neondb.';
END $$;

COMMIT;
