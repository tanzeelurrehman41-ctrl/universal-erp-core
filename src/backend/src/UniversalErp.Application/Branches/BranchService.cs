using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalErp.Application.Branches;

public class BranchService
{
    private readonly IBranchesRepository _repository;

    public BranchService(IBranchesRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<BranchDto>> GetBranchesAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        return _repository.GetBranchesAsync(tenantId, companyId, ct);
    }

    public Task<BranchDto?> GetBranchByIdAsync(Guid tenantId, Guid companyId, Guid branchId, CancellationToken ct = default)
    {
        return _repository.GetBranchByIdAsync(tenantId, companyId, branchId, ct);
    }

    public Task<BranchDto> CreateBranchAsync(Guid tenantId, Guid companyId, CreateBranchRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.BranchCode))
            throw new ArgumentException("Branch code is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.BranchName))
            throw new ArgumentException("Branch name is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.City))
            throw new ArgumentException("City is required.", nameof(request));

        return _repository.CreateBranchAsync(tenantId, companyId, request, ct);
    }

    public Task<BranchDto?> UpdateBranchAsync(Guid tenantId, Guid companyId, Guid branchId, UpdateBranchRequest request, CancellationToken ct = default)
    {
        return _repository.UpdateBranchAsync(tenantId, companyId, branchId, request, ct);
    }

    public Task<IReadOnlyList<BranchHierarchyDto>> GetHierarchyAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
    {
        return _repository.GetHierarchyAsync(tenantId, companyId, ct);
    }

    public async Task<BranchSwitchResult> SwitchBranchAsync(Guid tenantId, Guid companyId, Guid branchId, CancellationToken ct = default)
    {
        var branch = await _repository.GetBranchByIdAsync(tenantId, companyId, branchId, ct);
        if (branch == null)
            throw new InvalidOperationException($"Branch {branchId} not found or inaccessible in active company.");
        if (!branch.IsActive)
            throw new InvalidOperationException($"Branch {branch.BranchCode} is inactive and cannot be selected.");

        var tokenContext = $"tenant:{tenantId};company:{companyId};branch:{branch.Id};code:{branch.BranchCode}";
        return new BranchSwitchResult(branch.Id, branch.BranchCode, branch.BranchName, branch.BranchType, branch.IsActive, tokenContext);
    }
}
