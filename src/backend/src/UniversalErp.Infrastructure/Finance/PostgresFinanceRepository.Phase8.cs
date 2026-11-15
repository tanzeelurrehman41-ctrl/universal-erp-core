using Npgsql;
using UniversalErp.Application.Finance;

namespace UniversalErp.Infrastructure.Finance;

public sealed partial class PostgresFinanceRepository
{
    // ── 1. General Ledger Inquiry & Reversals ─────────────────────────────────

    public async Task<JournalSummary> ReverseJournalAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid journalId,
        string reason,
        CancellationToken cancellationToken)
    {
        var original = await GetJournalByIdAsync(tenantId, companyId, journalId, cancellationToken);
        if (original is null) throw new InvalidOperationException("JOURNAL_NOT_FOUND");
        if (original.Status != "posted") throw new InvalidOperationException("CANNOT_REVERSE_UNPOSTED_JOURNAL");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        var reversingJournalId = Guid.NewGuid();
        var reversingNo = $"REV-{original.JournalNo}";
        var idempotencyKey = $"reversal-{journalId:N}";
        var now = DateTimeOffset.UtcNow;
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        // 1. Insert reversing journal header
        const string insertHeaderSql = """
            INSERT INTO finance.journals
                (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id,
                 description, functional_currency, source_module, source_type, source_id,
                 idempotency_key, status, created_by, posted_by, posted_at, reverses_journal_id, created_at)
            VALUES
                (@id, @tenantId, @companyId, @journalNo, @journalDate, @fiscalPeriodId,
                 @description, @functionalCurrency, 'finance', 'reversal', @sourceId,
                 @idempotencyKey, 'posted', @userId, @userId, @postedAt, @reversesJournalId, now());
            """;

        await using (var cmd = new NpgsqlCommand(insertHeaderSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", reversingJournalId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("journalNo", reversingNo);
            cmd.Parameters.AddWithValue("journalDate", date.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("fiscalPeriodId", original.FiscalPeriodId);
            cmd.Parameters.AddWithValue("description", $"Reversal of {original.JournalNo}: {reason}");
            cmd.Parameters.AddWithValue("functionalCurrency", original.FunctionalCurrency);
            cmd.Parameters.AddWithValue("sourceId", journalId.ToString());
            cmd.Parameters.AddWithValue("idempotencyKey", idempotencyKey);
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("postedAt", now);
            cmd.Parameters.AddWithValue("reversesJournalId", journalId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2. Insert reversed lines (swap debits and credits)
        const string insertLineSql = """
            INSERT INTO finance.journal_lines
                (tenant_id, company_id, journal_id, line_no, account_id, description,
                 transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
            VALUES
                (@tenantId, @companyId, @journalId, @lineNo, @accountId, @description,
                 @currency, @amount, 1.0, @debit, @credit);
            """;

        for (var i = 0; i < original.Lines.Count; i++)
        {
            var origLine = original.Lines[i];
            await using var cmd = new NpgsqlCommand(insertLineSql, connection, transaction);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("journalId", reversingJournalId);
            cmd.Parameters.AddWithValue("lineNo", i + 1);
            cmd.Parameters.AddWithValue("accountId", origLine.AccountId);
            cmd.Parameters.AddWithValue("description", $"Reversal: {origLine.Description ?? reason}");
            cmd.Parameters.AddWithValue("currency", origLine.TransactionCurrency);
            cmd.Parameters.AddWithValue("amount", origLine.TransactionAmount);
            cmd.Parameters.AddWithValue("debit", origLine.CreditFunctional);  // Swapped
            cmd.Parameters.AddWithValue("credit", origLine.DebitFunctional);  // Swapped
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 3. Mark original journal as reversed
        const string updateOriginalSql = "UPDATE finance.journals SET status = 'reversed' WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @journalId;";
        await using (var updCmd = new NpgsqlCommand(updateOriginalSql, connection, transaction))
        {
            updCmd.Parameters.AddWithValue("tenantId", tenantId);
            updCmd.Parameters.AddWithValue("companyId", companyId);
            updCmd.Parameters.AddWithValue("journalId", journalId);
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 4. Log financial audit trail
        const string auditSql = """
            INSERT INTO finance.financial_audit_logs
                (tenant_id, company_id, id, event_type, entity_name, entity_id, actor_user_id, description, created_at)
            VALUES
                (@tenantId, @companyId, gen_random_uuid(), 'journal_reversed', 'journals', @journalId, @userId, @desc, now());
            """;
        await using (var audCmd = new NpgsqlCommand(auditSql, connection, transaction))
        {
            audCmd.Parameters.AddWithValue("tenantId", tenantId);
            audCmd.Parameters.AddWithValue("companyId", companyId);
            audCmd.Parameters.AddWithValue("journalId", journalId.ToString());
            audCmd.Parameters.AddWithValue("userId", userId);
            audCmd.Parameters.AddWithValue("desc", $"Reversed journal {original.JournalNo} with {reversingNo}. Reason: {reason}");
            await audCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (await GetJournalByIdAsync(tenantId, companyId, reversingJournalId, cancellationToken))!;
    }

    // ── 2. Bank Accounts ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BankAccountSummary>> ListBankAccountsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            SELECT b.id, b.account_number, b.account_name, b.bank_name, b.branch_name,
                   b.swift_bic, b.iban, b.currency_code, b.account_type, b.gl_account_id,
                   a.account_code, a.account_name AS gl_name, b.current_balance, b.is_active, b.created_at
            FROM finance.bank_accounts b
            JOIN finance.accounts a ON a.tenant_id = b.tenant_id AND a.company_id = b.company_id AND a.id = b.gl_account_id
            WHERE b.tenant_id = @tenantId AND b.company_id = @companyId
            ORDER BY b.account_name;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<BankAccountSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new BankAccountSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetGuid(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetDecimal(12),
                reader.GetBoolean(13),
                reader.GetFieldValue<DateTimeOffset>(14)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return list;
    }

    public async Task<BankAccountSummary> CreateBankAccountAsync(
        Guid tenantId,
        Guid companyId,
        CreateBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        var bankAccountId = Guid.NewGuid();
        const string sql = """
            INSERT INTO finance.bank_accounts
                (id, tenant_id, company_id, account_number, account_name, bank_name, branch_name,
                 swift_bic, iban, currency_code, account_type, gl_account_id, current_balance, is_active, created_at, updated_at)
            VALUES
                (@id, @tenantId, @companyId, @accNo, @accName, @bankName, @branch,
                 @swift, @iban, @currency, @accType, @glAccount, 0, true, now(), now())
            RETURNING id;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("id", bankAccountId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("accNo", request.AccountNumber.Trim());
        cmd.Parameters.AddWithValue("accName", request.AccountName.Trim());
        cmd.Parameters.AddWithValue("bankName", request.BankName.Trim());
        cmd.Parameters.AddWithValue("branch", (object?)request.BranchName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("swift", (object?)request.SwiftBic ?? DBNull.Value);
        cmd.Parameters.AddWithValue("iban", (object?)request.Iban ?? DBNull.Value);
        cmd.Parameters.AddWithValue("currency", request.CurrencyCode);
        cmd.Parameters.AddWithValue("accType", request.AccountType);
        cmd.Parameters.AddWithValue("glAccount", request.GlAccountId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetBankAccountAsync(tenantId, companyId, bankAccountId, cancellationToken))!;
    }

    public async Task<BankAccountSummary?> GetBankAccountAsync(
        Guid tenantId,
        Guid companyId,
        Guid bankAccountId,
        CancellationToken cancellationToken)
    {
        var accounts = await ListBankAccountsAsync(tenantId, companyId, cancellationToken);
        return accounts.FirstOrDefault(b => b.BankAccountId == bankAccountId);
    }

    // ── 3. Bank Statements & Reconciliation ────────────────────────────────────

    public async Task<BankStatementSummary> ImportBankStatementAsync(
        Guid tenantId,
        Guid companyId,
        ImportBankStatementRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        var statementId = Guid.NewGuid();

        const string stmtSql = """
            INSERT INTO finance.bank_statements
                (id, tenant_id, company_id, bank_account_id, statement_number, statement_date,
                 opening_balance, closing_balance, status, imported_at, created_at)
            VALUES
                (@id, @tenantId, @companyId, @bankAccountId, @stmtNo, @stmtDate,
                 @openBal, @closeBal, 'imported', now(), now());
            """;

        await using (var cmd = new NpgsqlCommand(stmtSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", statementId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("bankAccountId", request.BankAccountId);
            cmd.Parameters.AddWithValue("stmtNo", request.StatementNumber.Trim());
            cmd.Parameters.AddWithValue("stmtDate", request.StatementDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("openBal", request.OpeningBalance);
            cmd.Parameters.AddWithValue("closeBal", request.ClosingBalance);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        const string lineSql = """
            INSERT INTO finance.bank_statement_lines
                (id, tenant_id, company_id, statement_id, line_no, transaction_date, value_date,
                 reference_number, description, amount, balance_after, reconciled_status)
            VALUES
                (gen_random_uuid(), @tenantId, @companyId, @stmtId, @lineNo, @txDate, @valDate,
                 @refNo, @desc, @amount, @balAfter, 'unmatched');
            """;

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var l = request.Lines[i];
            await using var cmd = new NpgsqlCommand(lineSql, connection, transaction);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("stmtId", statementId);
            cmd.Parameters.AddWithValue("lineNo", i + 1);
            cmd.Parameters.AddWithValue("txDate", l.TransactionDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("valDate", l.ValueDate.HasValue ? l.ValueDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
            cmd.Parameters.AddWithValue("refNo", (object?)l.ReferenceNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("desc", l.Description.Trim());
            cmd.Parameters.AddWithValue("amount", l.Amount);
            cmd.Parameters.AddWithValue("balAfter", (object?)l.BalanceAfter ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var statements = await ListBankStatementsAsync(tenantId, companyId, request.BankAccountId, cancellationToken);
        return statements.First(s => s.StatementId == statementId);
    }

    public async Task<IReadOnlyList<BankStatementSummary>> ListBankStatementsAsync(
        Guid tenantId,
        Guid companyId,
        Guid? bankAccountId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string stmtSql = """
            SELECT s.id, s.bank_account_id, b.account_name, s.statement_number, s.statement_date,
                   s.opening_balance, s.closing_balance, s.status, s.imported_at, s.reconciled_at, s.reconciled_by
            FROM finance.bank_statements s
            JOIN finance.bank_accounts b ON b.tenant_id = s.tenant_id AND b.company_id = s.company_id AND b.id = s.bank_account_id
            WHERE s.tenant_id = @tenantId AND s.company_id = @companyId
              AND (@bankId::uuid IS NULL OR s.bank_account_id = @bankId)
            ORDER BY s.statement_date DESC, s.imported_at DESC;
            """;

        await using var cmd = new NpgsqlCommand(stmtSql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("bankId", (object?)bankAccountId ?? DBNull.Value);

        var statements = new List<BankStatementSummary>();
        var statementIds = new List<Guid>();

        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var sId = reader.GetGuid(0);
                statementIds.Add(sId);
                statements.Add(new BankStatementSummary(
                    sId,
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    DateOnly.FromDateTime(reader.GetDateTime(4)),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6),
                    reader.GetString(7),
                    reader.GetFieldValue<DateTimeOffset>(8),
                    reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                    reader.IsDBNull(10) ? null : reader.GetGuid(10),
                    []));
            }
            await reader.CloseAsync();
        }

        if (statements.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return statements;
        }

        const string lineSql = """
            SELECT id, statement_id, line_no, transaction_date, value_date, reference_number,
                   description, amount, balance_after, reconciled_status, matched_journal_line_id, matched_at
            FROM finance.bank_statement_lines
            WHERE tenant_id = @tenantId AND company_id = @companyId AND statement_id = ANY(@ids)
            ORDER BY statement_id, line_no;
            """;

        var linesMap = new Dictionary<Guid, List<BankStatementLineSummary>>();
        await using (var lCmd = new NpgsqlCommand(lineSql, connection, transaction))
        {
            lCmd.Parameters.AddWithValue("tenantId", tenantId);
            lCmd.Parameters.AddWithValue("companyId", companyId);
            lCmd.Parameters.AddWithValue("ids", statementIds.ToArray());

            await using var lReader = await lCmd.ExecuteReaderAsync(cancellationToken);
            while (await lReader.ReadAsync(cancellationToken))
            {
                var sId = lReader.GetGuid(1);
                if (!linesMap.TryGetValue(sId, out var list))
                {
                    list = [];
                    linesMap[sId] = list;
                }
                list.Add(new BankStatementLineSummary(
                    lReader.GetGuid(0),
                    lReader.GetInt32(2),
                    DateOnly.FromDateTime(lReader.GetDateTime(3)),
                    lReader.IsDBNull(4) ? null : DateOnly.FromDateTime(lReader.GetDateTime(4)),
                    lReader.IsDBNull(5) ? null : lReader.GetString(5),
                    lReader.GetString(6),
                    lReader.GetDecimal(7),
                    lReader.IsDBNull(8) ? null : lReader.GetDecimal(8),
                    lReader.GetString(9),
                    lReader.IsDBNull(10) ? null : lReader.GetGuid(10),
                    lReader.IsDBNull(11) ? null : lReader.GetFieldValue<DateTimeOffset>(11)));
            }
            await lReader.CloseAsync();
        }

        await transaction.CommitAsync(cancellationToken);
        return statements.Select(st => st with
        {
            Lines = linesMap.TryGetValue(st.StatementId, out var lines) ? lines : []
        }).ToList();
    }

    public async Task<BankReconciliationResult> ReconcileBankStatementAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid statementId,
        string? note,
        CancellationToken cancellationToken)
    {
        var statements = await ListBankStatementsAsync(tenantId, companyId, null, cancellationToken);
        var stmt = statements.FirstOrDefault(s => s.StatementId == statementId);
        if (stmt is null) throw new InvalidOperationException("STATEMENT_NOT_FOUND");

        var bankAccount = await GetBankAccountAsync(tenantId, companyId, stmt.BankAccountId, cancellationToken);
        if (bankAccount is null) throw new InvalidOperationException("BANK_ACCOUNT_NOT_FOUND");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        // Fetch GL journal lines for the bank account
        const string glSql = """
            SELECT jl.account_id, j.id AS journal_id, j.journal_no, j.journal_date,
                   (jl.debit_functional - jl.credit_functional) AS net_amount,
                   j.source_id AS ref_no
            FROM finance.journal_lines jl
            JOIN finance.journals j ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
            WHERE jl.tenant_id = @tenantId AND jl.company_id = @companyId
              AND jl.account_id = @glAccountId
              AND j.status = 'posted'
            ORDER BY j.journal_date;
            """;

        var glCandidates = new List<GlJournalMatchCandidate>();
        decimal glBookBalance = 0m;

        await using (var glCmd = new NpgsqlCommand(glSql, connection, transaction))
        {
            glCmd.Parameters.AddWithValue("tenantId", tenantId);
            glCmd.Parameters.AddWithValue("companyId", companyId);
            glCmd.Parameters.AddWithValue("glAccountId", bankAccount.GlAccountId);

            await using var glReader = await glCmd.ExecuteReaderAsync(cancellationToken);
            while (await glReader.ReadAsync(cancellationToken))
            {
                var net = glReader.GetDecimal(4);
                glBookBalance += net;
                glCandidates.Add(new GlJournalMatchCandidate(
                    Guid.NewGuid(),
                    glReader.GetGuid(1),
                    glReader.GetString(2),
                    DateOnly.FromDateTime(glReader.GetDateTime(3)),
                    net,
                    glReader.IsDBNull(5) ? null : glReader.GetString(5)));
            }
            await glReader.CloseAsync();
        }

        var statementCandidates = stmt.Lines.Select(l => new StatementMatchCandidate(
            l.LineId,
            l.LineNo,
            l.TransactionDate,
            l.ReferenceNumber,
            l.Amount)).ToList();

        var (matches, _, discrepancy) = BankReconciliationEngine.Reconcile(
            statementCandidates,
            glCandidates,
            stmt.OpeningBalance,
            stmt.ClosingBalance,
            glBookBalance);

        // Update matched lines
        const string updateLineSql = """
            UPDATE finance.bank_statement_lines
            SET reconciled_status = 'matched', matched_at = now()
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @lineId;
            """;

        foreach (var match in matches)
        {
            await using var updCmd = new NpgsqlCommand(updateLineSql, connection, transaction);
            updCmd.Parameters.AddWithValue("tenantId", tenantId);
            updCmd.Parameters.AddWithValue("companyId", companyId);
            updCmd.Parameters.AddWithValue("lineId", match.StatementLineId);
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var isFullyReconciled = matches.Count == stmt.Lines.Count && Math.Abs(discrepancy) < 0.01m;
        var newStatus = isFullyReconciled ? "reconciled" : (matches.Count > 0 ? "partially_reconciled" : "imported");

        const string updateStmtSql = """
            UPDATE finance.bank_statements
            SET status = @status, reconciled_at = now(), reconciled_by = @userId
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @stmtId;
            """;

        await using (var updStmtCmd = new NpgsqlCommand(updateStmtSql, connection, transaction))
        {
            updStmtCmd.Parameters.AddWithValue("status", newStatus);
            updStmtCmd.Parameters.AddWithValue("userId", userId);
            updStmtCmd.Parameters.AddWithValue("tenantId", tenantId);
            updStmtCmd.Parameters.AddWithValue("companyId", companyId);
            updStmtCmd.Parameters.AddWithValue("stmtId", statementId);
            await updStmtCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Also update bank account current_balance to closing balance when reconciled
        if (isFullyReconciled)
        {
            const string updateBankSql = "UPDATE finance.bank_accounts SET current_balance = @bal, updated_at = now() WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @bankId;";
            await using var updBankCmd = new NpgsqlCommand(updateBankSql, connection, transaction);
            updBankCmd.Parameters.AddWithValue("bal", stmt.ClosingBalance);
            updBankCmd.Parameters.AddWithValue("tenantId", tenantId);
            updBankCmd.Parameters.AddWithValue("companyId", companyId);
            updBankCmd.Parameters.AddWithValue("bankId", stmt.BankAccountId);
            await updBankCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new BankReconciliationResult(
            statementId,
            newStatus,
            matches.Count,
            stmt.Lines.Count,
            stmt.OpeningBalance,
            stmt.ClosingBalance,
            glBookBalance,
            discrepancy,
            isFullyReconciled);
    }

    // ── 4. Treasury Payments ──────────────────────────────────────────────────

    public async Task<TreasuryPaymentSummary> CreateTreasuryPaymentAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        CreateTreasuryPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var bankAccount = await GetBankAccountAsync(tenantId, companyId, request.BankAccountId, cancellationToken);
        if (bankAccount is null) throw new InvalidOperationException("BANK_ACCOUNT_NOT_FOUND");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        // Fetch active fiscal period
        Guid activePeriodId;
        const string periodSql = "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tenantId AND company_id = @companyId AND status = 'open' ORDER BY starts_on ASC LIMIT 1;";
        await using (var pCmd = new NpgsqlCommand(periodSql, connection, transaction))
        {
            pCmd.Parameters.AddWithValue("tenantId", tenantId);
            pCmd.Parameters.AddWithValue("companyId", companyId);
            var pVal = await pCmd.ExecuteScalarAsync(cancellationToken);
            if (pVal is not Guid pid) throw new InvalidOperationException("NO_OPEN_FISCAL_PERIOD: Cannot post treasury payment journal.");
            activePeriodId = pid;
        }

        var paymentId = Guid.NewGuid();
        var journalId = Guid.NewGuid();
        var journalNo = $"JRN-TRP-{request.PaymentNumber}";
        var idempotencyKey = $"treasury-pay-{paymentId:N}";
        var now = DateTimeOffset.UtcNow;

        // Determine Contra Account
        Guid contraAccountId;
        if (request.PaymentType == "inter_bank_transfer" && request.DestinationBankAccountId.HasValue)
        {
            var destAccount = await GetBankAccountAsync(tenantId, companyId, request.DestinationBankAccountId.Value, cancellationToken);
            if (destAccount is null) throw new InvalidOperationException("DESTINATION_BANK_ACCOUNT_NOT_FOUND");
            contraAccountId = destAccount.GlAccountId;
        }
        else if (request.PaymentType == "vendor_disbursement")
        {
            // Accounts Payable (2010)
            const string apSql = "SELECT id FROM finance.accounts WHERE tenant_id = @tenantId AND company_id = @companyId AND account_code = '2010' LIMIT 1;";
            await using var apCmd = new NpgsqlCommand(apSql, connection, transaction);
            apCmd.Parameters.AddWithValue("tenantId", tenantId);
            apCmd.Parameters.AddWithValue("companyId", companyId);
            contraAccountId = (Guid)(await apCmd.ExecuteScalarAsync(cancellationToken))!;
        }
        else if (request.PaymentType == "customer_collection")
        {
            // Accounts Receivable (1100)
            const string arSql = "SELECT id FROM finance.accounts WHERE tenant_id = @tenantId AND company_id = @companyId AND account_code = '1100' LIMIT 1;";
            await using var arCmd = new NpgsqlCommand(arSql, connection, transaction);
            arCmd.Parameters.AddWithValue("tenantId", tenantId);
            arCmd.Parameters.AddWithValue("companyId", companyId);
            contraAccountId = (Guid)(await arCmd.ExecuteScalarAsync(cancellationToken))!;
        }
        else
        {
            // Default: General & Administrative Expense (6010)
            const string expSql = "SELECT id FROM finance.accounts WHERE tenant_id = @tenantId AND company_id = @companyId AND account_code = '6010' LIMIT 1;";
            await using var expCmd = new NpgsqlCommand(expSql, connection, transaction);
            expCmd.Parameters.AddWithValue("tenantId", tenantId);
            expCmd.Parameters.AddWithValue("companyId", companyId);
            contraAccountId = (Guid)(await expCmd.ExecuteScalarAsync(cancellationToken))!;
        }

        // Post Journal Header
        const string jourSql = """
            INSERT INTO finance.journals
                (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id,
                 description, functional_currency, source_module, source_type, source_id,
                 idempotency_key, status, created_by, posted_by, posted_at, created_at)
            VALUES
                (@id, @tenantId, @companyId, @no, @date, @pid,
                 @desc, 'PKR', 'finance', 'treasury_payment', @sid,
                 @key, 'posted', @by, @by, now(), now());
            """;

        await using (var jCmd = new NpgsqlCommand(jourSql, connection, transaction))
        {
            jCmd.Parameters.AddWithValue("id", journalId);
            jCmd.Parameters.AddWithValue("tenantId", tenantId);
            jCmd.Parameters.AddWithValue("companyId", companyId);
            jCmd.Parameters.AddWithValue("no", journalNo);
            jCmd.Parameters.AddWithValue("date", request.PaymentDate.ToDateTime(TimeOnly.MinValue));
            jCmd.Parameters.AddWithValue("pid", activePeriodId);
            jCmd.Parameters.AddWithValue("desc", $"Treasury Payment {request.PaymentNumber}: {request.PayeePayerName}");
            jCmd.Parameters.AddWithValue("sid", paymentId.ToString());
            jCmd.Parameters.AddWithValue("key", idempotencyKey);
            jCmd.Parameters.AddWithValue("by", userId);
            await jCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Post Journal Lines
        // For disbursement / expense / transfer: Dr Contra Account, Cr Source Bank Account
        // For collection: Dr Bank Account, Cr Contra (AR)
        Guid debitAccId = request.PaymentType == "customer_collection" ? bankAccount.GlAccountId : contraAccountId;
        Guid creditAccId = request.PaymentType == "customer_collection" ? contraAccountId : bankAccount.GlAccountId;

        const string lineSql = """
            INSERT INTO finance.journal_lines
                (tenant_id, company_id, journal_id, line_no, account_id, description,
                 transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
            VALUES
                (@tenantId, @companyId, @jid, 1, @debitAcc, @desc1, 'PKR', @amt, 1.0, @amt, 0),
                (@tenantId, @companyId, @jid, 2, @creditAcc, @desc2, 'PKR', @amt, 1.0, 0, @amt);
            """;

        await using (var lCmd = new NpgsqlCommand(lineSql, connection, transaction))
        {
            lCmd.Parameters.AddWithValue("tenantId", tenantId);
            lCmd.Parameters.AddWithValue("companyId", companyId);
            lCmd.Parameters.AddWithValue("jid", journalId);
            lCmd.Parameters.AddWithValue("debitAcc", debitAccId);
            lCmd.Parameters.AddWithValue("desc1", $"Debit for {request.PaymentNumber}");
            lCmd.Parameters.AddWithValue("creditAcc", creditAccId);
            lCmd.Parameters.AddWithValue("desc2", $"Credit for {request.PaymentNumber}");
            lCmd.Parameters.AddWithValue("amt", request.Amount);
            await lCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Insert Treasury Payment Record
        const string paySql = """
            INSERT INTO finance.treasury_payments
                (id, tenant_id, company_id, payment_number, payment_type, payment_date,
                 bank_account_id, destination_bank_account_id, amount, currency_code,
                 payment_method, reference_number, payee_payer_name, gl_journal_id, status,
                 created_by, approved_by, created_at, updated_at)
            VALUES
                (@id, @tenantId, @companyId, @pno, @ptype, @pdate,
                 @bankId, @destBankId, @amt, @cur,
                 @method, @refNo, @payee, @jid, 'cleared',
                 @by, @by, now(), now());
            """;

        await using (var pCmd = new NpgsqlCommand(paySql, connection, transaction))
        {
            pCmd.Parameters.AddWithValue("id", paymentId);
            pCmd.Parameters.AddWithValue("tenantId", tenantId);
            pCmd.Parameters.AddWithValue("companyId", companyId);
            pCmd.Parameters.AddWithValue("pno", request.PaymentNumber.Trim());
            pCmd.Parameters.AddWithValue("ptype", request.PaymentType);
            pCmd.Parameters.AddWithValue("pdate", request.PaymentDate.ToDateTime(TimeOnly.MinValue));
            pCmd.Parameters.AddWithValue("bankId", request.BankAccountId);
            pCmd.Parameters.AddWithValue("destBankId", (object?)request.DestinationBankAccountId ?? DBNull.Value);
            pCmd.Parameters.AddWithValue("amt", request.Amount);
            pCmd.Parameters.AddWithValue("cur", request.CurrencyCode);
            pCmd.Parameters.AddWithValue("method", request.PaymentMethod);
            pCmd.Parameters.AddWithValue("refNo", (object?)request.ReferenceNumber ?? DBNull.Value);
            pCmd.Parameters.AddWithValue("payee", request.PayeePayerName.Trim());
            pCmd.Parameters.AddWithValue("jid", journalId);
            pCmd.Parameters.AddWithValue("by", userId);
            await pCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Update Bank Account Current Balance
        decimal balanceDelta = request.PaymentType == "customer_collection" ? request.Amount : -request.Amount;
        const string updBalSql = "UPDATE finance.bank_accounts SET current_balance = current_balance + @delta, updated_at = now() WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @bankId;";
        await using (var ubCmd = new NpgsqlCommand(updBalSql, connection, transaction))
        {
            ubCmd.Parameters.AddWithValue("delta", balanceDelta);
            ubCmd.Parameters.AddWithValue("tenantId", tenantId);
            ubCmd.Parameters.AddWithValue("companyId", companyId);
            ubCmd.Parameters.AddWithValue("bankId", request.BankAccountId);
            await ubCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (request.PaymentType == "inter_bank_transfer" && request.DestinationBankAccountId.HasValue)
        {
            await using var ubDestCmd = new NpgsqlCommand(updBalSql, connection, transaction);
            ubDestCmd.Parameters.AddWithValue("delta", request.Amount);
            ubDestCmd.Parameters.AddWithValue("tenantId", tenantId);
            ubDestCmd.Parameters.AddWithValue("companyId", companyId);
            ubDestCmd.Parameters.AddWithValue("bankId", request.DestinationBankAccountId.Value);
            await ubDestCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var payments = await ListTreasuryPaymentsAsync(tenantId, companyId, cancellationToken);
        return payments.First(p => p.PaymentId == paymentId);
    }

    public async Task<IReadOnlyList<TreasuryPaymentSummary>> ListTreasuryPaymentsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            SELECT p.id, p.payment_number, p.payment_type, p.payment_date,
                   p.bank_account_id, b.account_name, p.destination_bank_account_id,
                   p.amount, p.currency_code, p.payment_method, p.reference_number,
                   p.payee_payer_name, p.gl_journal_id, p.status, p.created_by, p.approved_by, p.created_at
            FROM finance.treasury_payments p
            JOIN finance.bank_accounts b ON b.tenant_id = p.tenant_id AND b.company_id = p.company_id AND b.id = p.bank_account_id
            WHERE p.tenant_id = @tenantId AND p.company_id = @companyId
            ORDER BY p.payment_date DESC, p.created_at DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<TreasuryPaymentSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new TreasuryPaymentSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetDecimal(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetGuid(12),
                reader.GetString(13),
                reader.GetGuid(14),
                reader.IsDBNull(15) ? null : reader.GetGuid(15),
                reader.GetFieldValue<DateTimeOffset>(16)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return list;
    }

    // ── 5. Tax Codes & Tax Filings ────────────────────────────────────────────

    public async Task<IReadOnlyList<TaxCodeSummary>> ListTaxCodesAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            SELECT t.id, t.code, t.name, t.tax_type, t.rate_percent,
                   t.gl_payable_account_id, a.account_code, t.gl_receivable_account_id, t.is_active
            FROM finance.tax_codes t
            JOIN finance.accounts a ON a.tenant_id = t.tenant_id AND a.company_id = t.company_id AND a.id = t.gl_payable_account_id
            WHERE t.tenant_id = @tenantId AND t.company_id = @companyId
            ORDER BY t.code;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<TaxCodeSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new TaxCodeSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetGuid(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.GetBoolean(8)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return list;
    }

    public async Task<TaxCodeSummary> CreateTaxCodeAsync(
        Guid tenantId,
        Guid companyId,
        CreateTaxCodeRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        var taxCodeId = Guid.NewGuid();
        const string sql = """
            INSERT INTO finance.tax_codes
                (id, tenant_id, company_id, code, name, tax_type, rate_percent, gl_payable_account_id, gl_receivable_account_id, is_active, created_at)
            VALUES
                (@id, @tenantId, @companyId, @code, @name, @type, @rate, @payable, @receivable, true, now());
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("id", taxCodeId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("code", request.Code.Trim());
        cmd.Parameters.AddWithValue("name", request.Name.Trim());
        cmd.Parameters.AddWithValue("type", request.TaxType);
        cmd.Parameters.AddWithValue("rate", request.RatePercent);
        cmd.Parameters.AddWithValue("payable", request.GlPayableAccountId);
        cmd.Parameters.AddWithValue("receivable", (object?)request.GlReceivableAccountId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var all = await ListTaxCodesAsync(tenantId, companyId, cancellationToken);
        return all.First(t => t.TaxCodeId == taxCodeId);
    }

    public async Task<TaxLiabilitySummary> GetTaxLiabilitySummaryAsync(
        Guid tenantId,
        Guid companyId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        // Sales Tax Output (2100)
        const string taxSql = """
            SELECT COALESCE(SUM(jl.credit_functional), 0), COALESCE(SUM(jl.debit_functional), 0)
            FROM finance.journal_lines jl
            JOIN finance.accounts a ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
            JOIN finance.journals j ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
            WHERE jl.tenant_id = @tenantId AND jl.company_id = @companyId
              AND a.account_code = '2100'
              AND j.status = 'posted'
              AND j.journal_date >= @from AND j.journal_date <= @to;
            """;

        decimal outputTax = 0m;
        await using (var cmd = new NpgsqlCommand(taxSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("from", fromDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("to", toDate.ToDateTime(TimeOnly.MinValue));

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var credits = reader.GetDecimal(0);
                var debits = reader.GetDecimal(1);
                outputTax = Math.Max(0m, credits - debits);
            }
            await reader.CloseAsync();
        }

        // Sales revenue total
        const string revSql = """
            SELECT COALESCE(SUM(jl.credit_functional - jl.debit_functional), 0)
            FROM finance.journal_lines jl
            JOIN finance.accounts a ON a.tenant_id = jl.tenant_id AND a.company_id = jl.company_id AND a.id = jl.account_id
            JOIN finance.journals j ON j.tenant_id = jl.tenant_id AND j.company_id = jl.company_id AND j.id = jl.journal_id
            WHERE jl.tenant_id = @tenantId AND jl.company_id = @companyId
              AND a.account_code = '4010'
              AND j.status = 'posted'
              AND j.journal_date >= @from AND j.journal_date <= @to;
            """;

        decimal salesAmount = 0m;
        await using (var cmd = new NpgsqlCommand(revSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("from", fromDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("to", toDate.ToDateTime(TimeOnly.MinValue));
            salesAmount = Convert.ToDecimal(await cmd.ExecuteScalarAsync(cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);

        return new TaxLiabilitySummary(
            TotalSalesAmount: salesAmount,
            TotalPurchasesAmount: 0m,
            TotalOutputTax: outputTax,
            TotalInputTax: 0m,
            NetTaxPayable: outputTax);
    }

    public async Task<TaxFilingSummary> CreateTaxFilingAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        CreateTaxFilingRequest request,
        CancellationToken cancellationToken)
    {
        var summary = await GetTaxLiabilitySummaryAsync(tenantId, companyId, request.PeriodStart, request.PeriodEnd, cancellationToken);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        var filingId = Guid.NewGuid();
        const string sql = """
            INSERT INTO finance.tax_filings
                (id, tenant_id, company_id, filing_number, period_start, period_end, tax_type,
                 total_taxable_amount, output_tax_amount, input_tax_amount, net_tax_payable,
                 status, filed_at, notes, created_at)
            VALUES
                (@id, @tenantId, @companyId, @no, @start, @end, @type,
                 @taxable, @output, @input, @net,
                 'filed', now(), @notes, now());
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("id", filingId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("no", request.FilingNumber.Trim());
        cmd.Parameters.AddWithValue("start", request.PeriodStart.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("end", request.PeriodEnd.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("type", request.TaxType);
        cmd.Parameters.AddWithValue("taxable", summary.TotalSalesAmount);
        cmd.Parameters.AddWithValue("output", summary.TotalOutputTax);
        cmd.Parameters.AddWithValue("input", summary.TotalInputTax);
        cmd.Parameters.AddWithValue("net", summary.NetTaxPayable);
        cmd.Parameters.AddWithValue("notes", (object?)request.Notes ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var filings = await ListTaxFilingsAsync(tenantId, companyId, cancellationToken);
        return filings.First(f => f.FilingId == filingId);
    }

    public async Task<IReadOnlyList<TaxFilingSummary>> ListTaxFilingsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            SELECT id, filing_number, period_start, period_end, tax_type,
                   total_taxable_amount, output_tax_amount, input_tax_amount, net_tax_payable,
                   status, filed_at, gl_settlement_journal_id, notes
            FROM finance.tax_filings
            WHERE tenant_id = @tenantId AND company_id = @companyId
            ORDER BY period_end DESC, created_at DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<TaxFilingSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new TaxFilingSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                reader.GetString(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return list;
    }

    public async Task<TaxFilingSummary> SettleTaxFilingAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid filingId,
        SettleTaxFilingRequest request,
        CancellationToken cancellationToken)
    {
        var filings = await ListTaxFilingsAsync(tenantId, companyId, cancellationToken);
        var filing = filings.FirstOrDefault(f => f.FilingId == filingId);
        if (filing is null) throw new InvalidOperationException("TAX_FILING_NOT_FOUND");
        if (filing.Status == "settled") return filing;

        var bankAccount = await GetBankAccountAsync(tenantId, companyId, request.BankAccountId, cancellationToken);
        if (bankAccount is null) throw new InvalidOperationException("BANK_ACCOUNT_NOT_FOUND");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        // Fetch tax payable GL account (2100)
        Guid taxPayableAccount;
        const string taxAccSql = "SELECT id FROM finance.accounts WHERE tenant_id = @tenantId AND company_id = @companyId AND account_code = '2100' LIMIT 1;";
        await using (var cmd = new NpgsqlCommand(taxAccSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            taxPayableAccount = (Guid)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        }

        // Active fiscal period
        Guid activePeriodId;
        const string periodSql = "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tenantId AND company_id = @companyId AND status = 'open' ORDER BY starts_on ASC LIMIT 1;";
        await using (var pCmd = new NpgsqlCommand(periodSql, connection, transaction))
        {
            pCmd.Parameters.AddWithValue("tenantId", tenantId);
            pCmd.Parameters.AddWithValue("companyId", companyId);
            activePeriodId = (Guid)(await pCmd.ExecuteScalarAsync(cancellationToken))!;
        }

        Guid? settlementJournalId = null;
        if (filing.NetTaxPayable > 0)
        {
            var journalId = Guid.NewGuid();
            var journalNo = $"JRN-TAX-{filing.FilingNumber}";
            var idempotencyKey = $"tax-settle-{filingId:N}";

            // Post Settlement Journal: Dr 2100 Tax Output, Cr Bank Account
            const string jourSql = """
                INSERT INTO finance.journals
                    (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id,
                     description, functional_currency, source_module, source_type, source_id,
                     idempotency_key, status, created_by, posted_by, posted_at, created_at)
                VALUES
                    (@id, @tenantId, @companyId, @no, now(), @pid,
                     @desc, 'PKR', 'finance', 'tax_settlement', @sid,
                     @key, 'posted', @by, @by, now(), now());
                """;

            await using (var jCmd = new NpgsqlCommand(jourSql, connection, transaction))
            {
                jCmd.Parameters.AddWithValue("id", journalId);
                jCmd.Parameters.AddWithValue("tenantId", tenantId);
                jCmd.Parameters.AddWithValue("companyId", companyId);
                jCmd.Parameters.AddWithValue("no", journalNo);
                jCmd.Parameters.AddWithValue("pid", activePeriodId);
                jCmd.Parameters.AddWithValue("desc", $"Tax Settlement for Filing {filing.FilingNumber}");
                jCmd.Parameters.AddWithValue("sid", filingId.ToString());
                jCmd.Parameters.AddWithValue("key", idempotencyKey);
                jCmd.Parameters.AddWithValue("by", userId);
                await jCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            const string lineSql = """
                INSERT INTO finance.journal_lines
                    (tenant_id, company_id, journal_id, line_no, account_id, description,
                     transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
                VALUES
                    (@tenantId, @companyId, @jid, 1, @taxAcc, 'Tax Liability Settlement', 'PKR', @amt, 1.0, @amt, 0),
                    (@tenantId, @companyId, @jid, 2, @bankAcc, 'Tax Payment from Bank', 'PKR', @amt, 1.0, 0, @amt);
                """;

            await using (var lCmd = new NpgsqlCommand(lineSql, connection, transaction))
            {
                lCmd.Parameters.AddWithValue("tenantId", tenantId);
                lCmd.Parameters.AddWithValue("companyId", companyId);
                lCmd.Parameters.AddWithValue("jid", journalId);
                lCmd.Parameters.AddWithValue("taxAcc", taxPayableAccount);
                lCmd.Parameters.AddWithValue("bankAcc", bankAccount.GlAccountId);
                lCmd.Parameters.AddWithValue("amt", filing.NetTaxPayable);
                await lCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            settlementJournalId = journalId;
        }

        // Update filing status
        const string updFilingSql = """
            UPDATE finance.tax_filings
            SET status = 'settled', gl_settlement_journal_id = @jid
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @fid;
            """;
        await using (var ufCmd = new NpgsqlCommand(updFilingSql, connection, transaction))
        {
            ufCmd.Parameters.AddWithValue("jid", (object?)settlementJournalId ?? DBNull.Value);
            ufCmd.Parameters.AddWithValue("tenantId", tenantId);
            ufCmd.Parameters.AddWithValue("companyId", companyId);
            ufCmd.Parameters.AddWithValue("fid", filingId);
            await ufCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Update bank balance
        const string updBankSql = "UPDATE finance.bank_accounts SET current_balance = current_balance - @amt, updated_at = now() WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @bid;";
        await using (var ubCmd = new NpgsqlCommand(updBankSql, connection, transaction))
        {
            ubCmd.Parameters.AddWithValue("amt", filing.NetTaxPayable);
            ubCmd.Parameters.AddWithValue("tenantId", tenantId);
            ubCmd.Parameters.AddWithValue("companyId", companyId);
            ubCmd.Parameters.AddWithValue("bid", request.BankAccountId);
            await ubCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var refreshed = await ListTaxFilingsAsync(tenantId, companyId, cancellationToken);
        return refreshed.First(f => f.FilingId == filingId);
    }

    // ── 6. Accruals & Recurring Schedules ─────────────────────────────────────

    public async Task<RecurringScheduleSummary> CreateRecurringScheduleAsync(
        Guid tenantId,
        Guid companyId,
        CreateRecurringScheduleRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        var scheduleId = Guid.NewGuid();
        const string sql = """
            INSERT INTO finance.recurring_schedules
                (id, tenant_id, company_id, schedule_name, schedule_type, frequency,
                 start_date, end_date, total_amount, period_amount, debit_account_id,
                 credit_account_id, periods_count, posted_periods_count, next_run_date,
                 auto_reverse_accrual, status, description_template, created_at)
            VALUES
                (@id, @tenantId, @companyId, @name, @type, @freq,
                 @start, @end, @total, @period, @debit,
                 @credit, @count, 0, @next,
                 @autoRev, 'active', @template, now());
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("id", scheduleId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("name", request.ScheduleName.Trim());
        cmd.Parameters.AddWithValue("type", request.ScheduleType);
        cmd.Parameters.AddWithValue("freq", request.Frequency);
        cmd.Parameters.AddWithValue("start", request.StartDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("end", request.EndDate.HasValue ? request.EndDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("total", request.TotalAmount);
        cmd.Parameters.AddWithValue("period", request.PeriodAmount);
        cmd.Parameters.AddWithValue("debit", request.DebitAccountId);
        cmd.Parameters.AddWithValue("credit", request.CreditAccountId);
        cmd.Parameters.AddWithValue("count", request.PeriodsCount);
        cmd.Parameters.AddWithValue("next", request.StartDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("autoRev", request.AutoReverseAccrual);
        cmd.Parameters.AddWithValue("template", (object?)request.DescriptionTemplate ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var schedules = await ListRecurringSchedulesAsync(tenantId, companyId, cancellationToken);
        return schedules.First(s => s.ScheduleId == scheduleId);
    }

    public async Task<IReadOnlyList<RecurringScheduleSummary>> ListRecurringSchedulesAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            SELECT s.id, s.schedule_name, s.schedule_type, s.frequency, s.start_date, s.end_date,
                   s.total_amount, s.period_amount, s.debit_account_id, da.account_code, da.account_name,
                   s.credit_account_id, ca.account_code, ca.account_name, s.periods_count,
                   s.posted_periods_count, s.next_run_date, s.auto_reverse_accrual, s.status, s.description_template
            FROM finance.recurring_schedules s
            JOIN finance.accounts da ON da.tenant_id = s.tenant_id AND da.company_id = s.company_id AND da.id = s.debit_account_id
            JOIN finance.accounts ca ON ca.tenant_id = s.tenant_id AND ca.company_id = s.company_id AND ca.id = s.credit_account_id
            WHERE s.tenant_id = @tenantId AND s.company_id = @companyId
            ORDER BY s.next_run_date, s.schedule_name;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<RecurringScheduleSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RecurringScheduleSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateOnly.FromDateTime(reader.GetDateTime(4)),
                reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetGuid(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetGuid(11),
                reader.GetString(12),
                reader.GetString(13),
                reader.GetInt32(14),
                reader.GetInt32(15),
                DateOnly.FromDateTime(reader.GetDateTime(16)),
                reader.GetBoolean(17),
                reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return list;
    }

    public async Task<JournalSummary> PostRecurringSchedulePeriodAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        var schedules = await ListRecurringSchedulesAsync(tenantId, companyId, cancellationToken);
        var sched = schedules.FirstOrDefault(s => s.ScheduleId == scheduleId);
        if (sched is null) throw new InvalidOperationException("SCHEDULE_NOT_FOUND");
        if (sched.Status != "active") throw new InvalidOperationException("SCHEDULE_NOT_ACTIVE");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        // Fetch open period
        Guid activePeriodId;
        const string pSql = "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tenantId AND company_id = @companyId AND status = 'open' ORDER BY starts_on ASC LIMIT 1;";
        await using (var pCmd = new NpgsqlCommand(pSql, connection, transaction))
        {
            pCmd.Parameters.AddWithValue("tenantId", tenantId);
            pCmd.Parameters.AddWithValue("companyId", companyId);
            activePeriodId = (Guid)(await pCmd.ExecuteScalarAsync(cancellationToken))!;
        }

        var journalId = Guid.NewGuid();
        var journalNo = $"JRN-REC-{sched.ScheduleName.Replace(" ", "-").ToUpper()}-{sched.PostedPeriodsCount + 1}";
        var idempotencyKey = $"recurring-{scheduleId:N}-{sched.PostedPeriodsCount + 1}";
        var desc = sched.DescriptionTemplate ?? $"{sched.ScheduleName} - Period {sched.PostedPeriodsCount + 1}";

        // Post Journal Header
        const string jSql = """
            INSERT INTO finance.journals
                (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id,
                 description, functional_currency, source_module, source_type, source_id,
                 idempotency_key, status, created_by, posted_by, posted_at, created_at)
            VALUES
                (@id, @tenantId, @companyId, @no, now(), @pid,
                 @desc, 'PKR', 'finance', 'recurring_schedule', @sid,
                 @key, 'posted', @by, @by, now(), now());
            """;

        await using (var jCmd = new NpgsqlCommand(jSql, connection, transaction))
        {
            jCmd.Parameters.AddWithValue("id", journalId);
            jCmd.Parameters.AddWithValue("tenantId", tenantId);
            jCmd.Parameters.AddWithValue("companyId", companyId);
            jCmd.Parameters.AddWithValue("no", journalNo);
            jCmd.Parameters.AddWithValue("pid", activePeriodId);
            jCmd.Parameters.AddWithValue("desc", desc);
            jCmd.Parameters.AddWithValue("sid", scheduleId.ToString());
            jCmd.Parameters.AddWithValue("key", idempotencyKey);
            jCmd.Parameters.AddWithValue("by", userId);
            await jCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Post Lines
        const string lSql = """
            INSERT INTO finance.journal_lines
                (tenant_id, company_id, journal_id, line_no, account_id, description,
                 transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
            VALUES
                (@tenantId, @companyId, @jid, 1, @debit, @desc, 'PKR', @amt, 1.0, @amt, 0),
                (@tenantId, @companyId, @jid, 2, @credit, @desc, 'PKR', @amt, 1.0, 0, @amt);
            """;

        await using (var lCmd = new NpgsqlCommand(lSql, connection, transaction))
        {
            lCmd.Parameters.AddWithValue("tenantId", tenantId);
            lCmd.Parameters.AddWithValue("companyId", companyId);
            lCmd.Parameters.AddWithValue("jid", journalId);
            lCmd.Parameters.AddWithValue("debit", sched.DebitAccountId);
            lCmd.Parameters.AddWithValue("credit", sched.CreditAccountId);
            lCmd.Parameters.AddWithValue("desc", desc);
            lCmd.Parameters.AddWithValue("amt", sched.PeriodAmount);
            await lCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Advance schedule
        var newPostedCount = sched.PostedPeriodsCount + 1;
        var nextDate = sched.NextRunDate.AddMonths(sched.Frequency == "quarterly" ? 3 : (sched.Frequency == "annual" ? 12 : 1));
        var newStatus = newPostedCount >= sched.PeriodsCount ? "completed" : "active";

        const string updSchedSql = """
            UPDATE finance.recurring_schedules
            SET posted_periods_count = @cnt, next_run_date = @next, status = @status
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id;
            """;

        await using (var uCmd = new NpgsqlCommand(updSchedSql, connection, transaction))
        {
            uCmd.Parameters.AddWithValue("cnt", newPostedCount);
            uCmd.Parameters.AddWithValue("next", nextDate.ToDateTime(TimeOnly.MinValue));
            uCmd.Parameters.AddWithValue("status", newStatus);
            uCmd.Parameters.AddWithValue("tenantId", tenantId);
            uCmd.Parameters.AddWithValue("companyId", companyId);
            uCmd.Parameters.AddWithValue("id", scheduleId);
            await uCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (await GetJournalByIdAsync(tenantId, companyId, journalId, cancellationToken))!;
    }

    // ── 7. Fixed Assets & Depreciation ────────────────────────────────────────

    public async Task<FixedAssetSummary> CreateFixedAssetAsync(
        Guid tenantId,
        Guid companyId,
        CreateFixedAssetRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        var assetId = Guid.NewGuid();
        const string sql = """
            INSERT INTO finance.fixed_assets
                (id, tenant_id, company_id, asset_code, asset_name, asset_category, acquisition_date,
                 acquisition_cost, salvage_value, useful_life_months, depreciation_method,
                 asset_gl_account_id, accumulated_depr_account_id, depreciation_expense_account_id,
                 current_book_value, accumulated_depreciation, status, created_at, updated_at)
            VALUES
                (@id, @tenantId, @companyId, @code, @name, @cat, @acqDate,
                 @cost, @salvage, @life, @method,
                 @assetGl, @accumGl, @expGl,
                 @cost, 0, 'active', now(), now());
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("id", assetId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("code", request.AssetCode.Trim());
        cmd.Parameters.AddWithValue("name", request.AssetName.Trim());
        cmd.Parameters.AddWithValue("cat", request.AssetCategory);
        cmd.Parameters.AddWithValue("acqDate", request.AcquisitionDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("cost", request.AcquisitionCost);
        cmd.Parameters.AddWithValue("salvage", request.SalvageValue);
        cmd.Parameters.AddWithValue("life", request.UsefulLifeMonths);
        cmd.Parameters.AddWithValue("method", request.DepreciationMethod);
        cmd.Parameters.AddWithValue("assetGl", request.AssetGlAccountId);
        cmd.Parameters.AddWithValue("accumGl", request.AccumulatedDeprAccountId);
        cmd.Parameters.AddWithValue("expGl", request.DepreciationExpenseAccountId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var assets = await ListFixedAssetsAsync(tenantId, companyId, cancellationToken);
        return assets.First(a => a.AssetId == assetId);
    }

    public async Task<IReadOnlyList<FixedAssetSummary>> ListFixedAssetsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            SELECT f.id, f.asset_code, f.asset_name, f.asset_category, f.acquisition_date,
                   f.acquisition_cost, f.salvage_value, f.useful_life_months, f.depreciation_method,
                   f.asset_gl_account_id, agl.account_code, f.accumulated_depr_account_id, dgl.account_code,
                   f.depreciation_expense_account_id, egl.account_code, f.current_book_value, f.accumulated_depreciation, f.status
            FROM finance.fixed_assets f
            JOIN finance.accounts agl ON agl.tenant_id = f.tenant_id AND agl.company_id = f.company_id AND agl.id = f.asset_gl_account_id
            JOIN finance.accounts dgl ON dgl.tenant_id = f.tenant_id AND dgl.company_id = f.company_id AND dgl.id = f.accumulated_depr_account_id
            JOIN finance.accounts egl ON egl.tenant_id = f.tenant_id AND egl.company_id = f.company_id AND egl.id = f.depreciation_expense_account_id
            WHERE f.tenant_id = @tenantId AND f.company_id = @companyId
            ORDER BY f.asset_code;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<FixedAssetSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new FixedAssetSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateOnly.FromDateTime(reader.GetDateTime(4)),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetInt32(7),
                reader.GetString(8),
                reader.GetGuid(9),
                reader.GetString(10),
                reader.GetGuid(11),
                reader.GetString(12),
                reader.GetGuid(13),
                reader.GetString(14),
                reader.GetDecimal(15),
                reader.GetDecimal(16),
                reader.GetString(17)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return list;
    }

    public async Task<IReadOnlyList<AssetDepreciationRunSummary>> ExecuteDepreciationRunAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        ExecuteDepreciationRunRequest request,
        CancellationToken cancellationToken)
    {
        var allAssets = await ListFixedAssetsAsync(tenantId, companyId, cancellationToken);
        var targetAssets = request.SpecificAssetId.HasValue
            ? allAssets.Where(a => a.AssetId == request.SpecificAssetId.Value && a.Status == "active").ToList()
            : allAssets.Where(a => a.Status == "active" && a.CurrentBookValue > a.SalvageValue).ToList();

        if (targetAssets.Count == 0) return [];

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        var results = new List<AssetDepreciationRunSummary>();

        foreach (var asset in targetAssets)
        {
            var deprAmount = DepreciationEngine.CalculatePeriodDepreciation(
                asset.AcquisitionCost,
                asset.SalvageValue,
                asset.UsefulLifeMonths,
                asset.AccumulatedDepreciation,
                asset.DepreciationMethod);

            if (deprAmount <= 0) continue;

            var (newAccumulated, newBookValue, isFullyDepr) = DepreciationEngine.UpdateAssetDepreciation(
                asset.AcquisitionCost,
                asset.SalvageValue,
                asset.AccumulatedDepreciation,
                deprAmount);

            var runId = Guid.NewGuid();
            var journalId = Guid.NewGuid();
            var runNumber = $"DEP-{asset.AssetCode}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var journalNo = $"JRN-DEP-{asset.AssetCode}-{DateTime.UtcNow:yyyyMMdd}";
            var idempotencyKey = $"depr-{runId:N}";

            // 1. Post Journal
            const string jSql = """
                INSERT INTO finance.journals
                    (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id,
                     description, functional_currency, source_module, source_type, source_id,
                     idempotency_key, status, created_by, posted_by, posted_at, created_at)
                VALUES
                    (@id, @tenantId, @companyId, @no, @date, @pid,
                     @desc, 'PKR', 'finance', 'depreciation_run', @sid,
                     @key, 'posted', @by, @by, now(), now());
                """;

            await using (var jCmd = new NpgsqlCommand(jSql, connection, transaction))
            {
                jCmd.Parameters.AddWithValue("id", journalId);
                jCmd.Parameters.AddWithValue("tenantId", tenantId);
                jCmd.Parameters.AddWithValue("companyId", companyId);
                jCmd.Parameters.AddWithValue("no", journalNo);
                jCmd.Parameters.AddWithValue("date", request.RunDate.ToDateTime(TimeOnly.MinValue));
                jCmd.Parameters.AddWithValue("pid", request.FiscalPeriodId);
                jCmd.Parameters.AddWithValue("desc", $"Monthly Depreciation: {asset.AssetName} ({asset.AssetCode})");
                jCmd.Parameters.AddWithValue("sid", runId.ToString());
                jCmd.Parameters.AddWithValue("key", idempotencyKey);
                jCmd.Parameters.AddWithValue("by", userId);
                await jCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // 2. Post Journal Lines: Dr Depreciation Expense / Cr Accumulated Depreciation
            const string lSql = """
                INSERT INTO finance.journal_lines
                    (tenant_id, company_id, journal_id, line_no, account_id, description,
                     transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
                VALUES
                    (@tenantId, @companyId, @jid, 1, @expAcc, @desc, 'PKR', @amt, 1.0, @amt, 0),
                    (@tenantId, @companyId, @jid, 2, @accumAcc, @desc, 'PKR', @amt, 1.0, 0, @amt);
                """;

            await using (var lCmd = new NpgsqlCommand(lSql, connection, transaction))
            {
                lCmd.Parameters.AddWithValue("tenantId", tenantId);
                lCmd.Parameters.AddWithValue("companyId", companyId);
                lCmd.Parameters.AddWithValue("jid", journalId);
                lCmd.Parameters.AddWithValue("expAcc", asset.DepreciationExpenseAccountId);
                lCmd.Parameters.AddWithValue("accumAcc", asset.AccumulatedDeprAccountId);
                lCmd.Parameters.AddWithValue("desc", $"Depreciation for {asset.AssetCode}");
                lCmd.Parameters.AddWithValue("amt", deprAmount);
                await lCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // 3. Update Fixed Asset Record
            const string updAssetSql = """
                UPDATE finance.fixed_assets
                SET accumulated_depreciation = @accum, current_book_value = @book,
                    status = @status, updated_at = now()
                WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @aid;
                """;

            await using (var aCmd = new NpgsqlCommand(updAssetSql, connection, transaction))
            {
                aCmd.Parameters.AddWithValue("accum", newAccumulated);
                aCmd.Parameters.AddWithValue("book", newBookValue);
                aCmd.Parameters.AddWithValue("status", isFullyDepr ? "fully_depreciated" : "active");
                aCmd.Parameters.AddWithValue("tenantId", tenantId);
                aCmd.Parameters.AddWithValue("companyId", companyId);
                aCmd.Parameters.AddWithValue("aid", asset.AssetId);
                await aCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // 4. Record Run
            const string runSql = """
                INSERT INTO finance.asset_depreciation_runs
                    (id, tenant_id, company_id, run_number, run_date, fiscal_period_id, asset_id,
                     depreciation_amount, gl_journal_id, status, created_at)
                VALUES
                    (@id, @tenantId, @companyId, @runNo, @date, @pid, @aid,
                     @amt, @jid, 'posted', now());
                """;

            await using (var rCmd = new NpgsqlCommand(runSql, connection, transaction))
            {
                rCmd.Parameters.AddWithValue("id", runId);
                rCmd.Parameters.AddWithValue("tenantId", tenantId);
                rCmd.Parameters.AddWithValue("companyId", companyId);
                rCmd.Parameters.AddWithValue("runNo", runNumber);
                rCmd.Parameters.AddWithValue("date", request.RunDate.ToDateTime(TimeOnly.MinValue));
                rCmd.Parameters.AddWithValue("pid", request.FiscalPeriodId);
                rCmd.Parameters.AddWithValue("aid", asset.AssetId);
                rCmd.Parameters.AddWithValue("amt", deprAmount);
                rCmd.Parameters.AddWithValue("jid", journalId);
                await rCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            results.Add(new AssetDepreciationRunSummary(
                runId,
                runNumber,
                request.RunDate,
                request.FiscalPeriodId,
                asset.AssetId,
                asset.AssetCode,
                asset.AssetName,
                deprAmount,
                journalId,
                "posted",
                DateTimeOffset.UtcNow));
        }

        await transaction.CommitAsync(cancellationToken);
        return results;
    }

    // ── 8. Fiscal Period Closing & Reopening ─────────────────────────────────

    public async Task<bool> SoftCloseFiscalPeriodAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid periodId,
        string? note,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = "UPDATE finance.fiscal_periods SET status = 'soft_closed' WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id;";
        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("id", periodId);

        var count = await cmd.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return count > 0;
    }

    public async Task<bool> CloseFiscalPeriodAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid periodId,
        string? note,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            UPDATE finance.fiscal_periods
            SET status = 'closed', closed_at = now(), closed_by = @userId
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("id", periodId);

        var count = await cmd.ExecuteNonQueryAsync(cancellationToken);

        const string auditSql = """
            INSERT INTO finance.financial_audit_logs
                (tenant_id, company_id, id, event_type, entity_name, entity_id, actor_user_id, description, created_at)
            VALUES
                (@tenantId, @companyId, gen_random_uuid(), 'period_closed', 'fiscal_periods', @id, @userId, @desc, now());
            """;
        await using var aCmd = new NpgsqlCommand(auditSql, connection, transaction);
        aCmd.Parameters.AddWithValue("tenantId", tenantId);
        aCmd.Parameters.AddWithValue("companyId", companyId);
        aCmd.Parameters.AddWithValue("id", periodId.ToString());
        aCmd.Parameters.AddWithValue("userId", userId);
        aCmd.Parameters.AddWithValue("desc", $"Fiscal period {periodId} closed. Note: {note ?? "None"}");
        await aCmd.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return count > 0;
    }

    public async Task<bool> ReopenFiscalPeriodAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid periodId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("REASON_REQUIRED_FOR_REOPEN");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            UPDATE finance.fiscal_periods
            SET status = 'open', reopened_at = now(), reopened_by = @userId, reopen_reason = @reason
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("reason", reason.Trim());
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("id", periodId);

        var count = await cmd.ExecuteNonQueryAsync(cancellationToken);

        const string auditSql = """
            INSERT INTO finance.financial_audit_logs
                (tenant_id, company_id, id, event_type, entity_name, entity_id, actor_user_id, description, created_at)
            VALUES
                (@tenantId, @companyId, gen_random_uuid(), 'period_reopened', 'fiscal_periods', @id, @userId, @desc, now());
            """;
        await using var aCmd = new NpgsqlCommand(auditSql, connection, transaction);
        aCmd.Parameters.AddWithValue("tenantId", tenantId);
        aCmd.Parameters.AddWithValue("companyId", companyId);
        aCmd.Parameters.AddWithValue("id", periodId.ToString());
        aCmd.Parameters.AddWithValue("userId", userId);
        aCmd.Parameters.AddWithValue("desc", $"Fiscal period {periodId} reopened. Reason: {reason}");
        await aCmd.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return count > 0;
    }

    public async Task<IReadOnlyList<PeriodCloseChecklistItemSummary>> GetPeriodCloseChecklistAsync(
        Guid tenantId,
        Guid companyId,
        Guid periodId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        // Ensure default checklist tasks exist
        var defaultTasks = new[]
        {
            ("ap_reconciliation", "Accounts Payable Subledger Reconciliation"),
            ("ar_reconciliation", "Accounts Receivable Subledger Reconciliation"),
            ("inventory_reconciliation", "Inventory FIFO Asset Valuation Reconciliation"),
            ("bank_reconciliation", "Bank Accounts & Statement Reconciliation"),
            ("depreciation_run", "Fixed Asset Monthly Depreciation Execution"),
            ("tax_reconciliation", "Sales Tax & Withholding Tax Liability Review")
        };

        const string insSql = """
            INSERT INTO finance.period_close_checklists
                (id, tenant_id, company_id, fiscal_period_id, task_code, task_name, is_completed, created_at)
            VALUES
                (gen_random_uuid(), @tenantId, @companyId, @pid, @code, @name, false, now())
            ON CONFLICT (tenant_id, company_id, fiscal_period_id, task_code) DO NOTHING;
            """;

        foreach (var (code, name) in defaultTasks)
        {
            await using var insCmd = new NpgsqlCommand(insSql, connection, transaction);
            insCmd.Parameters.AddWithValue("tenantId", tenantId);
            insCmd.Parameters.AddWithValue("companyId", companyId);
            insCmd.Parameters.AddWithValue("pid", periodId);
            insCmd.Parameters.AddWithValue("code", code);
            insCmd.Parameters.AddWithValue("name", name);
            await insCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        const string listSql = """
            SELECT id, fiscal_period_id, task_code, task_name, is_completed, completed_at, completed_by, notes
            FROM finance.period_close_checklists
            WHERE tenant_id = @tenantId AND company_id = @companyId AND fiscal_period_id = @pid
            ORDER BY task_code;
            """;

        await using var cmd = new NpgsqlCommand(listSql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("pid", periodId);

        var list = new List<PeriodCloseChecklistItemSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new PeriodCloseChecklistItemSummary(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return list;
    }

    public async Task<bool> UpdatePeriodCloseChecklistItemAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid itemId,
        bool isCompleted,
        string? notes,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            UPDATE finance.period_close_checklists
            SET is_completed = @done,
                completed_at = CASE WHEN @done THEN now() ELSE NULL END,
                completed_by = CASE WHEN @done THEN @userId ELSE NULL END,
                notes = @notes
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("done", isCompleted);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("id", itemId);

        var count = await cmd.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return count > 0;
    }

    public async Task<YearEndCloseSummary> CloseFiscalYearAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        YearEndCloseRequest request,
        CancellationToken cancellationToken)
    {
        var years = await ListFiscalYearsAsync(tenantId, companyId, cancellationToken);
        var year = years.FirstOrDefault(y => y.YearId == request.FiscalYearId);
        if (year is null) throw new InvalidOperationException("FISCAL_YEAR_NOT_FOUND");
        if (year.Status == "closed") throw new InvalidOperationException("FISCAL_YEAR_ALREADY_CLOSED");

        var tb = await GetTrialBalanceAsync(tenantId, companyId, cancellationToken);
        var formatted = FinancialStatementEngine.BuildStatements(tb);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        // Fetch open period in this year for the closing entry
        Guid closingPeriodId;
        const string pSql = "SELECT id FROM finance.fiscal_periods WHERE tenant_id = @tenantId AND company_id = @companyId AND fiscal_year_id = @yid ORDER BY period_no DESC LIMIT 1;";
        await using (var pCmd = new NpgsqlCommand(pSql, connection, transaction))
        {
            pCmd.Parameters.AddWithValue("tenantId", tenantId);
            pCmd.Parameters.AddWithValue("companyId", companyId);
            pCmd.Parameters.AddWithValue("yid", request.FiscalYearId);
            var val = await pCmd.ExecuteScalarAsync(cancellationToken);
            if (val is not Guid pid) throw new InvalidOperationException("NO_PERIOD_FOR_YEAR_CLOSE");
            closingPeriodId = pid;
        }

        var closingJournalId = Guid.NewGuid();
        var closingJournalNo = $"JRN-YE-CLOSE-{year.Code}";
        var idempotencyKey = $"year-end-close-{year.YearId:N}";

        // Post Closing Journal Header
        const string jSql = """
            INSERT INTO finance.journals
                (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id,
                 description, functional_currency, source_module, source_type, source_id,
                 idempotency_key, status, created_by, posted_by, posted_at, created_at)
            VALUES
                (@id, @tenantId, @companyId, @no, @date, @pid,
                 @desc, 'PKR', 'finance', 'year_end_close', @sid,
                 @key, 'posted', @by, @by, now(), now());
            """;

        await using (var jCmd = new NpgsqlCommand(jSql, connection, transaction))
        {
            jCmd.Parameters.AddWithValue("id", closingJournalId);
            jCmd.Parameters.AddWithValue("tenantId", tenantId);
            jCmd.Parameters.AddWithValue("companyId", companyId);
            jCmd.Parameters.AddWithValue("no", closingJournalNo);
            jCmd.Parameters.AddWithValue("date", request.ClosingDate.ToDateTime(TimeOnly.MinValue));
            jCmd.Parameters.AddWithValue("pid", closingPeriodId);
            jCmd.Parameters.AddWithValue("desc", $"Year-End Financial Close for Fiscal Year {year.Code}");
            jCmd.Parameters.AddWithValue("sid", year.YearId.ToString());
            jCmd.Parameters.AddWithValue("key", idempotencyKey);
            jCmd.Parameters.AddWithValue("by", userId);
            await jCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Post Closing Lines:
        // Zero out revenue (Debit revenue lines), zero out expenses (Credit expense lines), net to Retained Earnings
        var linesToInsert = new List<(Guid accountId, string desc, decimal debit, decimal credit)>();

        foreach (var r in formatted.RevenueLines)
        {
            var acc = tb.First(a => a.AccountCode == r.AccountCode);
            if (r.Balance > 0)
            {
                linesToInsert.Add((acc.AccountId, $"Close Revenue: {r.AccountCode}", r.Balance, 0m));
            }
        }

        foreach (var e in formatted.ExpenseLines)
        {
            var acc = tb.First(a => a.AccountCode == e.AccountCode);
            if (e.Balance > 0)
            {
                linesToInsert.Add((acc.AccountId, $"Close Expense: {e.AccountCode}", 0m, e.Balance));
            }
        }

        // Retained earnings
        if (formatted.NetIncome > 0)
        {
            linesToInsert.Add((request.RetainedEarningsAccountId, "Transfer Net Income to Retained Earnings", 0m, formatted.NetIncome));
        }
        else if (formatted.NetIncome < 0)
        {
            linesToInsert.Add((request.RetainedEarningsAccountId, "Transfer Net Loss to Retained Earnings", Math.Abs(formatted.NetIncome), 0m));
        }

        const string lSql = """
            INSERT INTO finance.journal_lines
                (tenant_id, company_id, journal_id, line_no, account_id, description,
                 transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
            VALUES
                (@tenantId, @companyId, @jid, @lno, @acc, @desc, 'PKR', @amt, 1.0, @debit, @credit);
            """;

        for (var i = 0; i < linesToInsert.Count; i++)
        {
            var item = linesToInsert[i];
            await using var lCmd = new NpgsqlCommand(lSql, connection, transaction);
            lCmd.Parameters.AddWithValue("tenantId", tenantId);
            lCmd.Parameters.AddWithValue("companyId", companyId);
            lCmd.Parameters.AddWithValue("jid", closingJournalId);
            lCmd.Parameters.AddWithValue("lno", i + 1);
            lCmd.Parameters.AddWithValue("acc", item.accountId);
            lCmd.Parameters.AddWithValue("desc", item.desc);
            lCmd.Parameters.AddWithValue("amt", item.debit > 0 ? item.debit : item.credit);
            lCmd.Parameters.AddWithValue("debit", item.debit);
            lCmd.Parameters.AddWithValue("credit", item.credit);
            await lCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Close Fiscal Year
        const string updYearSql = "UPDATE finance.fiscal_years SET status = 'closed' WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @yid;";
        await using (var yCmd = new NpgsqlCommand(updYearSql, connection, transaction))
        {
            yCmd.Parameters.AddWithValue("tenantId", tenantId);
            yCmd.Parameters.AddWithValue("companyId", companyId);
            yCmd.Parameters.AddWithValue("yid", request.FiscalYearId);
            await yCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new YearEndCloseSummary(
            request.FiscalYearId,
            year.Code,
            closingJournalId,
            closingJournalNo,
            formatted.TotalRevenue,
            formatted.TotalCostOfSales + formatted.TotalOperatingExpenses,
            formatted.NetIncome,
            DateTimeOffset.UtcNow);
    }

    // ── 9. Audit Trail & Control Dashboards ───────────────────────────────────

    public async Task<FinanceControlDashboardSummary> GetControlDashboardAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var accounts = await ListAccountsAsync(tenantId, companyId, cancellationToken);
        var tb = await GetTrialBalanceAsync(tenantId, companyId, cancellationToken);
        var bankAccounts = await ListBankAccountsAsync(tenantId, companyId, cancellationToken);
        var statements = await ListBankStatementsAsync(tenantId, companyId, null, cancellationToken);
        var fixedAssets = await ListFixedAssetsAsync(tenantId, companyId, cancellationToken);
        var schedules = await ListRecurringSchedulesAsync(tenantId, companyId, cancellationToken);
        var periods = await ListFiscalPeriodsAsync(tenantId, companyId, null, cancellationToken);

        decimal totalDebits = tb.Sum(r => r.TotalDebit);
        decimal totalCredits = tb.Sum(r => r.TotalCredit);
        decimal variance = Math.Round(totalDebits - totalCredits, 4);

        return new FinanceControlDashboardSummary(
            TotalAccounts: accounts.Count,
            TotalGlDebits: totalDebits,
            TotalGlCredits: totalCredits,
            GlNetVariance: variance,
            IsGlBalanced: Math.Abs(variance) < 0.01m,
            ActiveBankAccountsCount: bankAccounts.Count(b => b.IsActive),
            TotalCashAndBankBalances: bankAccounts.Where(b => b.IsActive).Sum(b => b.CurrentBalance),
            UnreconciledStatementsCount: statements.Count(s => s.Status != "reconciled"),
            ActiveFixedAssetsCount: fixedAssets.Count(a => a.Status == "active"),
            TotalFixedAssetBookValue: fixedAssets.Where(a => a.Status == "active").Sum(a => a.CurrentBookValue),
            ActiveRecurringSchedulesCount: schedules.Count(s => s.Status == "active"),
            OpenFiscalPeriodsCount: periods.Count(p => p.Status == "open"),
            EstimatedNetTaxPayable: 0m);
    }

    public async Task<IReadOnlyList<FinancialAuditLogSummary>> ListAuditLogsAsync(
        Guid tenantId,
        Guid companyId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            SELECT id, event_type, entity_name, entity_id, actor_user_id, description, metadata_json, created_at
            FROM finance.financial_audit_logs
            WHERE tenant_id = @tenantId AND company_id = @companyId
            ORDER BY created_at DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("limit", Math.Max(1, Math.Min(limit, 100)));

        var list = new List<FinancialAuditLogSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new FinancialAuditLogSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return list;
    }
}
