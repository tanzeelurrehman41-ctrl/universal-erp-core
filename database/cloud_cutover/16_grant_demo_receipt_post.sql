-- Universal ERP -- Grant Stock Receipt Permissions (Staging Demo)
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target User: tanzeelurrehman41@gmail.com
-- Granted Permissions: inventory.receipt.create, inventory.receipt.post
-- Safety: Transactional, idempotent, strictly least privilege, zero transfer/adjustment/admin rights

BEGIN;

DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_email text := 'tanzeelurrehman41@gmail.com';
    v_role_code text := 'DEMO_INVENTORY_VIEWER';

    v_current_db text;
    v_user_id uuid;
    v_membership_id uuid;
    v_role_id uuid;
    v_perm text;
    v_perm_count integer;

    -- All 11 allowed permissions (5 read + 4 catalog/warehouse create + 2 receipt)
    c_allowed_perms text[] := ARRAY[
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
        'inventory.receipt.post'
    ];

    -- Representative write / transfer / adjustment / purchasing / sales permissions strictly denied
    c_forbidden_perms text[] := ARRAY[
        'inventory.transfer.create',
        'inventory.adjustment.create',
        'purchasing.po.create',
        'sales.order.create'
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

    -- 3. Locate Existing Role DEMO_INVENTORY_VIEWER
    SELECT id INTO v_role_id
    FROM "authorization".roles
    WHERE tenant_id = v_tenant_id AND role_code = v_role_code;

    IF v_role_id IS NULL THEN
        RAISE EXCEPTION 'ABORT: Role % does not exist in tenant DEMO-CLOUD.', v_role_code;
    END IF;

    -- Assert role is assigned ONLY to Tanzeel's tenant membership
    IF EXISTS (
        SELECT 1 FROM "authorization".membership_role_assignments
        WHERE tenant_id = v_tenant_id
          AND role_id = v_role_id
          AND membership_id <> v_membership_id
    ) THEN
        RAISE EXCEPTION 'SAFETY ABORT: Role % is assigned to other memberships. Must be isolated to Tanzeel only!', v_role_code;
    END IF;

    -- 4. Verify inventory.receipt.create and inventory.receipt.post Exist in authorization.permissions
    IF NOT EXISTS (SELECT 1 FROM "authorization".permissions WHERE permission_code = 'inventory.receipt.create') THEN
        RAISE EXCEPTION 'PRECONDITION FAILED: Permission inventory.receipt.create does not exist in authorization.permissions!';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM "authorization".permissions WHERE permission_code = 'inventory.receipt.post') THEN
        RAISE EXCEPTION 'PRECONDITION FAILED: Permission inventory.receipt.post does not exist in authorization.permissions!';
    END IF;

    -- 5. Idempotently Add inventory.receipt.create and inventory.receipt.post to Role
    INSERT INTO "authorization".role_permissions (
        tenant_id, role_id, permission_code, scope_type
    )
    VALUES 
        (v_tenant_id, v_role_id, 'inventory.receipt.create', 'company'),
        (v_tenant_id, v_role_id, 'inventory.receipt.post', 'company')
    ON CONFLICT (tenant_id, role_id, permission_code, scope_type) DO NOTHING;

    RAISE NOTICE '[PERMISSIONS ADDED] Added inventory.receipt.create and inventory.receipt.post to role %', v_role_code;

    -- 6. Safety Audit: Assert Role Holds Exactly 11 Permissions
    SELECT count(*) INTO v_perm_count
    FROM "authorization".role_permissions
    WHERE tenant_id = v_tenant_id AND role_id = v_role_id;

    IF v_perm_count <> 11 THEN
        RAISE EXCEPTION 'SAFETY AUDIT FAILED: Role % has % permissions (expected exactly 11)!', v_role_code, v_perm_count;
    END IF;

    IF EXISTS (
        SELECT 1 FROM "authorization".role_permissions
        WHERE tenant_id = v_tenant_id
          AND role_id = v_role_id
          AND permission_code NOT IN (
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
              'inventory.receipt.post'
          )
    ) THEN
        RAISE EXCEPTION 'SAFETY VIOLATION: Role % contains unexpected or forbidden permissions!', v_role_code;
    END IF;

    -- 7. Positive Verification: user_has_permission returns TRUE for all 11 allowed permissions
    FOREACH v_perm IN ARRAY c_allowed_perms
    LOOP
        IF NOT identity.user_has_permission(v_tenant_id, v_company_id, v_user_id, v_perm) THEN
            RAISE EXCEPTION 'VERIFICATION FAILED: user_has_permission returned FALSE for % on user %!', v_perm, v_email;
        END IF;
    END LOOP;
    RAISE NOTICE '[VERIFIED] All 11 allowed inventory permissions evaluate to TRUE for %', v_email;

    -- 8. Negative Verification: user_has_permission returns FALSE for transfer/adjustment/purchasing/sales
    FOREACH v_perm IN ARRAY c_forbidden_perms
    LOOP
        IF identity.user_has_permission(v_tenant_id, v_company_id, v_user_id, v_perm) THEN
            RAISE EXCEPTION 'SAFETY VIOLATION: user_has_permission unexpectedly returned TRUE for forbidden permission %!', v_perm;
        END IF;
    END LOOP;
    RAISE NOTICE '[VERIFIED] All forbidden permissions evaluate to FALSE (strict least privilege preserved)';

    -- 9. Safety Invariant: Confirm Zeelman context remains intact
    IF NOT EXISTS (
        SELECT 1 FROM identity.list_user_company_contexts(
            'https://securetoken.google.com/universal-erp-staging',
            'g5UVBoX1RFODIQ3suHZtN1DUiam2'
        )
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
    ) THEN
        RAISE EXCEPTION 'SAFETY INVARIANT VIOLATION: Demo account zeelman49@gmail.com context was disrupted!';
    END IF;

    RAISE NOTICE '[COMPLETE] Receipt creation & posting permissions successfully validated for %', v_email;
END $$;

COMMIT;
