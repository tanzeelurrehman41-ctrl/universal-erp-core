using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using UniversalErp.Application.Sync;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Infrastructure.Sync;

public sealed class PostgresSyncChangeFeed(NpgsqlDataSource dataSource) : ISyncChangeFeed
{
    public async Task<SyncChangePage> ReadPageAsync(
        TenantContext context,
        string entityType,
        long afterSequence,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var setScope = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenantId, true), set_config('app.company_id', @companyId, true)",
            connection,
            transaction))
        {
            setScope.Parameters.AddWithValue("tenantId", context.TenantId.ToString());
            setScope.Parameters.AddWithValue("companyId", context.CompanyId.ToString());
            await setScope.ExecuteNonQueryAsync(cancellationToken);
        }

        var changes = new List<SyncChange>(pageSize + 1);
        await using (var command = new NpgsqlCommand(
            "SELECT company_sequence, entity_type, entity_id, entity_version, change_type, payload::text, occurred_at FROM sync.change_log WHERE tenant_id = @tenantId AND company_id = @companyId AND entity_type = @entityType AND company_sequence > @afterSequence ORDER BY company_sequence LIMIT @rowLimit",
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("tenantId", context.TenantId);
            command.Parameters.AddWithValue("companyId", context.CompanyId);
            command.Parameters.AddWithValue("entityType", entityType);
            command.Parameters.AddWithValue("afterSequence", afterSequence);
            command.Parameters.AddWithValue("rowLimit", pageSize + 1);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                using var payload = JsonDocument.Parse(reader.GetString(5));
                changes.Add(new SyncChange(
                    reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetInt64(3),
                    reader.GetString(4),
                    payload.RootElement.Clone(),
                    reader.GetFieldValue<DateTimeOffset>(6)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        var hasMore = changes.Count > pageSize;
        if (hasMore) changes.RemoveAt(changes.Count - 1);
        var nextCursor = changes.Count == 0
            ? afterSequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : changes[^1].Cursor;
        return new SyncChangePage(nextCursor, hasMore, changes);
    }
}

public static class CompanyChangeLogWriter
{
    public static async Task AppendTenantWideAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid currentCompanyId,
        string entityType,
        Guid entityId,
        long entityVersion,
        string changeType,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var companyIds = new List<Guid>();
        await using (var companies = new NpgsqlCommand(
            "SELECT id FROM organization.companies WHERE tenant_id = @tenantId AND status = 'active' ORDER BY id",
            connection,
            transaction))
        {
            companies.Parameters.AddWithValue("tenantId", tenantId);
            await using var reader = await companies.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) companyIds.Add(reader.GetGuid(0));
        }

        if (!companyIds.Contains(currentCompanyId))
        {
            throw new InvalidOperationException("The active company is outside the tenant product catalog scope.");
        }

        foreach (var companyId in companyIds)
        {
            await using (var setCompany = new NpgsqlCommand(
                "SELECT set_config('app.company_id', @companyId, true)",
                connection,
                transaction))
            {
                setCompany.Parameters.AddWithValue("companyId", companyId.ToString());
                await setCompany.ExecuteNonQueryAsync(cancellationToken);
            }

            await AppendAsync(
                connection,
                transaction,
                tenantId,
                companyId,
                entityType,
                entityId,
                entityVersion,
                changeType,
                payloadJson,
                cancellationToken);
        }
    }

    public static async Task<long> AppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid companyId,
        string entityType,
        Guid entityId,
        long entityVersion,
        string changeType,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        long companySequence;
        await using (var allocate = new NpgsqlCommand(
            "INSERT INTO sync.company_change_counters (tenant_id, company_id, last_sequence) VALUES (@tenantId, @companyId, 1) ON CONFLICT (tenant_id, company_id) DO UPDATE SET last_sequence = sync.company_change_counters.last_sequence + 1, updated_at = now() RETURNING last_sequence",
            connection,
            transaction))
        {
            allocate.Parameters.AddWithValue("tenantId", tenantId);
            allocate.Parameters.AddWithValue("companyId", companyId);
            companySequence = (long)(await allocate.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not allocate the company change cursor."));
        }

        await using (var insert = new NpgsqlCommand(
            "INSERT INTO sync.change_log (tenant_id, company_id, company_sequence, entity_type, entity_id, entity_version, change_type, payload) VALUES (@tenantId, @companyId, @companySequence, @entityType, @entityId, @entityVersion, @changeType, @payload)",
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue("tenantId", tenantId);
            insert.Parameters.AddWithValue("companyId", companyId);
            insert.Parameters.AddWithValue("companySequence", companySequence);
            insert.Parameters.AddWithValue("entityType", entityType);
            insert.Parameters.AddWithValue("entityId", entityId);
            insert.Parameters.AddWithValue("entityVersion", entityVersion);
            insert.Parameters.AddWithValue("changeType", changeType);
            insert.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = payloadJson;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        return companySequence;
    }
}
