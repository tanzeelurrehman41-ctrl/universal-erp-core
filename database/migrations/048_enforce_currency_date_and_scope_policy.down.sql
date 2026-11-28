-- Rollback Migration 048: Enforce currency effective-date floor and company-scope permission policy
--
-- WARNING (POLICY DOWNSCOPE):
-- Rolling back Migration 048 restores the Migration 047 definition of
-- identity.update_company_currency_settings.
-- Doing so REINTRODUCES the following policy gaps fixed by Migration 048:
--   1. Past effective dates (< CURRENT_DATE) are accepted again.
--   2. The RBAC check accepts both scope_type = 'company' AND scope_type = 'tenant',
--      allowing tenant-only role holders to modify company currency settings.

CREATE OR REPLACE FUNCTION identity.update_company_currency_settings(
    p_tenant_id uuid,
    p_company_id uuid,
    p_actor_user_id uuid,
    p_functional_currency char(3),
    p_presentation_currency char(3),
    p_effective_date date,
    p_confirm_future_transactions boolean,
    p_change_reason text DEFAULT NULL
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path TO 'pg_catalog', 'organization', 'finance', 'audit', 'identity'
AS $$
DECLARE
    v_ctx_tenant uuid := NULLIF(pg_catalog.current_setting('app.tenant_id', true), '')::uuid;
    v_ctx_company uuid := NULLIF(pg_catalog.current_setting('app.company_id', true), '')::uuid;
    v_prev_func char(3);
    v_prev_pres char(3);
    v_prev_date date;
    v_new_func char(3) := UPPER(TRIM(p_functional_currency))::char(3);
    v_new_pres char(3) := UPPER(TRIM(p_presentation_currency))::char(3);
    v_has_posted boolean := false;
    v_reason text := NULLIF(TRIM(p_change_reason), '');
BEGIN
    IF v_ctx_tenant IS NULL OR v_ctx_company IS NULL
       OR v_ctx_tenant IS DISTINCT FROM p_tenant_id
       OR v_ctx_company IS DISTINCT FROM p_company_id THEN
        RAISE EXCEPTION 'UNAUTHORIZED_TENANT_COMPANY_SCOPE'
            USING ERRCODE = '42501';
    END IF;

    IF p_actor_user_id IS NULL OR NOT EXISTS (
        SELECT 1
        FROM identity.users u
        JOIN identity.tenant_memberships tm
          ON tm.tenant_id = v_ctx_tenant
         AND tm.user_id = u.id
         AND tm.status = 'active'
        JOIN identity.company_memberships cm
          ON cm.tenant_id = v_ctx_tenant
         AND cm.company_id = v_ctx_company
         AND cm.user_id = u.id
         AND cm.status = 'active'
        WHERE u.id = p_actor_user_id
          AND u.status = 'active'
    ) THEN
        RAISE EXCEPTION 'UNAUTHORIZED_ACTOR_MEMBERSHIP'
            USING ERRCODE = '42501';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM identity.company_memberships AS cm
        JOIN identity.tenant_memberships AS tm
          ON tm.tenant_id = cm.tenant_id
         AND tm.user_id = cm.user_id
        JOIN "authorization".membership_role_assignments AS assignment
          ON assignment.tenant_id = tm.tenant_id
         AND assignment.membership_id = tm.id
        JOIN "authorization".role_permissions AS permission
          ON permission.tenant_id = assignment.tenant_id
         AND permission.role_id = assignment.role_id
        WHERE cm.tenant_id = v_ctx_tenant
          AND cm.company_id = v_ctx_company
          AND cm.user_id = p_actor_user_id
          AND cm.status = 'active'
          AND tm.status = 'active'
          AND assignment.effective_from <= now()
          AND (assignment.effective_until IS NULL OR assignment.effective_until > now())
          AND permission.permission_code = 'admin.currency_settings.manage'
          AND permission.scope_type IN ('tenant', 'company')
    ) THEN
        RAISE EXCEPTION 'MISSING_PERMISSION_ADMIN_CURRENCY_SETTINGS_MANAGE'
            USING ERRCODE = '42501';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM finance.currencies WHERE code = v_new_func AND is_active = true) THEN
        RAISE EXCEPTION 'INVALID_FUNCTIONAL_CURRENCY';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM finance.currencies WHERE code = v_new_pres AND is_active = true) THEN
        RAISE EXCEPTION 'INVALID_PRESENTATION_CURRENCY';
    END IF;

    IF p_effective_date IS NULL THEN
        RAISE EXCEPTION 'EFFECTIVE_DATE_REQUIRED';
    END IF;

    SELECT c.functional_currency_code, c.presentation_currency_code, c.currency_effective_date
    INTO v_prev_func, v_prev_pres, v_prev_date
    FROM organization.companies c
    WHERE c.tenant_id = v_ctx_tenant AND c.id = v_ctx_company AND c.status = 'active'
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'COMPANY_NOT_FOUND';
    END IF;

    v_has_posted := EXISTS (
        SELECT 1 FROM finance.journals j
        WHERE j.tenant_id = v_ctx_tenant AND j.company_id = v_ctx_company AND j.status = 'posted'
    ) OR EXISTS (
        SELECT 1 FROM erp_inventory.inventory_cost_layer cl
        WHERE cl.tenant_id = v_ctx_tenant AND cl.company_id = v_ctx_company AND cl.remaining_qty > 0
    );

    IF v_prev_func <> v_new_func AND COALESCE(p_confirm_future_transactions, false) IS NOT TRUE THEN
        RAISE EXCEPTION 'CONFIRMATION_REQUIRED_FOR_FUTURE_TRANSACTIONS';
    END IF;

    UPDATE organization.companies
    SET functional_currency_code = v_new_func,
        base_currency_code = v_new_func,
        presentation_currency_code = v_new_pres,
        currency_effective_date = p_effective_date,
        updated_at = now()
    WHERE tenant_id = v_ctx_tenant AND id = v_ctx_company;

    INSERT INTO organization.company_currency_history (
        tenant_id, company_id,
        previous_functional_currency, new_functional_currency,
        previous_presentation_currency, new_presentation_currency,
        previous_effective_date, new_effective_date,
        confirmed_future_transactions, change_reason,
        changed_by_user_id, changed_at
    ) VALUES (
        v_ctx_tenant, v_ctx_company,
        v_prev_func, v_new_func,
        v_prev_pres, v_new_pres,
        v_prev_date, p_effective_date,
        COALESCE(p_confirm_future_transactions, false), v_reason,
        p_actor_user_id, now()
    );

    INSERT INTO audit.audit_events (
        id, tenant_id, company_id, actor_user_id,
        event_code, entity_type, entity_id, correlation_id,
        occurred_at, recorded_at, metadata
    ) VALUES (
        gen_random_uuid(), v_ctx_tenant, v_ctx_company, p_actor_user_id,
        'COMPANY_CURRENCY_SETTINGS_UPDATED', 'company', v_ctx_company, gen_random_uuid(),
        now(), now(),
        jsonb_build_object(
            'previousFunctionalCurrency', TRIM(v_prev_func),
            'newFunctionalCurrency', TRIM(v_new_func),
            'previousPresentationCurrency', TRIM(v_prev_pres),
            'newPresentationCurrency', TRIM(v_new_pres),
            'previousEffectiveDate', v_prev_date,
            'newEffectiveDate', p_effective_date,
            'confirmedFutureTransactions', COALESCE(p_confirm_future_transactions, false),
            'historicalRecordsPreserved', true,
            'reason', v_reason
        )
    );

    RETURN identity.get_company_settings(v_ctx_tenant, v_ctx_company);
