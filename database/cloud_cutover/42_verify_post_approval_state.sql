-- Universal ERP -- Verify Post-Approval FIFO Valuation & Ledger State (Model A)
-- Target Database: Neon PostgreSQL Cloud (neondb)
-- Target Tenant  : DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company : CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target SKU     : SCISSOR-EA-01 (2871558f-e577-4d2d-b365-fa60be311111)

BEGIN;

SELECT set_config('app.tenant_id', '22222222-2222-4222-8222-222222222222', true);
SELECT set_config('app.company_id', '22222222-2222-4222-8222-222222222223', true);

-- 1. Active Cost Layers Summary
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
JOIN erp_inventory.warehouse w ON w.tenant_id = cl.tenant_id AND w.company_id = cl.company_id AND w.warehouse_id = cl.warehouse_id
JOIN erp_inventory.stock_bin b ON b.tenant_id = cl.tenant_id AND b.company_id = cl.company_id AND b.bin_id = cl.bin_id
WHERE cl.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND cl.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND cl.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
  AND cl.status_code = 'available'
ORDER BY w.code, b.code, cl.received_at;

-- 2. Physical Stock vs Valued Stock by Bin
SELECT 
    w.code AS warehouse_code,
    b.code AS bin_code,
    sb.on_hand_qty AS physical_on_hand,
    COALESCE(SUM(cl.remaining_qty), 0) AS valued_fifo_qty,
    sb.on_hand_qty - COALESCE(SUM(cl.remaining_qty), 0) AS unvalued_qty,
    COALESCE(SUM(cl.remaining_qty * cl.unit_cost), 0) AS valued_amount_pkr,
    CASE 
        WHEN sb.on_hand_qty = COALESCE(SUM(cl.remaining_qty), 0) AND sb.on_hand_qty > 0 THEN '100% VALUED & AVAILABLE'
        WHEN COALESCE(SUM(cl.remaining_qty), 0) = 0 AND sb.on_hand_qty > 0 THEN 'BLOCKED_UNVALUED'
        ELSE 'AVAILABLE_FOR_SALE'
    END AS pos_status
FROM erp_inventory.stock_balance sb
JOIN erp_inventory.warehouse w ON w.tenant_id = sb.tenant_id AND w.company_id = sb.company_id AND w.warehouse_id = sb.warehouse_id
JOIN erp_inventory.stock_bin b ON b.tenant_id = sb.tenant_id AND b.company_id = sb.company_id AND b.bin_id = sb.bin_id
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

-- 3. GL Account 1200 vs FIFO Valuation (Post-Approval Zero Variance)
SELECT 
    'Account 1200 (GL Balance)' AS metric,
    COALESCE(SUM(jl.debit_functional) - SUM(jl.credit_functional), 0) AS amount_pkr
FROM finance.journal_lines jl
JOIN finance.accounts a ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
JOIN finance.journals j ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
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
    'Reconciliation Variance (Should be ZERO)' AS metric,
    COALESCE(SUM(remaining_qty * unit_cost), 0) - (
        SELECT COALESCE(SUM(jl2.debit_functional) - SUM(jl2.credit_functional), 0)
        FROM finance.journal_lines jl2
        JOIN finance.accounts a2 ON a2.tenant_id = jl2.tenant_id AND a2.company_id = jl2.company_id AND a2.id = jl2.account_id
        JOIN finance.journals j2 ON j2.tenant_id = jl2.tenant_id AND j2.company_id = jl2.company_id AND j2.id = jl2.journal_id
        WHERE jl2.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
          AND jl2.company_id = '22222222-2222-4222-8222-222222222223'::uuid
          AND a2.account_code = '1200' AND j2.status = 'posted'
    ) AS amount_pkr
FROM erp_inventory.inventory_cost_layer
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
  AND status_code = 'available';

-- 4. Strict Post-Approval Invariants Assertion
DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_variant_id uuid := '2871558f-e577-4d2d-b365-fa60be311111'::uuid;
    v_total_layers integer;
    v_total_valued_qty numeric(24,9);
    v_total_valued_cost numeric(24,9);
    v_gl_1200 numeric(20,4);
    v_gl_3010 numeric(20,4);
    v_jrn_adj_count integer;
    v_jrn_cap_count integer;
    v_pick_unvalued numeric(24,9);
    v_receive_unvalued numeric(24,9);
