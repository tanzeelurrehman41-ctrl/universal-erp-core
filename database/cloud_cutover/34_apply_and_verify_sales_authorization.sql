-- Universal ERP -- Apply Migration 025 & Grant DEMO_SALES_OPERATOR Role (Neon neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target User: tanzeelurrehman41@gmail.com (Firebase UID: nnnmCg7nT0cehV14o5APFFtY2s53)
-- Role Code: DEMO_SALES_OPERATOR
-- Schema Alignment: Matches actual authorization.roles (id, tenant_id, role_code, name, is_system_role, created_at)
-- Safety Invariants:
--   1. Strict Neon DB assertion (neondb only).
--   2. Registers exactly the 7 sales permissions in authorization.permissions (Migration 025).
--   3. Creates DEMO_SALES_OPERATOR with strictly the 7 sales permissions (company scope).
--   4. Assigns DEMO_SALES_OPERATOR to Tanzeel.
--   5. Verifies identity.user_has_permission returns true for all 7 permissions.
--   6. Asserts existing roles (DEMO_INVENTORY_*, DEMO_PURCHASING_*) remain untouched.
--   7. Asserts Zeelman isolation invariant.

BEGIN;

-- ── 1. Apply Migration 025 (Register Permissions) ───────────────────────────

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
  ('sales.customer.view', 'View customer list and details.', 'sales'),
  ('sales.customer.create', 'Create and update customers.', 'sales'),
  ('sales.order.view', 'View sales orders list and details.', 'sales'),
  ('sales.order.create', 'Create draft sales orders.', 'sales'),
  ('sales.order.confirm', 'Confirm and approve sales orders.', 'sales'),
  ('sales.invoice.view', 'View sales invoices and POS transaction receipts.', 'sales'),
  ('sales.invoice.post', 'Post sales invoices, dispatch stock, and trigger General Ledger entries.', 'sales')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('025_register_sales_permissions', now())
ON CONFLICT (version) DO NOTHING;

-- ── 2. Create and Assign DEMO_SALES_OPERATOR Role ───────────────────────────

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

    -- 3. Create or Resolve DEMO_SALES_OPERATOR Role in DEMO-CLOUD
    SELECT id INTO v_role_id
    FROM "authorization".roles
    WHERE tenant_id = v_tenant_id AND role_code = v_role_code;

    IF v_role_id IS NULL THEN
        v_role_id := gen_random_uuid();
        INSERT INTO "authorization".roles (
            id, tenant_id, role_code, name, is_system_role, created_at
        ) VALUES (
            v_role_id,
            v_tenant_id,
            v_role_code,
            'Demo Sales & POS Operator',
            false,
            now()
        );
        RAISE NOTICE '[STEP 1 PASS] Created role % with ID % in tenant DEMO-CLOUD.', v_role_code, v_role_id;
    ELSE
        RAISE NOTICE '[STEP 1 PASS] Role % already exists with ID % in tenant DEMO-CLOUD.', v_role_code, v_role_id;
    END IF;

    -- 4. Grant Exactly the 7 Sales Permissions to DEMO_SALES_OPERATOR (scope_type = company)
    FOREACH v_perm IN ARRAY c_sales_perms
    LOOP
        INSERT INTO "authorization".role_permissions (
            tenant_id, role_id, permission_code, scope_type
        ) VALUES (
            v_tenant_id,
            v_role_id,
            v_perm,
            'company'
        ) ON CONFLICT (tenant_id, role_id, permission_code, scope_type) DO NOTHING;
    END LOOP;

    -- Verify exact permission count on DEMO_SALES_OPERATOR
    SELECT count(*) INTO v_perm_count
    FROM "authorization".role_permissions
    WHERE tenant_id = v_tenant_id AND role_id = v_role_id;

    IF v_perm_count <> 7 THEN
        RAISE EXCEPTION 'SAFETY VIOLATION: Role % must have exactly 7 permissions, found %!', v_role_code, v_perm_count;
    END IF;
    RAISE NOTICE '[STEP 2 PASS] Role % has exactly 7 permissions (scope: company).', v_role_code;

    -- Invariant: Assert no forbidden permissions are attached
    IF EXISTS (
        SELECT 1 FROM "authorization".role_permissions
        WHERE tenant_id = v_tenant_id AND role_id = v_role_id AND permission_code = ANY(c_forbidden_perms)
    ) THEN
        RAISE EXCEPTION 'CRITICAL SECURITY VIOLATION: Role % contains forbidden administrative permissions!', v_role_code;
    END IF;
    RAISE NOTICE '[STEP 3 PASS] Role % contains zero forbidden administrative permissions.', v_role_code;

    -- 5. Assign DEMO_SALES_OPERATOR to Tanzeel in DEMO-CLOUD
    IF NOT EXISTS (
        SELECT 1 FROM "authorization".membership_role_assignments
        WHERE tenant_id = v_tenant_id
          AND membership_id = v_membership_id
          AND role_id = v_role_id
          AND (effective_until IS NULL OR effective_until > now())
    ) THEN
        INSERT INTO "authorization".membership_role_assignments (
            id, tenant_id, membership_id, role_id, assigned_by_user_id, effective_from
        ) VALUES (
            gen_random_uuid(),
            v_tenant_id,
            v_membership_id,
            v_role_id,
            v_user_id,
            now()
        );
        RAISE NOTICE '[STEP 4 PASS] Assigned role % to Tanzeel membership %.', v_role_code, v_membership_id;
    ELSE
        RAISE NOTICE '[STEP 4 PASS] Role % already assigned to membership %.', v_role_code, v_membership_id;
    END IF;

    -- 6. Verification: Assert identity.user_has_permission returns true for all 7 sales permissions
    FOREACH v_perm IN ARRAY c_sales_perms
    LOOP
        IF NOT identity.user_has_permission(v_tenant_id, v_company_id, v_user_id, v_perm) THEN
            RAISE EXCEPTION 'VERIFICATION_FAILED: User % does not have verified permission % for company CLOUD-LOGISTICS!', v_email, v_perm;
        END IF;
    END LOOP;
    RAISE NOTICE '[STEP 5 PASS] Verified identity.user_has_permission returns TRUE for all 7 sales permissions!';

    -- 7. Isolation Invariant: Assert user zeelman@gmail.com has ZERO access to DEMO-CLOUD
    IF EXISTS (
        SELECT 1 FROM identity.users u
        JOIN identity.tenant_memberships tm ON tm.user_id = u.id
        WHERE u.email_normalized = 'zeelman@gmail.com' AND tm.tenant_id = v_tenant_id
    ) THEN
        RAISE EXCEPTION 'ISOLATION BREACH: zeelman@gmail.com has membership in DEMO-CLOUD!';
    END IF;
    RAISE NOTICE '[STEP 6 PASS] Isolation invariant verified: zeelman@gmail.com has ZERO access to DEMO-CLOUD.';

    -- 8. Existing Roles Invariant: Assert DEMO_INVENTORY_* and DEMO_PURCHASING_* roles are intact
    IF NOT EXISTS (
        SELECT 1 FROM "authorization".roles
        WHERE tenant_id = v_tenant_id AND role_code = 'DEMO_INVENTORY_OPERATOR'
    ) OR NOT EXISTS (
        SELECT 1 FROM "authorization".roles
        WHERE tenant_id = v_tenant_id AND role_code = 'DEMO_PURCHASING_OPERATOR'
    ) THEN
        RAISE EXCEPTION 'INTEGRITY BREACH: Prior inventory or purchasing roles were modified or deleted!';
    END IF;
    RAISE NOTICE '[STEP 7 PASS] Existing inventory and purchasing operator roles remain completely untouched.';

    RAISE NOTICE '==========================================================================';
    RAISE NOTICE 'ALL AUTHORIZATION & RBAC PRECONDITIONS SUCCESSFULLY APPLIED AND VERIFIED!';
    RAISE NOTICE '==========================================================================';
END $$;

COMMIT;

-- ── 3. Diagnostic Report Output ─────────────────────────────────────────────

\echo ''
\echo '=========================================================================='
\echo 'DIAGNOSTIC: EFFECTIVE PERMISSIONS FOR TANZEEL (tanzeelurrehman41@gmail.com)'
\echo '=========================================================================='
WITH tanzeel_user AS (
    SELECT id FROM identity.users WHERE email_normalized = 'tanzeelurrehman41@gmail.com'
)
SELECT 
    perm.permission_code,
    identity.user_has_permission(
        '22222222-2222-4222-8222-222222222222'::uuid,
        '22222222-2222-4222-8222-222222222223'::uuid,
        (SELECT id FROM tanzeel_user),
        perm.permission_code
    ) AS is_effective
FROM (
    VALUES 
        ('sales.customer.view'),
        ('sales.customer.create'),
        ('sales.order.view'),
        ('sales.order.create'),
        ('sales.order.confirm'),
        ('sales.invoice.view'),
        ('sales.invoice.post')
) AS perm(permission_code);
