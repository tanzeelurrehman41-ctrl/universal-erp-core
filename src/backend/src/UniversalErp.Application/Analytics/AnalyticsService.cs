using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalErp.Application.Analytics;

public class AnalyticsService
{
    private readonly IAnalyticsRepository _repository;

    public AnalyticsService(IAnalyticsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<ExecutiveDashboardDto> GetExecutiveDashboardAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, bool includeConfidentialSalary = false, CancellationToken ct = default)
        => _repository.GetExecutiveDashboardAsync(tenantId, companyId, filter, includeConfidentialSalary, ct);

    public Task<SalesRevenueAnalyticsDto> GetSalesRevenueAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct = default)
        => _repository.GetSalesRevenueAnalyticsAsync(tenantId, companyId, filter, ct);

    public Task<PurchasingSupplierAnalyticsDto> GetPurchasingSupplierAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct = default)
        => _repository.GetPurchasingSupplierAnalyticsAsync(tenantId, companyId, filter, ct);

    public Task<InventoryValuationAgeingDto> GetInventoryValuationAgeingAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct = default)
        => _repository.GetInventoryValuationAgeingAsync(tenantId, companyId, filter, ct);

    public Task<ManufacturingEfficiencyDto> GetManufacturingEfficiencyAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct = default)
        => _repository.GetManufacturingEfficiencyAsync(tenantId, companyId, filter, ct);

    public Task<WarehouseLogisticsAnalyticsDto> GetWarehouseLogisticsAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct = default)
        => _repository.GetWarehouseLogisticsAnalyticsAsync(tenantId, companyId, filter, ct);

    public Task<FinanceProfitabilityAnalyticsDto> GetFinanceProfitabilityAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, CancellationToken ct = default)
        => _repository.GetFinanceProfitabilityAnalyticsAsync(tenantId, companyId, filter, ct);

    public Task<WorkforceCostAnalyticsDto> GetWorkforceCostAnalyticsAsync(Guid tenantId, Guid companyId, AnalyticsDateRangeFilter filter, bool includeConfidentialSalary = false, CancellationToken ct = default)
        => _repository.GetWorkforceCostAnalyticsAsync(tenantId, companyId, filter, includeConfidentialSalary, ct);

    public Task<PlatformReconciliationReportDto> GetPlatformReconciliationReportAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
        => _repository.GetPlatformReconciliationReportAsync(tenantId, companyId, ct);

    public Task<IReadOnlyList<KpiDefinitionDto>> GetKpiDefinitionsAsync(Guid tenantId, Guid companyId, string? category = null, CancellationToken ct = default)
        => _repository.GetKpiDefinitionsAsync(tenantId, companyId, category, ct);

    public Task<KpiDefinitionDto> CreateKpiDefinitionAsync(Guid tenantId, Guid companyId, CreateKpiDefinitionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code)) throw new ArgumentException("KPI code is required.");
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("KPI name is required.");
        return _repository.CreateKpiDefinitionAsync(tenantId, companyId, request, ct);
    }

    public Task<IReadOnlyList<KpiSnapshotDto>> GetKpiSnapshotsAsync(Guid tenantId, Guid companyId, string? kpiCode = null, DateOnly? fromDate = null, CancellationToken ct = default)
        => _repository.GetKpiSnapshotsAsync(tenantId, companyId, kpiCode, fromDate, ct);

    public Task<KpiSnapshotDto> RecordKpiSnapshotAsync(Guid tenantId, Guid companyId, CreateKpiSnapshotRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.KpiCode)) throw new ArgumentException("KPI code is required.");
        return _repository.RecordKpiSnapshotAsync(tenantId, companyId, request, ct);
    }

    /// <summary>
    /// Generates structured CSV / TSV export files for analytical reports.
    /// </summary>
    public async Task<ExportResponse> ExportAnalyticsDataAsync(Guid tenantId, Guid companyId, ExportRequest request, bool includeConfidentialSalary = false, CancellationToken ct = default)
    {
        var filter = new AnalyticsDateRangeFilter(request.StartDate, request.EndDate);
        var sb = new StringBuilder();
        string delimiter = request.Format.Equals("tsv", StringComparison.OrdinalIgnoreCase) ? "\t" : ",";
        string ext = request.Format.Equals("tsv", StringComparison.OrdinalIgnoreCase) ? "tsv" : "csv";
        string contentType = request.Format.Equals("tsv", StringComparison.OrdinalIgnoreCase) ? "text/tab-separated-values" : "text/csv";
        int rowCount = 0;

        string fileName = $"{request.ReportType}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{ext}";

        switch (request.ReportType.ToLowerInvariant())
        {
            case "sales":
                var sales = await _repository.GetSalesRevenueAnalyticsAsync(tenantId, companyId, filter, ct);
                sb.AppendLine(string.Join(delimiter, "Customer Code", "Customer Name", "Total Revenue (PKR)", "Orders Count", "Contribution %"));
                foreach (var c in sales.TopCustomers)
                {
                    sb.AppendLine(string.Join(delimiter, Escape(c.CustomerCode), Escape(c.CustomerName), c.TotalRevenuePkr, c.OrdersCount, c.ContributionPercentage));
                    rowCount++;
                }
                break;

            case "purchasing":
                var purch = await _repository.GetPurchasingSupplierAnalyticsAsync(tenantId, companyId, filter, ct);
                sb.AppendLine(string.Join(delimiter, "Supplier Code", "Supplier Name", "Total Spend (PKR)", "PO Count", "Fulfillment %"));
                foreach (var s in purch.TopSuppliers)
                {
                    sb.AppendLine(string.Join(delimiter, Escape(s.SupplierCode), Escape(s.SupplierName), s.TotalSpendPkr, s.PoCount, s.FulfillmentPercentage));
                    rowCount++;
                }
                break;

            case "inventory_ageing":
                var inv = await _repository.GetInventoryValuationAgeingAsync(tenantId, companyId, filter, ct);
                sb.AppendLine(string.Join(delimiter, "SKU", "Product Name", "Quantity On Hand", "Valuation (PKR)", "Days Inactive"));
                foreach (var item in inv.SlowMovingItems)
                {
                    sb.AppendLine(string.Join(delimiter, Escape(item.Sku), Escape(item.ProductName), item.QuantityOnHand, item.TotalValuationPkr, item.DaysSinceLastMovement));
                    rowCount++;
                }
                break;

            case "workforce":
                var wf = await _repository.GetWorkforceCostAnalyticsAsync(tenantId, companyId, filter, includeConfidentialSalary, ct);
                sb.AppendLine(string.Join(delimiter, "Department Code", "Department Name", "Headcount", "Total Cost (PKR)", "Cost %"));
                foreach (var d in wf.DepartmentBreakdown)
                {
                    sb.AppendLine(string.Join(delimiter, Escape(d.DepartmentCode), Escape(d.DepartmentName), d.Headcount, d.TotalSalaryCostPkr, d.CostPercentage));
                    rowCount++;
                }
                break;

            case "reconciliation":
                var rec = await _repository.GetPlatformReconciliationReportAsync(tenantId, companyId, ct);
                sb.AppendLine(string.Join(delimiter, "Reconciliation Domain", "Status", "Metric 1", "Metric 2", "Variance (PKR)"));
                sb.AppendLine(string.Join(delimiter, "General Ledger Balanced Check", rec.GlReconciliation.IsBalanced ? "BALANCED" : "DISCREPANCY", $"Debits: {rec.GlReconciliation.TotalDebits}", $"Credits: {rec.GlReconciliation.TotalCredits}", rec.GlReconciliation.Discrepancy));
                sb.AppendLine(string.Join(delimiter, "Model A FIFO Layer Reconciliation", rec.FifoReconciliation.IsReconciled ? "RECONCILED" : "VARIANCE", $"FIFO Layers: {rec.FifoReconciliation.FifoActiveLayersValuationPkr}", $"GL 1200: {rec.FifoReconciliation.GlAccount1200ValuationPkr}", rec.FifoReconciliation.VariancePkr));
                rowCount = 2;
                break;

            default: // executive_summary
                var dash = await _repository.GetExecutiveDashboardAsync(tenantId, companyId, filter, includeConfidentialSalary, ct);
                sb.AppendLine(string.Join(delimiter, "KPI Code", "KPI Name", "Category", "Actual Value", "Target Value", "Variance %", "Status"));
                foreach (var kpi in dash.KeyPerformanceIndicators)
                {
                    sb.AppendLine(string.Join(delimiter, Escape(kpi.Code), Escape(kpi.Name), Escape(kpi.Category), kpi.ActualValue, kpi.TargetValue, kpi.VariancePercentage, kpi.Status));
                    rowCount++;
                }
                break;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        string base64 = Convert.ToBase64String(bytes);

        return new ExportResponse(fileName, contentType, base64, rowCount);

        static string Escape(string val) => $"\"{val.Replace("\"", "\"\"")}\"";
    }
}
