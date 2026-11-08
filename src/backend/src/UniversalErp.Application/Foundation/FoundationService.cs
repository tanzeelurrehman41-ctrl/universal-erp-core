using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Application.Foundation;

public sealed class FoundationService(IFoundationRepository repository)
{
    public async Task<UserProfileResponse> GetProfileAsync(TenantContext context, CancellationToken ct = default)
    {
        var profile = await repository.GetUserProfileAsync(context.TenantId, context.CompanyId, context.UserId, ct);
        if (profile is null)
        {
            throw new KeyNotFoundException("User profile not found in active tenant and company context.");
        }
        return profile;
    }

    public Task<CompanySettingsResponse?> GetSettingsAsync(TenantContext context, CancellationToken ct = default) =>
        repository.GetCompanySettingsAsync(context.TenantId, context.CompanyId, ct);

    public async Task<CompanySettingsResponse> UpdateSettingsAsync(TenantContext context, UpdateCompanySettingsCommand command, CancellationToken ct = default)
    {
        var updated = await repository.UpdateCompanySettingsAsync(context.TenantId, context.CompanyId, command, ct);
        if (updated is null)
        {
            throw new KeyNotFoundException("Company settings could not be updated.");
        }

        await repository.RecordAuditEventAsync(
            context.TenantId,
            context.CompanyId,
            context.UserId,
            "ORGANIZATION_SETTINGS_UPDATED",
            "company",
            context.CompanyId,
            new { command.Name, command.LegalName, command.RegistrationNumber, command.Timezone },
            ct);

        return updated;
    }

    public Task<IReadOnlyList<CompanyUserSummary>> ListUsersAsync(TenantContext context, CancellationToken ct = default) =>
        repository.ListCompanyUsersAsync(context.TenantId, context.CompanyId, ct);

    public async Task<CompanyUserSummary> InviteUserAsync(TenantContext context, InviteCompanyUserCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Email) || !command.Email.Contains('@'))
        {
            throw new ArgumentException("A valid email address is required.", nameof(command));
        }

        var user = await repository.InviteCompanyUserAsync(context.TenantId, context.CompanyId, command, ct);
        if (user is null)
        {
            throw new InvalidOperationException("Failed to invite company user.");
        }

        await repository.RecordAuditEventAsync(
            context.TenantId,
            context.CompanyId,
            context.UserId,
            "USER_INVITED",
            "user",
            user.UserId,
            new { command.Email, command.DisplayName, command.RoleCode },
            ct);

        return user;
    }

    public Task<IReadOnlyList<RoleDetail>> ListRolesAsync(TenantContext context, CancellationToken ct = default) =>
        repository.ListRolesAndPermissionsAsync(context.TenantId, ct);

    public Task<IReadOnlyList<AuditEventSummary>> ListAuditEventsAsync(TenantContext context, int limit = 50, CancellationToken ct = default) =>
        repository.ListAuditEventsAsync(context.TenantId, context.CompanyId, limit, ct);
}
