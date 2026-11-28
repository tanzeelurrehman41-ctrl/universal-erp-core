-- Rollback Migration: 028_core_purchasing_completion.down.sql
-- Module: erp_purchasing
-- Purpose: Safely revert 028_core_purchasing_completion.sql schema, RLS policies, and permissions.

BEGIN;

-- ── 1. Drop Tables ──────────────────────────────────────────────────────────────

DROP TABLE IF EXISTS erp_purchasing.supplier_debit_note CASCADE;
DROP TABLE IF EXISTS erp_purchasing.purchase_return_line_record CASCADE;
DROP TABLE IF EXISTS erp_purchasing.purchase_return_record CASCADE;
DROP TABLE IF EXISTS erp_purchasing.supplier_invoice_line CASCADE;
DROP TABLE IF EXISTS erp_purchasing.supplier_invoice CASCADE;
DROP TABLE IF EXISTS erp_purchasing.quarantine_release_record CASCADE;
DROP TABLE IF EXISTS erp_purchasing.purchase_inspection_line CASCADE;
DROP TABLE IF EXISTS erp_purchasing.purchase_inspection CASCADE;
DROP TABLE IF EXISTS erp_purchasing.purchasing_tolerance_policy CASCADE;

-- ── 2. Revert Column Additions ──────────────────────────────────────────────────

ALTER TABLE erp_purchasing.goods_received_note_line
  DROP COLUMN IF EXISTS accepted_qty,
  DROP COLUMN IF EXISTS quarantined_qty,
  DROP COLUMN IF EXISTS rejected_qty;

ALTER TABLE erp_purchasing.purchase_order_line
  DROP COLUMN IF EXISTS tax_rate,
  DROP COLUMN IF EXISTS discount_amount,
  DROP COLUMN IF EXISTS line_total;

ALTER TABLE erp_purchasing.purchase_order
  DROP COLUMN IF EXISTS approved_by,
  DROP COLUMN IF EXISTS approved_at,
  DROP COLUMN IF EXISTS tax_rate,
  DROP COLUMN IF EXISTS discount_amount;

-- ── 3. Revert Constraint ────────────────────────────────────────────────────────

ALTER TABLE erp_purchasing.purchase_order
  DROP CONSTRAINT IF EXISTS purchase_order_order_status_check;

ALTER TABLE erp_purchasing.purchase_order
  ADD CONSTRAINT purchase_order_order_status_check
  CHECK (order_status IN ('draft', 'confirmed', 'partially_received', 'received', 'cancelled'));

-- ── 4. Remove Permissions ───────────────────────────────────────────────────────

DELETE FROM "authorization".permissions
WHERE permission_code IN (
    'purchasing.po.approve',
    'purchasing.invoice.view',
    'purchasing.invoice.create',
    'purchasing.invoice.match',
    'purchasing.inspection.view',
    'purchasing.inspection.record',
    'purchasing.quarantine.release',
    'purchasing.return.view',
    'purchasing.return.create',
    'purchasing.debit_note.view',
    'purchasing.debit_note.create',
    'purchasing.supplier.balance'
);

-- ── 5. Remove Migration Record ──────────────────────────────────────────────────

DELETE FROM public.schema_migrations
WHERE version = '028_core_purchasing_completion';

COMMIT;
