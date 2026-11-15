-- ==============================================================================
-- Universal ERP — Database Migration 033: Enterprise HR, Attendance & Payroll
-- Phase 11: Employee Management, Organization Hierarchy, Shifts & Attendance,
--           Leave Entitlements & Workflows, Configurable Statutory Deductions,
--           Tax Slabs (Income Tax Ordinance 2001), Loans & Advances,
--           Payroll Run Engine, Payslips, Reversals, and GL Accounting Posting
-- ==============================================================================

CREATE SCHEMA IF NOT EXISTS hr;

-- ── 1. Departments Master Table ──────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS hr.departments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    department_code text NOT NULL,
    name text NOT NULL,
    parent_department_id uuid REFERENCES hr.departments(id) ON DELETE SET NULL,
    manager_id uuid, -- Reference added after employees table
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT departments_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT departments_unique_code UNIQUE (tenant_id, company_id, department_code)
);

-- ── 2. Designations Master Table ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS hr.designations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    designation_code text NOT NULL,
    title text NOT NULL,
    grade_level text NOT NULL DEFAULT 'G1',
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT designations_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT designations_unique_code UNIQUE (tenant_id, company_id, designation_code)
);

-- ── 3. Employees Master Table ────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS hr.employees (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    employee_code text NOT NULL,
    first_name text NOT NULL,
    last_name text NOT NULL,
    email text NOT NULL,
    phone text,
    cnic_or_national_id text NOT NULL,
    date_of_birth date,
    gender text CHECK (gender IN ('male', 'female', 'other')),
    hire_date date NOT NULL,
    termination_date date,
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'probation', 'suspended', 'terminated', 'resigned')),
    employment_type text NOT NULL DEFAULT 'full_time' CHECK (employment_type IN ('full_time', 'part_time', 'contract', 'intern')),
    department_id uuid REFERENCES hr.departments(id) ON DELETE SET NULL,
    designation_id uuid REFERENCES hr.designations(id) ON DELETE SET NULL,
    manager_id uuid REFERENCES hr.employees(id) ON DELETE SET NULL,
    user_id uuid REFERENCES identity.users(id) ON DELETE SET NULL,
    bank_name text,
    bank_account_title text,
    bank_account_number text,
    bank_iban text,
    tax_identification_number text,
    is_eobi_applicable boolean NOT NULL DEFAULT true,
    is_pf_applicable boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT employees_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT employees_unique_code UNIQUE (tenant_id, company_id, employee_code)
);

-- Add deferred department manager foreign key
ALTER TABLE hr.departments ADD CONSTRAINT departments_manager_fkey FOREIGN KEY (manager_id) REFERENCES hr.employees(id) ON DELETE SET NULL;

