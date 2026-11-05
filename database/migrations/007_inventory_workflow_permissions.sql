-- Seed essential development inventory reference data and least-privilege API rights.
INSERT INTO erp_inventory.stock_status
  (tenant_id, status_code, description, is_reservable, is_pickable)
SELECT id, 'available', 'Available for normal inventory use.', true, true
FROM platform.tenants
ON CONFLICT (tenant_id, status_code) DO NOTHING;

INSERT INTO erp_inventory.stock_status
  (tenant_id, status_code, description, is_reservable, is_pickable)
SELECT id, 'quality_hold', 'Held for inspection or quality review.', false, false
FROM platform.tenants
ON CONFLICT (tenant_id, status_code) DO NOTHING;

INSERT INTO erp_inventory.stock_status
  (tenant_id, status_code, description, is_reservable, is_pickable)
SELECT id, 'blocked', 'Blocked from normal use.', false, false
FROM platform.tenants
ON CONFLICT (tenant_id, status_code) DO NOTHING;

INSERT INTO erp_inventory.unit_of_measure
  (tenant_id, uom_id, code, name, dimension, decimal_places)
VALUES
  ('00000000-0000-4000-8000-000000000001',
   '00000000-0000-4000-8000-000000000006', 'EA', 'Each', 'count', 0)
ON CONFLICT (tenant_id, code) DO NOTHING;

INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
  ('inventory.uom.view', 'View inventory units of measure.', 'inventory'),
  ('inventory.uom.create', 'Create inventory units of measure.', 'inventory'),
  ('inventory.variant.view', 'View stock variants.', 'inventory'),
  ('inventory.variant.create', 'Create stock variants.', 'inventory'),
  ('inventory.warehouse.view', 'View warehouses and storage bins.', 'inventory'),
  ('inventory.warehouse.create', 'Create warehouses and storage bins.', 'inventory'),
  ('inventory.receipt.create', 'Create inventory receipts.', 'inventory'),
  ('inventory.receipt.post', 'Post stock into inventory.', 'inventory'),
  ('inventory.availability.view', 'View inventory availability.', 'inventory')
ON CONFLICT (permission_code) DO NOTHING;

GRANT SELECT, INSERT ON erp_inventory.unit_of_measure TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.product_variant TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.warehouse TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.stock_bin TO erp_app;
GRANT SELECT ON erp_inventory.stock_status TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.inventory_movement,
  erp_inventory.inventory_movement_leg TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_inventory.stock_balance TO erp_app;
GRANT SELECT, INSERT ON erp_inventory.outbox_event TO erp_app;
