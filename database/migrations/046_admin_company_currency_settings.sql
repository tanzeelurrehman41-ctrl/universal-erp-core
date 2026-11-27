-- Migration 046: Admin Company Currency Settings & IAS 21 Presentation Currency Foundation
-- Establishes distinct Functional (Accounting) vs Presentation (Reporting) currency settings
-- per company, immutable currency configuration audit history, company-scoped exchange rates
-- with (pair, effective_date, rate_type) uniqueness, and RBAC permission seeding.
-- Preserves all historical posted journals, inventory FIFO layers, and GL balances untouched.

-- 1. Ensure standard ISO 4217 currencies are available
INSERT INTO finance.currencies (code, name, minor_unit, is_active)
VALUES
    ('PKR', 'Pakistani Rupee', 2, true),
    ('USD', 'US Dollar', 2, true),
    ('GBP', 'British Pound Sterling', 2, true),
    ('EUR', 'Euro', 2, true),
    ('AED', 'UAE Dirham', 2, true),
    ('SAR', 'Saudi Riyal', 2, true),
    ('CAD', 'Canadian Dollar', 2, true),
    ('AUD', 'Australian Dollar', 2, true),
    ('INR', 'Indian Rupee', 2, true),
    ('JPY', 'Japanese Yen', 0, true),
    ('CHF', 'Swiss Franc', 2, true),
    ('SGD', 'Singapore Dollar', 2, true)
ON CONFLICT (code) DO NOTHING;

-- 2. Separate Functional vs Presentation currency settings on organization.companies
ALTER TABLE organization.companies
    ADD COLUMN IF NOT EXISTS functional_currency_code char(3) REFERENCES finance.currencies(code),
    ADD COLUMN IF NOT EXISTS presentation_currency_code char(3) REFERENCES finance.currencies(code),
    ADD COLUMN IF NOT EXISTS currency_effective_date date;

DO $$
DECLARE
    t_rec record;
BEGIN
    FOR t_rec IN SELECT DISTINCT id FROM platform.tenants LOOP
        PERFORM set_config('app.tenant_id', t_rec.id::text, true);
        UPDATE organization.companies
        SET functional_currency_code = COALESCE(functional_currency_code, base_currency_code),
            presentation_currency_code = COALESCE(presentation_currency_code, base_currency_code),
            currency_effective_date = COALESCE(currency_effective_date, created_at::date)
        WHERE tenant_id = t_rec.id
          AND (
              functional_currency_code IS NULL
              OR presentation_currency_code IS NULL
              OR currency_effective_date IS NULL
          );
    END LOOP;
END $$;

CREATE OR REPLACE FUNCTION organization.set_default_company_currencies()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.functional_currency_code := UPPER(TRIM(COALESCE(NEW.functional_currency_code, NEW.base_currency_code, 'PKR')))::char(3);
    NEW.presentation_currency_code := UPPER(TRIM(COALESCE(NEW.presentation_currency_code, NEW.functional_currency_code, NEW.base_currency_code, 'PKR')))::char(3);
    NEW.currency_effective_date := COALESCE(NEW.currency_effective_date, CURRENT_DATE);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_companies_default_currencies ON organization.companies;
CREATE TRIGGER trg_companies_default_currencies
    BEFORE INSERT ON organization.companies
    FOR EACH ROW
    EXECUTE FUNCTION organization.set_default_company_currencies();

ALTER TABLE organization.companies
    ALTER COLUMN functional_currency_code DROP DEFAULT,
    ALTER COLUMN functional_currency_code SET NOT NULL,
    ALTER COLUMN presentation_currency_code DROP DEFAULT,
    ALTER COLUMN presentation_currency_code SET NOT NULL,
    ALTER COLUMN currency_effective_date SET DEFAULT CURRENT_DATE,
    ALTER COLUMN currency_effective_date SET NOT NULL;

-- 3. Immutable Company Currency Configuration History Table (RLS-enforced)
CREATE TABLE IF NOT EXISTS organization.company_currency_history (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    company_id uuid NOT NULL,
    previous_functional_currency char(3) NOT NULL REFERENCES finance.currencies(code),
    new_functional_currency char(3) NOT NULL REFERENCES finance.currencies(code),
    previous_presentation_currency char(3) NOT NULL REFERENCES finance.currencies(code),
    new_presentation_currency char(3) NOT NULL REFERENCES finance.currencies(code),
    previous_effective_date date NOT NULL,
    new_effective_date date NOT NULL,
    confirmed_future_transactions boolean NOT NULL DEFAULT false,
    change_reason text,
    changed_by_user_id uuid NOT NULL REFERENCES identity.users(id),
    changed_at timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id)
);