-- ── 4. Compensation Structures ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS hr.compensation_structures (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    employee_id uuid NOT NULL REFERENCES hr.employees(id) ON DELETE CASCADE,
    effective_date date NOT NULL,
    base_salary_pkr numeric(14,4) NOT NULL DEFAULT 0,
    house_rent_allowance_pkr numeric(14,4) NOT NULL DEFAULT 0,
    utility_allowance_pkr numeric(14,4) NOT NULL DEFAULT 0,
    conveyance_allowance_pkr numeric(14,4) NOT NULL DEFAULT 0,
    medical_allowance_pkr numeric(14,4) NOT NULL DEFAULT 0,
    special_allowance_pkr numeric(14,4) NOT NULL DEFAULT 0,
    gross_salary_pkr numeric(14,4) NOT NULL DEFAULT 0,
    payment_frequency text NOT NULL DEFAULT 'monthly' CHECK (payment_frequency IN ('monthly', 'bi_weekly', 'weekly')),
    currency_code character(3) NOT NULL DEFAULT 'PKR' REFERENCES finance.currencies(code),
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT comp_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

-- ── 5. Employee Document Records ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS hr.employee_documents (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    employee_id uuid NOT NULL REFERENCES hr.employees(id) ON DELETE CASCADE,
    document_type text NOT NULL CHECK (document_type IN ('cnic', 'contract', 'resume', 'degree', 'certification', 'nda', 'tax_form', 'other')),
    title text NOT NULL,
    file_reference text NOT NULL,
    expiry_date date,
    is_verified boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT emp_docs_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

-- ── 6. Shifts & Schedules ────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS hr.shifts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    shift_code text NOT NULL,
    name text NOT NULL,
    start_time time NOT NULL,
    end_time time NOT NULL,
    break_duration_minutes integer NOT NULL DEFAULT 60,
    grace_period_minutes integer NOT NULL DEFAULT 15,
    half_day_threshold_hours numeric(5,2) NOT NULL DEFAULT 4.0,
    full_day_hours numeric(5,2) NOT NULL DEFAULT 8.0,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT shifts_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT shifts_unique_code UNIQUE (tenant_id, company_id, shift_code)
);

CREATE TABLE IF NOT EXISTS hr.employee_shift_assignments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    employee_id uuid NOT NULL REFERENCES hr.employees(id) ON DELETE CASCADE,
    shift_id uuid NOT NULL REFERENCES hr.shifts(id) ON DELETE CASCADE,
    effective_from date NOT NULL,
    effective_to date,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT shift_assign_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

-- ── 7. Daily Attendance Records ──────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS hr.attendance_records (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    employee_id uuid NOT NULL REFERENCES hr.employees(id) ON DELETE CASCADE,
    attendance_date date NOT NULL,
    check_in_time timestamptz,
    check_out_time timestamptz,
    total_work_minutes integer NOT NULL DEFAULT 0,
    overtime_minutes integer NOT NULL DEFAULT 0,
    status text NOT NULL DEFAULT 'present' CHECK (status IN ('present', 'absent', 'half_day', 'on_leave', 'holiday', 'rest_day')),
    late_minutes integer NOT NULL DEFAULT 0,
    is_regularized boolean NOT NULL DEFAULT false,
    regularization_reason text,
    regularized_by uuid REFERENCES identity.users(id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT att_records_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT att_unique_emp_date UNIQUE (tenant_id, company_id, employee_id, attendance_date)
);

-- ── 8. Leave Types & Allocations ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS hr.leave_types (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    default_annual_quota numeric(5,2) NOT NULL DEFAULT 14.0,
    is_paid boolean NOT NULL DEFAULT true,
    is_carry_forward_allowed boolean NOT NULL DEFAULT false,
    max_carry_forward_days numeric(5,2) NOT NULL DEFAULT 0,
    is_encashable boolean NOT NULL DEFAULT false,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT leave_types_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT leave_types_unique_code UNIQUE (tenant_id, company_id, code)
);

CREATE TABLE IF NOT EXISTS hr.leave_allocations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    employee_id uuid NOT NULL REFERENCES hr.employees(id) ON DELETE CASCADE,
    leave_type_id uuid NOT NULL REFERENCES hr.leave_types(id) ON DELETE CASCADE,
    year integer NOT NULL,
    allocated_days numeric(5,2) NOT NULL DEFAULT 0,
    used_days numeric(5,2) NOT NULL DEFAULT 0,
    pending_days numeric(5,2) NOT NULL DEFAULT 0,
    carried_forward_days numeric(5,2) NOT NULL DEFAULT 0,
    remaining_days numeric(5,2) NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT leave_alloc_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT leave_alloc_unique UNIQUE (tenant_id, company_id, employee_id, leave_type_id, year)
);

CREATE TABLE IF NOT EXISTS hr.leave_requests (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    employee_id uuid NOT NULL REFERENCES hr.employees(id) ON DELETE CASCADE,
    leave_type_id uuid NOT NULL REFERENCES hr.leave_types(id) ON DELETE CASCADE,
    start_date date NOT NULL,
    end_date date NOT NULL,
    total_days numeric(5,2) NOT NULL,
    reason text NOT NULL,
    status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'approved', 'rejected', 'cancelled')),
    approved_by uuid REFERENCES identity.users(id) ON DELETE SET NULL,
    approved_at timestamptz,
    rejection_reason text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT leave_req_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS hr.holiday_calendars (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    holiday_date date NOT NULL,
    name text NOT NULL,
    description text,
    is_recurring boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT holiday_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT holiday_unique_date UNIQUE (tenant_id, company_id, holiday_date)
);

