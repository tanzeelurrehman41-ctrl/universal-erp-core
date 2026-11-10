-- Universal ERP -- Propose Separate Least-Privilege Purchasing Operator Role (Staging Demo)
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target User: tanzeelurrehman41@gmail.com
-- New Role Code: DEMO_PURCHASING_OPERATOR
-- Granted Permissions: exactly 7 purchasing permissions (scope_type: 'company')
-- Safety Invariants:
--   1. Strict Neon DB assertion.
--   2. Preserves DEMO_INVENTORY_VIEWER and DEMO_INVENTORY_OPERATOR completely untouched.
--   3. Scoped strictly to company CLOUD-LOGISTICS and user Tanzeel.
--   4. Strictly zero sales, finance GL posting, or administrative rights.
--   5. Verifies Zeelman isolation invariant.

BEGIN;

DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_email text := 'tanzeelurrehman41@gmail.com';
    v_role_code text := 'DEMO_PURCHASING_OPERATOR';

    v_current_db text;
    v_user_id uuid;
    v_membership_id uuid;
    v_role_id uuid;
    v_perm text;
    v_perm_count integer;

    c_purchasing_perms text[] := ARRAY[
        'purchasing.supplier.view',
        'purchasing.supplier.create',
        'purchasing.po.view',
        'purchasing.po.create',
        'purchasing.po.confirm',
        'purchasing.grn.view',
        'purchasing.grn.post'
    ];

    c_forbidden_perms text[] := ARRAY[
        'sales.order.create',
        'sales.order.confirm',
        'sales.invoice.post',
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

    -- 3. Invariant: Assert DEMO_INVENTORY_VIEWER and DEMO_INVENTORY_OPERATOR have ZERO purchasing permissions
    IF EXISTS (
        SELECT 1 FROM "authorization".role_permissions rp
        JOIN "authorization".roles r ON r.id = rp.role_id AND r.tenant_id = rp.tenant_id
        WHERE r.tenant_id = v_tenant_id
          AND r.role_code IN ('DEMO_INVENTORY_VIEWER', 'DEMO_INVENTORY_OPERATOR')
          AND rp.permission_code LIKE 'purchasing%'
    ) THEN
        RAISE EXCEPTION 'SAFETY VIOLATION: Existing inventory roles contain purchasing permissions! Isolation violated.';
    END IF;

    -- 4. Verify all 7 purchasing permissions exist in authorization.permissions (Migration 024 prerequisite)
    FOREACH v_perm IN ARRAY c_purchasing_perms
    LOOP
        IF NOT EXISTS (SELECT 1 FROM "authorization".permissions WHERE permission_code = v_perm) THEN
            RAISE EXCEPTION 'PRECONDITION FAILED: Permission % does not exist in authorization.permissions! Apply Migration 024 first.', v_perm;
        END IF;
    END LOOP;

    -- 5. Idempotently Create or Locate DEMO_PURCHASING_OPERATOR Role
    SELECT id INTO v_role_id
    FROM "authorization".roles
    WHERE tenant_id = v_tenant_id AND role_code = v_role_code;

    IF v_role_id IS NULL THEN
        v_role_id := gen_random_uuid();
        INSERT INTO "authorization".roles (
            id, tenant_id, role_code, name, is_system_role, created_at
        )
        VALUES (
            v_role_id,
            v_tenant_id,
            v_role_code,
            'Demo Purchasing Operator',
            false,
            now()
        );
        RAISE NOTICE '[ROLE CREATED] Created new role % with ID %', v_role_code, v_role_id;
    ELSE
        RAISE NOTICE '[ROLE EXISTS] Located existing role % with ID %', v_role_code, v_role_id;
    END IF;

    -- 6. Assign DEMO_PURCHASING_OPERATOR strictly to Tanzeel's tenant membership
    IF NOT EXISTS (
        SELECT 1 FROM "authorization".membership_role_assignments
        WHERE tenant_id = v_tenant_id
          AND membership_id = v_membership_id
          AND role_id = v_role_id
          AND effective_from <= now()
          AND (effective_until IS NULL OR effective_until > now())
    ) THEN
        INSERT INTO "authorization".membership_role_assignments (
            id, tenant_id, membership_id, role_id, assigned_by_user_id, effective_from
        )
        VALUES (
            gen_random_uuid(),
            v_tenant_id,
            v_membership_id,
            v_role_id,
            v_user_id,
            now()
        );
        RAISE NOTICE '[ROLE ASSIGNED] Assigned % to membership %', v_role_code, v_membership_id;
    ELSE
        RAISE NOTICE '[ROLE ALREADY ASSIGNED] % already assigned to membership %', v_role_code, v_membership_id;
    END IF;

    -- Assert role is assigned ONLY to Tanzeel
    IF EXISTS (
        SELECT 1 FROM "authorization".membership_role_assignments
        WHERE tenant_id = v_tenant_id
          AND role_id = v_role_id
          AND membership_id <> v_membership_id
    ) THEN
        RAISE EXCEPTION 'SAFETY ABORT: Role % is assigned to unexpected memberships!', v_role_code;
    END IF;

    -- 7. Grant the 7 purchasing permissions to DEMO_PURCHASING_OPERATOR
    FOREACH v_perm IN ARRAY c_purchasing_perms
    LOOP
        INSERT INTO "authorization".role_permissions (
            tenant_id, role_id, permission_code, scope_type
        )
        VALUES (
            v_tenant_id, v_role_id, v_perm, 'company'
        )
        ON CONFLICT (tenant_id, role_id, permission_code, scope_type) DO NOTHING;
    END LOOP;

    RAISE NOTICE '[PERMISSIONS GRANTED] Assigned all 7 purchasing permissions to role %', v_role_code;

    -- 8. Safety Audit: Assert DEMO_PURCHASING_OPERATOR holds EXACTLY 7 permissions
    SELECT count(*) INTO v_perm_count
    FROM "authorization".role_permissions
    WHERE tenant_id = v_tenant_id AND role_id = v_role_id;

    IF v_perm_count <> 7 THEN
        RAISE EXCEPTION 'SAFETY AUDIT FAILED: Role % has % permissions (expected exactly 7)!', v_role_code, v_perm_count;
    END IF;

    -- 9. Positive Verification: user_has_permission returns TRUE for all 7 purchasing permissions
    FOREACH v_perm IN ARRAY c_purchasing_perms
    LOOP
        IF NOT identity.user_has_permission(v_tenant_id, v_company_id, v_user_id, v_perm) THEN
            RAISE EXCEPTION 'VERIFICATION FAILED: user_has_permission returned FALSE for % on user %!', v_perm, v_email;
        END IF;
    END LOOP;
    RAISE NOTICE '[VERIFIED] All 7 purchasing permissions evaluate to TRUE for %', v_email;

    -- 10. Negative Verification: user_has_permission returns FALSE for non-purchasing operations
    FOREACH v_perm IN ARRAY c_forbidden_perms
    LOOP
        IF identity.user_has_permission(v_tenant_id, v_company_id, v_user_id, v_perm) THEN
            RAISE EXCEPTION 'SAFETY VIOLATION: user_has_permission unexpectedly returned TRUE for forbidden permission %!', v_perm;
        END IF;
    END LOOP;
    RAISE NOTICE '[VERIFIED] All forbidden permissions evaluate to FALSE (strict boundary preserved)';

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

    RAISE NOTICE '[SUCCESS] Role DEMO_PURCHASING_OPERATOR successfully defined, granted, and audited for %', v_email;
END $$;

COMMIT;
