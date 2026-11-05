-- Migration 011 – Purchasing module foundation
-- Suppliers, purchase orders, order lines, goods received notes (GRNs),
-- and GRN lines. GRN posting raises an outbox event consumed by inventory.
-- All tables are tenant+company scoped with RLS identical to the inventory schema.

CREATE SCHEMA IF NOT EXISTS erp_purchasing;

-- ── Suppliers ─────────────────────────────────────────────────────────────────

CREATE TABLE erp_purchasing.supplier (
  tenant_id   UUID    NOT NULL,
  company_id  UUID    NOT NULL,
  supplier_id UUID    NOT NULL DEFAULT gen_random_uuid(),
  supplier_code TEXT  NOT NULL,
  name          TEXT  NOT NULL,
  contact_name  TEXT,
  email         TEXT,
  phone         TEXT,
  address       TEXT,
  currency_code CHAR(3) NOT NULL DEFAULT 'PKR',
  payment_terms_days SMALLINT NOT NULL DEFAULT 30 CHECK (payment_terms_days >= 0),
  is_active     BOOLEAN NOT NULL DEFAULT TRUE,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, supplier_id),
  UNIQUE (tenant_id, company_id, supplier_code),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id)
);

-- ── Purchase Orders ───────────────────────────────────────────────────────────

CREATE TABLE erp_purchasing.purchase_order (
  tenant_id        UUID    NOT NULL,
  company_id       UUID    NOT NULL,
  purchase_order_id UUID   NOT NULL DEFAULT gen_random_uuid(),
  po_number        TEXT    NOT NULL,
  supplier_id      UUID    NOT NULL,
  warehouse_id     UUID    NOT NULL,   -- receiving warehouse
  order_status     TEXT    NOT NULL DEFAULT 'draft'
    CHECK (order_status IN ('draft','confirmed','partially_received','received','cancelled')),
  currency_code    CHAR(3) NOT NULL DEFAULT 'PKR',
  order_date       DATE    NOT NULL DEFAULT CURRENT_DATE,
  expected_date    DATE,
  note             TEXT,
  created_by       UUID    NOT NULL,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, purchase_order_id),
  UNIQUE (tenant_id, company_id, po_number),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, company_id, supplier_id)
    REFERENCES erp_purchasing.supplier(tenant_id, company_id, supplier_id),
  FOREIGN KEY (tenant_id, company_id, warehouse_id)
    REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE TABLE erp_purchasing.purchase_order_line (
  tenant_id         UUID       NOT NULL,
  company_id        UUID       NOT NULL,
  purchase_order_id UUID       NOT NULL,
  line_number       SMALLINT   NOT NULL CHECK (line_number > 0),
  variant_id        UUID       NOT NULL,
  ordered_qty       NUMERIC(24,9) NOT NULL CHECK (ordered_qty > 0),
  received_qty      NUMERIC(24,9) NOT NULL DEFAULT 0 CHECK (received_qty >= 0),
  unit_of_measure_id UUID      NOT NULL,
  unit_price        NUMERIC(24,4) NOT NULL DEFAULT 0 CHECK (unit_price >= 0),
  line_status       TEXT       NOT NULL DEFAULT 'open'
    CHECK (line_status IN ('open','partially_received','received','cancelled')),
  PRIMARY KEY (tenant_id, company_id, purchase_order_id, line_number),
  FOREIGN KEY (tenant_id, company_id, purchase_order_id)
    REFERENCES erp_purchasing.purchase_order(tenant_id, company_id, purchase_order_id),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
  FOREIGN KEY (tenant_id, unit_of_measure_id)
    REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id)
);

-- ── Goods Received Notes ──────────────────────────────────────────────────────

CREATE TABLE erp_purchasing.goods_received_note (
  tenant_id             UUID    NOT NULL,
  company_id            UUID    NOT NULL,
  grn_id                UUID    NOT NULL DEFAULT gen_random_uuid(),
  client_transaction_id UUID    NOT NULL,   -- idempotency; maps to inventory movement
  grn_number            TEXT    NOT NULL,
  purchase_order_id     UUID    NOT NULL,
  supplier_id           UUID    NOT NULL,
  warehouse_id          UUID    NOT NULL,
  grn_status            TEXT    NOT NULL DEFAULT 'draft'
    CHECK (grn_status IN ('draft','posted','reversed')),
  received_date         DATE    NOT NULL DEFAULT CURRENT_DATE,
  supplier_reference    TEXT,
  note                  TEXT,
  created_by            UUID    NOT NULL,
  posted_at             TIMESTAMPTZ,
  created_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, grn_id),
  UNIQUE (tenant_id, company_id, grn_number),
  UNIQUE (tenant_id, client_transaction_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, company_id, purchase_order_id)
    REFERENCES erp_purchasing.purchase_order(tenant_id, company_id, purchase_order_id),
  FOREIGN KEY (tenant_id, company_id, supplier_id)
    REFERENCES erp_purchasing.supplier(tenant_id, company_id, supplier_id),
  FOREIGN KEY (tenant_id, company_id, warehouse_id)
    REFERENCES erp_inventory.warehouse(tenant_id, company_id, warehouse_id)
);

