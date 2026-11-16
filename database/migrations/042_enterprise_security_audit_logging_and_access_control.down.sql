-- Universal ERP — Rollback Migration 042: Enterprise Security Hardening, Audit Logging & Access Control
-- Reverts security and audit tables.

DROP TABLE IF EXISTS security.api_rate_limit_states;
DROP TABLE IF EXISTS security.webhook_endpoints;
DROP TABLE IF EXISTS security.privileged_support_sessions;
DROP TABLE IF EXISTS audit.tamper_evident_event_log;
DROP FUNCTION IF EXISTS audit.prevent_audit_log_modification();
