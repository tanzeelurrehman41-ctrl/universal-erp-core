-- Migration 015 – Sales, POS, Customers & Outbox Events
-- Schema: erp_sales

CREATE SCHEMA IF NOT EXISTS erp_sales;

-- ── 1. Customers ─────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS erp_sales.customer (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    customer_id uuid NOT NULL DEFAULT gen_random_uuid(),
    customer_code text NOT NULL,
    name text NOT NULL,
    customer_type text NOT NULL DEFAULT 'retail' CHECK (customer_type IN ('retail', 'wholesale', 'distributor', 'corporate')),
    contact_name text,
    email text,
    phone text,
    address text,
    ntn_number text,
    strn_number text,
    credit_limit numeric(20,4) NOT NULL DEFAULT 0 CHECK (credit_limit >= 0),
    outstanding_balance numeric(20,4) NOT NULL DEFAULT 0,
    currency_code char(3) NOT NULL DEFAULT 'PKR',
    payment_terms_days integer NOT NULL DEFAULT 0 CHECK (payment_terms_days >= 0),
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, customer_id),
    UNIQUE (tenant_id, company_id, customer_code),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id)
);

CREATE INDEX IF NOT EXISTS ix_sales_customer_code ON erp_sales.customer(tenant_id, company_id, customer_code);
CREATE INDEX IF NOT EXISTS ix_sales_customer_name ON erp_sales.customer(tenant_id, company_id, name);

-- ── 2. Sales Orders ──────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS erp_sales.sales_order (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    order_id uuid NOT NULL DEFAULT gen_random_uuid(),
    order_number text NOT NULL,
    customer_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    order_type text NOT NULL DEFAULT 'standard' CHECK (order_type IN ('standard', 'pos', 'wholesale')),
    order_status text NOT NULL DEFAULT 'draft' CHECK (order_status IN ('draft', 'confirmed', 'fulfilled', 'cancelled')),
    currency_code char(3) NOT NULL DEFAULT 'PKR',
    order_date date NOT NULL DEFAULT CURRENT_DATE,
    subtotal_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (subtotal_amount >= 0),
    tax_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    discount_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    total_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    note text,
    created_by uuid REFERENCES identity.users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, order_id),
    UNIQUE (tenant_id, company_id, order_number),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, customer_id)
        REFERENCES erp_sales.customer(tenant_id, company_id, customer_id),
    FOREIGN KEY (tenant_id, company_id, warehouse_id)
        REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE INDEX IF NOT EXISTS ix_sales_order_status ON erp_sales.sales_order(tenant_id, company_id, order_status);

CREATE TABLE IF NOT EXISTS erp_sales.sales_order_line (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    order_id uuid NOT NULL,
    line_number integer NOT NULL CHECK (line_number > 0),
    variant_id uuid NOT NULL,
    bin_id uuid NOT NULL,
    ordered_qty numeric(20,4) NOT NULL CHECK (ordered_qty > 0),
    fulfilled_qty numeric(20,4) NOT NULL DEFAULT 0 CHECK (fulfilled_qty >= 0),
    unit_of_measure_id uuid NOT NULL,
    unit_price numeric(20,4) NOT NULL CHECK (unit_price >= 0),
    tax_rate numeric(6,4) NOT NULL DEFAULT 0 CHECK (tax_rate >= 0),
    line_total numeric(20,4) NOT NULL CHECK (line_total >= 0),
    line_status text NOT NULL DEFAULT 'pending' CHECK (line_status IN ('pending', 'fulfilled', 'cancelled')),
    PRIMARY KEY (tenant_id, company_id, order_id, line_number),
    FOREIGN KEY (tenant_id, company_id, order_id)
        REFERENCES erp_sales.sales_order(tenant_id, company_id, order_id),
    FOREIGN KEY (tenant_id, variant_id)
        REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
    FOREIGN KEY (tenant_id, company_id, bin_id)
        REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id),
    FOREIGN KEY (tenant_id, unit_of_measure_id)
        REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id)
);

-- ── 3. Sales Invoices & POS Receipts ─────────────────────────────────────────

