-- Universal ERP -- Read-Only Source Lineage & FIFO Layer Verification (Model A)
-- Target Database: Neon PostgreSQL Cloud (neondb)
-- Target Tenant  : DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company : CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target SKU     : SCISSOR-EA-01 (2871558f-e577-4d2d-b365-fa60be311111)

BEGIN;

SELECT set_config('app.tenant_id', '22222222-2222-4222-8222-222222222222', true);
SELECT set_config('app.company_id', '22222222-2222-4222-8222-222222222223', true);

-- 1. Detailed FIFO Cost Layer Source Lineage
SELECT 
    cl.layer_id,
    w.code AS warehouse_code,
    b.code AS bin_code,
    cl.source_document_type,
    COALESCE(grn.grn_number, j.journal_no, cl.source_document_id::text) AS canonical_source_ref,
    cl.source_document_id,
    cl.source_line_number,
    cl.received_at,
    cl.original_qty,
    (cl.original_qty - cl.remaining_qty) AS consumed_qty,
    cl.remaining_qty,
    cl.unit_cost,
    (cl.remaining_qty * cl.unit_cost) AS remaining_valuation_pkr,
    cl.currency_code,
    cl.status_code
FROM erp_inventory.inventory_cost_layer cl
JOIN erp_inventory.warehouse w
  ON w.tenant_id = cl.tenant_id AND w.company_id = cl.company_id AND w.warehouse_id = cl.warehouse_id
JOIN erp_inventory.stock_bin b
  ON b.tenant_id = cl.tenant_id AND b.company_id = cl.company_id AND b.bin_id = cl.bin_id
LEFT JOIN erp_purchasing.goods_received_note grn
  ON grn.tenant_id = cl.tenant_id AND grn.company_id = cl.company_id AND grn.grn_id = cl.source_document_id
LEFT JOIN finance.journals j
  ON j.tenant_id = cl.tenant_id AND j.company_id = cl.company_id AND j.id = cl.source_document_id
WHERE cl.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND cl.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND cl.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
  AND cl.status_code = 'available'
ORDER BY cl.received_at ASC;

-- 2. Consumption Allocations Audit (Verifying Consumed Quantities)
SELECT 
    ca.allocation_id,
    ca.layer_id,
    cl.source_document_type AS layer_source_type,
    COALESCE(grn.grn_number, 'OPENING_CAPITAL') AS layer_source_doc,
    ca.source_document_type AS consumption_type,
    si.invoice_number,
    ca.source_line_number,
    ca.consumed_qty,
    ca.unit_cost,
    ca.extended_cost,
    ca.allocated_at
FROM erp_inventory.inventory_cost_allocation ca
JOIN erp_inventory.inventory_cost_layer cl
  ON cl.tenant_id = ca.tenant_id AND cl.company_id = ca.company_id AND cl.layer_id = ca.layer_id
LEFT JOIN erp_purchasing.goods_received_note grn
  ON grn.tenant_id = cl.tenant_id AND grn.company_id = cl.company_id AND grn.grn_id = cl.source_document_id
LEFT JOIN erp_sales.sales_invoice si
  ON si.tenant_id = ca.tenant_id AND si.company_id = ca.company_id AND si.invoice_id = ca.source_document_id
WHERE ca.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND ca.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY ca.allocated_at ASC;

-- 3. Confirm Journals Posted Exactly Once
SELECT 
    journal_no,
    status,
    posted_at,
    COUNT(*) AS instance_count
FROM finance.journals
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND journal_no IN ('JRN-ADJ-PROP-FIFO-001', 'JRN-ADJ-PROP-CAP-001')
GROUP BY journal_no, status, posted_at;

-- 4. Invariant Assertion: Exactly 3 layers, Canonical UUIDs & Quantities, Zero Variance
DO $$
DECLARE
    v_tenant_id        uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id       uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_variant_id       uuid := '2871558f-e577-4d2d-b365-fa60be311111'::uuid;
    
    v_layer1_exists    boolean;
    v_layer2_exists    boolean;
    v_layer3_exists    boolean;
    
    v_total_layers     integer;
    v_total_orig_qty   numeric(24,9);
    v_total_rem_qty    numeric(24,9);
    v_total_rem_val    numeric(24,9);
    v_gl_1200          numeric(20,4);
    v_jrn_adj_count    integer;
    v_jrn_cap_count    integer;
