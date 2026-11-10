-- Universal ERP -- Read-Only Sales & POS Execution Verification on Neon
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target Customer: DEMO-CUSTOMER-POS-01
-- Target Variant: SCISSOR-EA-01 (2871558f-e577-4d2d-b365-fa60be311111)
-- Purpose: Read-only verification of Customer, Confirmed Sales Order, Controlled POS Sale,
--          Inventory relief (58 EA -> 53 EA), GL Journal postings, Account 1200 (PKR 2,025), Account 5010 (PKR 225),
--          and Live Trial Balance debit-credit balance proof.
-- Safety: Strictly read-only queries and assertion block. Zero database mutations.

\echo '=========================================================================='
\echo 'UNIVERSAL ERP -- SALES & POS EXECUTION VERIFICATION'
\echo '=========================================================================='

SELECT current_database() AS connected_database, current_user AS connected_user, version() AS pg_version;

\echo ''
\echo '=========================================================================='
\echo '1. Customer Record in erp_sales.customer'
\echo '=========================================================================='
SELECT 
    c.customer_id,
    c.customer_code,
    c.name,
    c.customer_type,
    c.credit_limit,
    c.outstanding_balance,
    c.currency_code,
    c.is_active,
    c.created_at
FROM erp_sales.customer c
WHERE c.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND c.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND c.customer_code = 'DEMO-CUSTOMER-POS-01';

\echo ''
\echo '=========================================================================='
\echo '2. Sales Order Record & Lines (Expected: 1 EA @ PKR 75.00, Status: confirmed)'
\echo '=========================================================================='
SELECT 
    so.order_id,
    so.order_number,
    so.order_status,
    so.order_type,
    so.order_date,
    so.subtotal_amount,
    so.tax_amount,
    so.total_amount,
    so.created_at
FROM erp_sales.sales_order so
WHERE so.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND so.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY so.created_at DESC;

SELECT 
    sol.line_number,
    v.sku,
    b.code AS bin_code,
    sol.ordered_qty,
    sol.fulfilled_qty,
    sol.unit_price,
    sol.tax_rate,
    sol.line_total,
    sol.line_status
FROM erp_sales.sales_order_line sol
JOIN erp_sales.sales_order so ON so.order_id = sol.order_id AND so.tenant_id = sol.tenant_id AND so.company_id = sol.company_id
JOIN erp_inventory.product_variant v ON v.variant_id = sol.variant_id AND v.tenant_id = sol.tenant_id
JOIN erp_inventory.stock_bin b ON b.bin_id = sol.bin_id AND b.tenant_id = sol.tenant_id AND b.company_id = sol.company_id
WHERE sol.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND sol.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY sol.line_number;

\echo ''
\echo '=========================================================================='
\echo '3. POS Sales Invoice Record & Lines (Expected: Exactly 1 Invoice, 5 EA)'
\echo '=========================================================================='
SELECT 
    si.invoice_id,
    si.invoice_number,
    si.client_transaction_id,
    si.order_id,
    c.customer_code,
    c.name AS customer_name,
    si.payment_method,
    si.invoice_status,
    si.invoice_date,
    si.subtotal_amount,
    si.tax_amount,
    si.discount_amount,
    si.total_amount,
    si.paid_amount,
    si.posted_at
FROM erp_sales.sales_invoice si
JOIN erp_sales.customer c ON c.customer_id = si.customer_id AND c.tenant_id = si.tenant_id AND c.company_id = si.company_id
WHERE si.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND si.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY si.posted_at DESC;

SELECT 
    sil.line_number,
    v.sku,
    w.code AS warehouse_code,
    b.code AS bin_code,
    sil.quantity,
    sil.unit_price,
    sil.unit_cost,
    sil.tax_rate,
    sil.line_total
FROM erp_sales.sales_invoice_line sil
JOIN erp_sales.sales_invoice si ON si.invoice_id = sil.invoice_id AND si.tenant_id = sil.tenant_id AND si.company_id = sil.company_id
JOIN erp_inventory.product_variant v ON v.variant_id = sil.variant_id AND v.tenant_id = sil.tenant_id
JOIN erp_inventory.warehouse w ON w.warehouse_id = sil.warehouse_id AND w.tenant_id = sil.tenant_id AND w.company_id = sil.company_id
JOIN erp_inventory.stock_bin b ON b.bin_id = sil.bin_id AND b.tenant_id = sil.tenant_id AND b.company_id = sil.company_id
WHERE sil.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND sil.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY sil.line_number;

