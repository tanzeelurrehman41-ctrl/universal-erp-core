-- ==============================================================================
-- Migration 029: Core Sales & Order-to-Cash Completion (Phase 7)
-- ==============================================================================

BEGIN;

-- ── 1. Update sales_order status constraint and add approval / quotation links ───

ALTER TABLE erp_sales.sales_order
  DROP CONSTRAINT IF EXISTS sales_order_order_status_check;

ALTER TABLE erp_sales.sales_order
  ADD CONSTRAINT sales_order_order_status_check
  CHECK (order_status IN ('draft', 'pending_approval', 'approved', 'confirmed', 'partially_fulfilled', 'fulfilled', 'cancelled'));

ALTER TABLE erp_sales.sales_order
  ADD COLUMN IF NOT EXISTS quotation_id UUID,
  ADD COLUMN IF NOT EXISTS approved_by UUID REFERENCES identity.users(id),
  ADD COLUMN IF NOT EXISTS approved_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS revision_number INT NOT NULL DEFAULT 1,
  ADD COLUMN IF NOT EXISTS cancellation_reason TEXT,
  ADD COLUMN IF NOT EXISTS cancelled_at TIMESTAMPTZ;

-- ── 2. Update sales_invoice status constraint and add dispatch / due_date ─────

ALTER TABLE erp_sales.sales_invoice
  DROP CONSTRAINT IF EXISTS sales_invoice_invoice_status_check;

ALTER TABLE erp_sales.sales_invoice
  ADD CONSTRAINT sales_invoice_invoice_status_check
  CHECK (invoice_status IN ('draft', 'posted', 'partially_paid', 'paid', 'reversed', 'cancelled'));

ALTER TABLE erp_sales.sales_invoice
  ADD COLUMN IF NOT EXISTS dispatch_id UUID,
  ADD COLUMN IF NOT EXISTS due_date DATE NOT NULL DEFAULT (CURRENT_DATE + INTERVAL '30 days'),
  ADD COLUMN IF NOT EXISTS gl_journal_id UUID;

-- ── 3. Sales Quotation & Quotation Lines ───────────────────────────────────────

CREATE TABLE IF NOT EXISTS erp_sales.sales_quotation (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    quotation_id       UUID          NOT NULL DEFAULT gen_random_uuid(),
    quotation_number   TEXT          NOT NULL,
    customer_id        UUID          NOT NULL,
    warehouse_id       UUID          NOT NULL,
    quotation_date     DATE          NOT NULL DEFAULT CURRENT_DATE,
    valid_until        DATE          NOT NULL DEFAULT (CURRENT_DATE + INTERVAL '15 days'),
    status             TEXT          NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft', 'pending_approval', 'approved', 'converted', 'expired', 'rejected')),
    currency_code      CHAR(3)       NOT NULL DEFAULT 'PKR',
    subtotal_amount    NUMERIC(20,4) NOT NULL DEFAULT 0 CHECK (subtotal_amount >= 0),
    tax_amount         NUMERIC(20,4) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    discount_amount    NUMERIC(20,4) NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    total_amount       NUMERIC(20,4) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    note               TEXT,
    approved_by        UUID          REFERENCES identity.users(id),
    approved_at        TIMESTAMPTZ,
    converted_order_id UUID,
    created_by         UUID          REFERENCES identity.users(id),
    created_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, quotation_id),
    UNIQUE (tenant_id, company_id, quotation_number),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, customer_id)
        REFERENCES erp_sales.customer(tenant_id, company_id, customer_id),
    FOREIGN KEY (tenant_id, company_id, warehouse_id)
        REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE TABLE IF NOT EXISTS erp_sales.sales_quotation_line (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    quotation_id       UUID          NOT NULL,
    line_number        INT           NOT NULL CHECK (line_number > 0),
    variant_id         UUID          NOT NULL,
    bin_id             UUID          NOT NULL,
    requested_qty      NUMERIC(20,4) NOT NULL CHECK (requested_qty > 0),
    unit_of_measure_id UUID          NOT NULL,
    unit_price         NUMERIC(20,4) NOT NULL CHECK (unit_price >= 0),
    tax_rate           NUMERIC(6,4)  NOT NULL DEFAULT 0 CHECK (tax_rate >= 0),
    discount_percent   NUMERIC(5,2)  NOT NULL DEFAULT 0 CHECK (discount_percent >= 0 AND discount_percent <= 100),
    line_total         NUMERIC(20,4) NOT NULL CHECK (line_total >= 0),
    PRIMARY KEY (tenant_id, company_id, quotation_id, line_number),
    FOREIGN KEY (tenant_id, company_id, quotation_id)
        REFERENCES erp_sales.sales_quotation(tenant_id, company_id, quotation_id) ON DELETE CASCADE,
    FOREIGN KEY (tenant_id, variant_id)
        REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
    FOREIGN KEY (tenant_id, unit_of_measure_id)
        REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id)
);

