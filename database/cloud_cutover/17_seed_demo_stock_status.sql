-- Universal ERP -- Seed Tenant Stock Statuses for DEMO-CLOUD (Staging Demo)
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Purpose: Resolve FK violation on erp_inventory.inventory_movement_leg(tenant_id, status_code)
-- Safety: Transactional, idempotent, strictly least privilege

BEGIN;

DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_current_db text;
    v_count integer;
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
        RAISE EXCEPTION 'ABORT: Target tenant DEMO-CLOUD (ID %) not found or inactive.', v_tenant_id;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM organization.companies
        WHERE id = v_company_id AND tenant_id = v_tenant_id AND company_code = 'CLOUD-LOGISTICS' AND status = 'active'
    ) THEN
        RAISE EXCEPTION 'ABORT: Target company CLOUD-LOGISTICS (ID %) not found or inactive.', v_company_id;
    END IF;

    -- 2. Idempotently Insert Stock Statuses for DEMO-CLOUD
    INSERT INTO erp_inventory.stock_status (
        tenant_id, status_code, description, is_reservable, is_pickable
    )
    VALUES
        (v_tenant_id, 'available', 'Available for normal inventory use.', true, true),
        (v_tenant_id, 'quality_hold', 'Held for inspection or quality review.', false, false),
        (v_tenant_id, 'blocked', 'Blocked from normal use.', false, false)
    ON CONFLICT (tenant_id, status_code) DO NOTHING;

    RAISE NOTICE '[SEEDED] Seeded standard stock statuses (available, quality_hold, blocked) for DEMO-CLOUD';

    -- 3. Verify that stock_status has 'available'
    SELECT count(*) INTO v_count
    FROM erp_inventory.stock_status
    WHERE tenant_id = v_tenant_id AND status_code IN ('available', 'quality_hold', 'blocked');

    IF v_count < 3 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: Expected 3 stock statuses for DEMO-CLOUD, found %', v_count;
    END IF;

    -- 4. Ensure erp_app has SELECT on erp_inventory.stock_status
    EXECUTE 'GRANT SELECT ON erp_inventory.stock_status TO erp_app;';
    RAISE NOTICE '[TABLE PRIVILEGES] Ensured erp_app has SELECT on erp_inventory.stock_status';

    -- 5. Safety Invariant: Confirm Zeelman context remains intact
    IF NOT EXISTS (
        SELECT 1 FROM identity.list_user_company_contexts(
            'https://securetoken.google.com/universal-erp-staging',
            'g5UVBoX1RFODIQ3suHZtN1DUiam2'
        )
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
    ) THEN
        RAISE EXCEPTION 'SAFETY INVARIANT VIOLATION: Demo account zeelman49@gmail.com context was disrupted!';
    END IF;

    RAISE NOTICE '[COMPLETE] Tenant stock statuses successfully verified for DEMO-CLOUD';
END $$;

COMMIT;
