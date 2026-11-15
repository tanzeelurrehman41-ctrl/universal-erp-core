-- ==============================================================================
-- Migration 029 Rollback: Core Sales & Order-to-Cash Completion (Phase 7)
-- ==============================================================================

BEGIN;

-- ── 1. Drop Tables ─────────────────────────────────────────────────────────────

DROP TABLE IF EXISTS erp_sales.sales_credit_note CASCADE;
DROP TABLE IF EXISTS erp_sales.sales_return_line CASCADE;
DROP TABLE IF EXISTS erp_sales.sales_return CASCADE;
DROP TABLE IF EXISTS erp_sales.customer_receipt_allocation CASCADE;
DROP TABLE IF EXISTS erp_sales.customer_receipt CASCADE;
DROP TABLE IF EXISTS erp_sales.sales_dispatch_line CASCADE;
DROP TABLE IF EXISTS erp_sales.sales_dispatch CASCADE;
DROP TABLE IF EXISTS erp_sales.sales_quotation_line CASCADE;
DROP TABLE IF EXISTS erp_sales.sales_quotation CASCADE;

-- ── 2. Revert sales_invoice columns and constraints ───────────────────────────

ALTER TABLE erp_sales.sales_invoice
  DROP COLUMN IF EXISTS gl_journal_id,
  DROP COLUMN IF EXISTS due_date,
  DROP COLUMN IF EXISTS dispatch_id;

ALTER TABLE erp_sales.sales_invoice
  DROP CONSTRAINT IF EXISTS sales_invoice_invoice_status_check;

ALTER TABLE erp_sales.sales_invoice
  ADD CONSTRAINT sales_invoice_invoice_status_check
  CHECK (invoice_status = ANY (ARRAY['posted'::text, 'paid'::text, 'reversed'::text]));

-- ── 3. Revert sales_order columns and constraints ─────────────────────────────

ALTER TABLE erp_sales.sales_order
  DROP COLUMN IF EXISTS cancelled_at,
  DROP COLUMN IF EXISTS cancellation_reason,
  DROP COLUMN IF EXISTS revision_number,
  DROP COLUMN IF EXISTS approved_at,
  DROP COLUMN IF EXISTS approved_by,
  DROP COLUMN IF EXISTS quotation_id;

ALTER TABLE erp_sales.sales_order
  DROP CONSTRAINT IF EXISTS sales_order_order_status_check;

ALTER TABLE erp_sales.sales_order
  ADD CONSTRAINT sales_order_order_status_check
  CHECK (order_status = ANY (ARRAY['draft'::text, 'confirmed'::text, 'fulfilled'::text, 'cancelled'::text]));

-- ── 4. Remove Registered Permissions ───────────────────────────────────────────

DELETE FROM "authorization".permissions
WHERE permission_code IN (
    'sales.quotation.create',
    'sales.quotation.approve',
    'sales.order.approve',
    'sales.dispatch.create',
    'sales.dispatch.confirm',
    'sales.receipt.create',
    'sales.receipt.allocate',
    'sales.return.create',
    'sales.return.approve',
    'sales.creditnote.create',
    'sales.creditnote.approve',
    'sales.receivable.view'
);

-- ── 5. Remove from Migration Registry ──────────────────────────────────────────

DELETE FROM public.schema_migrations
WHERE version = '029_core_sales_completion';

COMMIT;