-- ── 4. Sales Dispatch & Dispatch Lines (Fulfillment / Goods Issue) ─────────────

CREATE TABLE IF NOT EXISTS erp_sales.sales_dispatch (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    dispatch_id        UUID          NOT NULL DEFAULT gen_random_uuid(),
    dispatch_number    TEXT          NOT NULL,
    order_id           UUID          NOT NULL,
    customer_id        UUID          NOT NULL,
    warehouse_id       UUID          NOT NULL,
    dispatch_date      DATE          NOT NULL DEFAULT CURRENT_DATE,
    carrier            TEXT,
    tracking_number    TEXT,
    status             TEXT          NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft', 'dispatched', 'delivered', 'cancelled')),
    note               TEXT,
    gl_journal_id      UUID,
    movement_id        UUID,
    dispatched_by      UUID          REFERENCES identity.users(id),
    dispatched_at      TIMESTAMPTZ,
    delivered_at       TIMESTAMPTZ,
    created_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, dispatch_id),
    UNIQUE (tenant_id, company_id, dispatch_number),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, order_id)
        REFERENCES erp_sales.sales_order(tenant_id, company_id, order_id),
    FOREIGN KEY (tenant_id, company_id, customer_id)
        REFERENCES erp_sales.customer(tenant_id, company_id, customer_id),
    FOREIGN KEY (tenant_id, company_id, warehouse_id)
        REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE TABLE IF NOT EXISTS erp_sales.sales_dispatch_line (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    dispatch_id        UUID          NOT NULL,
    line_number        INT           NOT NULL CHECK (line_number > 0),
    order_line_number  INT           NOT NULL,
    variant_id         UUID          NOT NULL,
    bin_id             UUID          NOT NULL,
    dispatched_qty     NUMERIC(20,4) NOT NULL CHECK (dispatched_qty > 0),
    unit_of_measure_id UUID          NOT NULL,
    unit_cost          NUMERIC(20,4) NOT NULL DEFAULT 0 CHECK (unit_cost >= 0),
    total_cost         NUMERIC(20,4) NOT NULL DEFAULT 0 CHECK (total_cost >= 0),
    PRIMARY KEY (tenant_id, company_id, dispatch_id, line_number),
    FOREIGN KEY (tenant_id, company_id, dispatch_id)
        REFERENCES erp_sales.sales_dispatch(tenant_id, company_id, dispatch_id) ON DELETE CASCADE,
    FOREIGN KEY (tenant_id, variant_id)
        REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
    FOREIGN KEY (tenant_id, unit_of_measure_id)
        REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id),
    FOREIGN KEY (tenant_id, company_id, bin_id)
        REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id)
);

-- ── 5. Customer Receipts & Payment Allocations ─────────────────────────────────

CREATE TABLE IF NOT EXISTS erp_sales.customer_receipt (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    receipt_id         UUID          NOT NULL DEFAULT gen_random_uuid(),
    receipt_number     TEXT          NOT NULL,
    customer_id        UUID          NOT NULL,
    receipt_date       DATE          NOT NULL DEFAULT CURRENT_DATE,
    payment_method     TEXT          NOT NULL
        CHECK (payment_method IN ('cash', 'bank_transfer', 'cheque', 'card', 'mobile_wallet')),
    amount             NUMERIC(20,4) NOT NULL CHECK (amount > 0),
    unallocated_amount NUMERIC(20,4) NOT NULL DEFAULT 0 CHECK (unallocated_amount >= 0),
    reference_number   TEXT,
    status             TEXT          NOT NULL DEFAULT 'posted'
        CHECK (status IN ('draft', 'posted', 'cancelled')),
    gl_journal_id      UUID,
    note               TEXT,
    received_by        UUID          REFERENCES identity.users(id),
    created_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, receipt_id),
    UNIQUE (tenant_id, company_id, receipt_number),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, customer_id)
        REFERENCES erp_sales.customer(tenant_id, company_id, customer_id)
);

