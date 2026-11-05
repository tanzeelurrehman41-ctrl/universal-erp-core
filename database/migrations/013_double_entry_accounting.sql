-- Migration 013 – Finance & Double-Entry Accounting Foundation
-- PostgreSQL schema for Chart of Accounts, Fiscal Years/Periods, Journals, and Ledger Posting.

CREATE SCHEMA IF NOT EXISTS finance;

-- ── 1. Currencies ─────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS finance.currencies (
    code char(3) PRIMARY KEY,
    name text NOT NULL,
    minor_unit smallint NOT NULL CHECK (minor_unit BETWEEN 0 AND 4),
    is_active boolean NOT NULL DEFAULT true
);

INSERT INTO finance.currencies (code, name, minor_unit)
VALUES
    ('PKR', 'Pakistani Rupee', 2),
    ('USD', 'US Dollar', 2),
    ('EUR', 'Euro', 2),
    ('GBP', 'Pound Sterling', 2),
    ('AED', 'UAE Dirham', 2),
    ('SAR', 'Saudi Riyal', 2)
ON CONFLICT (code) DO NOTHING;

-- ── 2. Fiscal Years & Periods ─────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS finance.fiscal_years (
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    code text NOT NULL,
    starts_on date NOT NULL,
    ends_on date NOT NULL,
    status text NOT NULL DEFAULT 'open' CHECK (status IN ('open','closed')),
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, id),
    UNIQUE (tenant_id, company_id, code),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    CHECK (ends_on >= starts_on)
);

CREATE TABLE IF NOT EXISTS finance.fiscal_periods (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    fiscal_year_id uuid NOT NULL,
    period_no smallint NOT NULL CHECK (period_no BETWEEN 1 AND 16),
    name text NOT NULL,
    starts_on date NOT NULL,
    ends_on date NOT NULL,
    status text NOT NULL DEFAULT 'open' CHECK (status IN ('open','soft_closed','closed')),
    closed_at timestamptz,
    closed_by uuid REFERENCES identity.users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, id),
    UNIQUE (tenant_id, company_id, fiscal_year_id, period_no),
    FOREIGN KEY (tenant_id, company_id, fiscal_year_id)
        REFERENCES finance.fiscal_years(tenant_id, company_id, id),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    CHECK (ends_on >= starts_on)
);

-- ── 3. Chart of Accounts ─────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS finance.accounts (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    account_code text NOT NULL,
    account_name text NOT NULL,
    account_type text NOT NULL CHECK (
        account_type IN ('asset','liability','equity','revenue','expense')
    ),
    normal_balance text NOT NULL CHECK (normal_balance IN ('debit','credit')),
    parent_account_id uuid,
    currency_code char(3) REFERENCES finance.currencies(code),
    is_control_account boolean NOT NULL DEFAULT false,
    allow_manual_posting boolean NOT NULL DEFAULT true,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, id),
    UNIQUE (tenant_id, company_id, account_code),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, parent_account_id)
        REFERENCES finance.accounts(tenant_id, company_id, id)
);

CREATE INDEX IF NOT EXISTS ix_finance_accounts_code ON finance.accounts(tenant_id, company_id, account_code);
CREATE INDEX IF NOT EXISTS ix_finance_accounts_type ON finance.accounts(tenant_id, company_id, account_type);

-- ── 4. Exchange Rates ────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS finance.exchange_rates (
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    from_currency char(3) NOT NULL REFERENCES finance.currencies(code),
    to_currency char(3) NOT NULL REFERENCES finance.currencies(code),
    effective_at timestamptz NOT NULL,
    rate numeric(20,10) NOT NULL CHECK (rate > 0),
    source text NOT NULL DEFAULT 'manual',
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, id),
    CHECK (from_currency <> to_currency)
);

-- ── 5. General Ledger Journals & Lines ───────────────────────────────────────

