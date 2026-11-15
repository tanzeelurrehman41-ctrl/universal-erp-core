using System;
using System.Collections.Generic;

namespace UniversalErp.Application.Analytics;

// ── Query Filters ────────────────────────────────────────────────────────────

public record AnalyticsDateRangeFilter(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    Guid? WarehouseId = null,
    Guid? DepartmentId = null,
    Guid? ProductId = null
);

// ── Executive Dashboard ──────────────────────────────────────────────────────

public record ExecutiveDashboardDto(
    Guid TenantId,
    Guid CompanyId,
    DateOnly AsOfDate,
    ExecutiveFinancialSummary Financials,
    ExecutiveSupplyChainSummary SupplyChain,
    ExecutiveManufacturingSummary Manufacturing,
    ExecutiveWorkforceSummary Workforce,
    IReadOnlyList<KpiCardDto> KeyPerformanceIndicators,
    DateTimeOffset CalculatedAt
);

public record ExecutiveFinancialSummary(
    decimal TotalRevenuePkr,
    decimal CostOfGoodsSoldPkr,
    decimal GrossProfitPkr,
    decimal GrossProfitMarginPercentage,
    decimal OperatingExpensesPkr,
    decimal NetIncomePkr,
    decimal NetProfitMarginPercentage,
    decimal OperatingCashFlowPkr,
    decimal AccountsReceivablePkr,
    decimal AccountsPayablePkr,
    bool IsGeneralLedgerBalanced
);

public record ExecutiveSupplyChainSummary(
    decimal TotalInventoryValuationPkr,
    decimal FifoValuationPkr,
    bool IsFifoReconciled,
    int ActiveSkuCount,
    decimal OpenSalesOrdersValuePkr,
    int OpenSalesOrdersCount,
    decimal OpenPurchaseOrdersValuePkr,
    int OpenPurchaseOrdersCount,
    decimal InventoryTurnoverRatio,
    decimal AverageBinUtilizationPercentage
);

public record ExecutiveManufacturingSummary(
    int TotalWorkOrders,
    int CompletedWorkOrders,
    decimal WorkOrderCompletionRatePercentage,
    decimal WorkInProgressValuationPkr,
    decimal TotalScrapCostPkr,
    decimal ScrapRatePercentage
);

public record ExecutiveWorkforceSummary(
    int TotalActiveEmployees,
    decimal MonthlyGrossPayrollPkr,
    decimal MonthlyNetPayrollPkr,
    decimal EmployerStatutoryCostPkr,
    decimal AverageCostPerEmployeePkr,
    decimal OvertimeExpenditurePkr
);

public record KpiCardDto(
    string Code,
    string Name,
    string Category,
    string Unit,
    decimal ActualValue,
    decimal TargetValue,
    decimal Variance,
    decimal VariancePercentage,
    string Status, // 'on_track', 'warning', 'critical'
    string TrendDirection // 'up', 'down', 'flat'
);

// ── Sales & Revenue Analytics ────────────────────────────────────────────────

public record SalesRevenueAnalyticsDto(
    decimal TotalRevenuePkr,
    decimal TotalCostOfSalesPkr,
    decimal GrossMarginPkr,
    decimal MarginPercentage,
    IReadOnlyList<MonthlyRevenueTrendDto> MonthlyTrends,
    IReadOnlyList<CustomerRevenueRankDto> TopCustomers,
    IReadOnlyList<CategoryRevenueRankDto> TopCategories,
    ArAgingAnalysisDto ArAging
);

public record MonthlyRevenueTrendDto(
    string Month, // YYYY-MM
    decimal RevenuePkr,
    decimal CostPkr,
    decimal GrossProfitPkr,
    int OrderCount
);

public record CustomerRevenueRankDto(
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    decimal TotalRevenuePkr,
    int OrdersCount,
    decimal ContributionPercentage
);

public record CategoryRevenueRankDto(
    string CategoryName,
    decimal TotalRevenuePkr,
    decimal UnitsSold,
    decimal ContributionPercentage
);

