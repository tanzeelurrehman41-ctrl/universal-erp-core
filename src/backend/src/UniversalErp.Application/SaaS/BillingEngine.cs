namespace UniversalErp.Application.SaaS;

public sealed class BillingEngine
{
    public sealed record ProrationResult(
        decimal UnusedOldPlanCredit,
        decimal NewPlanCharge,
        decimal NetPayableAmount,
        int RemainingDays,
        int TotalPeriodDays);

    public sealed record InvoiceCalculationResult(
        decimal Subtotal,
        decimal DiscountAmount,
        decimal CreditsApplied,
        decimal TaxRatePercentage,
        decimal TaxAmount,
        decimal TotalAmount,
        bool IsTaxInclusive = false);

    public sealed record TaxResolutionRequest(
        string CountryCode,
        string? ProvinceState,
        string? TariffCode = null,
        string TaxRegime = "standard_with_input_tax",
        bool IsExport = false,
        bool HasValidForeignRemittanceProof = false,
        bool ProviderElectedReducedRateWithoutInputTax = false,
        DateTimeOffset? AsOfDate = null);

    public sealed record TaxResolutionResult(
        string Jurisdiction,
        string Authority,
        string StatutoryReference,
        string TariffCode,
        string TaxRegime,
        decimal RatePercentage,
        bool InputTaxAllowable,
        string? EligibilityConditions);

    public sealed record CreditNoteCalculationResult(
        decimal RefundSubtotal,
        decimal TaxReversed,
        decimal TotalCreditNoteAmount,
        decimal TaxRatePercentage,
        bool IsTaxInclusive);

    public ProrationResult CalculateProration(
        decimal oldBasePrice,
        decimal newBasePrice,
        DateTimeOffset currentPeriodStart,
        DateTimeOffset currentPeriodEnd,
        DateTimeOffset asOfDate)
    {
        var totalDays = Math.Max(1, (int)Math.Ceiling((currentPeriodEnd - currentPeriodStart).TotalDays));
        var remainingDays = Math.Max(0, (int)Math.Ceiling((currentPeriodEnd - asOfDate).TotalDays));

        if (remainingDays <= 0 || oldBasePrice <= 0 && newBasePrice <= 0)
        {
            return new ProrationResult(
                UnusedOldPlanCredit: 0.00m,
                NewPlanCharge: 0.00m,
                NetPayableAmount: Math.Max(0, newBasePrice - oldBasePrice),
                RemainingDays: 0,
                TotalPeriodDays: totalDays);
        }

        var fraction = (decimal)remainingDays / totalDays;
        var unusedCredit = Math.Round(oldBasePrice * fraction, 2);
        var newCharge = Math.Round(newBasePrice * fraction, 2);
        var netPayable = Math.Max(0.00m, newCharge - unusedCredit);

        return new ProrationResult(
            UnusedOldPlanCredit: unusedCredit,
            NewPlanCharge: newCharge,
            NetPayableAmount: netPayable,
            RemainingDays: remainingDays,
            TotalPeriodDays: totalDays);
    }

    /// <summary>
    /// Resolves statutory sales tax rate by jurisdiction.
    /// Standard Sindh rate is verified at 15.00% under Sindh Finance Act 2024 / SSTS Act 2011 Sec 8(1).
    /// </summary>
    public decimal ResolveTaxRate(string countryCode, string? provinceState)
    {
        if (!string.Equals(countryCode, "PK", StringComparison.OrdinalIgnoreCase))
        {
            return 0.00m; // International or export zero-rate
        }

        var province = provinceState?.ToUpperInvariant().Trim() ?? "FEDERAL";
        return province switch
        {
            "PUNJAB" => 16.0000m,                               // PRA Punjab Sales Tax on Services (Standard SaaS)
            "SINDH" => 15.0000m,                                // SRB Sindh Sales Tax on Services (Standard SaaS, Sindh Finance Act 2024)
            "KP" or "KPK" or "KHYBER_PAKHTUNKHWA" => 15.0000m,  // KPRA Sales Tax on Services
            "BALOCHISTAN" => 15.0000m,                          // BRA Balochistan Sales Tax on Services
            _ => 15.0000m                                       // ICT Federal Sales Tax on Services
        };
    }

