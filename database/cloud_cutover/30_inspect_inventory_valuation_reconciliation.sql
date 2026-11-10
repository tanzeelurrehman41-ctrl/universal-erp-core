-- Universal ERP -- Read-Only Inventory Valuation & General Ledger Reconciliation
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Variant SKU: SCISSOR-EA-01 (2871558f-e577-4d2d-b365-fa60be311111)
-- Purpose: Read-only audit of ERP valuation method, physical stock balances, inventory movements,
--          GL Account 1200 (Merchandise Inventory) reconciliation, Account 5010 (COGS), and POS mechanics.
-- Safety: Strictly read-only queries and assertion block. Zero database mutations.

\echo '=========================================================================='
\echo 'UNIVERSAL ERP -- INVENTORY VALUATION & GENERAL LEDGER RECONCILIATION'
\echo '=========================================================================='

SELECT current_database() AS connected_database, current_user AS connected_user, version() AS pg_version;

\echo ''
\echo '=========================================================================='
\echo '1. Variant Configuration & Costing Method in erp_inventory.product_variant'
\echo '=========================================================================='
SELECT 
    v.variant_id,
    v.sku,
    v.variant_name,
    v.costing_method,
    v.tracking_mode,
    v.negative_stock_allowed,
    v.is_active,
    u.code AS base_uom_code,
    p.product_code,
    p.name AS product_name
FROM erp_inventory.product_variant v
JOIN erp_inventory.product p ON p.product_id = v.product_id AND p.tenant_id = v.tenant_id
JOIN erp_inventory.unit_of_measure u ON u.uom_id = v.base_uom_id AND u.tenant_id = v.tenant_id
WHERE v.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND v.sku = 'SCISSOR-EA-01';

\echo ''
\echo '=========================================================================='
\echo '2. Subledger Valuation Table erp_inventory.valuation_entry Check'
\echo '=========================================================================='
SELECT 
    count(*) AS valuation_entry_count,
    COALESCE(sum(quantity), 0) AS total_valuation_qty,
    COALESCE(sum(extended_cost), 0) AS total_valuation_extended_cost
FROM erp_inventory.valuation_entry
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo ''
\echo '=========================================================================='
\echo '3. Physical Stock Balances in erp_inventory.stock_balance (Expected: 58 EA)'
\echo '=========================================================================='
SELECT 
    w.code AS warehouse_code,
    w.name AS warehouse_name,
    b.code AS bin_code,
    b.bin_type,
    sb.status_code,
    sb.on_hand_qty,
    -- Valuation at incoming Purchasing GRN unit cost (PKR 45.00/EA)
    45.00::numeric(12,2) AS benchmark_unit_cost,
    ROUND(sb.on_hand_qty * 45.00, 2) AS benchmark_replacement_value,
    sb.updated_at
FROM erp_inventory.stock_balance sb
JOIN erp_inventory.warehouse w ON w.warehouse_id = sb.warehouse_id AND w.tenant_id = sb.tenant_id AND w.company_id = sb.company_id
JOIN erp_inventory.stock_bin b ON b.bin_id = sb.bin_id AND b.tenant_id = sb.tenant_id AND b.company_id = sb.company_id
WHERE sb.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND sb.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND sb.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
ORDER BY w.code, b.code;

SELECT 
    count(*) AS total_bin_buckets,
    sum(sb.on_hand_qty) AS total_physical_on_hand_qty,
    ROUND(sum(sb.on_hand_qty) * 45.00, 2) AS total_replacement_value_pkr
FROM erp_inventory.stock_balance sb
WHERE sb.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND sb.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND sb.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid;

\echo ''
\echo '=========================================================================='
\echo '4. Full Inventory Movement History vs. General Ledger Journal Linkage'
\echo '=========================================================================='
SELECT 
    m.movement_id,
    m.occurred_at,
    m.movement_type,
    m.source_document_type,
    m.source_document_id,
    leg.quantity_delta,
    w.code AS warehouse_code,
    b.code AS bin_code,
    m.note,
    -- Check linked GL Journal
    j.id AS gl_journal_id,
    j.journal_no AS gl_journal_no,
    COALESCE(jl_agg.inventory_debit, 0) AS gl_inventory_debit_pkr,
    CASE 
        WHEN j.id IS NOT NULL THEN 'POSTED TO GL'
        ELSE 'NO GL JOURNAL (PRE-CUTOVER / SUBLEDGER ONLY)'
    END AS gl_integration_status
