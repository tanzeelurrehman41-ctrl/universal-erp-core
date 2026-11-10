-- Universal ERP -- Read-Only Finance Workspace & Trial Balance Verification
-- Target Database: Neon PostgreSQL (neondb)
-- Target Tenant: DEMO-CLOUD (22222222-2222-4222-8222-222222222222)
-- Target Company: CLOUD-LOGISTICS (22222222-2222-4222-8222-222222222223)
-- Purpose: Read-only inspection and reconciliation of Chart of Accounts, Journal Entries, Lines, and Trial Balance.
-- Safety: Strictly read-only queries and assertion block. Zero database mutations.

\echo '=========================================================================='
\echo 'UNIVERSAL ERP -- FINANCE WORKSPACE & TRIAL BALANCE VERIFICATION'
\echo '=========================================================================='

SELECT current_database() AS connected_database, current_user AS connected_user, version() AS pg_version;

\echo ''
\echo '=========================================================================='
\echo '1. Chart of Accounts in finance.accounts (Expected: 12 Active Accounts)'
\echo '=========================================================================='
SELECT 
    a.account_code,
    a.account_name,
    a.account_type,
    a.normal_balance,
    a.currency_code,
    a.is_control_account,
    a.allow_manual_posting,
    a.is_active
FROM finance.accounts a
WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY a.account_code ASC;

SELECT count(*) AS total_accounts,
       count(*) FILTER (WHERE is_active = true) AS active_accounts,
       count(*) FILTER (WHERE is_control_account = true) AS control_accounts
FROM finance.accounts
WHERE tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo ''
\echo '=========================================================================='
\echo '2. Fiscal Periods in finance.fiscal_periods (Expected: Open November 2026)'
\echo '=========================================================================='
SELECT 
    fp.period_no,
    fp.name,
    fp.starts_on,
    fp.ends_on,
    fp.status,
    fy.code AS fiscal_year_code
FROM finance.fiscal_periods fp
JOIN finance.fiscal_years fy ON fy.id = fp.fiscal_year_id AND fy.tenant_id = fp.tenant_id AND fy.company_id = fp.company_id
WHERE fp.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND fp.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY fp.starts_on ASC;

\echo ''
\echo '=========================================================================='
\echo '3. Posted Journal Headers in finance.journals'
\echo '=========================================================================='
SELECT 
    j.id AS journal_id,
    j.journal_no,
    j.journal_date,
    j.source_module,
    j.source_type,
    j.source_id,
    j.status,
    j.posted_at,
    COALESCE(sum(jl.debit_functional), 0) AS total_debit,
    COALESCE(sum(jl.credit_functional), 0) AS total_credit,
    CASE WHEN COALESCE(sum(jl.debit_functional), 0) = COALESCE(sum(jl.credit_functional), 0) THEN 'BALANCED' ELSE 'UNBALANCED' END AS balance_status
FROM finance.journals j
LEFT JOIN finance.journal_lines jl ON jl.journal_id = j.id AND jl.tenant_id = j.tenant_id AND jl.company_id = j.company_id
WHERE j.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND j.company_id = '22222222-2222-4222-8222-222222222223'::uuid
GROUP BY j.id, j.journal_no, j.journal_date, j.source_module, j.source_type, j.source_id, j.status, j.posted_at
ORDER BY j.journal_date ASC, j.created_at ASC;

\echo ''
\echo '=========================================================================='
\echo '4. Double-Entry Journal Lines in finance.journal_lines'
\echo '=========================================================================='
SELECT 
    j.journal_no,
    jl.line_no,
    a.account_code,
    a.account_name,
    a.account_type,
    jl.description,
    jl.transaction_currency,
    jl.debit_functional,
    jl.credit_functional
FROM finance.journal_lines jl
JOIN finance.journals j ON j.id = jl.journal_id AND j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id
JOIN finance.accounts a ON a.id = jl.account_id AND a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id
WHERE jl.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND jl.company_id = '22222222-2222-4222-8222-222222222223'::uuid
ORDER BY j.journal_no, jl.line_no;