    /// <summary>
    /// Comprehensive statutory tax resolution engine taking into account:
    /// - Service classification (Tariff heading 9815.6000 / SaaS)
    /// - Place of provision (Customer jurisdiction)
    /// - Standard vs. conditional reduced rate (with vs. without input tax credit)
    /// - Effective-dated intervals (e.g. Sindh 13% pre-July 2024 vs 15% post-July 2024)
    /// - Export of services zero-rating (remittance in foreign exchange)
    /// </summary>
    public TaxResolutionResult ResolveStatutoryTaxRule(TaxResolutionRequest request)
    {
        var country = request.CountryCode.ToUpperInvariant().Trim();
        var province = request.ProvinceState?.ToUpperInvariant().Trim() ?? "FEDERAL";
        var asOf = request.AsOfDate ?? DateTimeOffset.UtcNow;

        // 1. Export of Services Check (Place of provision outside Pakistan + foreign exchange proceeds)
        if (request.IsExport || country != "PK")
        {
            if (request.HasValidForeignRemittanceProof || country != "PK")
            {
                return new TaxResolutionResult(
                    Jurisdiction: "EXPORT",
                    Authority: "FBR_PROVINCIAL_HARMONIZED",
                    StatutoryReference: "SRB / PRA / KPRA / FBR Place of Provision of Services Rules (Export Zero-Rating)",
                    TariffCode: !string.IsNullOrWhiteSpace(request.TariffCode) ? request.TariffCode : "9815.6000",
                    TaxRegime: "export_zero_rated",
                    RatePercentage: 0.0000m,
                    InputTaxAllowable: true,
                    EligibilityConditions: "Customer located outside Pakistan, benefit consumed outside Pakistan, and proceeds received via banking channel in convertible foreign exchange (SBP e-PRC).");
            }
        }

        // 2. Provincial & Federal Jurisdictional Resolution
        switch (province)
        {
            case "SINDH":
                var sindhTariff = !string.IsNullOrWhiteSpace(request.TariffCode) ? request.TariffCode : "9815.6000";
                // Historical interval check (Pre-July 1, 2024 Sindh standard rate was 13%)
                if (asOf < new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero))
                {
                    return new TaxResolutionResult(
                        Jurisdiction: "SINDH",
                        Authority: "SRB",
                        StatutoryReference: "Sindh Sales Tax on Services Act, 2011, Sec 8(1) (pre-2024 interval)",
                        TariffCode: sindhTariff,
                        TaxRegime: "standard_with_input_tax",
                        RatePercentage: 13.0000m,
                        InputTaxAllowable: true,
                        EligibilityConditions: "Historical rate interval prior to Sindh Finance Act 2024.");
                }

                // Reduced rate election check (3% without input tax credit)
                if (request.ProviderElectedReducedRateWithoutInputTax)
                {
                    return new TaxResolutionResult(
                        Jurisdiction: "SINDH",
                        Authority: "SRB",
                        StatutoryReference: "SRB Notification No. SRB-3-4/7/2013 as amended & Second Schedule Heading 9815.6000",
                        TariffCode: sindhTariff,
                        TaxRegime: "reduced_without_input_tax",
                        RatePercentage: 3.0000m,
                        InputTaxAllowable: false,
                        EligibilityConditions: "Requires formal election on monthly tax return and complete surrender of input tax adjustment and refund rights under SRB Special Procedure Rules. Does NOT apply automatically to every SaaS subscription.");
                }

                // Standard Rate: 15.00% under Sindh Finance Act 2024
                return new TaxResolutionResult(
                    Jurisdiction: "SINDH",
                    Authority: "SRB",
                    StatutoryReference: "Sindh Sales Tax on Services Act, 2011, Sec 8(1) as amended by Sindh Finance Act 2024",
                    TariffCode: sindhTariff,
                    TaxRegime: "standard_with_input_tax",
                    RatePercentage: 15.0000m,
                    InputTaxAllowable: true,
                    EligibilityConditions: "Standard statutory rate with full input tax credit and adjustment rights under SRB rules.");

            case "PUNJAB":
                var punjabTariff = !string.IsNullOrWhiteSpace(request.TariffCode) ? request.TariffCode : "9815.6000";
                if (request.ProviderElectedReducedRateWithoutInputTax)
                {
                    return new TaxResolutionResult(
                        Jurisdiction: "PUNJAB",
                        Authority: "PRA",
                        StatutoryReference: "PRA Notification No. SO(TAX)1-1/2020 & Second Schedule Heading 9815.6000",
                        TariffCode: punjabTariff,
                        TaxRegime: "reduced_without_input_tax",
                        RatePercentage: 5.0000m,
                        InputTaxAllowable: false,
                        EligibilityConditions: "Conditional upon formal election to forgo input tax credit under PRA Special Procedure Rules.");
                }

                return new TaxResolutionResult(
                    Jurisdiction: "PUNJAB",
                    Authority: "PRA",
                    StatutoryReference: "Punjab Sales Tax on Services Act, 2012, Second Schedule, Heading 9815.6000",
                    TariffCode: punjabTariff,
                    TaxRegime: "standard_with_input_tax",
                    RatePercentage: 16.0000m,
                    InputTaxAllowable: true,
                    EligibilityConditions: "Standard statutory rate with full input tax credit.");

            case "KP" or "KPK" or "KHYBER_PAKHTUNKHWA":
                var kpTariff = !string.IsNullOrWhiteSpace(request.TariffCode) ? request.TariffCode : "9850.0000";
                if (request.ProviderElectedReducedRateWithoutInputTax)
                {
                    return new TaxResolutionResult(
                        Jurisdiction: "KP",
                        Authority: "KPRA",
                        StatutoryReference: "KP Finance Act 2026 & Second Schedule Heading 9850.0000 Reduced Column",
                        TariffCode: kpTariff,
                        TaxRegime: "reduced_without_input_tax",
                        RatePercentage: 2.0000m,
                        InputTaxAllowable: false,
                        EligibilityConditions: "Applies strictly to qualifying digital and IT-based services under tariff heading 9850.0000 without input tax credit. Not automatically assigned.");
                }

                return new TaxResolutionResult(
                    Jurisdiction: "KP",
                    Authority: "KPRA",
                    StatutoryReference: "Khyber Pakhtunkhwa Sales Tax on Services Act, 2022, Second Schedule, Heading 9850.0000 as amended by KP Finance Act 2026",
                    TariffCode: kpTariff,
                    TaxRegime: "standard_with_input_tax",
                    RatePercentage: 15.0000m,
                    InputTaxAllowable: true,
                    EligibilityConditions: "Standard statutory rate with full input tax credit.");

            case "BALOCHISTAN":
                var balochistanTariff = !string.IsNullOrWhiteSpace(request.TariffCode) ? request.TariffCode : "9868.0000";
                if (request.ProviderElectedReducedRateWithoutInputTax)
                {
                    // Check historical interval prior to Balochistan Finance Act 2026 (2026-07-01)
                    if (asOf < new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero))
                    {
                        return new TaxResolutionResult(
                            Jurisdiction: "BALOCHISTAN",
                            Authority: "BRA",
                            StatutoryReference: "Balochistan Sales Tax on Services Act, 2015, Second Schedule (historical interval prior to Finance Act 2026)",
                            TariffCode: balochistanTariff,
                            TaxRegime: "reduced_without_input_tax",
                            RatePercentage: 3.0000m,
                            InputTaxAllowable: false,
                            EligibilityConditions: "Historical reduced rate interval prior to Balochistan Finance Act 2026. Input tax credit barred.");
                    }

                    return new TaxResolutionResult(
                        Jurisdiction: "BALOCHISTAN",
                        Authority: "BRA",
                        StatutoryReference: "Balochistan Finance Act 2026 & Second Schedule Heading 9868.0000 Reduced Rate Column",
                        TariffCode: balochistanTariff,
                        TaxRegime: "reduced_without_input_tax",
                        RatePercentage: 6.0000m,
                        InputTaxAllowable: false,
                        EligibilityConditions: "Applies strictly to qualifying IT/software services under tariff heading 9868.0000 where provider formally elects no input tax adjustment. Input tax credit is strictly barred.");
                }

