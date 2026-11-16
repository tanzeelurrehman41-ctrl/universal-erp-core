using System.Data;
using System.Text.Json;
using Npgsql;
using UniversalErp.Application.SaaS;

namespace UniversalErp.Infrastructure.SaaS;

public sealed class PostgresSaaSRepository : ISaaSRepository
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly string? _connectionString;

    public PostgresSaaSRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public PostgresSaaSRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(Guid? tenantId, CancellationToken ct)
    {
        var conn = _dataSource != null
            ? await _dataSource.OpenConnectionAsync(ct)
            : new NpgsqlConnection(_connectionString);

        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        if (tenantId.HasValue)
        {
            const string setScopeSql = "SELECT set_config('app.tenant_id', @tenantId, false);";
            await using var cmd = new NpgsqlCommand(setScopeSql, conn);
            cmd.Parameters.AddWithValue("tenantId", tenantId.Value.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return conn;
    }

    // ── Plans & Pricing ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(null, cancellationToken);

        const string sql = """
            SELECT p.id, p.plan_code, p.name, p.description, p.tier_rank, p.status, p.is_public,
                   f.feature_code, f.enabled, f.quantity_limit, f.limit_unit
            FROM platform.subscription_plans p
            LEFT JOIN platform.plan_features f ON p.id = f.plan_id
            ORDER BY p.tier_rank, f.feature_code;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var planMap = new Dictionary<Guid, (Guid Id, string Code, string Name, string? Desc, int Rank, string Status, bool IsPublic, List<PlanFeatureDto> Features)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var planId = reader.GetGuid(0);
            if (!planMap.TryGetValue(planId, out var entry))
            {
                entry = (
                    planId,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetBoolean(6),
                    new List<PlanFeatureDto>());
                planMap[planId] = entry;
            }

            if (!reader.IsDBNull(7))
            {
                entry.Features.Add(new PlanFeatureDto(
                    reader.GetString(7),
                    reader.GetBoolean(8),
                    reader.IsDBNull(9) ? null : reader.GetInt64(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10)));
            }
        }
        await reader.CloseAsync();

        // Fetch pricing
        var pricingList = await ListPricingAsync(cancellationToken);
        var pricingGroup = pricingList.GroupBy(p => p.PlanId).ToDictionary(g => g.Key, g => g.ToList());

        return planMap.Values.Select(p => new SubscriptionPlanDto(
            p.Id,
            p.Code,
            p.Name,
            p.Desc,
            p.Rank,
            p.Status,
            p.IsPublic,
            pricingGroup.TryGetValue(p.Id, out var prices) ? prices : new List<PlanPricingDto>(),
            p.Features)).ToList();
    }

    public async Task<SubscriptionPlanDto?> GetPlanByCodeAsync(string planCode, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.PlanCode, planCode, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<PlanPricingDto>> ListPricingAsync(CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(null, cancellationToken);

        const string sql = """
            SELECT id, plan_id, billing_interval, currency_code, base_price, effective_from, effective_until, is_active
            FROM platform.plan_pricing
            WHERE is_active = true
            ORDER BY plan_id, billing_interval;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var list = new List<PlanPricingDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new PlanPricingDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetBoolean(7)));
        }

        return list;
    }

    // ── Tenant Provisioning ─────────────────────────────────────────────────

    public async Task<TenantProvisioningResult> ProvisionTenantAsync(
        Guid idempotencyKey,
        TenantProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(null, cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        // 1. Idempotency check
        await using (var checkCmd = new NpgsqlCommand(
            "SELECT tenant_id, result_metadata FROM platform.tenant_provisioning_jobs WHERE idempotency_key = @key",
            conn, tx))
        {
            checkCmd.Parameters.AddWithValue("key", idempotencyKey);
            await using var reader = await checkCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var existingTenantId = reader.GetGuid(0);
                var metaJson = reader.GetString(1);
                await reader.CloseAsync();

                using var doc = JsonDocument.Parse(metaJson);
                var root = doc.RootElement;
                await tx.CommitAsync(cancellationToken);

                return new TenantProvisioningResult(
                    existingTenantId,
                    root.GetProperty("tenantCode").GetString()!,
                    root.GetProperty("displayName").GetString()!,
                    Guid.Parse(root.GetProperty("companyId").GetString()!),
                    root.GetProperty("companyCode").GetString()!,
                    Guid.Parse(root.GetProperty("branchId").GetString()!),
                    root.GetProperty("branchCode").GetString()!,
                    Guid.Parse(root.GetProperty("subscriptionId").GetString()!),
                    root.GetProperty("planCode").GetString()!,
                    Guid.Parse(root.GetProperty("adminUserId").GetString()!),
                    root.GetProperty("adminEmail").GetString()!,
                    "active",
                    DateTimeOffset.UtcNow);
            }
            await reader.CloseAsync();
        }

        // 2. Validate tenant code
        var tenantCode = request.TenantCode!.Trim().ToLowerInvariant();
        await using (var codeCmd = new NpgsqlCommand("SELECT 1 FROM platform.tenants WHERE tenant_code = @code", conn, tx))
        {
            codeCmd.Parameters.AddWithValue("code", tenantCode);
            if (await codeCmd.ExecuteScalarAsync(cancellationToken) != null)
            {
                throw new InvalidOperationException($"Tenant code '{tenantCode}' is already registered.");
            }
        }

        // 3. Resolve plan
        var planCode = request.PlanCode ?? "starter";
        Guid planId;
        await using (var planCmd = new NpgsqlCommand("SELECT id FROM platform.subscription_plans WHERE plan_code = @code AND status = 'active'", conn, tx))
        {
            planCmd.Parameters.AddWithValue("code", planCode.ToLowerInvariant());
            var pRes = await planCmd.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException($"Subscription plan '{planCode}' is not active or does not exist.");
            planId = (Guid)pRes;
        }

        // 4. Resolve base price
        var interval = request.BillingInterval ?? "month";
        decimal basePrice = 0m;
        await using (var priceCmd = new NpgsqlCommand(
            "SELECT base_price FROM platform.plan_pricing WHERE plan_id = @planId AND billing_interval = @interval AND is_active = true LIMIT 1",
            conn, tx))
        {
            priceCmd.Parameters.AddWithValue("planId", planId);
            priceCmd.Parameters.AddWithValue("interval", interval.ToLowerInvariant());
            var prRes = await priceCmd.ExecuteScalarAsync(cancellationToken);
            if (prRes != null && prRes != DBNull.Value)
            {
                basePrice = Convert.ToDecimal(prRes);
            }
        }

        // 5. Generate identifiers
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var companyCode = request.CompanyCode ?? $"{tenantCode.ToUpperInvariant()}-CORP";
        var branchCode = request.BranchCode ?? "HQ";
        var branchName = request.BranchName ?? "Headquarters Main";
        var countryCode = request.CountryCode ?? "PK";
        var currencyCode = request.BaseCurrencyCode ?? "PKR";
        var timezone = request.Timezone ?? "Asia/Karachi";

        // 6. Insert Tenant
        await using (var insTenant = new NpgsqlCommand(
            """
            INSERT INTO platform.tenants
                (id, tenant_code, display_name, legal_name, status, default_country_code, default_currency_code, default_timezone)
            VALUES
                (@id, @code, @displayName, @legalName, 'active', @country, @currency, @tz)
            """, conn, tx))
        {
            insTenant.Parameters.AddWithValue("id", tenantId);
            insTenant.Parameters.AddWithValue("code", tenantCode);
            insTenant.Parameters.AddWithValue("displayName", request.DisplayName!);
            insTenant.Parameters.AddWithValue("legalName", (object?)request.LegalName ?? DBNull.Value);
            insTenant.Parameters.AddWithValue("country", countryCode);
            insTenant.Parameters.AddWithValue("currency", currencyCode);
            insTenant.Parameters.AddWithValue("tz", timezone);
            await insTenant.ExecuteNonQueryAsync(cancellationToken);
        }

        // Set RLS scope for newly created tenant
        await using (var scopeCmd = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tId, false);", conn, tx))
        {
            scopeCmd.Parameters.AddWithValue("tId", tenantId.ToString());
            await scopeCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 7. Insert Subscription
        var now = DateTimeOffset.UtcNow;
        var periodEnd = interval == "year" ? now.AddYears(1) : now.AddMonths(1);
        await using (var insSub = new NpgsqlCommand(
            """
            INSERT INTO platform.subscriptions
                (id, tenant_id, plan_id, status, billing_interval, currency_code, current_price, starts_at, current_period_start, current_period_end)
            VALUES
                (@id, @tenantId, @planId, 'active', @interval, @currency, @price, @now, @now, @end)
            """, conn, tx))
        {
            insSub.Parameters.AddWithValue("id", subscriptionId);
            insSub.Parameters.AddWithValue("tenantId", tenantId);
            insSub.Parameters.AddWithValue("planId", planId);
            insSub.Parameters.AddWithValue("interval", interval);
            insSub.Parameters.AddWithValue("currency", currencyCode);
            insSub.Parameters.AddWithValue("price", basePrice);
            insSub.Parameters.AddWithValue("now", now);
            insSub.Parameters.AddWithValue("end", periodEnd);
            await insSub.ExecuteNonQueryAsync(cancellationToken);
        }

        // 8. Snapshot entitlements from plan_features
        await using (var insEnt = new NpgsqlCommand(
            """
            INSERT INTO platform.tenant_entitlements
                (tenant_id, feature_code, enabled, quantity_limit, source_type, source_id, effective_from)
            SELECT
                @tenantId, feature_code, enabled, quantity_limit, 'plan', @planId, now()
            FROM platform.plan_features
            WHERE plan_id = @planId
            """, conn, tx))
        {
            insEnt.Parameters.AddWithValue("tenantId", tenantId);
            insEnt.Parameters.AddWithValue("planId", planId);
            await insEnt.ExecuteNonQueryAsync(cancellationToken);
        }

        // 9. Insert Company
        await using (var insComp = new NpgsqlCommand(
            """
            INSERT INTO organization.companies
                (id, tenant_id, company_code, name, legal_name, registration_number, country_code, base_currency_code, timezone, status)
            VALUES
                (@id, @tenantId, @code, @name, @legalName, NULL, @country, @currency, @tz, 'active')
            """, conn, tx))
        {
            insComp.Parameters.AddWithValue("id", companyId);
            insComp.Parameters.AddWithValue("tenantId", tenantId);
            insComp.Parameters.AddWithValue("code", companyCode);
            insComp.Parameters.AddWithValue("name", request.CompanyName ?? request.DisplayName!);
            insComp.Parameters.AddWithValue("legalName", (object?)request.LegalName ?? DBNull.Value);
            insComp.Parameters.AddWithValue("country", countryCode);
            insComp.Parameters.AddWithValue("currency", currencyCode);
            insComp.Parameters.AddWithValue("tz", timezone);
            await insComp.ExecuteNonQueryAsync(cancellationToken);
        }

        // 10. Insert Branch (organization.branches from Phase 13)
        await using (var insBranch = new NpgsqlCommand(
            """
            INSERT INTO organization.branches
                (id, tenant_id, company_id, branch_code, branch_name, branch_type, is_active, city, province_or_state, country_code)
            VALUES
                (@id, @tenantId, @companyId, @code, @name, 'headquarters', true, 'Islamabad', 'FEDERAL', @country)
            """, conn, tx))
        {
            insBranch.Parameters.AddWithValue("id", branchId);
            insBranch.Parameters.AddWithValue("tenantId", tenantId);
            insBranch.Parameters.AddWithValue("companyId", companyId);
            insBranch.Parameters.AddWithValue("code", branchCode);
            insBranch.Parameters.AddWithValue("name", branchName);
            insBranch.Parameters.AddWithValue("country", countryCode);
            await insBranch.ExecuteNonQueryAsync(cancellationToken);
        }

        // 11. Insert Warehouse linked to branch
        await using (var insWh = new NpgsqlCommand(
            """
            INSERT INTO erp_inventory.warehouse
                (tenant_id, company_id, warehouse_id, code, name, is_active, branch_id)
            VALUES
                (@tenantId, @companyId, @id, @code, @name, true, @branchId)
            """, conn, tx))
        {
            insWh.Parameters.AddWithValue("id", warehouseId);
            insWh.Parameters.AddWithValue("tenantId", tenantId);
            insWh.Parameters.AddWithValue("companyId", companyId);
            insWh.Parameters.AddWithValue("code", $"{branchCode}-WH");
            insWh.Parameters.AddWithValue("name", $"{branchName} Warehouse");
            insWh.Parameters.AddWithValue("branchId", branchId);
            await insWh.ExecuteNonQueryAsync(cancellationToken);
        }

        // 12. Insert Admin User
        var adminEmail = request.AdminEmail!.Trim().ToLowerInvariant();
        await using (var insUser = new NpgsqlCommand(
            """
            INSERT INTO identity.users
                (id, email_normalized, display_name, status)
            VALUES
                (@id, @email, @name, 'active')
            ON CONFLICT (email_normalized) DO UPDATE SET display_name = EXCLUDED.display_name
            RETURNING id;
            """, conn, tx))
        {
            insUser.Parameters.AddWithValue("id", adminUserId);
            insUser.Parameters.AddWithValue("email", adminEmail);
            insUser.Parameters.AddWithValue("name", request.AdminFullName ?? request.DisplayName!);
            var uid = await insUser.ExecuteScalarAsync(cancellationToken);
            if (uid != null)
            {
                adminUserId = (Guid)uid;
            }
        }

        // 13. Insert Tenant Membership & Company Membership
        await using (var insMem = new NpgsqlCommand(
            """
            INSERT INTO identity.tenant_memberships (id, tenant_id, user_id, status)
            VALUES (gen_random_uuid(), @tenantId, @userId, 'active')
            ON CONFLICT DO NOTHING;

            INSERT INTO identity.company_memberships (id, tenant_id, company_id, user_id, status)
            VALUES (gen_random_uuid(), @tenantId, @companyId, @userId, 'active')
            ON CONFLICT DO NOTHING;
            """, conn, tx))
        {
            insMem.Parameters.AddWithValue("tenantId", tenantId);
            insMem.Parameters.AddWithValue("companyId", companyId);
            insMem.Parameters.AddWithValue("userId", adminUserId);
            await insMem.ExecuteNonQueryAsync(cancellationToken);
        }

        // 14. Initialize Core Usage Aggregations
        var coreMetrics = new[] { "users", "companies", "branches", "transactions_monthly", "storage_mb", "api_calls_monthly", "integrations" };
        foreach (var metric in coreMetrics)
        {
            decimal initialVal = metric switch
            {
                "users" => 1m,
                "companies" => 1m,
                "branches" => 1m,
                _ => 0m
            };

            await using var insAgg = new NpgsqlCommand(
                """
                INSERT INTO platform.tenant_usage_aggregations
                    (id, tenant_id, metric_code, period_start, period_end, current_quantity, quantity_limit)
                SELECT
                    gen_random_uuid(), @tenantId, @metric, @now, @end, @val, f.quantity_limit
                FROM platform.plan_features f
                WHERE f.plan_id = @planId AND f.feature_code = 'max_' || @metric
                ON CONFLICT (tenant_id, metric_code, period_start, period_end) DO NOTHING;
                """, conn, tx);
            insAgg.Parameters.AddWithValue("tenantId", tenantId);
            insAgg.Parameters.AddWithValue("metric", metric);
            insAgg.Parameters.AddWithValue("now", now);
            insAgg.Parameters.AddWithValue("end", periodEnd);
            insAgg.Parameters.AddWithValue("val", initialVal);
            insAgg.Parameters.AddWithValue("planId", planId);
            await insAgg.ExecuteNonQueryAsync(cancellationToken);
        }

        // 15. Record Provisioning Job
        var resultMeta = JsonSerializer.Serialize(new
        {
            tenantCode,
            displayName = request.DisplayName,
            companyId = companyId.ToString(),
            companyCode,
            branchId = branchId.ToString(),
            branchCode,
            subscriptionId = subscriptionId.ToString(),
            planCode,
            adminUserId = adminUserId.ToString(),
            adminEmail
        });

        await using (var insJob = new NpgsqlCommand(
            """
            INSERT INTO platform.tenant_provisioning_jobs
                (tenant_id, idempotency_key, job_type, status, payload, result_metadata)
            VALUES
                (@tId, @key, 'create_tenant', 'succeeded', @payload::jsonb, @meta::jsonb)
            """, conn, tx))
        {
            insJob.Parameters.AddWithValue("tId", tenantId);
            insJob.Parameters.AddWithValue("key", idempotencyKey);
            insJob.Parameters.AddWithValue("payload", JsonSerializer.Serialize(request));
            insJob.Parameters.AddWithValue("meta", resultMeta);
            await insJob.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return new TenantProvisioningResult(
            tenantId,
            tenantCode,
            request.DisplayName!,
            companyId,
            companyCode,
            branchId,
            branchCode,
            subscriptionId,
            planCode,
            adminUserId,
            adminEmail,
            "active",
            now);
    }

    public async Task<TenantDetailDto?> GetTenantDetailAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(tenantId, cancellationToken);

        // Load tenant
        const string sql = """
            SELECT t.id, t.tenant_code, t.display_name, t.legal_name, t.status,
                   t.default_country_code, t.default_currency_code, t.default_timezone,
                   t.created_at, t.suspended_at, t.suspension_reason
            FROM platform.tenants t
            WHERE t.id = @tenantId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var tId = reader.GetGuid(0);
        var tCode = reader.GetString(1);
        var disp = reader.GetString(2);
        var legal = reader.IsDBNull(3) ? null : reader.GetString(3);
        var status = reader.GetString(4);
        var country = reader.GetString(5);
        var curr = reader.GetString(6);
        var tz = reader.GetString(7);
        var created = reader.GetFieldValue<DateTimeOffset>(8);
        var suspendedAt = reader.IsDBNull(9) ? null : (DateTimeOffset?)reader.GetFieldValue<DateTimeOffset>(9);
        var suspReason = reader.IsDBNull(10) ? null : reader.GetString(10);
        await reader.CloseAsync();

        // Company
        var company = await GetFirstCompanyBriefAsync(conn, tenantId, cancellationToken);

        // Default Branch
        var branch = await GetFirstBranchBriefAsync(conn, tenantId, cancellationToken);

        // Subscription
        var sub = await GetSubscriptionByTenantIdAsync(tenantId, cancellationToken);

        // Entitlements
        var entitlements = await GetTenantEntitlementsAsync(tenantId, cancellationToken);

        // Admin User
        var admin = await GetFirstAdminBriefAsync(conn, tenantId, cancellationToken);

        return new TenantDetailDto(
            tId, tCode, disp, legal, status, country, curr, tz, created, suspendedAt, suspReason,
            company, branch, sub!, entitlements, admin);
    }

    public async Task<TenantDetailDto?> GetTenantByCodeAsync(string tenantCode, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(null, cancellationToken);
        await using var cmd = new NpgsqlCommand("SELECT id FROM platform.tenants WHERE tenant_code = @c", conn);
        cmd.Parameters.AddWithValue("c", tenantCode.Trim().ToLowerInvariant());
        var res = await cmd.ExecuteScalarAsync(cancellationToken);
        if (res is null or DBNull) return null;

        return await GetTenantDetailAsync((Guid)res, cancellationToken);
    }

    public async Task<IReadOnlyList<TenantDetailDto>> ListTenantsAsync(string? status, int limit, int offset, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(null, cancellationToken);

        var sql = "SELECT id FROM platform.tenants";
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " WHERE status = @s";
        }
        sql += " ORDER BY created_at DESC LIMIT @lim OFFSET @off;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        if (!string.IsNullOrWhiteSpace(status))
        {
            cmd.Parameters.AddWithValue("s", status.ToLowerInvariant().Trim());
        }
        cmd.Parameters.AddWithValue("lim", limit <= 0 ? 50 : limit);
        cmd.Parameters.AddWithValue("off", Math.Max(0, offset));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }
        await reader.CloseAsync();

        var list = new List<TenantDetailDto>();
        foreach (var id in ids)
        {
            var detail = await GetTenantDetailAsync(id, cancellationToken);
            if (detail != null) list.Add(detail);
        }

        return list;
    }

    public async Task<bool> UpdateTenantLifecycleAsync(Guid tenantId, string newStatus, string? reason, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(null, cancellationToken);

        var sql = """
            UPDATE platform.tenants
            SET status = @status,
                suspended_at = CASE WHEN @status = 'suspended' THEN now() ELSE NULL END,
                suspension_reason = CASE WHEN @status = 'suspended' THEN @reason ELSE NULL END,
                archived_at = CASE WHEN @status = 'archived' THEN now() ELSE archived_at END,
                updated_at = now()
            WHERE id = @tenantId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("status", newStatus.ToLowerInvariant().Trim());
        cmd.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    // ── Entitlements & Metering ─────────────────────────────────────────────

    public async Task<IReadOnlyList<TenantEntitlementDto>> GetTenantEntitlementsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(tenantId, cancellationToken);

        const string sql = """
            SELECT feature_code, enabled, quantity_limit, NULL::text AS limit_unit, source_type, effective_from, effective_until
            FROM platform.tenant_entitlements
            WHERE tenant_id = @tId
            ORDER BY feature_code;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tId", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var list = new List<TenantEntitlementDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new TenantEntitlementDto(
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return list;
    }

    public async Task<TenantEntitlementDto?> GetEntitlementByFeatureAsync(Guid tenantId, string featureCode, CancellationToken cancellationToken)
    {
        var entitlements = await GetTenantEntitlementsAsync(tenantId, cancellationToken);
        var exact = entitlements.FirstOrDefault(e => string.Equals(e.FeatureCode, featureCode, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        return entitlements.FirstOrDefault(e =>
        {
            var cleanE = e.FeatureCode.StartsWith("max_") ? e.FeatureCode[4..] : e.FeatureCode;
            var cleanF = featureCode.StartsWith("max_") ? featureCode[4..] : featureCode;
            if (string.Equals(cleanE, cleanF, StringComparison.OrdinalIgnoreCase)) return true;
            if (cleanE.Contains("transaction") && cleanF.Contains("transaction")) return true;
            if (cleanE.Contains("storage") && cleanF.Contains("storage")) return true;
            if (cleanE.Contains("api_call") && cleanF.Contains("api_call")) return true;
            if (cleanE.Contains("compan") && cleanF.Contains("compan")) return true;
            if (cleanE.Contains("user") && cleanF.Contains("user")) return true;
            if (cleanE.Contains("branch") && cleanF.Contains("branch")) return true;
            if (cleanE.Contains("location") && cleanF.Contains("location")) return true;
            if (cleanE.Contains("integration") && cleanF.Contains("integration")) return true;
            return false;
        });
    }

    public async Task RecordUsageAsync(RecordUsageRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(request.TenantId, cancellationToken);

        const string sql = """
            INSERT INTO platform.usage_records
                (id, tenant_id, metric_code, quantity, unit, period_start, period_end, source_event_id, recorded_at)
            VALUES
                (gen_random_uuid(), @tenantId, @metric, @qty, @unit, @start, @end, @event, now());
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", request.TenantId);
        cmd.Parameters.AddWithValue("metric", request.MetricCode);
        cmd.Parameters.AddWithValue("qty", request.Quantity);
        cmd.Parameters.AddWithValue("unit", request.Unit);
        cmd.Parameters.AddWithValue("start", request.PeriodStart);
        cmd.Parameters.AddWithValue("end", request.PeriodEnd);
        cmd.Parameters.AddWithValue("event", (object?)request.SourceEventId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UsageAggregationDto>> GetUsageAggregationsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(tenantId, cancellationToken);

        const string sql = """
            SELECT id, tenant_id, metric_code, period_start, period_end, current_quantity,
                   quantity_limit, is_exceeded, warning_threshold_pct, warning_issued, last_aggregated_at
            FROM platform.tenant_usage_aggregations
            WHERE tenant_id = @tId
            ORDER BY metric_code;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tId", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var list = new List<UsageAggregationDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new UsageAggregationDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.GetBoolean(7),
                reader.GetInt32(8),
                reader.GetBoolean(9),
                reader.GetFieldValue<DateTimeOffset>(10)));
        }

        return list;
    }

    public async Task<UsageAggregationDto> AggregateUsageMetricAsync(
        Guid tenantId,
        string metricCode,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(tenantId, cancellationToken);

        // Sum usage records
        const string sumSql = """
            SELECT COALESCE(SUM(quantity), 0)
            FROM platform.usage_records
            WHERE tenant_id = @tId AND metric_code = @metric AND period_start >= @start AND period_end <= @end;
            """;

        await using var sumCmd = new NpgsqlCommand(sumSql, conn);
        sumCmd.Parameters.AddWithValue("tId", tenantId);
        sumCmd.Parameters.AddWithValue("metric", metricCode);
        sumCmd.Parameters.AddWithValue("start", periodStart);
        sumCmd.Parameters.AddWithValue("end", periodEnd);

        var totalQty = Convert.ToDecimal(await sumCmd.ExecuteScalarAsync(cancellationToken) ?? 0m);

        // Upsert into aggregations
        const string upsertSql = """
            INSERT INTO platform.tenant_usage_aggregations
                (id, tenant_id, metric_code, period_start, period_end, current_quantity, last_aggregated_at)
            VALUES
                (gen_random_uuid(), @tId, @metric, @start, @end, @qty, now())
            ON CONFLICT (tenant_id, metric_code, period_start, period_end)
            DO UPDATE SET current_quantity = EXCLUDED.current_quantity, last_aggregated_at = now()
            RETURNING id, quantity_limit, is_exceeded, warning_threshold_pct, warning_issued, last_aggregated_at;
            """;

        await using var upCmd = new NpgsqlCommand(upsertSql, conn);
        upCmd.Parameters.AddWithValue("tId", tenantId);
        upCmd.Parameters.AddWithValue("metric", metricCode);
        upCmd.Parameters.AddWithValue("start", periodStart);
        upCmd.Parameters.AddWithValue("end", periodEnd);
        upCmd.Parameters.AddWithValue("qty", totalQty);

        await using var reader = await upCmd.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new UsageAggregationDto(
            reader.GetGuid(0),
            tenantId,
            metricCode,
            periodStart,
            periodEnd,
            totalQty,
            reader.IsDBNull(1) ? null : reader.GetDecimal(1),
            reader.GetBoolean(2),
            reader.GetInt32(3),
            reader.GetBoolean(4),
            reader.GetFieldValue<DateTimeOffset>(5));
    }

    // ── Subscriptions ───────────────────────────────────────────────────────

    public async Task<SubscriptionDetailDto?> GetSubscriptionByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(tenantId, cancellationToken);

        const string sql = """
            SELECT s.id, s.tenant_id, s.plan_id, p.plan_code, p.name, s.status,
                   s.billing_interval, s.currency_code, s.current_price, s.overage_policy, s.overage_rate,
                   s.starts_at, s.current_period_start, s.current_period_end, s.trial_ends_at, s.grace_ends_at,
                   s.cancel_at_period_end, s.cancelled_at
            FROM platform.subscriptions s
            JOIN platform.subscription_plans p ON s.plan_id = p.id
            WHERE s.tenant_id = @tId
            ORDER BY s.created_at DESC
            LIMIT 1;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tId", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SubscriptionDetailDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetDecimal(8),
            reader.GetString(9),
            reader.GetDecimal(10),
            reader.GetFieldValue<DateTimeOffset>(11),
            reader.GetFieldValue<DateTimeOffset>(12),
            reader.GetFieldValue<DateTimeOffset>(13),
            reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
            reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
            reader.GetBoolean(16),
            reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17));
    }

    public async Task<UpgradeSubscriptionResult> UpgradeSubscriptionAsync(
        Guid tenantId,
        string targetPlanCode,
        string? billingInterval,
        decimal prorationCredit,
        decimal newCharge,
        decimal netPayable,
        CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(tenantId, cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        // Resolve target plan
        Guid targetPlanId;
        string oldPlanCode;
        Guid subId;
        await using (var getSubCmd = new NpgsqlCommand(
            """
            SELECT s.id, p.plan_code, tp.id
            FROM platform.subscriptions s
            JOIN platform.subscription_plans p ON s.plan_id = p.id
            JOIN platform.subscription_plans tp ON tp.plan_code = @targetCode
            WHERE s.tenant_id = @tId
            LIMIT 1;
            """, conn, tx))
        {
            getSubCmd.Parameters.AddWithValue("targetCode", targetPlanCode.ToLowerInvariant().Trim());
            getSubCmd.Parameters.AddWithValue("tId", tenantId);
            await using var reader = await getSubCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"Cannot upgrade: current subscription or target plan '{targetPlanCode}' not found.");
            }
            subId = reader.GetGuid(0);
            oldPlanCode = reader.GetString(1);
            targetPlanId = reader.GetGuid(2);
            await reader.CloseAsync();
        }

        // Update subscription
        var interval = billingInterval ?? "month";
        await using (var updateSub = new NpgsqlCommand(
            """
            UPDATE platform.subscriptions
            SET plan_id = @targetPlanId,
                billing_interval = @interval,
                current_price = @newPrice,
                proration_credit = @credit,
                updated_at = now()
            WHERE id = @subId;
            """, conn, tx))
        {
            updateSub.Parameters.AddWithValue("targetPlanId", targetPlanId);
            updateSub.Parameters.AddWithValue("interval", interval);
            updateSub.Parameters.AddWithValue("newPrice", newCharge);
            updateSub.Parameters.AddWithValue("credit", prorationCredit);
            updateSub.Parameters.AddWithValue("subId", subId);
            await updateSub.ExecuteNonQueryAsync(cancellationToken);
        }

        // Snapshot new entitlements from plan_features
        await using (var refreshEnt = new NpgsqlCommand(
            """
            DELETE FROM platform.tenant_entitlements WHERE tenant_id = @tId;

            INSERT INTO platform.tenant_entitlements
                (tenant_id, feature_code, enabled, quantity_limit, source_type, source_id, effective_from)
            SELECT
                @tId, feature_code, enabled, quantity_limit, 'plan', @targetPlanId, now()
            FROM platform.plan_features
            WHERE plan_id = @targetPlanId;
            """, conn, tx))
        {
            refreshEnt.Parameters.AddWithValue("tId", tenantId);
            refreshEnt.Parameters.AddWithValue("targetPlanId", targetPlanId);
            await refreshEnt.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return new UpgradeSubscriptionResult(
            subId,
            oldPlanCode,
            targetPlanCode,
            prorationCredit,
            newCharge,
            netPayable,
            null,
            "succeeded");
    }

    public async Task<bool> UpdateSubscriptionLifecycleAsync(
        Guid tenantId,
        string status,
        DateTimeOffset? graceEndsAt,
        CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(null, cancellationToken);

        const string sql = """
            UPDATE platform.subscriptions
            SET status = @status,
                grace_ends_at = @grace,
                updated_at = now()
            WHERE tenant_id = @tId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("status", status.ToLowerInvariant().Trim());
        cmd.Parameters.AddWithValue("grace", (object?)graceEndsAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("tId", tenantId);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    // ── Invoices & Billing ──────────────────────────────────────────────────

    public async Task<SubscriptionInvoiceDto> CreateInvoiceAsync(SubscriptionInvoiceDto invoice, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(invoice.TenantId, cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        const string sql = """
            INSERT INTO platform.subscription_invoices
                (id, tenant_id, subscription_id, invoice_number, billing_reason, period_start, period_end,
                 subtotal, discount_amount, tax_amount, total_amount, paid_amount, currency_code, status,
                 due_date, paid_at, hosted_invoice_url, pdf_download_url)
            VALUES
                (@id, @tenantId, @subId, @num, @reason, @start, @end,
                 @subtotal, @discount, @tax, @total, @paid, @curr, @status,
                 @due, @paidAt, @hosted, @pdf);
            """;

        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", invoice.Id);
            cmd.Parameters.AddWithValue("tenantId", invoice.TenantId);
            cmd.Parameters.AddWithValue("subId", invoice.SubscriptionId);
            cmd.Parameters.AddWithValue("num", invoice.InvoiceNumber);
            cmd.Parameters.AddWithValue("reason", invoice.BillingReason);
            cmd.Parameters.AddWithValue("start", invoice.PeriodStart);
            cmd.Parameters.AddWithValue("end", invoice.PeriodEnd);
            cmd.Parameters.AddWithValue("subtotal", invoice.Subtotal);
            cmd.Parameters.AddWithValue("discount", invoice.DiscountAmount);
            cmd.Parameters.AddWithValue("tax", invoice.TaxAmount);
            cmd.Parameters.AddWithValue("total", invoice.TotalAmount);
            cmd.Parameters.AddWithValue("paid", invoice.PaidAmount);
            cmd.Parameters.AddWithValue("curr", invoice.CurrencyCode);
            cmd.Parameters.AddWithValue("status", invoice.Status);
            cmd.Parameters.AddWithValue("due", invoice.DueDate);
            cmd.Parameters.AddWithValue("paidAt", (object?)invoice.PaidAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("hosted", (object?)invoice.HostedInvoiceUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("pdf", (object?)invoice.PdfDownloadUrl ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var line in invoice.Lines)
        {
            const string lineSql = """
                INSERT INTO platform.subscription_invoice_lines
                    (id, invoice_id, line_number, description, line_type, quantity, unit_price, amount)
                VALUES
                    (@id, @invId, @lineNum, @desc, @type, @qty, @price, @amount);
                """;

            await using var lineCmd = new NpgsqlCommand(lineSql, conn, tx);
            lineCmd.Parameters.AddWithValue("id", line.Id);
            lineCmd.Parameters.AddWithValue("invId", invoice.Id);
            lineCmd.Parameters.AddWithValue("lineNum", line.LineNumber);
            lineCmd.Parameters.AddWithValue("desc", line.Description);
            lineCmd.Parameters.AddWithValue("type", line.LineType);
            lineCmd.Parameters.AddWithValue("qty", line.Quantity);
            lineCmd.Parameters.AddWithValue("price", line.UnitPrice);
            lineCmd.Parameters.AddWithValue("amount", line.Amount);
            await lineCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return invoice;
    }

    public async Task<IReadOnlyList<SubscriptionInvoiceDto>> GetInvoicesByTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(tenantId, cancellationToken);

        const string sql = """
            SELECT id, tenant_id, subscription_id, invoice_number, billing_reason, period_start, period_end,
                   subtotal, discount_amount, tax_amount, total_amount, paid_amount, currency_code, status,
                   due_date, paid_at, hosted_invoice_url, pdf_download_url
            FROM platform.subscription_invoices
            WHERE tenant_id = @tId
            ORDER BY created_at DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tId", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var list = new List<SubscriptionInvoiceDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SubscriptionInvoiceDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetDecimal(11),
                reader.GetString(12),
                reader.GetString(13),
                reader.GetFieldValue<DateTimeOffset>(14),
                reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                new List<SubscriptionInvoiceLineDto>()));
        }

        return list;
    }

    public async Task<SubscriptionInvoiceDto?> GetInvoiceByIdAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(null, cancellationToken);

        const string sql = """
            SELECT id, tenant_id, subscription_id, invoice_number, billing_reason, period_start, period_end,
                   subtotal, discount_amount, tax_amount, total_amount, paid_amount, currency_code, status,
                   due_date, paid_at, hosted_invoice_url, pdf_download_url
            FROM platform.subscription_invoices
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", invoiceId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var inv = new SubscriptionInvoiceDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            reader.GetDecimal(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetFieldValue<DateTimeOffset>(14),
            reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            new List<SubscriptionInvoiceLineDto>());

        await reader.CloseAsync();

        // Load lines
        const string linesSql = """
            SELECT id, line_number, description, line_type, quantity, unit_price, amount
            FROM platform.subscription_invoice_lines
            WHERE invoice_id = @id
            ORDER BY line_number;
            """;

        await using var linesCmd = new NpgsqlCommand(linesSql, conn);
        linesCmd.Parameters.AddWithValue("id", invoiceId);
        await using var linesReader = await linesCmd.ExecuteReaderAsync(cancellationToken);

        var lines = new List<SubscriptionInvoiceLineDto>();
        while (await linesReader.ReadAsync(cancellationToken))
        {
            lines.Add(new SubscriptionInvoiceLineDto(
                linesReader.GetGuid(0),
                linesReader.GetInt32(1),
                linesReader.GetString(2),
                linesReader.GetString(3),
                linesReader.GetDecimal(4),
                linesReader.GetDecimal(5),
                linesReader.GetDecimal(6)));
        }

        return inv with { Lines = lines };
    }

    public async Task<SubscriptionPaymentDto> RecordPaymentAsync(SubscriptionPaymentDto payment, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(payment.TenantId, cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        // Idempotency check
        await using (var checkCmd = new NpgsqlCommand(
            "SELECT id, status FROM platform.subscription_payments WHERE idempotency_key = @key", conn, tx))
        {
            checkCmd.Parameters.AddWithValue("key", payment.IdempotencyKey);
            await using var chkReader = await checkCmd.ExecuteReaderAsync(cancellationToken);
            if (await chkReader.ReadAsync(cancellationToken))
            {
                var existingId = chkReader.GetGuid(0);
                var existingStatus = chkReader.GetString(1);
                await chkReader.CloseAsync();
                await tx.CommitAsync(cancellationToken);
                return payment with { Id = existingId, Status = existingStatus };
            }
            await chkReader.CloseAsync();
        }

        const string insSql = """
            INSERT INTO platform.subscription_payments
                (id, tenant_id, invoice_id, payment_reference, provider_type, amount, currency_code, status, failure_reason, idempotency_key, processed_at)
            VALUES
                (@id, @tenantId, @invId, @ref, @provider, @amount, @curr, @status, @reason, @key, @proc);
            """;

        await using (var insCmd = new NpgsqlCommand(insSql, conn, tx))
        {
            insCmd.Parameters.AddWithValue("id", payment.Id);
            insCmd.Parameters.AddWithValue("tenantId", payment.TenantId);
            insCmd.Parameters.AddWithValue("invId", (object?)payment.InvoiceId ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("ref", payment.PaymentReference);
            insCmd.Parameters.AddWithValue("provider", payment.ProviderType);
            insCmd.Parameters.AddWithValue("amount", payment.Amount);
            insCmd.Parameters.AddWithValue("curr", payment.CurrencyCode);
            insCmd.Parameters.AddWithValue("status", payment.Status);
            insCmd.Parameters.AddWithValue("reason", (object?)payment.FailureReason ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("key", payment.IdempotencyKey);
            insCmd.Parameters.AddWithValue("proc", payment.ProcessedAt);
            await insCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // If succeeded and linked to invoice, mark invoice as paid
        if (payment.Status == "succeeded" && payment.InvoiceId.HasValue)
        {
            const string updInvSql = """
                UPDATE platform.subscription_invoices
                SET status = 'paid', paid_amount = paid_amount + @amount, paid_at = now(), updated_at = now()
                WHERE id = @invId;
                """;

            await using var updCmd = new NpgsqlCommand(updInvSql, conn, tx);
            updCmd.Parameters.AddWithValue("amount", payment.Amount);
            updCmd.Parameters.AddWithValue("invId", payment.InvoiceId.Value);
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return payment;
    }

    public async Task<SubscriptionCreditNoteDto> IssueCreditNoteAsync(SubscriptionCreditNoteDto creditNote, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(creditNote.TenantId, cancellationToken);

        const string sql = """
            INSERT INTO platform.subscription_credit_notes
                (id, tenant_id, invoice_id, credit_note_number, amount, reason, status, created_at)
            VALUES
                (@id, @tenantId, @invId, @num, @amount, @reason, @status, @created);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", creditNote.Id);
        cmd.Parameters.AddWithValue("tenantId", creditNote.TenantId);
        cmd.Parameters.AddWithValue("invId", creditNote.InvoiceId);
        cmd.Parameters.AddWithValue("num", creditNote.CreditNoteNumber);
        cmd.Parameters.AddWithValue("amount", creditNote.Amount);
        cmd.Parameters.AddWithValue("reason", creditNote.Reason);
        cmd.Parameters.AddWithValue("status", creditNote.Status);
        cmd.Parameters.AddWithValue("created", creditNote.CreatedAt);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return creditNote;
    }

    // ── Support Access & Alerts ─────────────────────────────────────────────

    public async Task<SupportAccessGrantDto> GrantSupportAccessAsync(SupportAccessGrantRequest request, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(tenantId, cancellationToken);

        var grantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(Math.Max(1, request.DurationHours));

        const string sql = """
            INSERT INTO platform.support_access_grants
                (id, tenant_id, support_user_id, authorized_by, reason, granted_at, expires_at, status)
            VALUES
                (@id, @tId, @supId, @authBy, @reason, @granted, @expires, 'active');
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", grantId);
        cmd.Parameters.AddWithValue("tId", tenantId);
        cmd.Parameters.AddWithValue("supId", request.SupportUserId);
        cmd.Parameters.AddWithValue("authBy", request.AuthorizedBy);
        cmd.Parameters.AddWithValue("reason", request.Reason);
        cmd.Parameters.AddWithValue("granted", now);
        cmd.Parameters.AddWithValue("expires", expiresAt);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        return new SupportAccessGrantDto(
            grantId, tenantId, request.SupportUserId, request.AuthorizedBy, request.Reason, now, expiresAt, null, "active");
    }

    public async Task<bool> RevokeSupportAccessAsync(Guid grantId, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(null, cancellationToken);

        const string sql = """
            UPDATE platform.support_access_grants
            SET status = 'revoked', revoked_at = now()
            WHERE id = @id AND status = 'active';
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", grantId);
        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<IReadOnlyList<SupportAccessGrantDto>> ListSupportAccessGrantsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(tenantId, cancellationToken);

        const string sql = """
            SELECT id, tenant_id, support_user_id, authorized_by, reason, granted_at, expires_at, revoked_at, status
            FROM platform.support_access_grants
            WHERE tenant_id = @tId
            ORDER BY granted_at DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tId", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var list = new List<SupportAccessGrantDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SupportAccessGrantDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetString(8)));
        }

        return list;
    }

    public async Task CreatePlatformAlertAsync(SaaSPlatformAlertDto alert, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(alert.TenantId, cancellationToken);

        const string sql = """
            INSERT INTO platform.saas_platform_alerts
                (id, tenant_id, alert_type, severity, title, message, is_acknowledged, created_at)
            VALUES
                (@id, @tId, @type, @sev, @title, @msg, @ack, @created);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", alert.Id);
        cmd.Parameters.AddWithValue("tId", (object?)alert.TenantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("type", alert.AlertType);
        cmd.Parameters.AddWithValue("sev", alert.Severity);
        cmd.Parameters.AddWithValue("title", alert.Title);
        cmd.Parameters.AddWithValue("msg", alert.Message);
        cmd.Parameters.AddWithValue("ack", alert.IsAcknowledged);
        cmd.Parameters.AddWithValue("created", alert.CreatedAt);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SaaSPlatformAlertDto>> ListPlatformAlertsAsync(Guid? tenantId, bool unacknowledgedOnly, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(tenantId, cancellationToken);

        var sql = "SELECT id, tenant_id, alert_type, severity, title, message, is_acknowledged, created_at FROM platform.saas_platform_alerts WHERE 1=1";
        if (tenantId.HasValue)
        {
            sql += " AND tenant_id = @tId";
        }
        if (unacknowledgedOnly)
        {
            sql += " AND is_acknowledged = false";
        }
        sql += " ORDER BY created_at DESC LIMIT 100;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        if (tenantId.HasValue)
        {
            cmd.Parameters.AddWithValue("tId", tenantId.Value);
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<SaaSPlatformAlertDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SaaSPlatformAlertDto(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return list;
    }

    // ── Helper lookups ──────────────────────────────────────────────────────

    private static async Task<CompanyBriefDto> GetFirstCompanyBriefAsync(NpgsqlConnection conn, Guid tenantId, CancellationToken ct)
    {
        const string sql = "SELECT id, company_code, name, country_code, base_currency_code, status FROM organization.companies WHERE tenant_id = @tId LIMIT 1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tId", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new CompanyBriefDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5));
        }

        return new CompanyBriefDto(Guid.Empty, "DEFAULT", "Default Company", "PK", "PKR", "active");
    }

    private static async Task<BranchBriefDto> GetFirstBranchBriefAsync(NpgsqlConnection conn, Guid tenantId, CancellationToken ct)
    {
        const string sql = "SELECT id, branch_code, branch_name, branch_type, is_active FROM organization.branches WHERE tenant_id = @tId LIMIT 1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tId", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new BranchBriefDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4));
        }

        return new BranchBriefDto(Guid.Empty, "HQ", "Headquarters", "headquarters", true);
    }

    private static async Task<AdminUserBriefDto> GetFirstAdminBriefAsync(NpgsqlConnection conn, Guid tenantId, CancellationToken ct)
    {
        const string sql = """
            SELECT u.id, u.email_normalized, u.display_name, u.status
            FROM identity.users u
            JOIN identity.tenant_memberships tm ON u.id = tm.user_id
            WHERE tm.tenant_id = @tId
            LIMIT 1;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tId", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new AdminUserBriefDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3));
        }

        return new AdminUserBriefDto(Guid.Empty, "admin@example.com", "Administrator", "active");
    }
}