public record ArAgingAnalysisDto(
    decimal CurrentPkr,
    decimal Days1To30Pkr,
    decimal Days31To60Pkr,
    decimal Days61To90Pkr,
    decimal Over90DaysPkr,
    decimal TotalOutstandingPkr,
    decimal DaysSalesOutstanding
);

// ── Purchasing & Supplier Performance ────────────────────────────────────────

public record PurchasingSupplierAnalyticsDto(
    decimal TotalSpendPkr,
    int TotalPurchaseOrders,
    int CompletedPurchaseOrders,
    decimal PoFulfillmentRatePercentage,
    decimal OnTimeDeliveryPercentage,
    decimal PurchasePriceVariancePkr,
    IReadOnlyList<SupplierSpendRankDto> TopSuppliers,
    ApAgingAnalysisDto ApAging
);

public record SupplierSpendRankDto(
    Guid SupplierId,
    string SupplierCode,
    string SupplierName,
    decimal TotalSpendPkr,
    int PoCount,
    decimal FulfillmentPercentage
);

public record ApAgingAnalysisDto(
    decimal CurrentPkr,
    decimal Days1To30Pkr,
    decimal Days31To60Pkr,
    decimal Days61To90Pkr,
    decimal Over90DaysPkr,
    decimal TotalPayablePkr,
    decimal DaysPayableOutstanding
);

// ── Inventory Valuation & Ageing Analytics ───────────────────────────────────

public record InventoryValuationAgeingDto(
    decimal TotalGlInventoryValuationPkr,
    decimal TotalFifoLayerValuationPkr,
    decimal ValuationVariancePkr,
    bool IsReconciled,
    decimal TurnoverRatio,
    InventoryAgeingBrackets AgeingBrackets,
    IReadOnlyList<WarehouseInventoryBreakdownDto> WarehouseBreakdown,
    IReadOnlyList<SlowMovingItemDto> SlowMovingItems
);

public record InventoryAgeingBrackets(
    decimal Days0To30ValuePkr,
    decimal Days31To60ValuePkr,
    decimal Days61To90ValuePkr,
    decimal Over90DaysValuePkr
);

public record WarehouseInventoryBreakdownDto(
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    decimal ValuationPkr,
    int BinCount,
    decimal CapacityUtilizationPercentage
);

public record SlowMovingItemDto(
    Guid ProductVariantId,
    string Sku,
    string ProductName,
    decimal QuantityOnHand,
    decimal TotalValuationPkr,
    int DaysSinceLastMovement
);

// ── Manufacturing Efficiency & WIP ───────────────────────────────────────────

public record ManufacturingEfficiencyDto(
    int TotalWorkOrders,
    int CompletedWorkOrders,
    int InProgressWorkOrders,
    decimal CompletionRatePercentage,
    decimal WorkInProgressValuationPkr,
    decimal MaterialCostPkr,
    decimal LaborCostPkr,
    decimal OverheadCostPkr,
    decimal CostVariancePkr,
    decimal TotalScrapCostPkr,
    decimal ScrapRatePercentage,
    IReadOnlyList<WorkCenterPerformanceDto> WorkCenterMetrics
);

public record WorkCenterPerformanceDto(
    Guid WorkCenterId,
    string Code,
    string Name,
    int TotalJobs,
    decimal TotalHours,
    decimal UtilizationPercentage
);

// ── Warehouse Logistics Analytics ────────────────────────────────────────────

public record WarehouseLogisticsAnalyticsDto(
    int TotalBins,
    int OccupiedBins,
    decimal BinOccupancyPercentage,
    decimal AverageWeightUtilizationPercentage,
    decimal AverageVolumeUtilizationPercentage,
    int TotalStockMovements,
    int TotalShipmentPackages,
    IReadOnlyList<ZoneUtilizationDto> ZoneMetrics
);

public record ZoneUtilizationDto(
    string ZoneCode,
    string ZoneName,
    int TotalBins,
    int OccupiedBins,
    decimal OccupancyPercentage
);

// ── Finance, P&L & Cash Flow Analytics ───────────────────────────────────────

public record FinanceProfitabilityAnalyticsDto(
    bool IsGeneralLedgerBalanced,
    decimal TotalDebitPkr,
    decimal TotalCreditPkr,
    IncomeStatementSummary PnL,
    BalanceSheetSummary BalanceSheet,
    CashFlowSummary CashFlow
);