-- ── 9. Configurable Statutory Rules & Tax Slabs ──────────────────────────────
CREATE TABLE IF NOT EXISTS hr.statutory_configurations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    config_key text NOT NULL,
    config_name text NOT NULL,
    effective_from date NOT NULL,
    effective_to date,
    rate_percentage numeric(8,4),
    threshold_amount_pkr numeric(14,4),
    employer_rate_percentage numeric(8,4),
    employer_fixed_amount_pkr numeric(14,4),
    employee_fixed_amount_pkr numeric(14,4),
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT stat_conf_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT stat_conf_unique_key UNIQUE (tenant_id, company_id, config_key, effective_from)
);

CREATE TABLE IF NOT EXISTS hr.tax_slabs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    tax_year integer NOT NULL,
    effective_from date NOT NULL,
    effective_to date,
    slab_order integer NOT NULL DEFAULT 1,
    min_annual_income_pkr numeric(14,4) NOT NULL DEFAULT 0,
    max_annual_income_pkr numeric(14,4),
    fixed_tax_amount_pkr numeric(14,4) NOT NULL DEFAULT 0,
    marginal_tax_rate_percentage numeric(8,4) NOT NULL DEFAULT 0,
    description text,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT tax_slabs_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT tax_slabs_unique_order UNIQUE (tenant_id, company_id, tax_year, slab_order)
);

-- ── 10. Staff Loans & Salary Advances ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS hr.salary_loans (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    loan_number text NOT NULL,
    employee_id uuid NOT NULL REFERENCES hr.employees(id) ON DELETE CASCADE,
    principal_amount_pkr numeric(14,4) NOT NULL,
    monthly_installment_pkr numeric(14,4) NOT NULL,
    total_installments integer NOT NULL,
    paid_installments integer NOT NULL DEFAULT 0,
    remaining_balance_pkr numeric(14,4) NOT NULL,
    status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'approved', 'active', 'repaid', 'written_off')),
    approved_by uuid REFERENCES identity.users(id) ON DELETE SET NULL,
    disbursed_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT salary_loans_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT salary_loans_unique_number UNIQUE (tenant_id, company_id, loan_number)
);

-- ── 11. Payroll Periods & Processing Runs ────────────────────────────────────
CREATE TABLE IF NOT EXISTS hr.payroll_periods (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    period_name text NOT NULL,
    period_code text NOT NULL,
    start_date date NOT NULL,
    end_date date NOT NULL,
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'calculated', 'approved', 'posted', 'reversed')),
    total_gross_pkr numeric(14,4) NOT NULL DEFAULT 0,
    total_deductions_pkr numeric(14,4) NOT NULL DEFAULT 0,
    total_net_pkr numeric(14,4) NOT NULL DEFAULT 0,
    total_employer_contributions_pkr numeric(14,4) NOT NULL DEFAULT 0,
    gl_journal_id uuid,
    processed_by uuid REFERENCES identity.users(id) ON DELETE SET NULL,
    processed_at timestamptz,
    approved_by uuid REFERENCES identity.users(id) ON DELETE SET NULL,
    approved_at timestamptz,
    posted_by uuid REFERENCES identity.users(id) ON DELETE SET NULL,
    posted_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT payroll_periods_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT payroll_periods_unique_code UNIQUE (tenant_id, company_id, period_code)
);

