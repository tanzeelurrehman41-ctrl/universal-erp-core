-- Universal ERP — Migration 045 Down: Rollback Controlled Production Launch, Regional Data Residency Enforcement & Customer Pilot Setup

-- 1. Drop Triggers and Functions
DROP TRIGGER IF EXISTS trg_enforce_pilot_payments ON platform.subscription_payments;
DROP FUNCTION IF EXISTS platform.enforce_pilot_guardrails();

DROP TRIGGER IF EXISTS trg_enforce_tenant_residency_rules ON platform.tenant_data_residency_routes;
DROP FUNCTION IF EXISTS platform.enforce_tenant_residency_rules();

-- 2. Remove Pilot Data
DELETE FROM platform.tenant_entitlements WHERE tenant_id = 'a0000000-0000-0000-0000-000000000001';
DELETE FROM platform.tenant_data_residency_routes WHERE tenant_id = 'a0000000-0000-0000-0000-000000000001';
DELETE FROM platform.tenants WHERE id = 'a0000000-0000-0000-0000-000000000001';

-- 3. Drop Policy Table
DROP TABLE IF EXISTS platform.tenant_residency_enforcement_policies CASCADE;

-- 4. Clean up added regional nodes
DELETE FROM platform.regional_deployment_nodes
WHERE region_code IN (
    'ap-southeast-2', 'ap-southeast-4',
    'ap-south-1', 'ap-south-2',
    'eu-west-2',
    'me-central-1-uae', 'me-south-1-bhr', 'me-central-2-ksa'
);