CREATE TABLE IF NOT EXISTS finance.journals (
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    company_id uuid NOT NULL,
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    journal_no text NOT NULL,
    journal_date date NOT NULL,
    fiscal_period_id uuid NOT NULL,
    description text NOT NULL,
    functional_currency char(3) NOT NULL REFERENCES finance.currencies(code),
    source_module text NOT NULL,
    source_type text NOT NULL,
    source_id text NOT NULL,
    idempotency_key text NOT NULL,
    status text NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft','submitted','approved','posted','reversed')),
    created_by uuid NOT NULL REFERENCES identity.users(id),
    approved_by uuid REFERENCES identity.users(id),
    posted_by uuid REFERENCES identity.users(id),
    posted_at timestamptz,
    reverses_journal_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, id),
    UNIQUE (tenant_id, company_id, journal_no),
    UNIQUE (tenant_id, company_id, idempotency_key),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, fiscal_period_id)
        REFERENCES finance.fiscal_periods(tenant_id, company_id, id),
    FOREIGN KEY (tenant_id, company_id, reverses_journal_id)
        REFERENCES finance.journals(tenant_id, company_id, id),
    CHECK ((status IN ('posted','reversed') AND posted_at IS NOT NULL AND posted_by IS NOT NULL)
        OR status NOT IN ('posted','reversed'))
);

CREATE TABLE IF NOT EXISTS finance.journal_lines (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    journal_id uuid NOT NULL,
    line_no integer NOT NULL CHECK (line_no > 0),
    account_id uuid NOT NULL,
    description text,
    transaction_currency char(3) NOT NULL REFERENCES finance.currencies(code),
    transaction_amount numeric(20,6) NOT NULL CHECK (transaction_amount > 0),
    exchange_rate numeric(20,10) NOT NULL CHECK (exchange_rate > 0),
    debit_functional numeric(20,4) NOT NULL DEFAULT 0 CHECK (debit_functional >= 0),
    credit_functional numeric(20,4) NOT NULL DEFAULT 0 CHECK (credit_functional >= 0),
    department_id uuid,
    location_id uuid,
    cost_center_id uuid,
    PRIMARY KEY (tenant_id, company_id, journal_id, line_no),
    FOREIGN KEY (tenant_id, company_id, journal_id)
        REFERENCES finance.journals(tenant_id, company_id, id),
    FOREIGN KEY (tenant_id, company_id, account_id)
        REFERENCES finance.accounts(tenant_id, company_id, id),
    FOREIGN KEY (tenant_id, company_id, location_id)
        REFERENCES organization.locations(tenant_id, company_id, id),
    CHECK (
        (debit_functional > 0 AND credit_functional = 0)
        OR (credit_functional > 0 AND debit_functional = 0)
    )
);

CREATE INDEX IF NOT EXISTS ix_journals_posting_date ON finance.journals(tenant_id, company_id, journal_date);
CREATE INDEX IF NOT EXISTS ix_journals_source ON finance.journals(tenant_id, company_id, source_module, source_type, source_id);
CREATE INDEX IF NOT EXISTS ix_journal_lines_account ON finance.journal_lines(tenant_id, company_id, account_id, journal_id);

-- ── 6. Balance Verification Constraint Trigger ───────────────────────────────

CREATE OR REPLACE FUNCTION finance.assert_posted_journal_balanced()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_tenant uuid;
    v_company uuid;
    v_journal uuid;
    v_status text;
    v_lines bigint;
    v_debits numeric(24,4);
    v_credits numeric(24,4);
