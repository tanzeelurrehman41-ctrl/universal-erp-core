using System.Text.Json;
using Npgsql;
using UniversalErp.Application.Tenancy;

namespace UniversalErp.Infrastructure.Tenancy;

public sealed class PostgresTenantOnboardingRepository(NpgsqlDataSource dataSource)
    : ITenantOnboardingRepository
{
    public async Task<IReadOnlyList<SubscriptionPlanDetail>> ListSubscriptionPlansAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT
                p.id, p.plan_code, p.name, p.description, p.status,
                f.feature_code, f.enabled, f.quantity_limit, f.limit_unit
            FROM platform.subscription_plans p
            LEFT JOIN platform.plan_features f ON p.id = f.plan_id
            ORDER BY p.created_at, f.feature_code;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var planMap = new Dictionary<Guid, (Guid Id, string Code, string Name, string? Desc, string Status, List<TenantEntitlementDetail> Features)>();

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
                    reader.GetString(4),
                    new List<TenantEntitlementDetail>());
                planMap[planId] = entry;
            }

            if (!reader.IsDBNull(5))
            {
                entry.Features.Add(new TenantEntitlementDetail(
                    reader.GetString(5),
                    reader.GetBoolean(6),
                    reader.IsDBNull(7) ? null : reader.GetInt64(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }

        return planMap.Values
            .Select(p => new SubscriptionPlanDetail(p.Id, p.Code, p.Name, p.Desc, p.Status, p.Features))
            .ToList();
    }

    public async Task<SubscriptionPlanDetail?> GetPlanByCodeAsync(
        string planCode,
        CancellationToken cancellationToken)
    {
        var plans = await ListSubscriptionPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.PlanCode, planCode, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<TenantDetail?> GetTenantByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenantId, cancellationToken);

        var tenant = await LoadTenantDetailInternalAsync(connection, transaction, tenantId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return tenant;
    }

    public async Task<TenantDetail?> GetTenantByCodeAsync(
        string tenantCode,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await using var lookupCmd = new NpgsqlCommand(
            "SELECT id FROM platform.tenants WHERE tenant_code = @code",
            connection);
        lookupCmd.Parameters.AddWithValue("code", tenantCode);

        var result = await lookupCmd.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            return null;
        }

        var tenantId = (Guid)result;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenantId, cancellationToken);

        var tenant = await LoadTenantDetailInternalAsync(connection, transaction, tenantId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return tenant;
    }

    public async Task<TenantDetail> RegisterTenantAsync(
        Guid idempotencyKey,
        string requestHash,
        RegisterTenantRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // 1. Check idempotency in platform.tenant_provisioning_jobs
        await using (var checkJobCmd = new NpgsqlCommand(
            "SELECT tenant_id, payload FROM platform.tenant_provisioning_jobs WHERE idempotency_key = @key",
            connection,
            transaction))
        {
            checkJobCmd.Parameters.AddWithValue("key", idempotencyKey);
            await using var jobReader = await checkJobCmd.ExecuteReaderAsync(cancellationToken);
            if (await jobReader.ReadAsync(cancellationToken))
            {
                var existingTenantId = jobReader.GetGuid(0);
                var existingPayload = jobReader.GetString(1);
                await jobReader.CloseAsync();

                using var doc = JsonDocument.Parse(existingPayload);
                var existingHash = doc.RootElement.TryGetProperty("requestHash", out var h) ? h.GetString() : null;
                if (!string.Equals(existingHash, requestHash, StringComparison.Ordinal))
                {
                    throw new TenantIdempotencyConflictException();
                }

                await SetTenantScopeAsync(connection, transaction, existingTenantId, cancellationToken);
                var existing = await LoadTenantDetailInternalAsync(connection, transaction, existingTenantId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return existing ?? throw new InvalidOperationException("Provisioned tenant record could not be found.");
            }
            await jobReader.CloseAsync();
        }

        // 2. Check tenant code collision
        await using (var checkCodeCmd = new NpgsqlCommand(
            "SELECT 1 FROM platform.tenants WHERE tenant_code = @code",
            connection,
            transaction))
        {
            checkCodeCmd.Parameters.AddWithValue("code", request.TenantCode!);
            var exists = await checkCodeCmd.ExecuteScalarAsync(cancellationToken);
            if (exists is not null and not DBNull)
            {
                throw new TenantCodeConflictException();
            }
        }

        // 3. Check user email collision
        await using (var checkEmailCmd = new NpgsqlCommand(
            "SELECT 1 FROM identity.users WHERE email_normalized = @email",
            connection,
            transaction))
        {
            checkEmailCmd.Parameters.AddWithValue("email", request.AdminEmail!);
            var exists = await checkEmailCmd.ExecuteScalarAsync(cancellationToken);
            if (exists is not null and not DBNull)
            {
                throw new UserEmailConflictException();
            }
        }

        // 4. Resolve subscription plan
        Guid planId;
        string planName;
        await using (var planCmd = new NpgsqlCommand(
            "SELECT id, name FROM platform.subscription_plans WHERE plan_code = @planCode AND status = 'active'",
            connection,
            transaction))
        {
            planCmd.Parameters.AddWithValue("planCode", request.PlanCode!);
            await using var planReader = await planCmd.ExecuteReaderAsync(cancellationToken);
            if (!await planReader.ReadAsync(cancellationToken))
            {
                throw new InvalidPlanException(request.PlanCode!);
            }
            planId = planReader.GetGuid(0);
            planName = planReader.GetString(1);
            await planReader.CloseAsync();
        }

        // 5. Generate identifiers
        var tenantId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantMembershipId = Guid.NewGuid();
        var companyMembershipId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        // 6. Insert tenant
        await using (var insertTenant = new NpgsqlCommand(
            """
            INSERT INTO platform.tenants
                (id, tenant_code, display_name, legal_name, status, default_country_code, default_currency_code, default_timezone)
            VALUES
                (@id, @code, @displayName, @legalName, 'active', @country, @currency, @tz)
            """,
            connection,
            transaction))
        {
            insertTenant.Parameters.AddWithValue("id", tenantId);
            insertTenant.Parameters.AddWithValue("code", request.TenantCode!);
            insertTenant.Parameters.AddWithValue("displayName", request.DisplayName!);
            insertTenant.Parameters.AddWithValue("legalName", (object?)request.LegalName ?? DBNull.Value);
            insertTenant.Parameters.AddWithValue("country", request.CountryCode!);
            insertTenant.Parameters.AddWithValue("currency", request.BaseCurrencyCode!);
            insertTenant.Parameters.AddWithValue("tz", request.Timezone!);
            await insertTenant.ExecuteNonQueryAsync(cancellationToken);
        }

        // Set RLS scope to newly created tenant
        await SetTenantScopeAsync(connection, transaction, tenantId, cancellationToken);

        // 7. Insert subscription
        await using (var insertSub = new NpgsqlCommand(
            """
            INSERT INTO platform.subscriptions
                (id, tenant_id, plan_id, status, billing_interval, currency_code, starts_at, current_period_start, current_period_end)
            VALUES
                (@id, @tenantId, @planId, 'active', 'month', @currency, now(), now(), now() + interval '30 days')
            """,
            connection,
            transaction))
        {
            insertSub.Parameters.AddWithValue("id", subscriptionId);
            insertSub.Parameters.AddWithValue("tenantId", tenantId);
            insertSub.Parameters.AddWithValue("planId", planId);
            insertSub.Parameters.AddWithValue("currency", request.BaseCurrencyCode!);
            await insertSub.ExecuteNonQueryAsync(cancellationToken);
        }

        // 8. Snapshot entitlements from plan_features
        await using (var copyEntitlements = new NpgsqlCommand(
            """
            INSERT INTO platform.tenant_entitlements
                (tenant_id, feature_code, enabled, quantity_limit, source_type, source_id, effective_from)
            SELECT
                @tenantId, feature_code, enabled, quantity_limit, 'plan', @planId, now()
            FROM platform.plan_features
            WHERE plan_id = @planId
            """,
            connection,
            transaction))
        {
            copyEntitlements.Parameters.AddWithValue("tenantId", tenantId);
            copyEntitlements.Parameters.AddWithValue("planId", planId);
            await copyEntitlements.ExecuteNonQueryAsync(cancellationToken);
        }

        // 9. Insert initial company
        await using (var insertCompany = new NpgsqlCommand(
            """
            INSERT INTO organization.companies
                (id, tenant_id, company_code, name, legal_name, registration_number, country_code, base_currency_code, timezone, status)
            VALUES
                (@id, @tenantId, @code, @name, @legalName, @regNo, @country, @currency, @tz, 'active')
            """,
            connection,
            transaction))
        {
            insertCompany.Parameters.AddWithValue("id", companyId);
            insertCompany.Parameters.AddWithValue("tenantId", tenantId);
            insertCompany.Parameters.AddWithValue("code", request.CompanyCode!);
            insertCompany.Parameters.AddWithValue("name", request.CompanyName!);
            insertCompany.Parameters.AddWithValue("legalName", (object?)request.LegalName ?? DBNull.Value);
            insertCompany.Parameters.AddWithValue("regNo", (object?)request.RegistrationNumber ?? DBNull.Value);
            insertCompany.Parameters.AddWithValue("country", request.CountryCode!);
            insertCompany.Parameters.AddWithValue("currency", request.BaseCurrencyCode!);
            insertCompany.Parameters.AddWithValue("tz", request.Timezone!);
            await insertCompany.ExecuteNonQueryAsync(cancellationToken);
        }

        // 10. Insert headquarters location
        await using (var insertLocation = new NpgsqlCommand(
            """
            INSERT INTO organization.locations
                (id, tenant_id, company_id, location_code, location_type_code, name, country_code, timezone, status)
            VALUES
                (@id, @tenantId, @companyId, 'HQ', 'warehouse', 'Headquarters', @country, @tz, 'active')
            """,
            connection,
            transaction))
        {
            insertLocation.Parameters.AddWithValue("id", locationId);
            insertLocation.Parameters.AddWithValue("tenantId", tenantId);
            insertLocation.Parameters.AddWithValue("companyId", companyId);
            insertLocation.Parameters.AddWithValue("country", request.CountryCode!);
            insertLocation.Parameters.AddWithValue("tz", request.Timezone!);
            await insertLocation.ExecuteNonQueryAsync(cancellationToken);
        }

        // 11. Insert user
        await using (var insertUser = new NpgsqlCommand(
            """
            INSERT INTO identity.users
                (id, email_normalized, display_name, phone_e164, status)
            VALUES
                (@id, @email, @name, @phone, 'active')
            """,
            connection,
            transaction))
        {
            insertUser.Parameters.AddWithValue("id", userId);
            insertUser.Parameters.AddWithValue("email", request.AdminEmail!);
            insertUser.Parameters.AddWithValue("name", request.AdminFullName!);
            insertUser.Parameters.AddWithValue("phone", (object?)request.AdminPhone ?? DBNull.Value);
            await insertUser.ExecuteNonQueryAsync(cancellationToken);
        }

        // 12. Insert tenant membership
        await using (var insertTenantMember = new NpgsqlCommand(
            """
            INSERT INTO identity.tenant_memberships
                (id, tenant_id, user_id, status, joined_at)
            VALUES
                (@id, @tenantId, @userId, 'active', now())
            """,
            connection,
            transaction))
        {
            insertTenantMember.Parameters.AddWithValue("id", tenantMembershipId);
            insertTenantMember.Parameters.AddWithValue("tenantId", tenantId);
            insertTenantMember.Parameters.AddWithValue("userId", userId);
            await insertTenantMember.ExecuteNonQueryAsync(cancellationToken);
        }

        // 13. Insert company membership
        await using (var insertCompanyMember = new NpgsqlCommand(
            """
            INSERT INTO identity.company_memberships
                (id, tenant_id, company_id, user_id, status)
            VALUES
                (@id, @tenantId, @companyId, @userId, 'active')
            """,
            connection,
            transaction))
        {
            insertCompanyMember.Parameters.AddWithValue("id", companyMembershipId);
            insertCompanyMember.Parameters.AddWithValue("tenantId", tenantId);
            insertCompanyMember.Parameters.AddWithValue("companyId", companyId);
            insertCompanyMember.Parameters.AddWithValue("userId", userId);
            await insertCompanyMember.ExecuteNonQueryAsync(cancellationToken);
        }

        // 14. Insert default tenant_admin role and assignment
        await using (var insertRole = new NpgsqlCommand(
            """
            INSERT INTO "authorization".roles
                (id, tenant_id, role_code, name, is_system_role)
            VALUES
                (@id, @tenantId, 'tenant_admin', 'Tenant Administrator', true)
            ON CONFLICT (tenant_id, role_code) DO NOTHING
            RETURNING id;
            """,
            connection,
            transaction))
        {
            insertRole.Parameters.AddWithValue("id", roleId);
            insertRole.Parameters.AddWithValue("tenantId", tenantId);
            var roleResult = await insertRole.ExecuteScalarAsync(cancellationToken);
            if (roleResult is not null and not DBNull)
            {
                roleId = (Guid)roleResult;
            }
        }

        await using (var assignRole = new NpgsqlCommand(
            """
            INSERT INTO "authorization".membership_role_assignments
                (tenant_id, membership_id, role_id, assigned_by_user_id)
            VALUES
                (@tenantId, @membershipId, @roleId, @userId)
            """,
            connection,
            transaction))
        {
            assignRole.Parameters.AddWithValue("tenantId", tenantId);
            assignRole.Parameters.AddWithValue("membershipId", tenantMembershipId);
            assignRole.Parameters.AddWithValue("roleId", roleId);
            assignRole.Parameters.AddWithValue("userId", userId);
            await assignRole.ExecuteNonQueryAsync(cancellationToken);
        }

        // 15. Record provisioning job
        var payloadJson = JsonSerializer.Serialize(new
        {
            requestHash,
            tenantCode = request.TenantCode,
            displayName = request.DisplayName,
            planCode = request.PlanCode,
            companyCode = request.CompanyCode,
            adminEmail = request.AdminEmail,
        });

        var resultMetadataJson = JsonSerializer.Serialize(new
        {
            tenantId,
            companyId,
            userId,
            subscriptionId,
        });

        await using (var insertJob = new NpgsqlCommand(
            """
            INSERT INTO platform.tenant_provisioning_jobs
                (job_id, tenant_id, idempotency_key, job_type, status, payload, result_metadata)
            VALUES
                (@id, @tenantId, @key, 'create_tenant', 'succeeded', @payload::jsonb, @result::jsonb)
            """,
            connection,
            transaction))
        {
            insertJob.Parameters.AddWithValue("id", jobId);
            insertJob.Parameters.AddWithValue("tenantId", tenantId);
            insertJob.Parameters.AddWithValue("key", idempotencyKey);
            insertJob.Parameters.AddWithValue("payload", payloadJson);
            insertJob.Parameters.AddWithValue("result", resultMetadataJson);
            await insertJob.ExecuteNonQueryAsync(cancellationToken);
        }

        // 16. Record audit event
        await using (var insertAudit = new NpgsqlCommand(
            """
            INSERT INTO audit.audit_events
                (tenant_id, company_id, location_id, actor_user_id, correlation_id, event_code, entity_type, entity_id, occurred_at, metadata)
            VALUES
                (@tenantId, @companyId, @locationId, @userId, @key, 'tenant.provisioned', 'tenant', @tenantId, now(), @meta::jsonb)
            """,
            connection,
            transaction))
        {
            insertAudit.Parameters.AddWithValue("tenantId", tenantId);
            insertAudit.Parameters.AddWithValue("companyId", companyId);
            insertAudit.Parameters.AddWithValue("locationId", locationId);
            insertAudit.Parameters.AddWithValue("userId", userId);
            insertAudit.Parameters.AddWithValue("key", idempotencyKey);
            insertAudit.Parameters.AddWithValue("meta", resultMetadataJson);
            await insertAudit.ExecuteNonQueryAsync(cancellationToken);
        }

        var detail = await LoadTenantDetailInternalAsync(connection, transaction, tenantId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return detail ?? throw new InvalidOperationException("Tenant detail could not be assembled after creation.");
    }

    private static async Task<TenantDetail?> LoadTenantDetailInternalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // 1. Tenant
        await using var tenantCmd = new NpgsqlCommand(
            """
            SELECT id, tenant_code, display_name, legal_name, status,
                   default_country_code, default_currency_code, default_timezone, created_at
            FROM platform.tenants
            WHERE id = @id
            """,
            connection,
            transaction);
        tenantCmd.Parameters.AddWithValue("id", tenantId);

        await using var reader = await tenantCmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.CloseAsync();
            return null;
        }

        var id = reader.GetGuid(0);
        var tenantCode = reader.GetString(1);
        var displayName = reader.GetString(2);
        var legalName = reader.IsDBNull(3) ? null : reader.GetString(3);
        var status = reader.GetString(4);
        var countryCode = reader.GetString(5);
        var currencyCode = reader.GetString(6);
        var timezone = reader.GetString(7);
        var createdAt = reader.GetFieldValue<DateTimeOffset>(8);
        await reader.CloseAsync();

        // 2. Company
        await using var compCmd = new NpgsqlCommand(
            """
            SELECT id, company_code, name, legal_name, registration_number,
                   country_code, base_currency_code, timezone, status
            FROM organization.companies
            WHERE tenant_id = @id
            ORDER BY created_at
            LIMIT 1
            """,
            connection,
            transaction);
        compCmd.Parameters.AddWithValue("id", tenantId);

        InitialCompanyDetail company;
        await using (var compReader = await compCmd.ExecuteReaderAsync(cancellationToken))
        {
            if (!await compReader.ReadAsync(cancellationToken))
            {
                await compReader.CloseAsync();
                return null;
            }
            company = new InitialCompanyDetail(
                compReader.GetGuid(0),
                compReader.GetString(1),
                compReader.GetString(2),
                compReader.IsDBNull(3) ? null : compReader.GetString(3),
                compReader.IsDBNull(4) ? null : compReader.GetString(4),
                compReader.GetString(5),
                compReader.GetString(6),
                compReader.GetString(7),
                compReader.GetString(8));
            await compReader.CloseAsync();
        }

        // 3. Subscription
        await using var subCmd = new NpgsqlCommand(
            """
            SELECT s.id, p.plan_code, p.name, s.status, s.starts_at, s.current_period_end
            FROM platform.subscriptions s
            JOIN platform.subscription_plans p ON s.plan_id = p.id
            WHERE s.tenant_id = @id
            ORDER BY s.created_at DESC
            LIMIT 1
            """,
            connection,
            transaction);
        subCmd.Parameters.AddWithValue("id", tenantId);

        TenantSubscriptionDetail subscription;
        await using (var subReader = await subCmd.ExecuteReaderAsync(cancellationToken))
        {
            if (!await subReader.ReadAsync(cancellationToken))
            {
                await subReader.CloseAsync();
                return null;
            }
            subscription = new TenantSubscriptionDetail(
                subReader.GetGuid(0),
                subReader.GetString(1),
                subReader.GetString(2),
                subReader.GetString(3),
                subReader.GetFieldValue<DateTimeOffset>(4),
                subReader.GetFieldValue<DateTimeOffset>(5));
            await subReader.CloseAsync();
        }

        // 4. Entitlements
        var entitlements = new List<TenantEntitlementDetail>();
        await using (var entCmd = new NpgsqlCommand(
            """
            SELECT te.feature_code, te.enabled, te.quantity_limit, pf.limit_unit
            FROM platform.tenant_entitlements te
            LEFT JOIN platform.plan_features pf
              ON pf.plan_id = te.source_id AND pf.feature_code = te.feature_code
            WHERE te.tenant_id = @id
            ORDER BY te.feature_code
            """,
            connection,
            transaction))
        {
            entCmd.Parameters.AddWithValue("id", tenantId);
            await using var entReader = await entCmd.ExecuteReaderAsync(cancellationToken);
            while (await entReader.ReadAsync(cancellationToken))
            {
                entitlements.Add(new TenantEntitlementDetail(
                    entReader.GetString(0),
                    entReader.GetBoolean(1),
                    entReader.IsDBNull(2) ? null : entReader.GetInt64(2),
                    entReader.IsDBNull(3) ? null : entReader.GetString(3)));
            }
            await entReader.CloseAsync();
        }

        // 5. Admin User
        await using var userCmd = new NpgsqlCommand(
            """
            SELECT u.id, u.email_normalized, u.display_name, u.status
            FROM identity.tenant_memberships tm
            JOIN identity.users u ON tm.user_id = u.id
            WHERE tm.tenant_id = @id
            ORDER BY tm.created_at
            LIMIT 1
            """,
            connection,
            transaction);
        userCmd.Parameters.AddWithValue("id", tenantId);

        TenantAdminUserDetail adminUser;
        await using (var userReader = await userCmd.ExecuteReaderAsync(cancellationToken))
        {
            if (!await userReader.ReadAsync(cancellationToken))
            {
                await userReader.CloseAsync();
                return null;
            }
            adminUser = new TenantAdminUserDetail(
                userReader.GetGuid(0),
                userReader.IsDBNull(1) ? string.Empty : userReader.GetString(1),
                userReader.GetString(2),
                userReader.GetString(3));
            await userReader.CloseAsync();
        }

        return new TenantDetail(
            id,
            tenantCode,
            displayName,
            legalName,
            status,
            countryCode,
            currencyCode,
            timezone,
            createdAt,
            company,
            subscription,
            entitlements,
            adminUser);
    }

    private static async Task SetTenantScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenantId, true)",
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
