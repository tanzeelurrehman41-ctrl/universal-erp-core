-- =============================================================================
-- Universal ERP — Production Incremental Cutover: Migrations 021 & 022
-- Target: Confirmed Production Database (127.0.0.1:5432, universal_erp)
-- Safety: Enforces PostgreSQL session advisory lock 714209 and atomic transaction
-- =============================================================================

\set ON_ERROR_STOP on

BEGIN;

-- 1. Acquire exclusive advisory lock for deployment
SELECT pg_advisory_xact_lock(714209);

-- 2. Validate current production ledger contains exactly migrations 001 through 020
DO $$
DECLARE
    m_count integer;
    has_020 boolean;
BEGIN
    SELECT count(*) INTO m_count FROM public.schema_migrations;
    SELECT EXISTS (SELECT 1 FROM public.schema_migrations WHERE version = '020_reconcile_application_runtime_privileges') INTO has_020;
    
    IF m_count < 20 OR NOT has_020 THEN
        RAISE EXCEPTION 'Precondition Failed: Target production database must contain migrations 001 through 020 (found %)', m_count;
    END IF;
    
    IF EXISTS (SELECT 1 FROM public.schema_migrations WHERE version = '021_grant_context_listing_routine_privilege') THEN
        RAISE NOTICE 'Migration 021 already recorded in ledger; skipping re-application.';
    END IF;
END $$;

-- 3. Migration 021: Context resolution and membership verification routines
\ir ../migrations/021_grant_context_listing_routine_privilege.sql

INSERT INTO public.schema_migrations (version, applied_at, checksum)
VALUES ('021_grant_context_listing_routine_privilege', now(), 'c10e97790b7bd685a83184022c3ae46ef03485f6e4b568a645612c783816b299')
ON CONFLICT (version) DO UPDATE 
SET applied_at = EXCLUDED.applied_at, checksum = EXCLUDED.checksum;

-- 4. Migration 022: Complete ERP Foundation management routines
\ir ../migrations/022_complete_erp_foundation.sql

INSERT INTO public.schema_migrations (version, applied_at, checksum)
VALUES ('022_complete_erp_foundation', now(), '3f21225933deeaf863a528184cf2144e9dfb336be8c6c97a506a1acc8ca0740f')
ON CONFLICT (version) DO UPDATE 
SET applied_at = EXCLUDED.applied_at, checksum = EXCLUDED.checksum;

-- 5. Post-condition assertion: verify ledger now contains exactly 22 migrations
DO $$
DECLARE
    final_count integer;
BEGIN
    SELECT count(*) INTO final_count FROM public.schema_migrations;
    IF final_count <> 22 THEN
        RAISE EXCEPTION 'Cutover Verification Failed: Expected 22 migrations, found %', final_count;
    END IF;
END $$;

COMMIT;
