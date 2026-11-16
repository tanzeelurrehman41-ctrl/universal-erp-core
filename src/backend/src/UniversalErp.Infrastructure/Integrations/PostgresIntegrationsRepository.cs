using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UniversalErp.Application.Integrations;

namespace UniversalErp.Infrastructure.Integrations;

public class PostgresIntegrationsRepository : IIntegrationsRepository
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly string? _connectionString;
    private readonly WebhookSigningEngine _signingEngine;

    public PostgresIntegrationsRepository(NpgsqlDataSource dataSource, WebhookSigningEngine signingEngine)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _signingEngine = signingEngine ?? throw new ArgumentNullException(nameof(signingEngine));
    }

    public PostgresIntegrationsRepository(string connectionString, WebhookSigningEngine signingEngine)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _signingEngine = signingEngine ?? throw new ArgumentNullException(nameof(signingEngine));
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(Guid tenantId, Guid? companyId, CancellationToken ct)
    {
        var conn = _dataSource != null
            ? await _dataSource.OpenConnectionAsync(ct)
            : new NpgsqlConnection(_connectionString);

        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        const string setScopeSql = "SELECT set_config('app.tenant_id', @tenantId, false), set_config('app.company_id', @companyId, false);";
        await using var cmd = new NpgsqlCommand(setScopeSql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId.ToString());
        cmd.Parameters.AddWithValue("companyId", companyId.HasValue && companyId.Value != Guid.Empty ? companyId.Value.ToString() : "");
        await cmd.ExecuteNonQueryAsync(ct);

        return conn;
    }

    // ── 1. Webhook Subscriptions ────────────────────────────────────────────────

    public async Task<IReadOnlyList<WebhookSubscriptionDto>> GetWebhookSubscriptionsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);

        const string sql = @"
            SELECT id, tenant_id, company_id, event_type, target_url, is_active, created_at, updated_at
            FROM integrations.webhook_subscriptions
            WHERE tenant_id = @tenantId AND company_id = @companyId
            ORDER BY created_at DESC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<WebhookSubscriptionDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new WebhookSubscriptionDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.GetDateTime(6),
                reader.GetDateTime(7)
            ));
        }

        return list;
    }

    public async Task<WebhookSubscriptionDto?> GetWebhookSubscriptionByIdAsync(Guid tenantId, Guid subscriptionId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        const string sql = @"
            SELECT id, tenant_id, company_id, event_type, target_url, is_active, created_at, updated_at
            FROM integrations.webhook_subscriptions
            WHERE tenant_id = @tenantId AND id = @subId;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("subId", subscriptionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new WebhookSubscriptionDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.GetDateTime(6),
                reader.GetDateTime(7)
            );
        }

        return null;
    }

    public async Task<WebhookSubscriptionCreateResult> CreateWebhookSubscriptionAsync(Guid tenantId, Guid companyId, CreateWebhookSubscriptionRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);

        var plaintextSecret = string.IsNullOrWhiteSpace(request.Secret)
            ? _signingEngine.GenerateSecret()
            : request.Secret;

        var secretHash = _signingEngine.HashSecret(plaintextSecret);

        const string sql = @"
            INSERT INTO integrations.webhook_subscriptions (
                tenant_id, company_id, event_type, target_url, secret_hash, is_active
            )
            VALUES (
                @tenantId, @companyId, @eventType, @targetUrl, @secretHash, true
            )
            RETURNING id, tenant_id, company_id, event_type, target_url, is_active, created_at, updated_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("eventType", request.EventType);
        cmd.Parameters.AddWithValue("targetUrl", request.TargetUrl);
        cmd.Parameters.AddWithValue("secretHash", secretHash);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var dto = new WebhookSubscriptionDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.GetDateTime(6),
                reader.GetDateTime(7)
            );

            return new WebhookSubscriptionCreateResult(dto, plaintextSecret);
        }

        throw new InvalidOperationException("Failed to insert webhook subscription.");
    }

    public async Task<RotateWebhookSecretResult> RotateWebhookSecretAsync(Guid tenantId, Guid subscriptionId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        var newPlaintext = _signingEngine.GenerateSecret();
        var newHash = _signingEngine.HashSecret(newPlaintext);

        const string sql = @"
            UPDATE integrations.webhook_subscriptions
            SET secret_hash = @hash,
                updated_at = now()
            WHERE tenant_id = @tenantId AND id = @subId
            RETURNING updated_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("subId", subscriptionId);
        cmd.Parameters.AddWithValue("hash", newHash);

        var res = await cmd.ExecuteScalarAsync(ct);
        if (res is DateTime updatedAt)
        {
            return new RotateWebhookSecretResult(subscriptionId, newPlaintext, newHash, updatedAt);
        }

        throw new InvalidOperationException($"Webhook subscription {subscriptionId} not found.");
    }

    public async Task<bool> DeleteWebhookSubscriptionAsync(Guid tenantId, Guid subscriptionId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        const string sql = @"
            DELETE FROM integrations.webhook_subscriptions
            WHERE tenant_id = @tenantId AND id = @subId;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("subId", subscriptionId);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    // ── 2. Outbound Event Deliveries ────────────────────────────────────────────

    public async Task<OutboundEventDeliveryDto> EnqueueEventDeliveryAsync(Guid tenantId, EnqueueEventRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        const string sql = @"
            INSERT INTO integrations.outbound_event_deliveries (
                tenant_id, subscription_id, event_type, event_payload, delivery_status, attempt_count
            )
            VALUES (
                @tenantId, @subId, @eventType, @payload::jsonb, 'pending', 0
            )
            RETURNING id, subscription_id, event_type, event_payload::text, delivery_status,
                      attempt_count, last_response_code, last_error, next_retry_at,
                      delivered_at, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("subId", request.SubscriptionId);
        cmd.Parameters.AddWithValue("eventType", request.EventType);
        cmd.Parameters.AddWithValue("payload", request.EventPayloadJson);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapDelivery(reader);
        }

        throw new InvalidOperationException("Failed to enqueue event delivery.");
    }

    public async Task<IReadOnlyList<OutboundEventDeliveryDto>> GetEventDeliveriesAsync(Guid tenantId, Guid? subscriptionId = null, string? status = null, int limit = 50, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        var sql = @"
            SELECT id, subscription_id, event_type, event_payload::text, delivery_status,
                   attempt_count, last_response_code, last_error, next_retry_at,
                   delivered_at, created_at
            FROM integrations.outbound_event_deliveries
            WHERE tenant_id = @tenantId";

        if (subscriptionId.HasValue && subscriptionId.Value != Guid.Empty)
            sql += " AND subscription_id = @subId";
        if (!string.IsNullOrWhiteSpace(status))
            sql += " AND delivery_status = @status";

        sql += " ORDER BY created_at DESC LIMIT @limit;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        if (subscriptionId.HasValue && subscriptionId.Value != Guid.Empty)
            cmd.Parameters.AddWithValue("subId", subscriptionId.Value);
        if (!string.IsNullOrWhiteSpace(status))
            cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("limit", limit);

        var list = new List<OutboundEventDeliveryDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(MapDelivery(reader));
        }

        return list;
    }

    public async Task<OutboundEventDeliveryDto> RecordDeliveryAttemptAsync(Guid tenantId, Guid deliveryId, int statusCode, string? error, bool succeeded, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        // Fetch current attempt count
        const string curSql = @"SELECT attempt_count FROM integrations.outbound_event_deliveries WHERE tenant_id = @tenantId AND id = @deliveryId;";
        await using var curCmd = new NpgsqlCommand(curSql, conn);
        curCmd.Parameters.AddWithValue("tenantId", tenantId);
        curCmd.Parameters.AddWithValue("deliveryId", deliveryId);
        var currentAttempt = Convert.ToInt32(await curCmd.ExecuteScalarAsync(ct) ?? 0);

        var newAttempt = currentAttempt + 1;
        string newStatus;
        DateTimeOffset? nextRetry = null;
        DateTimeOffset? deliveredAt = null;

        if (succeeded)
        {
            newStatus = "delivered";
            deliveredAt = DateTimeOffset.UtcNow;
        }
        else
        {
            nextRetry = _signingEngine.CalculateNextRetry(newAttempt, DateTimeOffset.UtcNow);
            newStatus = nextRetry.HasValue ? "failed" : "exhausted"; // dead-letter
        }

        const string updSql = @"
            UPDATE integrations.outbound_event_deliveries
            SET delivery_status = @status,
                attempt_count = @attempt,
                last_response_code = @code,
                last_error = @error,
                next_retry_at = @nextRetry,
                delivered_at = @deliveredAt
            WHERE tenant_id = @tenantId AND id = @deliveryId
            RETURNING id, subscription_id, event_type, event_payload::text, delivery_status,
                      attempt_count, last_response_code, last_error, next_retry_at,
                      delivered_at, created_at;";

        await using var cmd = new NpgsqlCommand(updSql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("deliveryId", deliveryId);
        cmd.Parameters.AddWithValue("status", newStatus);
        cmd.Parameters.AddWithValue("attempt", newAttempt);
        cmd.Parameters.AddWithValue("code", statusCode);
        cmd.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("nextRetry", (object?)nextRetry ?? DBNull.Value);
        cmd.Parameters.AddWithValue("deliveredAt", (object?)deliveredAt ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapDelivery(reader);
        }

        throw new InvalidOperationException($"Delivery {deliveryId} not found.");
    }

    public async Task<OutboundEventDeliveryDto> RetryDeliveryAsync(Guid tenantId, Guid deliveryId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        const string sql = @"
            UPDATE integrations.outbound_event_deliveries
            SET delivery_status = 'pending',
                next_retry_at = now()
            WHERE tenant_id = @tenantId AND id = @deliveryId
            RETURNING id, subscription_id, event_type, event_payload::text, delivery_status,
                      attempt_count, last_response_code, last_error, next_retry_at,
                      delivered_at, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("deliveryId", deliveryId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapDelivery(reader);
        }

        throw new InvalidOperationException($"Delivery {deliveryId} not found.");
    }

    // ── 3. API Partners ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ApiPartnerDto>> GetApiPartnersAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);

        const string sql = @"
            SELECT id, tenant_id, company_id, partner_name, permissions::text, is_active, created_at
            FROM integrations.api_partners
            WHERE tenant_id = @tenantId AND company_id = @companyId
            ORDER BY created_at DESC;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);

        var list = new List<ApiPartnerDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var permJson = reader.GetString(4);
            var perms = JsonSerializer.Deserialize<List<string>>(permJson) ?? new List<string>();

            list.Add(new ApiPartnerDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                perms,
                reader.GetBoolean(5),
                reader.GetDateTime(6)
            ));
        }

        return list;
    }

    public async Task<CreateApiPartnerResult> CreateApiPartnerAsync(Guid tenantId, Guid companyId, CreateApiPartnerRequest request, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, companyId, ct);

        var plaintextApiKey = "erp_live_" + _signingEngine.GenerateSecret();
        var keyHash = _signingEngine.HashSecret(plaintextApiKey);
        var permJson = JsonSerializer.Serialize(request.Permissions);

        const string sql = @"
            INSERT INTO integrations.api_partners (
                tenant_id, company_id, partner_name, api_key_hash, permissions, is_active
            )
            VALUES (
                @tenantId, @companyId, @name, @keyHash, @perms::jsonb, true
            )
            RETURNING id, tenant_id, company_id, partner_name, permissions::text, is_active, created_at;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("companyId", companyId);
        cmd.Parameters.AddWithValue("name", request.PartnerName);
        cmd.Parameters.AddWithValue("keyHash", keyHash);
        cmd.Parameters.AddWithValue("perms", permJson);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var perms = JsonSerializer.Deserialize<List<string>>(reader.GetString(4)) ?? new List<string>();
            var partner = new ApiPartnerDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                perms,
                reader.GetBoolean(5),
                reader.GetDateTime(6)
            );

            return new CreateApiPartnerResult(partner, plaintextApiKey);
        }

        throw new InvalidOperationException("Failed to insert API partner.");
    }

    public async Task<bool> RevokeApiPartnerAsync(Guid tenantId, Guid partnerId, CancellationToken ct = default)
    {
        await using var conn = await OpenConnectionAsync(tenantId, null, ct);

        const string sql = @"
            UPDATE integrations.api_partners
            SET is_active = false
            WHERE tenant_id = @tenantId AND id = @partnerId;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("partnerId", partnerId);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    private static OutboundEventDeliveryDto MapDelivery(NpgsqlDataReader r)
    {
        return new OutboundEventDeliveryDto(
            r.GetGuid(0),
            r.GetGuid(1),
            r.GetString(2),
            r.GetString(3),
            r.GetString(4),
            r.GetInt32(5),
            r.IsDBNull(6) ? null : r.GetInt32(6),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.IsDBNull(8) ? null : r.GetDateTime(8),
            r.IsDBNull(9) ? null : r.GetDateTime(9),
            r.GetDateTime(10)
        );
    }
}
