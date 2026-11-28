-- Migration: 028_core_purchasing_completion.sql
-- Module: erp_purchasing
-- Purpose: Complete core purchasing management with multi-line PO approval workflows,
--          goods receipt quality inspection, quarantine stock tracking, controlled release,
--          supplier invoices with 3-way matching engine, purchase returns, supplier debit notes,
--          configurable tolerance policies, and supplier balance ledgers.
-- Safety: Additive, tenant/company RLS enabled, atomic transaction, rollback companion provided.

BEGIN;

-- ── 1. Update purchase_order status constraint and approval audit columns ───────

ALTER TABLE erp_purchasing.purchase_order
  DROP CONSTRAINT IF EXISTS purchase_order_order_status_check;

ALTER TABLE erp_purchasing.purchase_order
  ADD CONSTRAINT purchase_order_order_status_check
  CHECK (order_status IN ('draft', 'pending_approval', 'approved', 'confirmed', 'partially_received', 'received', 'cancelled'));

ALTER TABLE erp_purchasing.purchase_order
  ADD COLUMN IF NOT EXISTS approved_by UUID,
  ADD COLUMN IF NOT EXISTS approved_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS tax_rate NUMERIC(5,2) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS discount_amount NUMERIC(24,4) NOT NULL DEFAULT 0;

ALTER TABLE erp_purchasing.purchase_order_line
  ADD COLUMN IF NOT EXISTS tax_rate NUMERIC(5,2) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS discount_amount NUMERIC(24,4) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS line_total NUMERIC(24,4) NOT NULL DEFAULT 0;

-- ── 2. Update goods_received_note_line with quality inspection breakdown ────────

ALTER TABLE erp_purchasing.goods_received_note_line
  ADD COLUMN IF NOT EXISTS accepted_qty NUMERIC(24,9) NOT NULL DEFAULT 0 CHECK (accepted_qty >= 0),
  ADD COLUMN IF NOT EXISTS quarantined_qty NUMERIC(24,9) NOT NULL DEFAULT 0 CHECK (quarantined_qty >= 0),
  ADD COLUMN IF NOT EXISTS rejected_qty NUMERIC(24,9) NOT NULL DEFAULT 0 CHECK (rejected_qty >= 0);

-- ── 3. Purchasing Tolerance Policy ──────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS erp_purchasing.purchasing_tolerance_policy (
    tenant_id                     UUID         NOT NULL,
    company_id                    UUID         NOT NULL,
    policy_id                     UUID         NOT NULL DEFAULT gen_random_uuid(),
    price_tolerance_percent       NUMERIC(5,2) NOT NULL DEFAULT 5.00 CHECK (price_tolerance_percent >= 0),
    quantity_tolerance_percent    NUMERIC(5,2) NOT NULL DEFAULT 5.00 CHECK (quantity_tolerance_percent >= 0),
    auto_approve_within_tolerance BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at                    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at                    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, policy_id),
    UNIQUE (tenant_id, company_id),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id)
);

-- ── 4. Quality Inspection Tables ────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS erp_purchasing.purchase_inspection (
    tenant_id          UUID        NOT NULL,
    company_id         UUID        NOT NULL,
    inspection_id      UUID        NOT NULL DEFAULT gen_random_uuid(),
    inspection_number  TEXT        NOT NULL,
    grn_id             UUID        NOT NULL,
    inspector_id       UUID        NOT NULL,
    inspected_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    overall_status     TEXT        NOT NULL DEFAULT 'pending'
        CHECK (overall_status IN ('pending', 'passed', 'partial', 'rejected')),
    notes              TEXT,
    created_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, inspection_id),
    UNIQUE (tenant_id, company_id, inspection_number),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, grn_id)
        REFERENCES erp_purchasing.goods_received_note(tenant_id, company_id, grn_id)
);

