-- =============================================================================
-- Universal ERP — Production Safe Rollback: Migrations 022 & 021
-- Target: Confirmed Production Database (127.0.0.1:5432, universal_erp)
-- Safety: Preserves 100% of business data, tables, and migrations 001-020.
-- =============================================================================

\set ON_ERROR_STOP on

BEGIN;

SELECT pg_advisory_xact_lock(714209);

-- 1. Rollback Migration 022
\ir ../migrations/022_complete_erp_foundation.down.sql
DELETE FROM public.schema_migrations WHERE version = '022_complete_erp_foundation';

-- 2. Rollback Migration 021
\ir ../migrations/021_grant_context_listing_routine_privilege.down.sql
DELETE FROM public.schema_migrations WHERE version = '021_grant_context_listing_routine_privilege';

-- 3. Assert ledger count is restored to 20
DO $$
DECLARE
    m_count integer;
BEGIN
    SELECT count(*) INTO m_count FROM public.schema_migrations;
    IF m_count <> 20 THEN
        RAISE EXCEPTION 'Rollback Verification Failed: Expected 20 migrations, found %', m_count;
    END IF;
END $$;

COMMIT;
