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
        await using var cmd = new NpgsqlCommand(
            "SELECT identity.get_company_settings(@tenantId, @companyId);",
            connection);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
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
        await using var cmd = new NpgsqlCommand(
            "SELECT identity.update_company_settings(@tenantId, @companyId, @name, @legalName, @regNum, @timezone);",
            connection);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("name", (object?)command.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("legalName", (object?)command.LegalName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("regNum", (object?)command.RegistrationNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("timezone", (object?)command.Timezone ?? DBNull.Value);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        if (string.IsNullOrWhiteSpace(json)) return null;

        return JsonSerializer.Deserialize<CompanySettingsResponse>(json, JsonOptions);
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
        cmd.Parameters.AddWithValue("roleCode", (object?)command.RoleCode ?? DBNull.Value);

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
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT identity.record_audit_event(@tenantId, @companyId, @actorUserId, @eventCode, @entityType, @entityId, @metadata::jsonb);",
            connection);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("actorUserId", actorUserId);
        cmd.Parameters.AddWithValue("eventCode", eventCode);
        cmd.Parameters.AddWithValue("entityType", entityType);
        cmd.Parameters.AddWithValue("entityId", (object?)entityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("metadata", metadata is null ? "{}" : JsonSerializer.Serialize(metadata));

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid g ? g : Guid.Empty;
    }
}
