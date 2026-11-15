-- Migration Down: 027_complete_inventory_management.down.sql
-- Module: inventory (Phase 5 - Rollback)
-- Safety: Reversible rollback of Phase 5 inventory management DDL and permissions.

BEGIN;

-- 1. Drop tables in reverse order
DROP TABLE IF EXISTS erp_inventory.stock_return_line CASCADE;
DROP TABLE IF EXISTS erp_inventory.stock_return CASCADE;
DROP TABLE IF EXISTS erp_inventory.stock_count_line CASCADE;
DROP TABLE IF EXISTS erp_inventory.stock_count_session CASCADE;
DROP TABLE IF EXISTS erp_inventory.stock_pick_line CASCADE;
DROP TABLE IF EXISTS erp_inventory.stock_pick_list CASCADE;

-- 2. Drop enhanced columns on stock_reservation if present
ALTER TABLE erp_inventory.stock_reservation DROP COLUMN IF EXISTS fulfilled_qty;
ALTER TABLE erp_inventory.stock_reservation DROP COLUMN IF EXISTS released_at;
ALTER TABLE erp_inventory.stock_reservation DROP COLUMN IF EXISTS note;

-- 3. Delete registered permissions
DELETE FROM "authorization".permissions
WHERE permission_code IN (
  'inventory.reservation.create',
  'inventory.reservation.release',
  'inventory.reservation.view',
  'inventory.pick.create',
  'inventory.pick.dispatch',
  'inventory.pick.view',
  'inventory.count.create',
  'inventory.count.record',
  'inventory.count.approve',
  'inventory.count.view',
  'inventory.adjustment.approve',
  'inventory.returns.create',
  'inventory.returns.post',
  'inventory.history.view',
  'inventory.valuation.report'
);

-- 4. Remove migration record
DELETE FROM public.schema_migrations
WHERE version = '027_complete_inventory_management';

COMMIT;
