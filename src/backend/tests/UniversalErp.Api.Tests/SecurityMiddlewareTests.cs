using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using UniversalErp.Api.Security;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class SecurityMiddlewareTests
{
    [Theory]
    [InlineData("/api/v1/plans")]
    [InlineData("/api/v1/tenants/register")]
    [InlineData("/api/modules")]
    [InlineData("/health")]
    public async Task Middleware_Allows_Public_And_Onboarding_Endpoints_Without_Tenant_Headers(string path)
    {
        var wasCalled = false;
        RequestDelegate next = (ctx) =>
        {
            wasCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new AuthenticatedTenantContextMiddleware(next);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;

        // Unauthenticated request
        await middleware.InvokeAsync(httpContext, null!, null!);

        Assert.True(wasCalled);
        Assert.Equal(200, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_Rejects_Protected_Endpoint_When_Missing_Tenant_And_Company_Headers()
    {
        var wasCalled = false;
        RequestDelegate next = (ctx) =>
        {
            wasCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new AuthenticatedTenantContextMiddleware(next);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/products";

        // Authenticated user with claims
        var claims = new[]
        {
            new Claim("erp:issuer", "https://auth.example.invalid"),
            new Claim("sub", "auth0|123456789")
        };
        var identity = new ClaimsIdentity(claims, "Bearer");
        httpContext.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(httpContext, null!, null!);

        Assert.False(wasCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_Rejects_Protected_Endpoint_When_Missing_OIDC_Subject_Or_Issuer()
    {
        var wasCalled = false;
        RequestDelegate next = (ctx) =>
        {
            wasCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new AuthenticatedTenantContextMiddleware(next);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/products";

        // Authenticated user but missing "sub" and "erp:issuer"
        var identity = new ClaimsIdentity([new Claim("name", "Test User")], "Bearer");
        httpContext.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(httpContext, null!, null!);

        Assert.False(wasCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
    }
}