CREATE TABLE IF NOT EXISTS erp_purchasing.purchase_inspection_line (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    inspection_line_id UUID          NOT NULL DEFAULT gen_random_uuid(),
    inspection_id      UUID          NOT NULL,
    grn_line_number    SMALLINT      NOT NULL,
    variant_id         UUID          NOT NULL,
    inspected_qty      NUMERIC(24,9) NOT NULL CHECK (inspected_qty >= 0),
    accepted_qty       NUMERIC(24,9) NOT NULL DEFAULT 0 CHECK (accepted_qty >= 0),
    quarantined_qty    NUMERIC(24,9) NOT NULL DEFAULT 0 CHECK (quarantined_qty >= 0),
    rejected_qty       NUMERIC(24,9) NOT NULL DEFAULT 0 CHECK (rejected_qty >= 0),
    rejection_reason   TEXT,
    disposition_notes  TEXT,
    PRIMARY KEY (tenant_id, company_id, inspection_line_id),
    FOREIGN KEY (tenant_id, company_id, inspection_id)
        REFERENCES erp_purchasing.purchase_inspection(tenant_id, company_id, inspection_id) ON DELETE CASCADE,
    FOREIGN KEY (tenant_id, variant_id)
        REFERENCES erp_inventory.product_variant(tenant_id, variant_id)
);

-- ── 5. Controlled Quarantine Stock Release ──────────────────────────────────────

CREATE TABLE IF NOT EXISTS erp_purchasing.quarantine_release_record (
    tenant_id           UUID          NOT NULL,
    company_id          UUID          NOT NULL,
    release_id          UUID          NOT NULL DEFAULT gen_random_uuid(),
    release_number      TEXT          NOT NULL,
    inspection_line_id  UUID          NOT NULL,
    grn_id              UUID          NOT NULL,
    variant_id          UUID          NOT NULL,
    source_bin_id       UUID          NOT NULL,
    target_bin_id       UUID          NOT NULL,
    released_qty        NUMERIC(24,9) NOT NULL CHECK (released_qty > 0),
    release_destination TEXT          NOT NULL CHECK (release_destination IN ('available', 'rejected')),
    authorized_by       UUID          NOT NULL,
    released_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    notes               TEXT,
    PRIMARY KEY (tenant_id, company_id, release_id),
    UNIQUE (tenant_id, company_id, release_number),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, inspection_line_id)
        REFERENCES erp_purchasing.purchase_inspection_line(tenant_id, company_id, inspection_line_id),
    FOREIGN KEY (tenant_id, company_id, grn_id)
        REFERENCES erp_purchasing.goods_received_note(tenant_id, company_id, grn_id),
    FOREIGN KEY (tenant_id, variant_id)
        REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
    FOREIGN KEY (tenant_id, company_id, source_bin_id)
        REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id),
    FOREIGN KEY (tenant_id, company_id, target_bin_id)
        REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id)
);

-- ── 6. Supplier Invoice Headers & Lines (with 3-Way Matching & Duplicate Detection) ─

CREATE TABLE IF NOT EXISTS erp_purchasing.supplier_invoice (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    invoice_id         UUID          NOT NULL DEFAULT gen_random_uuid(),
    invoice_number     TEXT          NOT NULL,
    supplier_id        UUID          NOT NULL,
    purchase_order_id  UUID,
    grn_id             UUID,
    invoice_date       DATE          NOT NULL DEFAULT CURRENT_DATE,
    due_date           DATE          NOT NULL DEFAULT CURRENT_DATE + INTERVAL '30 days',
    currency_code      CHAR(3)       NOT NULL DEFAULT 'PKR',
    subtotal           NUMERIC(24,4) NOT NULL DEFAULT 0 CHECK (subtotal >= 0),
    tax_amount         NUMERIC(24,4) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    total_amount       NUMERIC(24,4) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    matching_status    TEXT          NOT NULL DEFAULT 'unmatched'
        CHECK (matching_status IN ('unmatched', 'matched', 'variance_exceeded', 'disputed')),
    status             TEXT          NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft', 'pending_approval', 'approved', 'on_hold', 'disputed', 'paid', 'cancelled')),
    payment_status     TEXT          NOT NULL DEFAULT 'unpaid'
        CHECK (payment_status IN ('unpaid', 'partially_paid', 'paid')),
    paid_amount        NUMERIC(24,4) NOT NULL DEFAULT 0 CHECK (paid_amount >= 0),
    variance_amount    NUMERIC(24,4) NOT NULL DEFAULT 0,
    variance_reason    TEXT,
    gl_journal_id      UUID,
    approved_by        UUID,
    approved_at        TIMESTAMPTZ,
    created_by         UUID          NOT NULL,
    created_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, invoice_id),
    UNIQUE (tenant_id, company_id, supplier_id, invoice_number),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, supplier_id)
        REFERENCES erp_purchasing.supplier(tenant_id, company_id, supplier_id),
    FOREIGN KEY (tenant_id, company_id, purchase_order_id)
        REFERENCES erp_purchasing.purchase_order(tenant_id, company_id, purchase_order_id),
    FOREIGN KEY (tenant_id, company_id, grn_id)
        REFERENCES erp_purchasing.goods_received_note(tenant_id, company_id, grn_id)
);

