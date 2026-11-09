-- Universal ERP -- Assign Minimal Read-Only Inventory Role (Staging Demo)
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target User: tanzeelurrehman41@gmail.com
-- Role: DEMO_INVENTORY_VIEWER
-- Granted Permissions: exactly 5 read-only inventory view permissions
-- Safety: Transactional, idempotent, strictly least privilege, zero write/admin access

BEGIN;

DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_email text := 'tanzeelurrehman41@gmail.com';
    v_role_code text := 'DEMO_INVENTORY_VIEWER';
    v_role_name text := 'Demo Inventory Viewer';

    v_current_db text;
    v_user_id uuid;
    v_membership_id uuid;
    v_role_id uuid;
    v_perm text;
    v_perm_count integer;

    -- Exactly five read-only inventory permissions
    c_required_perms text[] := ARRAY[
        'inventory.product.view',
        'inventory.uom.view',
        'inventory.variant.view',
        'inventory.warehouse.view',
        'inventory.availability.view'
    ];

    -- Representative write / admin permissions to assert denial
    c_forbidden_perms text[] := ARRAY[
        'inventory.product.create',
        'inventory.uom.create',
        'inventory.variant.create',
        'inventory.warehouse.create',
        'inventory.receipt.create',
        'inventory.receipt.post',
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

    -- Assert active company membership exists
    IF NOT EXISTS (
        SELECT 1 FROM identity.company_memberships
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id AND user_id = v_user_id AND status = 'active'
    ) THEN
        RAISE EXCEPTION 'ABORT: Active company membership not found for user % in company CLOUD-LOGISTICS.', v_email;
    END IF;

    -- 3. Verify Required Permissions Exist in authorization.permissions (Do not invent new codes)
    FOREACH v_perm IN ARRAY c_required_perms
    LOOP
        IF NOT EXISTS (SELECT 1 FROM "authorization".permissions WHERE permission_code = v_perm) THEN
            RAISE EXCEPTION 'PRECONDITION FAILED: Permission % does not exist in authorization.permissions!', v_perm;
        END IF;
    END LOOP;

    -- 4. Create or Reuse Tenant-Scoped Role
    SELECT id INTO v_role_id
    FROM "authorization".roles
    WHERE tenant_id = v_tenant_id AND role_code = v_role_code;

    IF v_role_id IS NULL THEN
        v_role_id := gen_random_uuid();
        INSERT INTO "authorization".roles (
            id, tenant_id, role_code, name, is_system_role, created_at
        )
        VALUES (
            v_role_id, v_tenant_id, v_role_code, v_role_name, false, now()
        );
        RAISE NOTICE '[ROLE CREATED] Created tenant role % (ID: %)', v_role_code, v_role_id;
    ELSE
        RAISE NOTICE '[ROLE REUSED] Reusing existing tenant role % (ID: %)', v_role_code, v_role_id;
    END IF;

    -- 5. Grant Exactly the Five Read-Only Permissions to Role
    FOREACH v_perm IN ARRAY c_required_perms
    LOOP
        INSERT INTO "authorization".role_permissions (
            tenant_id, role_id, permission_code, scope_type
        )
        VALUES (
            v_tenant_id, v_role_id, v_perm, 'company'
        )
        ON CONFLICT (tenant_id, role_id, permission_code, scope_type) DO NOTHING;
    END LOOP;

    -- 5b. Safety Audit: Assert role has NO extraneous or write permissions
    SELECT count(*) INTO v_perm_count
    FROM "authorization".role_permissions
    WHERE tenant_id = v_tenant_id AND role_id = v_role_id;

    IF v_perm_count <> 5 THEN
        RAISE EXCEPTION 'SAFETY AUDIT FAILED: Role % has % permissions (expected exactly 5)!', v_role_code, v_perm_count;
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
              'inventory.availability.view'
          )
    ) THEN
        RAISE EXCEPTION 'SAFETY VIOLATION: Role % contains unexpected or non-inventory permissions!', v_role_code;
    END IF;

    -- 6. Assign Role to Tanzeel's Tenant Membership (Idempotent, Single User Only)
    IF NOT EXISTS (
        SELECT 1 FROM "authorization".membership_role_assignments
        WHERE tenant_id = v_tenant_id
          AND membership_id = v_membership_id
          AND role_id = v_role_id
          AND effective_from <= now()
          AND (effective_until IS NULL OR effective_until > now())
    ) THEN
        INSERT INTO "authorization".membership_role_assignments (
            id,
            tenant_id,
            membership_id,
            role_id,
            assigned_by_user_id,
            effective_from,
            created_at
        )
        VALUES (
            gen_random_uuid(),
            v_tenant_id,
            v_membership_id,
            v_role_id,
            v_user_id,
            now(),
            now()
        );
        RAISE NOTICE '[ASSIGNED] Assigned % to tenant membership % (User: %)', v_role_code, v_membership_id, v_email;
    ELSE
        RAISE NOTICE '[ALREADY ASSIGNED] Role % is already active on membership %', v_role_code, v_membership_id;
    END IF;

    -- 7. Verification: Assert identity.user_has_permission returns TRUE for all 5 view permissions
    FOREACH v_perm IN ARRAY c_required_perms
    LOOP
        IF NOT identity.user_has_permission(v_tenant_id, v_company_id, v_user_id, v_perm) THEN
            RAISE EXCEPTION 'VERIFICATION FAILED: user_has_permission returned FALSE for % on user %!', v_perm, v_email;
        END IF;
    END LOOP;
    RAISE NOTICE '[VERIFIED] All 5 read-only inventory permissions evaluate to TRUE for %', v_email;

    -- 8. Negative Verification: Assert identity.user_has_permission returns FALSE for write/admin permissions
    FOREACH v_perm IN ARRAY c_forbidden_perms
    LOOP
        IF identity.user_has_permission(v_tenant_id, v_company_id, v_user_id, v_perm) THEN
            RAISE EXCEPTION 'SAFETY VIOLATION: user_has_permission unexpectedly returned TRUE for write permission %!', v_perm;
        END IF;
    END LOOP;
    RAISE NOTICE '[VERIFIED] Representative write/admin permissions evaluate to FALSE (strict least privilege preserved)';

    -- 9. Safety Invariant: Confirm zeelman49@gmail.com context remains intact and undisturbed
    IF NOT EXISTS (
        SELECT 1 FROM identity.list_user_company_contexts(
            'https://securetoken.google.com/universal-erp-staging',
            'g5UVBoX1RFODIQ3suHZtN1DUiam2'
        )
        WHERE tenant_id = v_tenant_id AND company_id = v_company_id
    ) THEN
        RAISE EXCEPTION 'SAFETY INVARIANT VIOLATION: Demo account zeelman49@gmail.com context was disrupted!';
    END IF;

    RAISE NOTICE '[COMPLETE] Staging demo role assignment successfully validated for %', v_email;
END $$;

COMMIT;
