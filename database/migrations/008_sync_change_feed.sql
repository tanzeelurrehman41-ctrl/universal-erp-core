-- Add a commit-ordered, company-scoped cursor for incremental offline downloads.
-- Updating the counter row inside the caller's business transaction ensures that
-- a later committed change cannot be skipped behind an earlier sequence value.

CREATE TABLE sync.company_change_counters (
  tenant_id uuid NOT NULL,
  company_id uuid NOT NULL,
  last_sequence bigint NOT NULL DEFAULT 0 CHECK (last_sequence >= 0),
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, company_id),
  FOREIGN KEY (tenant_id, company_id)
    REFERENCES organization.companies(tenant_id, id)
);

ALTER TABLE sync.company_change_counters ENABLE ROW LEVEL SECURITY;
ALTER TABLE sync.company_change_counters FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_company_scope ON sync.company_change_counters
  USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
     AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
  WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
          AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE sync.change_log ADD COLUMN company_sequence bigint;

WITH sequenced AS (
  SELECT ctid,
         row_number() OVER (
           PARTITION BY tenant_id, company_id
           ORDER BY change_sequence
         )::bigint AS next_company_sequence
  FROM sync.change_log
)
UPDATE sync.change_log AS change
SET company_sequence = sequenced.next_company_sequence
FROM sequenced
WHERE change.ctid = sequenced.ctid;

ALTER TABLE sync.change_log ALTER COLUMN company_sequence SET NOT NULL;
ALTER TABLE sync.change_log
  ADD CONSTRAINT change_log_company_sequence_positive CHECK (company_sequence > 0);
CREATE UNIQUE INDEX change_log_company_sequence_idx
  ON sync.change_log(tenant_id, company_id, company_sequence);

INSERT INTO sync.company_change_counters(tenant_id, company_id, last_sequence)
SELECT tenant_id, company_id, max(company_sequence)
FROM sync.change_log
GROUP BY tenant_id, company_id
ON CONFLICT (tenant_id, company_id) DO UPDATE
SET last_sequence = GREATEST(sync.company_change_counters.last_sequence, EXCLUDED.last_sequence),
    updated_at = now();

GRANT SELECT, INSERT ON sync.change_log TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.company_change_counters TO erp_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA sync TO erp_app;
