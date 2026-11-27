using System.Text.RegularExpressions;
using UniversalErp.Domain.Common;

namespace UniversalErp.Application.Finance;

public sealed partial class FinanceService
{
    private static readonly Regex IsoCurrencyRegex = new("^[A-Z]{3}$", RegexOptions.Compiled);
    private static readonly HashSet<string> ValidRateTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "transaction", "average", "closing"
    };

    public Task<IReadOnlyList<CurrencySummary>> ListCurrenciesAsync(CancellationToken cancellationToken) =>
        repository.ListCurrenciesAsync(cancellationToken);

    public Task<CompanyCurrencyContextInfo> GetCompanyCurrencyContextAsync(CancellationToken cancellationToken) =>
        repository.GetCompanyCurrencyContextAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            cancellationToken);

    public Task<IReadOnlyList<ExchangeRateSummary>> ListExchangeRatesAsync(
        string? fromCurrency,
        string? toCurrency,
        string? rateType,
        CancellationToken cancellationToken) =>
        repository.ListExchangeRatesAsync(
            tenantContext.Current.TenantId,
            tenantContext.Current.CompanyId,
            fromCurrency,
            toCurrency,
            rateType,
            cancellationToken);

    public async Task<Result<ExchangeRateSummary>> CreateExchangeRateAsync(
        UpsertExchangeRateRequest request,
        CancellationToken cancellationToken)
    {
        var validated = ValidateRateRequest(request);
        if (!validated.IsSuccess)
        {
            return Result<ExchangeRateSummary>.Failure(validated.Error!);
        }

        try
        {
            var created = await repository.CreateExchangeRateAsync(
                tenantContext.Current.TenantId,
                tenantContext.Current.CompanyId,
                tenantContext.Current.UserId,
                validated.Value!,
                cancellationToken);
            return Result<ExchangeRateSummary>.Success(created);
        }
        catch (ExchangeRateConflictException ex)
        {
            return Result<ExchangeRateSummary>.Failure(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVALID_CURRENCY_CODE")
        {
            return Result<ExchangeRateSummary>.Failure("INVALID_CURRENCY_CODE");
        }
    }

    public async Task<Result<ExchangeRateSummary>> UpdateExchangeRateAsync(
        Guid exchangeRateId,
        UpsertExchangeRateRequest request,
        CancellationToken cancellationToken)
    {
        if (exchangeRateId == Guid.Empty)
        {
            return Result<ExchangeRateSummary>.Failure("INVALID_EXCHANGE_RATE_ID");
        }

        var validated = ValidateRateRequest(request);
        if (!validated.IsSuccess)
        {
            return Result<ExchangeRateSummary>.Failure(validated.Error!);
        }

        try
        {
            var updated = await repository.UpdateExchangeRateAsync(
                tenantContext.Current.TenantId,
                tenantContext.Current.CompanyId,
                tenantContext.Current.UserId,
                exchangeRateId,
                validated.Value!,
                cancellationToken);

            return updated is null
                ? Result<ExchangeRateSummary>.Failure("EXCHANGE_RATE_NOT_FOUND")
                : Result<ExchangeRateSummary>.Success(updated);
        }
        catch (ExchangeRateConflictException ex)
        {
            return Result<ExchangeRateSummary>.Failure(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVALID_CURRENCY_CODE")
        {
            return Result<ExchangeRateSummary>.Failure("INVALID_CURRENCY_CODE");
        }
    }

    public async Task<PresentationFinancialStatementsResponse> GetPresentationFinancialStatementsAsync(
        DateOnly? asOfDate,
        string? incomeRateMethod,
        CancellationToken cancellationToken)
    {
        var effectiveAsOf = asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var tenantId = tenantContext.Current.TenantId;
        var companyId = tenantContext.Current.CompanyId;

        var currencyCtx = await repository.GetCompanyCurrencyContextAsync(tenantId, companyId, cancellationToken);
        var accounts = await repository.ListAccountsAsync(tenantId, companyId, cancellationToken);
        var lines = await repository.ListPostedJournalLinesForTranslationAsync(tenantId, companyId, effectiveAsOf, cancellationToken);
        var rates = await repository.ListExchangeRatesAsync(tenantId, companyId, null, null, null, cancellationToken);
        var currencies = await repository.ListCurrenciesAsync(cancellationToken);

        var presDecimals = currencies
            .FirstOrDefault(c => string.Equals(c.Code, currencyCtx.PresentationCurrencyCode, StringComparison.OrdinalIgnoreCase))
            ?.MinorUnit ?? 2;

        var rateInputs = rates
            .Select(r => new Ias21ExchangeRateInput(
                r.ExchangeRateId,
                r.FromCurrency,
                r.ToCurrency,
                r.Rate,
                r.EffectiveDate,
                r.RateType,
                r.Source,
                r.CreatedAt))
            .ToList();

        return Ias21TranslationEngine.BuildPresentationStatements(
            currencyContext: currencyCtx,
            asOfDate: effectiveAsOf,
            accounts: accounts,
            postedLines: lines,
            exchangeRates: rateInputs,
            incomeRateMethod: string.IsNullOrWhiteSpace(incomeRateMethod)
                ? Ias21TranslationEngine.IncomeRateMethodTransaction
                : incomeRateMethod.Trim(),
            presentationDecimals: presDecimals);
    }

    private static Result<UpsertExchangeRateRequest> ValidateRateRequest(UpsertExchangeRateRequest request)
    {
        var from = (request.FromCurrency ?? string.Empty).Trim().ToUpperInvariant();
        var to = (request.ToCurrency ?? string.Empty).Trim().ToUpperInvariant();

        if (!IsoCurrencyRegex.IsMatch(from) || !IsoCurrencyRegex.IsMatch(to))
        {
            return Result<UpsertExchangeRateRequest>.Failure("INVALID_CURRENCY_CODE");
        }

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return Result<UpsertExchangeRateRequest>.Failure("SAME_CURRENCY_PAIR_NOT_ALLOWED");
        }

        if (request.Rate <= 0m)
        {
            return Result<UpsertExchangeRateRequest>.Failure("INVALID_EXCHANGE_RATE");
        }

        if (request.EffectiveDate == default)
        {
            return Result<UpsertExchangeRateRequest>.Failure("EFFECTIVE_DATE_REQUIRED");
        }

        var rateType = (request.RateType ?? string.Empty).Trim().ToLowerInvariant();
        if (!ValidRateTypes.Contains(rateType))
        {
            return Result<UpsertExchangeRateRequest>.Failure("INVALID_RATE_TYPE");
        }

        var source = string.IsNullOrWhiteSpace(request.Source) ? "manual" : request.Source.Trim();
        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();

        return Result<UpsertExchangeRateRequest>.Success(new UpsertExchangeRateRequest(
            FromCurrency: from,
            ToCurrency: to,
            Rate: request.Rate,
            EffectiveDate: request.EffectiveDate,
            RateType: rateType,
            Source: source,
            Reference: reference));
    }
}
