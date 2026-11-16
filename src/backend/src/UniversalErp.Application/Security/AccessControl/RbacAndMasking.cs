using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UniversalErp.Application.Security.AccessControl;

public sealed record UserSecurityContext(
    Guid UserId,
    string Username,
    Guid TenantId,
    Guid? CompanyId,
    Guid? BranchId,
    string Role,
    IReadOnlySet<string> Permissions,
    bool IsPrivilegedSupport = false);

public sealed record PrivilegedSupportSession(
    Guid SessionId,
    Guid TenantId,
    Guid SupportUserId,
    Guid ApprovedByUserId,
    string Justification,
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt = null)
{
    public bool IsActive => RevokedAt is null && DateTimeOffset.UtcNow < ExpiresAt;
}

public sealed class TenantScopedRbacService
{
    private static readonly Dictionary<string, HashSet<string>> RolePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PlatformAdmin"] = new() { "*", "support:elevated_access" },
        ["TenantOwner"] = new() { "tenant:*", "company:*", "branch:*", "finance:*", "payroll:*", "inventory:*", "tax:*", "support:approve" },
        ["CompanyAdmin"] = new() { "company:*", "branch:*", "finance:*", "payroll:*", "inventory:*", "tax:read" },
        ["BranchManager"] = new() { "branch:*", "inventory:*", "sales:*", "purchasing:*" },
        ["Accountant"] = new() { "finance:read", "finance:post", "payroll:read", "payroll:run", "tax:read" },
        ["Staff"] = new() { "sales:create", "inventory:read" }
    };

    public static bool EvaluateAccess(
        UserSecurityContext user,
        Guid targetTenantId,
        Guid? targetCompanyId,
        Guid? targetBranchId,
        string requiredPermission)
    {
        // 1. Privileged support exception (if within active approved support session)
        if (user.IsPrivilegedSupport && user.TenantId == targetTenantId)
        {
            return true;
        }

        // 2. Strict Tenant Isolation Boundary
        if (user.TenantId != targetTenantId && !string.Equals(user.Role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 3. Company Scoping Boundary
        if (targetCompanyId.HasValue && user.CompanyId.HasValue && user.CompanyId.Value != targetCompanyId.Value)
        {
            if (!string.Equals(user.Role, "TenantOwner", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(user.Role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 4. Branch Scoping Boundary
        if (targetBranchId.HasValue && user.BranchId.HasValue && user.BranchId.Value != targetBranchId.Value)
        {
            if (!string.Equals(user.Role, "CompanyAdmin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(user.Role, "TenantOwner", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(user.Role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 5. Permission Resolution
        if (user.Permissions != null && (user.Permissions.Contains("*") || user.Permissions.Contains(requiredPermission)))
        {
            return true;
        }

        if (RolePermissions.TryGetValue(user.Role, out var rolePerms))
        {
            if (rolePerms.Contains("*") || rolePerms.Contains(requiredPermission))
            {
                return true;
            }

            var wildcard = requiredPermission.Split(':')[0] + ":*";
            if (rolePerms.Contains(wildcard))
            {
                return true;
            }
        }

        return false;
    }
}

public static class DataMaskingService
{
    public static string MaskSsn(string? ssn)
    {
        if (string.IsNullOrWhiteSpace(ssn)) return "";
        var clean = Regex.Replace(ssn, @"[\s-]", "");
        if (clean.Length != 9) return "***-**-****";
        return $"***-**-{clean.Substring(5)}";
    }

    public static string MaskCnic(string? cnic)
    {
        if (string.IsNullOrWhiteSpace(cnic)) return "";
        var clean = Regex.Replace(cnic, @"[\s-]", "");
        if (clean.Length != 13) return "***** ******* *";
        return $"{clean.Substring(0, 5)}-*******-{clean.Substring(12, 1)}";
    }

    public static string MaskBankAccount(string? account)
    {
        if (string.IsNullOrWhiteSpace(account)) return "";
        var trimmed = account.Trim();
        if (trimmed.Length <= 4) return "****";
        return new string('*', trimmed.Length - 4) + trimmed.Substring(trimmed.Length - 4);
    }

    public static string MaskCreditCard(string? cc)
    {
        if (string.IsNullOrWhiteSpace(cc)) return "";
        var clean = Regex.Replace(cc, @"[\s-]", "");
        if (clean.Length <= 4) return "**** **** **** ****";
        return $"**** **** **** {clean.Substring(clean.Length - 4)}";
    }

    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return "***@***.com";
        var parts = email.Split('@');
        var name = parts[0];
        var domain = parts[1];
        var maskedName = name.Length <= 1 ? name + "***" : name[0] + "***" + name[^1];
        return $"{maskedName}@{domain}";
    }
}