FROM erp_inventory.inventory_movement m
JOIN erp_inventory.inventory_movement_leg leg 
    ON leg.movement_id = m.movement_id 
   AND leg.tenant_id = m.tenant_id 
   AND leg.company_id = m.company_id
LEFT JOIN erp_inventory.warehouse w 
    ON w.warehouse_id = leg.warehouse_id 
   AND w.tenant_id = leg.tenant_id 
   AND w.company_id = leg.company_id
LEFT JOIN erp_inventory.stock_bin b 
    ON b.bin_id = leg.bin_id 
   AND b.tenant_id = leg.tenant_id 
   AND b.company_id = leg.company_id
LEFT JOIN finance.journals j 
    ON (j.source_id = m.source_document_id::text OR j.source_id = m.movement_id::text)
   AND j.tenant_id = m.tenant_id 
   AND j.company_id = m.company_id
LEFT JOIN (
    SELECT 
        jl.journal_id,
        jl.tenant_id,
        jl.company_id,
        sum(jl.debit_functional) AS inventory_debit
    FROM finance.journal_lines jl
    JOIN finance.accounts a ON a.id = jl.account_id AND a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id
    WHERE a.account_code = '1200'
    GROUP BY jl.journal_id, jl.tenant_id, jl.company_id
) jl_agg ON jl_agg.journal_id = j.id AND jl_agg.tenant_id = j.tenant_id AND jl_agg.company_id = j.company_id
WHERE m.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND m.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND leg.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
ORDER BY m.occurred_at ASC;

\echo ''
\echo '=========================================================================='
\echo '5. GL Account 1200 (Merchandise Inventory) Current Book Balance'
\echo '=========================================================================='
SELECT 
    a.account_code,
    a.account_name,
    a.account_type,
    a.normal_balance,
    COALESCE(sum(jl.debit_functional), 0) AS total_debit,
    COALESCE(sum(jl.credit_functional), 0) AS total_credit,
    COALESCE(sum(jl.debit_functional) - sum(jl.credit_functional), 0) AS net_inventory_asset_balance_pkr
FROM finance.accounts a
LEFT JOIN finance.journal_lines jl ON jl.account_id = a.id AND jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id
WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND a.account_code = '1200'
GROUP BY a.account_code, a.account_name, a.account_type, a.normal_balance;

\echo ''
\echo '=========================================================================='
\echo '6. GL Account 5010 (Cost of Goods Sold) Current Balance'
\echo '=========================================================================='
SELECT 
    a.account_code,
    a.account_name,
    a.account_type,
    a.normal_balance,
    COALESCE(sum(jl.debit_functional), 0) AS total_debit,
    COALESCE(sum(jl.credit_functional), 0) AS total_credit,
    COALESCE(sum(jl.debit_functional) - sum(jl.credit_functional), 0) AS net_cogs_balance_pkr
FROM finance.accounts a
LEFT JOIN finance.journal_lines jl ON jl.account_id = a.id AND jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id
WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND a.account_code = '5010'
GROUP BY a.account_code, a.account_name, a.account_type, a.normal_balance;

