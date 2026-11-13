-- Universal ERP -- Proposed Idempotent FIFO Layer & Valuation Backfill (Model A: Strict Physical FIFO)
-- Target Database: Neon PostgreSQL Cloud (neondb) (FOR REVIEW ONLY - DO NOT RUN WITHOUT APPROVAL)
-- Target Tenant  : DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company : CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target SKU     : SCISSOR-EA-01 (2871558f-e577-4d2d-b365-fa60be311111)
--
-- Model A Reconciliation Target:
--   * Preserves original GRN receipt quantities:
--       - GRN-2024-01: 20 EA @ PKR 45 into MAIN-W1 / PICK-01
--       - GRN-2024-03: 30 EA @ PKR 45 into MAIN-W1 / RECEIVE-01
--   * Historical physical chronological FIFO consumption:
--       - POS-MWD4K5DH: 3 unvalued opening-stock EA + 2 EA from GRN-2024-01 (leaves 18 EA remaining)
--       - POS-MWB5YM9G: 1 unvalued opening-stock EA + 1 EA from GRN-2024-03 (leaves 29 EA remaining)
--       - POS-MWB642DH: 1 unvalued opening-stock EA from STORE-01 (leaves 3 EA unvalued in STORE-01)
--   * Resulting Inventory Valuation:
--       - PICK-01    : 18 EA valued @ PKR 45 = PKR 810.00 (0 EA unvalued)
--       - RECEIVE-01 : 29 EA valued @ PKR 45 = PKR 1,305.00 (0 EA unvalued)
--       - STORE-01   :  0 EA valued          = PKR   0.00 (3 EA unvalued)
--       - Total Stock: 50 EA physical, 47 EA valued (PKR 2,115.00), 3 EA unvalued
--   * General Ledger Account 1200:
--       - Current GL 1200 balance: PKR 1,980.00
--       - Model A FIFO Valuation: PKR 2,115.00
--       - Variance of PKR 135.00 is resolved via a separate Accounting proposal (Dr 1200 / Cr 5010),
--         NOT through synthetic inventory adjustments.
--   * Zero mutations to historical sales invoices, stock balances, movements, or journals.
--   * Fully idempotent: safe to execute repeatedly without duplicating layers or allocations.

BEGIN;

SELECT set_config('app.tenant_id', '22222222-2222-4222-8222-222222222222', true);
SELECT set_config('app.company_id', '22222222-2222-4222-8222-222222222223', true);

DO $$
DECLARE
    v_tenant_id       uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id      uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_variant_id      uuid := '2871558f-e577-4d2d-b365-fa60be311111'::uuid;
    v_main_wh_id      uuid := 'de9ffb26-8faa-4ec0-a90d-4945bdf4b7ff'::uuid;
    v_pick_bin_id     uuid := 'a6824c8e-1d91-42a3-bb49-b6136465e167'::uuid;
    v_receive_bin_id  uuid := '3e394e87-e01a-486b-824d-ec727c7d1a20'::uuid;

    -- Verified Purchasing GRNs & Receipt Movements
    v_grn1_id         uuid := '41a9d790-2bc9-4893-b2ac-bd6800192173'::uuid; -- GRN-2024-01 (20 EA @ 45 -> PICK-01)
    v_grn1_mov_id     uuid := 'eb1021a1-e6ce-40fa-a0ad-f3352ab20b9b'::uuid;
    v_grn1_ts         timestamptz := '2026-11-10 07:28:34.130976+00'::timestamptz;

    v_grn3_id         uuid := '07da9829-4327-4545-9bf5-117ef50fd756'::uuid; -- GRN-2024-03 (30 EA @ 45 -> RECEIVE-01)
    v_grn3_mov_id     uuid := 'bf7bb3f9-06b7-4217-aad4-76ee75dd6d20'::uuid;
    v_grn3_ts         timestamptz := '2026-11-10 07:28:34.831115+00'::timestamptz;

    -- Verified Historical POS Issues
    v_pos0_inv_id     uuid := '52692949-0229-4b47-bd34-a1f1cda42fbd'::uuid; -- POS-MWB5YM9G (2 EA @ 0 -> RECEIVE-01)
    v_pos0_mov_id     uuid := '56ba8ce8-6929-4b59-bf95-7b8ec2ceae59'::uuid;
    v_pos0_posted_at  timestamptz := '2026-11-10 20:49:48.048062+00'::timestamptz;

    v_pos1_inv_id     uuid := '28bef0a2-5422-4873-a4fb-4ace102b3708'::uuid; -- POS-MWB642DH (1 EA @ 45 -> STORE-01)
    v_pos1_mov_id     uuid := '41c2d138-3e0a-4171-9130-2df084c51d4e'::uuid;

    v_pos5_inv_id     uuid;
    v_pos5_mov_id     uuid;
    v_pos5_qty        numeric(24,9);
    v_pos5_unit_cost  numeric(24,9);
    v_pos5_bin_id     uuid;
    v_pos5_posted_at  timestamptz;

    v_layer1_id       uuid;
    v_layer2_id       uuid;
    v_alloc1_id       uuid;
    v_alloc2_id       uuid;

    v_pick_on_hand    numeric(24,9);
    v_recv_on_hand    numeric(24,9);
    v_total_on_hand   numeric(24,9);
    v_gl_1200_net     numeric(20,4);

    v_layer_orig_sum  numeric(24,9);
    v_layer_rem_sum   numeric(24,9);
    v_layer_val_sum   numeric(24,9);
    v_val_entry_sum   numeric(24,9);
