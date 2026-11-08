-- Migration 022 Down: Revoke and drop Foundation routines
REVOKE EXECUTE ON FUNCTION identity.list_audit_events(uuid, uuid, int) FROM erp_app;
REVOKE EXECUTE ON FUNCTION identity.record_audit_event(uuid, uuid, uuid, text, text, uuid, jsonb) FROM erp_app;
REVOKE EXECUTE ON FUNCTION identity.list_roles_and_permissions(uuid) FROM erp_app;
REVOKE EXECUTE ON FUNCTION identity.invite_or_assign_company_user(uuid, uuid, text, text, text) FROM erp_app;
REVOKE EXECUTE ON FUNCTION identity.list_company_users(uuid, uuid) FROM erp_app;
REVOKE EXECUTE ON FUNCTION identity.update_company_settings(uuid, uuid, text, text, text, text) FROM erp_app;
REVOKE EXECUTE ON FUNCTION identity.get_company_settings(uuid, uuid) FROM erp_app;
REVOKE EXECUTE ON FUNCTION identity.get_user_profile(uuid, uuid, uuid) FROM erp_app;

DROP FUNCTION IF EXISTS identity.list_audit_events(uuid, uuid, int);
DROP FUNCTION IF EXISTS identity.record_audit_event(uuid, uuid, uuid, text, text, uuid, jsonb);
DROP FUNCTION IF EXISTS identity.list_roles_and_permissions(uuid);
DROP FUNCTION IF EXISTS identity.invite_or_assign_company_user(uuid, uuid, text, text, text);
DROP FUNCTION IF EXISTS identity.list_company_users(uuid, uuid);
DROP FUNCTION IF EXISTS identity.update_company_settings(uuid, uuid, text, text, text, text);
DROP FUNCTION IF EXISTS identity.get_company_settings(uuid, uuid);
DROP FUNCTION IF EXISTS identity.get_user_profile(uuid, uuid, uuid);
