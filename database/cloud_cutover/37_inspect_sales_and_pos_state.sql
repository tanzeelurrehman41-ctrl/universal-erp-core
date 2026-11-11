-- Universal ERP -- Read-Only Inspection: Current Sales & POS State on Neon
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target Customer: DEMO-CUSTOMER-POS-01
-- Target Variant: SCISSOR-EA-01 (2871558f-e577-4d2d-b365-fa60be311111)
-- Purpose: Read-only investigation of:
--          1. Visible Invoices: POS-MWB642DH (PKR 55) and POS-MWB5YM9G (PKR 0)
--          2. Header & Line Details, Payment Status, and Linked Journal IDs
--          3. Confirmed Sales Order SO-2024-01 status, quantity, price, warehouse, bin
--          4. Verification of whether the planned 5 EA @ PKR 75 sale has been posted
--          5. Inspection of PICK-01 bin type ('receiving') and POS dispatch validity
--          6. Actual Stock Balances & GL Account Balances vs Pre-Sale Baseline
-- Safety: Strictly read-only queries. Zero database mutations.

\echo '=========================================================================='
\echo 'UNIVERSAL ERP -- READ-ONLY SALES & POS STATE INSPECTION'
\echo '=========================================================================='

SELECT current_database() AS connected_database, current_user AS connected_user, version() AS pg_version;

\echo ''
\echo '=========================================================================='
\echo '1. Visible Sales Invoices & Linked General Ledger Journals'
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
    si.posted_at,
    j.journal_no,
    j.id AS journal_id,
    j.status AS journal_status
FROM erp_sales.sales_invoice si
JOIN erp_sales.customer c ON c.customer_id = si.customer_id AND c.tenant_id = si.tenant_id AND c.company_id = si.company_id
LEFT JOIN finance.journals j ON j.source_module = 'sales' 
                            AND j.source_type = 'sales_invoice' 
                            AND j.source_id = si.invoice_id::text
                            AND j.tenant_id = si.tenant_id 
                            AND j.company_id = si.company_id
WHERE si.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND si.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY si.posted_at DESC;

\echo ''
\echo '=========================================================================='
\echo '2. Detailed Invoice Lines for Both Invoices'
\echo '=========================================================================='
SELECT 
    si.invoice_number,
    sil.line_number,
    v.sku,
    w.code AS warehouse_code,
    b.code AS bin_code,
    b.bin_type,
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
ORDER BY si.posted_at DESC, sil.line_number;

\echo ''
\echo '=========================================================================='
\echo '3. Posted General Ledger Lines for Sales Invoices'
\echo '=========================================================================='
SELECT 
    j.journal_no,
    j.status AS journal_status,
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
\echo '4. Sales Order Record, Lines, Warehouse, and Bin'
\echo '=========================================================================='
SELECT 
    so.order_id,
    so.order_number,
    so.order_status,
    so.order_type,
    w.code AS warehouse_code,
    w.name AS warehouse_name,
    c.customer_code,
    c.name AS customer_name,
    so.order_date,
    so.subtotal_amount,
    so.tax_amount,
    so.total_amount,
    so.created_at
FROM erp_sales.sales_order so
JOIN erp_sales.customer c ON c.customer_id = so.customer_id AND c.tenant_id = so.tenant_id AND c.company_id = so.company_id
JOIN erp_inventory.warehouse w ON w.warehouse_id = so.warehouse_id AND w.tenant_id = so.tenant_id AND w.company_id = so.company_id
WHERE so.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND so.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY so.created_at DESC;

SELECT 
    so.order_number,
    sol.line_number,
    v.sku,
    b.code AS bin_code,
    b.bin_type,
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
ORDER BY so.order_number, sol.line_number;

\echo ''
\echo '=========================================================================='
\echo '5. Planned 5 EA @ PKR 75 Sale Existence Check'
\echo '=========================================================================='
SELECT 
    count(*) AS matching_5ea_invoice_count,
    COALESCE(sum(si.total_amount), 0) AS matching_5ea_total_amount
FROM erp_sales.sales_invoice si
JOIN erp_sales.sales_invoice_line sil ON sil.invoice_id = si.invoice_id AND sil.tenant_id = si.tenant_id AND sil.company_id = si.company_id
WHERE si.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND si.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND (sil.quantity = 5.0000 OR sil.unit_price = 75.0000 OR si.total_amount = 375.0000);

\echo ''
\echo '=========================================================================='
\echo '6. Warehouse MAIN-W1 Bins & PICK-01 Configuration'
\echo '=========================================================================='
SELECT 
    w.code AS warehouse_code,
    w.name AS warehouse_name,
    b.bin_id,
    b.code AS bin_code,
    b.bin_type,
    b.is_active,
    COALESCE(sb.on_hand_qty, 0) AS on_hand_qty,
    sb.status_code