CREATE TABLE IF NOT EXISTS erp_sales.customer_receipt_allocation (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    allocation_id      UUID          NOT NULL DEFAULT gen_random_uuid(),
    receipt_id         UUID          NOT NULL,
    invoice_id         UUID          NOT NULL,
    allocated_amount   NUMERIC(20,4) NOT NULL CHECK (allocated_amount > 0),
    allocated_at       TIMESTAMPTZ   NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, allocation_id),
    UNIQUE (tenant_id, company_id, receipt_id, invoice_id),
    FOREIGN KEY (tenant_id, company_id, receipt_id)
        REFERENCES erp_sales.customer_receipt(tenant_id, company_id, receipt_id) ON DELETE CASCADE,
    FOREIGN KEY (tenant_id, company_id, invoice_id)
        REFERENCES erp_sales.sales_invoice(tenant_id, company_id, invoice_id)
);

-- ── 6. Sales Returns & Customer Credit Notes ───────────────────────────────────

CREATE TABLE IF NOT EXISTS erp_sales.sales_return (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    return_id          UUID          NOT NULL DEFAULT gen_random_uuid(),
    return_number      TEXT          NOT NULL,
    customer_id        UUID          NOT NULL,
    invoice_id         UUID,
    order_id           UUID,
    return_date        DATE          NOT NULL DEFAULT CURRENT_DATE,
    total_amount       NUMERIC(20,4) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    total_cost         NUMERIC(20,4) NOT NULL DEFAULT 0 CHECK (total_cost >= 0),
    status             TEXT          NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft', 'approved', 'rejected', 'completed')),
    reason             TEXT,
    authorized_by      UUID          REFERENCES identity.users(id),
    authorized_at      TIMESTAMPTZ,
    gl_journal_id      UUID,
    created_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, return_id),
    UNIQUE (tenant_id, company_id, return_number),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, customer_id)
        REFERENCES erp_sales.customer(tenant_id, company_id, customer_id)
);

CREATE TABLE IF NOT EXISTS erp_sales.sales_return_line (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    return_id          UUID          NOT NULL,
    line_number        INT           NOT NULL CHECK (line_number > 0),
    variant_id         UUID          NOT NULL,
    warehouse_id       UUID          NOT NULL,
    bin_id             UUID          NOT NULL,
    returned_qty       NUMERIC(20,4) NOT NULL CHECK (returned_qty > 0),
    unit_price         NUMERIC(20,4) NOT NULL CHECK (unit_price >= 0),
    unit_cost          NUMERIC(20,4) NOT NULL DEFAULT 0 CHECK (unit_cost >= 0),
    line_total         NUMERIC(20,4) NOT NULL CHECK (line_total >= 0),
    reason             TEXT,
    PRIMARY KEY (tenant_id, company_id, return_id, line_number),
    FOREIGN KEY (tenant_id, company_id, return_id)
        REFERENCES erp_sales.sales_return(tenant_id, company_id, return_id) ON DELETE CASCADE,
    FOREIGN KEY (tenant_id, variant_id)
        REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
    FOREIGN KEY (tenant_id, company_id, bin_id)
        REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id),
    FOREIGN KEY (tenant_id, company_id, warehouse_id)
        REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE TABLE IF NOT EXISTS erp_sales.sales_credit_note (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    credit_note_id     UUID          NOT NULL DEFAULT gen_random_uuid(),
    credit_note_number TEXT          NOT NULL,
    customer_id        UUID          NOT NULL,
    return_id          UUID,
    invoice_id         UUID,
    credit_note_date   DATE          NOT NULL DEFAULT CURRENT_DATE,
    currency_code      CHAR(3)       NOT NULL DEFAULT 'PKR',
    subtotal_amount    NUMERIC(20,4) NOT NULL CHECK (subtotal_amount >= 0),
    tax_amount         NUMERIC(20,4) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    total_amount       NUMERIC(20,4) NOT NULL CHECK (total_amount > 0),
    status             TEXT          NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft', 'approved', 'applied', 'refunded', 'cancelled')),
    gl_journal_id      UUID,
    reason             TEXT,
    authorized_by      UUID          REFERENCES identity.users(id),
    authorized_at      TIMESTAMPTZ,
    created_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, credit_note_id),
    UNIQUE (tenant_id, company_id, credit_note_number),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, customer_id)
        REFERENCES erp_sales.customer(tenant_id, company_id, customer_id)
);

-- ── 7. Performance Indexes ─────────────────────────────────────────────────────

CREATE INDEX IF NOT EXISTS ix_sales_quotation_customer
    ON erp_sales.sales_quotation (tenant_id, company_id, customer_id, status);

CREATE INDEX IF NOT EXISTS ix_sales_dispatch_order
    ON erp_sales.sales_dispatch (tenant_id, company_id, order_id, status);

CREATE INDEX IF NOT EXISTS ix_sales_dispatch_customer
    ON erp_sales.sales_dispatch (tenant_id, company_id, customer_id, dispatch_date);

