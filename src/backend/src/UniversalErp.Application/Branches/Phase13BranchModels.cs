using System;
using System.Collections.Generic;

namespace UniversalErp.Application.Branches;

public record BranchDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string BranchCode,
    string BranchName,
    string BranchType,
    Guid? ParentBranchId,
    string? TaxRegistrationNumber,
    string? AddressLine,
    string City,
    string ProvinceOrState,
    string CountryCode,
    Guid? ManagerId,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record CreateBranchRequest(
    string BranchCode,
    string BranchName,
    string BranchType,
    string City,
    string ProvinceOrState,
    Guid? ParentBranchId = null,
    string? TaxRegistrationNumber = null,
    string? AddressLine = null,
    string CountryCode = "PK",
    Guid? ManagerId = null
);

public record UpdateBranchRequest(
    string BranchName,
    string BranchType,
    string City,
    string ProvinceOrState,
    Guid? ParentBranchId = null,
    string? TaxRegistrationNumber = null,
    string? AddressLine = null,
    string CountryCode = "PK",
    Guid? ManagerId = null,
    bool IsActive = true
);

public record BranchHierarchyDto(
    BranchDto Branch,
    IReadOnlyList<BranchHierarchyDto> Children
);

public record BranchSwitchResult(
    Guid BranchId,
    string BranchCode,
    string BranchName,
    string BranchType,
    bool IsActive,
    string TokenContext
);
