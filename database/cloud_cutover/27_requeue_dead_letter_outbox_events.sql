-- Universal ERP -- Re-queue Purchasing Outbox Events for Immediate Processing
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Purpose: Safely and explicitly reset outbox events to pending AFTER finance configuration is verified.
-- Safety:
--   1. Strictly asserts connected database is neondb.
--   2. Asserts open fiscal period and accounts 1200 & 2010 exist before resetting.
--   3. Increments redrive_count for full operational auditability.
--   4. Never modifies inventory movements, stock balances, or already-published events.

BEGIN;

DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_current_db text;
    v_open_periods integer;
    v_acc_count integer;
    v_requeue_count integer;
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

    -- 2. Precondition Verification: Open Fiscal Period Must Exist
    SELECT count(*) INTO v_open_periods
    FROM finance.fiscal_periods
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id AND status = 'open';

    IF v_open_periods = 0 THEN
        RAISE EXCEPTION 'PRECONDITION FAILED: No open fiscal period found for CLOUD-LOGISTICS! Apply finance seed (Migration/Seed 26) first.';
    END IF;
    RAISE NOTICE '[PRECONDITION PASSED] % open fiscal period(s) found.', v_open_periods;

    -- 3. Precondition Verification: GL Control Accounts 1200 & 2010 Must Exist
    SELECT count(*) INTO v_acc_count
    FROM finance.accounts
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id
      AND account_code IN ('1200', '2010') AND is_active = true;

    IF v_acc_count < 2 THEN
        RAISE EXCEPTION 'PRECONDITION FAILED: Accounts 1200 or 2010 missing in finance.accounts for CLOUD-LOGISTICS! Apply finance seed first.';
    END IF;
    RAISE NOTICE '[PRECONDITION PASSED] Both required GL accounts (1200 Inventory & 2010 AP) exist and are active.';

    -- 4. Re-queue eligible unpublished outbox events
    UPDATE erp_purchasing.outbox_event
    SET status = 'pending',
        attempt_count = 0,
        last_error = NULL,
        next_retry_at = NULL,
        redrive_count = redrive_count + 1
    WHERE tenant_id = v_tenant_id
      AND company_id = v_company_id
      AND published_at IS NULL
      AND (status = 'dead_letter' OR next_retry_at IS NOT NULL OR attempt_count > 0);

    GET DIAGNOSTICS v_requeue_count = ROW_COUNT;

    IF v_requeue_count = 0 THEN
        RAISE NOTICE '[OUTBOX NOTICE] No dead-letter or delayed outbox events required re-queuing.';
    ELSE
        RAISE NOTICE '[OUTBOX RE-QUEUED] Successfully re-queued % outbox event(s) to pending for immediate processing.', v_requeue_count;
    END IF;
END $$;

-- Summary of current outbox state post re-queue
SELECT 
    event_id,
    grn_id,
    event_type,
    status,
    attempt_count,
    redrive_count,
    last_error,
    next_retry_at,
    occurred_at
FROM erp_purchasing.outbox_event
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY occurred_at ASC;

COMMIT;
