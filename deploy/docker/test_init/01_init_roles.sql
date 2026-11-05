-- universal-erp-core/deploy/docker/test_init/01_init_roles.sql
-- Executed under test_bootstrap_admin during container first-boot

DO $$
DECLARE
    admin_pwd text := trim(pg_read_file('/run/secrets/test_admin_pwd'));
    app_pwd   text := trim(pg_read_file('/run/secrets/test_app_pwd'));
BEGIN
    -- 1. Provision erp_dev (Fixtures, Cleanup, Fault Injection)
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_dev') THEN
        EXECUTE format('CREATE ROLE erp_dev WITH LOGIN PASSWORD %L', admin_pwd);
    ELSE
        EXECUTE format('ALTER ROLE erp_dev WITH PASSWORD %L', admin_pwd);
    END IF;
    ALTER ROLE erp_dev WITH NOSUPERUSER BYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT LOGIN;

    -- 2. Provision erp_app (Application Workflows, RLS Assertions)
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_app') THEN
        EXECUTE format('CREATE ROLE erp_app WITH LOGIN PASSWORD %L', app_pwd);
    ELSE
        EXECUTE format('ALTER ROLE erp_app WITH PASSWORD %L', app_pwd);
    END IF;
    ALTER ROLE erp_app WITH NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT LOGIN;
END $$;