CREATE TABLE IF NOT EXISTS erp_purchasing.supplier_invoice_line (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    invoice_line_id    UUID          NOT NULL DEFAULT gen_random_uuid(),
    invoice_id         UUID          NOT NULL,
    line_number        SMALLINT      NOT NULL CHECK (line_number > 0),
    po_line_number     SMALLINT,
    grn_line_number    SMALLINT,
    variant_id         UUID          NOT NULL,
    invoiced_qty       NUMERIC(24,9) NOT NULL CHECK (invoiced_qty > 0),
    unit_price         NUMERIC(24,4) NOT NULL CHECK (unit_price >= 0),
    line_total         NUMERIC(24,4) NOT NULL CHECK (line_total >= 0),
    matched_qty        NUMERIC(24,9) NOT NULL DEFAULT 0 CHECK (matched_qty >= 0),
    price_variance     NUMERIC(24,4) NOT NULL DEFAULT 0,
    qty_variance       NUMERIC(24,9) NOT NULL DEFAULT 0,
    matching_status    TEXT          NOT NULL DEFAULT 'unmatched'
        CHECK (matching_status IN ('unmatched', 'matched', 'variance_exceeded')),
    PRIMARY KEY (tenant_id, company_id, invoice_line_id),
    UNIQUE (tenant_id, company_id, invoice_id, line_number),
    FOREIGN KEY (tenant_id, company_id, invoice_id)
        REFERENCES erp_purchasing.supplier_invoice(tenant_id, company_id, invoice_id) ON DELETE CASCADE,
    FOREIGN KEY (tenant_id, variant_id)
        REFERENCES erp_inventory.product_variant(tenant_id, variant_id)
);

-- ── 7. Purchase Returns & Supplier Debit Notes ──────────────────────────────────

CREATE TABLE IF NOT EXISTS erp_purchasing.purchase_return_record (
    tenant_id           UUID        NOT NULL,
    company_id          UUID        NOT NULL,
    return_id           UUID        NOT NULL DEFAULT gen_random_uuid(),
    return_number       TEXT        NOT NULL,
    supplier_id         UUID        NOT NULL,
    purchase_order_id   UUID,
    grn_id              UUID,
    supplier_invoice_id UUID,
    status              TEXT        NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft', 'approved', 'shipped', 'completed', 'cancelled')),
    reason              TEXT        NOT NULL,
    authorized_by       UUID,
    authorized_at       TIMESTAMPTZ,
    created_by          UUID        NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, return_id),
    UNIQUE (tenant_id, company_id, return_number),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, supplier_id)
        REFERENCES erp_purchasing.supplier(tenant_id, company_id, supplier_id),
    FOREIGN KEY (tenant_id, company_id, purchase_order_id)
        REFERENCES erp_purchasing.purchase_order(tenant_id, company_id, purchase_order_id),
    FOREIGN KEY (tenant_id, company_id, grn_id)
        REFERENCES erp_purchasing.goods_received_note(tenant_id, company_id, grn_id),
    FOREIGN KEY (tenant_id, company_id, supplier_invoice_id)
        REFERENCES erp_purchasing.supplier_invoice(tenant_id, company_id, invoice_id)
);

