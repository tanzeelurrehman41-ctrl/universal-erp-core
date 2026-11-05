-- Allow the API to enroll an authenticated device, issue company-scoped receipt
-- grants, and reserve/replay receipt outcomes in the durable operation inbox.
-- Application RLS policies continue to restrict these writes to the active
-- tenant and company context.

GRANT SELECT, INSERT, UPDATE ON identity.devices TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.device_company_scopes, sync.offline_grants TO erp_app;
GRANT SELECT, INSERT, UPDATE ON sync.operation_inbox TO erp_app;
