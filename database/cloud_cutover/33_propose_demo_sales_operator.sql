-- Universal ERP -- Propose Separate Least-Privilege Sales Operator Role (Staging Demo)
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target User: tanzeelurrehman41@gmail.com (Firebase UID: nnnmCg7nT0cehV14o5APFFtY2s53)
-- New Role Code: DEMO_SALES_OPERATOR
-- Granted Permissions: exactly 7 sales permissions (scope_type: 'company')
-- Safety Invariants:
--   1. Strict Neon DB assertion.
--   2. Preserves DEMO_INVENTORY_*, DEMO_PURCHASING_* untouched.
--   3. Scoped strictly to company CLOUD-LOGISTICS and user Tanzeel.
--   4. Strictly zero finance GL manual posting or identity administrative rights.
--   5. Verifies Zeelman isolation invariant.

BEGIN;

DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_email text := 'tanzeelurrehman41@gmail.com';
    v_role_code text := 'DEMO_SALES_OPERATOR';

    v_current_db text;
    v_user_id uuid;
    v_membership_id uuid;
    v_role_id uuid;
    v_perm text;
    v_perm_count integer;

    c_sales_perms text[] := ARRAY[
        'sales.customer.view',
        'sales.customer.create',
        'sales.order.view',
        'sales.order.create',
        'sales.order.confirm',
        'sales.invoice.view',
        'sales.invoice.post'
    ];

    c_forbidden_perms text[] := ARRAY[
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

    -- Assert Firebase UID mapping strictly matches target user ID
    IF NOT EXISTS (
        SELECT 1 FROM identity.external_identities
        WHERE issuer = 'https://securetoken.google.com/universal-erp-staging'
          AND subject = 'nnnmCg7nT0cehV14o5APFFtY2s53'
          AND user_id = v_user_id
    ) THEN
        RAISE EXCEPTION 'SAFETY ABORT: Firebase UID nnnmCg7nT0cehV14o5APFFtY2s53 is not linked to target user ID % (%)!', v_user_id, v_email;
    END IF;

    SELECT id INTO v_membership_id
    FROM identity.tenant_memberships
    WHERE tenant_id = v_tenant_id AND user_id = v_user_id AND status = 'active';

    IF v_membership_id IS NULL THEN
        RAISE EXCEPTION 'ABORT: Active tenant membership not found for user % in tenant DEMO-CLOUD.', v_email;
    END IF;

    -- 3. Assert all 7 required sales permissions exist in authorization.permissions (Migration 025 must be applied)
    FOREACH v_perm IN ARRAY c_sales_perms
    LOOP
        IF NOT EXISTS (
            SELECT 1 FROM "authorization".permissions
            WHERE permission_code = v_perm
        ) THEN
            RAISE EXCEPTION 'PREREQUISITE_FAILED: Permission % does not exist in authorization.permissions. Apply Migration 025 first.', v_perm;
        END IF;
    END LOOP;

    -- 4. Create or Resolve DEMO_SALES_OPERATOR Role in DEMO-CLOUD
    SELECT id INTO v_role_id
    FROM "authorization".roles
    WHERE tenant_id = v_tenant_id AND role_code = v_role_code;

    IF v_role_id IS NULL THEN
        v_role_id := gen_random_uuid();
        INSERT INTO "authorization".roles (
            id, tenant_id, role_code, name, description, is_system_role
        ) VALUES (
            v_role_id,
            v_tenant_id,
            v_role_code,
            'Demo Sales & POS Operator',
            'Least-privilege role for sales order management and POS transaction posting in CLOUD-LOGISTICS.',
            false
        );
        RAISE NOTICE 'Created new role % with ID % in tenant DEMO-CLOUD.', v_role_code, v_role_id;
    ELSE
        RAISE NOTICE 'Role % already exists with ID % in tenant DEMO-CLOUD.', v_role_code, v_role_id;
    END IF;

    -- 5. Grant Exactly the 7 Sales Permissions to DEMO_SALES_OPERATOR (scope_type = company)
    FOREACH v_perm IN ARRAY c_sales_perms
    LOOP
        INSERT INTO "authorization".role_permissions (
            tenant_id, role_id, permission_code, scope_type
        ) VALUES (
            v_tenant_id,
            v_role_id,
            v_perm,
            'company'
        ) ON CONFLICT (tenant_id, role_id, permission_code) DO NOTHING;
    END LOOP;

    -- Verify exact permission count on DEMO_SALES_OPERATOR
    SELECT count(*) INTO v_perm_count
    FROM "authorization".role_permissions
    WHERE tenant_id = v_tenant_id AND role_id = v_role_id;

    IF v_perm_count <> 7 THEN
        RAISE EXCEPTION 'SAFETY VIOLATION: Role % must have exactly 7 permissions, found %!', v_role_code, v_perm_count;
    END IF;

    -- Invariant: Assert no forbidden permissions are attached
    IF EXISTS (
        SELECT 1 FROM "authorization".role_permissions
        WHERE tenant_id = v_tenant_id AND role_id = v_role_id AND permission_code = ANY(c_forbidden_perms)
    ) THEN
        RAISE EXCEPTION 'CRITICAL SECURITY VIOLATION: Role % contains forbidden administrative permissions!', v_role_code;
    END IF;

    -- 6. Assign DEMO_SALES_OPERATOR to Tanzeel in DEMO-CLOUD
    INSERT INTO "authorization".membership_role_assignments (
        id, tenant_id, membership_id, role_id, effective_from
    ) VALUES (
        gen_random_uuid(),
        v_tenant_id,
        v_membership_id,
        v_role_id,
        now()
    ) ON CONFLICT (tenant_id, membership_id, role_id) DO NOTHING;

    -- 7. Verification: Assert identity.user_has_permission returns true for all 7 sales permissions
    FOREACH v_perm IN ARRAY c_sales_perms
    LOOP
        IF NOT identity.user_has_permission(v_tenant_id, v_company_id, v_user_id, v_perm) THEN
            RAISE EXCEPTION 'VERIFICATION_FAILED: User % does not have verified permission % for company CLOUD-LOGISTICS!', v_email, v_perm;
        END IF;
    END LOOP;

    -- 8. Isolation Invariant: Assert user zeelman@gmail.com has ZERO access to DEMO-CLOUD
    IF EXISTS (
        SELECT 1 FROM identity.users u
        JOIN identity.tenant_memberships tm ON tm.user_id = u.id
        WHERE u.email_normalized = 'zeelman@gmail.com' AND tm.tenant_id = v_tenant_id
    ) THEN
        RAISE EXCEPTION 'ISOLATION BREACH: zeelman@gmail.com has membership in DEMO-CLOUD!';
    END IF;

    RAISE NOTICE '==========================================================================';
    RAISE NOTICE 'SUCCESS: Role DEMO_SALES_OPERATOR provisioned and granted to Tanzeel!';
    RAISE NOTICE 'User: % | Tenant: DEMO-CLOUD | Company: CLOUD-LOGISTICS', v_email;
    RAISE NOTICE 'Permissions verified (7 total): %', c_sales_perms;
    RAISE NOTICE '==========================================================================';
END $$;

COMMIT;
