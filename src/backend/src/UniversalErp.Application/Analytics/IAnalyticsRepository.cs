using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalErp.Application.Analytics;

public interface IAnalyticsRepository
{
    Task<ExecutiveDashboardDto> GetExecutiveDashboardAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, bool includeConfidentialSalary = false, CancellationToken ct = default);
    Task<SalesRevenueAnalyticsDto> GetSalesRevenueAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct = default);
    Task<PurchasingSupplierAnalyticsDto> GetPurchasingSupplierAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct = default);
    Task<InventoryValuationAgeingDto> GetInventoryValuationAgeingAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct = default);
    Task<ManufacturingEfficiencyDto> GetManufacturingEfficiencyAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct = default);
    Task<WarehouseLogisticsAnalyticsDto> GetWarehouseLogisticsAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct = default);
    Task<FinanceProfitabilityAnalyticsDto> GetFinanceProfitabilityAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct = default);
    Task<WorkforceCostAnalyticsDto> GetWorkforceCostAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, bool includeConfidentialSalary = false, CancellationToken ct = default);
    Task<PlatformReconciliationReportDto> GetPlatformReconciliationReportAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);

    // KPI Management
    Task<IReadOnlyList<KpiDefinitionDto>> GetKpiDefinitionsAsync(Guid tenantId, Guid companyId, string? category = null, CancellationToken ct = default);
    Task<KpiDefinitionDto> CreateKpiDefinitionAsync(Guid tenantId, Guid companyId, CreateKpiDefinitionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<KpiSnapshotDto>> GetKpiSnapshotsAsync(Guid tenantId, Guid companyId, string? kpiCode = null, DateOnly? fromDate = null, CancellationToken ct = default);
    Task<KpiSnapshotDto> RecordKpiSnapshotAsync(Guid tenantId, Guid companyId, CreateKpiSnapshotRequest request, CancellationToken ct = default);
}
