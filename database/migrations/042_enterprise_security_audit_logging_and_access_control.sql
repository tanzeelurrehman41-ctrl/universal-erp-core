-- Universal ERP — Migration 042: Enterprise Security Hardening, Audit Logging & Access Control
-- Creates immutable tamper-evident cryptographic audit logs, privileged support access tracking,
-- webhook security registers, and token-bucket API rate limiting states.

CREATE SCHEMA IF NOT EXISTS audit;
CREATE SCHEMA IF NOT EXISTS security;

-- 1. Immutable Tamper-Evident Cryptographic Audit Event Log
CREATE TABLE IF NOT EXISTS audit.tamper_evident_event_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id UUID NOT NULL UNIQUE,
    tenant_id UUID NOT NULL,
    company_id UUID NOT NULL,
    branch_id UUID,
    actor_id UUID NOT NULL,
    actor_username VARCHAR(100) NOT NULL,
    action VARCHAR(50) NOT NULL,
    entity_type VARCHAR(50) NOT NULL,
    entity_id VARCHAR(100) NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    client_ip INET,
    before_state JSONB,
    after_state JSONB,
    prev_hash VARCHAR(64) NOT NULL,
    record_hash VARCHAR(64) NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_audit_log_tenant_occurred ON audit.tamper_evident_event_log (tenant_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_log_entity ON audit.tamper_evident_event_log (entity_type, entity_id);

-- Enforce append-only immutability via PostgreSQL trigger
CREATE OR REPLACE FUNCTION audit.prevent_audit_log_modification()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION '[FAIL-CLOSED] audit.tamper_evident_event_log is strictly append-only. UPDATE and DELETE operations are forbidden.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_audit_log_immutable ON audit.tamper_evident_event_log;
CREATE TRIGGER trg_audit_log_immutable
    BEFORE UPDATE OR DELETE ON audit.tamper_evident_event_log
    FOR EACH ROW
    EXECUTE FUNCTION audit.prevent_audit_log_modification();

-- 2. Privileged Support Access Session Tracking
CREATE TABLE IF NOT EXISTS security.privileged_support_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    support_user_id UUID NOT NULL,
    approved_by_user_id UUID NOT NULL,
    justification TEXT NOT NULL,
    granted_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_support_sessions_active ON security.privileged_support_sessions (tenant_id, expires_at);

-- 3. Webhook Endpoints & Encryption Key Registry
CREATE TABLE IF NOT EXISTS security.webhook_endpoints (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    url TEXT NOT NULL,
    hmac_secret_hash VARCHAR(64) NOT NULL,
    encryption_key_id VARCHAR(50) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_webhook_tenant ON security.webhook_endpoints (tenant_id, is_active);

-- 4. API Token Bucket Rate Limiting State
CREATE TABLE IF NOT EXISTS security.api_rate_limit_states (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL,
    user_id UUID,
    route_key VARCHAR(100) NOT NULL,
    tokens_available NUMERIC(10,2) NOT NULL,
    last_replenished_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_rate_limit_tenant_user_route UNIQUE (tenant_id, user_id, route_key)
);

-- Seed Genesis Audit Event
INSERT INTO audit.tamper_evident_event_log (
    event_id, tenant_id, company_id, branch_id, actor_id, actor_username,
    action, entity_type, entity_id, occurred_at, prev_hash, record_hash
) VALUES (
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000000',
    '00000000-0000-0000-0000-000000000000',
    NULL,
    '00000000-0000-0000-0000-000000000000',
    'system',
    'GENESIS',
    'AuditLog',
    'GENESIS_BLOCK',
    '2026-01-01 00:00:00+00',
    '0000000000000000000000000000000000000000000000000000000000000000',
    'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
) ON CONFLICT (event_id) DO NOTHING;
