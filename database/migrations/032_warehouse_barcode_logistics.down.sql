-- ==============================================================================
-- Universal ERP — Database Migration 032 Rollback: Warehouse Management & Barcode Logistics
-- ==============================================================================

-- 1. Remove permissions
DELETE FROM "authorization".permissions
WHERE module_code = 'warehouse' OR permission_code LIKE 'warehouse.%';

-- 2. Drop warehouse tables
DROP TABLE IF EXISTS warehouse.quarantine_records CASCADE;
DROP TABLE IF EXISTS warehouse.replenishment_tasks CASCADE;
DROP TABLE IF EXISTS warehouse.shipment_package_items CASCADE;
DROP TABLE IF EXISTS warehouse.shipment_packages CASCADE;
DROP TABLE IF EXISTS warehouse.wave_pick_items CASCADE;
DROP TABLE IF EXISTS warehouse.picking_waves CASCADE;
DROP TABLE IF EXISTS warehouse.putaway_tasks CASCADE;
DROP TABLE IF EXISTS warehouse.scan_audit_logs CASCADE;
DROP TABLE IF EXISTS warehouse.barcode_definitions CASCADE;
DROP TABLE IF EXISTS warehouse.replenishment_rules CASCADE;
DROP TABLE IF EXISTS warehouse.putaway_strategies CASCADE;

-- 3. Drop columns from stock_bin
ALTER TABLE erp_inventory.stock_bin DROP COLUMN IF EXISTS storage_restriction;
ALTER TABLE erp_inventory.stock_bin DROP COLUMN IF EXISTS is_pick_face;
ALTER TABLE erp_inventory.stock_bin DROP COLUMN IF EXISTS max_units;
ALTER TABLE erp_inventory.stock_bin DROP COLUMN IF EXISTS max_volume_cbm;
ALTER TABLE erp_inventory.stock_bin DROP COLUMN IF EXISTS max_weight_kg;
ALTER TABLE erp_inventory.stock_bin DROP COLUMN IF EXISTS bin_position;
ALTER TABLE erp_inventory.stock_bin DROP COLUMN IF EXISTS shelf;
ALTER TABLE erp_inventory.stock_bin DROP COLUMN IF EXISTS rack;
ALTER TABLE erp_inventory.stock_bin DROP COLUMN IF EXISTS aisle;
ALTER TABLE erp_inventory.stock_bin DROP COLUMN IF EXISTS zone_id;

-- 4. Drop zones table and schema
DROP TABLE IF EXISTS warehouse.zones CASCADE;
DROP SCHEMA IF EXISTS warehouse CASCADE;

-- 5. Remove migration record
DELETE FROM public.schema_migrations WHERE version = '032_warehouse_barcode_logistics';