CREATE TABLE erp_purchasing.goods_received_note_line (
  tenant_id    UUID       NOT NULL,
  company_id   UUID       NOT NULL,
  grn_id       UUID       NOT NULL,
  line_number  SMALLINT   NOT NULL CHECK (line_number > 0),
  po_line_number SMALLINT NOT NULL,
  purchase_order_id UUID  NOT NULL,
  variant_id   UUID       NOT NULL,
  bin_id       UUID       NOT NULL,
  received_qty NUMERIC(24,9) NOT NULL CHECK (received_qty > 0),
  unit_of_measure_id UUID NOT NULL,
  unit_price   NUMERIC(24,4) NOT NULL DEFAULT 0 CHECK (unit_price >= 0),
  PRIMARY KEY (tenant_id, company_id, grn_id, line_number),
  FOREIGN KEY (tenant_id, company_id, grn_id)
    REFERENCES erp_purchasing.goods_received_note(tenant_id, company_id, grn_id),
  FOREIGN KEY (tenant_id, company_id, purchase_order_id, po_line_number)
    REFERENCES erp_purchasing.purchase_order_line(tenant_id, company_id, purchase_order_id, line_number),
  FOREIGN KEY (tenant_id, variant_id)
    REFERENCES erp_inventory.product_variant(tenant_id, variant_id),
  FOREIGN KEY (tenant_id, company_id, bin_id)
    REFERENCES erp_inventory.stock_bin(tenant_id, company_id, bin_id),
  FOREIGN KEY (tenant_id, unit_of_measure_id)
    REFERENCES erp_inventory.unit_of_measure(tenant_id, uom_id)
);

-- ── Outbox (purchasing → inventory events) ───────────────────────────────────

CREATE TABLE erp_purchasing.outbox_event (
  tenant_id    UUID    NOT NULL,
  company_id   UUID    NOT NULL,
  event_id     UUID    NOT NULL DEFAULT gen_random_uuid(),
  grn_id       UUID    NOT NULL,
  event_type   TEXT    NOT NULL,
  payload      JSONB   NOT NULL,
  occurred_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  published_at TIMESTAMPTZ,
  attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
  PRIMARY KEY (tenant_id, company_id, event_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  FOREIGN KEY (tenant_id, company_id, grn_id)
    REFERENCES erp_purchasing.goods_received_note(tenant_id, company_id, grn_id)
);

-- ── Indexes ───────────────────────────────────────────────────────────────────

CREATE INDEX ix_po_supplier   ON erp_purchasing.purchase_order(tenant_id, company_id, supplier_id);
CREATE INDEX ix_po_status     ON erp_purchasing.purchase_order(tenant_id, company_id, order_status);
CREATE INDEX ix_grn_po        ON erp_purchasing.goods_received_note(tenant_id, company_id, purchase_order_id);
CREATE INDEX ix_grn_status    ON erp_purchasing.goods_received_note(tenant_id, company_id, grn_status);
CREATE INDEX ix_outbox_unpub  ON erp_purchasing.outbox_event(occurred_at) WHERE published_at IS NULL;

-- ── Row-Level Security ────────────────────────────────────────────────────────

DO $$
DECLARE tname TEXT;
BEGIN
  FOREACH tname IN ARRAY ARRAY[
    'supplier','purchase_order','purchase_order_line',
    'goods_received_note','goods_received_note_line','outbox_event'
  ] LOOP
    EXECUTE format('ALTER TABLE erp_purchasing.%I ENABLE ROW LEVEL SECURITY', tname);
    EXECUTE format('ALTER TABLE erp_purchasing.%I FORCE ROW LEVEL SECURITY', tname);
    EXECUTE format(
      $p$CREATE POLICY tenant_company_scope ON erp_purchasing.%I
         USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
         WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
            AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)$p$,
      tname
    );
  END LOOP;
END $$;

-- ── Grants ────────────────────────────────────────────────────────────────────

GRANT USAGE ON SCHEMA erp_purchasing TO erp_app;
GRANT SELECT, INSERT, UPDATE ON
  erp_purchasing.supplier,
  erp_purchasing.purchase_order,
  erp_purchasing.purchase_order_line,
  erp_purchasing.goods_received_note,
  erp_purchasing.goods_received_note_line,
  erp_purchasing.outbox_event
TO erp_app;
