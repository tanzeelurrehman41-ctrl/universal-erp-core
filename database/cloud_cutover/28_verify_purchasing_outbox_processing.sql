-- Universal ERP -- Post-Processing Verification: Inventory Sync & GL Journal Posting
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Purpose: Post-processing verification after outbox events are redriven and processed.
-- Verification Criteria:
--   1. Exactly 2 GRN inventory movements exist without duplication (total +50 EA).
--   2. Stock balances reflect exactly 58.0000 EA total on hand.
--   3. Exactly 2 GL journals exist, are strictly balanced (Debit = Credit), and total 2,250.00 PKR.
--   4. Exactly 2 outbox events have status = 'published' with published_at populated and last_error IS NULL.

\echo '=========================================================================='
\echo 'UNIVERSAL ERP -- POST-PROCESSING VERIFICATION SUITE'
\echo '=========================================================================='

SELECT current_database() AS connected_database, current_user AS connected_user, version() AS pg_version;

\echo ''
\echo '=========================================================================='
\echo '1. Inventory Movements & Legs for Target GRNs (Expected: 2 movements, +50 EA)'
\echo '=========================================================================='
SELECT 
    m.movement_id,
    m.movement_type,
    m.source_document_type,
    m.source_document_id,
    m.occurred_at,
    count(l.leg_number) AS leg_count,
    COALESCE(sum(l.quantity_delta), 0) AS total_delta_qty
FROM erp_inventory.inventory_movement m
LEFT JOIN erp_inventory.inventory_movement_leg l 
  ON l.movement_id = m.movement_id AND l.tenant_id = m.tenant_id AND l.company_id = m.company_id
WHERE m.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND m.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND m.source_document_type = 'grn'
  AND m.source_document_id IN (
      '41a9d790-2bc9-4893-b2ac-bd6800192173',
      '07da9829-4327-4545-9bf5-117ef50fd756'
  )
GROUP BY m.movement_id, m.movement_type, m.source_document_type, m.source_document_id, m.occurred_at
ORDER BY m.occurred_at ASC;

SELECT 
    l.movement_id,
    l.leg_number,
    pv.sku,
    w.code AS warehouse_code,
    b.code AS bin_code,
    l.status_code,
    l.quantity_delta
FROM erp_inventory.inventory_movement_leg l
JOIN erp_inventory.inventory_movement m 
  ON m.movement_id = l.movement_id AND m.tenant_id = l.tenant_id AND m.company_id = l.company_id
JOIN erp_inventory.product_variant pv 
  ON pv.variant_id = l.variant_id AND pv.tenant_id = l.tenant_id
JOIN erp_inventory.warehouse w 
  ON w.warehouse_id = l.warehouse_id AND w.tenant_id = l.tenant_id
JOIN erp_inventory.stock_bin b 
  ON b.bin_id = l.bin_id AND b.tenant_id = l.tenant_id
WHERE l.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND l.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND m.source_document_id IN (
      '41a9d790-2bc9-4893-b2ac-bd6800192173',
      '07da9829-4327-4545-9bf5-117ef50fd756'
  )
ORDER BY l.movement_id, l.leg_number;

\echo ''
\echo '=========================================================================='
\echo '2. Current Stock Balances by Warehouse and Bin (Expected Total: 58.0000 EA)'
\echo '=========================================================================='
SELECT 
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
  AND sb.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY w.code, b.code;

SELECT 
    pv.sku,
    sum(sb.on_hand_qty) AS aggregate_on_hand_qty
FROM erp_inventory.stock_balance sb
JOIN erp_inventory.product_variant pv ON pv.variant_id = sb.variant_id AND pv.tenant_id = sb.tenant_id
WHERE sb.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND sb.company_id = '22222222-2222-4222-8222-222222222223'::uuid
GROUP BY pv.sku;

\echo ''
\echo '=========================================================================='
\echo '3. GL Journals and Double-Entry Lines (Expected: 2 journals, Balanced, 2,250 PKR)'
\echo '=========================================================================='
SELECT 
    j.id AS journal_id,
    j.journal_no,
    j.journal_date,
    j.source_module,
    j.source_type,
    j.source_id,
    j.status,
    count(jl.line_no) AS line_count,
    sum(jl.debit_functional) AS total_debit,
    sum(jl.credit_functional) AS total_credit,
    CASE WHEN sum(jl.debit_functional) = sum(jl.credit_functional) THEN 'BALANCED' ELSE 'UNBALANCED' END AS balance_status
FROM finance.journals j
JOIN finance.journal_lines jl ON jl.journal_id = j.id AND jl.tenant_id = j.tenant_id AND jl.company_id = j.company_id
WHERE j.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND j.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND j.source_module = 'purchasing'
  AND j.source_type = 'grn'
  AND j.source_id IN (
      '41a9d790-2bc9-4893-b2ac-bd6800192173',
      '07da9829-4327-4545-9bf5-117ef50fd756'
  )
GROUP BY j.id, j.journal_no, j.journal_date, j.source_module, j.source_type, j.source_id, j.status
ORDER BY j.journal_date ASC;

SELECT 
    j.journal_no,
    jl.line_no,
    a.account_code,
    a.account_name,
    jl.description,
    jl.debit_functional,
    jl.credit_functional
FROM finance.journal_lines jl
JOIN finance.journals j ON j.id = jl.journal_id AND j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id
JOIN finance.accounts a ON a.id = jl.account_id AND a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id
WHERE jl.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND jl.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND j.source_id IN (
      '41a9d790-2bc9-4893-b2ac-bd6800192173',
      '07da9829-4327-4545-9bf5-117ef50fd756'
  )