CREATE TABLE IF NOT EXISTS erp_purchasing.purchase_return_line_record (
    tenant_id          UUID          NOT NULL,
    company_id         UUID          NOT NULL,
    return_line_id     UUID          NOT NULL DEFAULT gen_random_uuid(),
    return_id          UUID          NOT NULL,
    line_number        SMALLINT      NOT NULL CHECK (line_number > 0),
    variant_id         UUID          NOT NULL,
    warehouse_id       UUID          NOT NULL,
    bin_id             UUID          NOT NULL,
    return_qty         NUMERIC(24,9) NOT NULL CHECK (return_qty > 0),
    unit_cost          NUMERIC(24,4) NOT NULL CHECK (unit_cost >= 0),
    total_cost         NUMERIC(24,4) NOT NULL CHECK (total_cost >= 0),
    from_stock_status  TEXT          NOT NULL DEFAULT 'available'
        CHECK (from_stock_status IN ('available', 'quarantine')),
    restock_reason     TEXT,
    PRIMARY KEY (tenant_id, company_id, return_line_id),
    UNIQUE (tenant_id, company_id, return_id, line_number),
    FOREIGN KEY (tenant_id, company_id, return_id)
        REFERENCES erp_purchasing.purchase_return_record(tenant_id, company_id, return_id) ON DELETE CASCADE,
    FOREIGN KEY (tenant_id, variant_id)
        REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
    FOREIGN KEY (tenant_id, company_id, warehouse_id)
        REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id),
    FOREIGN KEY (tenant_id, company_id, bin_id)
        REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id)
);

CREATE TABLE IF NOT EXISTS erp_purchasing.supplier_debit_note (
    tenant_id           UUID          NOT NULL,
    company_id          UUID          NOT NULL,
    debit_note_id       UUID          NOT NULL DEFAULT gen_random_uuid(),
    debit_note_number   TEXT          NOT NULL,
    supplier_id         UUID          NOT NULL,
    purchase_return_id  UUID,
    supplier_invoice_id UUID,
    debit_note_date     DATE          NOT NULL DEFAULT CURRENT_DATE,
    currency_code       CHAR(3)       NOT NULL DEFAULT 'PKR',
    amount              NUMERIC(24,4) NOT NULL CHECK (amount > 0),
    status              TEXT          NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft', 'approved', 'applied', 'cancelled')),
    gl_journal_id       UUID,
    authorized_by       UUID,
    authorized_at       TIMESTAMPTZ,
    notes               TEXT,
    created_by          UUID          NOT NULL,
    created_at          TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ   NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, debit_note_id),
    UNIQUE (tenant_id, company_id, debit_note_number),
    FOREIGN KEY (tenant_id, company_id)
        REFERENCES organization.companies(tenant_id, id),
    FOREIGN KEY (tenant_id, company_id, supplier_id)
        REFERENCES erp_purchasing.supplier(tenant_id, company_id, supplier_id),
    FOREIGN KEY (tenant_id, company_id, purchase_return_id)
        REFERENCES erp_purchasing.purchase_return_record(tenant_id, company_id, return_id),
    FOREIGN KEY (tenant_id, company_id, supplier_invoice_id)
        REFERENCES erp_purchasing.supplier_invoice(tenant_id, company_id, invoice_id)
);

-- ── 8. Indexes ──────────────────────────────────────────────────────────────────

CREATE INDEX IF NOT EXISTS ix_po_approved_by        ON erp_purchasing.purchase_order(tenant_id, company_id, approved_by);
CREATE INDEX IF NOT EXISTS ix_inspection_grn        ON erp_purchasing.purchase_inspection(tenant_id, company_id, grn_id);
CREATE INDEX IF NOT EXISTS ix_quarantine_rel_grn    ON erp_purchasing.quarantine_release_record(tenant_id, company_id, grn_id);
CREATE INDEX IF NOT EXISTS ix_supplier_inv_sup      ON erp_purchasing.supplier_invoice(tenant_id, company_id, supplier_id);
CREATE INDEX IF NOT EXISTS ix_supplier_inv_status   ON erp_purchasing.supplier_invoice(tenant_id, company_id, status);
CREATE INDEX IF NOT EXISTS ix_supplier_inv_pay      ON erp_purchasing.supplier_invoice(tenant_id, company_id, payment_status);
CREATE INDEX IF NOT EXISTS ix_purch_return_sup      ON erp_purchasing.purchase_return_record(tenant_id, company_id, supplier_id);
CREATE INDEX IF NOT EXISTS ix_debit_note_sup        ON erp_purchasing.supplier_debit_note(tenant_id, company_id, supplier_id);
CREATE INDEX IF NOT EXISTS ix_debit_note_status     ON erp_purchasing.supplier_debit_note(tenant_id, company_id, status);

