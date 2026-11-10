-- Migration: 024_register_purchasing_permissions.sql
-- Module: purchasing
-- Purpose: Register standard purchasing catalog permissions in authorization.permissions.
-- Safety: Idempotent (ON CONFLICT DO NOTHING), zero role mutations, zero data changes.

BEGIN;

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
  ('purchasing.supplier.view', 'View suppliers list and details.', 'purchasing'),
  ('purchasing.supplier.create', 'Create and update suppliers.', 'purchasing'),
  ('purchasing.po.view', 'View purchase orders list and details.', 'purchasing'),
  ('purchasing.po.create', 'Create draft purchase orders.', 'purchasing'),
  ('purchasing.po.confirm', 'Confirm and approve purchase orders.', 'purchasing'),
  ('purchasing.grn.view', 'View goods received notes.', 'purchasing'),
  ('purchasing.grn.post', 'Post goods received notes and trigger inventory outbox synchronization.', 'purchasing')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('024_register_purchasing_permissions', now())
ON CONFLICT (version) DO NOTHING;

COMMIT;