\echo ''
\echo '=========================================================================='
\echo '5. Line Sums by Account (Expected: 1200 Debit 2,250 PKR, 2010 Credit 2,250 PKR)'
\echo '=========================================================================='
SELECT 
    a.account_code,
    a.account_name,
    a.account_type,
    a.normal_balance,
    sum(jl.debit_functional) AS total_debit,
    sum(jl.credit_functional) AS total_credit
FROM finance.journal_lines jl
JOIN finance.accounts a ON a.id = jl.account_id AND a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id
WHERE jl.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND jl.company_id = '22222222-2222-4222-8222-222222222223'::uuid
GROUP BY a.account_code, a.account_name, a.account_type, a.normal_balance
ORDER BY a.account_code;

\echo ''
\echo '=========================================================================='
\echo '6. General Ledger Trial Balance (Live Application Query)'
\echo '=========================================================================='
SELECT 
    a.account_code,
    a.account_name,
    a.account_type AS classification,
    a.normal_balance,
    COALESCE(SUM(l.debit_functional), 0) AS total_debit,
    COALESCE(SUM(l.credit_functional), 0) AS total_credit,
    CASE 
        WHEN a.normal_balance = 'debit' THEN COALESCE(SUM(l.debit_functional), 0) - COALESCE(SUM(l.credit_functional), 0)
        ELSE COALESCE(SUM(l.credit_functional), 0) - COALESCE(SUM(l.debit_functional), 0)
    END AS net_balance
FROM finance.accounts a
LEFT JOIN (
    finance.journal_lines l
    JOIN finance.journals j
      ON j.tenant_id = l.tenant_id AND j.company_id = l.company_id AND j.id = l.journal_id AND j.status = 'posted'
) ON a.tenant_id = l.tenant_id AND a.company_id = l.company_id AND a.id = l.account_id
WHERE a.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND a.company_id = '22222222-2222-4222-8222-222222222223'::uuid
  AND a.is_active = true
GROUP BY a.id, a.account_code, a.account_name, a.account_type, a.normal_balance
ORDER BY a.account_code;

SELECT 
    COALESCE(sum(l.debit_functional), 0) AS tb_total_debit,
    COALESCE(sum(l.credit_functional), 0) AS tb_total_credit,
    COALESCE(sum(l.debit_functional), 0) - COALESCE(sum(l.credit_functional), 0) AS tb_difference,
    CASE 
        WHEN COALESCE(sum(l.debit_functional), 0) = COALESCE(sum(l.credit_functional), 0) THEN '✓ MATCHED (BALANCED)'
        ELSE '✕ OUT OF BALANCE'
    END AS trial_balance_status
FROM finance.journal_lines l
JOIN finance.journals j ON j.id = l.journal_id AND j.tenant_id = l.tenant_id AND j.company_id = l.company_id AND j.status = 'posted'
WHERE l.tenant_id = '22222222-2222-4222-8222-222222222222'::uuid
  AND l.company_id = '22222222-2222-4222-8222-222222222223'::uuid;

\echo ''
\echo '=========================================================================='
\echo '7. Automated Finance Reconciliations & Integrity Checks'
\echo '=========================================================================='
DO $$
DECLARE
    v_tenant_id uuid := '22222222-2222-4222-8222-222222222222'::uuid;
    v_company_id uuid := '22222222-2222-4222-8222-222222222223'::uuid;
    v_account_count integer;
    v_journal_count integer;
    v_unposted_count integer;
    v_duplicate_journals integer;
    v_acc_1200_debit numeric;
    v_acc_2010_credit numeric;
    v_tb_debit numeric;
    v_tb_credit numeric;
    v_all_passed boolean := true;
