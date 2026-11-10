-- Migration: 023_register_inventory_transfer_and_adjustment_permissions.sql
-- Module: inventory
-- Purpose: Register missing catalog permissions for inventory transfer and adjustment workflows.
-- Safety: Idempotent (ON CONFLICT DO NOTHING), zero role assignment changes, zero stock mutations.

BEGIN;

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
  ('inventory.transfer.create', 'Create and post inter-bin inventory transfers.', 'inventory'),
  ('inventory.adjustment.create', 'Create and post inventory quantity adjustments.', 'inventory')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('023_register_inventory_transfer_and_adjustment_permissions', now())
ON CONFLICT (version) DO NOTHING;

COMMIT;