END;
$$;

ALTER FUNCTION identity.update_company_currency_settings(uuid, uuid, uuid, char(3), char(3), date, boolean, text) OWNER TO CURRENT_USER;
REVOKE ALL ON FUNCTION identity.update_company_currency_settings(uuid, uuid, uuid, char(3), char(3), date, boolean, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.update_company_currency_settings(uuid, uuid, uuid, char(3), char(3), date, boolean, text) TO erp_app;

CREATE OR REPLACE FUNCTION identity.user_has_permission(
    p_tenant_id uuid,
    p_company_id uuid,
    p_user_id uuid,
    p_permission_code text
)
RETURNS boolean
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'identity', 'authorization'
AS $$
    SELECT EXISTS (
      SELECT 1
      FROM identity.company_memberships AS cm
      JOIN identity.tenant_memberships AS tm
        ON tm.tenant_id = cm.tenant_id
       AND tm.user_id = cm.user_id
      JOIN "authorization".membership_role_assignments AS assignment
        ON assignment.tenant_id = tm.tenant_id
       AND assignment.membership_id = tm.id
      JOIN "authorization".role_permissions AS permission
        ON permission.tenant_id = assignment.tenant_id
       AND permission.role_id = assignment.role_id
      WHERE cm.tenant_id = p_tenant_id
        AND cm.company_id = p_company_id
        AND cm.user_id = p_user_id
        AND cm.status = 'active'
        AND tm.status = 'active'
        AND assignment.effective_from <= now()
        AND (assignment.effective_until IS NULL OR assignment.effective_until > now())
        AND permission.permission_code = p_permission_code
        AND permission.scope_type IN ('tenant','company')
    );
$$;

REVOKE ALL ON FUNCTION identity.user_has_permission(uuid, uuid, uuid, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.user_has_permission(uuid, uuid, uuid, text) TO erp_app;
