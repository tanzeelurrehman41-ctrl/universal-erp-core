using System.Security.Claims;
using System.Text.Json;
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
/// Regression tests for Option A2 currency-aware inventory valuation, FX resolution,
/// GL currency reconciliation, tenant/company isolation, and valuation authorization.
/// </summary>
public sealed class InventoryValuationHotfixTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class StubTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext Current { get; } = context;
    }

    private sealed class CapturingInventoryRepository(
        Func<Guid, Guid, InventoryValuationReportSummary>? factory = null) : IInventoryWorkflowRepository
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
            if (factory is not null)
            {
                return Task.FromResult(factory(tenantId, companyId));
            }

            return Task.FromResult(InventoryValuationReportSummary.Empty("PKR"));
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

    private sealed class StubDatabasePermissionHandler(HashSet<string> grantedPermissions)
        : AuthorizationHandler<DatabasePermissionRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            DatabasePermissionRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated == true &&
                grantedPermissions.Contains(requirement.PermissionCode))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
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

    private static InventoryValuationSourceLayerInput MakeLayer(
        string binCode,
        string docType,
        DateTimeOffset receivedAt,
        decimal origQty,
        decimal remQty,
        decimal unitCost,
        string sourceCurrency) =>
        new(
            LayerId: Guid.NewGuid(),
            ParentLayerId: null,
            VariantId: Guid.Parse("2871558f-e577-4d2d-b365-fa60be311111"),
            Sku: "SCISSOR-EA-01",
            WarehouseId: Guid.Parse("de9ffb26-8faa-4ec0-a90d-4945bdf4b7ff"),
            WarehouseCode: "MAIN-W1",
            BinId: Guid.NewGuid(),
            BinCode: binCode,
            SourceDocumentType: docType,
            SourceDocumentId: Guid.NewGuid(),
            ReceivedAt: receivedAt,
            OriginalQty: origQty,
            RemainingQty: remQty,
            SourceUnitCost: unitCost,
            SourceCurrencyCode: sourceCurrency,
            StatusCode: "available");

    [Fact]
    public async Task ValuationReportService_PropagatesTenantAndCompanyIsolation()
    {
        var tenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var companyA = Guid.Parse("22222222-2222-4222-8222-222222222223");
        var companyB = Guid.Parse("33333333-3333-4333-8333-333333333334");
        var otherTenant = Guid.Parse("44444444-4444-4444-8444-444444444445");

        var repo = new CapturingInventoryRepository((_, cid) =>
            cid == companyA
                ? InventoryValuationCalculator.BuildReport(
                    "USD",
                    [MakeLayer("PICK-01", "goods_receipt", DateTimeOffset.Parse("2026-11-10T07:28:34Z"), 20m, 18m, 10m, "USD")],
                    [],
                    18m,
                    [new InventoryValuationGlCurrencyBalanceInput("USD", 1, 180m)])
                : InventoryValuationCalculator.BuildReport(
                    "PKR",
                    [MakeLayer("RECEIVE-01", "goods_receipt", DateTimeOffset.Parse("2026-11-10T07:28:34Z"), 90m, 90m, 45.2778m, "PKR")],
                    [],
                    90m,
                    [new InventoryValuationGlCurrencyBalanceInput("PKR", 2, 4075.002m)]));

        var serviceA = new InventoryWorkflowService(
            new StubTenantContextAccessor(new TenantContext(tenantId, companyA, Guid.NewGuid())),
            repo);
        var reportA = await serviceA.GetInventoryValuationReportAsync(CancellationToken.None);

        Assert.Equal(tenantId, repo.CapturedTenantId);
        Assert.Equal(companyA, repo.CapturedCompanyId);
        Assert.Equal("USD", reportA.FunctionalCurrencyCode);
        Assert.Equal(180m, reportA.FunctionalValuation);

        var serviceB = new InventoryWorkflowService(
            new StubTenantContextAccessor(new TenantContext(otherTenant, companyB, Guid.NewGuid())),
            repo);
        var reportB = await serviceB.GetInventoryValuationReportAsync(CancellationToken.None);

        Assert.Equal(otherTenant, repo.CapturedTenantId);
        Assert.Equal(companyB, repo.CapturedCompanyId);
        Assert.Equal("PKR", reportB.FunctionalCurrencyCode);
        Assert.NotEqual(reportA.FunctionalCurrencyCode, reportB.FunctionalCurrencyCode);
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
    public void Calculator_UsdFunctionalValuation_ComputesAndReconcilesInUsd_WithoutMislabeledPkrFields()
    {
        var layers = new[]
        {
            MakeLayer("PICK-01", "goods_receipt", DateTimeOffset.Parse("2026-11-10T07:28:34Z"), 20m, 20m, 12.50m, "USD"),
            MakeLayer("RECEIVE-01", "goods_receipt", DateTimeOffset.Parse("2026-11-11T09:00:00Z"), 10m, 10m, 15.00m, "usd"),
        };

        var report = InventoryValuationCalculator.BuildReport(
            companyBaseCurrencyCode: "USD",
            sourceLayers: layers,
            exchangeRates: [],
            totalPhysicalStock: 30m,
            gl1200BalancesByCurrency: [new InventoryValuationGlCurrencyBalanceInput("USD", 2, 400.00m)]);

        Assert.Equal("USD", report.FunctionalCurrencyCode);
        Assert.Equal(400.00m, report.FunctionalValuation);
        Assert.Equal(400.00m, report.TotalValuation);
        Assert.Null(report.TotalValuationPkr);
        Assert.Equal(400.00m, report.GeneralLedger1200FunctionalBalance);
        Assert.Equal(400.00m, report.GeneralLedger1200Balance);
        Assert.Null(report.GeneralLedger1200BalancePkr);
        Assert.Equal(0m, report.ReconciliationVarianceFunctional);
        Assert.Equal(0m, report.ReconciliationVariance);
        Assert.Null(report.ReconciliationVariancePkr);
        Assert.True(report.IsReconciled);
        Assert.Equal("RECONCILED", report.ReconciliationStatus);
        Assert.True(report.IsFunctionalValuationResolved);
        Assert.True(report.IsGeneralLedgerCurrencyAligned);
        Assert.Equal(0, report.MissingExchangeRateLayerCount);
        Assert.Null(report.UnresolvedCurrencyIssue);

        var totalSourceUsd = Assert.Single(report.SourceValuationByCurrency);
        Assert.Equal("USD", totalSourceUsd.CurrencyCode);
        Assert.Equal(400.00m, totalSourceUsd.Amount);

        Assert.All(report.Layers, layer =>
        {
            Assert.Equal("USD", layer.SourceCurrencyCode);
            Assert.Equal("USD", layer.FunctionalCurrencyCode);
            Assert.Equal(1m, layer.AppliedExchangeRate);
            Assert.Equal("same_currency", layer.CurrencyConversionStatus);
            Assert.Equal(layer.SourceUnitCost, layer.FunctionalUnitCost);
            Assert.Equal(layer.SourceExtendedValuation, layer.FunctionalExtendedValuation);
        });
    }

    [Fact]
    public void Calculator_PkrSourceWithUsdHistoricalRate_ConvertsUsingApplicableSpotRateAndReconciles()
    {
        var t1 = DateTimeOffset.Parse("2026-11-10T07:28:34Z");
        var t2 = DateTimeOffset.Parse("2026-11-10T07:28:35Z");
        var t3 = DateTimeOffset.Parse("2026-11-15T11:57:00Z");

        var layers = new[]
        {
            MakeLayer("PICK-01", "goods_receipt", t1, 20m, 18m, 45m, "PKR"),
            MakeLayer("RECEIVE-01", "goods_receipt", t2, 30m, 29m, 45m, "PKR"),
            MakeLayer("PICK-01", "opening_capitalization", t3, 3m, 3m, 45m, "PKR"),
        };

        var exchangeRates = new[]
        {
            // Historical spot rate applicable on Nov 10: 1 PKR = 0.0036 USD
            new InventoryValuationExchangeRateInput("PKR", "USD", DateTimeOffset.Parse("2026-11-10T00:00:00Z"), 0.0036m),
            // Newer historical spot rate applicable on Nov 15 (inverse quote convention: 1 USD = 250 PKR => 0.0040 USD/PKR)
            new InventoryValuationExchangeRateInput("USD", "PKR", DateTimeOffset.Parse("2026-11-15T00:00:00Z"), 250m),
            // Future rate after all receipt timestamps — MUST NOT be applied to Nov 10 or Nov 15 layers
            new InventoryValuationExchangeRateInput("PKR", "USD", DateTimeOffset.Parse("2026-11-25T00:00:00Z"), 0.0100m),
        };

        // Expected USD functional values:
        // Layer 1: 810 PKR * 0.0036 = 2.9160 USD
        // Layer 2: 1305 PKR * 0.0036 = 4.6980 USD
        // Layer 3: 135 PKR / 250 = 0.5400 USD
        // Total USD = 8.1540 USD (source total = PKR 2,250.00)
        var report = InventoryValuationCalculator.BuildReport(
            companyBaseCurrencyCode: "USD",
            sourceLayers: layers,
            exchangeRates: exchangeRates,
            totalPhysicalStock: 50m,
            gl1200BalancesByCurrency: [new InventoryValuationGlCurrencyBalanceInput("USD", 3, 8.1540m)]);

        Assert.Equal("USD", report.FunctionalCurrencyCode);
        Assert.True(report.IsFunctionalValuationResolved);
        Assert.Equal(0, report.MissingExchangeRateLayerCount);
        Assert.Equal(8.1540m, report.FunctionalValuation);
        Assert.Equal(8.1540m, report.TotalValuation);
        Assert.Null(report.TotalValuationPkr);
        Assert.Equal(8.1540m, report.GeneralLedger1200FunctionalBalance);
        Assert.Equal(0m, report.ReconciliationVarianceFunctional);
        Assert.True(report.IsReconciled);
        Assert.Equal("RECONCILED", report.ReconciliationStatus);

        var sourcePkr = Assert.Single(report.SourceValuationByCurrency);
        Assert.Equal("PKR", sourcePkr.CurrencyCode);
        Assert.Equal(2250m, sourcePkr.Amount);
        Assert.Equal(50m, sourcePkr.Quantity);

        Assert.Equal("PKR", report.Layers[0].SourceCurrencyCode);
        Assert.Equal(45m, report.Layers[0].SourceUnitCost);
        Assert.Equal(810m, report.Layers[0].SourceExtendedValuation);
        Assert.Equal("USD", report.Layers[0].FunctionalCurrencyCode);
        Assert.Equal(0.0036m, report.Layers[0].AppliedExchangeRate);
        Assert.Equal(0.162m, report.Layers[0].FunctionalUnitCost);
        Assert.Equal(2.9160m, report.Layers[0].FunctionalExtendedValuation);
        Assert.Equal("converted_historical", report.Layers[0].CurrencyConversionStatus);

        Assert.Equal(0.004m, report.Layers[2].AppliedExchangeRate);
        Assert.Equal(0.5400m, report.Layers[2].FunctionalExtendedValuation);
    }

    [Fact]
    public void Calculator_MissingHistoricalExchangeRate_DoesNotManufactureUsdOrCompareRawPkrAsUsd()
    {
        var t1 = DateTimeOffset.Parse("2026-11-10T07:28:34Z");
        var t2 = DateTimeOffset.Parse("2026-11-10T07:28:35Z");
        var t3 = DateTimeOffset.Parse("2026-11-15T11:57:00Z");

        var layers = new[]
        {
            MakeLayer("PICK-01", "goods_receipt", t1, 20m, 18m, 45m, "PKR"),
            MakeLayer("RECEIVE-01", "goods_receipt", t2, 30m, 29m, 45m, "PKR"),
            MakeLayer("PICK-01", "opening_capitalization", t3, 3m, 3m, 45m, "PKR"),
        };

        // Include ONLY a later ("today's") exchange rate after layer timestamps — must not be substituted!
        var futureRateOnly = new[]
        {
            new InventoryValuationExchangeRateInput("PKR", "USD", DateTimeOffset.Parse("2026-11-25T00:00:00Z"), 0.0036m),
        };

        var report = InventoryValuationCalculator.BuildReport(
            companyBaseCurrencyCode: "USD",
            sourceLayers: layers,
            exchangeRates: futureRateOnly,
            totalPhysicalStock: 50m,
            gl1200BalancesByCurrency: [new InventoryValuationGlCurrencyBalanceInput("PKR", 6, 2250m)]);

        Assert.Equal("USD", report.FunctionalCurrencyCode);
        Assert.False(report.IsFunctionalValuationResolved);
        Assert.False(report.IsGeneralLedgerCurrencyAligned);
        Assert.False(report.IsReconciled);
        Assert.Equal("UNRESOLVED_CURRENCY_MISMATCH", report.ReconciliationStatus);
        Assert.Equal(3, report.MissingExchangeRateLayerCount);
        Assert.Null(report.FunctionalValuation);
        Assert.Null(report.TotalValuation);
        Assert.Null(report.TotalValuationPkr);
        Assert.Null(report.GeneralLedger1200FunctionalBalance);
        Assert.Null(report.GeneralLedger1200Balance);
        Assert.Null(report.GeneralLedger1200BalancePkr);
        Assert.Null(report.ReconciliationVarianceFunctional);
        Assert.Null(report.ReconciliationVariance);
        Assert.Null(report.ReconciliationVariancePkr);
        Assert.NotNull(report.UnresolvedCurrencyIssue);

        var sourcePkr = Assert.Single(report.SourceValuationByCurrency);
        Assert.Equal("PKR", sourcePkr.CurrencyCode);
        Assert.Equal(2250m, sourcePkr.Amount);

        var glPkr = Assert.Single(report.GeneralLedger1200BalancesByCurrency);
        Assert.Equal("PKR", glPkr.CurrencyCode);
        Assert.Equal(2250m, glPkr.Amount);

        Assert.All(report.Layers, l =>
        {
            Assert.Equal("PKR", l.SourceCurrencyCode);
            Assert.Equal("USD", l.FunctionalCurrencyCode);
            Assert.Equal("missing_historical_rate", l.CurrencyConversionStatus);
            Assert.Null(l.AppliedExchangeRate);
            Assert.Null(l.FunctionalUnitCost);
            Assert.Null(l.FunctionalExtendedValuation);
        });

        // Verify JSON contract emitted to dashboard never exposes raw 2250 as totalValuation or totalValuationPkr
        var json = JsonSerializer.Serialize(report, WebJsonOptions);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("USD", doc.RootElement.GetProperty("functionalCurrencyCode").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("functionalValuation").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("totalValuation").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("totalValuationPkr").ValueKind);
        Assert.False(doc.RootElement.GetProperty("isReconciled").GetBoolean());
    }

    [Fact]
    public void Calculator_CurrencyAwareGlReconciliation_RejectsMismatchedJournalCurrency_AndPreservesLocalPkrBaseline()
    {
        // Even if numeric totals match (100 vs 100), PKR GL journals must NEVER reconcile against a USD company!
        var mismatchedGlReport = InventoryValuationCalculator.BuildReport(
            companyBaseCurrencyCode: "USD",
            sourceLayers: [MakeLayer("PICK-01", "goods_receipt", DateTimeOffset.Parse("2026-11-10T07:00:00Z"), 10m, 10m, 10m, "USD")],
            exchangeRates: [],
            totalPhysicalStock: 10m,
            gl1200BalancesByCurrency: [new InventoryValuationGlCurrencyBalanceInput("PKR", 1, 100m)]);

        Assert.True(mismatchedGlReport.IsFunctionalValuationResolved);
        Assert.Equal(100m, mismatchedGlReport.FunctionalValuation);
        Assert.False(mismatchedGlReport.IsGeneralLedgerCurrencyAligned);
        Assert.Null(mismatchedGlReport.GeneralLedger1200FunctionalBalance);
        Assert.Null(mismatchedGlReport.ReconciliationVarianceFunctional);
        Assert.False(mismatchedGlReport.IsReconciled);
        Assert.Equal("GL_CURRENCY_MISMATCH", mismatchedGlReport.ReconciliationStatus);

        // Local staging parity (PKR company, PKR layers, PKR GL journals) must reconcile cleanly
        var localStagingReport = InventoryValuationCalculator.BuildReport(
            companyBaseCurrencyCode: "PKR",
            sourceLayers: [MakeLayer("RECEIVE-01", "goods_receipt", DateTimeOffset.Parse("2026-11-10T07:00:00Z"), 90m, 90m, 45.277777778m, "PKR")],
            exchangeRates: [],
            totalPhysicalStock: 90m,
            gl1200BalancesByCurrency: [new InventoryValuationGlCurrencyBalanceInput("PKR", 2, 4075.00m)]);

        Assert.Equal("PKR", localStagingReport.FunctionalCurrencyCode);
        Assert.Equal(4075.00m, localStagingReport.FunctionalValuation);
        Assert.Equal(4075.00m, localStagingReport.TotalValuationPkr);
        Assert.Equal(4075.00m, localStagingReport.GeneralLedger1200FunctionalBalance);
        Assert.Equal(4075.00m, localStagingReport.GeneralLedger1200BalancePkr);
        Assert.Equal(0m, localStagingReport.ReconciliationVarianceFunctional);
        Assert.True(localStagingReport.IsReconciled);
        Assert.Equal("RECONCILED", localStagingReport.ReconciliationStatus);
    }

    [Fact]
    public async Task InventoryEndpoints_MapOnlyValuationReport_AndRequireDedicatedValuationViewPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ITenantContextAccessor>(new StubTenantContextAccessor(
            new TenantContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())));
        builder.Services.AddSingleton<IInventoryWorkflowRepository, CapturingInventoryRepository>();
        builder.Services.AddScoped<InventoryWorkflowService>();
        var granted = new HashSet<string>(StringComparer.Ordinal);
        builder.Services.AddSingleton<IAuthorizationHandler>(new StubDatabasePermissionHandler(granted));
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("inventory.valuation.view", policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new DatabasePermissionRequirement("inventory.valuation.view")));
        });

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
        Assert.Equal(["inventory.valuation.view"], authPolicies);
        Assert.DoesNotContain("inventory.valuation.report", authPolicies);
        Assert.DoesNotContain("inventory.availability.view", authPolicies);

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

        // Verify authorization policy enforcement for missing vs granted permission
        using var scope = app.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var viewerUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("erp:issuer", "https://securetoken.google.com/universal-erp-staging"),
            new Claim("sub", "nnnmCg7nT0cehV14o5APFFtY2s53"),
        ], "Firebase"));

        granted.Add("inventory.availability.view");
        var deniedWithoutValuation = await authService.AuthorizeAsync(viewerUser, null, "inventory.valuation.view");
        Assert.False(deniedWithoutValuation.Succeeded);

        granted.Add("inventory.valuation.view");
        var allowedWithValuation = await authService.AuthorizeAsync(viewerUser, null, "inventory.valuation.view");
        Assert.True(allowedWithValuation.Succeeded);

        var unauthUser = new ClaimsPrincipal(new ClaimsIdentity());
        var deniedUnauthenticated = await authService.AuthorizeAsync(unauthUser, null, "inventory.valuation.view");
        Assert.False(deniedUnauthenticated.Succeeded);
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

        Assert.Contains("identity.get_company_settings", repoSource, StringComparison.Ordinal);
        Assert.Contains("erp_inventory.inventory_cost_layer", repoSource, StringComparison.Ordinal);
        Assert.Contains("erp_inventory.stock_balance", repoSource, StringComparison.Ordinal);
        Assert.Contains("finance.exchange_rates", repoSource, StringComparison.Ordinal);
        Assert.Contains("finance.journal_lines", repoSource, StringComparison.Ordinal);
        Assert.Contains("j.functional_currency", repoSource, StringComparison.Ordinal);
    }
}
