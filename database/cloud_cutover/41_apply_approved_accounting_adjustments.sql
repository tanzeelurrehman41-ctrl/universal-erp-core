-- Universal ERP -- Apply Approved Model A Accounting Adjustments & Stock Capitalization
-- Target Database: Neon PostgreSQL Cloud (neondb)
-- Target Tenant  : DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company : CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target SKU     : SCISSOR-EA-01 (2871558f-e577-4d2d-b365-fa60be311111)
--
-- Approved Accounting Actions:
--   1. Decision 1 (PROP-FIFO-001):
--      Post adjusting journal Dr 1200 Inventory Asset PKR 135.00 / Cr 5010 COGS PKR 135.00.
--      Reconciles historical COGS timing variance from legacy invoices.
--
--   2. Decision 2 (Option A - Capitalization):
--      Capitalize the 3 unvalued physical units in MAIN-W1 / PICK-01 at PKR 45.00/unit (total PKR 135.00).
--      Post journal Dr 1200 Inventory Asset PKR 135.00 / Cr 3010 Shareholder Capital PKR 135.00.
--      Create 3rd FIFO cost layer (3 EA @ PKR 45.00) in MAIN-W1 / PICK-01.
--
-- Resulting Canonical State:
--   - Total Physical Stock       : 50 EA
--   - Total Valued Stock         : 50 EA (100% valued across 3 FIFO layers)
--   - Unvalued Stock             : 0 EA (All units unblocked for POS checkout)
--   - Total FIFO Layer Valuation : PKR 2,250.00
--   - GL Account 1200 Balance    : PKR 2,250.00
--   - Net Accounting Variance    : PKR 0.00 (Perfect Zero-Variance Balance)

BEGIN;

SELECT set_config('app.tenant_id', '22222222-2222-4222-8222-222222222222', true);
SELECT set_config('app.company_id', '22222222-2222-4222-8222-222222222223', true);

DO $$
DECLARE
    v_tenant_id        uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id       uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_variant_id       uuid := '2871558f-e577-4d2d-b365-fa60be311111'::uuid;
    v_main_wh_id       uuid := 'de9ffb26-8faa-4ec0-a90d-4945bdf4b7ff'::uuid;
    v_pick_bin_id      uuid := 'a6824c8e-1d91-42a3-bb49-b6136465e167'::uuid;
    v_fiscal_period_id uuid := '22222222-2222-4222-8222-222222222235'::uuid; -- November 2026 Open Period
    
    v_acc_1200_id      uuid;
    v_acc_5010_id      uuid;
    v_acc_3010_id      uuid;
    v_user_id          uuid;
    
    v_jrn_adj_id       uuid := '77777777-7777-4777-8777-777777777701'::uuid;
    v_jrn_cap_id       uuid := '77777777-7777-4777-8777-777777777702'::uuid;
    v_layer3_id        uuid := 'c3333333-3333-4333-8333-333333333333'::uuid;
    
    v_current_db       text;
    v_total_layers     integer;
    v_total_valued_qty numeric(24,9);
    v_total_valued_cost numeric(24,9);
    v_gl_1200          numeric(20,4);