BEGIN
    -- Check Layer 1: e4a1c001-2024-4001-8001-41a9d7900001 (GRN-2024-01: Orig 20 EA, Rem 18 EA)
    SELECT EXISTS (
        SELECT 1 FROM erp_inventory.inventory_cost_layer
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND layer_id = 'e4a1c001-2024-4001-8001-41a9d7900001'::uuid
          AND original_qty = 20.000000000 AND remaining_qty = 18.000000000 AND unit_cost = 45.000000000
    ) INTO v_layer1_exists;

    -- Check Layer 2: e4a1c002-2024-4003-8001-07da98290001 (GRN-2024-03: Orig 30 EA, Rem 29 EA)
    SELECT EXISTS (
        SELECT 1 FROM erp_inventory.inventory_cost_layer
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND layer_id = 'e4a1c002-2024-4003-8001-07da98290001'::uuid
          AND original_qty = 30.000000000 AND remaining_qty = 29.000000000 AND unit_cost = 45.000000000
    ) INTO v_layer2_exists;

    -- Check Layer 3: c3333333-3333-4333-8333-333333333333 (PROP-CAP-001: Orig 3 EA, Rem 3 EA)
    SELECT EXISTS (
        SELECT 1 FROM erp_inventory.inventory_cost_layer
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND layer_id = 'c3333333-3333-4333-8333-333333333333'::uuid
          AND original_qty = 3.000000000 AND remaining_qty = 3.000000000 AND unit_cost = 45.000000000
    ) INTO v_layer3_exists;

    IF NOT (v_layer1_exists AND v_layer2_exists AND v_layer3_exists) THEN
        RAISE EXCEPTION 'LINEAGE_VERIFICATION_FAILED: Canonical layers missing or quantities mismatched. L1=%, L2=%, L3=%',
            v_layer1_exists, v_layer2_exists, v_layer3_exists;
    END IF;

    -- Check Totals
    SELECT COUNT(*), COALESCE(SUM(original_qty), 0), COALESCE(SUM(remaining_qty), 0), COALESCE(SUM(remaining_qty * unit_cost), 0)
      INTO v_total_layers, v_total_orig_qty, v_total_rem_qty, v_total_rem_val
    FROM erp_inventory.inventory_cost_layer
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND variant_id = v_variant_id AND status_code = 'available';

    IF v_total_layers <> 3 OR v_total_orig_qty <> 53 OR v_total_rem_qty <> 50 OR v_total_rem_val <> 2250.000000000 THEN
        RAISE EXCEPTION 'LINEAGE_VERIFICATION_FAILED: Total layers/qty/val mismatch. Layers=%, Orig=%, Rem=%, Val=%',
            v_total_layers, v_total_orig_qty, v_total_rem_qty, v_total_rem_val;
    END IF;

    -- Check GL 1200 Balance = PKR 2,250.00
    SELECT COALESCE(SUM(jl.debit_functional) - SUM(jl.credit_functional), 0) INTO v_gl_1200
    FROM finance.journal_lines jl
    JOIN finance.accounts a ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
    JOIN finance.journals j ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
    WHERE jl.tenant_id = v_tenant_id AND jl.company_id = v_company_id
      AND a.account_code = '1200' AND j.status = 'posted';

    IF v_gl_1200 <> 2250.0000 THEN
        RAISE EXCEPTION 'LINEAGE_VERIFICATION_FAILED: GL 1200 balance % does not match PKR 2,250.00', v_gl_1200;
    END IF;

    -- Check Journals Posted Exactly Once
    SELECT COUNT(*) INTO v_jrn_adj_count
    FROM finance.journals
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND journal_no = 'JRN-ADJ-PROP-FIFO-001' AND status = 'posted';

    SELECT COUNT(*) INTO v_jrn_cap_count
    FROM finance.journals
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND journal_no = 'JRN-ADJ-PROP-CAP-001' AND status = 'posted';

    IF v_jrn_adj_count <> 1 OR v_jrn_cap_count <> 1 THEN
        RAISE EXCEPTION 'LINEAGE_VERIFICATION_FAILED: Expected both journals posted exactly once. Adj=%, Cap=%',
            v_jrn_adj_count, v_jrn_cap_count;
    END IF;

    RAISE NOTICE '[SUCCESS] Source lineage verified: GRN-2024-01 (Orig 20 EA / Rem 18 EA), GRN-2024-03 (Orig 30 EA / Rem 29 EA), Opening Capitalization (Orig 3 EA / Rem 3 EA). Total 50 EA Valued @ PKR 2,250.00 with ZERO variance.';
END $$;

COMMIT;
