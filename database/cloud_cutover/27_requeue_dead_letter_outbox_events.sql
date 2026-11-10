-- Universal ERP -- Re-queue Purchasing Outbox Events for Immediate Processing
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target Events: GRN-2024-01 (41a9d790-2bc9-4893-b2ac-bd6800192173), GRN-2024-03 (07da9829-4327-4545-9bf5-117ef50fd756)
-- Purpose: Safely and atomically reset dead-letter outbox events to pending AFTER finance configuration is verified.
-- Safety:
--   1. Strictly asserts connected database is neondb.
--   2. Asserts open fiscal period covering 2026-11-10 exists before resetting.
--   3. Asserts GL control accounts 1200 (Asset/Debit) & 2010 (Liability/Credit) exist and are active.
--   4. Targets strictly the two specified GRN event IDs with status = 'dead_letter' and published_at IS NULL.
--   5. Enforces single atomic transaction with strict abort/rollback unless exactly 2 rows are updated.
--   6. Increments redrive_count for full operational auditability.

BEGIN;

DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_grn_id_1 uuid := '41a9d790-2bc9-4893-b2ac-bd6800192173'::uuid; -- GRN-2024-01 (20 EA)
    v_grn_id_2 uuid := '07da9829-4327-4545-9bf5-117ef50fd756'::uuid; -- GRN-2024-03 (30 EA)
    v_current_db text;
    v_open_periods integer;
    v_requeue_count integer;
    r RECORD;
BEGIN
    -- 0. Database Safety Check: Strictly Neon neondb only
    SELECT current_database() INTO v_current_db;
    IF v_current_db <> 'neondb' THEN
        RAISE EXCEPTION 'SECURITY ABORT: Script must execute against Neon database "neondb". Current database: %', v_current_db;
    END IF;

    -- 1. Assert Target Tenant and Company exist and are active
    IF NOT EXISTS (
        SELECT 1 FROM platform.tenants
        WHERE id = v_tenant_id AND tenant_code = 'DEMO-CLOUD' AND status = 'active'
    ) THEN
        RAISE EXCEPTION 'ABORT: Target tenant DEMO-CLOUD not found or inactive.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM organization.companies
        WHERE id = v_company_id AND tenant_id = v_tenant_id AND company_code = 'CLOUD-LOGISTICS' AND status = 'active'
    ) THEN
        RAISE EXCEPTION 'ABORT: Target company CLOUD-LOGISTICS not found or inactive.';
    END IF;

    -- 2. Precondition Verification: Open Fiscal Period Covering 2026-11-10 Must Exist
    SELECT count(*) INTO v_open_periods
    FROM finance.fiscal_periods
    WHERE tenant_id = v_tenant_id 
      AND company_id = v_company_id 
      AND status = 'open'
      AND '2026-11-10'::date BETWEEN starts_on AND ends_on;

    IF v_open_periods = 0 THEN
        RAISE EXCEPTION 'PRECONDITION FAILED: No open fiscal period covering 2026-11-10 found for CLOUD-LOGISTICS! Apply finance seed (Migration/Seed 26) first.';
    END IF;
    RAISE NOTICE '[PRECONDITION PASSED] Open fiscal period covering 2026-11-10 verified.';

    -- 3. Precondition Verification: GL Control Accounts 1200 & 2010 Must Exist with Strict Semantics
    IF NOT EXISTS (
        SELECT 1 FROM finance.accounts
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND account_code = '1200' AND is_active = true
          AND account_type = 'asset' AND normal_balance = 'debit'
    ) THEN
        RAISE EXCEPTION 'PRECONDITION FAILED: Account 1200 (Merchandise Inventory Asset) missing, inactive, or invalid type/normal balance.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM finance.accounts
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
          AND account_code = '2010' AND is_active = true
          AND account_type = 'liability' AND normal_balance = 'credit'
    ) THEN
        RAISE EXCEPTION 'PRECONDITION FAILED: Account 2010 (Accounts Payable) missing, inactive, or invalid type/normal balance.';
    END IF;
    RAISE NOTICE '[PRECONDITION PASSED] Both required GL accounts (1200 Asset/Debit & 2010 Liability/Credit) verified active.';

    -- 4. Atomic Re-queue strictly for the two target GRN dead-letter outbox events
    CREATE TEMP TABLE tmp_requeued_events ON COMMIT DROP AS
    UPDATE erp_purchasing.outbox_event
    SET status = 'pending',
        attempt_count = 0,
        last_error = NULL,
        next_retry_at = NULL,
        redrive_count = redrive_count + 1
    WHERE tenant_id = v_tenant_id
      AND company_id = v_company_id
      AND grn_id IN (v_grn_id_1, v_grn_id_2)
      AND event_type = 'grn.posted'
      AND status = 'dead_letter'
      AND published_at IS NULL
    RETURNING event_id, grn_id, event_type, status, attempt_count, redrive_count, occurred_at;

    GET DIAGNOSTICS v_requeue_count = ROW_COUNT;

    IF v_requeue_count <> 2 THEN
        RAISE EXCEPTION 'ABORT: Expected exactly 2 dead-letter outbox events for GRN-2024-01 and GRN-2024-03, but matched % row(s). Rolling back.', v_requeue_count;
    END IF;

    FOR r IN SELECT * FROM tmp_requeued_events ORDER BY occurred_at ASC LOOP
        RAISE NOTICE '[OUTBOX RE-QUEUED] Event ID: %, GRN ID: %, Status: %, Redrive Count: %', r.event_id, r.grn_id, r.status, r.redrive_count;
    END LOOP;

    RAISE NOTICE '[SUCCESS] Exactly 2 outbox events safely reset to pending for immediate processing.';
END $$;

-- Summary of target outbox state post re-queue
SELECT 
    event_id,
    grn_id,
    event_type,
    status,
    attempt_count,
    redrive_count,
    last_error,
    next_retry_at,
    published_at,
    occurred_at
FROM erp_purchasing.outbox_event
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND grn_id IN (
      '41a9d790-2bc9-4893-b2ac-bd6800192173'::uuid,
      '07da9829-4327-4545-9bf5-117ef50fd756'::uuid
  )
ORDER BY occurred_at ASC;

COMMIT;