BEGIN
    -- Check 1: 3 Active Layers, 50 EA Valued, PKR 2,250.00 Total Cost
    SELECT COUNT(*), COALESCE(SUM(remaining_qty), 0), COALESCE(SUM(remaining_qty * unit_cost), 0)
      INTO v_total_layers, v_total_valued_qty, v_total_valued_cost
    FROM erp_inventory.inventory_cost_layer
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND variant_id = v_variant_id AND status_code = 'available';

    IF v_total_layers <> 3 OR v_total_valued_qty <> 50 OR v_total_valued_cost <> 2250.000000000 THEN
        RAISE EXCEPTION 'POST_APPROVAL_FAILED: Expected 3 layers, 50 EA valued, PKR 2,250.00 valuation. Got layers=%, qty=%, cost=%',
            v_total_layers, v_total_valued_qty, v_total_valued_cost;
    END IF;

    -- Check 2: GL 1200 Balance = PKR 2,250.00
    SELECT COALESCE(SUM(jl.debit_functional) - SUM(jl.credit_functional), 0) INTO v_gl_1200
    FROM finance.journal_lines jl
    JOIN finance.accounts a ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
    JOIN finance.journals j ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
    WHERE jl.tenant_id = v_tenant_id AND jl.company_id = v_company_id
      AND a.account_code = '1200' AND j.status = 'posted';

    IF v_gl_1200 <> 2250.0000 THEN
        RAISE EXCEPTION 'POST_APPROVAL_FAILED: Expected GL 1200 balance = PKR 2,250.00. Got %', v_gl_1200;
    END IF;

    -- Check 3: GL 3010 Credited PKR 135.00
    SELECT COALESCE(SUM(jl.credit_functional) - SUM(jl.debit_functional), 0) INTO v_gl_3010
    FROM finance.journal_lines jl
    JOIN finance.accounts a ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
    JOIN finance.journals j ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
    WHERE jl.tenant_id = v_tenant_id AND jl.company_id = v_company_id
      AND a.account_code = '3010' AND j.status = 'posted';

    IF v_gl_3010 < 135.0000 THEN
        RAISE EXCEPTION 'POST_APPROVAL_FAILED: Expected Account 3010 to have at least PKR 135.00 credit. Got %', v_gl_3010;
    END IF;

    -- Check 4: Journals Posted Exactly Once
    SELECT COUNT(*) INTO v_jrn_adj_count
    FROM finance.journals
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id AND journal_no = 'JRN-ADJ-PROP-FIFO-001' AND status = 'posted';

    SELECT COUNT(*) INTO v_jrn_cap_count
    FROM finance.journals
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id AND journal_no = 'JRN-ADJ-PROP-CAP-001' AND status = 'posted';

    IF v_jrn_adj_count <> 1 OR v_jrn_cap_count <> 1 THEN
        RAISE EXCEPTION 'POST_APPROVAL_FAILED: Expected both journals posted exactly once. Got adj=%, cap=%', v_jrn_adj_count, v_jrn_cap_count;
    END IF;

    -- Check 5: Zero Unvalued Units in PICK-01 and RECEIVE-01
    SELECT sb.on_hand_qty - COALESCE(SUM(cl.remaining_qty), 0) INTO v_pick_unvalued
    FROM erp_inventory.stock_balance sb
    JOIN erp_inventory.stock_bin b ON b.bin_id = sb.bin_id AND b.tenant_id = sb.tenant_id
    LEFT JOIN erp_inventory.inventory_cost_layer cl ON cl.bin_id = sb.bin_id AND cl.tenant_id = sb.tenant_id AND cl.variant_id = sb.variant_id AND cl.status_code = 'available'
    WHERE sb.tenant_id = v_tenant_id AND sb.company_id = v_company_id AND sb.variant_id = v_variant_id AND b.code = 'PICK-01' AND sb.status_code = 'available'
    GROUP BY sb.on_hand_qty;

    SELECT sb.on_hand_qty - COALESCE(SUM(cl.remaining_qty), 0) INTO v_receive_unvalued
    FROM erp_inventory.stock_balance sb
    JOIN erp_inventory.stock_bin b ON b.bin_id = sb.bin_id AND b.tenant_id = sb.tenant_id
    LEFT JOIN erp_inventory.inventory_cost_layer cl ON cl.bin_id = sb.bin_id AND cl.tenant_id = sb.tenant_id AND cl.variant_id = sb.variant_id AND cl.status_code = 'available'
    WHERE sb.tenant_id = v_tenant_id AND sb.company_id = v_company_id AND sb.variant_id = v_variant_id AND b.code = 'RECEIVE-01' AND sb.status_code = 'available'
    GROUP BY sb.on_hand_qty;

    IF v_pick_unvalued <> 0 OR v_receive_unvalued <> 0 THEN
        RAISE EXCEPTION 'POST_APPROVAL_FAILED: Expected zero unvalued units. Got PICK-01 unvalued=%, RECEIVE-01 unvalued=%', v_pick_unvalued, v_receive_unvalued;
    END IF;

    RAISE NOTICE '[SUCCESS] All post-approval Model A FIFO invariants 100%% verified on Neon neondb.';
END $$;

COMMIT;
