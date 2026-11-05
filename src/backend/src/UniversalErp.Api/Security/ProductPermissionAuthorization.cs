using Microsoft.AspNetCore.Authorization;
using Npgsql;
using UniversalErp.Application.Abstractions;

namespace UniversalErp.Api.Security;

public sealed record DatabasePermissionRequirement(string PermissionCode) : IAuthorizationRequirement;

public sealed class DatabasePermissionAuthorizationHandler(
    NpgsqlDataSource dataSource,
    ITenantContextAccessor tenantContextAccessor)
    : AuthorizationHandler<DatabasePermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DatabasePermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var tenantContext = tenantContextAccessor.Current;
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var setScope = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenantId, true), set_config('app.company_id', @companyId, true)",
            connection,
            transaction))
        {
            setScope.Parameters.AddWithValue("tenantId", tenantContext.TenantId.ToString());
            setScope.Parameters.AddWithValue("companyId", tenantContext.CompanyId.ToString());
            await setScope.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
              SELECT 1
              FROM identity.company_memberships AS membership
              JOIN "authorization".membership_role_assignments AS assignment
                ON assignment.tenant_id = membership.tenant_id
               AND assignment.membership_id = membership.id
              JOIN "authorization".role_permissions AS permission
                ON permission.tenant_id = assignment.tenant_id
               AND permission.role_id = assignment.role_id
              WHERE membership.tenant_id = @tenantId
                AND membership.company_id = @companyId
                AND membership.user_id = @userId
                AND membership.status = 'active'
                AND assignment.effective_from <= now()
                AND (assignment.effective_until IS NULL OR assignment.effective_until > now())
                AND permission.permission_code = @permissionCode
                AND permission.scope_type IN ('tenant','company')
            )
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantContext.TenantId);
        command.Parameters.AddWithValue("companyId", tenantContext.CompanyId);
        command.Parameters.AddWithValue("userId", tenantContext.UserId);
        command.Parameters.AddWithValue("permissionCode", requirement.PermissionCode);
        var isAllowed = await command.ExecuteScalarAsync() as bool? ?? false;
        await transaction.CommitAsync();

        if (isAllowed)
        {
            context.Succeed(requirement);
        }
    }
}
