using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalErp.Application.Branches;

public interface IBranchesRepository
{
    Task<IReadOnlyList<BranchDto>> GetBranchesAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);
    Task<BranchDto?> GetBranchByIdAsync(Guid tenantId, Guid companyId, Guid branchId, CancellationToken ct = default);
    Task<BranchDto> CreateBranchAsync(Guid tenantId, Guid companyId, CreateBranchRequest request, CancellationToken ct = default);
    Task<BranchDto?> UpdateBranchAsync(Guid tenantId, Guid companyId, Guid branchId, UpdateBranchRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<BranchHierarchyDto>> GetHierarchyAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);
}
