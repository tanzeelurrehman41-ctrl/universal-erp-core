-- Migration 021 Down: Revoke Context Listing and Authentication Resolution Routine Privileges
REVOKE EXECUTE ON FUNCTION identity.list_user_company_contexts(text, text) FROM erp_app;
REVOKE EXECUTE ON FUNCTION identity.resolve_authenticated_user_context(uuid, uuid, text, text) FROM erp_app;
REVOKE EXECUTE ON FUNCTION identity.user_has_permission(uuid, uuid, uuid, text) FROM erp_app;
REVOKE INSERT ON erp_inventory.product FROM erp_app;
DROP FUNCTION IF EXISTS identity.user_has_permission(uuid, uuid, uuid, text);
DROP FUNCTION IF EXISTS identity.resolve_authenticated_user_context(uuid, uuid, text, text);
