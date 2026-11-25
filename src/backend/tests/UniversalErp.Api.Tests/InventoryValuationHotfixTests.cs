using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UniversalErp.Api.Development;
using UniversalErp.Api.Inventory;
using UniversalErp.Api.Security;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.Inventory;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;
using UniversalErp.Infrastructure.Inventory;
using Xunit;

namespace UniversalErp.Api.Tests;

/// <summary>
/// Regression tests for the Option A2 minimal inventory valuation report hotfix and core security hardening.
/// </summary>
public sealed class InventoryValuationHotfixTests
{
    private sealed class StubTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current { get; } = context;
    }

    private sealed class CapturingInventoryRepository : IInventoryWorkflowRepository
    {
        public Guid? CapturedTenantId { get; private set; }
        public Guid? CapturedCompanyId { get; private set; }

        public Task<InventoryValuationReportSummary> GetInventoryValuationReportAsync(
            Guid tenantId,
            Guid companyId,
            CancellationToken cancellationToken)
        {
            CapturedTenantId = tenantId;
            CapturedCompanyId = companyId;
            return Task.FromResult(new InventoryValuationReportSummary(
                GeneratedAt: DateTimeOffset.UtcNow,
                TotalActiveLayers: 2,
                TotalPhysicalStock: 50m,
                TotalValuedStock: 50m,
                TotalUnvaluedStock: 0m,
                TotalValuationPkr: 2250m,
                GeneralLedger1200BalancePkr: 2250m,
                ReconciliationVariancePkr: 0m,
                IsReconciled: true,
                Layers:
                [
                    new InventoryValuationLayerReportItem(
                        Guid.NewGuid(), null, Guid.NewGuid(), "SKU-001",
                        Guid.NewGuid(), "MAIN-W1", Guid.NewGuid(), "RECEIVE-01",
                        "grn", null, DateTimeOffset.UtcNow, 29m, 29m, 45m, 1305m, "PKR", "available"),
                    new InventoryValuationLayerReportItem(
                        Guid.NewGuid(), null, Guid.NewGuid(), "SKU-001",
                        Guid.NewGuid(), "MAIN-W1", Guid.NewGuid(), "PICK-01",
                        "transfer", null, DateTimeOffset.UtcNow, 21m, 21m, 45m, 945m, "PKR", "available"),
                ]));
        }

        public Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<ProductCategorySummary>> CreateCategoryAsync(Guid tenantId, CreateProductCategoryRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<InventoryMovementSummary>> ListMovementsAsync(Guid tenantId, Guid companyId, int limit, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<UnitOfMeasureSummary>> ListUnitsAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<UnitOfMeasureSummary>> CreateUnitAsync(Guid tenantId, CreateUnitOfMeasureRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProductVariantSummary>> ListVariantsAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<ProductVariantSummary>> CreateVariantAsync(Guid tenantId, CreateProductVariantRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<WarehouseSummary>> ListWarehousesAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<WarehouseSummary>> CreateWarehouseAsync(Guid tenantId, Guid companyId, CreateWarehouseRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<StockBinSummary>> ListBinsAsync(Guid tenantId, Guid companyId, Guid? warehouseId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<StockBinSummary>> CreateBinAsync(Guid tenantId, Guid companyId, Guid warehouseId, CreateStockBinRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<InventoryAvailabilitySummary>> ListAvailabilityAsync(Guid tenantId, Guid companyId, Guid? variantId, Guid? warehouseId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<InventoryReceiptSummary>> PostReceiptAsync(TenantContext context, Guid transactionId, string requestHash, PostInventoryReceiptRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<InventoryTransferSummary>> PostTransferAsync(TenantContext context, Guid transactionId, string requestHash, PostInventoryTransferRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Result<InventoryAdjustmentSummary>> PostAdjustmentAsync(TenantContext context, Guid transactionId, string requestHash, PostInventoryAdjustmentRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FakeWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "UniversalErp.Api";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = environmentName;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    }

    [Fact]
    public async Task ValuationReportService_PropagatesTenantAndCompanyIsolation()
    {
        var tenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var companyId = Guid.Parse("22222222-2222-4222-8222-222222222223");
        var repo = new CapturingInventoryRepository();
        var service = new InventoryWorkflowService(
            new StubTenantContextAccessor(new TenantContext(tenantId, companyId, Guid.NewGuid())),
            repo);

        var summary = await service.GetInventoryValuationReportAsync(CancellationToken.None);

        Assert.Equal(tenantId, repo.CapturedTenantId);
        Assert.Equal(companyId, repo.CapturedCompanyId);
        Assert.True(summary.IsReconciled);
        Assert.Equal(2250m, summary.TotalValuationPkr);
        Assert.Equal(2250m, summary.GeneralLedger1200BalancePkr);
        Assert.Equal(0m, summary.ReconciliationVariancePkr);
        Assert.Equal(2, summary.Layers.Count);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "22222222-2222-4222-8222-222222222223")]
    [InlineData("22222222-2222-4222-8222-222222222222", "00000000-0000-0000-0000-000000000000")]
    public async Task ValuationReportService_RejectsMissingTenantOrCompanyContext(string tid, string cid)
    {
        var service = new InventoryWorkflowService(
            new StubTenantContextAccessor(new TenantContext(Guid.Parse(tid), Guid.Parse(cid), Guid.Empty)),
            new CapturingInventoryRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetInventoryValuationReportAsync(CancellationToken.None));
    }

    [Fact]
    public void InventoryEndpoints_MapOnlyValuationReport_AndRequireExistingValuationViewPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ITenantContextAccessor>(new StubTenantContextAccessor(
            new TenantContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())));
        builder.Services.AddSingleton<IInventoryWorkflowRepository, CapturingInventoryRepository>();
        builder.Services.AddScoped<InventoryWorkflowService>();
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.MapInventoryEndpoints(isDevelopment: false);

        var routeEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        var valuationEndpoint = routeEndpoints.SingleOrDefault(e =>
            string.Equals(e.RoutePattern.RawText, "/api/v1/inventory/valuation-report", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(valuationEndpoint);
        var authPolicies = valuationEndpoint!.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .ToList();
        Assert.Contains("inventory.valuation.view", authPolicies);

        var nonHotfixPhase5Routes = new[]
        {
            "/api/v1/inventory/reservations",
            "/api/v1/inventory/atp",
            "/api/v1/inventory/pick-lists",
            "/api/v1/inventory/counts",
            "/api/v1/inventory/returns",
            "/api/v1/inventory/movement-history",
        };
        foreach (var excludedRoute in nonHotfixPhase5Routes)
        {
            Assert.DoesNotContain(routeEndpoints, e =>
                (e.RoutePattern.RawText ?? string.Empty).StartsWith(excludedRoute, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void DevelopmentTenantContextAccessor_ThrowsInProduction_AndAllowsDevelopment()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevelopmentTenant:TenantId"] = "11111111-1111-4111-8111-111111111111",
                ["DevelopmentTenant:CompanyId"] = "11111111-1111-4111-8111-111111111112",
                ["DevelopmentTenant:UserId"] = "11111111-1111-4111-8111-111111111113",
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new DevelopmentTenantContextAccessor(config, new FakeWebHostEnvironment(Environments.Production)));

        var devAccessor = new DevelopmentTenantContextAccessor(config, new FakeWebHostEnvironment(Environments.Development));
        Assert.Equal(Guid.Parse("11111111-1111-4111-8111-111111111111"), devAccessor.Current.TenantId);
    }

    [Fact]
    public async Task Middleware_BlocksUnauthenticatedValuationReport_AndRequiresTenantHeaders()
    {
        var wasCalled = false;
        var middleware = new AuthenticatedTenantContextMiddleware(_ =>
        {
            wasCalled = true;
            return Task.CompletedTask;
        });

        var unauthContext = new DefaultHttpContext();
        unauthContext.Request.Path = "/api/v1/inventory/valuation-report";
        await middleware.InvokeAsync(unauthContext, null!, null!);

        Assert.False(wasCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthContext.Response.StatusCode);

        var authNoHeaders = new DefaultHttpContext();
        authNoHeaders.Request.Path = "/api/v1/inventory/valuation-report";
        authNoHeaders.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("erp:issuer", "https://securetoken.google.com/universal-erp-staging"),
            new Claim("sub", "nnnmCg7nT0cehV14o5APFFtY2s53"),
        ], "Firebase"));
        await middleware.InvokeAsync(authNoHeaders, null!, null!);

        Assert.False(wasCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, authNoHeaders.Response.StatusCode);
    }

    [Fact]
    public void RepositoryImplementation_HasNoDependencyOnM027OrLaterTables()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "UniversalErp.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var repoSource = File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "UniversalErp.Infrastructure", "Inventory", "PostgresInventoryWorkflowRepository.cs"));

        var forbiddenM027Tables = new[]
        {
            "stock_pick_list",
            "stock_pick_line",
            "stock_count_session",
            "stock_count_line",
            "stock_return",
            "stock_return_line",
            "stock_reservation",
        };

        foreach (var table in forbiddenM027Tables)
        {
            Assert.DoesNotContain(table, repoSource, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("erp_inventory.inventory_cost_layer", repoSource, StringComparison.Ordinal);
        Assert.Contains("erp_inventory.stock_balance", repoSource, StringComparison.Ordinal);
        Assert.Contains("finance.journal_lines", repoSource, StringComparison.Ordinal);
    }
}
