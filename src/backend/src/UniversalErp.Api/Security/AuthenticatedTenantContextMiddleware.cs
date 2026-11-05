using System.Security.Claims;
using Npgsql;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Api.Security;

public sealed class AuthenticatedTenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        RequestTenantContextAccessor tenantContextAccessor,
        NpgsqlDataSource dataSource)
    {
        if (context.Request.Path.Equals("/api/v1/contexts", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/api/v1/tenants/register") ||
            context.Request.Path.StartsWithSegments("/api/v1/plans") ||
            !context.Request.Path.StartsWithSegments("/api/v1") ||
            context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var issuer = context.User.FindFirstValue("erp:issuer");
        var subject = context.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { code = "OIDC_SUBJECT_REQUIRED" });
            return;
        }

        if (!Guid.TryParse(context.Request.Headers["X-Tenant-Id"], out var tenantId) || tenantId == Guid.Empty ||
            !Guid.TryParse(context.Request.Headers["X-Company-Id"], out var companyId) || companyId == Guid.Empty)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { code = "TENANT_AND_COMPANY_CONTEXT_REQUIRED" });
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(context.RequestAborted);
        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        await using (var setScope = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenantId, true), set_config('app.company_id', @companyId, true)",
            connection,
            transaction))
        {
            setScope.Parameters.AddWithValue("tenantId", tenantId.ToString());
            setScope.Parameters.AddWithValue("companyId", companyId.ToString());
            await setScope.ExecuteNonQueryAsync(context.RequestAborted);
        }

        Guid? userId;
        await using (var resolveUser = new NpgsqlCommand(
            """
            SELECT u.id
            FROM identity.external_identities AS external_identity
            JOIN identity.users AS u ON u.id = external_identity.user_id
            JOIN identity.tenant_memberships AS tenant_membership
              ON tenant_membership.tenant_id = @tenantId
             AND tenant_membership.user_id = u.id
            JOIN identity.company_memberships AS company_membership
              ON company_membership.tenant_id = @tenantId
             AND company_membership.company_id = @companyId
             AND company_membership.user_id = u.id
            JOIN platform.tenants AS tenant ON tenant.id = @tenantId
            JOIN organization.companies AS company
              ON company.tenant_id = @tenantId AND company.id = @companyId
            WHERE external_identity.issuer = @issuer
              AND external_identity.subject = @subject
              AND u.status = 'active'
              AND tenant_membership.status = 'active'
              AND company_membership.status = 'active'
              AND tenant.status = 'active'
              AND company.status = 'active'
            LIMIT 1
            """,
            connection,
            transaction))
        {
            resolveUser.Parameters.AddWithValue("tenantId", tenantId);
            resolveUser.Parameters.AddWithValue("companyId", companyId);
            resolveUser.Parameters.AddWithValue("issuer", issuer);
            resolveUser.Parameters.AddWithValue("subject", subject);
            var result = await resolveUser.ExecuteScalarAsync(context.RequestAborted);
            userId = result is Guid resolvedUserId ? resolvedUserId : null;
        }

        await transaction.CommitAsync(context.RequestAborted);
        if (userId is null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { code = "ACTIVE_COMPANY_MEMBERSHIP_REQUIRED" });
            return;
        }

        tenantContextAccessor.SetCurrent(new TenantContext(tenantId, companyId, userId.Value));
        await next(context);
    }
}
