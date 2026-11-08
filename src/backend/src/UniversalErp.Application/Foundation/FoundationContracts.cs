using System.Text.Json;

namespace UniversalErp.Application.Foundation;

public sealed record UserRoleDetail(
    string RoleCode,
    string Name);

public sealed record UserProfileResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    string Status,
    DateTimeOffset CreatedAt,
    Guid TenantId,
    string TenantCode,
    string TenantName,
    Guid CompanyId,
    string CompanyCode,
    string CompanyName,
    string? LegalName,
    string? RegistrationNumber,
    string CurrencyCode,
    string Timezone,
    IReadOnlyList<UserRoleDetail> Roles,
    IReadOnlyList<string> Permissions);

public sealed record CompanySettingsResponse(
    Guid CompanyId,
    Guid TenantId,
    string CompanyCode,
    string Name,
    string? LegalName,
    string? RegistrationNumber,
    string CountryCode,
    string CurrencyCode,
    string Timezone,
    int FiscalYearStartMonth,
    string Status,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateCompanySettingsCommand(
    string? Name,
    string? LegalName,
    string? RegistrationNumber,
    string? Timezone);

public sealed record CompanyUserSummary(
    Guid UserId,
    string Email,
    string DisplayName,
    string Status,
    IReadOnlyList<UserRoleDetail> Roles,
    DateTimeOffset CreatedAt);

public sealed record InviteCompanyUserCommand(
    string Email,
    string DisplayName,
    string? RoleCode);

public sealed record RolePermissionDetail(
    string Code,
    string Description,
    string Module);

public sealed record RoleDetail(
    Guid RoleId,
    string RoleCode,
    string Name,
    bool IsSystemRole,
    IReadOnlyList<RolePermissionDetail> Permissions);

public sealed record AuditEventSummary(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string? ActorEmail,
    string? ActorName,
    string EventCode,
    string EntityType,
    Guid? EntityId,
    JsonElement Metadata);

public interface IFoundationRepository
{
    Task<UserProfileResponse?> GetUserProfileAsync(Guid tenantId, Guid companyId, Guid userId, CancellationToken ct = default);
    Task<CompanySettingsResponse?> GetCompanySettingsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);
    Task<CompanySettingsResponse?> UpdateCompanySettingsAsync(Guid tenantId, Guid companyId, UpdateCompanySettingsCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<CompanyUserSummary>> ListCompanyUsersAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);
    Task<CompanyUserSummary?> InviteCompanyUserAsync(Guid tenantId, Guid companyId, InviteCompanyUserCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<RoleDetail>> ListRolesAndPermissionsAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEventSummary>> ListAuditEventsAsync(Guid tenantId, Guid companyId, int limit = 50, CancellationToken ct = default);
    Task<Guid> RecordAuditEventAsync(Guid tenantId, Guid companyId, Guid actorUserId, string eventCode, string entityType, Guid? entityId, object? metadata = null, CancellationToken ct = default);
}