-- ── 12. Employee Payslips & Itemized Lines ───────────────────────────────────
CREATE TABLE IF NOT EXISTS hr.payslips (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    payroll_period_id uuid NOT NULL REFERENCES hr.payroll_periods(id) ON DELETE CASCADE,
    employee_id uuid NOT NULL REFERENCES hr.employees(id) ON DELETE CASCADE,
    payslip_number text NOT NULL,
    currency_code character(3) NOT NULL DEFAULT 'PKR' REFERENCES finance.currencies(code),
    working_days numeric(5,2) NOT NULL DEFAULT 30,
    attended_days numeric(5,2) NOT NULL DEFAULT 0,
    leave_days numeric(5,2) NOT NULL DEFAULT 0,
    absent_days numeric(5,2) NOT NULL DEFAULT 0,
    overtime_hours numeric(6,2) NOT NULL DEFAULT 0,
    base_salary_pkr numeric(14,4) NOT NULL DEFAULT 0,
    allowances_pkr numeric(14,4) NOT NULL DEFAULT 0,
    overtime_pay_pkr numeric(14,4) NOT NULL DEFAULT 0,
    bonus_pkr numeric(14,4) NOT NULL DEFAULT 0,
    gross_pay_pkr numeric(14,4) NOT NULL DEFAULT 0,
    tax_deduction_pkr numeric(14,4) NOT NULL DEFAULT 0,
    eobi_employee_pkr numeric(14,4) NOT NULL DEFAULT 0,
    pf_employee_pkr numeric(14,4) NOT NULL DEFAULT 0,
    loan_deduction_pkr numeric(14,4) NOT NULL DEFAULT 0,
    other_deductions_pkr numeric(14,4) NOT NULL DEFAULT 0,
    total_deductions_pkr numeric(14,4) NOT NULL DEFAULT 0,
    net_pay_pkr numeric(14,4) NOT NULL DEFAULT 0,
    eobi_employer_pkr numeric(14,4) NOT NULL DEFAULT 0,
    pf_employer_pkr numeric(14,4) NOT NULL DEFAULT 0,
    payment_status text NOT NULL DEFAULT 'pending' CHECK (payment_status IN ('pending', 'paid', 'cancelled')),
    payment_method text NOT NULL DEFAULT 'bank_transfer' CHECK (payment_method IN ('bank_transfer', 'cheque', 'cash')),
    payment_reference text,
    paid_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT payslips_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT payslips_unique_period_emp UNIQUE (tenant_id, company_id, payroll_period_id, employee_id),
    CONSTRAINT payslips_unique_number UNIQUE (tenant_id, company_id, payslip_number)
);

