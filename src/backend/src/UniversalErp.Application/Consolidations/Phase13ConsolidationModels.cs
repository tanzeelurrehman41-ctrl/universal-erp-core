using System;
using System.Collections.Generic;

namespace UniversalErp.Application.Consolidations;

// ── Intercompany Relationships ───────────────────────────────────────────────

public record IntercompanyRelationshipDto(
    Guid Id,
    Guid TenantId,
    Guid FromCompanyId,
    Guid ToCompanyId,
    Guid DueToAccountId,
    Guid DueFromAccountId,
    string ClearingCurrencyCode,
    string TransferPricingRule,
    decimal MarkupPercentage,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record CreateIntercompanyRelationshipRequest(
    Guid FromCompanyId,
    Guid ToCompanyId,
    Guid DueToAccountId,
    Guid DueFromAccountId,
    string ClearingCurrencyCode,
    string TransferPricingRule = "at_cost",
    decimal MarkupPercentage = 0.000m
);

// ── Intercompany Stock Transfers ─────────────────────────────────────────────

public record IntercompanyTransferLineDto(
    Guid Id,
    Guid TransferId,
    Guid ProductId,
    decimal QuantityDispatched,
    decimal QuantityReceived,
    decimal UnitCostPkr,
    decimal TransferPricePkr,
    decimal LineTotalPkr,
    Guid? SourceLayerId,
    Guid? SourceWarehouseId,
    Guid? SourceBinId,
    Guid? DestWarehouseId,
    Guid? DestBinId
);

public record IntercompanyTransferLineRequest(
    Guid ProductId,
    decimal Quantity,
    decimal UnitCostPkr,
    decimal? TransferPricePkr = null,
    Guid? SourceLayerId = null,
    Guid? SourceWarehouseId = null,
    Guid? SourceBinId = null,
    Guid? DestWarehouseId = null,
    Guid? DestBinId = null
);

public record IntercompanyTransferDto(
    Guid Id,
    Guid TenantId,
    string TransferNumber,
    Guid FromCompanyId,
    Guid? FromBranchId,
    Guid ToCompanyId,
    Guid? ToBranchId,
    string Status,
    DateOnly DispatchDate,
    DateOnly? ReceiptDate,
    string CurrencyCode,
    decimal TotalAmountPkr,
    Guid? FromGlJournalId,
    Guid? ToGlJournalId,
    string? Notes,
    IReadOnlyList<IntercompanyTransferLineDto> Lines,
    DateTimeOffset CreatedAt
);

public record CreateIntercompanyTransferRequest(
    string TransferNumber,
    Guid FromCompanyId,
    Guid ToCompanyId,
    DateOnly DispatchDate,
    string CurrencyCode,
    IReadOnlyList<IntercompanyTransferLineRequest> Lines,
    Guid? FromBranchId = null,
    Guid? ToBranchId = null,
    string? Notes = null
);

public record IntercompanyTransferReceiptLine(
    Guid LineId,
    decimal QuantityReceived,
    Guid DestWarehouseId,
    Guid DestBinId
);

public record ReceiveIntercompanyTransferRequest(
    DateOnly ReceiptDate,
    IReadOnlyList<IntercompanyTransferReceiptLine>? Lines = null
);

// ── Financial Consolidation Runs & Statements ────────────────────────────────

public record ConsolidationRunDto(
    Guid Id,
    Guid TenantId,
    Guid? HoldingCompanyId,
    string RunCode,
    Guid FiscalPeriodId,
    string ReportingCurrencyCode,
    string Status,
    decimal TotalConsolidatedAssetsPkr,
    decimal TotalConsolidatedLiabilitiesPkr,
    decimal TotalConsolidatedEquityPkr,
    decimal TotalIntercompanyEliminationsPkr,
    decimal UnrealizedProfitEliminationPkr,
    Guid? EliminationJournalId,
    IReadOnlyList<Guid> ParticipatingCompanies,
    IReadOnlyList<EliminationLineDto> EliminationLines,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt = null,
    Guid? ApprovedBy = null
);

public record EliminationLineDto(
    Guid Id,
    Guid ConsolidationRunId,
    string EliminationType,
    Guid FromCompanyId,
    Guid ToCompanyId,
    string AccountCode,
    decimal DebitAmountPkr,
    decimal CreditAmountPkr,
    string Description
);

public record ExecuteConsolidationRequest(
    string RunCode,
    Guid FiscalPeriodId,
    Guid? HoldingCompanyId,
    List<Guid> ParticipatingCompanyIds,
    string ReportingCurrencyCode = "PKR",
    bool EliminateUnrealizedProfit = true
);

public record ConsolidatedStatementLineDto(
    string AccountCode,
    string AccountName,
    string AccountType,
    Dictionary<Guid, decimal> CompanyBalances,
    decimal EliminationDebit,
    decimal EliminationCredit,
    decimal ConsolidatedBalance
);

public record ConsolidatedStatementDto(
    string RunCode,
    Guid FiscalPeriodId,
    string StatementType,
    IReadOnlyList<ConsolidatedStatementLineDto> Lines,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal NetElimination,
    bool IsBalanced
);
