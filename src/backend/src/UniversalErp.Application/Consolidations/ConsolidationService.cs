using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalErp.Application.Consolidations;

public class ConsolidationService
{
    private readonly IConsolidationsRepository _repository;

    public ConsolidationService(IConsolidationsRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<IntercompanyRelationshipDto>> GetRelationshipsAsync(Guid tenantId, CancellationToken ct = default)
    {
        return _repository.GetRelationshipsAsync(tenantId, ct);
    }

    public Task<IntercompanyRelationshipDto?> GetRelationshipAsync(Guid tenantId, Guid fromCompanyId, Guid toCompanyId, CancellationToken ct = default)
    {
        return _repository.GetRelationshipAsync(tenantId, fromCompanyId, toCompanyId, ct);
    }

    public Task<IntercompanyRelationshipDto> CreateRelationshipAsync(Guid tenantId, CreateIntercompanyRelationshipRequest request, CancellationToken ct = default)
    {
        if (request.FromCompanyId == request.ToCompanyId)
            throw new ArgumentException("A company cannot establish an intercompany relationship with itself.");
        if (request.DueToAccountId == Guid.Empty || request.DueFromAccountId == Guid.Empty)
            throw new ArgumentException("Due-To and Due-From clearing accounts are required.");

        return _repository.CreateRelationshipAsync(tenantId, request, ct);
    }

    public Task<IReadOnlyList<IntercompanyTransferDto>> GetTransfersAsync(Guid tenantId, Guid? companyId = null, string? status = null, CancellationToken ct = default)
    {
        return _repository.GetTransfersAsync(tenantId, companyId, status, ct);
    }

    public Task<IntercompanyTransferDto?> GetTransferByIdAsync(Guid tenantId, Guid transferId, CancellationToken ct = default)
    {
        return _repository.GetTransferByIdAsync(tenantId, transferId, ct);
    }

    public Task<IntercompanyTransferDto> CreateTransferAsync(Guid tenantId, CreateIntercompanyTransferRequest request, CancellationToken ct = default)
    {
        if (request.FromCompanyId == request.ToCompanyId)
            throw new ArgumentException("Cannot transfer inventory to the same company.");
        if (request.Lines == null || request.Lines.Count == 0)
            throw new ArgumentException("At least one transfer line is required.");

        return _repository.CreateTransferAsync(tenantId, request, ct);
    }

    public Task<IntercompanyTransferDto> DispatchTransferAsync(Guid tenantId, Guid transferId, Guid userId, CancellationToken ct = default)
    {
        return _repository.DispatchTransferAsync(tenantId, transferId, userId, ct);
    }

    public Task<IntercompanyTransferDto> ReceiveTransferAsync(Guid tenantId, Guid transferId, Guid userId, ReceiveIntercompanyTransferRequest request, CancellationToken ct = default)
    {
        return _repository.ReceiveTransferAsync(tenantId, transferId, userId, request, ct);
    }

    public Task<ConsolidationRunDto> ExecuteConsolidationAsync(Guid tenantId, Guid userId, ExecuteConsolidationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RunCode))
            throw new ArgumentException("RunCode is required.", nameof(request));
        if (request.FiscalPeriodId == Guid.Empty)
            throw new ArgumentException("FiscalPeriodId is required.", nameof(request));
        if (request.ParticipatingCompanyIds == null || request.ParticipatingCompanyIds.Count < 2)
            throw new ArgumentException("Consolidation requires at least two participating companies.");

        return _repository.ExecuteConsolidationAsync(tenantId, userId, request, ct);
    }

    public Task<ConsolidationRunDto?> GetConsolidationRunByIdAsync(Guid tenantId, Guid runId, CancellationToken ct = default)
    {
        return _repository.GetConsolidationRunByIdAsync(tenantId, runId, ct);
    }

    public Task<IReadOnlyList<ConsolidationRunDto>> GetConsolidationRunsAsync(Guid tenantId, Guid? fiscalPeriodId = null, CancellationToken ct = default)
    {
        return _repository.GetConsolidationRunsAsync(tenantId, fiscalPeriodId, ct);
    }

    public Task<ConsolidatedStatementDto> GetConsolidatedStatementAsync(Guid tenantId, Guid runId, string statementType, CancellationToken ct = default)
    {
        return _repository.GetConsolidatedStatementAsync(tenantId, runId, statementType, ct);
    }
}