CREATE INDEX IF NOT EXISTS ix_customer_receipt_customer
    ON erp_sales.customer_receipt (tenant_id, company_id, customer_id, receipt_date);

CREATE INDEX IF NOT EXISTS ix_sales_return_customer
    ON erp_sales.sales_return (tenant_id, company_id, customer_id, return_date);

CREATE INDEX IF NOT EXISTS ix_sales_credit_note_customer
    ON erp_sales.sales_credit_note (tenant_id, company_id, customer_id, credit_note_date);

CREATE INDEX IF NOT EXISTS ix_sales_invoice_due_date
    ON erp_sales.sales_invoice (tenant_id, company_id, customer_id, due_date, invoice_status);

-- ── 8. Row Level Security Policies ─────────────────────────────────────────────

ALTER TABLE erp_sales.sales_quotation ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_sales.sales_quotation FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON erp_sales.sales_quotation;
CREATE POLICY tenant_company_scope ON erp_sales.sales_quotation
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE erp_sales.sales_quotation_line ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_sales.sales_quotation_line FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON erp_sales.sales_quotation_line;
CREATE POLICY tenant_company_scope ON erp_sales.sales_quotation_line
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE erp_sales.sales_dispatch ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_sales.sales_dispatch FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON erp_sales.sales_dispatch;
CREATE POLICY tenant_company_scope ON erp_sales.sales_dispatch
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE erp_sales.sales_dispatch_line ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_sales.sales_dispatch_line FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON erp_sales.sales_dispatch_line;
CREATE POLICY tenant_company_scope ON erp_sales.sales_dispatch_line
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE erp_sales.customer_receipt ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_sales.customer_receipt FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON erp_sales.customer_receipt;
CREATE POLICY tenant_company_scope ON erp_sales.customer_receipt
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE erp_sales.customer_receipt_allocation ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_sales.customer_receipt_allocation FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON erp_sales.customer_receipt_allocation;
CREATE POLICY tenant_company_scope ON erp_sales.customer_receipt_allocation
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE erp_sales.sales_return ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_sales.sales_return FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON erp_sales.sales_return;
CREATE POLICY tenant_company_scope ON erp_sales.sales_return
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE erp_sales.sales_return_line ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_sales.sales_return_line FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON erp_sales.sales_return_line;
CREATE POLICY tenant_company_scope ON erp_sales.sales_return_line
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

ALTER TABLE erp_sales.sales_credit_note ENABLE ROW LEVEL SECURITY;
ALTER TABLE erp_sales.sales_credit_note FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON erp_sales.sales_credit_note;
CREATE POLICY tenant_company_scope ON erp_sales.sales_credit_note
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid
    );

-- ── 9. Grant Permissions to erp_app ───────────────────────────────────────────

GRANT SELECT, INSERT, UPDATE, DELETE ON erp_sales.sales_quotation TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_sales.sales_quotation_line TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_sales.sales_dispatch TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_sales.sales_dispatch_line TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_sales.customer_receipt TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_sales.customer_receipt_allocation TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_sales.sales_return TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_sales.sales_return_line TO erp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON erp_sales.sales_credit_note TO erp_app;

-- ── 10. Register Authorization Permissions ─────────────────────────────────────

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
    ('sales.quotation.create', 'Draft customer quotations', 'sales'),
    ('sales.quotation.approve', 'Approve customer quotations and price discounts', 'sales'),
    ('sales.order.approve', 'Formally approve sales orders and credit limit overrides', 'sales'),
    ('sales.dispatch.create', 'Generate warehouse pick lists and dispatches', 'sales'),
    ('sales.dispatch.confirm', 'Confirm dispatch and trigger COGS/inventory relief', 'sales'),
    ('sales.receipt.create', 'Record customer payments and cash receipts', 'sales'),
    ('sales.receipt.allocate', 'Allocate customer payments against open invoices', 'sales'),
    ('sales.return.create', 'Create customer sales return requests', 'sales'),
    ('sales.return.approve', 'Approve sales returns and restock goods', 'sales'),
    ('sales.creditnote.create', 'Issue customer credit notes', 'sales'),
    ('sales.creditnote.approve', 'Approve credit notes and post AR/Revenue reversal', 'sales'),
    ('sales.receivable.view', 'View accounts receivable subledger, aging, and customer statements', 'sales')
ON CONFLICT (permission_code) DO UPDATE
SET description = EXCLUDED.description;

-- ── 11. Migration Registry ─────────────────────────────────────────────────────

INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('029_core_sales_completion', now())
ON CONFLICT (version) DO UPDATE SET applied_at = now();

COMMIT;
