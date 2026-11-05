-- Migration 019: Purchasing Outbox Poison-Event Handling and Dead-Letter Queue
-- Adds explicit status, error tracking, exponential backoff timestamp, and redrive audit tracking.

ALTER TABLE erp_purchasing.outbox_event
  ADD COLUMN IF NOT EXISTS status TEXT NOT NULL DEFAULT 'pending'
    CHECK (status IN ('pending', 'published', 'dead_letter')),
  ADD COLUMN IF NOT EXISTS last_error TEXT,
  ADD COLUMN IF NOT EXISTS next_retry_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS redrive_count INTEGER NOT NULL DEFAULT 0 CHECK (redrive_count >= 0),
  ADD COLUMN IF NOT EXISTS last_redriven_at TIMESTAMPTZ;

-- Backfill status for already published rows
UPDATE erp_purchasing.outbox_event
SET status = 'published'
WHERE published_at IS NOT NULL AND status != 'published';

-- Index for pending events eligible for processing/retry
CREATE INDEX IF NOT EXISTS ix_purchasing_outbox_pending
  ON erp_purchasing.outbox_event(tenant_id, company_id, occurred_at)
  WHERE status = 'pending';

-- Index for dead-letter events awaiting audit or redrive
CREATE INDEX IF NOT EXISTS ix_purchasing_outbox_dead_letter
  ON erp_purchasing.outbox_event(tenant_id, company_id, occurred_at)
  WHERE status = 'dead_letter';

GRANT SELECT, INSERT, UPDATE ON erp_purchasing.outbox_event TO erp_app;
