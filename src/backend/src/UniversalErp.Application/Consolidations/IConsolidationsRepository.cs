using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalErp.Application.Consolidations;

public interface IConsolidationsRepository
{
    Task<IReadOnlyList<IntercompanyRelationshipDto>> GetRelationshipsAsync(Guid tenantId, CancellationToken ct = default);
    Task<IntercompanyRelationshipDto?> GetRelationshipAsync(Guid tenantId, Guid fromCompanyId, Guid toCompanyId, CancellationToken ct = default);
    Task<IntercompanyRelationshipDto> CreateRelationshipAsync(Guid tenantId, CreateIntercompanyRelationshipRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<IntercompanyTransferDto>> GetTransfersAsync(Guid tenantId, Guid? companyId = null, string? status = null, CancellationToken ct = default);
    Task<IntercompanyTransferDto?> GetTransferByIdAsync(Guid tenantId, Guid transferId, CancellationToken ct = default);
    Task<IntercompanyTransferDto> CreateTransferAsync(Guid tenantId, CreateIntercompanyTransferRequest request, CancellationToken ct = default);
    Task<IntercompanyTransferDto> DispatchTransferAsync(Guid tenantId, Guid transferId, Guid userId, CancellationToken ct = default);
    Task<IntercompanyTransferDto> ReceiveTransferAsync(Guid tenantId, Guid transferId, Guid userId, ReceiveIntercompanyTransferRequest request, CancellationToken ct = default);

    Task<ConsolidationRunDto> ExecuteConsolidationAsync(Guid tenantId, Guid userId, ExecuteConsolidationRequest request, CancellationToken ct = default);
    Task<ConsolidationRunDto?> GetConsolidationRunByIdAsync(Guid tenantId, Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<ConsolidationRunDto>> GetConsolidationRunsAsync(Guid tenantId, Guid? fiscalPeriodId = null, CancellationToken ct = default);
    Task<ConsolidatedStatementDto> GetConsolidatedStatementAsync(Guid tenantId, Guid runId, string statementType, CancellationToken ct = default);
}
