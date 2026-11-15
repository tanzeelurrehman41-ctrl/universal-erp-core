using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniversalErp.Application.Analytics;
using Xunit;

namespace UniversalErp.Api.Tests;

public class AnalyticsEngineTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _companyId = Guid.NewGuid();

    private sealed class MockAnalyticsRepository : IAnalyticsRepository
    {
        public SalesRevenueAnalyticsDto? SalesData { get; set; }
        public PlatformReconciliationReportDto? ReconciliationData { get; set; }

        public Task<ExecutiveDashboardDto> GetExecutiveDashboardAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, bool includeConfidentialSalary, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<SalesRevenueAnalyticsDto> GetSalesRevenueAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct) =>
            Task.FromResult(SalesData ?? new SalesRevenueAnalyticsDto(0, 0, 0, 0, Array.Empty<MonthlyRevenueTrendDto>(), Array.Empty<CustomerRevenueRankDto>(), Array.Empty<CategoryRevenueRankDto>(), new ArAgingAnalysisDto(0, 0, 0, 0, 0, 0, 0)));

        public Task<PurchasingSupplierAnalyticsDto> GetPurchasingSupplierAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<InventoryValuationAgeingDto> GetInventoryValuationAgeingAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<ManufacturingEfficiencyDto> GetManufacturingEfficiencyAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<WarehouseLogisticsAnalyticsDto> GetWarehouseLogisticsAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<FinanceProfitabilityAnalyticsDto> GetFinanceProfitabilityAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<WorkforceCostAnalyticsDto> GetWorkforceCostAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, bool includeConfidentialSalary, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<PlatformReconciliationReportDto> GetPlatformReconciliationReportAsync(Guid tenantId, Guid companyId, CancellationToken ct) =>
            Task.FromResult(ReconciliationData ?? new PlatformReconciliationReportDto(tenantId, companyId, DateTimeOffset.UtcNow, new GlReconciliationStatus(true, 0, 0, 0, 0, 0), new FifoReconciliationStatus(true, 0, 0, 0, 0, 0), true));

        public Task<IReadOnlyList<KpiDefinitionDto>> GetKpiDefinitionsAsync(Guid tenantId, Guid companyId, string? category, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<KpiDefinitionDto> CreateKpiDefinitionAsync(Guid tenantId, Guid companyId, CreateKpiDefinitionRequest request, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<KpiSnapshotDto>> GetKpiSnapshotsAsync(Guid tenantId, Guid companyId, string? kpiCode, DateOnly? fromDate, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<KpiSnapshotDto> RecordKpiSnapshotAsync(Guid tenantId, Guid companyId, CreateKpiSnapshotRequest request, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task ExportAnalyticsDataAsync_GeneratesValidCsv_ForSales()
    {
        // Arrange
        var mockRepo = new MockAnalyticsRepository
        {
            SalesData = new SalesRevenueAnalyticsDto(
                TotalRevenuePkr: 1_250_000m,
                TotalCostOfSalesPkr: 800_000m,
                GrossMarginPkr: 450_000m,
                MarginPercentage: 36.0m,
                MonthlyTrends: Array.Empty<MonthlyRevenueTrendDto>(),
                TopCustomers: new List<CustomerRevenueRankDto>
                {
                    new(Guid.NewGuid(), "CUST-001", "Acme \"Global\", Inc.", 750_000m, 5, 60.0m),
                    new(Guid.NewGuid(), "CUST-002", "Beta Traders", 500_000m, 3, 40.0m)
                },
                TopCategories: Array.Empty<CategoryRevenueRankDto>(),
                ArAging: new ArAgingAnalysisDto(0, 0, 0, 0, 0, 0, 0)
            )
        };

        var service = new AnalyticsService(mockRepo);
        var request = new ExportRequest("sales", "csv");

        // Act
        var result = await service.ExportAnalyticsDataAsync(_tenantId, _companyId, request);

        // Assert
        Assert.NotNull(result);
        Assert.EndsWith(".csv", result.FileName);
        Assert.Equal("text/csv", result.ContentType);
        Assert.Equal(2, result.RowCount);

        string csvContent = Encoding.UTF8.GetString(Convert.FromBase64String(result.FileContentBase64));
        var lines = csvContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length); // 1 header + 2 data rows
        Assert.Contains("Customer Code,Customer Name,Total Revenue (PKR)", lines[0]);
        Assert.Contains("\"Acme \"\"Global\"\", Inc.\"", lines[1]); // Escaped quotes and comma
        Assert.Contains("\"Beta Traders\"", lines[2]);
    }

    [Fact]
    public async Task ExportAnalyticsDataAsync_GeneratesValidTsv_ForReconciliation()
    {
        // Arrange
        var mockRepo = new MockAnalyticsRepository
        {
            ReconciliationData = new PlatformReconciliationReportDto(
                TenantId: _tenantId,
                CompanyId: _companyId,
                ReconciledAt: DateTimeOffset.UtcNow,
                GlReconciliation: new GlReconciliationStatus(
                    IsBalanced: true,
                    TotalDebits: 2_500_000m,
                    TotalCredits: 2_500_000m,
                    Discrepancy: 0m,
                    TotalPostedJournals: 42,
                    TotalJournalLines: 84
                ),
                FifoReconciliation: new FifoReconciliationStatus(
                    IsReconciled: true,
                    FifoActiveLayersValuationPkr: 2_250m,
                    GlAccount1200ValuationPkr: 2_250m,
                    VariancePkr: 0m,
                    TotalActiveLayers: 3,
                    TotalActiveQuantity: 50m
                ),
                AllReconciled: true
            )
        };

        var service = new AnalyticsService(mockRepo);
        var request = new ExportRequest("reconciliation", "tsv");

        // Act
        var result = await service.ExportAnalyticsDataAsync(_tenantId, _companyId, request);

        // Assert
        Assert.NotNull(result);
        Assert.EndsWith(".tsv", result.FileName);
        Assert.Equal("text/tab-separated-values", result.ContentType);
        Assert.Equal(2, result.RowCount);

        string tsvContent = Encoding.UTF8.GetString(Convert.FromBase64String(result.FileContentBase64));
        var lines = tsvContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains("\t", lines[0]);
        Assert.Contains("BALANCED", lines[1]);
        Assert.Contains("RECONCILED", lines[2]);
    }

    [Theory]
    [InlineData(100_000, 1_000_000, 365, 36.5)]
    [InlineData(500_000, 2_000_000, 30, 7.5)]
    [InlineData(0, 1_000_000, 365, 0)]
    public void DaysSalesOutstanding_CalculatesCorrectly(decimal ar, decimal creditSales, int days, decimal expectedDso)
    {
        decimal dso = creditSales > 0 ? (ar / creditSales) * days : 0m;
        Assert.Equal(expectedDso, Math.Round(dso, 2));
    }

    [Theory]
    [InlineData(200_000, 1_200_000, 365, 60.83)]
    [InlineData(50_000, 500_000, 90, 9.0)]
    public void DaysPayableOutstanding_CalculatesCorrectly(decimal ap, decimal purchasesOrCogs, int days, decimal expectedDpo)
    {
        decimal dpo = purchasesOrCogs > 0 ? (ap / purchasesOrCogs) * days : 0m;
        Assert.Equal(expectedDpo, Math.Round(dpo, 2));
    }

    [Theory]
    [InlineData(100, 100, 0, "on_track")]
    [InlineData(110, 100, 10, "on_track")]
    [InlineData(95, 100, -5, "warning")]
    [InlineData(85, 100, -15, "critical")]
    public void StandardKpiStatus_EvaluatesCorrectly(decimal actual, decimal target, decimal expectedVariancePct, string expectedStatus)
    {
        decimal variancePct = target != 0 ? ((actual - target) / target) * 100m : 0m;
        Assert.Equal(expectedVariancePct, variancePct);

        string status = variancePct switch
        {
            >= 0 => "on_track",
            >= -10 => "warning",
            _ => "critical"
        };

        Assert.Equal(expectedStatus, status);
    }

    [Fact]
    public void FifoReconciliation_DetectsZeroVariance()
    {
        decimal fifoLayers = 2250m;
        decimal gl1200 = 2250m;
        decimal variance = Math.Abs(fifoLayers - gl1200);
        bool isReconciled = variance < 0.01m;

        Assert.True(isReconciled);
        Assert.Equal(0m, variance);
    }

    [Fact]
    public void FifoReconciliation_DetectsVariance_WhenUnbalanced()
    {
        decimal fifoLayers = 2250m;
        decimal gl1200 = 2300m;
        decimal variance = Math.Abs(fifoLayers - gl1200);
        bool isReconciled = variance < 0.01m;

        Assert.False(isReconciled);
        Assert.Equal(50m, variance);
    }
}