ORDER BY j.journal_no, jl.line_no;

\echo ''
\echo '=========================================================================='
\echo '4. Purchasing Outbox Status (Expected: 2 events, published, last_error IS NULL)'
\echo '=========================================================================='
SELECT 
    oe.event_id,
    oe.grn_id,
    oe.event_type,
    oe.status,
    oe.attempt_count,
    oe.redrive_count,
    oe.last_error,
    oe.published_at,
    oe.occurred_at
FROM erp_purchasing.outbox_event oe
WHERE oe.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND oe.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND oe.grn_id IN (
      '41a9d790-2bc9-4893-b2ac-bd6800192173'::uuid,
      '07da9829-4327-4545-9bf5-117ef50fd756'::uuid
  )
ORDER BY oe.occurred_at ASC;

\echo ''
\echo '=========================================================================='
\echo '5. Automated Assertion Suite & Verdicts'
\echo '=========================================================================='
DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_movement_count integer;
    v_duplicate_movements integer;
    v_total_stock numeric;
    v_journal_count integer;
    v_unbalanced_journals integer;
    v_total_debit numeric;
    v_total_credit numeric;
    v_published_outbox_count integer;
    v_all_passed boolean := true;
BEGIN
    -- Check 1: Movements
    SELECT count(DISTINCT movement_id), count(*) - count(DISTINCT source_document_id)
    INTO v_movement_count, v_duplicate_movements
    FROM erp_inventory.inventory_movement
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND source_document_type = 'grn'
      AND source_document_id IN ('41a9d790-2bc9-4893-b2ac-bd6800192173', '07da9829-4327-4545-9bf5-117ef50fd756');

    IF v_movement_count = 2 AND v_duplicate_movements = 0 THEN
        RAISE NOTICE '[PASS] 1. Inventory Movements: Exactly 2 movements exist without duplication.';
    ELSE
        RAISE WARNING '[FAIL] 1. Inventory Movements: Found % movement(s) with % duplicate(s) (expected 2, 0).', v_movement_count, v_duplicate_movements;
        v_all_passed := false;
    END IF;

    -- Check 2: Stock Balance
    SELECT COALESCE(sum(on_hand_qty), 0) INTO v_total_stock
    FROM erp_inventory.stock_balance
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id;

    IF v_total_stock = 58.0000 THEN
        RAISE NOTICE '[PASS] 2. Stock Balance: Aggregate on-hand quantity is exactly 58.0000 EA.';
    ELSE
        RAISE WARNING '[FAIL] 2. Stock Balance: Aggregate on-hand quantity is % EA (expected 58.0000 EA).', v_total_stock;
        v_all_passed := false;
    END IF;

    -- Check 3: GL Journals
    SELECT count(DISTINCT j.id),
           count(DISTINCT j.id) FILTER (WHERE sum_debit <> sum_credit),
           COALESCE(sum(sum_debit), 0),
           COALESCE(sum(sum_credit), 0)
    INTO v_journal_count, v_unbalanced_journals, v_total_debit, v_total_credit
    FROM (
        SELECT j.id, sum(jl.debit_functional) AS sum_debit, sum(jl.credit_functional) AS sum_credit
        FROM finance.journals j
        JOIN finance.journal_lines jl ON jl.journal_id = j.id AND jl.tenant_id = j.tenant_id AND jl.company_id = j.company_id
        WHERE j.tenant_id = v_tenant_id AND j.company_id = v_company_id
          AND j.source_module = 'purchasing' AND j.source_type = 'grn'
          AND j.source_id IN ('41a9d790-2bc9-4893-b2ac-bd6800192173', '07da9829-4327-4545-9bf5-117ef50fd756')
        GROUP BY j.id
    ) j;

    IF v_journal_count = 2 AND v_unbalanced_journals = 0 AND v_total_debit = 2250.00 AND v_total_credit = 2250.00 THEN
        RAISE NOTICE '[PASS] 3. GL Journals: Exactly 2 journals exist, both balanced, totaling 2,250.00 PKR debit and credit.';
    ELSE
        RAISE WARNING '[FAIL] 3. GL Journals: Journals count=%, Unbalanced=%, TotalDebit=%, TotalCredit=% (expected 2, 0, 2250.00, 2250.00).',
            v_journal_count, v_unbalanced_journals, v_total_debit, v_total_credit;
        v_all_passed := false;
    END IF;

    -- Check 4: Outbox Events
    SELECT count(*) INTO v_published_outbox_count
    FROM erp_purchasing.outbox_event
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND grn_id IN ('41a9d790-2bc9-4893-b2ac-bd6800192173'::uuid, '07da9829-4327-4545-9bf5-117ef50fd756'::uuid)
      AND status = 'published'
      AND published_at IS NOT NULL
      AND last_error IS NULL;

    IF v_published_outbox_count = 2 THEN
        RAISE NOTICE '[PASS] 4. Purchasing Outbox: Exactly 2 target outbox events marked published with published_at and last_error NULL.';
    ELSE
        RAISE WARNING '[FAIL] 4. Purchasing Outbox: Found % published event(s) meeting all criteria (expected 2).', v_published_outbox_count;
        v_all_passed := false;
    END IF;

    -- Final Verdict
    IF v_all_passed THEN
        RAISE NOTICE '==========================================================================';
        RAISE NOTICE '=== ALL POST-PROCESSING VERIFICATIONS PASSED SUCCESSFULLY              ===';
        RAISE NOTICE '==========================================================================';
    ELSE
        RAISE EXCEPTION 'POST-PROCESSING VERIFICATION FAILED: One or more assertions did not pass.';
    END IF;
END $$;
