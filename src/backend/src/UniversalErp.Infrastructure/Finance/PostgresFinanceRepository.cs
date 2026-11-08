using Npgsql;
using UniversalErp.Application.Finance;

namespace UniversalErp.Infrastructure.Finance;

public sealed class PostgresFinanceRepository(NpgsqlDataSource dataSource) : IFinanceRepository
{
    // ── Accounts ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AccountSummary>> ListAccountsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            SELECT id, account_code, account_name, account_type, normal_balance,
                   currency_code, is_control_account, allow_manual_posting, is_active
            FROM finance.accounts
            WHERE tenant_id = @tenantId AND company_id = @companyId
            ORDER BY account_code;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("companyId", companyId);

        var list = new List<AccountSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new AccountSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return list;
    }

    public async Task<AccountSummary> CreateAccountAsync(
        Guid tenantId,
        Guid companyId,
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        var accountId = Guid.NewGuid();

        const string sql = """
            INSERT INTO finance.accounts
                (id, tenant_id, company_id, account_code, account_name, account_type, normal_balance,
                 parent_account_id, currency_code, is_control_account, allow_manual_posting, is_active)
            VALUES
                (@id, @tenantId, @companyId, @code, @name, @type, @balance,
                 @parentId, @currency, @control, @manual, true)
            RETURNING id;
            """;

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", accountId);
            command.Parameters.AddWithValue("tenantId", tenantId);
            command.Parameters.AddWithValue("companyId", companyId);
            command.Parameters.AddWithValue("code", request.AccountCode);
            command.Parameters.AddWithValue("name", request.AccountName);
            command.Parameters.AddWithValue("type", request.AccountType);
            command.Parameters.AddWithValue("balance", request.NormalBalance);
            command.Parameters.AddWithValue("parentId", (object?)request.ParentAccountId ?? DBNull.Value);
            command.Parameters.AddWithValue("currency", (object?)request.CurrencyCode ?? DBNull.Value);
            command.Parameters.AddWithValue("control", request.IsControlAccount);
            command.Parameters.AddWithValue("manual", request.AllowManualPosting);

            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new AccountSummary(
                accountId,
                request.AccountCode,
                request.AccountName,
                request.AccountType,
                request.NormalBalance,
                request.CurrencyCode,
                request.IsControlAccount,
                request.AllowManualPosting,
                true);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new AccountCodeConflictException();
        }
    }

    // ── Fiscal Years & Periods ─────────────────────────────────────────────────

    public async Task<IReadOnlyList<FiscalYearSummary>> ListFiscalYearsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            SELECT id, code, starts_on, ends_on, status
            FROM finance.fiscal_years
            WHERE tenant_id = @tenantId AND company_id = @companyId
            ORDER BY starts_on DESC;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("companyId", companyId);

        var list = new List<FiscalYearSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new FiscalYearSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetString(4)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return list;
    }

    public async Task<IReadOnlyList<FiscalPeriodSummary>> ListFiscalPeriodsAsync(
        Guid tenantId,
        Guid companyId,
        Guid? fiscalYearId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        var sql = """
            SELECT id, fiscal_year_id, period_no, name, starts_on, ends_on, status
            FROM finance.fiscal_periods
            WHERE tenant_id = @tenantId AND company_id = @companyId
            """;

        if (fiscalYearId.HasValue)
        {
            sql += " AND fiscal_year_id = @yearId";
        }

        sql += " ORDER BY starts_on;";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("companyId", companyId);
        if (fiscalYearId.HasValue)
        {
            command.Parameters.AddWithValue("yearId", fiscalYearId.Value);
        }

        var list = new List<FiscalPeriodSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new FiscalPeriodSummary(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt16(2),
                reader.GetString(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetFieldValue<DateOnly>(5),
                reader.GetString(6)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return list;
    }

    // ── Journals ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<JournalSummary>> ListJournalsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            SELECT j.id, j.journal_no, j.journal_date, j.fiscal_period_id, j.description,
                   j.functional_currency, j.source_module, j.source_type, j.source_id,
                   j.status, j.posted_at,
                   COALESCE(SUM(l.debit_functional), 0) AS total_debit,
                   COALESCE(SUM(l.credit_functional), 0) AS total_credit
            FROM finance.journals j
            LEFT JOIN finance.journal_lines l
              ON l.tenant_id = j.tenant_id AND l.company_id = j.company_id AND l.journal_id = j.id
            WHERE j.tenant_id = @tenantId AND j.company_id = @companyId
            GROUP BY j.id, j.journal_no, j.journal_date, j.fiscal_period_id, j.description,
                     j.functional_currency, j.source_module, j.source_type, j.source_id,
                     j.status, j.posted_at, j.created_at
            ORDER BY j.journal_date DESC, j.created_at DESC;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("companyId", companyId);

        var list = new List<JournalSummary>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new JournalSummary(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetFieldValue<DateOnly>(2),
                    reader.GetGuid(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                    reader.GetDecimal(11),
                    reader.GetDecimal(12),
                    Array.Empty<JournalLineSummary>()));
            }
            await reader.CloseAsync();
        }

        await transaction.CommitAsync(cancellationToken);
        return list;
    }

    public async Task<JournalSummary?> GetJournalByIdAsync(
        Guid tenantId,
        Guid companyId,
        Guid journalId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string headerSql = """
            SELECT id, journal_no, journal_date, fiscal_period_id, description,
                   functional_currency, source_module, source_type, source_id,
                   status, posted_at
            FROM finance.journals
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id;
            """;

        JournalSummary? header = null;
        await using (var cmd = new NpgsqlCommand(headerSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("id", journalId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                header = new JournalSummary(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetFieldValue<DateOnly>(2),
                    reader.GetGuid(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                    0, 0,
                    Array.Empty<JournalLineSummary>());
            }
            await reader.CloseAsync();
        }

        if (header is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        const string linesSql = """
            SELECT l.line_no, l.account_id, a.account_code, a.account_name,
                   l.description, l.transaction_currency, l.transaction_amount,
                   l.debit_functional, l.credit_functional
            FROM finance.journal_lines l
            JOIN finance.accounts a
              ON a.tenant_id = l.tenant_id AND a.company_id = l.company_id AND a.id = l.account_id
            WHERE l.tenant_id = @tenantId AND l.company_id = @companyId AND l.journal_id = @id
            ORDER BY l.line_no;
            """;

        var lines = new List<JournalLineSummary>();
        decimal totalDebit = 0;
        decimal totalCredit = 0;

        await using (var lineCmd = new NpgsqlCommand(linesSql, connection, transaction))
        {
            lineCmd.Parameters.AddWithValue("tenantId", tenantId);
            lineCmd.Parameters.AddWithValue("companyId", companyId);
            lineCmd.Parameters.AddWithValue("id", journalId);

            await using var lineReader = await lineCmd.ExecuteReaderAsync(cancellationToken);
            while (await lineReader.ReadAsync(cancellationToken))
            {
                var deb = lineReader.GetDecimal(7);
                var cred = lineReader.GetDecimal(8);
                totalDebit += deb;
                totalCredit += cred;

                lines.Add(new JournalLineSummary(
                    lineReader.GetInt32(0),
                    lineReader.GetGuid(1),
                    lineReader.GetString(2),
                    lineReader.GetString(3),
                    lineReader.IsDBNull(4) ? null : lineReader.GetString(4),
                    lineReader.GetString(5),
                    lineReader.GetDecimal(6),
                    deb, cred));
            }
            await lineReader.CloseAsync();
        }

        await transaction.CommitAsync(cancellationToken);

        return header with
        {
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
            Lines = lines
        };
    }

    public async Task<JournalSummary> PostJournalAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid idempotencyKey,
        string requestHash,
        PostJournalRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        // 1. Check idempotency
        await using (var checkCmd = new NpgsqlCommand(
            "SELECT id FROM finance.journals WHERE tenant_id = @tenantId AND company_id = @companyId AND idempotency_key = @key",
            connection,
            transaction))
        {
            checkCmd.Parameters.AddWithValue("tenantId", tenantId);
            checkCmd.Parameters.AddWithValue("companyId", companyId);
            checkCmd.Parameters.AddWithValue("key", idempotencyKey.ToString());

            var existing = await checkCmd.ExecuteScalarAsync(cancellationToken);
            if (existing is Guid existingJournalId)
            {
                var loaded = await GetJournalByIdInternalAsync(connection, transaction, tenantId, companyId, existingJournalId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return loaded ?? throw new InvalidOperationException("Idempotent journal could not be loaded.");
            }
        }

        // 2. Check journal number conflict
        await using (var checkNoCmd = new NpgsqlCommand(
            "SELECT 1 FROM finance.journals WHERE tenant_id = @tenantId AND company_id = @companyId AND journal_no = @no",
            connection,
            transaction))
        {
            checkNoCmd.Parameters.AddWithValue("tenantId", tenantId);
            checkNoCmd.Parameters.AddWithValue("companyId", companyId);
            checkNoCmd.Parameters.AddWithValue("no", request.JournalNo);

            var exists = await checkNoCmd.ExecuteScalarAsync(cancellationToken);
            if (exists is not null and not DBNull)
            {
                throw new JournalNoConflictException();
            }
        }

        var journalId = Guid.NewGuid();

        // 3. Insert Journal header
        const string insertHeaderSql = """
            INSERT INTO finance.journals
                (id, tenant_id, company_id, journal_no, journal_date, fiscal_period_id, description,
                 functional_currency, source_module, source_type, source_id, idempotency_key,
                 status, created_by, posted_by, posted_at)
            VALUES
                (@id, @tenantId, @companyId, @no, @date, @periodId, @desc,
                 @curr, @sourceMod, @sourceType, @sourceId, @key,
                 'posted', @user, @user, now());
            """;

        await using (var insertHeader = new NpgsqlCommand(insertHeaderSql, connection, transaction))
        {
            insertHeader.Parameters.AddWithValue("id", journalId);
            insertHeader.Parameters.AddWithValue("tenantId", tenantId);
            insertHeader.Parameters.AddWithValue("companyId", companyId);
            insertHeader.Parameters.AddWithValue("no", request.JournalNo);
            insertHeader.Parameters.AddWithValue("date", request.JournalDate);
            insertHeader.Parameters.AddWithValue("periodId", request.FiscalPeriodId);
            insertHeader.Parameters.AddWithValue("desc", request.Description);
            insertHeader.Parameters.AddWithValue("curr", request.FunctionalCurrency);
            insertHeader.Parameters.AddWithValue("sourceMod", request.SourceModule);
            insertHeader.Parameters.AddWithValue("sourceType", request.SourceType);
            insertHeader.Parameters.AddWithValue("sourceId", request.SourceId);
            insertHeader.Parameters.AddWithValue("key", idempotencyKey.ToString());
            insertHeader.Parameters.AddWithValue("user", userId);

            await insertHeader.ExecuteNonQueryAsync(cancellationToken);
        }

        // 4. Insert Journal Lines
        const string insertLineSql = """
            INSERT INTO finance.journal_lines
                (tenant_id, company_id, journal_id, line_no, account_id, description,
                 transaction_currency, transaction_amount, exchange_rate, debit_functional, credit_functional)
            VALUES
                (@tenantId, @companyId, @journalId, @lineNo, @accId, @desc,
                 @curr, @amount, @rate, @debit, @credit);
            """;

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            await using var insertLine = new NpgsqlCommand(insertLineSql, connection, transaction);
            insertLine.Parameters.AddWithValue("tenantId", tenantId);
            insertLine.Parameters.AddWithValue("companyId", companyId);
            insertLine.Parameters.AddWithValue("journalId", journalId);
            insertLine.Parameters.AddWithValue("lineNo", i + 1);
            insertLine.Parameters.AddWithValue("accId", line.AccountId);
            insertLine.Parameters.AddWithValue("desc", (object?)line.Description ?? DBNull.Value);
            insertLine.Parameters.AddWithValue("curr", line.TransactionCurrency);
            insertLine.Parameters.AddWithValue("amount", line.TransactionAmount);
            insertLine.Parameters.AddWithValue("rate", line.ExchangeRate);
            insertLine.Parameters.AddWithValue("debit", line.DebitFunctional);
            insertLine.Parameters.AddWithValue("credit", line.CreditFunctional);

            await insertLine.ExecuteNonQueryAsync(cancellationToken);
        }

        // Commit triggers assert_posted_journal_balanced
        try
        {
            var result = await GetJournalByIdInternalAsync(connection, transaction, tenantId, companyId, journalId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result ?? throw new InvalidOperationException("Failed to assemble posted journal.");
        }
        catch (PostgresException ex) when (ex.Message.Contains("not balanced", StringComparison.OrdinalIgnoreCase))
        {
            throw new JournalUnbalancedException(0, 0);
        }
    }

    // ── Trial Balance ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            SELECT a.id, a.account_code, a.account_name, a.account_type, a.normal_balance,
                   COALESCE(SUM(l.debit_functional), 0) AS total_debit,
                   COALESCE(SUM(l.credit_functional), 0) AS total_credit
            FROM finance.accounts a
            LEFT JOIN (
                finance.journal_lines l
                JOIN finance.journals j
                  ON j.tenant_id = l.tenant_id AND j.company_id = l.company_id AND j.id = l.journal_id AND j.status = 'posted'
            ) ON a.tenant_id = l.tenant_id AND a.company_id = l.company_id AND a.id = l.account_id
            WHERE a.tenant_id = @tenantId AND a.company_id = @companyId AND a.is_active = true
            GROUP BY a.id, a.account_code, a.account_name, a.account_type, a.normal_balance
            ORDER BY a.account_code;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("companyId", companyId);

        var rows = new List<TrialBalanceRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var accId = reader.GetGuid(0);
            var code = reader.GetString(1);
            var name = reader.GetString(2);
            var type = reader.GetString(3);
            var normalBal = reader.GetString(4);
            var debit = reader.GetDecimal(5);
            var credit = reader.GetDecimal(6);

            var net = string.Equals(normalBal, "debit", StringComparison.OrdinalIgnoreCase)
                ? debit - credit
                : credit - debit;

            rows.Add(new TrialBalanceRow(accId, code, name, type, normalBal, debit, credit, net));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    private static async Task<JournalSummary?> GetJournalByIdInternalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid companyId,
        Guid journalId,
        CancellationToken cancellationToken)
    {
        const string headerSql = """
            SELECT id, journal_no, journal_date, fiscal_period_id, description,
                   functional_currency, source_module, source_type, source_id,
                   status, posted_at
            FROM finance.journals
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id;
            """;

        JournalSummary? header = null;
        await using (var cmd = new NpgsqlCommand(headerSql, connection, transaction))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("id", journalId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                header = new JournalSummary(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetFieldValue<DateOnly>(2),
                    reader.GetGuid(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                    0, 0,
                    Array.Empty<JournalLineSummary>());
            }
            await reader.CloseAsync();
        }

        if (header is null) return null;

        const string linesSql = """
            SELECT l.line_no, l.account_id, a.account_code, a.account_name,
                   l.description, l.transaction_currency, l.transaction_amount,
                   l.debit_functional, l.credit_functional
            FROM finance.journal_lines l
            JOIN finance.accounts a
              ON a.tenant_id = l.tenant_id AND a.company_id = l.company_id AND a.id = l.account_id
            WHERE l.tenant_id = @tenantId AND l.company_id = @companyId AND l.journal_id = @id
            ORDER BY l.line_no;
            """;

        var lines = new List<JournalLineSummary>();
        decimal totalDebit = 0;
        decimal totalCredit = 0;

        await using (var lineCmd = new NpgsqlCommand(linesSql, connection, transaction))
        {
            lineCmd.Parameters.AddWithValue("tenantId", tenantId);
            lineCmd.Parameters.AddWithValue("companyId", companyId);
            lineCmd.Parameters.AddWithValue("id", journalId);

            await using var lineReader = await lineCmd.ExecuteReaderAsync(cancellationToken);
            while (await lineReader.ReadAsync(cancellationToken))
            {
                var deb = lineReader.GetDecimal(7);
                var cred = lineReader.GetDecimal(8);
                totalDebit += deb;
                totalCredit += cred;

                lines.Add(new JournalLineSummary(
                    lineReader.GetInt32(0),
                    lineReader.GetGuid(1),
                    lineReader.GetString(2),
                    lineReader.GetString(3),
                    lineReader.IsDBNull(4) ? null : lineReader.GetString(4),
                    lineReader.GetString(5),
                    lineReader.GetDecimal(6),
                    deb, cred));
            }
            await lineReader.CloseAsync();
        }

        return header with
        {
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
            Lines = lines
        };
    }

    private static async Task SetScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenantId, true), set_config('app.company_id', @companyId, true)",
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId.ToString());
        command.Parameters.AddWithValue("companyId", companyId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
