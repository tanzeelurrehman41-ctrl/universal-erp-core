-- Universal ERP -- Cloud Demonstration Account Provisioning
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Target User: tanzeelurrehman41@gmail.com (UID: nnnmCg7nT0cehV14o5APFFtY2s53)
-- Safety: Transactional, idempotent, least privilege, zero administrative roles

BEGIN;

DO $$
DECLARE
    v_issuer text := 'https://securetoken.google.com/universal-erp-staging';
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    
    -- Target account: tanzeelurrehman41@gmail.com
    v_uid text := 'nnnmCg7nT0cehV14o5APFFtY2s53';
    v_email text := 'tanzeelurrehman41@gmail.com';
    v_default_display_name text := 'Tanzeel admin';

    v_user_id uuid;
    v_existing_user_id uuid;
    v_existing_identity_sub text;
    v_ctx_count integer;
    v_zeelman_count integer;
    v_current_db text;
BEGIN
    -- 0. Database Safety Check
    SELECT current_database() INTO v_current_db;
    IF v_current_db <> 'neondb' THEN
        RAISE EXCEPTION 'SECURITY ABORT: Execution is only allowed on database "neondb". Current database: %', v_current_db;
    END IF;

    -- 1. Assert Tenant and Company exist and are active
    IF NOT EXISTS (
        SELECT 1 FROM platform.tenants 
        WHERE id = v_tenant_id 
          AND tenant_code = 'DEMO-CLOUD' 
          AND status = 'active'
    ) THEN
        RAISE EXCEPTION 'ABORT: Target tenant DEMO-CLOUD (ID %) does not exist or is not active.', v_tenant_id;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM organization.companies 
        WHERE id = v_company_id 
          AND tenant_id = v_tenant_id 
          AND company_code = 'CLOUD-LOGISTICS' 
          AND status = 'active'
    ) THEN
        RAISE EXCEPTION 'ABORT: Target company CLOUD-LOGISTICS (ID %) does not exist or is not active in tenant %.', 
            v_company_id, v_tenant_id;
    END IF;

    -- 2. Identify existing user record:
    -- 2a. Look up by normalized email
    SELECT id INTO v_user_id
    FROM identity.users
    WHERE email_normalized = v_email;

    -- 2b. If not found by email, check for existing user by display name (e.g. 'Tanzeel admin')
    IF v_user_id IS NULL THEN
        SELECT id INTO v_user_id
        FROM identity.users
        WHERE display_name = v_default_display_name
           OR display_name ILIKE 'Tanzeel%'
        ORDER BY created_at ASC
        LIMIT 1;

        IF v_user_id IS NOT NULL THEN
            RAISE NOTICE '[REUSE] Found existing ERP user by display name (ID: %). Linking email %.', v_user_id, v_email;
            UPDATE identity.users
            SET email_normalized = v_email,
                status = 'active',
                updated_at = now()
            WHERE id = v_user_id;
        END IF;
    END IF;

    -- 2c. Conflict Checks:
    -- Check if the Firebase UID (issuer, subject) is already linked to a different user
    SELECT user_id INTO v_existing_user_id
    FROM identity.external_identities
    WHERE issuer = v_issuer AND subject = v_uid;

    IF v_existing_user_id IS NOT NULL AND v_user_id IS NOT NULL AND v_existing_user_id <> v_user_id THEN
        RAISE EXCEPTION 'SAFETY ABORT: Firebase identity % is already linked to user % but target user is %!',
            v_uid, v_existing_user_id, v_user_id;
    END IF;

    -- Check if this user already has a conflicting external identity from this issuer
    IF v_user_id IS NOT NULL THEN
        SELECT subject INTO v_existing_identity_sub
        FROM identity.external_identities
        WHERE issuer = v_issuer AND user_id = v_user_id;

        IF v_existing_identity_sub IS NOT NULL AND v_existing_identity_sub <> v_uid THEN
            RAISE EXCEPTION 'SAFETY ABORT: User % already has a different external identity subject % for issuer %!',
                v_user_id, v_existing_identity_sub, v_issuer;
        END IF;
    END IF;

    -- 3. Create or reuse identity.users record (least privilege, active, standard user)
    IF v_user_id IS NULL THEN
        IF v_existing_user_id IS NOT NULL THEN
            v_user_id := v_existing_user_id;
            UPDATE identity.users
            SET email_normalized = coalesce(email_normalized, v_email),
                status = 'active',
                updated_at = now()
            WHERE id = v_user_id;
        ELSE
            v_user_id := gen_random_uuid();
            INSERT INTO identity.users (
                id,
                email_normalized,
                display_name,
                status,
                preferred_language_code,
                mfa_required,
                created_at,
                updated_at
            )
            VALUES (
                v_user_id,
                v_email,
                v_default_display_name,
                'active',
                'en',
                false,
                now(),
                now()
            );
            RAISE NOTICE '[CREATED] Created new identity.users record (ID: %) for %', v_user_id, v_email;
        END IF;
    ELSE
        -- Ensure existing user is active
        UPDATE identity.users
        SET status = 'active', updated_at = now()
        WHERE id = v_user_id AND status <> 'active';
        RAISE NOTICE '[REUSED] Reusing active identity.users record (ID: %) for %', v_user_id, v_email;
    END IF;

    -- 4. Link external identity (issuer, subject) -> user_id
    INSERT INTO identity.external_identities (
        issuer,
        subject,
        user_id,
        linked_at
    )
    VALUES (
        v_issuer,
        v_uid,
        v_user_id,
        now()
    )
    ON CONFLICT (issuer, subject) DO UPDATE
        SET user_id = EXCLUDED.user_id
        WHERE identity.external_identities.user_id = EXCLUDED.user_id;

    -- 5. Assign active tenant membership (least privilege: no admin role assignments)
    INSERT INTO identity.tenant_memberships (
        id,
        tenant_id,
        user_id,
        status,
        joined_at,
        created_at
    )
    VALUES (
        gen_random_uuid(),
        v_tenant_id,
        v_user_id,
        'active',
        now(),
        now()
    )
    ON CONFLICT (tenant_id, user_id) DO UPDATE
        SET status = 'active', joined_at = coalesce(identity.tenant_memberships.joined_at, now());

    -- 6. Assign active company membership (least privilege: no admin role assignments)
    INSERT INTO identity.company_memberships (
        id,
        tenant_id,
        company_id,
        user_id,
        status,
        created_at
    )
    VALUES (
        gen_random_uuid(),
        v_tenant_id,
        v_company_id,
        v_user_id,
        'active',
        now()
    )
    ON CONFLICT (tenant_id, company_id, user_id) DO UPDATE
        SET status = 'active';

    -- 7. Verification: Confirm list_user_company_contexts resolves DEMO-CLOUD / CLOUD-LOGISTICS
    SELECT count(*) INTO v_ctx_count
    FROM identity.list_user_company_contexts(v_issuer, v_uid)
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id;

    IF v_ctx_count < 1 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: list_user_company_contexts returned 0 contexts for UID % in tenant %!', 
            v_uid, v_tenant_id;
    END IF;

    -- 8. Invariant Verification: Confirm zeelman49@gmail.com context remains intact
    SELECT count(*) INTO v_zeelman_count
    FROM identity.list_user_company_contexts(v_issuer, 'g5UVBoX1RFODIQ3suHZtN1DUiam2')
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id;

    IF v_zeelman_count < 1 THEN
        RAISE EXCEPTION 'SAFETY INVARIANT VIOLATION: Existing user zeelman49@gmail.com context was disrupted!';
    END IF;

    RAISE NOTICE '[PROVISIONED] Successfully linked % (UID: %) to DEMO-CLOUD / Cloud Global Logistics Ltd (% contexts verified, existing demo account verified)',
        v_email, v_uid, v_ctx_count;
END $$;

COMMIT;
