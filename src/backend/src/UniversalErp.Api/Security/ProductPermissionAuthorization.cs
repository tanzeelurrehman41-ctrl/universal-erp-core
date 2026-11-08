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
            "SELECT identity.user_has_permission(@tenantId, @companyId, @userId, @permissionCode)",
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
