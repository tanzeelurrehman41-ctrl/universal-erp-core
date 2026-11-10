-- Universal ERP -- Read-Only Sales Workspace & RBAC Preconditions Inspection
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target User: tanzeelurrehman41@gmail.com
-- Purpose: Read-only inspection of Sales schema, existing customers, existing sales orders/invoices,
--          authorization permissions, roles, Tanzeel's permissions, and stock availability.
-- Safety: Strictly read-only queries. Zero database mutations.

\echo '=========================================================================='
\echo 'UNIVERSAL ERP -- SALES & POS PRECONDITIONS INSPECTION'
\echo '=========================================================================='

SELECT current_database() AS connected_database, current_user AS connected_user, version() AS pg_version;

\echo ''
\echo '=========================================================================='
\echo '1. Existing Customers in erp_sales.customer for DEMO-CLOUD'
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
ORDER BY c.customer_code ASC;

SELECT count(*) AS total_customers_count
FROM erp_sales.customer
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo ''
\echo '=========================================================================='
\echo '2. Existing Sales Orders in erp_sales.sales_order for DEMO-CLOUD'
\echo '=========================================================================='
SELECT 
    so.order_id,
    so.order_number,
    so.customer_id,
    c.name AS customer_name,
    so.warehouse_id,
    so.order_type,
    so.order_status,
    so.order_date,
    so.subtotal_amount,
    so.total_amount,
    so.created_at
FROM erp_sales.sales_order so
LEFT JOIN erp_sales.customer c ON c.customer_id = so.customer_id AND c.tenant_id = so.tenant_id AND c.company_id = so.company_id
WHERE so.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND so.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY so.created_at DESC;

\echo ''
\echo '=========================================================================='
\echo '3. Existing Sales Invoices in erp_sales.sales_invoice for DEMO-CLOUD'
\echo '=========================================================================='
SELECT 
    si.invoice_id,
    si.invoice_number,
    si.client_transaction_id,
    si.order_id,
    si.customer_id,
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
WHERE si.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND si.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY si.posted_at DESC;

\echo ''
\echo '=========================================================================='
\echo '4. Sales Permissions Registered in authorization.permissions'
\echo '=========================================================================='
SELECT permission_code, module_code, description
FROM "authorization".permissions
WHERE permission_code LIKE 'sales.%'
ORDER BY permission_code;

\echo ''
\echo '=========================================================================='
\echo '5. Roles in Tenant DEMO-CLOUD and Their Permissions'
\echo '=========================================================================='
SELECT 
    r.role_code,
    r.name AS role_name,
    rp.permission_code
FROM "authorization".roles r
LEFT JOIN "authorization".role_permissions rp ON rp.role_id = r.id AND rp.tenant_id = r.tenant_id
WHERE r.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
ORDER BY r.role_code, rp.permission_code;

\echo ''
\echo '=========================================================================='
\echo '6. Effective Permissions for Tanzeel (tanzeelurrehman41@gmail.com)'
\echo '=========================================================================='
WITH tanzeel_user AS (
    SELECT id FROM identity.users WHERE email_normalized = 'tanzeelurrehman41@gmail.com'
)
SELECT 
    perm.permission_code,
    "authorization".user_has_permission(
        (SELECT id FROM tanzeel_user),
        '22222222-2222-4222-8222-222222222222'::uuid,
        perm.permission_code,
        '22222222-2222-4222-8222-222222222223'::uuid
    ) AS is_granted
FROM (
    VALUES 
        ('sales.customer.view'),
        ('sales.customer.create'),
        ('sales.order.view'),
        ('sales.order.create'),
        ('sales.order.confirm'),
        ('sales.invoice.view'),
        ('sales.invoice.post'),
        ('inventory.stock.view'),
        ('finance.gl.view')
) AS perm(permission_code);

\echo ''
\echo '=========================================================================='
\echo '7. Stock Balances by Warehouse and Bin for SCISSOR-EA-01'
\echo '=========================================================================='
SELECT 
    w.warehouse_id,
    w.code AS warehouse_code,
    w.name AS warehouse_name,
    b.bin_id,
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

\echo ''
\echo '=========================================================================='
\echo '8. Target Accounts Status (1010, 1100, 1200, 2010, 2100, 4010, 5010)'
\echo '=========================================================================='
SELECT 
    a.account_code,
    a.account_name,
    a.account_type,
    a.normal_balance,
    a.is_active,
    COALESCE(sum(jl.debit_functional), 0) AS total_debit,
    COALESCE(sum(jl.credit_functional), 0) AS total_credit
FROM finance.accounts a
LEFT JOIN finance.journal_lines jl ON jl.account_id = a.id AND jl.tenant_id = a.tenant_id AND jl.company_id = a.company_id
WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND a.account_code IN ('1010', '1100', '1200', '2010', '2100', '4010', '5010')
GROUP BY a.account_code, a.account_name, a.account_type, a.normal_balance, a.is_active
ORDER BY a.account_code;
