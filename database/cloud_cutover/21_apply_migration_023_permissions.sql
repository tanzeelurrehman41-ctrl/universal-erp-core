-- Universal ERP -- Apply Migration 023 (Register Transfer & Adjustment Permissions on Neon)
-- Target Database: Neon PostgreSQL (neondb)
-- Target Module: inventory
-- Safety: Strictly Neon neondb only, idempotent, zero role assignments, zero stock changes

BEGIN;

DO $$
DECLARE
    v_current_db text;
    v_count integer;
BEGIN
    -- 0. Database Safety Check: Strictly Neon neondb only
    SELECT current_database() INTO v_current_db;
    IF v_current_db <> 'neondb' THEN
        RAISE EXCEPTION 'SECURITY ABORT: Script must execute against Neon database "neondb". Current database: %', v_current_db;
    END IF;

    -- 1. Idempotently Insert Permission Definitions into authorization.permissions
    INSERT INTO "authorization".permissions (permission_code, description, module_code)
    VALUES
      ('inventory.transfer.create', 'Create and post inter-bin inventory transfers.', 'inventory'),
      ('inventory.adjustment.create', 'Create and post inventory quantity adjustments.', 'inventory')
    ON CONFLICT (permission_code) DO NOTHING;

    RAISE NOTICE '[CATALOG REGISTERED] Permissions inventory.transfer.create and inventory.adjustment.create registered.';

    -- 2. Record in public.schema_migrations ledger
    INSERT INTO public.schema_migrations (version, applied_at)
    VALUES ('023_register_inventory_transfer_and_adjustment_permissions', now())
    ON CONFLICT (version) DO NOTHING;

    -- 3. Verification: Assert both permissions exist in catalog
    SELECT count(*) INTO v_count
    FROM "authorization".permissions
    WHERE permission_code IN ('inventory.transfer.create', 'inventory.adjustment.create');

    IF v_count <> 2 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: Expected exactly 2 permissions in catalog, found %!', v_count;
    END IF;

    -- 4. Invariant: Assert DEMO_INVENTORY_VIEWER role is untouched (0 transfer/adjustment permissions)
    IF EXISTS (
        SELECT 1 FROM "authorization".role_permissions rp
        JOIN "authorization".roles r ON r.id = rp.role_id AND r.tenant_id = rp.tenant_id
        WHERE r.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
          AND r.role_code = 'DEMO_INVENTORY_VIEWER'
          AND rp.permission_code IN ('inventory.transfer.create', 'inventory.adjustment.create')
    ) THEN
        RAISE EXCEPTION 'SAFETY VIOLATION: DEMO_INVENTORY_VIEWER was mutated!';
    END IF;

    -- 5. Invariant: Assert Zeelman context is intact
    IF NOT EXISTS (
        SELECT 1 FROM identity.list_user_company_contexts(
            'https://securetoken.google.com/universal-erp-staging',
            'g5UVBoX1RFODIQ3suHZtN1DUiam2'
        )
        WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
          AND company_id = '22222222-2222-4222-8222-222222222223'::uuid
    ) THEN
        RAISE EXCEPTION 'SAFETY INVARIANT VIOLATION: Demo account zeelman49@gmail.com context was disrupted!';
    END IF;

    RAISE NOTICE '[SUCCESS] Migration 023 registered and verified cleanly on Neon.';
END $$;

COMMIT;
