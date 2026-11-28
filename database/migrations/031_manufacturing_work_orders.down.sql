-- 031_manufacturing_work_orders.down.sql
-- Rollback for Universal ERP Phase 9: Manufacturing, BOM & Work Orders

DROP TABLE IF EXISTS manufacturing.production_inspections CASCADE;
DROP TABLE IF EXISTS manufacturing.work_order_operations CASCADE;
DROP TABLE IF EXISTS manufacturing.work_order_materials CASCADE;
DROP TABLE IF EXISTS manufacturing.work_orders CASCADE;
DROP TABLE IF EXISTS manufacturing.material_substitutions CASCADE;
DROP TABLE IF EXISTS manufacturing.bom_items CASCADE;
DROP TABLE IF EXISTS manufacturing.bom_revisions CASCADE;
DROP TABLE IF EXISTS manufacturing.boms CASCADE;
DROP TABLE IF EXISTS manufacturing.routing_operations CASCADE;
DROP TABLE IF EXISTS manufacturing.routings CASCADE;
DROP TABLE IF EXISTS manufacturing.work_centers CASCADE;

DROP SCHEMA IF EXISTS manufacturing CASCADE;

DELETE FROM "authorization".permissions WHERE permission_code IN (
    'mfg.bom.view',
    'mfg.bom.manage',
    'mfg.work_center.manage',
    'mfg.routing.manage',
    'mfg.work_order.view',
    'mfg.work_order.create',
    'mfg.work_order.approve',
    'mfg.material.reserve',
    'mfg.material.issue',
    'mfg.material.return',
    'mfg.operation.report',
    'mfg.qc.inspect',
    'mfg.work_order.complete',
    'mfg.reports.view'
);

DELETE FROM public.schema_migrations WHERE version = '031_manufacturing_work_orders';
