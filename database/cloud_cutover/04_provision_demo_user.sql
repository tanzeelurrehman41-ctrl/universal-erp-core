-- Universal ERP -- Cloud Demonstration Account Provisioning
-- Target Database: Neon PostgreSQL (neondb)
-- Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)

BEGIN;

DO $$
DECLARE
    v_issuer text := 'https://securetoken.google.com/universal-erp-staging';
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    
    -- Candidate users from staging Firebase project
    -- Primary: tanzeelurrehman41@gmail.com (UID: nnnmCg7nT0cehV14o5APFFtY2s53)
    -- Secondary: zeelman49@gmail.com (UID: g5UVBoX1RFODIQ3suHZtN1DUiam2)
    -- Additional: jbstationers.pk@gmail.com (UID: edczKog2a2OzdeMXV3k67pUfVQw1)
    
    type_user_record RECORD;
    v_user_id uuid;
    v_existing_user_id uuid;
    v_ctx_count integer;
    
    -- Array of accounts to provision
    v_users jsonb := '[
        {"uid": "nnnmCg7nT0cehV14o5APFFtY2s53", "email": "tanzeelurrehman41@gmail.com", "name": "Tanzeel Rehman"},
        {"uid": "g5UVBoX1RFODIQ3suHZtN1DUiam2", "email": "zeelman49@gmail.com", "name": "Zeelman Demo"},
        {"uid": "edczKog2a2OzdeMXV3k67pUfVQw1", "email": "jbstationers.pk@gmail.com", "name": "Jb Stationers"}
    ]'::jsonb;
    
    v_item jsonb;
    v_uid text;
    v_email text;
    v_name text;
BEGIN
    -- 1. Assert Tenant and Company exist and are active
    IF NOT EXISTS (SELECT 1 FROM platform.tenants WHERE id = v_tenant_id AND status = 'active') THEN
        RAISE EXCEPTION 'ABORT: Target tenant DEMO-CLOUD (ID %) does not exist or is not active.', v_tenant_id;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM organization.companies WHERE id = v_company_id AND tenant_id = v_tenant_id AND status = 'active') THEN
        RAISE EXCEPTION 'ABORT: Target company CLOUD-LOGISTICS (ID %) does not exist or is not active.', v_company_id;
    END IF;

    -- 2. Process each staging account
    FOR v_item IN SELECT * FROM jsonb_array_elements(v_users)
    LOOP
        v_uid := v_item->>'uid';
        v_email := lower(trim(v_item->>'email'));
        v_name := coalesce(nullif(trim(v_item->>'name'), ''), 'Universal ERP User');

        -- Check if identity is already linked
        SELECT user_id INTO v_existing_user_id
        FROM identity.external_identities
        WHERE issuer = v_issuer AND subject = v_uid;

        -- Find or create identity.users record
        SELECT id INTO v_user_id
        FROM identity.users
        WHERE email_normalized = v_email;

        IF v_existing_user_id IS NOT NULL AND v_user_id IS NOT NULL AND v_existing_user_id <> v_user_id THEN
            RAISE EXCEPTION 'SAFETY ABORT: Firebase identity % is already linked to user % but email % belongs to user %!',
                v_uid, v_existing_user_id, v_email, v_user_id;
        END IF;

        IF v_user_id IS NULL THEN
            IF v_existing_user_id IS NOT NULL THEN
                v_user_id := v_existing_user_id;
            ELSE
                v_user_id := gen_random_uuid();
                INSERT INTO identity.users (
                    id, email_normalized, display_name, status,
                    preferred_language_code, mfa_required, created_at, updated_at
                )
                VALUES (
                    v_user_id, v_email, v_name, 'active',
                    'en', false, now(), now()
                );
            END IF;
        ELSE
            -- Ensure user is active
            UPDATE identity.users
            SET status = 'active', updated_at = now()
            WHERE id = v_user_id AND status <> 'active';
        END IF;

        -- Link external identity
        INSERT INTO identity.external_identities (issuer, subject, user_id, linked_at)
        VALUES (v_issuer, v_uid, v_user_id, now())
        ON CONFLICT (issuer, subject) DO UPDATE
            SET user_id = EXCLUDED.user_id
            WHERE identity.external_identities.user_id = EXCLUDED.user_id;

        -- Assign active tenant membership
        INSERT INTO identity.tenant_memberships (
            id, tenant_id, user_id, status, joined_at, created_at
        )
        VALUES (
            gen_random_uuid(), v_tenant_id, v_user_id, 'active', now(), now()
        )
        ON CONFLICT (tenant_id, user_id) DO UPDATE
            SET status = 'active', joined_at = coalesce(identity.tenant_memberships.joined_at, now());

        -- Assign active company membership
        INSERT INTO identity.company_memberships (
            id, tenant_id, company_id, user_id, status, created_at
        )
        VALUES (
            gen_random_uuid(), v_tenant_id, v_company_id, v_user_id, 'active', now()
        )
        ON CONFLICT (tenant_id, company_id, user_id) DO UPDATE
            SET status = 'active';

        -- Verify context lookup succeeds for this identity
        SELECT count(*) INTO v_ctx_count
        FROM identity.list_user_company_contexts(v_issuer, v_uid);

        IF v_ctx_count < 1 THEN
            RAISE EXCEPTION 'VERIFICATION FAILED: list_user_company_contexts returned 0 contexts for UID %!', v_uid;
        END IF;

        RAISE NOTICE '[PROVISIONED] User % (UID: %) successfully linked to DEMO-CLOUD / Cloud Global Logistics Ltd (% contexts)',
            v_email, v_uid, v_ctx_count;
    END LOOP;
END $$;

COMMIT;