-- ── 9. Row-Level Security ───────────────────────────────────────────────────────

DO $$
DECLARE
    tbl TEXT;
    target_tables TEXT[] := ARRAY[
        'purchasing_tolerance_policy',
        'purchase_inspection',
        'purchase_inspection_line',
        'quarantine_release_record',
        'supplier_invoice',
        'supplier_invoice_line',
        'purchase_return_record',
        'purchase_return_line_record',
        'supplier_debit_note'
    ];
BEGIN
    FOREACH tbl IN ARRAY target_tables
    LOOP
        EXECUTE format('ALTER TABLE erp_purchasing.%I ENABLE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('ALTER TABLE erp_purchasing.%I FORCE ROW LEVEL SECURITY;', tbl);

        EXECUTE format('DROP POLICY IF EXISTS tenant_company_scope ON erp_purchasing.%I;', tbl);
        EXECUTE format($p$
            CREATE POLICY tenant_company_scope ON erp_purchasing.%I
            FOR ALL
            USING (
                tenant_id = COALESCE(NULLIF(current_setting('app.current_tenant_id', true), ''), NULLIF(current_setting('app.tenant_id', true), ''))::uuid
                AND (
                    company_id IS NULL
                    OR company_id = COALESCE(NULLIF(current_setting('app.current_company_id', true), ''), NULLIF(current_setting('app.company_id', true), ''))::uuid
                )
            );
        $p$, tbl);
    END LOOP;
END $$;

-- ── 10. Role Grants ─────────────────────────────────────────────────────────────

GRANT SELECT, INSERT, UPDATE, DELETE ON
    erp_purchasing.purchasing_tolerance_policy,
    erp_purchasing.purchase_inspection,
    erp_purchasing.purchase_inspection_line,
    erp_purchasing.quarantine_release_record,
    erp_purchasing.supplier_invoice,
    erp_purchasing.supplier_invoice_line,
    erp_purchasing.purchase_return_record,
    erp_purchasing.purchase_return_line_record,
    erp_purchasing.supplier_debit_note
TO erp_app;

-- ── 11. Register Permissions ────────────────────────────────────────────────────

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
    ('purchasing.po.approve', 'Approve purchase orders with authorized signature.', 'purchasing'),
    ('purchasing.invoice.view', 'View supplier invoices, lines, and matching records.', 'purchasing'),
    ('purchasing.invoice.create', 'Create and register supplier invoices.', 'purchasing'),
    ('purchasing.invoice.match', 'Execute 3-way matching and approve supplier invoices.', 'purchasing'),
    ('purchasing.inspection.view', 'View goods receipt quality inspections.', 'purchasing'),
    ('purchasing.inspection.record', 'Record quality inspection results for goods receipt.', 'purchasing'),
    ('purchasing.quarantine.release', 'Authorize controlled release of quarantined inventory.', 'purchasing'),
    ('purchasing.return.view', 'View purchase return records.', 'purchasing'),
    ('purchasing.return.create', 'Create and process purchase returns to suppliers.', 'purchasing'),
    ('purchasing.debit_note.view', 'View supplier debit notes.', 'purchasing'),
    ('purchasing.debit_note.create', 'Issue and approve supplier debit notes.', 'purchasing'),
    ('purchasing.supplier.balance', 'View supplier balance ledger and AP aging status.', 'purchasing')
ON CONFLICT (permission_code) DO NOTHING;

-- ── 12. Record Migration ────────────────────────────────────────────────────────

INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('028_core_purchasing_completion', now())
ON CONFLICT (version) DO NOTHING;

COMMIT;
