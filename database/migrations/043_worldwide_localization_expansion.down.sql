-- Universal ERP — Migration 043 Rollback Companion
-- Safely tears down Phase 16 audit checkpoint tables and cleans schema_migrations.

DROP TRIGGER IF EXISTS trg_audit_checkpoints_immutable ON audit.audit_chain_checkpoints;
DROP FUNCTION IF EXISTS audit.prevent_checkpoint_modification();
DROP TABLE IF EXISTS audit.audit_chain_checkpoints;

DELETE FROM hr.payroll_country_packages
WHERE package_name IN (
    'Saudi Arabia GOSI & EOSA Package',
    'Canada CRA & RQ Statutory Payroll Package',
    'Australia ATO STP2 & Payday Super Package',
    'India CBIC GST & Statutory Payroll Package',
    'Netherlands Belastingdienst Payroll Package',
    'Spain AEAT & Seguridad Social Package',
    'Italy Agenzia delle Entrate & INPS Package',
    'Poland KAS & ZUS Package'
);

DELETE FROM schema_migrations WHERE version = '043_worldwide_localization_expansion';
