-- Universal ERP -- Seed Disposable Isolated QA Tenant
-- Target: Local Docker PostgreSQL (universal_erp)
-- Tenant ID: 99999999-9999-4999-8999-999999999999 (qa-disposable-tenant)
-- Company ID: 99999999-9999-4999-8999-999999999991 (QA-DISP-CO)
-- User ID: 00000000-0000-4000-8000-000000000003 (Google OAuth authenticated user)
-- Purpose: Complete isolated sandbox for RC1 Priority 3 authorized write & read-back workflows.
-- Safety: 100% isolated. Zero mutations to DEMO-CLOUD (22222222-2222-4222-8222-222222222222).

DO $block$
DECLARE
    v_tenant_id uuid := '99999999-9999-4999-8999-999999999999'::uuid;
    v_company_id uuid := '99999999-9999-4999-8999-999999999991'::uuid;
    v_user_id uuid := '00000000-0000-4000-8000-000000000003'::uuid;
    v_fiscal_year_id uuid := '99999999-9999-4999-8999-999999999992'::uuid;
    v_fiscal_period_id uuid := '99999999-9999-4999-8999-999999999993'::uuid;
    v_wh_id uuid := '99999999-9999-4999-8999-999999999994'::uuid;
    v_bin_receive_id uuid := '99999999-9999-4999-8999-999999999995'::uuid;
    v_bin_pick_id uuid := '99999999-9999-4999-8999-999999999996'::uuid;
    v_uom_id uuid := '99999999-9999-4999-8999-999999999997'::uuid;
    v_category_id uuid := '99999999-9999-4999-8999-999999999998'::uuid;
    v_product_id uuid := '99999999-9999-4999-8999-999999999988'::uuid;
    v_variant_id uuid := '99999999-9999-4999-8999-999999999989'::uuid;
    v_role_id uuid := '99999999-9999-4999-8999-999999999980'::uuid;
    v_membership_id uuid := '99999999-9999-4999-8999-999999999990'::uuid;
    v_co_membership_id uuid := '99999999-9999-4999-8999-999999999991'::uuid;
