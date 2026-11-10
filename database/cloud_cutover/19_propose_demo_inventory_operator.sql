-- Universal ERP -- Propose Separate Least-Privilege Inventory Operator Role (Staging Demo)
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target User: tanzeelurrehman41@gmail.com
-- New Role Code: DEMO_INVENTORY_OPERATOR
-- Granted Permissions: inventory.transfer.create, inventory.adjustment.create (scope_type: 'company')
-- Safety: 
--   1. Strict Neon DB guard (aborts on local/wrong database).
--   2. Preserves DEMO_INVENTORY_VIEWER completely unchanged (0 transfer/adjustment permissions added to it).
--   3. Scoped strictly to company CLOUD-LOGISTICS and user Tanzeel.
--   4. Strictly zero finance, purchasing, sales, or administrative privileges.
--   5. Verifies Zeelman isolation invariant.

BEGIN;

DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_email text := 'tanzeelurrehman41@gmail.com';
    v_viewer_role_code text := 'DEMO_INVENTORY_VIEWER';
    v_operator_role_code text := 'DEMO_INVENTORY_OPERATOR';

    v_current_db text;
    v_user_id uuid;
    v_membership_id uuid;
    v_viewer_role_id uuid;
    v_operator_role_id uuid;
    v_perm text;
    v_perm_count integer;

    c_all_expected_perms text[] := ARRAY[
        'inventory.product.view',
        'inventory.uom.view',
        'inventory.variant.view',
        'inventory.warehouse.view',
        'inventory.availability.view',
        'inventory.product.create',
        'inventory.uom.create',
        'inventory.variant.create',
        'inventory.warehouse.create',
        'inventory.receipt.create',
        'inventory.receipt.post',
        'inventory.transfer.create',
        'inventory.adjustment.create'
    ];

    c_forbidden_perms text[] := ARRAY[
        'purchasing.po.create',
        'purchasing.po.view',
        'sales.order.create',
        'sales.order.view',
        'finance.gl.post',
        'identity.user.create'
    ];
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

    -- 2. Locate Target User and Active Tenant Membership
    SELECT id INTO v_user_id
    FROM identity.users
    WHERE email_normalized = v_email AND status = 'active';

    IF v_user_id IS NULL THEN
        RAISE EXCEPTION 'ABORT: Active user % not found in identity.users.', v_email;
    END IF;

    SELECT id INTO v_membership_id
    FROM identity.tenant_memberships
    WHERE tenant_id = v_tenant_id AND user_id = v_user_id AND status = 'active';

    IF v_membership_id IS NULL THEN
        RAISE EXCEPTION 'ABORT: Active tenant membership not found for user % in tenant DEMO-CLOUD.', v_email;
    END IF;

    -- 3. Confirm DEMO_INVENTORY_VIEWER exists and has NOT been modified with transfer/adjustment
    SELECT id INTO v_viewer_role_id
    FROM "authorization".roles
    WHERE tenant_id = v_tenant_id AND role_code = v_viewer_role_code;

    IF v_viewer_role_id IS NULL THEN
        RAISE EXCEPTION 'ABORT: Existing role % does not exist in tenant DEMO-CLOUD.', v_viewer_role_code;
    END IF;

    IF EXISTS (
        SELECT 1 FROM "authorization".role_permissions
        WHERE tenant_id = v_tenant_id
          AND role_id = v_viewer_role_id
          AND permission_code IN ('inventory.transfer.create', 'inventory.adjustment.create')
    ) THEN
        RAISE EXCEPTION 'SAFETY VIOLATION: DEMO_INVENTORY_VIEWER already contains transfer or adjustment permissions! Must remain clean.';
    END IF;

    -- 4. Verify inventory.transfer.create and inventory.adjustment.create exist in authorization.permissions
    IF NOT EXISTS (SELECT 1 FROM "authorization".permissions WHERE permission_code = 'inventory.transfer.create') THEN
        RAISE EXCEPTION 'PRECONDITION FAILED: Permission inventory.transfer.create does not exist in authorization.permissions!';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM "authorization".permissions WHERE permission_code = 'inventory.adjustment.create') THEN
        RAISE EXCEPTION 'PRECONDITION FAILED: Permission inventory.adjustment.create does not exist in authorization.permissions!';
    END IF;

    -- 5. Idempotently Create or Locate DEMO_INVENTORY_OPERATOR Role
    SELECT id INTO v_operator_role_id
    FROM "authorization".roles
    WHERE tenant_id = v_tenant_id AND role_code = v_operator_role_code;

    IF v_operator_role_id IS NULL THEN
        v_operator_role_id := gen_random_uuid();
        INSERT INTO "authorization".roles (
            id, tenant_id, role_code, role_name, description, is_system_role
        )
        VALUES (
            v_operator_role_id,
            v_tenant_id,
            v_operator_role_code,
            'Demo Inventory Operator',
            'Least-privilege role for executing stock transfers and adjustments in CLOUD-LOGISTICS',
            false
        );
        RAISE NOTICE '[ROLE CREATED] Created new role % with ID %', v_operator_role_code, v_operator_role_id;
    ELSE
        RAISE NOTICE '[ROLE EXISTS] Located existing role % with ID %', v_operator_role_code, v_operator_role_id;
    END IF;

    -- 6. Assign DEMO_INVENTORY_OPERATOR strictly to Tanzeel's tenant membership
    INSERT INTO "authorization".membership_role_assignments (
        tenant_id, membership_id, role_id
    )
    VALUES (
        v_tenant_id, v_membership_id, v_operator_role_id
    )
    ON CONFLICT (tenant_id, membership_id, role_id) DO NOTHING;

    -- Assert role is assigned ONLY to Tanzeel
    IF EXISTS (
        SELECT 1 FROM "authorization".membership_role_assignments
        WHERE tenant_id = v_tenant_id
          AND role_id = v_operator_role_id
          AND membership_id <> v_membership_id
    ) THEN
        RAISE EXCEPTION 'SAFETY ABORT: Role % is assigned to unexpected memberships!', v_operator_role_code;
    END IF;

    -- 7. Grant inventory.transfer.create and inventory.adjustment.create to DEMO_INVENTORY_OPERATOR
    INSERT INTO "authorization".role_permissions (
        tenant_id, role_id, permission_code, scope_type
    )
    VALUES 
        (v_tenant_id, v_operator_role_id, 'inventory.transfer.create', 'company'),
        (v_tenant_id, v_operator_role_id, 'inventory.adjustment.create', 'company')
    ON CONFLICT (tenant_id, role_id, permission_code, scope_type) DO NOTHING;

    RAISE NOTICE '[PERMISSIONS GRANTED] Assigned transfer and adjustment permissions to role %', v_operator_role_code;

    -- 8. Safety Audit: Assert DEMO_INVENTORY_OPERATOR holds EXACTLY 2 permissions
    SELECT count(*) INTO v_perm_count
    FROM "authorization".role_permissions
    WHERE tenant_id = v_tenant_id AND role_id = v_operator_role_id;

    IF v_perm_count <> 2 THEN
        RAISE EXCEPTION 'SAFETY AUDIT FAILED: Role % has % permissions (expected exactly 2)!', v_operator_role_code, v_perm_count;
    END IF;

    IF EXISTS (
        SELECT 1 FROM "authorization".role_permissions
        WHERE tenant_id = v_tenant_id
          AND role_id = v_operator_role_id
          AND permission_code NOT IN ('inventory.transfer.create', 'inventory.adjustment.create')
    ) THEN
        RAISE EXCEPTION 'SAFETY VIOLATION: Role % contains unauthorized permissions!', v_operator_role_code;
    END IF;

    -- 9. Positive Verification: user_has_permission returns TRUE for all 13 effective permissions
    FOREACH v_perm IN ARRAY c_all_expected_perms
    LOOP
        IF NOT identity.user_has_permission(v_tenant_id, v_company_id, v_user_id, v_perm) THEN
            RAISE EXCEPTION 'VERIFICATION FAILED: user_has_permission returned FALSE for % on user %!', v_perm, v_email;
        END IF;
    END LOOP;
    RAISE NOTICE '[VERIFIED] All 13 expected inventory permissions evaluate to TRUE for %', v_email;

    -- 10. Negative Verification: user_has_permission returns FALSE for non-inventory operations
    FOREACH v_perm IN ARRAY c_forbidden_perms
    LOOP
        IF identity.user_has_permission(v_tenant_id, v_company_id, v_user_id, v_perm) THEN
            RAISE EXCEPTION 'SAFETY VIOLATION: user_has_permission unexpectedly returned TRUE for forbidden permission %!', v_perm;
        END IF;
    END LOOP;
    RAISE NOTICE '[VERIFIED] All non-inventory forbidden permissions evaluate to FALSE (strict boundary preserved)';

    -- 11. Safety Invariant: Confirm Zeelman context remains intact
    IF NOT EXISTS (
        SELECT 1 FROM identity.list_user_company_contexts(
            'https://securetoken.google.com/universal-erp-staging',
            'g5UVBoX1RFODIQ3suHZtN1DUiam2'
        )
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
    ) THEN
        RAISE EXCEPTION 'SAFETY INVARIANT VIOLATION: Demo account zeelman49@gmail.com context was disrupted!';
    END IF;

    RAISE NOTICE '[SUCCESS] Role DEMO_INVENTORY_OPERATOR successfully defined, granted, and audited for %', v_email;
END $$;

COMMIT;
