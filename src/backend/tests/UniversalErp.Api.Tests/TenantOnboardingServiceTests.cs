using UniversalErp.Application.Tenancy;
using Xunit;

namespace UniversalErp.Api.Tests;

public sealed class TenantOnboardingServiceTests
{
    private sealed class FakeTenantOnboardingRepository : ITenantOnboardingRepository
    {
        public readonly List<TenantDetail> Tenants = [];
        public readonly List<(Guid Key, string Hash, RegisterTenantRequest Req)> Registrations = [];
        public bool SimulateDuplicateTenantCode;
        public bool SimulateDuplicateUserEmail;
        public bool SimulateIdempotencyConflict;
        public bool SimulateInvalidPlan;

        public Task<IReadOnlyList<SubscriptionPlanDetail>> ListSubscriptionPlansAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyList<SubscriptionPlanDetail> plans =
            [
                new(
                    Guid.NewGuid(),
                    "starter",
                    "Starter Plan",
                    "Starter description",
                    "active",
                    [
                        new("max_companies", true, 1, "companies"),
                        new("catalog.core", true, null, null)
                    ]),
                new(
                    Guid.NewGuid(),
                    "professional",
                    "Professional Plan",
                    "Pro description",
                    "active",
                    [
                        new("max_companies", true, 5, "companies"),
                        new("inventory.advanced", true, null, null)
                    ])
            ];
            return Task.FromResult(plans);
        }

        public Task<SubscriptionPlanDetail?> GetPlanByCodeAsync(
            string planCode,
            CancellationToken cancellationToken)
        {
            if (SimulateInvalidPlan || planCode == "non_existent")
            {
                return Task.FromResult<SubscriptionPlanDetail?>(null);
            }

            return Task.FromResult<SubscriptionPlanDetail?>(new(
                Guid.NewGuid(),
                planCode,
                $"{planCode} Plan",
                "Description",
                "active",
                []));
        }

        public Task<TenantDetail?> GetTenantByIdAsync(
            Guid tenantId,
            CancellationToken cancellationToken)
        {
            var found = Tenants.FirstOrDefault(t => t.TenantId == tenantId);
            return Task.FromResult(found);
        }