CREATE TABLE IF NOT EXISTS hr.payslip_lines (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    payslip_id uuid NOT NULL REFERENCES hr.payslips(id) ON DELETE CASCADE,
    line_type text NOT NULL CHECK (line_type IN ('earning', 'deduction', 'employer_contribution')),
    category text NOT NULL,
    description text NOT NULL,
    amount_pkr numeric(14,4) NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT payslip_lines_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS hr.payroll_reversals (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    company_id uuid NOT NULL,
    payroll_period_id uuid NOT NULL REFERENCES hr.payroll_periods(id) ON DELETE CASCADE,
    reversal_reason text NOT NULL,
    reversal_journal_id uuid,
    reversed_by uuid REFERENCES identity.users(id) ON DELETE SET NULL,
    reversed_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT payroll_rev_tenant_company_fkey FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies(tenant_id, id) ON DELETE CASCADE
);

-- ── 13. Row-Level Security Enforcement ───────────────────────────────────────
ALTER TABLE hr.departments ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.departments FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.departments;
CREATE POLICY tenant_company_scope ON hr.departments
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.designations ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.designations FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.designations;
CREATE POLICY tenant_company_scope ON hr.designations
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.employees ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.employees FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.employees;
CREATE POLICY tenant_company_scope ON hr.employees
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.compensation_structures ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.compensation_structures FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.compensation_structures;
CREATE POLICY tenant_company_scope ON hr.compensation_structures
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.employee_documents ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.employee_documents FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.employee_documents;
CREATE POLICY tenant_company_scope ON hr.employee_documents
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.shifts ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.shifts FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.shifts;
CREATE POLICY tenant_company_scope ON hr.shifts
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.employee_shift_assignments ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.employee_shift_assignments FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.employee_shift_assignments;
CREATE POLICY tenant_company_scope ON hr.employee_shift_assignments
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.attendance_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.attendance_records FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.attendance_records;
CREATE POLICY tenant_company_scope ON hr.attendance_records
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.leave_types ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.leave_types FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.leave_types;
CREATE POLICY tenant_company_scope ON hr.leave_types
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.leave_allocations ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.leave_allocations FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.leave_allocations;
CREATE POLICY tenant_company_scope ON hr.leave_allocations
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.leave_requests ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.leave_requests FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.leave_requests;
CREATE POLICY tenant_company_scope ON hr.leave_requests
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.holiday_calendars ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.holiday_calendars FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.holiday_calendars;
CREATE POLICY tenant_company_scope ON hr.holiday_calendars
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.statutory_configurations ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.statutory_configurations FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.statutory_configurations;
CREATE POLICY tenant_company_scope ON hr.statutory_configurations
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.tax_slabs ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.tax_slabs FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.tax_slabs;
CREATE POLICY tenant_company_scope ON hr.tax_slabs
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.salary_loans ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.salary_loans FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.salary_loans;
CREATE POLICY tenant_company_scope ON hr.salary_loans
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.payroll_periods ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.payroll_periods FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.payroll_periods;
CREATE POLICY tenant_company_scope ON hr.payroll_periods
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.payslips ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.payslips FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.payslips;
CREATE POLICY tenant_company_scope ON hr.payslips
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.payslip_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.payslip_lines FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.payslip_lines;
CREATE POLICY tenant_company_scope ON hr.payslip_lines
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

ALTER TABLE hr.payroll_reversals ENABLE ROW LEVEL SECURITY;
ALTER TABLE hr.payroll_reversals FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_company_scope ON hr.payroll_reversals;
CREATE POLICY tenant_company_scope ON hr.payroll_reversals
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND company_id = NULLIF(current_setting('app.company_id', true), '')::uuid);

-- ── 14. Register Permissions ────────────────────────────────────────────────
INSERT INTO "authorization".permissions (permission_code, description, module_code)
VALUES
    ('hr.employee.read', 'View employee records and profiles', 'hr'),
    ('hr.employee.manage', 'Create and manage employee master data and compensation', 'hr'),
    ('hr.department.manage', 'Create and manage departments and designations', 'hr'),
    ('hr.attendance.read', 'View shift assignments and attendance logs', 'hr'),
    ('hr.attendance.manage', 'Record and regularize employee attendance and shifts', 'hr'),
    ('hr.leave.read', 'View leave balances and requests', 'hr'),
    ('hr.leave.apply', 'Submit leave applications', 'hr'),
    ('hr.leave.approve', 'Approve or reject leave requests', 'hr'),
    ('payroll.period.manage', 'Configure payroll periods and calendars', 'hr'),
    ('payroll.process', 'Execute payroll calculation runs', 'hr'),
    ('payroll.approve', 'Approve finalized payroll runs', 'hr'),
    ('payroll.post_gl', 'Post payroll journals to General Ledger', 'hr'),
    ('payroll.reverse', 'Reverse posted payroll and generate reversing journals', 'hr'),
    ('payroll.salary.view', 'View confidential salary and payslip amounts', 'hr'),
    ('payroll.loan.manage', 'Manage employee loans, salary advances and recovery', 'hr'),
    ('hr.reports.view', 'Access executive HR, attendance and payroll reports', 'hr')
ON CONFLICT (permission_code) DO NOTHING;

-- ── 15. Grant Permissions to erp_app Role ───────────────────────────────────
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_app') THEN
        GRANT USAGE ON SCHEMA hr TO erp_app;
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA hr TO erp_app;
    END IF;
END $$;

-- ── 16. Register in Schema Migrations ────────────────────────────────────────
INSERT INTO public.schema_migrations (version, applied_at)
VALUES ('033_enterprise_hr_payroll', now())
ON CONFLICT (version) DO NOTHING;
