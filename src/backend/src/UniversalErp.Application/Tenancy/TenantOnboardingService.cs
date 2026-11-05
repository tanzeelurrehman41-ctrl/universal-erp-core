using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UniversalErp.Domain.Common;

namespace UniversalErp.Application.Tenancy;

public sealed class TenantOnboardingService(ITenantOnboardingRepository repository)
{
    private static readonly Regex TenantCodePattern = new(
        @"^[a-z0-9][a-z0-9-]{1,48}[a-z0-9]$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Task<IReadOnlyList<SubscriptionPlanDetail>> ListSubscriptionPlansAsync(
        CancellationToken cancellationToken) =>
        repository.ListSubscriptionPlansAsync(cancellationToken);

    public Task<TenantDetail?> GetTenantByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken) =>
        repository.GetTenantByIdAsync(tenantId, cancellationToken);

    public Task<TenantDetail?> GetTenantByCodeAsync(
        string tenantCode,
        CancellationToken cancellationToken)
    {
        var normalizedCode = tenantCode.Trim().ToLowerInvariant();
        return repository.GetTenantByCodeAsync(normalizedCode, cancellationToken);
    }

    public async Task<Result<TenantDetail>> RegisterTenantAsync(
        RegisterTenantRequest request,
        Guid? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is null || idempotencyKey == Guid.Empty)
        {
            return Result<TenantDetail>.Failure("INVALID_IDEMPOTENCY_KEY");
        }

        var tenantCode = request.TenantCode?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(tenantCode) || !TenantCodePattern.IsMatch(tenantCode))
        {
            return Result<TenantDetail>.Failure("INVALID_TENANT_CODE");
        }

        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrEmpty(displayName) || displayName.Length < 2 || displayName.Length > 200)
        {
            return Result<TenantDetail>.Failure("INVALID_DISPLAY_NAME");
        }

        var planCode = string.IsNullOrWhiteSpace(request.PlanCode)
            ? "starter"
            : request.PlanCode.Trim().ToLowerInvariant();

        var adminEmail = request.AdminEmail?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(adminEmail) || !IsValidEmail(adminEmail))
        {
            return Result<TenantDetail>.Failure("INVALID_ADMIN_EMAIL");
        }

        var adminFullName = request.AdminFullName?.Trim();
        if (string.IsNullOrEmpty(adminFullName) || adminFullName.Length < 2 || adminFullName.Length > 200)
        {
            return Result<TenantDetail>.Failure("INVALID_ADMIN_NAME");
        }

        var companyName = string.IsNullOrWhiteSpace(request.CompanyName)
            ? displayName
            : request.CompanyName.Trim();

        var companyCode = string.IsNullOrWhiteSpace(request.CompanyCode)
            ? "MAIN"
            : request.CompanyCode.Trim().ToUpperInvariant();

        var countryCode = string.IsNullOrWhiteSpace(request.CountryCode)
            ? "PK"
            : request.CountryCode.Trim().ToUpperInvariant();

        var currencyCode = string.IsNullOrWhiteSpace(request.BaseCurrencyCode)
            ? "PKR"
            : request.BaseCurrencyCode.Trim().ToUpperInvariant();

        var timezone = string.IsNullOrWhiteSpace(request.Timezone)
            ? "Asia/Karachi"
            : request.Timezone.Trim();

        var normalizedRequest = new RegisterTenantRequest(
            tenantCode,
            displayName,
            string.IsNullOrWhiteSpace(request.LegalName) ? null : request.LegalName.Trim(),
            planCode,
            companyName,
            companyCode,
            string.IsNullOrWhiteSpace(request.RegistrationNumber) ? null : request.RegistrationNumber.Trim(),
            countryCode,
            currencyCode,
            timezone,
            adminEmail,
            adminFullName,
            string.IsNullOrWhiteSpace(request.AdminPhone) ? null : request.AdminPhone.Trim());

        var canonicalPayload = JsonSerializer.Serialize(new
        {
            tenantCode,
            displayName,
            legalName = normalizedRequest.LegalName,
            planCode,
            companyName,
            companyCode,
            registrationNumber = normalizedRequest.RegistrationNumber,
            countryCode,
            currencyCode,
            timezone,
            adminEmail,
            adminFullName,
            adminPhone = normalizedRequest.AdminPhone,
        });

        var requestHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));

        try
        {
            var tenant = await repository.RegisterTenantAsync(
                idempotencyKey.Value,
                requestHash,
                normalizedRequest,
                cancellationToken);

            return Result<TenantDetail>.Success(tenant);
        }
        catch (TenantCodeConflictException)
        {
            return Result<TenantDetail>.Failure("TENANT_CODE_EXISTS");
        }
        catch (UserEmailConflictException)
        {
            return Result<TenantDetail>.Failure("USER_EMAIL_EXISTS");
        }
        catch (TenantIdempotencyConflictException)
        {
            return Result<TenantDetail>.Failure("IDEMPOTENCY_KEY_REUSED");
        }
        catch (InvalidPlanException)
        {
            return Result<TenantDetail>.Failure("INVALID_PLAN_CODE");
        }
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new MailAddress(email);
            if (!string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var hostParts = address.Host.Split('.');
            return hostParts.Length >= 2 && hostParts.All(p => !string.IsNullOrWhiteSpace(p));
        }
        catch
        {
            return false;
        }
    }
}