FROM erp_inventory.stock_bin b
JOIN erp_inventory.warehouse w ON w.warehouse_id = b.warehouse_id AND w.tenant_id = b.tenant_id AND w.company_id = b.company_id
LEFT JOIN erp_inventory.stock_balance sb ON sb.bin_id = b.bin_id 
                                        AND sb.warehouse_id = b.warehouse_id 
                                        AND sb.tenant_id = b.tenant_id 
                                        AND sb.company_id = b.company_id
                                        AND sb.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
WHERE b.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND b.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND w.code = 'MAIN-W1'
ORDER BY b.code;

\echo ''
\echo '=========================================================================='
\echo '7. Stock Balances vs. Pre-Sale Baseline (Pre-Sale Total: 58 EA)'
\echo '=========================================================================='
SELECT 
    w.code AS warehouse_code,
    b.code AS bin_code,
    b.bin_type,
    sb.status_code,
    CASE 
        WHEN w.code = 'MAIN-W1' AND b.code = 'PICK-01' THEN 23.000000000
        WHEN w.code = 'MAIN-W1' AND b.code = 'RECEIVE-01' THEN 31.000000000
        WHEN w.code = 'STORE-01' AND b.code = 'STORE-01' THEN 4.000000000
        ELSE 0.000000000
    END AS pre_sale_baseline_ea,
    sb.on_hand_qty AS current_actual_ea,
    sb.on_hand_qty - CASE 
        WHEN w.code = 'MAIN-W1' AND b.code = 'PICK-01' THEN 23.000000000
        WHEN w.code = 'MAIN-W1' AND b.code = 'RECEIVE-01' THEN 31.000000000
        WHEN w.code = 'STORE-01' AND b.code = 'STORE-01' THEN 4.000000000
        ELSE 0.000000000
    END AS net_stock_delta_ea,
    sb.updated_at
FROM erp_inventory.stock_balance sb
JOIN erp_inventory.warehouse w ON w.warehouse_id = sb.warehouse_id AND w.tenant_id = sb.tenant_id AND w.company_id = sb.company_id
JOIN erp_inventory.stock_bin b ON b.bin_id = sb.bin_id AND b.tenant_id = sb.tenant_id AND b.company_id = sb.company_id
WHERE sb.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND sb.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND sb.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid
ORDER BY w.code, b.code;

SELECT 
    58.000000000 AS pre_sale_total_baseline_ea,
    sum(sb.on_hand_qty) AS current_actual_total_ea,
    sum(sb.on_hand_qty) - 58.000000000 AS net_total_delta_ea
FROM erp_inventory.stock_balance sb
WHERE sb.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND sb.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND sb.variant_id = '2871558f-e577-4d2d-b365-fa60be311111'::uuid;

\echo ''
\echo '=========================================================================='
\echo '8. General Ledger Account Balances vs. Pre-Sale Baseline'
\echo '=========================================================================='
SELECT 
    a.account_code,
    a.account_name,
    a.normal_balance,
    CASE 
        WHEN a.account_code = '1200' THEN 2250.00
        WHEN a.account_code = '2010' THEN 2250.00
        ELSE 0.00
    END AS pre_sale_baseline_pkr,
    COALESCE(sum(jl.debit_functional), 0) AS actual_total_debit_pkr,
    COALESCE(sum(jl.credit_functional), 0) AS actual_total_credit_pkr,
    CASE 
        WHEN a.normal_balance = 'debit' THEN COALESCE(sum(jl.debit_functional) - sum(jl.credit_functional), 0)
        ELSE COALESCE(sum(jl.credit_functional) - sum(jl.debit_functional), 0)
    END AS actual_net_balance_pkr,
    CASE 
        WHEN a.normal_balance = 'debit' THEN COALESCE(sum(jl.debit_functional) - sum(jl.credit_functional), 0)
        ELSE COALESCE(sum(jl.credit_functional) - sum(jl.debit_functional), 0)
    END - CASE 
        WHEN a.account_code = '1200' THEN 2250.00
        WHEN a.account_code = '2010' THEN 2250.00
        ELSE 0.00
    END AS net_balance_delta_pkr
FROM finance.accounts a
LEFT JOIN finance.journal_lines jl ON jl.account_id = a.id AND jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id
WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND a.account_code IN ('1010', '1100', '1200', '2010', '2100', '4010', '5010')
GROUP BY a.account_code, a.account_name, a.normal_balance
ORDER BY a.account_code;

\echo ''
\echo '=========================================================================='
\echo '9. Live Trial Balance Equality Proof'
\echo '=========================================================================='
SELECT 
    COALESCE(sum(jl.debit_functional), 0) AS trial_balance_total_debit,
    COALESCE(sum(jl.credit_functional), 0) AS trial_balance_total_credit,
    COALESCE(sum(jl.debit_functional), 0) - COALESCE(sum(jl.credit_functional), 0) AS net_variance,
    CASE 
        WHEN COALESCE(sum(jl.debit_functional), 0) = COALESCE(sum(jl.credit_functional), 0) THEN 'BALANCED' 
        ELSE 'UNBALANCED' 
    END AS trial_balance_status
FROM finance.journal_lines jl
WHERE jl.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND jl.company_id = '22222222-2222-4222-8222-222222222223'::uuid;
