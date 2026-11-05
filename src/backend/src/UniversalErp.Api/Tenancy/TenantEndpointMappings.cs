using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UniversalErp.Application.Tenancy;

namespace UniversalErp.Api.Tenancy;

public static class TenantEndpointMappings
{
    public static IEndpointRouteBuilder MapTenantEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool isDevelopment)
    {
        var group = endpoints.MapGroup("/api/v1");

        group.MapGet("/plans", async (
            TenantOnboardingService service,
            CancellationToken cancellationToken) =>
        {
            var plans = await service.ListSubscriptionPlansAsync(cancellationToken);
            return Results.Ok(plans);
        });

        group.MapPost("/tenants/register", async (
            HttpRequest httpRequest,
            RegisterTenantRequest request,
            TenantOnboardingService service,
            CancellationToken cancellationToken) =>
        {
            var keyHeader = httpRequest.Headers["Idempotency-Key"].ToString();
            Guid? idempotencyKey = Guid.TryParse(keyHeader, out var parsedKey)
                ? parsedKey
                : null;

            var result = await service.RegisterTenantAsync(request, idempotencyKey, cancellationToken);
            if (!result.IsSuccess)
            {
                var statusCode = result.Error switch
                {
                    "TENANT_CODE_EXISTS" or "USER_EMAIL_EXISTS" or "IDEMPOTENCY_KEY_REUSED" => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest,
                };

                return Results.Problem(
                    statusCode: statusCode,
                    title: result.Error,
                    detail: result.Error switch
                    {
                        "INVALID_IDEMPOTENCY_KEY" => "An Idempotency-Key header containing a valid UUID is required.",
                        "INVALID_TENANT_CODE" => "Tenant code must be 3-50 lowercase alphanumeric characters or hyphens (e.g. 'apex-pk').",
                        "INVALID_DISPLAY_NAME" => "Tenant display name is required and must be between 2 and 200 characters.",
                        "INVALID_ADMIN_EMAIL" => "A valid administrator email address is required.",
                        "INVALID_ADMIN_NAME" => "Administrator full name is required and must be between 2 and 200 characters.",
                        "INVALID_PLAN_CODE" => "The requested subscription plan code does not exist or is not active.",
                        "TENANT_CODE_EXISTS" => "A tenant with this tenant code is already registered.",
                        "USER_EMAIL_EXISTS" => "A user with this email address is already registered.",
                        "IDEMPOTENCY_KEY_REUSED" => "This Idempotency-Key was already used with different registration details.",
                        _ => "Tenant registration failed. Please review the submitted fields.",
                    });
            }

            var tenant = result.Value!;
            return Results.Created($"/api/v1/tenants/{tenant.TenantId}", tenant);
        });

        group.MapGet("/tenants/{tenantId:guid}", async (
            Guid tenantId,
            TenantOnboardingService service,
            CancellationToken cancellationToken) =>
        {
            var tenant = await service.GetTenantByIdAsync(tenantId, cancellationToken);
            return tenant is null ? Results.NotFound() : Results.Ok(tenant);
        });

        group.MapGet("/tenants/by-code/{tenantCode}", async (
            string tenantCode,
            TenantOnboardingService service,
            CancellationToken cancellationToken) =>
        {
            var tenant = await service.GetTenantByCodeAsync(tenantCode, cancellationToken);
            return tenant is null ? Results.NotFound() : Results.Ok(tenant);
        });

        return endpoints;
    }
}
