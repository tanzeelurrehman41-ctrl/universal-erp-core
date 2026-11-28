using System.Text.RegularExpressions;
using UniversalErp.Domain.Common;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Application.Foundation;

public sealed partial class FoundationService(IFoundationRepository repository)
{
    private static readonly Regex IsoCurrencyPattern = new("^[A-Z]{3}$", RegexOptions.Compiled);

    public const string FunctionalCurrencyGuidanceText =
        "Functional (Accounting) Currency is the currency of the primary economic environment in which the company operates and keeps its general ledger, FIFO inventory cost layers, and posted journals (IAS 21.8).";

    public const string PresentationCurrencyGuidanceText =
        "Presentation (Reporting) Currency is the currency in which financial statements and executive summaries are presented (IAS 21.38). Assets and liabilities translate at closing rates, income and expenses at transaction-date rates, and differences go to OCI without rewriting functional-currency ledger records.";

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

    public async Task<CompanyCurrencySettingsResponse?> GetCurrencySettingsAsync(TenantContext context, CancellationToken ct = default)
    {
        var settings = await repository.GetCompanySettingsAsync(context.TenantId, context.CompanyId, ct);
        if (settings is null) return null;

        var func = (settings.FunctionalCurrencyCode ?? settings.BaseCurrencyCode ?? settings.CurrencyCode).Trim().ToUpperInvariant();
        var pres = (settings.PresentationCurrencyCode ?? func).Trim().ToUpperInvariant();
        var effectiveDate = settings.CurrencyEffectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        return new CompanyCurrencySettingsResponse(
            CompanyId: settings.CompanyId,
            TenantId: settings.TenantId,
            CompanyCode: settings.CompanyCode,
            CompanyName: settings.Name,
            FunctionalCurrencyCode: func,
            PresentationCurrencyCode: pres,
            CurrencyEffectiveDate: effectiveDate,
            HasPostedTransactions: settings.HasPostedTransactions,
            UpdatedAt: settings.UpdatedAt,
            FunctionalCurrencyGuidance: FunctionalCurrencyGuidanceText,
            PresentationCurrencyGuidance: PresentationCurrencyGuidanceText);
    }

    public async Task<Result<CompanyCurrencySettingsResponse>> UpdateCurrencySettingsAsync(
        TenantContext context,
        UpdateCompanyCurrencySettingsCommand command,
        CancellationToken ct = default)
    {
        var functionalCurrency = (command.FunctionalCurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        var presentationCurrency = (command.PresentationCurrencyCode ?? string.Empty).Trim().ToUpperInvariant();

        if (!IsoCurrencyPattern.IsMatch(functionalCurrency))
        {
            return Result<CompanyCurrencySettingsResponse>.Failure("INVALID_FUNCTIONAL_CURRENCY");
        }

        if (!IsoCurrencyPattern.IsMatch(presentationCurrency))
        {
            return Result<CompanyCurrencySettingsResponse>.Failure("INVALID_PRESENTATION_CURRENCY");
        }

        if (command.EffectiveDate == default)
        {
            return Result<CompanyCurrencySettingsResponse>.Failure("EFFECTIVE_DATE_REQUIRED");
        }

        var existing = await repository.GetCompanySettingsAsync(context.TenantId, context.CompanyId, ct);
        if (existing is null)
        {
            return Result<CompanyCurrencySettingsResponse>.Failure("COMPANY_NOT_FOUND");
        }

        var currentFunctional = (existing.FunctionalCurrencyCode ?? existing.BaseCurrencyCode ?? existing.CurrencyCode)
            .Trim()
            .ToUpperInvariant();

        if (!string.Equals(currentFunctional, functionalCurrency, StringComparison.Ordinal) &&
            !command.ConfirmFutureTransactions)
        {
            return Result<CompanyCurrencySettingsResponse>.Failure("CONFIRMATION_REQUIRED_FOR_FUTURE_TRANSACTIONS");
        }

        var normalized = new UpdateCompanyCurrencySettingsCommand(
            FunctionalCurrencyCode: functionalCurrency,
            PresentationCurrencyCode: presentationCurrency,
            EffectiveDate: command.EffectiveDate,
            ConfirmFutureTransactions: command.ConfirmFutureTransactions,
            ChangeReason: string.IsNullOrWhiteSpace(command.ChangeReason) ? null : command.ChangeReason.Trim());

        try
        {
            var updated = await repository.UpdateCompanyCurrencySettingsAsync(
                context.TenantId,
                context.CompanyId,
                context.UserId,
                normalized,
                ct);

            return updated is null
                ? Result<CompanyCurrencySettingsResponse>.Failure("COMPANY_NOT_FOUND")
                : Result<CompanyCurrencySettingsResponse>.Success(updated);
        }
        catch (InvalidOperationException ex) when (ex.Message is
            "INVALID_FUNCTIONAL_CURRENCY" or
            "INVALID_PRESENTATION_CURRENCY" or
            "EFFECTIVE_DATE_REQUIRED" or
            "EFFECTIVE_DATE_MUST_NOT_BE_IN_THE_PAST" or
            "COMPANY_NOT_FOUND" or
            "CONFIRMATION_REQUIRED_FOR_FUTURE_TRANSACTIONS")
        {
            return Result<CompanyCurrencySettingsResponse>.Failure(ex.Message);
        }
    }

    public Task<IReadOnlyList<CompanyCurrencyHistoryItem>> ListCurrencyHistoryAsync(
        TenantContext context,
        int limit = 50,
        CancellationToken ct = default) =>
        repository.ListCompanyCurrencyHistoryAsync(context.TenantId, context.CompanyId, limit, ct);

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
