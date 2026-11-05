-- Offline sync foundation adapted to the canonical identity and organization schemas.
-- identity.devices, identity.company_memberships, and organization locations remain
-- the authoritative device and membership records; this migration does not duplicate them.

CREATE SCHEMA IF NOT EXISTS sync;

CREATE TABLE sync.device_company_scopes (
  tenant_id uuid NOT NULL,
  device_id uuid NOT NULL,
  company_id uuid NOT NULL,
  scope_status text NOT NULL DEFAULT 'active'
    CHECK (scope_status IN ('active','revoked')),
  all_locations boolean NOT NULL DEFAULT false,
  granted_at timestamptz NOT NULL DEFAULT now(),
  revoked_at timestamptz,
  PRIMARY KEY (tenant_id, device_id, company_id),
  FOREIGN KEY (tenant_id, device_id)
    REFERENCES identity.devices(tenant_id, id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  CHECK ((scope_status = 'active' AND revoked_at IS NULL)
      OR (scope_status = 'revoked' AND revoked_at IS NOT NULL))
);

CREATE TABLE sync.device_location_scopes (
  tenant_id uuid NOT NULL,
  device_id uuid NOT NULL,
  company_id uuid NOT NULL,
  location_id uuid NOT NULL,
  granted_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, device_id, company_id, location_id),
  FOREIGN KEY (tenant_id, device_id, company_id)
    REFERENCES sync.device_company_scopes(tenant_id, device_id, company_id),
  FOREIGN KEY (tenant_id, company_id, location_id)
    REFERENCES organization.locations(tenant_id, company_id, id)
);

CREATE TABLE sync.offline_grants (
  tenant_id uuid NOT NULL,
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  device_id uuid NOT NULL,
  company_id uuid NOT NULL,
  user_id uuid NOT NULL,
  grant_version integer NOT NULL DEFAULT 1 CHECK (grant_version > 0),
  allowed_operations jsonb NOT NULL,
  allowed_location_ids jsonb NOT NULL DEFAULT '[]'::jsonb,
  limits jsonb NOT NULL DEFAULT '{}'::jsonb,
  issued_at timestamptz NOT NULL DEFAULT now(),
  expires_at timestamptz NOT NULL,
  revoked_at timestamptz,
  revoked_by uuid,
  revocation_reason text,
  grant_signature text NOT NULL,
  PRIMARY KEY (tenant_id, id),
  UNIQUE (tenant_id, company_id, device_id, id),
  FOREIGN KEY (tenant_id, device_id, company_id)
    REFERENCES sync.device_company_scopes(tenant_id, device_id, company_id),
  FOREIGN KEY (tenant_id, company_id, user_id)
    REFERENCES identity.company_memberships(tenant_id, company_id, user_id),
  CHECK (expires_at > issued_at),
  CHECK (jsonb_typeof(allowed_operations) = 'array'),
  CHECK (jsonb_typeof(allowed_location_ids) = 'array'),
  CHECK (jsonb_typeof(limits) = 'object')
);

CREATE TABLE sync.operation_inbox (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  operation_id uuid NOT NULL,
  batch_id uuid NOT NULL,
  device_id uuid NOT NULL,
  grant_id uuid NOT NULL,
  device_sequence bigint NOT NULL CHECK (device_sequence > 0),
  operation_type text NOT NULL,
  schema_version integer NOT NULL CHECK (schema_version > 0),
  aggregate_type text NOT NULL,
  aggregate_id uuid,
  base_version bigint CHECK (base_version IS NULL OR base_version >= 0),
  client_created_at timestamptz,
  dependency_operation_ids uuid[] NOT NULL DEFAULT '{}',
  payload_hash text NOT NULL CHECK (payload_hash ~ '^[0-9a-fA-F]{64}$'),
  outcome text NOT NULL CHECK (
    outcome IN ('accepted','duplicate','conflict','rejected','deferred')
  ),
  server_record_type text,
  server_record_id uuid,
  response_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
  received_at timestamptz NOT NULL DEFAULT now(),
  processed_at timestamptz,
  PRIMARY KEY (tenant_id, company_id, operation_id),
  UNIQUE (tenant_id, device_id, device_sequence),
  FOREIGN KEY (tenant_id, device_id, company_id)
    REFERENCES sync.device_company_scopes(tenant_id, device_id, company_id),
  FOREIGN KEY (tenant_id, company_id, device_id, grant_id)
    REFERENCES sync.offline_grants(tenant_id, company_id, device_id, id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  CHECK (jsonb_typeof(response_payload) = 'object')
);

CREATE TABLE sync.device_cursors (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  device_id uuid NOT NULL,
  download_cursor text,
  last_acknowledged_sequence bigint NOT NULL DEFAULT 0
    CHECK (last_acknowledged_sequence >= 0),
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, device_id),
  FOREIGN KEY (tenant_id, device_id, company_id)
    REFERENCES sync.device_company_scopes(tenant_id, device_id, company_id)
);

CREATE TABLE sync.change_log (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  change_sequence bigint GENERATED ALWAYS AS IDENTITY,
  change_id uuid NOT NULL DEFAULT gen_random_uuid(),
  entity_type text NOT NULL,
  entity_id uuid NOT NULL,
  entity_version bigint NOT NULL CHECK (entity_version > 0),
  change_type text NOT NULL CHECK (change_type IN ('upsert','delete')),
  payload jsonb NOT NULL DEFAULT '{}'::jsonb,
  occurred_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id, change_sequence),
  UNIQUE (tenant_id, company_id, change_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id),
  CHECK (jsonb_typeof(payload) = 'object')
);

-- Fail-closed company-scoped isolation; the API must set both values transaction-locally.
DO $$
DECLARE table_name text;
BEGIN
  FOREACH table_name IN ARRAY ARRAY[
    'device_company_scopes','device_location_scopes','offline_grants',
    'operation_inbox','device_cursors','change_log'
  ] LOOP
    EXECUTE format('ALTER TABLE sync.%I ENABLE ROW LEVEL SECURITY', table_name);
    EXECUTE format('ALTER TABLE sync.%I FORCE ROW LEVEL SECURITY', table_name);
    EXECUTE format(
      'CREATE POLICY tenant_company_scope ON sync.%I
       USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid
          AND company_id = NULLIF(current_setting(''app.company_id'', true), '''')::uuid)
       WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid
          AND company_id = NULLIF(current_setting(''app.company_id'', true), '''')::uuid)',
      table_name
    );
  END LOOP;
END $$;

CREATE INDEX offline_grants_active_idx
  ON sync.offline_grants(tenant_id, device_id, company_id, expires_at)
  WHERE revoked_at IS NULL;
CREATE INDEX operation_inbox_received_idx
  ON sync.operation_inbox(tenant_id, company_id, received_at DESC);
CREATE INDEX device_scopes_status_idx
  ON sync.device_company_scopes(tenant_id, company_id, scope_status);
CREATE INDEX change_log_entity_idx
  ON sync.change_log(tenant_id, company_id, entity_type, entity_id, entity_version);

-- The API can inspect grants and record/replay operation outcomes. Grant issuance,
-- revocation, device enrollment, and change-log writes stay unavailable until their
-- authenticated services are implemented.
GRANT USAGE ON SCHEMA sync TO erp_app;
GRANT SELECT ON sync.device_company_scopes, sync.device_location_scopes,
  sync.offline_grants TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.operation_inbox, sync.device_cursors TO erp_app;
