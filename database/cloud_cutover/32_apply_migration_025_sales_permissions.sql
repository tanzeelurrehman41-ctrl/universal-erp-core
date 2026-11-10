-- Migration: 025_register_sales_permissions.sql
-- Module: sales
-- Purpose: Register standard sales and POS catalog permissions in authorization.permissions on Neon.
-- Safety: Idempotent (ON CONFLICT DO NOTHING), zero role mutations, zero data changes.

BEGIN;

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
  ('sales.customer.view', 'View customer list and details.', 'sales'),
  ('sales.customer.create', 'Create and update customers.', 'sales'),
  ('sales.order.view', 'View sales orders list and details.', 'sales'),
  ('sales.order.create', 'Create draft sales orders.', 'sales'),
  ('sales.order.confirm', 'Confirm and approve sales orders.', 'sales'),
  ('sales.invoice.view', 'View sales invoices and POS transaction receipts.', 'sales'),
  ('sales.invoice.post', 'Post sales invoices, dispatch stock, and trigger General Ledger entries.', 'sales')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('025_register_sales_permissions', now())
ON CONFLICT (version) DO NOTHING;

COMMIT;