CREATE TABLE IF NOT EXISTS erp_sales.sales_invoice (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    invoice_id uuid NOT NULL DEFAULT gen_random_uuid(),
    invoice_number text NOT NULL,
    client_transaction_id uuid NOT NULL,
    order_id uuid,
    customer_id uuid NOT NULL,
    payment_method text NOT NULL DEFAULT 'cash' CHECK (payment_method IN ('cash', 'credit', 'bank_transfer', 'card', 'mobile_wallet')),
    invoice_status text NOT NULL DEFAULT 'posted' CHECK (invoice_status IN ('posted', 'paid', 'reversed')),
    invoice_date date NOT NULL DEFAULT CURRENT_DATE,
    subtotal_amount numeric(20,4) NOT NULL CHECK (subtotal_amount >= 0),
    tax_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    discount_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    total_amount numeric(20,4) NOT NULL CHECK (total_amount >= 0),
    paid_amount numeric(20,4) NOT NULL DEFAULT 0 CHECK (paid_amount >= 0),
    note text,
    created_by uuid REFERENCES identity.users(id),
    posted_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, invoice_id),
    UNIQUE (tenant_id, company_id, invoice_number),
    UNIQUE (tenant_id, client_transaction_id),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, customer_id)
        REFERENCES erp_sales.customer(tenant_id, company_id, customer_id)
);

CREATE INDEX IF NOT EXISTS ix_sales_invoice_status ON erp_sales.sales_invoice(tenant_id, company_id, invoice_status);

CREATE TABLE IF NOT EXISTS erp_sales.sales_invoice_line (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    invoice_id uuid NOT NULL,
    line_number integer NOT NULL CHECK (line_number > 0),
    order_line_number integer,
    variant_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    bin_id uuid NOT NULL,
    quantity numeric(20,4) NOT NULL CHECK (quantity > 0),
    unit_of_measure_id uuid NOT NULL,
    unit_price numeric(20,4) NOT NULL CHECK (unit_price >= 0),
    unit_cost numeric(20,4) NOT NULL DEFAULT 0 CHECK (unit_cost >= 0),
    tax_rate numeric(6,4) NOT NULL DEFAULT 0 CHECK (tax_rate >= 0),
    line_total numeric(20,4) NOT NULL CHECK (line_total >= 0),
    PRIMARY KEY (tenant_id, company_id, invoice_id, line_number),
    FOREIGN KEY (tenant_id, company_id, invoice_id)
        REFERENCES erp_sales.sales_invoice(tenant_id, company_id, invoice_id),
    FOREIGN KEY (tenant_id, variant_id)
        REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
    FOREIGN KEY (tenant_id, company_id, warehouse_id)
        REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id),
    FOREIGN KEY (tenant_id, company_id, bin_id)
        REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id),
    FOREIGN KEY (tenant_id, unit_of_measure_id)
        REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id)
);

-- ── 4. Outbox Events (Sales → Inventory Relief & General Ledger) ──────────────

CREATE TABLE IF NOT EXISTS erp_sales.outbox_event (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    event_id uuid NOT NULL DEFAULT gen_random_uuid(),
    invoice_id uuid NOT NULL,
    event_type text NOT NULL,
    payload jsonb NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    PRIMARY KEY (tenant_id, company_id, event_id),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, invoice_id)
        REFERENCES erp_sales.sales_invoice(tenant_id, company_id, invoice_id)
);

CREATE INDEX IF NOT EXISTS ix_sales_outbox_unpub ON erp_sales.outbox_event(occurred_at) WHERE published_at IS NULL;

-- ── 5. Row-Level Security ────────────────────────────────────────────────────

DO $$
DECLARE
    tbl text;
BEGIN
    FOR tbl IN SELECT tablename FROM pg_tables WHERE schemaname = 'erp_sales'
    LOOP
        EXECUTE format('ALTER TABLE erp_sales.%I ENABLE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('ALTER TABLE erp_sales.%I FORCE ROW LEVEL SECURITY;', tbl);
        EXECUTE format(
          $p$CREATE POLICY tenant_company_scope ON erp_sales.%I
             USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
             WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)$p$,
          tbl
        );
    END LOOP;
END $$;

-- ── 6. Permissions Grant ─────────────────────────────────────────────────────

GRANT USAGE ON SCHEMA erp_sales TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA erp_sales TO erp_app;
