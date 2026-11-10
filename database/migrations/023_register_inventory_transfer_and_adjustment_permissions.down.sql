-- Migration Down: 023_register_inventory_transfer_and_adjustment_permissions.down.sql
-- Module: inventory

BEGIN;

DELETE FROM "authorization".permissions
WHERE permission_code IN ('inventory.transfer.create', 'inventory.adjustment.create');

DELETE FROM public.schema_migrations
WHERE version = '023_register_inventory_transfer_and_adjustment_permissions';

COMMIT;