BEGIN
    -- Check 1: Accounts count
    SELECT count(*) INTO v_account_count
    FROM finance.accounts
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id AND is_active = true;

    IF v_account_count >= 12 THEN
        RAISE NOTICE '[PASS] 1. Chart of Accounts: % active accounts found (meets standard 12 accounts).', v_account_count;
    ELSE
        RAISE WARNING '[FAIL] 1. Chart of Accounts: Found % active accounts (expected >= 12).', v_account_count;
        v_all_passed := false;
    END IF;

    -- Check 2: Journal count and status
    SELECT count(*), count(*) FILTER (WHERE status <> 'posted')
    INTO v_journal_count, v_unposted_count
    FROM finance.journals
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id;

    IF v_journal_count = 2 AND v_unposted_count = 0 THEN
        RAISE NOTICE '[PASS] 2. Journal Headers: Exactly 2 posted journals exist (GRN-2024-01 & GRN-2024-03) with 0 unposted.', v_journal_count;
    ELSE
        RAISE WARNING '[FAIL] 2. Journal Headers: Total journals=%, Unposted=% (expected 2, 0).', v_journal_count, v_unposted_count;
        v_all_passed := false;
    END IF;

    -- Check 3: Duplicate journals check
    SELECT count(*) - count(DISTINCT source_id) INTO v_duplicate_journals
    FROM finance.journals
    WHERE tenant_id = v_tenant_id AND company_id = v_company_id;

    IF v_duplicate_journals = 0 THEN
        RAISE NOTICE '[PASS] 3. Duplicate Journal Check: Zero duplicate source_id entries detected.';
    ELSE
        RAISE WARNING '[FAIL] 3. Duplicate Journal Check: Found % duplicate journal entries.', v_duplicate_journals;
        v_all_passed := false;
    END IF;

    -- Check 4: Account 1200 debit total
    SELECT COALESCE(sum(jl.debit_functional), 0) INTO v_acc_1200_debit
    FROM finance.journal_lines jl
    JOIN finance.accounts a ON a.id = jl.account_id AND a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id
    WHERE jl.tenant_id = v_tenant_id AND jl.company_id = v_company_id AND a.account_code = '1200';

    IF v_acc_1200_debit = 2250.00 THEN
        RAISE NOTICE '[PASS] 4. Account 1200 (Merchandise Inventory): Total debit is exactly 2,250.00 PKR.';
    ELSE
        RAISE WARNING '[FAIL] 4. Account 1200: Total debit is % PKR (expected 2250.00 PKR).', v_acc_1200_debit;
        v_all_passed := false;
    END IF;

    -- Check 5: Account 2010 credit total
    SELECT COALESCE(sum(jl.credit_functional), 0) INTO v_acc_2010_credit
    FROM finance.journal_lines jl
    JOIN finance.accounts a ON a.id = jl.account_id AND a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id
    WHERE jl.tenant_id = v_tenant_id AND jl.company_id = v_company_id AND a.account_code = '2010';

    IF v_acc_2010_credit = 2250.00 THEN
        RAISE NOTICE '[PASS] 5. Account 2010 (Accounts Payable): Total credit is exactly 2,250.00 PKR.';
    ELSE
        RAISE WARNING '[FAIL] 5. Account 2010: Total credit is % PKR (expected 2250.00 PKR).', v_acc_2010_credit;
        v_all_passed := false;
    END IF;

    -- Check 6: Trial Balance equality
    SELECT COALESCE(sum(l.debit_functional), 0), COALESCE(sum(l.credit_functional), 0)
    INTO v_tb_debit, v_tb_credit
    FROM finance.journal_lines l
    JOIN finance.journals j ON j.id = l.journal_id AND j.tenant_id = l.tenant_id AND j.company_id = l.company_id AND j.status = 'posted'
    WHERE l.tenant_id = v_tenant_id AND l.company_id = v_company_id;

    IF v_tb_debit = 2250.00 AND v_tb_credit = 2250.00 AND v_tb_debit = v_tb_credit THEN
        RAISE NOTICE '[PASS] 6. Trial Balance Equality: Total Debits (%) = Total Credits (%) -> Strictly Balanced.', v_tb_debit, v_tb_credit;
    ELSE
        RAISE WARNING '[FAIL] 6. Trial Balance Equality: Debits=%, Credits=% (expected 2250.00, 2250.00).', v_tb_debit, v_tb_credit;
        v_all_passed := false;
    END IF;

    -- Final Verdict
    IF v_all_passed THEN
        RAISE NOTICE '==========================================================================';
        RAISE NOTICE '=== ALL FINANCE WORKSPACE & TRIAL BALANCE VERIFICATIONS PASSED         ===';
        RAISE NOTICE '==========================================================================';
    ELSE
        RAISE EXCEPTION 'FINANCE VERIFICATION FAILED: One or more assertions did not pass.';
    END IF;
END $$;