        public Task<TenantDetail?> GetTenantByCodeAsync(
            string tenantCode,
            CancellationToken cancellationToken)
        {
            var found = Tenants.FirstOrDefault(t =>
                string.Equals(t.TenantCode, tenantCode, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(found);
        }

        public Task<TenantDetail> RegisterTenantAsync(
            Guid idempotencyKey,
            string requestHash,
            RegisterTenantRequest request,
            CancellationToken cancellationToken)
        {
            if (SimulateIdempotencyConflict)
            {
                throw new TenantIdempotencyConflictException();
            }

            if (SimulateDuplicateTenantCode)
            {
                throw new TenantCodeConflictException();
            }

            if (SimulateDuplicateUserEmail)
            {
                throw new UserEmailConflictException();
            }

            if (SimulateInvalidPlan)
            {
                throw new InvalidPlanException(request.PlanCode ?? "unknown");
            }

            Registrations.Add((idempotencyKey, requestHash, request));

            var tenantId = Guid.NewGuid();
            var detail = new TenantDetail(
                tenantId,
                request.TenantCode!,
                request.DisplayName!,
                request.LegalName,
                "active",
                request.CountryCode ?? "PK",
                request.BaseCurrencyCode ?? "PKR",
                request.Timezone ?? "Asia/Karachi",
                DateTimeOffset.UtcNow,
                new InitialCompanyDetail(
                    Guid.NewGuid(),
                    request.CompanyCode ?? "MAIN",
                    request.CompanyName ?? request.DisplayName!,
                    request.LegalName,
                    request.RegistrationNumber,
                    request.CountryCode ?? "PK",
                    request.BaseCurrencyCode ?? "PKR",
                    request.Timezone ?? "Asia/Karachi",
                    "active"),
                new TenantSubscriptionDetail(
                    Guid.NewGuid(),
                    request.PlanCode ?? "starter",
                    "Starter Plan",
                    "active",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddDays(30)),
                [
                    new("max_companies", true, 1, "companies"),
                    new("catalog.core", true, null, null)
                ],
                new TenantAdminUserDetail(
                    Guid.NewGuid(),
                    request.AdminEmail!,
                    request.AdminFullName!,
                    "active"));

            Tenants.Add(detail);
            return Task.FromResult(detail);
        }
    }

    [Fact]
    public async Task RegisterTenant_Requires_Valid_Idempotency_Key()
    {
        var repo = new FakeTenantOnboardingRepository();
        var service = new TenantOnboardingService(repo);

        var request = new RegisterTenantRequest(
            "karachi-traders",
            "Karachi Traders",
            null,
            "starter",
            null,
            null,
            null,
            null,
            null,
            null,
            "owner@karachitraders.pk",
            "Ahmed Ali",
            null);

        var emptyKeyResult = await service.RegisterTenantAsync(request, Guid.Empty, CancellationToken.None);
        Assert.False(emptyKeyResult.IsSuccess);
        Assert.Equal("INVALID_IDEMPOTENCY_KEY", emptyKeyResult.Error);

        var nullKeyResult = await service.RegisterTenantAsync(request, null, CancellationToken.None);
        Assert.False(nullKeyResult.IsSuccess);
        Assert.Equal("INVALID_IDEMPOTENCY_KEY", nullKeyResult.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData("-invalid")]
    [InlineData("invalid-")]
    [InlineData("UPPER_CASE")]
    [InlineData("spaces in code")]
    [InlineData("has_underscores")]
    public async Task RegisterTenant_Rejects_Invalid_Tenant_Codes(string invalidCode)
    {
        var repo = new FakeTenantOnboardingRepository();
        var service = new TenantOnboardingService(repo);

        var request = new RegisterTenantRequest(
            invalidCode,
            "Karachi Traders",
            null,
            "starter",
            null,
            null,
            null,
            null,
            null,
            null,
            "owner@karachitraders.pk",
            "Ahmed Ali",
            null);

        var result = await service.RegisterTenantAsync(request, Guid.NewGuid(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_TENANT_CODE", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-an-email")]
    [InlineData("missing@domain")]
    public async Task RegisterTenant_Rejects_Invalid_Admin_Email(string invalidEmail)
    {
        var repo = new FakeTenantOnboardingRepository();
        var service = new TenantOnboardingService(repo);

        var request = new RegisterTenantRequest(
            "valid-code",
            "Valid Company",
            null,
            "starter",
            null,
            null,
            null,
            null,
            null,
            null,
            invalidEmail,
            "Ahmed Ali",
            null);

        var result = await service.RegisterTenantAsync(request, Guid.NewGuid(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_ADMIN_EMAIL", result.Error);
    }

    [Fact]
    public async Task RegisterTenant_Normalizes_Fields_And_Defaults_PKR()
    {
        var repo = new FakeTenantOnboardingRepository();
        var service = new TenantOnboardingService(repo);

        var idempotencyKey = Guid.NewGuid();
        var request = new RegisterTenantRequest(
            "APEX-LOGISTICS",
            "Apex Logistics Pakistan",
            "Apex Logistics Pvt Ltd",
            "STARTER",
            null,
            null,
            "NTN-1234567-8",
            null,
            null,
            null,
            "Admin@ApexLogistics.PK",
            "Zubair Tariq",
            "+923001234567");

        var result = await service.RegisterTenantAsync(request, idempotencyKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var tenant = result.Value!;

        Assert.Equal("apex-logistics", tenant.TenantCode);
        Assert.Equal("Apex Logistics Pakistan", tenant.DisplayName);
        Assert.Equal("PK", tenant.DefaultCountryCode);
        Assert.Equal("PKR", tenant.DefaultCurrencyCode);
        Assert.Equal("Asia/Karachi", tenant.DefaultTimezone);
        Assert.Equal("Apex Logistics Pakistan", tenant.Company.Name);
        Assert.Equal("MAIN", tenant.Company.CompanyCode);
        Assert.Equal("PKR", tenant.Company.BaseCurrencyCode);
        Assert.Equal("admin@apexlogistics.pk", tenant.AdminUser.Email);

        Assert.Single(repo.Registrations);
        var registered = repo.Registrations[0];
        Assert.Equal(idempotencyKey, registered.Key);
        Assert.False(string.IsNullOrWhiteSpace(registered.Hash));
    }

    [Fact]
    public async Task RegisterTenant_Returns_Conflict_When_TenantCode_Exists()
    {
        var repo = new FakeTenantOnboardingRepository { SimulateDuplicateTenantCode = true };
        var service = new TenantOnboardingService(repo);

        var request = new RegisterTenantRequest(
            "existing-tenant",
            "Existing Tenant",
            null,
            "starter",
            null,
            null,
            null,
            null,
            null,
            null,
            "admin@existing.pk",
            "Admin User",
            null);

        var result = await service.RegisterTenantAsync(request, Guid.NewGuid(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("TENANT_CODE_EXISTS", result.Error);
    }

    [Fact]
    public async Task RegisterTenant_Returns_Conflict_When_UserEmail_Exists()
    {
        var repo = new FakeTenantOnboardingRepository { SimulateDuplicateUserEmail = true };
        var service = new TenantOnboardingService(repo);

        var request = new RegisterTenantRequest(
            "new-tenant",
            "New Tenant",
            null,
            "starter",
            null,
            null,
            null,
            null,
            null,
            null,
            "existing@user.pk",
            "Existing User",
            null);

        var result = await service.RegisterTenantAsync(request, Guid.NewGuid(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("USER_EMAIL_EXISTS", result.Error);
    }

    [Fact]
    public async Task RegisterTenant_Returns_Conflict_On_Idempotency_Collision()
    {
        var repo = new FakeTenantOnboardingRepository { SimulateIdempotencyConflict = true };
        var service = new TenantOnboardingService(repo);

        var request = new RegisterTenantRequest(
            "tenant-replay",
            "Tenant Replay",
            null,
            "starter",
            null,
            null,
            null,
            null,
            null,
            null,
            "admin@replay.pk",
            "Replay Admin",
            null);

        var result = await service.RegisterTenantAsync(request, Guid.NewGuid(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", result.Error);
    }

    [Fact]
    public async Task RegisterTenant_Returns_InvalidPlanCode_When_Plan_Not_Found()
    {
        var repo = new FakeTenantOnboardingRepository { SimulateInvalidPlan = true };
        var service = new TenantOnboardingService(repo);

        var request = new RegisterTenantRequest(
            "tenant-plan",
            "Tenant Plan",
            null,
            "nonexistent_plan",
            null,
            null,
            null,
            null,
            null,
            null,
            "admin@plan.pk",
            "Plan Admin",
            null);

        var result = await service.RegisterTenantAsync(request, Guid.NewGuid(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_PLAN_CODE", result.Error);
    }
}