BEGIN
    IF TG_TABLE_NAME = 'journal_lines' THEN
        v_tenant := COALESCE(NEW.tenant_id, OLD.tenant_id);
        v_company := COALESCE(NEW.company_id, OLD.company_id);
        v_journal := COALESCE(NEW.journal_id, OLD.journal_id);
    ELSE
        v_tenant := COALESCE(NEW.tenant_id, OLD.tenant_id);
        v_company := COALESCE(NEW.company_id, OLD.company_id);
        v_journal := COALESCE(NEW.id, OLD.id);
    END IF;

    SELECT status INTO v_status
    FROM finance.journals
    WHERE tenant_id = v_tenant AND company_id = v_company AND id = v_journal;

    IF v_status = 'posted' OR v_status = 'reversed' THEN
        SELECT count(*),
               COALESCE(sum(debit_functional), 0),
               COALESCE(sum(credit_functional), 0)
          INTO v_lines, v_debits, v_credits
        FROM finance.journal_lines
        WHERE tenant_id = v_tenant AND company_id = v_company AND journal_id = v_journal;

        IF v_lines < 2 OR v_debits <> v_credits THEN
            RAISE EXCEPTION 'Posted journal % is not balanced (lines %, debits %, credits %)',
                v_journal, v_lines, v_debits, v_credits;
        END IF;
    END IF;
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS journals_balanced_at_commit ON finance.journals;
CREATE CONSTRAINT TRIGGER journals_balanced_at_commit
AFTER INSERT OR UPDATE ON finance.journals
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION finance.assert_posted_journal_balanced();

DROP TRIGGER IF EXISTS journal_lines_balanced_at_commit ON finance.journal_lines;
CREATE CONSTRAINT TRIGGER journal_lines_balanced_at_commit
AFTER INSERT OR UPDATE OR DELETE ON finance.journal_lines
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION finance.assert_posted_journal_balanced();

-- Prevent mutation of lines belonging to posted journals
CREATE OR REPLACE FUNCTION finance.reject_posted_journal_line_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_status text;
BEGIN
    SELECT status INTO v_status
    FROM finance.journals
    WHERE tenant_id = COALESCE(OLD.tenant_id, NEW.tenant_id)
      AND company_id = COALESCE(OLD.company_id, NEW.company_id)
      AND id = COALESCE(OLD.journal_id, NEW.journal_id);

    IF v_status IN ('posted','reversed') THEN
        RAISE EXCEPTION 'Cannot mutate lines of a posted/reversed journal; reverse it instead';
    END IF;
    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS journal_lines_immutable_after_post ON finance.journal_lines;
CREATE TRIGGER journal_lines_immutable_after_post
BEFORE UPDATE OR DELETE ON finance.journal_lines
FOR EACH ROW EXECUTE FUNCTION finance.reject_posted_journal_line_mutation();

-- ── 7. Row-Level Security ─────────────────────────────────────────────────────

DO $$
DECLARE t text;
BEGIN
  FOREACH t IN ARRAY ARRAY[
    'fiscal_years','fiscal_periods','accounts','journals','journal_lines'
  ] LOOP
    EXECUTE format('ALTER TABLE finance.%I ENABLE ROW LEVEL SECURITY', t);
    EXECUTE format('ALTER TABLE finance.%I FORCE ROW LEVEL SECURITY', t);
    EXECUTE format('DROP POLICY IF EXISTS tenant_company_scope ON finance.%I', t);
    EXECUTE format(
      'CREATE POLICY tenant_company_scope ON finance.%I
       USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid
          AND company_id = NULLIF(current_setting(''app.company_id'', true), '''')::uuid)
       WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid
          AND company_id = NULLIF(current_setting(''app.company_id'', true), '''')::uuid)', t
    );
  END LOOP;
END $$;

ALTER TABLE finance.exchange_rates ENABLE ROW LEVEL SECURITY;
ALTER TABLE finance.exchange_rates FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_scope ON finance.exchange_rates;
CREATE POLICY tenant_scope ON finance.exchange_rates
USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- ── 8. Permissions & Least-Privilege Grants ──────────────────────────────────

GRANT USAGE ON SCHEMA finance TO erp_app;
GRANT SELECT ON finance.currencies TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON
    finance.fiscal_years,
    finance.fiscal_periods,
    finance.accounts,
    finance.exchange_rates,
    finance.journals,
    finance.journal_lines
TO erp_app;
