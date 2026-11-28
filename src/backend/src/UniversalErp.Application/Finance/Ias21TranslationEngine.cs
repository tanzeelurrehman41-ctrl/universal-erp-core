namespace UniversalErp.Application.Finance;

/// <summary>
/// Pure IAS 21 presentation-currency translation engine.
/// Translates assets and liabilities at the closing rate as of the reporting date,
/// income and expenses at transaction-date rates (or explicit average rate when opted in),
/// historical equity at transaction-date rates, and computes the resulting Foreign Currency
/// Translation Difference in Other Comprehensive Income (OCI).
/// Never assumes an exchange rate of 1.0 across distinct currencies and never fabricates missing rates.
/// </summary>
public static class Ias21TranslationEngine
{
    public const string IncomeRateMethodTransaction = "transaction";
    public const string IncomeRateMethodAverage = "average";

    private sealed record ResolvedRate(
        Guid ExchangeRateId,
        decimal RawRate,
        bool IsInverse,
        decimal EffectiveMultiplier,
        DateOnly EffectiveDate,
        string RateType,
        string Source);

    public static PresentationFinancialStatementsResponse BuildPresentationStatements(
        CompanyCurrencyContextInfo currencyContext,
        DateOnly asOfDate,
        IReadOnlyList<AccountSummary> accounts,
        IReadOnlyList<Ias21PostedJournalLineInput> postedLines,
        IReadOnlyList<Ias21ExchangeRateInput> exchangeRates,
        string incomeRateMethod = IncomeRateMethodTransaction,
        int presentationDecimals = 2)
    {
        var functionalCurrency = (currencyContext.FunctionalCurrencyCode ?? "PKR").Trim().ToUpperInvariant();
        var presentationCurrency = string.IsNullOrWhiteSpace(currencyContext.PresentationCurrencyCode)
            ? functionalCurrency
            : currencyContext.PresentationCurrencyCode.Trim().ToUpperInvariant();
        var normalizedIncomeMethod = string.Equals(incomeRateMethod?.Trim(), IncomeRateMethodAverage, StringComparison.OrdinalIgnoreCase)
            ? IncomeRateMethodAverage
            : IncomeRateMethodTransaction;

        bool sameCurrency = string.Equals(functionalCurrency, presentationCurrency, StringComparison.Ordinal);
        string methodDescription = sameCurrency
            ? $"Same Functional & Presentation Currency ({functionalCurrency})"
            : normalizedIncomeMethod == IncomeRateMethodAverage
                ? "IAS 21.39/40 (Assets/Liabilities at Closing Rate; Income/Expenses at Explicit Average Rate; Equity at Historical Rate; Difference to OCI)"
                : "IAS 21.39 (Assets/Liabilities at Closing Rate; Income/Expenses & Equity at Transaction-Date Rates; Difference to OCI)";

        // Filter lines up to asOfDate
        var scopedLines = postedLines
            .Where(l => l.JournalDate <= asOfDate)
            .ToList();

        var linesByAccount = scopedLines
            .GroupBy(l => l.AccountId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var missingDiagnostics = new List<MissingExchangeRateDiagnostic>();
        var missingKeys = new HashSet<string>(StringComparer.Ordinal);

        void AddMissing(string requiredFor, string fromCcy, string toCcy, string rateType, DateOnly reqDate, string reason)
        {
            var key = $"{fromCcy}:{toCcy}:{rateType}:{reqDate:yyyy-MM-dd}";
            if (missingKeys.Add(key))
            {
                missingDiagnostics.Add(new MissingExchangeRateDiagnostic(
                    RequiredFor: requiredFor,
                    CurrencyPair: $"{fromCcy}/{toCcy}",
                    RateType: rateType,
                    RequiredDate: reqDate,
                    Reason: reason));
            }
        }

        // Resolve closing rate for functionalCurrency <-> presentationCurrency on or before asOfDate
        ResolvedRate? closingRate = sameCurrency
            ? new ResolvedRate(Guid.Empty, 1m, false, 1m, asOfDate, "closing", "same_currency")
            : ResolveRate(functionalCurrency, presentationCurrency, asOfDate, "closing", exchangeRates);

        ResolvedRate? explicitAverageRate = (!sameCurrency && normalizedIncomeMethod == IncomeRateMethodAverage)
            ? ResolveRate(functionalCurrency, presentationCurrency, asOfDate, "average", exchangeRates)
            : null;

        var balanceSheetLines = new List<PresentationStatementLine>();
        var incomeStatementLines = new List<PresentationStatementLine>();

        decimal totalAssetsFunc = 0m;
        decimal totalLiabFunc = 0m;
        decimal totalEquityFunc = 0m;
        decimal totalRevFunc = 0m;
        decimal totalCogsFunc = 0m;
        decimal totalOpexFunc = 0m;

        bool hasNonZeroAssetOrLiab = false;
        bool hasMissingClosing = false;
        bool hasMissingEquity = false;
        bool hasMissingIncomeOrExpense = false;
        bool hasMixedFunctionalLedger = false;

        decimal totalAssetsPresRaw = 0m;
        decimal totalLiabPresRaw = 0m;
        decimal totalEquityPresRaw = 0m;
        decimal totalRevPresRaw = 0m;
        decimal totalCogsPresRaw = 0m;
        decimal totalOpexPresRaw = 0m;

        foreach (var account in accounts.Where(a => a.IsActive).OrderBy(a => a.AccountCode, StringComparer.Ordinal))
        {
            var accountType = account.AccountType.Trim().ToLowerInvariant();
            bool isDebitNormal = string.Equals(account.NormalBalance, "debit", StringComparison.OrdinalIgnoreCase);

            linesByAccount.TryGetValue(account.AccountId, out var accLines);
            accLines ??= [];

            decimal funcBalance = 0m;
            foreach (var line in accLines)
            {
                var lineFuncCurrency = string.IsNullOrWhiteSpace(line.JournalFunctionalCurrency)
                    ? functionalCurrency
                    : line.JournalFunctionalCurrency.Trim().ToUpperInvariant();

                if (!string.Equals(lineFuncCurrency, functionalCurrency, StringComparison.Ordinal))
                {
                    hasMixedFunctionalLedger = true;
                }

                decimal movement = isDebitNormal
                    ? line.DebitFunctional - line.CreditFunctional
                    : line.CreditFunctional - line.DebitFunctional;
                funcBalance += movement;
            }

            switch (accountType)
            {
                case "asset":
                    totalAssetsFunc += funcBalance;
                    break;
                case "liability":
                    totalLiabFunc += funcBalance;
                    break;
                case "equity":
                    totalEquityFunc += funcBalance;
                    break;
                case "revenue":
                    totalRevFunc += funcBalance;
                    break;
                case "expense":
                    if (account.AccountCode.StartsWith("50", StringComparison.Ordinal))
                        totalCogsFunc += funcBalance;
                    else
                        totalOpexFunc += funcBalance;
                    break;
            }

            if (accountType is "asset" or "liability")
            {
                if (accLines.Count > 0 || funcBalance != 0m)
                {
                    hasNonZeroAssetOrLiab = true;
                }

                if (sameCurrency)
                {
                    var translated = Math.Round(funcBalance, presentationDecimals, MidpointRounding.AwayFromZero);
                    if (accountType == "asset") totalAssetsPresRaw += funcBalance;
                    else totalLiabPresRaw += funcBalance;

                    balanceSheetLines.Add(new PresentationStatementLine(
                        AccountCode: account.AccountCode,
                        AccountName: account.AccountName,
                        AccountType: account.AccountType,
                        FunctionalBalance: Math.Round(funcBalance, 4, MidpointRounding.AwayFromZero),
                        PresentationBalance: translated,
                        AppliedRate: 1m,
                        RateEffectiveDate: asOfDate,
                        RateTypeUsed: "same_currency",
                        TranslationStatus: "SAME_CURRENCY"));
                    continue;
                }

                // If posted journal lines used a historical functional currency other than current functionalCurrency,
                // translate each line's recorded currency at closing rate (never silently re-labeling).
                bool lineClosingOk = accLines.Count == 0 ? closingRate is not null : true;
                decimal accPresSum = 0m;
                ResolvedRate? singleRateUsed = accLines.Count == 0 ? closingRate : null;

                foreach (var line in accLines)
                {
                    var lineCcy = string.IsNullOrWhiteSpace(line.JournalFunctionalCurrency)
                        ? functionalCurrency
                        : line.JournalFunctionalCurrency.Trim().ToUpperInvariant();
                    decimal movement = isDebitNormal
                        ? line.DebitFunctional - line.CreditFunctional
                        : line.CreditFunctional - line.DebitFunctional;

                    if (string.Equals(lineCcy, presentationCurrency, StringComparison.Ordinal))
                    {
                        accPresSum += movement;
                        continue;
                    }

                    var lineClosing = string.Equals(lineCcy, functionalCurrency, StringComparison.Ordinal)
                        ? closingRate
                        : ResolveRate(lineCcy, presentationCurrency, asOfDate, "closing", exchangeRates);

                    if (lineClosing is null)
                    {
                        lineClosingOk = false;
                        hasMissingClosing = true;
                        AddMissing(
                            $"Assets & Liabilities ({account.AccountCode} {account.AccountName})",
                            lineCcy,
                            presentationCurrency,
                            "closing",
                            asOfDate,
                            $"Missing closing rate for {lineCcy}/{presentationCurrency} on or before {asOfDate:yyyy-MM-dd}.");
                    }
                    else
                    {
                        singleRateUsed ??= lineClosing;
                        accPresSum += ApplyRate(movement, lineClosing);
                    }
                }

                if (!lineClosingOk)
                {
                    if (funcBalance != 0m || accLines.Count > 0)
                    {
                        hasMissingClosing = true;
                    }
                    if (closingRate is null && funcBalance == 0m && accLines.Count == 0)
                    {
                        // Zero-activity account does not fabricate values, marks unavailable rate cleanly
                    }

                    balanceSheetLines.Add(new PresentationStatementLine(
                        AccountCode: account.AccountCode,
                        AccountName: account.AccountName,
                        AccountType: account.AccountType,
                        FunctionalBalance: Math.Round(funcBalance, 4, MidpointRounding.AwayFromZero),
                        PresentationBalance: null,
                        AppliedRate: null,
                        RateEffectiveDate: null,
                        RateTypeUsed: "closing",
                        TranslationStatus: "MISSING_CLOSING_RATE"));
                }
                else
                {
                    if (accountType == "asset") totalAssetsPresRaw += accPresSum;
                    else totalLiabPresRaw += accPresSum;

                    balanceSheetLines.Add(new PresentationStatementLine(
                        AccountCode: account.AccountCode,
                        AccountName: account.AccountName,
                        AccountType: account.AccountType,
                        FunctionalBalance: Math.Round(funcBalance, 4, MidpointRounding.AwayFromZero),
                        PresentationBalance: Math.Round(accPresSum, presentationDecimals, MidpointRounding.AwayFromZero),
                        AppliedRate: singleRateUsed?.EffectiveMultiplier,
                        RateEffectiveDate: singleRateUsed?.EffectiveDate,
                        RateTypeUsed: "closing",
                        TranslationStatus: "TRANSLATED"));
                }
            }
            else if (accountType is "equity" or "revenue" or "expense")
            {
                if (sameCurrency)
                {
                    if (accountType == "equity") totalEquityPresRaw += funcBalance;
                    else if (accountType == "revenue") totalRevPresRaw += funcBalance;
                    else if (account.AccountCode.StartsWith("50", StringComparison.Ordinal)) totalCogsPresRaw += funcBalance;
                    else totalOpexPresRaw += funcBalance;

                    var lineDto = new PresentationStatementLine(
                        AccountCode: account.AccountCode,
                        AccountName: account.AccountName,
                        AccountType: account.AccountType,
                        FunctionalBalance: Math.Round(funcBalance, 4, MidpointRounding.AwayFromZero),
                        PresentationBalance: Math.Round(funcBalance, presentationDecimals, MidpointRounding.AwayFromZero),
                        AppliedRate: 1m,
                        RateEffectiveDate: asOfDate,
                        RateTypeUsed: "same_currency",
                        TranslationStatus: "SAME_CURRENCY");

                    if (accountType == "equity") balanceSheetLines.Add(lineDto);
                    else incomeStatementLines.Add(lineDto);
                    continue;
                }

                if (accLines.Count == 0)
                {
                    var zeroLine = new PresentationStatementLine(
                        AccountCode: account.AccountCode,
                        AccountName: account.AccountName,
                        AccountType: account.AccountType,
                        FunctionalBalance: 0m,
                        PresentationBalance: 0m,
                        AppliedRate: null,
                        RateEffectiveDate: null,
                        RateTypeUsed: accountType == "equity" ? "transaction" : normalizedIncomeMethod,
                        TranslationStatus: "ZERO_BALANCE");

                    if (accountType == "equity") balanceSheetLines.Add(zeroLine);
                    else incomeStatementLines.Add(zeroLine);
                    continue;
                }

                bool allLinesOk = true;
                decimal accPresSum = 0m;
                decimal? singleRate = null;
                DateOnly? singleDate = null;
                bool multipleRates = false;
                string rateTypeForLine = accountType == "equity" ? IncomeRateMethodTransaction : normalizedIncomeMethod;

                foreach (var line in accLines)
                {
                    var lineCcy = string.IsNullOrWhiteSpace(line.JournalFunctionalCurrency)
                        ? functionalCurrency
                        : line.JournalFunctionalCurrency.Trim().ToUpperInvariant();
                    decimal movement = isDebitNormal
                        ? line.DebitFunctional - line.CreditFunctional
                        : line.CreditFunctional - line.DebitFunctional;

                    if (movement == 0m) continue;

                    if (string.Equals(lineCcy, presentationCurrency, StringComparison.Ordinal))
                    {
                        accPresSum += movement;
                        continue;
                    }

                    ResolvedRate? resolved = null;
                    if (accountType != "equity" && normalizedIncomeMethod == IncomeRateMethodAverage)
                    {
                        resolved = string.Equals(lineCcy, functionalCurrency, StringComparison.Ordinal)
                            ? explicitAverageRate
                            : ResolveRate(lineCcy, presentationCurrency, asOfDate, "average", exchangeRates);
                    }
                    else
                    {
                        resolved = ResolveRate(lineCcy, presentationCurrency, line.JournalDate, "transaction", exchangeRates);
                    }

                    if (resolved is null)
                    {
                        allLinesOk = false;
                        if (accountType == "equity") hasMissingEquity = true;
                        else hasMissingIncomeOrExpense = true;

                        var reqDate = (accountType != "equity" && normalizedIncomeMethod == IncomeRateMethodAverage)
                            ? asOfDate
                            : line.JournalDate;
                        AddMissing(
                            $"{account.AccountCode} {account.AccountName}",
                            lineCcy,
                            presentationCurrency,
                            rateTypeForLine,
                            reqDate,
                            $"Missing {rateTypeForLine} rate for {lineCcy}/{presentationCurrency} on or before {reqDate:yyyy-MM-dd}.");
                    }
                    else
                    {
                        accPresSum += ApplyRate(movement, resolved);
                        if (singleRate is null)
                        {
                            singleRate = resolved.EffectiveMultiplier;
                            singleDate = resolved.EffectiveDate;
                        }
                        else if (singleRate != resolved.EffectiveMultiplier)
                        {
                            multipleRates = true;
                        }
                    }
                }

                if (!allLinesOk)
                {
                    var failDto = new PresentationStatementLine(
                        AccountCode: account.AccountCode,
                        AccountName: account.AccountName,
                        AccountType: account.AccountType,
                        FunctionalBalance: Math.Round(funcBalance, 4, MidpointRounding.AwayFromZero),
                        PresentationBalance: null,
                        AppliedRate: null,
                        RateEffectiveDate: null,
                        RateTypeUsed: rateTypeForLine,
                        TranslationStatus: rateTypeForLine == IncomeRateMethodAverage ? "MISSING_AVERAGE_RATE" : "MISSING_TRANSACTION_RATE");

                    if (accountType == "equity") balanceSheetLines.Add(failDto);
                    else incomeStatementLines.Add(failDto);
                }
                else
                {
                    if (accountType == "equity") totalEquityPresRaw += accPresSum;
                    else if (accountType == "revenue") totalRevPresRaw += accPresSum;
                    else if (account.AccountCode.StartsWith("50", StringComparison.Ordinal)) totalCogsPresRaw += accPresSum;
                    else totalOpexPresRaw += accPresSum;

                    var okDto = new PresentationStatementLine(
                        AccountCode: account.AccountCode,
                        AccountName: account.AccountName,
                        AccountType: account.AccountType,
                        FunctionalBalance: Math.Round(funcBalance, 4, MidpointRounding.AwayFromZero),
                        PresentationBalance: Math.Round(accPresSum, presentationDecimals, MidpointRounding.AwayFromZero),
                        AppliedRate: multipleRates ? null : singleRate,
                        RateEffectiveDate: multipleRates ? null : singleDate,
                        RateTypeUsed: multipleRates ? $"{rateTypeForLine}_weighted" : rateTypeForLine,
                        TranslationStatus: "TRANSLATED");

                    if (accountType == "equity") balanceSheetLines.Add(okDto);
                    else incomeStatementLines.Add(okDto);
                }
            }
        }

        // Functional totals (null when ledger spans multiple historical functional currencies without conversion)
        decimal? totalAssetsFuncOut = hasMixedFunctionalLedger ? null : Math.Round(totalAssetsFunc, 2, MidpointRounding.AwayFromZero);
        decimal? totalLiabFuncOut = hasMixedFunctionalLedger ? null : Math.Round(totalLiabFunc, 2, MidpointRounding.AwayFromZero);
        decimal? totalEquityFuncOut = hasMixedFunctionalLedger ? null : Math.Round(totalEquityFunc, 2, MidpointRounding.AwayFromZero);
        decimal? totalRevFuncOut = hasMixedFunctionalLedger ? null : Math.Round(totalRevFunc, 2, MidpointRounding.AwayFromZero);
        decimal? totalCogsFuncOut = hasMixedFunctionalLedger ? null : Math.Round(totalCogsFunc, 2, MidpointRounding.AwayFromZero);
        decimal? totalOpexFuncOut = hasMixedFunctionalLedger ? null : Math.Round(totalOpexFunc, 2, MidpointRounding.AwayFromZero);
        decimal? totalExpFuncOut = hasMixedFunctionalLedger ? null : Math.Round(totalCogsFunc + totalOpexFunc, 2, MidpointRounding.AwayFromZero);
        decimal? netIncomeFuncOut = hasMixedFunctionalLedger ? null : Math.Round(totalRevFunc - (totalCogsFunc + totalOpexFunc), 2, MidpointRounding.AwayFromZero);

        bool closingResolved = !hasMissingClosing && (!hasNonZeroAssetOrLiab || closingRate is not null || sameCurrency);
        bool incomeResolved = !hasMissingIncomeOrExpense;
        bool equityResolved = !hasMissingEquity;

        decimal? totalAssetsPresOut = closingResolved ? Math.Round(totalAssetsPresRaw, presentationDecimals, MidpointRounding.AwayFromZero) : null;
        decimal? totalLiabPresOut = closingResolved ? Math.Round(totalLiabPresRaw, presentationDecimals, MidpointRounding.AwayFromZero) : null;
        decimal? equityPresOut = equityResolved ? Math.Round(totalEquityPresRaw, presentationDecimals, MidpointRounding.AwayFromZero) : null;
        decimal? revPresOut = incomeResolved ? Math.Round(totalRevPresRaw, presentationDecimals, MidpointRounding.AwayFromZero) : null;
        decimal? cogsPresOut = incomeResolved ? Math.Round(totalCogsPresRaw, presentationDecimals, MidpointRounding.AwayFromZero) : null;
        decimal? opexPresOut = incomeResolved ? Math.Round(totalOpexPresRaw, presentationDecimals, MidpointRounding.AwayFromZero) : null;
        decimal? expPresOut = incomeResolved && cogsPresOut.HasValue && opexPresOut.HasValue
            ? Math.Round(cogsPresOut.Value + opexPresOut.Value, presentationDecimals, MidpointRounding.AwayFromZero)
            : null;
        decimal? netIncomePresOut = incomeResolved && revPresOut.HasValue && expPresOut.HasValue
            ? Math.Round(revPresOut.Value - expPresOut.Value, presentationDecimals, MidpointRounding.AwayFromZero)
            : null;

        // IAS 21.39(c) & 21.41 OCI Translation Difference balances Assets - Liabilities vs Equity + NetIncome
        decimal? ociPresOut = null;
        decimal? totalLiabEqOciPresOut = null;
        bool? isBalancedPres = null;
        decimal? discrepancyPres = null;

        if (totalAssetsPresOut.HasValue && totalLiabPresOut.HasValue && equityPresOut.HasValue && netIncomePresOut.HasValue)
        {
            if (sameCurrency)
            {
                ociPresOut = 0m;
                totalLiabEqOciPresOut = Math.Round(totalLiabPresOut.Value + equityPresOut.Value + netIncomePresOut.Value, presentationDecimals, MidpointRounding.AwayFromZero);
                discrepancyPres = Math.Round(totalAssetsPresOut.Value - totalLiabEqOciPresOut.Value, 4, MidpointRounding.AwayFromZero);
                isBalancedPres = Math.Abs(discrepancyPres.Value) < 0.01m;
            }
            else
            {
                var netAssetsPres = totalAssetsPresOut.Value - totalLiabPresOut.Value;
                var equityPlusNiPres = equityPresOut.Value + netIncomePresOut.Value;
                ociPresOut = Math.Round(netAssetsPres - equityPlusNiPres, presentationDecimals, MidpointRounding.AwayFromZero);
                totalLiabEqOciPresOut = Math.Round(totalLiabPresOut.Value + equityPresOut.Value + netIncomePresOut.Value + ociPresOut.Value, presentationDecimals, MidpointRounding.AwayFromZero);
                discrepancyPres = Math.Round(totalAssetsPresOut.Value - totalLiabEqOciPresOut.Value, 4, MidpointRounding.AwayFromZero);
                isBalancedPres = Math.Abs(discrepancyPres.Value) < 0.01m;
            }
        }

        string status = sameCurrency
            ? "SAME_CURRENCY"
            : missingDiagnostics.Count == 0
                ? "TRANSLATED"
                : (totalAssetsPresOut.HasValue || netIncomePresOut.HasValue || equityPresOut.HasValue)
                    ? "UNAVAILABLE_PARTIAL_RATES"
                    : "UNAVAILABLE_MISSING_RATES";

        string? closingDirection = closingRate is null
            ? null
            : closingRate.IsInverse
                ? $"{presentationCurrency} -> {functionalCurrency} (inverse 1/{closingRate.RawRate:G})"
                : $"{functionalCurrency} -> {presentationCurrency}";

        return new PresentationFinancialStatementsResponse(
            AsOfDate: asOfDate,
            FunctionalCurrencyCode: functionalCurrency,
            PresentationCurrencyCode: presentationCurrency,
            SameCurrency: sameCurrency,
            TranslationMethod: methodDescription,
            TranslationStatus: status,
            ClosingRate: closingRate?.EffectiveMultiplier,
            ClosingRateDirection: closingDirection,
            ClosingRateEffectiveDate: closingRate?.EffectiveDate,
            ClosingRateSource: closingRate?.Source,
            TotalAssetsFunctional: totalAssetsFuncOut,
            TotalAssetsPresentation: totalAssetsPresOut,
            TotalLiabilitiesFunctional: totalLiabFuncOut,
            TotalLiabilitiesPresentation: totalLiabPresOut,
            EquityBeforeOciFunctional: totalEquityFuncOut,
            EquityBeforeOciPresentation: equityPresOut,
            TotalRevenueFunctional: totalRevFuncOut,
            TotalRevenuePresentation: revPresOut,
            TotalCostOfSalesFunctional: totalCogsFuncOut,
            TotalCostOfSalesPresentation: cogsPresOut,
            TotalOperatingExpensesFunctional: totalOpexFuncOut,
            TotalOperatingExpensesPresentation: opexPresOut,
            TotalExpensesFunctional: totalExpFuncOut,
            TotalExpensesPresentation: expPresOut,
            NetIncomeFunctional: netIncomeFuncOut,
            NetIncomePresentation: netIncomePresOut,
            TranslationDifferenceOciPresentation: ociPresOut,
            TotalLiabilitiesAndEquityIncludingOciPresentation: totalLiabEqOciPresOut,
            IsBalanceSheetBalancedPresentation: isBalancedPres,
            BalanceSheetDiscrepancyPresentation: discrepancyPres,
            BalanceSheetLines: balanceSheetLines,
            IncomeStatementLines: incomeStatementLines,
            MissingRates: missingDiagnostics);
    }

    private static decimal ApplyRate(decimal amount, ResolvedRate rate) =>
        rate.IsInverse
            ? amount / rate.RawRate
            : amount * rate.RawRate;

    private static ResolvedRate? ResolveRate(
        string fromCurrency,
        string toCurrency,
        DateOnly targetDate,
        string rateType,
        IReadOnlyList<Ias21ExchangeRateInput> rates)
    {
        Ias21ExchangeRateInput? bestDirect = null;
        Ias21ExchangeRateInput? bestInverse = null;

        foreach (var r in rates)
        {
            if (r.Rate <= 0m ||
                r.EffectiveDate > targetDate ||
                !string.Equals(r.RateType, rateType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var from = r.FromCurrency.Trim().ToUpperInvariant();
            var to = r.ToCurrency.Trim().ToUpperInvariant();

            if (string.Equals(from, fromCurrency, StringComparison.Ordinal) &&
                string.Equals(to, toCurrency, StringComparison.Ordinal))
            {
                if (bestDirect is null ||
                    r.EffectiveDate > bestDirect.EffectiveDate ||
                    (r.EffectiveDate == bestDirect.EffectiveDate && r.CreatedAt > bestDirect.CreatedAt))
                {
                    bestDirect = r;
                }
            }
            else if (string.Equals(from, toCurrency, StringComparison.Ordinal) &&
                     string.Equals(to, fromCurrency, StringComparison.Ordinal))
            {
                if (bestInverse is null ||
                    r.EffectiveDate > bestInverse.EffectiveDate ||
                    (r.EffectiveDate == bestInverse.EffectiveDate && r.CreatedAt > bestInverse.CreatedAt))
                {
                    bestInverse = r;
                }
            }
        }

        if (bestDirect is null && bestInverse is null) return null;

        if (bestDirect is not null && (bestInverse is null || bestDirect.EffectiveDate >= bestInverse.EffectiveDate))
        {
            return new ResolvedRate(
                bestDirect.ExchangeRateId,
                bestDirect.Rate,
                IsInverse: false,
                EffectiveMultiplier: bestDirect.Rate,
                EffectiveDate: bestDirect.EffectiveDate,
                RateType: bestDirect.RateType,
                Source: bestDirect.Source);
        }

        var inv = bestInverse!;
        return new ResolvedRate(
            inv.ExchangeRateId,
            inv.Rate,
            IsInverse: true,
            EffectiveMultiplier: Math.Round(1m / inv.Rate, 10, MidpointRounding.AwayFromZero),
            EffectiveDate: inv.EffectiveDate,
            RateType: inv.RateType,
            Source: inv.Source);
    }
}