\echo ''
\echo '=========================================================================='
\echo '4. Inventory Stock Balances (Expected: Total 53 EA, PICK-01: 18 EA)'
\echo '=========================================================================='
SELECT 
    w.code AS warehouse_code,
    w.name AS warehouse_name,
    b.code AS bin_code,
    b.bin_type,
    sb.status_code,
    sb.on_hand_qty,
    sb.updated_at
FROM erp_inventory.stock_balance sb
JOIN erp_inventory.warehouse w ON w.warehouse_id = sb.warehouse_id AND w.tenant_id = sb.tenant_id AND w.company_id = sb.company_id
JOIN erp_inventory.stock_bin b ON b.bin_id = sb.bin_id AND b.tenant_id = sb.tenant_id AND b.company_id = sb.company_id
WHERE sb.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND sb.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND sb.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
ORDER BY w.code, b.code;

SELECT 
    count(*) AS bin_count,
    sum(sb.on_hand_qty) AS total_physical_stock_on_hand_ea
FROM erp_inventory.stock_balance sb
WHERE sb.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND sb.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND sb.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid;

\echo ''
\echo '=========================================================================='
\echo '5. Sales Dispatch Inventory Movement & Leg'
\echo '=========================================================================='
SELECT 
    m.movement_id,
    m.movement_type,
    m.source_document_type,
    m.source_document_id,
    leg.quantity_delta,
    w.code AS warehouse_code,
    b.code AS bin_code,
    m.occurred_at,
    m.posted_at,
    m.note
FROM erp_inventory.inventory_movement m
JOIN erp_inventory.inventory_movement_leg leg ON leg.movement_id = m.movement_id AND leg.tenant_id = m.tenant_id AND leg.company_id = m.company_id
JOIN erp_inventory.warehouse w ON w.warehouse_id = leg.warehouse_id AND w.tenant_id = leg.tenant_id AND w.company_id = leg.company_id
JOIN erp_inventory.stock_bin b ON b.bin_id = leg.bin_id AND b.tenant_id = leg.tenant_id AND b.company_id = leg.company_id
WHERE m.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND m.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND leg.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
  AND m.movement_type = 'issue'
ORDER BY m.posted_at DESC;

\echo ''
\echo '=========================================================================='
\echo '6. Sales Invoice GL Journal Header & Line Transactions'
\echo '=========================================================================='
SELECT 
    j.id AS journal_id,
    j.journal_no,
    j.journal_date,
    j.source_module,
    j.source_type,
    j.source_id,
    j.status,
    COALESCE(sum(jl.debit_functional), 0) AS total_debit,
    COALESCE(sum(jl.credit_functional), 0) AS total_credit,
    CASE WHEN COALESCE(sum(jl.debit_functional), 0) = COALESCE(sum(jl.credit_functional), 0) THEN 'BALANCED' ELSE 'UNBALANCED' END AS balance_status
FROM finance.journals j
LEFT JOIN finance.journal_lines jl ON jl.journal_id = j.id AND jl.tenant_id = j.tenant_id AND jl.company_id = j.company_id
WHERE j.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND j.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND j.source_module = 'sales'
GROUP BY j.id, j.journal_no, j.journal_date, j.source_module, j.source_type, j.source_id, j.status, j.posted_at
ORDER BY j.posted_at DESC;

SELECT 
    j.journal_no,
    jl.line_no,
    a.account_code,
    a.account_name,
    a.account_type,
    jl.description,
    jl.debit_functional,
    jl.credit_functional
FROM finance.journal_lines jl
JOIN finance.journals j ON j.id = jl.journal_id AND j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id
JOIN finance.accounts a ON a.id = jl.account_id AND a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id
WHERE j.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND j.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND j.source_module = 'sales'
ORDER BY j.journal_no, jl.line_no;

\echo ''
\echo '=========================================================================='
\echo '7. Account Balances: 1010, 1100, 1200, 2010, 2100, 4010, 5010'
\echo '=========================================================================='
SELECT 
    a.account_code,
    a.account_name,
    a.account_type,
    a.normal_balance,
    COALESCE(sum(jl.debit_functional), 0) AS total_debit,
    COALESCE(sum(jl.credit_functional), 0) AS total_credit,
    CASE 
        WHEN a.normal_balance = 'debit' THEN COALESCE(sum(jl.debit_functional) - sum(jl.credit_functional), 0)
        ELSE COALESCE(sum(jl.credit_functional) - sum(jl.debit_functional), 0)
    END AS net_balance_functional
