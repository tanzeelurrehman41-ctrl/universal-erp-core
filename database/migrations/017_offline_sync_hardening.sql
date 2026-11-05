-- Migration 017: Harden offline receipt sync for concurrent requests, crash-recoverable leases, and effective location enforcement.

-- 1. Add lease tracking columns to sync.operation_inbox
ALTER TABLE sync.operation_inbox
    ADD COLUMN IF NOT EXISTS claim_token uuid,
    ADD COLUMN IF NOT EXISTS claimed_at timestamptz,
    ADD COLUMN IF NOT EXISTS claim_expires_at timestamptz;

CREATE INDEX IF NOT EXISTS operation_inbox_lease_idx
    ON sync.operation_inbox(tenant_id, company_id, operation_id, outcome, claim_expires_at);

-- 2. Add location_id to erp_inventory.warehouse to support location-based authorization
ALTER TABLE erp_inventory.warehouse
    ADD COLUMN IF NOT EXISTS location_id uuid;

DO 'BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = ''warehouse_location_fkey''
    ) THEN
        ALTER TABLE erp_inventory.warehouse
            ADD CONSTRAINT warehouse_location_fkey
            FOREIGN KEY (tenant_id, company_id, location_id)
            REFERENCES organization.locations(tenant_id, company_id, id);
    END IF;
END;';

-- 3. Ensure permissions for erp_app
GRANT SELECT, INSERT, UPDATE ON sync.operation_inbox TO erp_app;
GRANT SELECT ON sync.device_company_scopes, sync.device_location_scopes, sync.offline_grants TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_inventory.warehouse TO erp_app;
