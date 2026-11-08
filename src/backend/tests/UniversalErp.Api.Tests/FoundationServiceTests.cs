using System.Text.Json;
using UniversalErp.Application.Foundation;
using UniversalErp.Domain.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class FoundationServiceTests
{
    private sealed class FakeFoundationRepository : IFoundationRepository
    {
        public UserProfileResponse? Profile;
        public CompanySettingsResponse? Settings;
        public readonly List<CompanyUserSummary> Users = [];
        public readonly List<RoleDetail> Roles = [];
        public readonly List<AuditEventSummary> AuditEvents = [];
        public readonly List<(Guid TenantId, Guid CompanyId, Guid ActorId, string Code, string Type, Guid? EntityId, object? Meta)> RecordedAudits = [];

        public Task<UserProfileResponse?> GetUserProfileAsync(Guid tenantId, Guid companyId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Profile);

        public Task<CompanySettingsResponse?> GetCompanySettingsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(Settings);

        public Task<CompanySettingsResponse?> UpdateCompanySettingsAsync(Guid tenantId, Guid companyId, UpdateCompanySettingsCommand command, CancellationToken ct = default)
        {
            if (Settings is null) return Task.FromResult<CompanySettingsResponse?>(null);
            Settings = Settings with
            {
                Name = command.Name ?? Settings.Name,
                LegalName = command.LegalName ?? Settings.LegalName,
                RegistrationNumber = command.RegistrationNumber ?? Settings.RegistrationNumber,
                Timezone = command.Timezone ?? Settings.Timezone,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return Task.FromResult<CompanySettingsResponse?>(Settings);
        }

        public Task<IReadOnlyList<CompanyUserSummary>> ListCompanyUsersAsync(Guid tenantId, Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CompanyUserSummary>>(Users);

        public Task<CompanyUserSummary?> InviteCompanyUserAsync(Guid tenantId, Guid companyId, InviteCompanyUserCommand command, CancellationToken ct = default)
        {
            var user = new CompanyUserSummary(
                Guid.NewGuid(),
                command.Email,
                command.DisplayName,
                "active",
                string.IsNullOrWhiteSpace(command.RoleCode) ? [] : [new(command.RoleCode, command.RoleCode)],
                DateTimeOffset.UtcNow);
            Users.Add(user);
            return Task.FromResult<CompanyUserSummary?>(user);
        }

        public Task<IReadOnlyList<RoleDetail>> ListRolesAndPermissionsAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RoleDetail>>(Roles);

        public Task<IReadOnlyList<AuditEventSummary>> ListAuditEventsAsync(Guid tenantId, Guid companyId, int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuditEventSummary>>(AuditEvents);

        public Task<Guid> RecordAuditEventAsync(Guid tenantId, Guid companyId, Guid actorUserId, string eventCode, string entityType, Guid? entityId, object? metadata = null, CancellationToken ct = default)
        {
            var id = Guid.NewGuid();
            RecordedAudits.Add((tenantId, companyId, actorUserId, eventCode, entityType, entityId, metadata));
            AuditEvents.Add(new(id, DateTimeOffset.UtcNow, "actor@example.com", "Actor", eventCode, entityType, entityId, JsonSerializer.SerializeToElement(metadata ?? new { })));
            return Task.FromResult(id);
        }
    }

    private readonly FakeFoundationRepository _repo = new();
    private readonly FoundationService _service;
    private readonly TenantContext _context = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    public FoundationServiceTests()
    {
        _service = new FoundationService(_repo);
    }

    [Fact]
    public async Task GetProfileAsync_ReturnsUserProfile_WhenExists()
    {
        _repo.Profile = new UserProfileResponse(
            _context.UserId,
            "test@example.com",
            "Test User",
            "active",
            DateTimeOffset.UtcNow,
            _context.TenantId,
            "TENANT-1",
            "Tenant 1",
            _context.CompanyId,
            "COMP-1",
            "Company 1",
            "Legal Co",
            "REG-123",
            "PKR",
            "Asia/Karachi",
            [new("admin", "Administrator")],
            ["inventory.product.view", "sales.order.view"]);

        var profile = await _service.GetProfileAsync(_context);

        Assert.NotNull(profile);
        Assert.Equal("test@example.com", profile.Email);
        Assert.Equal("Tenant 1", profile.TenantName);
        Assert.Single(profile.Roles);
        Assert.Equal(2, profile.Permissions.Count);
    }

    [Fact]
    public async Task GetProfileAsync_ThrowsKeyNotFound_WhenMissing()
    {
        _repo.Profile = null;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetProfileAsync(_context));
    }

    [Fact]
    public async Task GetSettingsAsync_ReturnsCompanySettings()
    {
        _repo.Settings = new CompanySettingsResponse(
            _context.CompanyId,
            _context.TenantId,
            "COMP-1",
            "Acme Corp",
            "Acme Legal",
            "REG-999",
            "PK",
            "PKR",
            "Asia/Karachi",
            7,
            "active",
            DateTimeOffset.UtcNow);

        var settings = await _service.GetSettingsAsync(_context);

        Assert.NotNull(settings);
        Assert.Equal("Acme Corp", settings.Name);
        Assert.Equal("REG-999", settings.RegistrationNumber);
    }

    [Fact]
    public async Task UpdateSettingsAsync_UpdatesAndRecordsAudit()
    {
        _repo.Settings = new CompanySettingsResponse(
            _context.CompanyId,
            _context.TenantId,
            "COMP-1",
            "Old Name",
            null,
            null,
            "PK",
            "PKR",
            "UTC",
            1,
            "active",
            null);

        var updated = await _service.UpdateSettingsAsync(_context, new UpdateCompanySettingsCommand("New Name", "New Legal", "NEW-REG", "Asia/Karachi"));

        Assert.Equal("New Name", updated.Name);
        Assert.Equal("New Legal", updated.LegalName);
        Assert.Single(_repo.RecordedAudits);
        Assert.Equal("ORGANIZATION_SETTINGS_UPDATED", _repo.RecordedAudits[0].Code);
    }

    [Fact]
    public async Task ListUsersAsync_ReturnsCompanyUsers()
    {
        _repo.Users.Add(new CompanyUserSummary(Guid.NewGuid(), "u1@example.com", "User 1", "active", [new("admin", "Admin")], DateTimeOffset.UtcNow));
        _repo.Users.Add(new CompanyUserSummary(Guid.NewGuid(), "u2@example.com", "User 2", "active", [], DateTimeOffset.UtcNow));

        var users = await _service.ListUsersAsync(_context);

        Assert.Equal(2, users.Count);
        Assert.Equal("u1@example.com", users[0].Email);
    }

    [Fact]
    public async Task InviteUserAsync_ValidCommand_CreatesUserAndRecordsAudit()
    {
        var result = await _service.InviteUserAsync(_context, new InviteCompanyUserCommand("colleague@example.com", "Colleague", "cashier"));

        Assert.NotNull(result);
        Assert.Equal("colleague@example.com", result.Email);
        Assert.Single(_repo.Users);
        Assert.Single(_repo.RecordedAudits);
        Assert.Equal("USER_INVITED", _repo.RecordedAudits[0].Code);
    }

    [Fact]
    public async Task InviteUserAsync_InvalidEmail_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.InviteUserAsync(_context, new InviteCompanyUserCommand("invalid-email", "Bad Email", "admin")));
    }

    [Fact]
    public async Task ListRolesAsync_ReturnsSystemRolesAndPermissions()
    {
        _repo.Roles.Add(new RoleDetail(
            Guid.NewGuid(),
            "admin",
            "Administrator",
            true,
            [new("all", "All permissions", "system")]));

        var roles = await _service.ListRolesAsync(_context);

        Assert.Single(roles);
        Assert.Equal("admin", roles[0].RoleCode);
        Assert.True(roles[0].IsSystemRole);
    }

    [Fact]
    public async Task ListAuditEventsAsync_ReturnsAuditTrail()
    {
        await _repo.RecordAuditEventAsync(_context.TenantId, _context.CompanyId, _context.UserId, "SECURITY_LOGIN", "user", _context.UserId);

        var events = await _service.ListAuditEventsAsync(_context);

        Assert.Single(events);
        Assert.Equal("SECURITY_LOGIN", events[0].EventCode);
    }
}