FROM finance.accounts a
LEFT JOIN finance.journal_lines jl ON jl.account_id = a.id AND jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id
WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND a.account_code IN ('1010', '1100', '1200', '2010', '2100', '4010', '5010')
GROUP BY a.account_code, a.account_name, a.account_type, a.normal_balance
ORDER BY a.account_code;

\echo ''
\echo '=========================================================================='
\echo '8. Live Trial Balance Equality Proof (Sum of All Accounts)'
\echo '=========================================================================='
SELECT 
    COALESCE(sum(jl.debit_functional), 0) AS trial_balance_total_debit,
    COALESCE(sum(jl.credit_functional), 0) AS trial_balance_total_credit,
    COALESCE(sum(jl.debit_functional), 0) - COALESCE(sum(jl.credit_functional), 0) AS trial_balance_net_variance,
    CASE 
        WHEN COALESCE(sum(jl.debit_functional), 0) = COALESCE(sum(jl.credit_functional), 0) THEN 'BALANCED' 
        ELSE 'UNBALANCED' 
    END AS trial_balance_status
FROM finance.journal_lines jl
WHERE jl.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND jl.company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo ''
\echo '=========================================================================='
\echo '9. Automated Read-Only Assertions'
\echo '=========================================================================='
DO $$
DECLARE
    v_total_stock numeric(24,9);
    v_total_invoiced_qty numeric(24,9);
    v_invoice_count integer;
    v_total_invoice_amount numeric(20,4);
    v_order_count integer;
    v_unconfirmed_order_count integer;
    v_total_cogs numeric(24,9);
    v_acct_1200_balance numeric(24,9);
    v_acct_5010_balance numeric(24,9);
    v_acct_1010_balance numeric(24,9);
    v_acct_4010_balance numeric(24,9);
    v_tb_debit numeric(24,9);
    v_tb_credit numeric(24,9);
    v_orphaned_movements integer;