CREATE INDEX IF NOT EXISTS ix_company_currency_history_lookup
    ON organization.company_currency_history(tenant_id, company_id, changed_at DESC);

ALTER TABLE organization.company_currency_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization.company_currency_history FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_scope ON organization.company_currency_history;
CREATE POLICY tenant_scope ON organization.company_currency_history
    USING (
        tenant_id = (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid
        AND (
            NULLIF(current_setting('app.company_id'::text, true), ''::text) IS NULL
            OR company_id = (NULLIF(current_setting('app.company_id'::text, true), ''::text))::uuid
        )
    )
    WITH CHECK (
        tenant_id = (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid
        AND (
            NULLIF(current_setting('app.company_id'::text, true), ''::text) IS NULL
            OR company_id = (NULLIF(current_setting('app.company_id'::text, true), ''::text))::uuid
        )
    );

GRANT USAGE ON SCHEMA organization TO erp_app;
GRANT SELECT, INSERT ON organization.company_currency_history TO erp_app;
REVOKE UPDATE, DELETE, TRUNCATE ON organization.company_currency_history FROM erp_app;

-- 4. Extend finance.exchange_rates for company isolation, rate_type, effective_date, reference, and user audit
ALTER TABLE finance.exchange_rates
    ADD COLUMN IF NOT EXISTS company_id uuid,
    ADD COLUMN IF NOT EXISTS effective_date date,
    ADD COLUMN IF NOT EXISTS rate_type text NOT NULL DEFAULT 'transaction',
    ADD COLUMN IF NOT EXISTS reference text,
    ADD COLUMN IF NOT EXISTS created_by uuid REFERENCES identity.users(id),
    ADD COLUMN IF NOT EXISTS updated_by uuid REFERENCES identity.users(id),
    ADD COLUMN IF NOT EXISTS updated_at timestamptz;

DO $$
DECLARE
    t_rec record;
BEGIN
    FOR t_rec IN SELECT DISTINCT id FROM platform.tenants LOOP
        PERFORM set_config('app.tenant_id', t_rec.id::text, true);
        UPDATE finance.exchange_rates
        SET effective_date = COALESCE(effective_date, (effective_at AT TIME ZONE 'UTC')::date)
        WHERE tenant_id = t_rec.id
          AND effective_date IS NULL;
    END LOOP;
END $$;

ALTER TABLE finance.exchange_rates
    ALTER COLUMN effective_date SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'exchange_rates_rate_type_check'
          AND conrelid = 'finance.exchange_rates'::regclass
    ) THEN
        ALTER TABLE finance.exchange_rates
            ADD CONSTRAINT exchange_rates_rate_type_check
            CHECK (rate_type IN ('transaction', 'average', 'closing'));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'exchange_rates_tenant_company_fkey'
          AND conrelid = 'finance.exchange_rates'::regclass
    ) THEN
        ALTER TABLE finance.exchange_rates
            ADD CONSTRAINT exchange_rates_tenant_company_fkey
            FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id);
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_finance_exchange_rates_pair_date_type
    ON finance.exchange_rates (
        tenant_id,
        COALESCE(company_id, '00000000-0000-0000-0000-000000000000'::uuid),
        from_currency,
        to_currency,
        effective_date,
        rate_type
    );

CREATE INDEX IF NOT EXISTS ix_finance_exchange_rates_company_date
    ON finance.exchange_rates (tenant_id, company_id, effective_date DESC, rate_type);