public record IncomeStatementSummary(
    decimal RevenuePkr,
    decimal CostOfGoodsSoldPkr,
    decimal GrossProfitPkr,
    decimal OperatingExpensesPkr,
    decimal NetIncomePkr,
    decimal GrossMarginPercentage,
    decimal NetMarginPercentage
);

public record BalanceSheetSummary(
    decimal CurrentAssetsPkr,
    decimal NonCurrentAssetsPkr,
    decimal TotalAssetsPkr,
    decimal CurrentLiabilitiesPkr,
    decimal NonCurrentLiabilitiesPkr,
    decimal TotalLiabilitiesPkr,
    decimal EquityPkr,
    bool IsBalanced
);

public record CashFlowSummary(
    decimal OperatingCashFlowPkr,
    decimal InvestingCashFlowPkr,
    decimal FinancingCashFlowPkr,
    decimal NetCashFlowPkr
);

// ── Workforce Cost & HR Analytics ────────────────────────────────────────────

public record WorkforceCostAnalyticsDto(
    int TotalActiveHeadcount,
    decimal MonthlyGrossPayrollPkr,
    decimal MonthlyNetPayrollPkr,
    decimal EmployerEobiCostPkr,
    decimal EmployerPfCostPkr,
    decimal TotalEmployerStatutoryCostPkr,
    decimal TotalOvertimeExpenditurePkr,
    decimal TotalStaffLoansOutstandingPkr,
    bool IsSalaryMasked,
    IReadOnlyList<DepartmentCostBreakdownDto> DepartmentBreakdown
);

public record DepartmentCostBreakdownDto(
    Guid DepartmentId,
    string DepartmentCode,
    string DepartmentName,
    int Headcount,
    decimal TotalSalaryCostPkr,
    decimal CostPercentage
);

// ── KPI Snapshot & Configuration Models ──────────────────────────────────────

public record KpiDefinitionDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string Code,
    string Name,
    string Category,
    string? Description,
    string Unit,
    decimal TargetValue,
    string CalculationMethod,
    string Frequency,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record CreateKpiDefinitionRequest(
    string Code,
    string Name,
    string Category,
    string Unit,
    decimal TargetValue,
    string CalculationMethod,
    string Frequency = "monthly",
    string? Description = null
);

public record KpiSnapshotDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    DateOnly SnapshotDate,
    string KpiCode,
    decimal ActualValue,
    decimal TargetValue,
    decimal Variance,
    decimal VariancePercentage,
    string Status,
    string? Notes,
    DateTimeOffset CreatedAt
);

public record CreateKpiSnapshotRequest(
    DateOnly SnapshotDate,
    string KpiCode,
    decimal ActualValue,
    string? Notes = null
);

// ── Reconciliation Models ────────────────────────────────────────────────────

public record PlatformReconciliationReportDto(
    Guid TenantId,
    Guid CompanyId,
    DateTimeOffset ReconciledAt,
    GlReconciliationStatus GlReconciliation,
    FifoReconciliationStatus FifoReconciliation,
    bool AllReconciled
);

public record GlReconciliationStatus(
    bool IsBalanced,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal Discrepancy,
    int TotalPostedJournals,
    int TotalJournalLines
);

public record FifoReconciliationStatus(
    bool IsReconciled,
    decimal FifoActiveLayersValuationPkr,
    decimal GlAccount1200ValuationPkr,
    decimal VariancePkr,
    int TotalActiveLayers,
    decimal TotalActiveQuantity
);

// ── Data Export Models ───────────────────────────────────────────────────────

public record ExportRequest(
    string ReportType, // 'executive_summary', 'sales', 'purchasing', 'inventory_ageing', 'finance_pnl', 'workforce', 'reconciliation'
    string Format,     // 'csv', 'tsv', 'json'
    DateOnly? StartDate = null,
    DateOnly? EndDate = null
);

public record ExportResponse(
    string FileName,
    string ContentType,
    string FileContentBase64,
    int RowCount
);