BEGIN
    -- 1. Tenant
    INSERT INTO platform.tenants (
        id, tenant_code, display_name, legal_name, status,
        default_country_code, default_currency_code, default_timezone
    ) VALUES (
        v_tenant_id, 'qa-disposable-tenant', 'QA Disposable Validation Tenant', 'QA Disposable Org Ltd', 'active',
        'PK', 'PKR', 'Asia/Karachi'
    ) ON CONFLICT (tenant_code) DO NOTHING;

    -- Set RLS session variables
    PERFORM set_config('app.tenant_id', v_tenant_id::text, true);
    PERFORM set_config('app.company_id', v_company_id::text, true);

    -- 2. Company
    INSERT INTO organization.companies (
        id, tenant_id, company_code, name, legal_name,
        country_code, base_currency_code, timezone, status
    ) VALUES (
        v_company_id, v_tenant_id, 'QA-DISP-CO', 'QA Disposable Company Ltd', 'QA Disposable Company Ltd',
        'PK', 'PKR', 'Asia/Karachi', 'active'
    ) ON CONFLICT (id) DO NOTHING;

    -- 3. Tenant Membership
    INSERT INTO identity.tenant_memberships (
        id, tenant_id, user_id, status, joined_at
    ) VALUES (
        v_membership_id, v_tenant_id, v_user_id, 'active', now()
    ) ON CONFLICT (tenant_id, user_id) DO NOTHING;

    -- 4. Company Membership
    INSERT INTO identity.company_memberships (
        id, tenant_id, company_id, user_id, status
    ) VALUES (
        v_co_membership_id, v_tenant_id, v_company_id, v_user_id, 'active'
    ) ON CONFLICT (tenant_id, company_id, user_id) DO NOTHING;

    -- 5. Role & Role Permissions
    INSERT INTO "authorization".roles (
        id, tenant_id, role_code, name, is_system_role
    ) VALUES (
        v_role_id, v_tenant_id, 'DEMO_ADMIN', 'QA Full Administrator', true
    ) ON CONFLICT (tenant_id, role_code) DO NOTHING;

    -- Temporarily set context to DEMO-CLOUD to read available permission codes
    PERFORM set_config('app.tenant_id', '22222222-2222-4222-8222-222222222222', true);

    CREATE TEMP TABLE IF NOT EXISTS _qa_temp_perms AS
    SELECT permission_code, scope_type
    FROM "authorization".role_permissions
    WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid;

    -- Switch back to QA tenant to insert permissions
    PERFORM set_config('app.tenant_id', v_tenant_id::text, true);

    INSERT INTO "authorization".role_permissions (tenant_id, role_id, permission_code, scope_type)
    SELECT v_tenant_id, v_role_id, permission_code, scope_type
    FROM _qa_temp_perms
    ON CONFLICT (tenant_id, role_id, permission_code, scope_type) DO NOTHING;

    DROP TABLE IF EXISTS _qa_temp_perms;

    -- Assign role to membership
    INSERT INTO "authorization".membership_role_assignments (
        id, tenant_id, membership_id, role_id, assigned_by_user_id
    ) VALUES (
        '99999999-9999-4999-8999-999999999981'::uuid, v_tenant_id, v_membership_id, v_role_id, v_user_id
    ) ON CONFLICT (id) DO NOTHING;

    -- 6. Stock Statuses
    INSERT INTO erp_inventory.stock_status (
        tenant_id, status_code, description, is_reservable, is_pickable
    ) VALUES
        (v_tenant_id, 'available', 'Available for normal inventory use.', true, true),
        (v_tenant_id, 'quality_hold', 'Held for inspection or quality review.', false, false),
        (v_tenant_id, 'blocked', 'Blocked from normal use.', false, false)
    ON CONFLICT (tenant_id, status_code) DO NOTHING;

    -- 7. Fiscal Year & Period
    INSERT INTO finance.fiscal_years (tenant_id, company_id, id, code, starts_on, ends_on, status)
    VALUES (v_tenant_id, v_company_id, v_fiscal_year_id, 'FY2026-27', '2026-07-01'::date, '2027-06-30'::date, 'open')
    ON CONFLICT (tenant_id, company_id, id) DO NOTHING;

    INSERT INTO finance.fiscal_periods (
        tenant_id, company_id, id, fiscal_year_id, period_no, name, starts_on, ends_on, status
    ) VALUES (
        v_tenant_id, v_company_id, v_fiscal_period_id, v_fiscal_year_id, 5, 'November 2026', '2026-11-01'::date, '2026-11-30'::date, 'open'
    ) ON CONFLICT (tenant_id, company_id, id) DO NOTHING;

    -- 8. Chart of Accounts
    INSERT INTO finance.accounts (
        tenant_id, company_id, id, account_code, account_name, account_type, normal_balance, is_control_account, allow_manual_posting
    ) VALUES
        (v_tenant_id, v_company_id, '99999999-9999-4999-8999-999999999971'::uuid, '1010', 'Cash on Hand', 'asset', 'debit', false, true),
        (v_tenant_id, v_company_id, '99999999-9999-4999-8999-999999999972'::uuid, '1020', 'Operating Bank Account (PKR)', 'asset', 'debit', false, true),
        (v_tenant_id, v_company_id, '99999999-9999-4999-8999-999999999973'::uuid, '1100', 'Accounts Receivable (Trade Debtors)', 'asset', 'debit', true, false),
        (v_tenant_id, v_company_id, '99999999-9999-4999-8999-999999999974'::uuid, '1200', 'Merchandise Inventory Asset', 'asset', 'debit', true, false),
        (v_tenant_id, v_company_id, '99999999-9999-4999-8999-999999999975'::uuid, '2010', 'Accounts Payable (Trade Creditors)', 'liability', 'credit', true, false),
        (v_tenant_id, v_company_id, '99999999-9999-4999-8999-999999999976'::uuid, '3010', 'Shareholder Capital', 'equity', 'credit', false, true),
        (v_tenant_id, v_company_id, '99999999-9999-4999-8999-999999999977'::uuid, '4010', 'Product Sales Revenue', 'revenue', 'credit', false, true),
        (v_tenant_id, v_company_id, '99999999-9999-4999-8999-999999999978'::uuid, '5010', 'Cost of Goods Sold (COGS)', 'expense', 'debit', false, true)
    ON CONFLICT (tenant_id, company_id, id) DO NOTHING;

    -- 9. Unit of Measure, Category, Product, Variant
    INSERT INTO erp_inventory.unit_of_measure (tenant_id, uom_id, code, name, dimension, decimal_places)
    VALUES (v_tenant_id, v_uom_id, 'EA', 'Each', 'count', 0)
    ON CONFLICT (tenant_id, uom_id) DO NOTHING;

    INSERT INTO erp_inventory.product_category (tenant_id, category_id, code, name)
    VALUES (v_tenant_id, v_category_id, 'GEN', 'General Category')
    ON CONFLICT (tenant_id, category_id) DO NOTHING;

    INSERT INTO erp_inventory.product (tenant_id, product_id, product_code, name, description, product_type)
    VALUES (v_tenant_id, v_product_id, 'QA-PROD-01', 'QA Test Scissor', 'QA Test Scissor for validation', 'stock')
    ON CONFLICT (tenant_id, product_id) DO NOTHING;

    INSERT INTO erp_inventory.product_variant (
        tenant_id, variant_id, product_id, sku, variant_name, base_uom_id, tracking_mode, costing_method, negative_stock_allowed
    ) VALUES (
        v_tenant_id, v_variant_id, v_product_id, 'QA-SKU-01', 'QA Test Scissor Standard', v_uom_id, 'none', 'fifo', false
    ) ON CONFLICT (tenant_id, variant_id) DO NOTHING;

    -- 10. Warehouse & Bins
    INSERT INTO erp_inventory.warehouse (
        tenant_id, company_id, warehouse_id, code, name, is_active
    ) VALUES (
        v_tenant_id, v_company_id, v_wh_id, 'QA-WH-01', 'QA Disposable Central Warehouse', true
    ) ON CONFLICT (tenant_id, company_id, warehouse_id) DO NOTHING;

    INSERT INTO erp_inventory.stock_bin (
        tenant_id, company_id, warehouse_id, bin_id, code, bin_type, is_active
    ) VALUES
        (v_tenant_id, v_company_id, v_wh_id, v_bin_receive_id, 'RECEIVE-01', 'receiving', true),
        (v_tenant_id, v_company_id, v_wh_id, v_bin_pick_id, 'PICK-01', 'storage', true)
    ON CONFLICT (tenant_id, company_id, bin_id) DO NOTHING;

END $block$;