DROP POLICY IF EXISTS tenant_scope ON finance.exchange_rates;
CREATE POLICY tenant_scope ON finance.exchange_rates
    USING (
        tenant_id = (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid
        AND (
            NULLIF(current_setting('app.company_id'::text, true), ''::text) IS NULL
            OR company_id IS NULL
            OR company_id = (NULLIF(current_setting('app.company_id'::text, true), ''::text))::uuid
        )
    )
    WITH CHECK (
        tenant_id = (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid
        AND (
            NULLIF(current_setting('app.company_id'::text, true), ''::text) IS NULL
            OR company_id = (NULLIF(current_setting('app.company_id'::text, true), ''::text))::uuid
        )
    );

GRANT USAGE ON SCHEMA finance TO erp_app;
GRANT SELECT (base_currency_code, functional_currency_code, presentation_currency_code, currency_effective_date) ON organization.companies TO erp_app;
GRANT SELECT ON finance.currencies TO erp_app;
GRANT SELECT, INSERT, UPDATE ON finance.exchange_rates TO erp_app;
GRANT SELECT, INSERT ON finance.financial_audit_logs TO erp_app;
REVOKE DELETE, TRUNCATE ON finance.exchange_rates FROM erp_app;

-- 5. Seed RBAC Permissions for Currency Settings and Exchange-Rate Management
INSERT INTO "authorization".permissions (permission_code, module_code, description)
VALUES
    ('admin.currency_settings.view', 'foundation', 'View company functional and presentation currency settings and audit history'),
    ('admin.currency_settings.manage', 'foundation', 'Modify company functional and presentation currency settings with confirmation'),
    ('finance.exchange_rate.manage', 'finance', 'Create and manage company exchange rates across transaction, average, and closing rate types')
ON CONFLICT (permission_code) DO UPDATE SET
    module_code = EXCLUDED.module_code,
    description = EXCLUDED.description;

DO $$
DECLARE
    role_rec record;
    p_code text;
BEGIN
    FOR role_rec IN
        SELECT id, tenant_id
        FROM "authorization".roles
        WHERE role_code IN ('tenant_admin', 'DEMO_ADMIN', 'company_admin', 'accountant', 'finance_manager', 'finance_admin')
    LOOP
        PERFORM set_config('app.tenant_id', role_rec.tenant_id::text, true);
        FOREACH p_code IN ARRAY ARRAY[
            'admin.currency_settings.view',
            'admin.currency_settings.manage',
            'finance.exchange_rate.manage'
        ] LOOP
            INSERT INTO "authorization".role_permissions (tenant_id, role_id, permission_code, scope_type)
            VALUES (role_rec.tenant_id, role_rec.id, p_code, 'company')
            ON CONFLICT (tenant_id, role_id, permission_code, scope_type) DO NOTHING;
        END LOOP;
    END LOOP;
END $$;

-- 6. Update identity routines for Functional & Presentation currencies and history
DROP FUNCTION IF EXISTS identity.list_user_company_contexts(text, text);

CREATE FUNCTION identity.list_user_company_contexts(
    p_issuer text,
    p_subject text
)
RETURNS TABLE (
    tenant_id uuid,
    tenant_code text,
    tenant_name text,
    company_id uuid,
    company_code text,
    company_name text,
    base_currency_code char(3),
    timezone text,
    functional_currency_code char(3),
    presentation_currency_code char(3),
    currency_effective_date date
)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, identity, platform, organization
AS $$
    SELECT tenant.id,
           tenant.tenant_code,
           tenant.display_name,
           company.id,
           company.company_code,
           company.name,
           company.functional_currency_code,
           company.timezone,
           company.functional_currency_code,
           company.presentation_currency_code,
           company.currency_effective_date
    FROM identity.external_identities AS external_identity
    JOIN identity.users AS erp_user
      ON erp_user.id = external_identity.user_id
    JOIN identity.tenant_memberships AS tenant_membership
      ON tenant_membership.user_id = erp_user.id
    JOIN identity.company_memberships AS company_membership
      ON company_membership.tenant_id = tenant_membership.tenant_id
     AND company_membership.user_id = erp_user.id
    JOIN platform.tenants AS tenant
      ON tenant.id = tenant_membership.tenant_id
    JOIN organization.companies AS company
      ON company.tenant_id = tenant.id
     AND company.id = company_membership.company_id
    WHERE external_identity.issuer = p_issuer
      AND external_identity.subject = p_subject
      AND erp_user.status = 'active'
      AND tenant_membership.status = 'active'
      AND company_membership.status = 'active'
      AND tenant.status = 'active'
      AND company.status = 'active'
    ORDER BY tenant.display_name, company.name;
$$;

ALTER FUNCTION identity.list_user_company_contexts(text, text) OWNER TO CURRENT_USER;
REVOKE ALL ON FUNCTION identity.list_user_company_contexts(text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.list_user_company_contexts(text, text) TO erp_app;

CREATE OR REPLACE FUNCTION identity.get_company_settings(
    p_tenant_id uuid,
    p_company_id uuid
)
RETURNS jsonb
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'organization', 'finance', 'erp_inventory'
AS $$
    SELECT jsonb_build_object(
        'companyId', c.id,
        'tenantId', c.tenant_id,
        'companyCode', c.company_code,
        'name', c.name,
        'legalName', c.legal_name,
        'registrationNumber', c.registration_number,
        'countryCode', c.country_code,
        'currencyCode', c.functional_currency_code,
        'baseCurrencyCode', c.base_currency_code,
        'functionalCurrencyCode', c.functional_currency_code,
        'presentationCurrencyCode', c.presentation_currency_code,
        'currencyEffectiveDate', c.currency_effective_date,
        'hasPostedTransactions', (
            EXISTS (
                SELECT 1 FROM finance.journals j
                WHERE j.tenant_id = p_tenant_id AND j.company_id = p_company_id AND j.status = 'posted'
            ) OR EXISTS (
                SELECT 1 FROM erp_inventory.inventory_cost_layer cl
                WHERE cl.tenant_id = p_tenant_id AND cl.company_id = p_company_id AND cl.remaining_qty > 0
            )
        ),
        'timezone', c.timezone,
        'fiscalYearStartMonth', c.fiscal_year_start_month,
        'status', c.status,
        'updatedAt', c.updated_at
    )
    FROM organization.companies c
    WHERE c.tenant_id = p_tenant_id AND c.id = p_company_id;
$$;

ALTER FUNCTION identity.get_company_settings(uuid, uuid) OWNER TO CURRENT_USER;
REVOKE ALL ON FUNCTION identity.get_company_settings(uuid, uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.get_company_settings(uuid, uuid) TO erp_app;

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
    v_prev_func char(3);
    v_prev_pres char(3);
    v_prev_date date;
    v_new_func char(3) := UPPER(TRIM(p_functional_currency))::char(3);
    v_new_pres char(3) := UPPER(TRIM(p_presentation_currency))::char(3);
    v_has_posted boolean := false;
    v_reason text := NULLIF(TRIM(p_change_reason), '');
BEGIN
    PERFORM set_config('app.tenant_id', p_tenant_id::text, true);
    PERFORM set_config('app.company_id', p_company_id::text, true);

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
    WHERE c.tenant_id = p_tenant_id AND c.id = p_company_id AND c.status = 'active'
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'COMPANY_NOT_FOUND';
    END IF;

    v_has_posted := EXISTS (
        SELECT 1 FROM finance.journals j
        WHERE j.tenant_id = p_tenant_id AND j.company_id = p_company_id AND j.status = 'posted'
    ) OR EXISTS (
        SELECT 1 FROM erp_inventory.inventory_cost_layer cl
        WHERE cl.tenant_id = p_tenant_id AND cl.company_id = p_company_id AND cl.remaining_qty > 0
    );

    IF v_prev_func <> v_new_func AND COALESCE(p_confirm_future_transactions, false) IS NOT TRUE THEN
        RAISE EXCEPTION 'CONFIRMATION_REQUIRED_FOR_FUTURE_TRANSACTIONS';
    END IF;

    -- Update company settings ONLY; posted journals and FIFO layers are never restated
    UPDATE organization.companies
    SET functional_currency_code = v_new_func,
        base_currency_code = v_new_func,
        presentation_currency_code = v_new_pres,
        currency_effective_date = p_effective_date,
        updated_at = now()
    WHERE tenant_id = p_tenant_id AND id = p_company_id;

    INSERT INTO organization.company_currency_history (
        tenant_id,
        company_id,
        previous_functional_currency,
        new_functional_currency,
        previous_presentation_currency,
        new_presentation_currency,
        previous_effective_date,
        new_effective_date,
        confirmed_future_transactions,
        change_reason,
        changed_by_user_id,
        changed_at
    ) VALUES (
        p_tenant_id,
        p_company_id,
        v_prev_func,
        v_new_func,
        v_prev_pres,
        v_new_pres,
        v_prev_date,
        p_effective_date,
        COALESCE(p_confirm_future_transactions, false),
        v_reason,
        p_actor_user_id,
        now()
    );

    INSERT INTO audit.audit_events (
        id,
        tenant_id,
        company_id,
        actor_user_id,
        event_code,
        entity_type,
        entity_id,
        correlation_id,
        occurred_at,
        recorded_at,
        metadata
    ) VALUES (
        gen_random_uuid(),
        p_tenant_id,
        p_company_id,
        p_actor_user_id,
        'COMPANY_CURRENCY_SETTINGS_UPDATED',
        'company',
        p_company_id,
        gen_random_uuid(),
        now(),
        now(),
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

    RETURN identity.get_company_settings(p_tenant_id, p_company_id);
END;
$$;

ALTER FUNCTION identity.update_company_currency_settings(uuid, uuid, uuid, char(3), char(3), date, boolean, text) OWNER TO CURRENT_USER;
REVOKE ALL ON FUNCTION identity.update_company_currency_settings(uuid, uuid, uuid, char(3), char(3), date, boolean, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.update_company_currency_settings(uuid, uuid, uuid, char(3), char(3), date, boolean, text) TO erp_app;

CREATE OR REPLACE FUNCTION identity.list_company_currency_history(
    p_tenant_id uuid,
    p_company_id uuid,
    p_limit int DEFAULT 50
)
RETURNS TABLE (
    history_id uuid,
    previous_functional_currency char(3),
    new_functional_currency char(3),
    previous_presentation_currency char(3),
    new_presentation_currency char(3),
    previous_effective_date date,
    new_effective_date date,
    confirmed_future_transactions boolean,
    change_reason text,
    changed_by_user_id uuid,
    changed_by_email text,
    changed_by_name text,
    changed_at timestamptz
)
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'organization', 'identity'
AS $$
    SELECT h.id,
           h.previous_functional_currency,
           h.new_functional_currency,
           h.previous_presentation_currency,
           h.new_presentation_currency,
           h.previous_effective_date,
           h.new_effective_date,
           h.confirmed_future_transactions,
           h.change_reason,
           h.changed_by_user_id,
           u.email_normalized,
           u.display_name,
           h.changed_at
    FROM organization.company_currency_history h
    LEFT JOIN identity.users u ON u.id = h.changed_by_user_id
    WHERE h.tenant_id = p_tenant_id
      AND h.company_id = p_company_id
    ORDER BY h.changed_at DESC
    LIMIT LEAST(GREATEST(p_limit, 1), 200);
$$;

ALTER FUNCTION identity.list_company_currency_history(uuid, uuid, int) OWNER TO CURRENT_USER;
REVOKE ALL ON FUNCTION identity.list_company_currency_history(uuid, uuid, int) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.list_company_currency_history(uuid, uuid, int) TO erp_app;

CREATE OR REPLACE FUNCTION identity.list_company_exchange_rates(
    p_tenant_id uuid,
    p_company_id uuid,
    p_from_currency char(3) DEFAULT NULL,
    p_to_currency char(3) DEFAULT NULL,
    p_rate_type text DEFAULT NULL
)
RETURNS TABLE (
    exchange_rate_id uuid,
    company_id uuid,
    from_currency char(3),
    to_currency char(3),
    rate numeric(20,10),
    effective_date date,
    effective_at timestamptz,
    rate_type text,
    source text,
    reference text,
    created_by uuid,
    created_by_email text,
    created_by_name text,
    created_at timestamptz,
    updated_by uuid,
    updated_by_email text,
    updated_at timestamptz
)
LANGUAGE sql
STABLE SECURITY DEFINER
SET search_path TO 'pg_catalog', 'finance', 'identity'
AS $$
    SELECT er.id,
           er.company_id,
           er.from_currency,
           er.to_currency,
           er.rate,
           er.effective_date,
           er.effective_at,
           er.rate_type,
           er.source,
           er.reference,
           er.created_by,
           cu.email_normalized,
           cu.display_name,
           er.created_at,
           er.updated_by,
           uu.email_normalized,
           er.updated_at
    FROM finance.exchange_rates er
    LEFT JOIN identity.users cu ON cu.id = er.created_by
    LEFT JOIN identity.users uu ON uu.id = er.updated_by
    WHERE er.tenant_id = p_tenant_id
      AND er.company_id = p_company_id
      AND (p_from_currency IS NULL OR er.from_currency = UPPER(TRIM(p_from_currency))::char(3))
      AND (p_to_currency IS NULL OR er.to_currency = UPPER(TRIM(p_to_currency))::char(3))
      AND (p_rate_type IS NULL OR er.rate_type = LOWER(TRIM(p_rate_type)))
    ORDER BY er.effective_date DESC, er.rate_type ASC, er.from_currency ASC, er.to_currency ASC, er.created_at DESC;
$$;

ALTER FUNCTION identity.list_company_exchange_rates(uuid, uuid, char(3), char(3), text) OWNER TO CURRENT_USER;
REVOKE ALL ON FUNCTION identity.list_company_exchange_rates(uuid, uuid, char(3), char(3), text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.list_company_exchange_rates(uuid, uuid, char(3), char(3), text) TO erp_app;