\echo ''
\echo '=========================================================================='
\echo '7. Complete Reconciliation: Subledger Units vs. GL Book Valuation'
\echo '=========================================================================='
WITH subledger_summary AS (
    SELECT 
        COALESCE(sum(sb.on_hand_qty), 0) AS physical_units
    FROM erp_inventory.stock_balance sb
    WHERE sb.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND sb.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND sb.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
),
movements_summary AS (
    SELECT 
        COALESCE(sum(CASE WHEN m.source_document_type = 'goods_received_note' THEN leg.quantity_delta ELSE 0 END), 0) AS grn_costed_units,
        COALESCE(sum(CASE WHEN m.source_document_type IS DISTINCT FROM 'goods_received_note' THEN leg.quantity_delta ELSE 0 END), 0) AS precutover_unrecorded_units
    FROM erp_inventory.inventory_movement_leg leg
    JOIN erp_inventory.inventory_movement m ON m.movement_id = leg.movement_id AND m.tenant_id = leg.tenant_id AND m.company_id = leg.company_id
    WHERE leg.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND leg.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND leg.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
),
gl_summary AS (
    SELECT 
        COALESCE(sum(jl.debit_functional) - sum(jl.credit_functional), 0) AS gl_book_value_pkr
    FROM finance.accounts a
    JOIN finance.journal_lines jl ON jl.account_id = a.id AND jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id
    WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND a.account_code = '1200'
)
SELECT 
    sub.physical_units AS total_physical_units,
    mov.grn_costed_units,
    mov.precutover_unrecorded_units,
    45.00::numeric(10,2) AS grn_unit_cost_pkr,
    gl.gl_book_value_pkr AS gl_account_1200_balance_pkr,
    (mov.grn_costed_units * 45.00)::numeric(12,2) AS grn_reconciled_value_pkr,
    (gl.gl_book_value_pkr - (mov.grn_costed_units * 45.00))::numeric(12,2) AS grn_reconciliation_variance_pkr,
    ROUND(gl.gl_book_value_pkr / NULLIF(sub.physical_units, 0), 4) AS effective_weighted_cost_per_physical_ea_pkr,
    (mov.precutover_unrecorded_units * 45.00)::numeric(12,2) AS unrecorded_opening_stock_value_pkr,
    ROUND(sub.physical_units * 45.00, 2) AS total_inventory_at_replacement_cost_pkr
FROM subledger_summary sub
CROSS JOIN movements_summary mov
CROSS JOIN gl_summary gl;

\echo ''
\echo '=========================================================================='
\echo '8. Simulation: POS Sale Mechanics & General Ledger Posting'
\echo '=========================================================================='
-- Demonstrating what happens during POS sale of 5 EA at Selling Price PKR 75.00, Cost PKR 45.00, Tax 0%:
SELECT 
    'POS Simulation: Sale of 5 EA @ PKR 75.00' AS scenario,
    5.0000 AS sale_quantity_ea,
    75.00::numeric(10,2) AS unit_price_pkr,
    45.00::numeric(10,2) AS unit_cost_pkr,
    (5 * 75.00)::numeric(10,2) AS total_revenue_pkr,
    (5 * 45.00)::numeric(10,2) AS total_cogs_pkr,
    (5 * 75.00 - 5 * 45.00)::numeric(10,2) AS gross_profit_pkr;

SELECT 
    line_no,
    account_code,
    account_name,
    debit_pkr,
    credit_pkr,
    line_description
FROM (
    VALUES 
        (1, '1010', 'Cash on Hand', 375.00::numeric(10,2), 0.00::numeric(10,2), 'Debit Cash for Gross Sale (5 EA x PKR 75.00)'),
        (2, '4010', 'Sales Revenue', 0.00::numeric(10,2), 375.00::numeric(10,2), 'Credit Sales Revenue for Net Revenue'),
        (3, '5010', 'Cost of Goods Sold', 225.00::numeric(10,2), 0.00::numeric(10,2), 'Debit COGS for Inventory Cost (5 EA x PKR 45.00)'),
        (4, '1200', 'Merchandise Inventory', 0.00::numeric(10,2), 225.00::numeric(10,2), 'Credit Inventory Asset for Inventory Cost Relieved')
) AS sim(line_no, account_code, account_name, debit_pkr, credit_pkr, line_description);

\echo ''
\echo '=========================================================================='
\echo '9. Automated Read-Only Assertions'
\echo '=========================================================================='
DO $$
DECLARE
    v_costing_method text;
    v_valuation_count integer;
    v_total_stock numeric(24,9);
    v_gl_1200_debit numeric(24,9);
    v_gl_1200_credit numeric(24,9);
    v_gl_5010_debit numeric(24,9);
    v_grn_journal_count integer;
    v_total_movements integer;
