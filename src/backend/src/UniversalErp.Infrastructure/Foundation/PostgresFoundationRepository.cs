using System.Text.Json;
using Npgsql;
using UniversalErp.Application.Foundation;

namespace UniversalErp.Infrastructure.Foundation;

public sealed class PostgresFoundationRepository(NpgsqlDataSource dataSource) : IFoundationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<UserProfileResponse?> GetUserProfileAsync(
        Guid tenantId,
        Guid companyId,
        Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT identity.get_user_profile(@tenantId, @companyId, @userId);",
            connection);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("userId", userId);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        if (string.IsNullOrWhiteSpace(json)) return null;

        return JsonSerializer.Deserialize<UserProfileResponse>(json, JsonOptions);
    }

    public async Task<CompanySettingsResponse?> GetCompanySettingsAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetScopeAsync(connection, transaction, tenantId, companyId, ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT identity.get_company_settings(@tenantId, @companyId);",
            connection,
            transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        await transaction.CommitAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return null;

        return JsonSerializer.Deserialize<CompanySettingsResponse>(json, JsonOptions);
    }

    public async Task<CompanySettingsResponse?> UpdateCompanySettingsAsync(
        Guid tenantId,
        Guid companyId,
        UpdateCompanySettingsCommand command,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetScopeAsync(connection, transaction, tenantId, companyId, ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT identity.update_company_settings(@tenantId, @companyId, @name, @legalName, @regNum, @timezone);",
            connection,
            transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("name", (object?)command.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("legalName", (object?)command.LegalName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("regNum", (object?)command.RegistrationNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("timezone", (object?)command.Timezone ?? DBNull.Value);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        await transaction.CommitAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return null;

        return JsonSerializer.Deserialize<CompanySettingsResponse>(json, JsonOptions);
    }

    public async Task<CompanyCurrencySettingsResponse?> UpdateCompanyCurrencySettingsAsync(
        Guid tenantId,
        Guid companyId,
        Guid actorUserId,
        UpdateCompanyCurrencySettingsCommand command,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetScopeAsync(connection, transaction, tenantId, companyId, ct);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT identity.update_company_currency_settings(
                @tenantId, @companyId, @actorUserId,
                @functionalCurrency, @presentationCurrency, @effectiveDate,
                @confirmFutureTransactions, @changeReason
            );
            """,
            connection,
            transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("actorUserId", actorUserId);
        cmd.Parameters.AddWithValue("functionalCurrency", command.FunctionalCurrencyCode);
        cmd.Parameters.AddWithValue("presentationCurrency", command.PresentationCurrencyCode);
        cmd.Parameters.AddWithValue("effectiveDate", command.EffectiveDate);
        cmd.Parameters.AddWithValue("confirmFutureTransactions", command.ConfirmFutureTransactions);
        cmd.Parameters.Add(new NpgsqlParameter("changeReason", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)command.ChangeReason ?? DBNull.Value });

        string? json;
        try
        {
            json = await cmd.ExecuteScalarAsync(ct) as string;
            await transaction.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState is "P0001" or "22023")
        {
            throw new InvalidOperationException(ex.MessageText, ex);
        }

        if (string.IsNullOrWhiteSpace(json)) return null;

        var settings = JsonSerializer.Deserialize<CompanySettingsResponse>(json, JsonOptions);
        if (settings is null) return null;

        var functionalCurrency = (settings.FunctionalCurrencyCode ?? settings.BaseCurrencyCode ?? settings.CurrencyCode).Trim().ToUpperInvariant();
        return new CompanyCurrencySettingsResponse(
            CompanyId: settings.CompanyId,
            TenantId: settings.TenantId,
            CompanyCode: settings.CompanyCode,
            CompanyName: settings.Name,
            FunctionalCurrencyCode: functionalCurrency,
            PresentationCurrencyCode: (settings.PresentationCurrencyCode ?? functionalCurrency).Trim().ToUpperInvariant(),
            CurrencyEffectiveDate: settings.CurrencyEffectiveDate ?? command.EffectiveDate,
            HasPostedTransactions: settings.HasPostedTransactions,
            UpdatedAt: settings.UpdatedAt,
            FunctionalCurrencyGuidance: FoundationService.FunctionalCurrencyGuidanceText,
            PresentationCurrencyGuidance: FoundationService.PresentationCurrencyGuidanceText);
    }

    public async Task<IReadOnlyList<CompanyCurrencyHistoryItem>> ListCompanyCurrencyHistoryAsync(
        Guid tenantId,
        Guid companyId,
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetScopeAsync(connection, transaction, tenantId, companyId, ct);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT history_id, previous_functional_currency, new_functional_currency,
                   previous_presentation_currency, new_presentation_currency,
                   previous_effective_date, new_effective_date,
                   confirmed_future_transactions, change_reason,
                   changed_by_user_id, changed_by_email, changed_by_name, changed_at
            FROM identity.list_company_currency_history(@tenantId, @companyId, @limit);
            """,
            connection,
            transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("limit", limit);

        var items = new List<CompanyCurrencyHistoryItem>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                items.Add(new CompanyCurrencyHistoryItem(
                    HistoryId: reader.GetGuid(0),
                    PreviousFunctionalCurrency: reader.GetString(1).Trim().ToUpperInvariant(),
                    NewFunctionalCurrency: reader.GetString(2).Trim().ToUpperInvariant(),
                    PreviousPresentationCurrency: reader.GetString(3).Trim().ToUpperInvariant(),
                    NewPresentationCurrency: reader.GetString(4).Trim().ToUpperInvariant(),
                    PreviousEffectiveDate: reader.GetFieldValue<DateOnly>(5),
                    NewEffectiveDate: reader.GetFieldValue<DateOnly>(6),
                    ConfirmedFutureTransactions: reader.GetBoolean(7),
                    ChangeReason: reader.IsDBNull(8) ? null : reader.GetString(8),
                    ChangedByUserId: reader.GetGuid(9),
                    ChangedByEmail: reader.IsDBNull(10) ? null : reader.GetString(10),
                    ChangedByName: reader.IsDBNull(11) ? null : reader.GetString(11),
                    ChangedAt: reader.GetFieldValue<DateTimeOffset>(12)));
            }
        }

        await transaction.CommitAsync(ct);
        return items;
    }

    private static async Task SetScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid companyId,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenantId, true), set_config('app.company_id', @companyId, true);",
            connection,
            transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId.ToString());
        cmd.Parameters.AddWithValue("companyId", companyId.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CompanyUserSummary>> ListCompanyUsersAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT user_id, email, display_name, status, roles, created_at FROM identity.list_company_users(@tenantId, @companyId);",
            connection);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<CompanyUserSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var rolesJson = reader.GetString(4);
            var roles = JsonSerializer.Deserialize<List<UserRoleDetail>>(rolesJson, JsonOptions) ?? [];

            list.Add(new CompanyUserSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                roles,
                reader.GetFieldValue<DateTimeOffset>(5)));
        }
        return list;
    }

    public async Task<CompanyUserSummary?> InviteCompanyUserAsync(
        Guid tenantId,
        Guid companyId,
        InviteCompanyUserCommand command,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT identity.invite_or_assign_company_user(@tenantId, @companyId, @email, @displayName, @roleCode);",
            connection);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("email", command.Email);
        cmd.Parameters.AddWithValue("displayName", command.DisplayName);
        cmd.Parameters.Add(new NpgsqlParameter("roleCode", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)command.RoleCode ?? DBNull.Value });

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        if (string.IsNullOrWhiteSpace(json)) return null;

        var result = JsonSerializer.Deserialize<JsonElement>(json);
        var userId = result.GetProperty("userId").GetGuid();
        var email = result.GetProperty("email").GetString() ?? command.Email;
        var displayName = result.GetProperty("displayName").GetString() ?? command.DisplayName;
        var status = result.GetProperty("status").GetString() ?? "active";
        var roles = string.IsNullOrWhiteSpace(command.RoleCode)
            ? new List<UserRoleDetail>()
            : new List<UserRoleDetail> { new(command.RoleCode, command.RoleCode) };

        return new CompanyUserSummary(userId, email, displayName, status, roles, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<RoleDetail>> ListRolesAndPermissionsAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT role_id, role_code, name, is_system_role, permissions FROM identity.list_roles_and_permissions(@tenantId);",
            connection);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        var list = new List<RoleDetail>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var permsJson = reader.GetString(4);
            var perms = JsonSerializer.Deserialize<List<RolePermissionDetail>>(permsJson, JsonOptions) ?? [];

            list.Add(new RoleDetail(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                perms));
        }
        return list;
    }

    public async Task<IReadOnlyList<AuditEventSummary>> ListAuditEventsAsync(
        Guid tenantId,
        Guid companyId,
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT event_id, occurred_at, actor_email, actor_name, event_code, entity_type, entity_id, metadata " +
            "FROM identity.list_audit_events(@tenantId, @companyId, @limit);",
            connection);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("limit", limit);

        var list = new List<AuditEventSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var metaJson = reader.GetString(7);
            var metadata = JsonSerializer.Deserialize<JsonElement>(metaJson);

            list.Add(new AuditEventSummary(
                reader.GetGuid(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                metadata));
        }
        return list;
    }

    public async Task<Guid> RecordAuditEventAsync(
        Guid tenantId,
        Guid companyId,
        Guid actorUserId,
        string eventCode,
        string entityType,
        Guid? entityId,
        object? metadata = null,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty tenantId is required to record an audit event.", nameof(tenantId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty companyId is required to record an audit event.", nameof(companyId));
        }

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetScopeAsync(connection, transaction, tenantId, companyId, ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT identity.record_audit_event(@tenantId, @companyId, @actorUserId, @eventCode, @entityType, @entityId, @metadata::jsonb);",
            connection,
            transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("actorUserId", actorUserId);
        cmd.Parameters.AddWithValue("eventCode", eventCode);
        cmd.Parameters.AddWithValue("entityType", entityType);
        cmd.Parameters.AddWithValue("entityId", (object?)entityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("metadata", metadata is null ? "{}" : JsonSerializer.Serialize(metadata));

        var result = await cmd.ExecuteScalarAsync(ct);
        await transaction.CommitAsync(ct);
        return result is Guid g ? g : Guid.Empty;
    }
}