BEGIN
    -- 0. Database Safety Assertion
    SELECT current_database() INTO v_current_db;
    IF v_current_db <> 'neondb' THEN
        RAISE EXCEPTION 'SECURITY ABORT: Execution permitted only on database "neondb". Current database: %', v_current_db;
    END IF;

    -- 1. Resolve Account IDs
    SELECT id INTO v_acc_1200_id FROM finance.accounts 
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id AND account_code = '1200' AND is_active = true;
    
    SELECT id INTO v_acc_5010_id FROM finance.accounts 
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id AND account_code = '5010' AND is_active = true;
    
    SELECT id INTO v_acc_3010_id FROM finance.accounts 
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id AND account_code = '3010' AND is_active = true;

    IF v_acc_1200_id IS NULL OR v_acc_5010_id IS NULL OR v_acc_3010_id IS NULL THEN
        RAISE EXCEPTION 'PRECONDITION FAILED: Required GL Accounts (1200, 5010, 3010) not found or inactive.';
    END IF;

    -- 2. Resolve Active User ID for audit trail
    SELECT tm.user_id INTO v_user_id 
    FROM identity.tenant_memberships tm
    JOIN identity.users u ON u.id = tm.user_id
    WHERE tm.tenant_id = v_tenant_id AND u.status = 'active'
    LIMIT 1;

    IF v_user_id IS NULL THEN
        SELECT id INTO v_user_id FROM identity.users WHERE status = 'active' LIMIT 1;
    END IF;

    IF v_user_id IS NULL THEN
        RAISE EXCEPTION 'PRECONDITION FAILED: No active user found for audit attribution.';
    END IF;

    -- 3. Post Journal PROP-FIFO-001 (Dr 1200 / Cr 5010 PKR 135.00)
    INSERT INTO finance.journals (
        tenant_id, company_id, id, journal_no, journal_date, fiscal_period_id,
        description, functional_currency, source_module, source_type, source_id,
        idempotency_key, status, created_by, approved_by, posted_by, posted_at
    ) VALUES (
        v_tenant_id, v_company_id, v_jrn_adj_id, 'JRN-ADJ-PROP-FIFO-001', '2026-11-15'::date, v_fiscal_period_id,
        'Approved Model A FIFO Historical COGS Reconciliation Adjustment', 'PKR', 'inventory', 'fifo_reconciliation', 'PROP-FIFO-001',
        'IDEMP-PROP-FIFO-001-20261115', 'posted', v_user_id, v_user_id, v_user_id, now()
    )
    ON CONFLICT (tenant_id, company_id, journal_no) DO NOTHING;

    INSERT INTO finance.journal_lines (
        tenant_id, company_id, journal_id, line_no, account_id, description,
        transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional
    ) VALUES
        (v_tenant_id, v_company_id, v_jrn_adj_id, 1, v_acc_1200_id, 'Reconcile historical inventory asset under-credit', 'PKR', 135.000000, 1.0000000000, 135.0000, 0.0000),
        (v_tenant_id, v_company_id, v_jrn_adj_id, 2, v_acc_5010_id, 'Reconcile historical COGS manual entry over-charge', 'PKR', 135.000000, 1.0000000000, 0.0000, 135.0000)
    ON CONFLICT (tenant_id, company_id, journal_id, line_no) DO NOTHING;

    -- 4. Post Journal PROP-CAP-001 (Option A Capitalization: Dr 1200 / Cr 3010 PKR 135.00)
    INSERT INTO finance.journals (
        tenant_id, company_id, id, journal_no, journal_date, fiscal_period_id,
        description, functional_currency, source_module, source_type, source_id,
        idempotency_key, status, created_by, approved_by, posted_by, posted_at
    ) VALUES (
        v_tenant_id, v_company_id, v_jrn_cap_id, 'JRN-ADJ-PROP-CAP-001', '2026-11-15'::date, v_fiscal_period_id,
        'Approved Capitalization of 3 Unvalued Physical Units in MAIN-W1 / PICK-01', 'PKR', 'inventory', 'stock_capitalization', 'PROP-CAP-001',
        'IDEMP-PROP-CAP-001-20261115', 'posted', v_user_id, v_user_id, v_user_id, now()
    )
    ON CONFLICT (tenant_id, company_id, journal_no) DO NOTHING;

    INSERT INTO finance.journal_lines (
        tenant_id, company_id, journal_id, line_no, account_id, description,
        transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional
    ) VALUES
        (v_tenant_id, v_company_id, v_jrn_cap_id, 1, v_acc_1200_id, 'Capitalize 3 physical units SCISSOR-EA-01 @ PKR 45.00', 'PKR', 135.000000, 1.0000000000, 135.0000, 0.0000),
        (v_tenant_id, v_company_id, v_jrn_cap_id, 2, v_acc_3010_id, 'Opening equity recognition for capitalized inventory', 'PKR', 135.000000, 1.0000000000, 0.0000, 135.0000)
    ON CONFLICT (tenant_id, company_id, journal_id, line_no) DO NOTHING;

    -- 5. Create 3rd FIFO Cost Layer for 3 EA @ PKR 45.00 in MAIN-W1 / PICK-01
    INSERT INTO erp_inventory.inventory_cost_layer (
        tenant_id, company_id, layer_id, parent_layer_id, variant_id, warehouse_id, bin_id,
        status_code, source_document_type, source_document_id, source_line_number,
        received_at, original_qty, remaining_qty, unit_cost, currency_code
    ) VALUES (
        v_tenant_id, v_company_id, v_layer3_id, NULL, v_variant_id, v_main_wh_id, v_pick_bin_id,
        'available', 'opening_capitalization', v_jrn_cap_id, 1,
        '2026-11-15 11:57:00+00'::timestamptz, 3.000000000, 3.000000000, 45.000000000, 'PKR'
    )
    ON CONFLICT (tenant_id, company_id, layer_id) DO NOTHING;

    -- 6. Post-Adjustment Assertions
    SELECT COUNT(*), COALESCE(SUM(remaining_qty), 0), COALESCE(SUM(remaining_qty * unit_cost), 0)
      INTO v_total_layers, v_total_valued_qty, v_total_valued_cost
    FROM erp_inventory.inventory_cost_layer
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND variant_id = v_variant_id AND status_code = 'available';

    IF v_total_layers <> 3 OR v_total_valued_qty <> 50 OR v_total_valued_cost <> 2250.000000000 THEN
        RAISE EXCEPTION 'ASSERTION FAILED: Expected 3 layers, 50 EA valued, PKR 2,250.00 valuation. Got layers=%, qty=%, cost=%',
            v_total_layers, v_total_valued_qty, v_total_valued_cost;
    END IF;

    SELECT COALESCE(SUM(jl.debit_functional) - SUM(jl.credit_functional), 0) INTO v_gl_1200
    FROM finance.journal_lines jl
    JOIN finance.accounts a ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
    JOIN finance.journals j ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
    WHERE jl.tenant_id = v_tenant_id AND jl.company_id = v_company_id
      AND a.account_code = '1200' AND j.status = 'posted';

    IF v_gl_1200 <> 2250.0000 THEN
        RAISE EXCEPTION 'ASSERTION FAILED: Expected GL 1200 balance to equal PKR 2,250.00. Got %', v_gl_1200;
    END IF;

    RAISE NOTICE '[SUCCESS] Approved adjustments successfully posted. GL 1200 and FIFO valuation are 100%% reconciled at PKR 2,250.00 with ZERO variance.';
END $$;

COMMIT;
