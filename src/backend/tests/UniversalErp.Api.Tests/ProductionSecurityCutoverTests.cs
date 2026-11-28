using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UniversalErp.Api.Development;
using UniversalErp.Api.Security;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class ProductionSecurityCutoverTests
{
    private sealed class FakeWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "UniversalErp.Api";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void DevelopmentTenantContextAccessor_Throws_In_Production_Environment()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevelopmentTenant:TenantId"] = Guid.NewGuid().ToString(),
                ["DevelopmentTenant:CompanyId"] = Guid.NewGuid().ToString(),
                ["DevelopmentTenant:UserId"] = Guid.NewGuid().ToString()
            })
            .Build();

        var prodEnv = new FakeWebHostEnvironment(Environments.Production);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new DevelopmentTenantContextAccessor(configuration, prodEnv));

        Assert.Contains("cannot be used in a non-development environment", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DevelopmentTenantContextAccessor_Succeeds_In_Development_Environment()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevelopmentTenant:TenantId"] = tenantId.ToString(),
                ["DevelopmentTenant:CompanyId"] = companyId.ToString(),
                ["DevelopmentTenant:UserId"] = userId.ToString()
            })
            .Build();

        var devEnv = new FakeWebHostEnvironment(Environments.Development);

        var accessor = new DevelopmentTenantContextAccessor(configuration, devEnv);

        Assert.Equal(tenantId, accessor.Current.TenantId);
        Assert.Equal(companyId, accessor.Current.CompanyId);
        Assert.Equal(userId, accessor.Current.UserId);
    }

    [Theory]
    [InlineData("/api/v1/warehouse/zones")]
    [InlineData("/api/v1/branches")]
    [InlineData("/api/v1/hr/departments")]
    [InlineData("/api/v1/intercompany/relationships")]
    [InlineData("/api/v1/integrations/webhooks")]
    [InlineData("/api/v1/analytics/dashboard")]
    public async Task SecurityMiddleware_Rejects_Unauthenticated_Requests_To_Protected_Endpoints(string path)
    {
        var wasNextInvoked = false;
        RequestDelegate next = _ =>
        {
            wasNextInvoked = true;
            return Task.CompletedTask;
        };

        var middleware = new AuthenticatedTenantContextMiddleware(next);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;

        await middleware.InvokeAsync(httpContext, null!, null!);

        Assert.False(wasNextInvoked);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task SecurityMiddleware_Rejects_Request_With_Missing_Or_Empty_Tenant_And_Company_Headers()
    {
        var wasNextInvoked = false;
        RequestDelegate next = _ =>
        {
            wasNextInvoked = true;
            return Task.CompletedTask;
        };

        var middleware = new AuthenticatedTenantContextMiddleware(next);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/inventory/warehouses";

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("erp:issuer", "https://securetoken.google.com/universal-erp-staging"),
            new Claim("sub", "valid-firebase-uid-12345")
        }, "Firebase");
        httpContext.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(httpContext, null!, null!);

        Assert.False(wasNextInvoked);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task SecurityMiddleware_Rejects_Request_With_Missing_Subject_Claim()
    {
        var wasNextInvoked = false;
        RequestDelegate next = _ =>
        {
            wasNextInvoked = true;
            return Task.CompletedTask;
        };

        var middleware = new AuthenticatedTenantContextMiddleware(next);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/inventory/warehouses";
        httpContext.Request.Headers["X-Tenant-Id"] = Guid.NewGuid().ToString();
        httpContext.Request.Headers["X-Company-Id"] = Guid.NewGuid().ToString();

        // Has issuer but missing subject ("sub")
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("erp:issuer", "https://securetoken.google.com/universal-erp-staging")
        }, "Firebase");
        httpContext.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(httpContext, null!, null!);

        Assert.False(wasNextInvoked);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task SecurityMiddleware_Allows_Public_Endpoints_Without_Authentication()
    {
        var allowedPaths = new[]
        {
            "/api/v1/plans",
            "/api/v1/saas/plans",
            "/api/v1/saas/tax-rules",
            "/api/v1/tenants/register",
            "/health",
            "/health/live"
        };

        foreach (var path in allowedPaths)
        {
            var wasNextInvoked = false;
            RequestDelegate next = _ =>
            {
                wasNextInvoked = true;
                return Task.CompletedTask;
            };

            var middleware = new AuthenticatedTenantContextMiddleware(next);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = path;

            await middleware.InvokeAsync(httpContext, null!, null!);

            Assert.True(wasNextInvoked, $"Endpoint {path} should be publicly reachable without authentication.");
        }
    }
}