BEGIN
    -- ======================================================================
    -- 1. Environment & Precondition Guardrails
    -- ======================================================================
    IF current_database() <> 'neondb' THEN
        RAISE EXCEPTION 'SAFETY_ABORT: Backfill is restricted to Neon neondb (connected to %).', current_database();
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.schema_migrations WHERE version = '026_fifo_valuation_layers') THEN
        RAISE EXCEPTION 'PRECONDITION_FAILED: Migration 026_fifo_valuation_layers must be applied before running this backfill.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM organization.companies c
        JOIN platform.tenants t ON t.id = c.tenant_id
        WHERE t.id = v_tenant_id AND c.id = v_company_id
          AND t.tenant_code = 'DEMO-CLOUD' AND c.company_code = 'CLOUD-LOGISTICS'
    ) THEN
        RAISE EXCEPTION 'PRECONDITION_FAILED: Target tenant DEMO-CLOUD / company CLOUD-LOGISTICS not found.';
    END IF;

    -- Verify expected stock_balance state (50 EA total: PICK-01=18, RECEIVE-01=29, STORE-01=3)
    SELECT COALESCE(sum(on_hand_qty), 0) INTO v_pick_on_hand
    FROM erp_inventory.stock_balance
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id AND variant_id = v_variant_id
      AND warehouse_id = v_main_wh_id AND bin_id = v_pick_bin_id AND status_code = 'available';

    SELECT COALESCE(sum(on_hand_qty), 0) INTO v_recv_on_hand
    FROM erp_inventory.stock_balance
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id AND variant_id = v_variant_id
      AND warehouse_id = v_main_wh_id AND bin_id = v_receive_bin_id AND status_code = 'available';

    SELECT COALESCE(sum(on_hand_qty), 0) INTO v_total_on_hand
    FROM erp_inventory.stock_balance
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id AND variant_id = v_variant_id
      AND status_code = 'available';

    IF v_pick_on_hand <> 18 OR v_recv_on_hand <> 29 OR v_total_on_hand <> 50 THEN
        RAISE EXCEPTION 'PRECONDITION_FAILED: Unexpected physical stock_balance (PICK-01=%, RECEIVE-01=%, TOTAL=%). Expected 18, 29, 50.',
            v_pick_on_hand, v_recv_on_hand, v_total_on_hand;
    END IF;

    -- Verify GL Account 1200 net debit balance = PKR 1,980.00
    SELECT COALESCE(sum(jl.debit_functional) - sum(jl.credit_functional), 0) INTO v_gl_1200_net
    FROM finance.journal_lines jl
    JOIN finance.accounts a
      ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
    JOIN finance.journals j
      ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
    WHERE jl.tenant_id = v_tenant_id AND jl.company_id = v_company_id
      AND a.account_code = '1200' AND j.status = 'posted';

    IF v_gl_1200_net <> 1980.0000 THEN
        RAISE EXCEPTION 'PRECONDITION_FAILED: GL Account 1200 net debit balance is %, expected 1980.0000.', v_gl_1200_net;
    END IF;

    -- Verify GRN movements and resolve POS-MWD4K5DH dynamically
    IF NOT EXISTS (
        SELECT 1 FROM erp_inventory.inventory_movement_leg
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND movement_id = v_grn1_mov_id AND bin_id = v_pick_bin_id AND quantity_delta = 20
    ) OR NOT EXISTS (
        SELECT 1 FROM erp_inventory.inventory_movement_leg
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND movement_id = v_grn3_mov_id AND bin_id = v_receive_bin_id AND quantity_delta = 30
    ) THEN
        RAISE EXCEPTION 'PRECONDITION_FAILED: Expected GRN-2024-01 (20 EA) or GRN-2024-03 (30 EA) movement legs missing.';
    END IF;

    SELECT si.invoice_id, m.movement_id, sil.quantity, sil.unit_cost, sil.bin_id, si.posted_at
      INTO v_pos5_inv_id, v_pos5_mov_id, v_pos5_qty, v_pos5_unit_cost, v_pos5_bin_id, v_pos5_posted_at
    FROM erp_sales.sales_invoice si
    JOIN erp_sales.sales_invoice_line sil
      ON sil.tenant_id = si.tenant_id AND sil.company_id = si.company_id AND sil.invoice_id = si.invoice_id
    JOIN erp_inventory.inventory_movement m
      ON m.tenant_id = si.tenant_id AND m.company_id = si.company_id
     AND m.source_document_type = 'sales_invoice' AND m.source_document_id = si.invoice_id
    WHERE si.tenant_id = v_tenant_id AND si.company_id = v_company_id
      AND si.invoice_number = 'POS-MWD4K5DH' AND sil.line_number = 1;

    IF v_pos5_inv_id IS NULL OR v_pos5_qty <> 5 OR v_pos5_unit_cost <> 45 OR v_pos5_bin_id <> v_pick_bin_id THEN
        RAISE EXCEPTION 'PRECONDITION_FAILED: Invoice POS-MWD4K5DH did not match expected 5 EA @ PKR 45 in PICK-01.';
    END IF;

    IF (SELECT count(*) FROM erp_sales.sales_invoice WHERE tenant_id = v_tenant_id AND company_id = v_company_id) <> 3
       OR NOT EXISTS (
           SELECT 1 FROM erp_sales.sales_invoice
           WHERE tenant_id = v_tenant_id AND company_id = v_company_id
             AND invoice_id = v_pos0_inv_id AND invoice_number = 'POS-MWB5YM9G' AND total_amount = 0
       )
       OR NOT EXISTS (
           SELECT 1 FROM erp_sales.sales_invoice
           WHERE tenant_id = v_tenant_id AND company_id = v_company_id
             AND invoice_id = v_pos1_inv_id AND invoice_number = 'POS-MWB642DH' AND total_amount = 55
       ) THEN
        RAISE EXCEPTION 'PRECONDITION_FAILED: Historical POS invoice baseline (3 invoices: POS-MWB5YM9G, POS-MWB642DH, POS-MWD4K5DH) mismatch.';
    END IF;

    -- ======================================================================
    -- 2. Backfill Layer 1: GRN-2024-01 (Original 20 EA @ PKR 45 in PICK-01)
    -- ======================================================================
    INSERT INTO erp_inventory.inventory_cost_layer (
        tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id, status_code,
        source_document_type, source_document_id, source_line_number, source_movement_id,
        received_at, original_qty, remaining_qty, unit_cost, currency_code, created_at, updated_at
    ) VALUES (
        v_tenant_id, v_company_id, 'e4a1c001-2024-4001-8001-41a9d7900001'::uuid,
        v_variant_id, v_main_wh_id, v_pick_bin_id, 'available',
        'goods_receipt', v_grn1_id, 1, v_grn1_mov_id,
        v_grn1_ts, 20.000000000, 20.000000000, 45.000000000, 'PKR', v_grn1_ts, now()
    )
    ON CONFLICT (tenant_id, company_id, source_document_type, source_document_id, source_line_number, bin_id)
      WHERE parent_layer_id IS NULL AND source_document_id IS NOT NULL AND source_line_number IS NOT NULL
    DO NOTHING;

    SELECT layer_id INTO v_layer1_id
    FROM erp_inventory.inventory_cost_layer
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND source_document_type = 'goods_receipt' AND source_document_id = v_grn1_id
      AND source_line_number = 1 AND bin_id = v_pick_bin_id;

    INSERT INTO erp_inventory.valuation_entry (
        tenant_id, company_id, valuation_entry_id, movement_id, variant_id,
        entry_type, quantity, unit_cost, extended_cost, currency_code, posted_at
    )
    SELECT
        v_tenant_id, v_company_id, 'e4a1f001-2024-4001-8001-eb1021a10001'::uuid,
        v_grn1_mov_id, v_variant_id,
        'receipt_cost', 20.000000000, 45.000000000, 900.000000000, 'PKR', v_grn1_ts
    WHERE NOT EXISTS (
        SELECT 1 FROM erp_inventory.valuation_entry
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND movement_id = v_grn1_mov_id AND entry_type = 'receipt_cost'
    );

    -- ======================================================================
    -- 3. Backfill Layer 2: GRN-2024-03 (Original 30 EA @ PKR 45 in RECEIVE-01)
    -- ======================================================================
    INSERT INTO erp_inventory.inventory_cost_layer (
        tenant_id, company_id, layer_id, variant_id, warehouse_id, bin_id, status_code,
        source_document_type, source_document_id, source_line_number, source_movement_id,
        received_at, original_qty, remaining_qty, unit_cost, currency_code, created_at, updated_at
    ) VALUES (
        v_tenant_id, v_company_id, 'e4a1c002-2024-4003-8001-07da98290001'::uuid,
        v_variant_id, v_main_wh_id, v_receive_bin_id, 'available',
        'goods_receipt', v_grn3_id, 1, v_grn3_mov_id,
        v_grn3_ts, 30.000000000, 30.000000000, 45.000000000, 'PKR', v_grn3_ts, now()
    )
    ON CONFLICT (tenant_id, company_id, source_document_type, source_document_id, source_line_number, bin_id)
      WHERE parent_layer_id IS NULL AND source_document_id IS NOT NULL AND source_line_number IS NOT NULL
    DO NOTHING;

    SELECT layer_id INTO v_layer2_id
    FROM erp_inventory.inventory_cost_layer
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND source_document_type = 'goods_receipt' AND source_document_id = v_grn3_id
      AND source_line_number = 1 AND bin_id = v_receive_bin_id;

    INSERT INTO erp_inventory.valuation_entry (
        tenant_id, company_id, valuation_entry_id, movement_id, variant_id,
        entry_type, quantity, unit_cost, extended_cost, currency_code, posted_at
    )
    SELECT
        v_tenant_id, v_company_id, 'e4a1f002-2024-4003-8001-bf7bb3f90001'::uuid,
        v_grn3_mov_id, v_variant_id,
        'receipt_cost', 30.000000000, 45.000000000, 1350.000000000, 'PKR', v_grn3_ts
    WHERE NOT EXISTS (
        SELECT 1 FROM erp_inventory.valuation_entry
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND movement_id = v_grn3_mov_id AND entry_type = 'receipt_cost'
    );

    -- ======================================================================
    -- 4. Historical Consumption Audit: POS-MWD4K5DH (Model A: 2 EA from GRN-2024-01 @ PKR 45)
    --    Chronological FIFO in PICK-01 consumed 3 EA unvalued opening stock
    --    and 2 EA from GRN-2024-01, leaving exactly 18 EA remaining (valued at PKR 810.00).
    -- ======================================================================
    INSERT INTO erp_inventory.inventory_cost_allocation (
        tenant_id, company_id, allocation_id, layer_id, movement_id,
        source_document_type, source_document_id, source_line_number,
        variant_id, consumed_qty, unit_cost, extended_cost, currency_code, allocated_at
    ) VALUES (
        v_tenant_id, v_company_id, 'e4a1a001-2024-4005-8001-41c2d1380001'::uuid, v_layer1_id, v_pos5_mov_id,
        'sales_invoice', v_pos5_inv_id, 1,
        v_variant_id, 2.000000000, 45.000000000, 90.000000000, 'PKR', v_pos5_posted_at
    )
    ON CONFLICT (tenant_id, company_id, source_document_type, source_document_id, source_line_number, layer_id)
    DO NOTHING
    RETURNING allocation_id INTO v_alloc1_id;

    IF v_alloc1_id IS NOT NULL THEN
        UPDATE erp_inventory.inventory_cost_layer
        SET remaining_qty = remaining_qty - 2.000000000,
            updated_at = now()
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND layer_id = v_layer1_id AND remaining_qty >= 2.000000000;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'BACKFILL_FAILED: Layer 1 (GRN-2024-01) lacked 2 EA remaining for POS-MWD4K5DH.';
        END IF;
    END IF;

    INSERT INTO erp_inventory.valuation_entry (
        tenant_id, company_id, valuation_entry_id, movement_id, variant_id,
        entry_type, quantity, unit_cost, extended_cost, currency_code, posted_at
    )
    SELECT
        v_tenant_id, v_company_id, 'e4a1f004-2024-4005-8001-41c2d1380002'::uuid,
        v_pos5_mov_id, v_variant_id,
        'issue_cost', -2.000000000, 45.000000000, -90.000000000, 'PKR', v_pos5_posted_at
    WHERE NOT EXISTS (
        SELECT 1 FROM erp_inventory.valuation_entry
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND movement_id = v_pos5_mov_id AND entry_type = 'issue_cost'
    );

    -- ======================================================================
    -- 5. Historical Consumption Audit: POS-MWB5YM9G (Model A: 1 EA from GRN-2024-03 @ PKR 45)
    --    Chronological FIFO in RECEIVE-01 consumed 1 EA unvalued opening stock
    --    and 1 EA from GRN-2024-03, leaving exactly 29 EA remaining (valued at PKR 1,305.00).
    -- ======================================================================
    INSERT INTO erp_inventory.inventory_cost_allocation (
        tenant_id, company_id, allocation_id, layer_id, movement_id,
        source_document_type, source_document_id, source_line_number,
        variant_id, consumed_qty, unit_cost, extended_cost, currency_code, allocated_at
    ) VALUES (
        v_tenant_id, v_company_id, 'e4a1a002-2024-4003-8001-56ba8ce80001'::uuid, v_layer2_id, v_pos0_mov_id,
        'sales_invoice', v_pos0_inv_id, 1,
        v_variant_id, 1.000000000, 45.000000000, 45.000000000, 'PKR', v_pos0_posted_at
    )
    ON CONFLICT (tenant_id, company_id, source_document_type, source_document_id, source_line_number, layer_id)
    DO NOTHING
    RETURNING allocation_id INTO v_alloc2_id;

    IF v_alloc2_id IS NOT NULL THEN
        UPDATE erp_inventory.inventory_cost_layer
        SET remaining_qty = remaining_qty - 1.000000000,
            updated_at = now()
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND layer_id = v_layer2_id AND remaining_qty >= 1.000000000;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'BACKFILL_FAILED: Layer 2 (GRN-2024-03) lacked 1 EA remaining for POS-MWB5YM9G.';
        END IF;
    END IF;

    INSERT INTO erp_inventory.valuation_entry (
        tenant_id, company_id, valuation_entry_id, movement_id, variant_id,
        entry_type, quantity, unit_cost, extended_cost, currency_code, posted_at
    )
    SELECT
        v_tenant_id, v_company_id, 'e4a1f005-2024-4003-8001-56ba8ce80002'::uuid,
        v_pos0_mov_id, v_variant_id,
        'issue_cost', -1.000000000, 45.000000000, -45.000000000, 'PKR', v_pos0_posted_at
    WHERE NOT EXISTS (
        SELECT 1 FROM erp_inventory.valuation_entry
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND movement_id = v_pos0_mov_id AND entry_type = 'issue_cost'
    );

    -- ======================================================================
    -- 6. Post-Backfill Reconciliation Assertions (Model A)
    -- ======================================================================
    SELECT COALESCE(sum(original_qty), 0),
           COALESCE(sum(remaining_qty), 0),
           COALESCE(sum(remaining_qty * unit_cost), 0)
      INTO v_layer_orig_sum, v_layer_rem_sum, v_layer_val_sum
    FROM erp_inventory.inventory_cost_layer
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND variant_id = v_variant_id AND status_code = 'available';

    SELECT COALESCE(sum(extended_cost), 0) INTO v_val_entry_sum
    FROM erp_inventory.valuation_entry
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND variant_id = v_variant_id;

    IF v_layer_orig_sum <> 50 OR v_layer_rem_sum <> 47 OR v_layer_val_sum <> 2115.000000000 OR v_val_entry_sum <> 2115.000000000 THEN
        RAISE EXCEPTION 'POSTCHECK_FAILED: Model A check failed! Orig=% (exp 50), Rem=% (exp 47), LayerVal=% (exp 2115), ValEntrySum=% (exp 2115).',
            v_layer_orig_sum, v_layer_rem_sum, v_layer_val_sum, v_val_entry_sum;
    END IF;

    RAISE NOTICE '[PASS] Model A FIFO backfill reconciled: Original=50 EA, Remaining=47 EA (18 PICK-01 + 29 RECEIVE-01), Valued=PKR 2,115.00. (Remaining 3 EA in STORE-01 is protected unvalued stock).';
END $$;

COMMIT;