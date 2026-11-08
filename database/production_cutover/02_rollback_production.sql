-- =============================================================================
-- Universal ERP Production Emergency Rollback Script
-- Target: Reset production database to pristine state in the event of aborted cutover
-- SAFETY NOTE: Only execute if deployment transaction failed or recovery is required.
-- =============================================================================

BEGIN;

-- 1. Terminate all active application sessions connected to the database
SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE datname = current_database()
  AND pid <> pg_backend_pid()
  AND usename IN ('erp_app', 'erp_dev');

-- 2. Drop all domain and operational schemas in dependency order
DROP SCHEMA IF EXISTS sync CASCADE;
DROP SCHEMA IF EXISTS erp_sales CASCADE;
DROP SCHEMA IF EXISTS erp_purchasing CASCADE;
DROP SCHEMA IF EXISTS erp_inventory CASCADE;
DROP SCHEMA IF EXISTS finance CASCADE;
DROP SCHEMA IF EXISTS organization CASCADE;
DROP SCHEMA IF EXISTS identity CASCADE;
DROP SCHEMA IF EXISTS "authorization" CASCADE;
DROP SCHEMA IF EXISTS audit CASCADE;
DROP SCHEMA IF EXISTS platform CASCADE;

-- 3. Drop public schema migrations ledger
DROP TABLE IF EXISTS public.schema_migrations CASCADE;

COMMIT;

-- 4. Revoke and drop application runtime role
DO $$
BEGIN
    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'erp_app') THEN
        DROP OWNED BY erp_app CASCADE;
        DROP ROLE erp_app;
    END IF;
END $$;
