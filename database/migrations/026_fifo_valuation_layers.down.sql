-- Migration: 026_fifo_valuation_layers.down.sql
-- Purpose: Reverse FIFO valuation layers tables and permissions cleanly.

BEGIN;

DROP TABLE IF EXISTS erp_inventory.inventory_cost_allocation;
DROP TABLE IF EXISTS erp_inventory.inventory_cost_layer;

DELETE FROM "authorization".role_permissions
WHERE permission_code = 'inventory.valuation.view';

DELETE FROM "authorization".permissions
WHERE permission_code = 'inventory.valuation.view';

DELETE FROM public.schema_migrations
WHERE version = '026_fifo_valuation_layers';

COMMIT;
