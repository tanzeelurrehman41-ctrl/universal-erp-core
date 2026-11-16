-- Rollback Migration 036: Multi-Branch Operations, Intercompany Consolidation & External Integrations Hub

-- 1. Drop integrations schema and tables
DROP TABLE IF EXISTS integrations.api_partners CASCADE;
DROP TABLE IF EXISTS integrations.outbound_event_deliveries CASCADE;
DROP TABLE IF EXISTS integrations.webhook_subscriptions CASCADE;
DROP SCHEMA IF EXISTS integrations CASCADE;

-- 2. Drop consolidation schema and tables
DROP TABLE IF EXISTS consolidation.elimination_lines CASCADE;
DROP TABLE IF EXISTS consolidation.consolidation_runs CASCADE;
DROP TABLE IF EXISTS consolidation.intercompany_transfer_lines CASCADE;
DROP TABLE IF EXISTS consolidation.intercompany_transfers CASCADE;
DROP TABLE IF EXISTS consolidation.intercompany_relationships CASCADE;
DROP SCHEMA IF EXISTS consolidation CASCADE;

-- 3. Remove branch_id column from domain tables
ALTER TABLE IF EXISTS erp_inventory.warehouse DROP COLUMN IF EXISTS branch_id;
ALTER TABLE IF EXISTS erp_sales.sales_order DROP COLUMN IF EXISTS branch_id;
ALTER TABLE IF EXISTS erp_sales.sales_invoice DROP COLUMN IF EXISTS branch_id;
ALTER TABLE IF EXISTS erp_purchasing.purchase_order DROP COLUMN IF EXISTS branch_id;
ALTER TABLE IF EXISTS hr.employees DROP COLUMN IF EXISTS branch_id;

-- 4. Drop branches table
DROP TABLE IF EXISTS organization.branches CASCADE;

-- 5. Delete permissions
DELETE FROM "authorization".permissions
WHERE permission_code IN (
    'branch.view',
    'branch.manage',
    'branch.switch',
    'intercompany.transfer.create',
    'intercompany.transfer.receive',
    'consolidation.run',
    'consolidation.view',
    'integrations.webhook.manage',
    'integrations.partner.manage'
);

-- 6. Remove migration registration
DELETE FROM public.schema_migrations
WHERE version = '036_multi_branch_and_intercompany_consolidation';
