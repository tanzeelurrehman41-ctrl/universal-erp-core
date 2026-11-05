-- Migration 018: Add company_id to command_idempotency and create sync.operation_conflicts table

-- 1. Add company_id to erp_inventory.command_idempotency
ALTER TABLE erp_inventory.command_idempotency
    ADD COLUMN IF NOT EXISTS company_id uuid;

DO 'BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = ''command_idempotency_company_fkey'') THEN
        ALTER TABLE erp_inventory.command_idempotency
            ADD CONSTRAINT command_idempotency_company_fkey
            FOREIGN KEY (tenant_id, company_id)
            REFERENCES organization.companies(tenant_id, id);
    END IF;
END;';

-- 2. Durable conflict store for out-of-order or reused device sequence conflicts
CREATE TABLE IF NOT EXISTS sync.operation_conflicts (
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    device_id uuid NOT NULL,
    device_sequence bigint NOT NULL,
    conflict_code text NOT NULL,
    conflicting_operation_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, company_id, operation_id),
    FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id)
);

ALTER TABLE sync.operation_conflicts ENABLE ROW LEVEL SECURITY;
ALTER TABLE sync.operation_conflicts FORCE ROW LEVEL SECURITY;

DO 'BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = ''tenant_company_scope'' AND tablename = ''operation_conflicts'') THEN
        CREATE POLICY tenant_company_scope ON sync.operation_conflicts
            USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid
               AND company_id = NULLIF(current_setting(''app.company_id'', true), '''')::uuid)
            WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid
               AND company_id = NULLIF(current_setting(''app.company_id'', true), '''')::uuid);
    END IF;
END;';

GRANT SELECT, INSERT, UPDATE ON sync.operation_conflicts TO erp_app;
GRANT SELECT, INSERT, UPDATE ON erp_inventory.command_idempotency TO erp_app;