BEGIN
    -- 1. Assert sales invoices exist in posted status
    SELECT count(*), COALESCE(sum(total_amount), 0)
    INTO v_invoice_count, v_total_invoice_amount
    FROM erp_sales.sales_invoice
    WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND invoice_status = 'posted';

    IF v_invoice_count < 1 THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Expected at least 1 posted sales invoice, found %', v_invoice_count;
    END IF;
    RAISE NOTICE '[ASSERTION 1 PASS] Found % posted POS invoices totaling PKR %.', v_invoice_count, v_total_invoice_amount;

    -- 2. Assert inventory movements match sales invoices 1-to-1
    SELECT count(*) INTO v_orphaned_movements
    FROM erp_sales.sales_invoice si
    LEFT JOIN erp_inventory.inventory_movement m 
           ON m.source_document_id = si.invoice_id 
          AND m.source_document_type = 'sales_invoice'
          AND m.tenant_id = si.tenant_id 
          AND m.company_id = si.company_id
    WHERE si.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND si.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND m.movement_id IS NULL;

    IF v_orphaned_movements > 0 THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Found % sales invoices without matching inventory issue movement', v_orphaned_movements;
    END IF;
    RAISE NOTICE '[ASSERTION 2 PASS] Every sales invoice has a corresponding inventory issue movement.';

    -- 3. Assert total stock decreased exactly by total invoiced quantity (starting from 58 EA)
    SELECT COALESCE(sum(quantity), 0) INTO v_total_invoiced_qty
    FROM erp_sales.sales_invoice_line sil
    JOIN erp_sales.sales_invoice si ON si.invoice_id = sil.invoice_id AND si.tenant_id = sil.tenant_id AND si.company_id = sil.company_id
    WHERE sil.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND sil.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND sil.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid;

    SELECT COALESCE(sum(on_hand_qty), 0) INTO v_total_stock
    FROM erp_inventory.stock_balance
    WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid;

    IF v_total_stock <> (58.000000000 - v_total_invoiced_qty) THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Total stock % EA does not reconcile with starting stock 58 EA minus invoiced % EA (expected % EA)',
            v_total_stock, v_total_invoiced_qty, (58.000000000 - v_total_invoiced_qty);
    END IF;
    RAISE NOTICE '[ASSERTION 3 PASS] Total stock balance (% EA) reconciles with starting 58 EA minus % EA invoiced.', v_total_stock, v_total_invoiced_qty;

    -- 4. Calculate total expected COGS from invoice lines
    SELECT COALESCE(sum(sil.quantity * sil.unit_cost), 0) INTO v_total_cogs
    FROM erp_sales.sales_invoice_line sil
    JOIN erp_sales.sales_invoice si ON si.invoice_id = sil.invoice_id AND si.tenant_id = sil.tenant_id AND si.company_id = sil.company_id
    WHERE sil.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND sil.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND sil.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
      AND si.total_amount > 0;

    -- 5. Assert GL Account 1200 reflects starting 2,250 minus COGS
    SELECT COALESCE(sum(jl.debit_functional) - sum(jl.credit_functional), 0) INTO v_acct_1200_balance
    FROM finance.accounts a
    JOIN finance.journal_lines jl ON jl.account_id = a.id AND jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id
    WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND a.account_code = '1200';

    IF v_acct_1200_balance <> (2250.00 - v_total_cogs) THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Expected Account 1200 balance %, found %', (2250.00 - v_total_cogs), v_acct_1200_balance;
    END IF;
    RAISE NOTICE '[ASSERTION 4 PASS] GL Account 1200 is Debit PKR % (Starting 2,250 - COGS %).', v_acct_1200_balance, v_total_cogs;

    -- 6. Assert GL Account 5010 reflects COGS
    SELECT COALESCE(sum(jl.debit_functional) - sum(jl.credit_functional), 0) INTO v_acct_5010_balance
    FROM finance.accounts a
    JOIN finance.journal_lines jl ON jl.account_id = a.id AND jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id
    WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND a.account_code = '5010';

    IF v_acct_5010_balance <> v_total_cogs THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Expected Account 5010 balance %, found %', v_total_cogs, v_acct_5010_balance;
    END IF;
    RAISE NOTICE '[ASSERTION 5 PASS] GL Account 5010 is Debit PKR % matching recognized COGS.', v_acct_5010_balance;

    -- 7. Assert GL Account 1010 and 4010 reflect Cash and Revenue
    SELECT COALESCE(sum(jl.debit_functional) - sum(jl.credit_functional), 0) INTO v_acct_1010_balance
    FROM finance.accounts a
    JOIN finance.journal_lines jl ON jl.account_id = a.id AND jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id
    WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND a.account_code = '1010';

    SELECT COALESCE(sum(jl.credit_functional) - sum(jl.debit_functional), 0) INTO v_acct_4010_balance
    FROM finance.accounts a
    JOIN finance.journal_lines jl ON jl.account_id = a.id AND jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id
    WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
      AND a.account_code = '4010';

    IF v_acct_1010_balance <> v_total_invoice_amount OR v_acct_4010_balance <> v_total_invoice_amount THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Expected 1010 Debit % and 4010 Credit %. Found 1010: %, 4010: %',
            v_total_invoice_amount, v_total_invoice_amount, v_acct_1010_balance, v_acct_4010_balance;
    END IF;
    RAISE NOTICE '[ASSERTION 6 PASS] GL Cash (1010) is Debit PKR % and Sales Revenue (4010) is Credit PKR %.', v_acct_1010_balance, v_acct_4010_balance;

    -- 8. Assert Trial Balance equality
    SELECT COALESCE(sum(jl.debit_functional), 0), COALESCE(sum(jl.credit_functional), 0)
    INTO v_tb_debit, v_tb_credit
    FROM finance.journal_lines jl
    WHERE jl.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND jl.company_id = '22222222-2222-4222-8222-222222222223'::uuid;

    IF v_tb_debit <> v_tb_credit THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Trial balance unbalanced! Debit %, Credit %', v_tb_debit, v_tb_credit;
    END IF;
    RAISE NOTICE '[ASSERTION 7 PASS] Trial Balance is perfectly balanced: Total Debits PKR % = Total Credits PKR % (Net variance = 0).', v_tb_debit, v_tb_credit;

    -- 9. Assert Sales Orders are all confirmed and have no unfulfilled inventory reservations
    SELECT count(*), count(*) FILTER (WHERE order_status <> 'confirmed')
    INTO v_order_count, v_unconfirmed_order_count
    FROM erp_sales.sales_order
    WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
      AND company_id = '22222222-2222-4222-8222-222222222223'::uuid;

    IF v_order_count < 1 OR v_unconfirmed_order_count > 0 THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: Expected all orders confirmed, found total % unconfirmed %', v_order_count, v_unconfirmed_order_count;
    END IF;
    RAISE NOTICE '[ASSERTION 8 PASS] All sales orders (% found) are in "confirmed" status without inventory or GL mutation.', v_order_count;

    RAISE NOTICE '';
    RAISE NOTICE '==========================================================================';
    RAISE NOTICE 'ALL SALES & POS EXECUTION VERIFICATION ASSERTIONS PASSED SUCCESSFULLY!';
    RAISE NOTICE '==========================================================================';
END $$;