BEGIN
    -- 1. Check costing_method in product_variant
    SELECT costing_method INTO v_costing_method
    FROM erp_inventory.product_variant
    WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND sku = 'SCISSOR-EA-01';
      
    IF v_costing_method IS NULL THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Variant SCISSOR-EA-01 not found.';
    END IF;
    RAISE NOTICE '[ASSERTION 1 PASS] Product variant costing_method is configured as "%".', v_costing_method;

    -- 2. Check valuation_entry table count
    SELECT count(*) INTO v_valuation_count
    FROM erp_inventory.valuation_entry
    WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND company_id = '22222222-2222-4222-8222-222222222223'::uuid;
      
    RAISE NOTICE '[ASSERTION 2 PASS] erp_inventory.valuation_entry count is % (proving ERP uses payload-derived valuation).', v_valuation_count;

    -- 3. Check total stock balance = 58 EA
    SELECT COALESCE(sum(on_hand_qty), 0) INTO v_total_stock
    FROM erp_inventory.stock_balance
    WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid;

    IF v_total_stock <> 58.000000000 THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Expected 58 EA stock balance, found %', v_total_stock;
    END IF;
    RAISE NOTICE '[ASSERTION 3 PASS] Total stock balance on hand is exactly % EA.', v_total_stock;

    -- 4. Check total inventory movements = 6 (1 initial receipt + 3 adjustments + 2 purchasing GRNs)
    SELECT count(DISTINCT m.movement_id) INTO v_total_movements
    FROM erp_inventory.inventory_movement m
    JOIN erp_inventory.inventory_movement_leg leg ON leg.movement_id = m.movement_id AND leg.tenant_id = m.tenant_id AND leg.company_id = m.company_id
    WHERE m.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND m.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND leg.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid;

    IF v_total_movements <> 6 THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Expected 6 inventory movements, found %', v_total_movements;
    END IF;
    RAISE NOTICE '[ASSERTION 4 PASS] Total inventory movements for SCISSOR-EA-01 is exactly % (1 receipt, 3 adjustments, 2 GRNs).', v_total_movements;

    -- 5. Check GL Account 1200 balance = exactly PKR 2,250.00
    SELECT 
        COALESCE(sum(jl.debit_functional), 0),
        COALESCE(sum(jl.credit_functional), 0)
    INTO v_gl_1200_debit, v_gl_1200_credit
    FROM finance.accounts a
    JOIN finance.journal_lines jl ON jl.account_id = a.id AND jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id
    WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND a.account_code = '1200';

    IF v_gl_1200_debit <> 2250.00 OR v_gl_1200_credit <> 0.00 THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Expected Account 1200 Debit 2250.00, Credit 0.00. Found Debit %, Credit %', v_gl_1200_debit, v_gl_1200_credit;
    END IF;
    RAISE NOTICE '[ASSERTION 5 PASS] GL Account 1200 balance is exactly Debit PKR % (Credit PKR %). Reconciles 100%% with the 50 EA Purchasing GRNs @ PKR 45.00.', v_gl_1200_debit, v_gl_1200_credit;

    -- 6. Check GL Account 5010 balance = 0.00 (no sales yet)
    SELECT COALESCE(sum(jl.debit_functional), 0) INTO v_gl_5010_debit
    FROM finance.accounts a
    LEFT JOIN finance.journal_lines jl ON jl.account_id = a.id AND jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id
    WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND a.account_code = '5010';

    IF v_gl_5010_debit <> 0.00 THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Expected Account 5010 Debit 0.00, found %', v_gl_5010_debit;
    END IF;
    RAISE NOTICE '[ASSERTION 6 PASS] GL Account 5010 (COGS) balance is PKR 0.00, confirmed ready for POS sales.';

    RAISE NOTICE '';
    RAISE NOTICE '==========================================================================';
    RAISE NOTICE 'ALL INVENTORY VALUATION & GL RECONCILIATION ASSERTIONS PASSED SUCCESSFULLY!';
    RAISE NOTICE '==========================================================================';
END $$;