                return new TaxResolutionResult(
                    Jurisdiction: "BALOCHISTAN",
                    Authority: "BRA",
                    StatutoryReference: "Balochistan Sales Tax on Services Act, 2015, Second Schedule, Heading 9868.0000 as amended by Balochistan Finance Act 2026",
                    TariffCode: balochistanTariff,
                    TaxRegime: "standard_with_input_tax",
                    RatePercentage: 15.0000m,
                    InputTaxAllowable: true,
                    EligibilityConditions: "Standard statutory rate with full input tax credit under BRA rules.");

            default: // FEDERAL / ICT
                var federalTariff = !string.IsNullOrWhiteSpace(request.TariffCode) ? request.TariffCode : "9815.6000";
                if (request.ProviderElectedReducedRateWithoutInputTax)
                {
                    return new TaxResolutionResult(
                        Jurisdiction: "FEDERAL",
                        Authority: "FBR_ICT",
                        StatutoryReference: "ICT Ordinance 2001, Table-II & S.R.O. Notifications for IT-enabled services",
                        TariffCode: federalTariff,
                        TaxRegime: "reduced_without_input_tax",
                        RatePercentage: 5.0000m,
                        InputTaxAllowable: false,
                        EligibilityConditions: "Applies strictly to qualifying software/IT development consultants and PSEB-registered entities who forgo input tax credit. Does NOT apply automatically to general SaaS subscriptions.");
                }

