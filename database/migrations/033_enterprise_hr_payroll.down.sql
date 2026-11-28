-- ==============================================================================
-- Universal ERP — Migration 033 Rollback: Enterprise HR, Attendance & Payroll
-- ==============================================================================

-- Remove migration registration
DELETE FROM public.schema_migrations WHERE version = '033_enterprise_hr_payroll';

-- Drop foreign key constraint from departments
ALTER TABLE IF EXISTS hr.departments DROP CONSTRAINT IF EXISTS departments_manager_fkey;

-- Drop tables in reverse dependency order
DROP TABLE IF EXISTS hr.payroll_reversals CASCADE;
DROP TABLE IF EXISTS hr.payslip_lines CASCADE;
DROP TABLE IF EXISTS hr.payslips CASCADE;
DROP TABLE IF EXISTS hr.payroll_periods CASCADE;
DROP TABLE IF EXISTS hr.salary_loans CASCADE;
DROP TABLE IF EXISTS hr.tax_slabs CASCADE;
DROP TABLE IF EXISTS hr.statutory_configurations CASCADE;
DROP TABLE IF EXISTS hr.holiday_calendars CASCADE;
DROP TABLE IF EXISTS hr.leave_requests CASCADE;
DROP TABLE IF EXISTS hr.leave_allocations CASCADE;
DROP TABLE IF EXISTS hr.leave_types CASCADE;
DROP TABLE IF EXISTS hr.attendance_records CASCADE;
DROP TABLE IF EXISTS hr.employee_shift_assignments CASCADE;
DROP TABLE IF EXISTS hr.shifts CASCADE;
DROP TABLE IF EXISTS hr.employee_documents CASCADE;
DROP TABLE IF EXISTS hr.compensation_structures CASCADE;
DROP TABLE IF EXISTS hr.employees CASCADE;
DROP TABLE IF EXISTS hr.designations CASCADE;
DROP TABLE IF EXISTS hr.departments CASCADE;

-- Drop schema if empty
DROP SCHEMA IF EXISTS hr CASCADE;

-- Remove permissions
DELETE FROM "authorization".permissions
WHERE permission_code LIKE 'hr.%' OR permission_code LIKE 'payroll.%';
