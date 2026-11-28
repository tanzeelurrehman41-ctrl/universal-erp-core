using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using UniversalErp.Application.Finance;

namespace UniversalErp.Infrastructure.Finance;

public sealed partial class PostgresFinanceRepository
{
    public async Task<string> ResolveCompanyFunctionalCurrencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COALESCE(functional_currency_code, base_currency_code, 'PKR')
            FROM organization.companies
            WHERE tenant_id = @tenantId AND id = @companyId;
            """,
            connection,
            transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        var val = await cmd.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(val) ? "PKR" : val.Trim().ToUpperInvariant();
    }

    public async Task<IReadOnlyList<CurrencySummary>> ListCurrenciesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT code, name, minor_unit, is_active
            FROM finance.currencies
            WHERE is_active = true
            ORDER BY code ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        var list = new List<CurrencySummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new CurrencySummary(
                Code: reader.GetString(0).Trim().ToUpperInvariant(),
                Name: reader.GetString(1),
                MinorUnit: reader.GetInt16(2),
                IsActive: reader.GetBoolean(3)));
        }

        return list;
    }

    public async Task<CompanyCurrencyContextInfo> GetCompanyCurrencyContextAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT COALESCE(functional_currency_code, base_currency_code, 'PKR'),
                   COALESCE(presentation_currency_code, functional_currency_code, base_currency_code, 'PKR'),
                   currency_effective_date
            FROM organization.companies
            WHERE tenant_id = @tenantId AND id = @companyId;
            """,
            connection,
            transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        CompanyCurrencyContextInfo info = new("PKR", "PKR", DateOnly.FromDateTime(DateTime.UtcNow));
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                info = new CompanyCurrencyContextInfo(
                    FunctionalCurrencyCode: reader.GetString(0).Trim().ToUpperInvariant(),
                    PresentationCurrencyCode: reader.GetString(1).Trim().ToUpperInvariant(),
                    CurrencyEffectiveDate: reader.GetFieldValue<DateOnly>(2));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return info;
    }

    public async Task<IReadOnlyList<ExchangeRateSummary>> ListExchangeRatesAsync(
        Guid tenantId,
        Guid companyId,
        string? fromCurrency,
        string? toCurrency,
        string? rateType,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT exchange_rate_id, company_id, from_currency, to_currency, rate,
                   effective_date, effective_at, rate_type, source, reference,
                   created_by, created_by_email, created_by_name, created_at,
                   updated_by, updated_by_email, updated_at
            FROM identity.list_company_exchange_rates(@tenantId, @companyId, @from, @to, @rateType);
            """,
            connection,
            transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(fromCurrency) ? DBNull.Value : fromCurrency.Trim().ToUpperInvariant()
        });
        cmd.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(toCurrency) ? DBNull.Value : toCurrency.Trim().ToUpperInvariant()
        });
        cmd.Parameters.Add(new NpgsqlParameter("rateType", NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(rateType) ? DBNull.Value : rateType.Trim().ToLowerInvariant()
        });

        var results = new List<ExchangeRateSummary>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadExchangeRateSummary(reader));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return results;
    }

    public async Task<ExchangeRateSummary> CreateExchangeRateAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        UpsertExchangeRateRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        await EnsureNoExchangeRateConflictAsync(
            connection,
            transaction,
            tenantId,
            companyId,
            null,
            request.FromCurrency,
            request.ToCurrency,
            request.EffectiveDate,
            request.RateType,
            cancellationToken);

        var rateId = Guid.NewGuid();
        var effectiveAt = new DateTimeOffset(request.EffectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var source = string.IsNullOrWhiteSpace(request.Source) ? "manual" : request.Source.Trim();

        const string insertSql = """
            INSERT INTO finance.exchange_rates
                (id, tenant_id, company_id, from_currency, to_currency, effective_date, effective_at,
                 rate, rate_type, source, reference, created_by, created_at)
            VALUES
                (@id, @tenantId, @companyId, @from, @to, @effectiveDate, @effectiveAt,
                 @rate, @rateType, @source, @reference, @userId, now());
            """;

        try
        {
            await using var cmd = new NpgsqlCommand(insertSql, connection, transaction);
            cmd.Parameters.AddWithValue("id", rateId);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("companyId", companyId);
            cmd.Parameters.AddWithValue("from", request.FromCurrency);
            cmd.Parameters.AddWithValue("to", request.ToCurrency);
            cmd.Parameters.AddWithValue("effectiveDate", request.EffectiveDate);
            cmd.Parameters.AddWithValue("effectiveAt", effectiveAt);
            cmd.Parameters.AddWithValue("rate", request.Rate);
            cmd.Parameters.AddWithValue("rateType", request.RateType);
            cmd.Parameters.AddWithValue("source", source);
            cmd.Parameters.Add(new NpgsqlParameter("reference", NpgsqlDbType.Text) { Value = (object?)request.Reference ?? DBNull.Value });
            cmd.Parameters.AddWithValue("userId", userId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new ExchangeRateConflictException();
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            throw new InvalidOperationException("INVALID_CURRENCY_CODE", ex);
        }

        await RecordRateAuditAsync(
            connection,
            transaction,
            tenantId,
            companyId,
            userId,
            "EXCHANGE_RATE_CREATED",
            rateId,
            $"Exchange rate {request.FromCurrency}/{request.ToCurrency} = {request.Rate} ({request.RateType}) effective {request.EffectiveDate:yyyy-MM-dd}",
            new
            {
                fromCurrency = request.FromCurrency,
                toCurrency = request.ToCurrency,
                rate = request.Rate,
                effectiveDate = request.EffectiveDate.ToString("yyyy-MM-dd"),
                rateType = request.RateType,
                source,
                reference = request.Reference
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var all = await ListExchangeRatesAsync(tenantId, companyId, request.FromCurrency, request.ToCurrency, request.RateType, cancellationToken);
        return all.First(r => r.ExchangeRateId == rateId);
    }

    public async Task<ExchangeRateSummary?> UpdateExchangeRateAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        Guid exchangeRateId,
        UpsertExchangeRateRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        string? prevFrom = null;
        string? prevTo = null;
        decimal prevRate = 0m;
        DateOnly prevDate = default;
        string? prevType = null;

        await using (var lockCmd = new NpgsqlCommand(
            """
            SELECT from_currency, to_currency, rate, effective_date, rate_type
            FROM finance.exchange_rates
            WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id
            FOR UPDATE;
            """,
            connection,
            transaction))
        {
            lockCmd.Parameters.AddWithValue("tenantId", tenantId);
            lockCmd.Parameters.AddWithValue("companyId", companyId);
            lockCmd.Parameters.AddWithValue("id", exchangeRateId);
            await using var r = await lockCmd.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
            prevFrom = r.GetString(0).Trim().ToUpperInvariant();
            prevTo = r.GetString(1).Trim().ToUpperInvariant();
            prevRate = r.GetDecimal(2);
            prevDate = r.GetFieldValue<DateOnly>(3);
            prevType = r.GetString(4).Trim().ToLowerInvariant();
        }

        await EnsureNoExchangeRateConflictAsync(
            connection,
            transaction,
            tenantId,
            companyId,
            exchangeRateId,
            request.FromCurrency,
            request.ToCurrency,
            request.EffectiveDate,
            request.RateType,
            cancellationToken);

        var effectiveAt = new DateTimeOffset(request.EffectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var source = string.IsNullOrWhiteSpace(request.Source) ? "manual" : request.Source.Trim();

        try
        {
            await using var updCmd = new NpgsqlCommand(
                """
                UPDATE finance.exchange_rates
                SET from_currency = @from,
                    to_currency = @to,
                    rate = @rate,
                    effective_date = @effectiveDate,
                    effective_at = @effectiveAt,
                    rate_type = @rateType,
                    source = @source,
                    reference = @reference,
                    updated_by = @userId,
                    updated_at = now()
                WHERE tenant_id = @tenantId AND company_id = @companyId AND id = @id;
                """,
                connection,
                transaction);
            updCmd.Parameters.AddWithValue("tenantId", tenantId);
            updCmd.Parameters.AddWithValue("companyId", companyId);
            updCmd.Parameters.AddWithValue("id", exchangeRateId);
            updCmd.Parameters.AddWithValue("from", request.FromCurrency);
            updCmd.Parameters.AddWithValue("to", request.ToCurrency);
            updCmd.Parameters.AddWithValue("rate", request.Rate);
            updCmd.Parameters.AddWithValue("effectiveDate", request.EffectiveDate);
            updCmd.Parameters.AddWithValue("effectiveAt", effectiveAt);
            updCmd.Parameters.AddWithValue("rateType", request.RateType);
            updCmd.Parameters.AddWithValue("source", source);
            updCmd.Parameters.Add(new NpgsqlParameter("reference", NpgsqlDbType.Text) { Value = (object?)request.Reference ?? DBNull.Value });
            updCmd.Parameters.AddWithValue("userId", userId);
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new ExchangeRateConflictException();
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            throw new InvalidOperationException("INVALID_CURRENCY_CODE", ex);
        }

        await RecordRateAuditAsync(
            connection,
            transaction,
            tenantId,
            companyId,
            userId,
            "EXCHANGE_RATE_UPDATED",
            exchangeRateId,
            $"Updated rate {prevFrom}/{prevTo} {prevRate} -> {request.FromCurrency}/{request.ToCurrency} {request.Rate} ({request.RateType} {request.EffectiveDate:yyyy-MM-dd})",
            new
            {
                previousFromCurrency = prevFrom,
                previousToCurrency = prevTo,
                previousRate = prevRate,
                previousEffectiveDate = prevDate.ToString("yyyy-MM-dd"),
                previousRateType = prevType,
                newFromCurrency = request.FromCurrency,
                newToCurrency = request.ToCurrency,
                newRate = request.Rate,
                newEffectiveDate = request.EffectiveDate.ToString("yyyy-MM-dd"),
                newRateType = request.RateType,
                source,
                reference = request.Reference
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var all = await ListExchangeRatesAsync(tenantId, companyId, request.FromCurrency, request.ToCurrency, request.RateType, cancellationToken);
        return all.FirstOrDefault(r => r.ExchangeRateId == exchangeRateId);
    }

    public async Task<IReadOnlyList<Ias21PostedJournalLineInput>> ListPostedJournalLinesForTranslationAsync(
        Guid tenantId,
        Guid companyId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetScopeAsync(connection, transaction, tenantId, companyId, cancellationToken);

        const string sql = """
            SELECT j.id, j.journal_date, a.id, a.account_code, a.account_name,
                   a.account_type, a.normal_balance, j.functional_currency,
                   l.debit_functional, l.credit_functional
            FROM finance.journal_lines l
            JOIN finance.journals j
              ON j.tenant_id = l.tenant_id AND j.company_id = l.company_id AND j.id = l.journal_id
            JOIN finance.accounts a
              ON a.tenant_id = l.tenant_id AND a.company_id = l.company_id AND a.id = l.account_id
            WHERE j.tenant_id = @tenantId
              AND j.company_id = @companyId
              AND j.status = 'posted'
              AND j.journal_date <= @asOfDate
              AND a.is_active = true
            ORDER BY j.journal_date ASC, j.id ASC, l.line_no ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("asOfDate", asOfDate);

        var lines = new List<Ias21PostedJournalLineInput>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add(new Ias21PostedJournalLineInput(
                    JournalId: reader.GetGuid(0),
                    JournalDate: reader.GetFieldValue<DateOnly>(1),
                    AccountId: reader.GetGuid(2),
                    AccountCode: reader.GetString(3),
                    AccountName: reader.GetString(4),
                    AccountType: reader.GetString(5),
                    NormalBalance: reader.GetString(6),
                    JournalFunctionalCurrency: reader.GetString(7).Trim().ToUpperInvariant(),
                    DebitFunctional: reader.GetDecimal(8),
                    CreditFunctional: reader.GetDecimal(9)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return lines;
    }

    private static async Task EnsureNoExchangeRateConflictAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid companyId,
        Guid? excludeId,
        string fromCurrency,
        string toCurrency,
        DateOnly effectiveDate,
        string rateType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT from_currency, to_currency
            FROM finance.exchange_rates
            WHERE tenant_id = @tenantId
              AND company_id = @companyId
              AND effective_date = @effectiveDate
              AND rate_type = @rateType
              AND (CAST(@excludeId AS uuid) IS NULL OR id <> CAST(@excludeId AS uuid))
              AND (
                  (from_currency = @from AND to_currency = @to)
                  OR (from_currency = @to AND to_currency = @from)
              )
            LIMIT 1;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("effectiveDate", effectiveDate);
        cmd.Parameters.AddWithValue("rateType", rateType);
        cmd.Parameters.Add(new NpgsqlParameter("excludeId", NpgsqlDbType.Uuid) { Value = (object?)excludeId ?? DBNull.Value });
        cmd.Parameters.AddWithValue("from", fromCurrency);
        cmd.Parameters.AddWithValue("to", toCurrency);

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await r.ReadAsync(cancellationToken))
        {
            var existingFrom = r.GetString(0).Trim().ToUpperInvariant();
            var code = string.Equals(existingFrom, fromCurrency, StringComparison.Ordinal)
                ? "DUPLICATE_EXCHANGE_RATE"
                : "AMBIGUOUS_INVERSE_EXCHANGE_RATE";
            throw new ExchangeRateConflictException(code);
        }
    }

    private static async Task RecordRateAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid companyId,
        Guid userId,
        string eventType,
        Guid entityId,
        string description,
        object metadata,
        CancellationToken cancellationToken)
    {
        var metadataJson = JsonSerializer.Serialize(metadata);

        await using (var logCmd = new NpgsqlCommand(
            """
            INSERT INTO finance.financial_audit_logs
                (tenant_id, company_id, event_type, entity_name, entity_id, actor_user_id, description, metadata_json)
            VALUES
                (@tenantId, @companyId, @eventType, 'exchange_rate', @entityIdText, @userId, @description, @metadata);
            """,
            connection,
            transaction))
        {
            logCmd.Parameters.AddWithValue("tenantId", tenantId);
            logCmd.Parameters.AddWithValue("companyId", companyId);
            logCmd.Parameters.AddWithValue("eventType", eventType);
            logCmd.Parameters.AddWithValue("entityIdText", entityId.ToString());
            logCmd.Parameters.AddWithValue("userId", userId);
            logCmd.Parameters.AddWithValue("description", description);
            logCmd.Parameters.AddWithValue("metadata", metadataJson);
            await logCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var auditCmd = new NpgsqlCommand(
            "SELECT identity.record_audit_event(@tenantId, @companyId, @userId, @eventCode, 'exchange_rate', @entityId, @metadata::jsonb);",
            connection,
            transaction))
        {
            auditCmd.Parameters.AddWithValue("tenantId", tenantId);
            auditCmd.Parameters.AddWithValue("companyId", companyId);
            auditCmd.Parameters.AddWithValue("userId", userId);
            auditCmd.Parameters.AddWithValue("eventCode", eventType);
            auditCmd.Parameters.AddWithValue("entityId", entityId);
            auditCmd.Parameters.AddWithValue("metadata", metadataJson);
            await auditCmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static ExchangeRateSummary ReadExchangeRateSummary(NpgsqlDataReader reader) => new(
        ExchangeRateId: reader.GetGuid(0),
        CompanyId: reader.IsDBNull(1) ? null : reader.GetGuid(1),
        FromCurrency: reader.GetString(2).Trim().ToUpperInvariant(),
        ToCurrency: reader.GetString(3).Trim().ToUpperInvariant(),
        Rate: reader.GetDecimal(4),
        EffectiveDate: reader.GetFieldValue<DateOnly>(5),
        EffectiveAt: reader.GetFieldValue<DateTimeOffset>(6),
        RateType: reader.GetString(7).Trim().ToLowerInvariant(),
        Source: reader.GetString(8),
        Reference: reader.IsDBNull(9) ? null : reader.GetString(9),
        CreatedByUserId: reader.IsDBNull(10) ? null : reader.GetGuid(10),
        CreatedByEmail: reader.IsDBNull(11) ? null : reader.GetString(11),
        CreatedByName: reader.IsDBNull(12) ? null : reader.GetString(12),
        CreatedAt: reader.GetFieldValue<DateTimeOffset>(13),
        UpdatedByUserId: reader.IsDBNull(14) ? null : reader.GetGuid(14),
        UpdatedByEmail: reader.IsDBNull(15) ? null : reader.GetString(15),
        UpdatedAt: reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16));
}