                return new TaxResolutionResult(
                    Jurisdiction: "FEDERAL",
                    Authority: "FBR_ICT",
                    StatutoryReference: "Islamabad Capital Territory (Tax on Services) Ordinance, 2001, Schedule Table, Heading 9815.6000",
                    TariffCode: federalTariff,
                    TaxRegime: "standard_with_input_tax",
                    RatePercentage: 15.0000m,
                    InputTaxAllowable: true,
                    EligibilityConditions: "Standard rate for services rendered or consumed in Islamabad Capital Territory.");
        }
    }

    /// <summary>
    /// Computes full invoice figures supporting tax-exclusive and tax-inclusive pricing.
    /// </summary>
    public InvoiceCalculationResult CalculateInvoice(
        decimal basePrice,
        decimal overageCharges,
        decimal discountAmount,
        decimal availableCredits,
        decimal taxRatePct,
        bool isTaxInclusive = false)
    {
        var subtotal = Math.Max(0.00m, basePrice + overageCharges);
        var totalDiscount = Math.Min(subtotal, Math.Max(0.00m, discountAmount));
        var afterDiscount = subtotal - totalDiscount;

        var creditsApplied = Math.Min(afterDiscount, Math.Max(0.00m, availableCredits));
        var netChargeable = Math.Max(0.00m, afterDiscount - creditsApplied);

        decimal taxableBase;
        decimal taxAmount;
        decimal total;

        if (isTaxInclusive)
        {
            if (taxRatePct > 0)
            {
                // NetChargeable is inclusive of tax: TaxableBase = NetChargeable / (1 + Rate / 100)
                taxableBase = Math.Round(netChargeable / (1.0m + (taxRatePct / 100.0m)), 2);
                taxAmount = netChargeable - taxableBase;
                total = netChargeable;
            }
            else
            {
                taxableBase = netChargeable;
                taxAmount = 0.00m;
                total = netChargeable;
            }
        }
        else
        {
            taxableBase = netChargeable;
            taxAmount = Math.Round(taxableBase * (taxRatePct / 100.0m), 2);
            total = taxableBase + taxAmount;
        }

        return new InvoiceCalculationResult(
            Subtotal: subtotal,
            DiscountAmount: totalDiscount,
            CreditsApplied: creditsApplied,
            TaxRatePercentage: taxRatePct,
            TaxAmount: taxAmount,
            TotalAmount: total,
            IsTaxInclusive: isTaxInclusive);
    }

    /// <summary>
    /// Computes formal credit note figures for service refunds or proration credit adjustments.
    /// </summary>
    public CreditNoteCalculationResult CalculateCreditNote(
        decimal refundAmount,
        decimal taxRatePercentage,
        bool isTaxInclusive = false)
    {
        decimal refundSubtotal;
        decimal taxReversed;
        decimal totalCreditNote;

        if (isTaxInclusive)
        {
            if (taxRatePercentage > 0)
            {
                refundSubtotal = Math.Round(refundAmount / (1.0m + (taxRatePercentage / 100.0m)), 2);
                taxReversed = refundAmount - refundSubtotal;
                totalCreditNote = refundAmount;
            }
            else
            {
                refundSubtotal = refundAmount;
                taxReversed = 0.00m;
                totalCreditNote = refundAmount;
            }
        }
        else
        {
            refundSubtotal = refundAmount;
            taxReversed = Math.Round(refundSubtotal * (taxRatePercentage / 100.0m), 2);
            totalCreditNote = refundSubtotal + taxReversed;
        }

        return new CreditNoteCalculationResult(
            RefundSubtotal: refundSubtotal,
            TaxReversed: taxReversed,
            TotalCreditNoteAmount: totalCreditNote,
            TaxRatePercentage: taxRatePercentage,
            IsTaxInclusive: isTaxInclusive);
    }

    public string GenerateInvoiceNumber(string tenantCode, DateTimeOffset asOfDate)
    {
        var datePart = asOfDate.ToString("yyyyMM");
        var randomSuffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return $"INV-{tenantCode.ToUpperInvariant()}-{datePart}-{randomSuffix}";
    }
}
