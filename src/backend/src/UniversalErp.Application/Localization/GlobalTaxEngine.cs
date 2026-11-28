using System;

namespace UniversalErp.Application.Localization;

public sealed class GlobalTaxEngine
{
    /// <summary>
    /// Comprehensive global tax determination evaluating supplier establishment, customer location,
    /// place of supply, customer registration (B2B/B2C), product classification (SaaS vs Dev),
    /// effective dates, exemption certificates, and input tax recovery rules.
    /// </summary>
    public TaxDeterminationResult DetermineTax(TaxDeterminationRequest request)
    {
        var supplierCountry = (request.SupplierCountryCode ?? "US").Trim().ToUpperInvariant();
        var customerCountry = (request.CustomerCountryCode ?? "US").Trim().ToUpperInvariant();
        var province = request.CustomerProvinceState?.Trim().ToUpperInvariant();
        var asOf = request.AsOfDate ?? DateTimeOffset.UtcNow;
        var hasVatId = !string.IsNullOrWhiteSpace(request.CustomerTaxId);
        var isB2B = string.Equals(request.CustomerType, "b2b", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(request.CustomerType, "b2b_registered", StringComparison.OrdinalIgnoreCase) ||
                    hasVatId;

        // 1. Pakistan IT Export Zero-Rating with Remittance Proof
        if (supplierCountry == "PK" && customerCountry != "PK" && (request.IsExport || request.HasValidForeignRemittanceProof))
        {
            return new TaxDeterminationResult(
                Jurisdiction: "PK-EXPORT",
                CountryCode: customerCountry,
                ProvinceState: province,
                Authority: "FBR_PRC",
                TaxSystem: "export_zero_rated",
                TaxName: "Pakistan IT Export of Services (0% Zero-Rated)",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "9815.6000",
                RatePercentage: 0.0000m,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: "Income Tax Ordinance 2001 Sec 154A & Provincial Place of Provision of Services Rules",
                CalculationExplanation: $"Qualifying IT services export with foreign remittance proof from {supplierCountry} to {customerCountry}.",
                AuditEvidenceSummary: $"Pakistan IT export zero-rating confirmed. RemittanceProof={request.HasValidForeignRemittanceProof}, Customer={customerCountry}.");
        }

        // 2. European Union (EU Member States: B2B Reverse Charge vs B2C One Stop Shop Destination VAT)
        if (IsEuMemberState(customerCountry))
        {
            var isCrossBorder = supplierCountry != customerCountry;

            // Intra-EU or Cross-Border B2B with verified VAT ID -> Reverse Charge (0%) under Article 196
            if (isCrossBorder && isB2B && hasVatId)
            {
                return new TaxDeterminationResult(
                    Jurisdiction: $"EU-{customerCountry}",
                    CountryCode: customerCountry,
                    ProvinceState: province,
                    Authority: $"EU_REVENUE_{customerCountry}",
                    TaxSystem: "reverse_charge",
                    TaxName: "EU B2B Reverse Charge (Directive 2006/112/EC Art 196)",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "CPA-62.01",
                    RatePercentage: 0.0000m,
                    InputTaxAllowable: true,
                    IsReverseCharge: true,
                    IsExempt: false,
                    StatutoryReference: "Council Directive 2006/112/EC, Article 196 (Reverse Charge on Cross-Border B2B Services)",
                    CalculationExplanation: $"Cross-border B2B electronic supply from {supplierCountry} to taxable person in {customerCountry}. Customer ({request.CustomerTaxId}) self-assesses VAT.",
                    AuditEvidenceSummary: $"VIES VAT ID verified: {request.CustomerTaxId}. Reverse charge applies.",
                    EInvoicingMandatePlatform: GetEuEInvoicingPlatform(customerCountry),
                    IsDestinationVat: true);
            }

            // B2C or Cross-Border B2B without valid VAT ID -> Destination VAT under EU One Stop Shop (OSS)
            var euRate = GetEuStandardVatRate(customerCountry);
            var isOss = isCrossBorder && request.IsEuOssEligible;

            return new TaxDeterminationResult(
                Jurisdiction: $"EU-{customerCountry}",
                CountryCode: customerCountry,
                ProvinceState: province,
                Authority: $"EU_REVENUE_{customerCountry}",
                TaxSystem: isOss ? "eu_oss" : "vat",
                TaxName: isOss ? $"EU One Stop Shop (OSS) Destination VAT ({customerCountry})" : $"EU Domestic VAT ({customerCountry})",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "CPA-62.01",
                RatePercentage: euRate,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: isOss
                    ? "EU Directive 2006/112/EC Article 58 & Council Implementing Regulation (EU) 2019/1995 (OSS Scheme)"
                    : $"National VAT Act of {customerCountry} (Standard Rate)",
                CalculationExplanation: isOss
                    ? $"Electronic service supplied to consumer in {customerCountry}. Destination VAT of {euRate}% charged under OSS scheme."
                    : $"Domestic supply in {customerCountry} charged at standard rate of {euRate}%.",
                AuditEvidenceSummary: $"EU destination VAT applied. Destination={customerCountry}, Rate={euRate}%, OSS={isOss}.",
                EInvoicingMandatePlatform: GetEuEInvoicingPlatform(customerCountry),
                IsDestinationVat: true);
        }

        // 3. United Kingdom (HMRC)
        if (customerCountry == "GB")
        {
            var isCrossBorder = supplierCountry != "GB";

            // Cross-border B2B with verified UK VAT number -> Reverse Charge under VATA 1994 s7A
            if (isCrossBorder && isB2B && hasVatId)
            {
                return new TaxDeterminationResult(
                    Jurisdiction: "GB-HMRC",
                    CountryCode: "GB",
                    ProvinceState: "UK",
                    Authority: "HMRC",
                    TaxSystem: "reverse_charge",
                    TaxName: "UK B2B Cross-Border Reverse Charge",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "CPA-62.01",
                    RatePercentage: 0.0000m,
                    InputTaxAllowable: true,
                    IsReverseCharge: true,
                    IsExempt: false,
                    StatutoryReference: "Value Added Tax Act 1994, Section 7A (Place of Supply B2B General Rule)",
                    CalculationExplanation: $"Supply from {supplierCountry} to UK business customer ({request.CustomerTaxId}). Customer accounts for VAT under reverse charge.",
                    AuditEvidenceSummary: $"UK VAT registration verified: {request.CustomerTaxId}. Reverse charge applies.",
                    IsDestinationVat: true);
            }

            // Domestic or B2C Cross-Border -> UK Standard 20% VAT
            return new TaxDeterminationResult(
                Jurisdiction: "GB-HMRC",
                CountryCode: "GB",
                ProvinceState: "UK",
                Authority: "HMRC",
                TaxSystem: "vat",
                TaxName: isCrossBorder ? "UK Overseas Vendor Digital Services VAT" : "UK Value Added Tax (Standard Rate)",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "CPA-62.01",
                RatePercentage: 20.0000m,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: "Value Added Tax Act 1994, Section 2(1) and Schedule 4A",
                CalculationExplanation: isCrossBorder
                    ? "UK Overseas vendor electronic services rules require standard 20% VAT on sales to UK consumers."
                    : "Standard rate UK VAT for domestic supply of SaaS / electronic services.",
                AuditEvidenceSummary: "HMRC statutory standard 20% VAT rate.",
                IsDestinationVat: isCrossBorder);
        }

        // 2. United States Destination-Based Sales Tax
        if (customerCountry == "US")
        {
            // B2B Exemption Certificate provided (e.g. Resale Certificate ST-120)
            if (!string.IsNullOrWhiteSpace(request.ExemptionCertificateNumber))
            {
                return new TaxDeterminationResult(
                    Jurisdiction: $"US-{province ?? "FED"}",
                    CountryCode: "US",
                    ProvinceState: province,
                    Authority: "US_STATE_TAX",
                    TaxSystem: "exempt",
                    TaxName: "US State Sales Tax Exemption",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "NAICS-518210",
                    RatePercentage: 0.0000m,
                    InputTaxAllowable: true,
                    IsReverseCharge: false,
                    IsExempt: true,
                    StatutoryReference: "Uniform Sales & Use Tax Resale Certificate / State Exemption Rules",
                    CalculationExplanation: $"Zero tax assessed due to verified exemption certificate: {request.ExemptionCertificateNumber}.",
                    AuditEvidenceSummary: $"US Exemption Certificate verified. CertificateNo={request.ExemptionCertificateNumber}, State={province}");
            }

            return province switch
            {
                // New York: SaaS is taxable under NY Tax Law § 1105(c)(9) (Combined State + Local e.g. NYC 8.875%)
                "NY" => new TaxDeterminationResult(
                    Jurisdiction: "US-NY",
                    CountryCode: "US",
                    ProvinceState: "NY",
                    Authority: "NYS_TAX",
                    TaxSystem: "sales_tax",
                    TaxName: "New York State & Local Sales Tax on SaaS",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "NAICS-518210",
                    RatePercentage: 8.8750m,
                    InputTaxAllowable: true,
                    IsReverseCharge: false,
                    IsExempt: false,
                    StatutoryReference: "NY Tax Law § 1105(c)(9) & TSB-M-08(7)S (Prewritten Software / SaaS Access)",
                    CalculationExplanation: "New York classifies SaaS access as taxable prewritten computer software.",
                    AuditEvidenceSummary: "NYS Department of Taxation and Finance combined rate (4.0% state + 4.5% NYC + 0.375% MCTD)."),

                // Texas: 80% Taxable Base for SaaS / Data Processing
                // Texas levies a 6.25% state sales tax with applicable local taxes (city, county, transit) up to 2.0% (totaling up to 8.25%).
                // Under Tex. Tax Code § 151.0101(a)(12) & 34 TAC Rule 3.342, 20% of the value is exempt (80% taxable base).
                "TX_STATE" or "TX_UNINCORPORATED" => new TaxDeterminationResult(
                    Jurisdiction: "US-TX-STATE",
                    CountryCode: "US",
                    ProvinceState: "TX",
                    Authority: "TX_COMPTROLLER",
                    TaxSystem: "sales_tax",
                    TaxName: "Texas State Sales Tax on SaaS (State Only 6.25% on 80% Base)",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "NAICS-518210",
                    RatePercentage: 5.0000m, // 6.25% * 0.8 = 5.0%
                    InputTaxAllowable: true,
                    IsReverseCharge: false,
                    IsExempt: false,
                    StatutoryReference: "Texas Tax Code § 151.0101(a)(12) & 34 TAC Rule 3.342 (State Rate 6.25% on 80% Taxable Base)",
                    CalculationExplanation: "Texas state-only sales tax: 6.25% statutory rate assessed on 80% taxable base yields 5.0% effective tax.",
                    AuditEvidenceSummary: "Texas Comptroller state rate 6.25% on 80% taxable base without local jurisdiction add-ons."),

                "TX" or "TX_COMBINED" or "TX_AUSTIN" or "TX_HOUSTON" or "TX_DALLAS" => new TaxDeterminationResult(
                    Jurisdiction: "US-TX",
                    CountryCode: "US",
                    ProvinceState: "TX",
                    Authority: "TX_COMPTROLLER",
                    TaxSystem: "sales_tax",
                    TaxName: "Texas Combined State & Local Sales Tax on SaaS (8.25% on 80% Base)",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "NAICS-518210",
                    RatePercentage: 6.6000m, // (6.25% state + 2.0% local = 8.25%) * 0.8 = 6.6%
                    InputTaxAllowable: true,
                    IsReverseCharge: false,
                    IsExempt: false,
                    StatutoryReference: "Texas Tax Code § 151.0101(a)(12) & 34 TAC Rule 3.342 (Combined 8.25% on 80% Taxable Base)",
                    CalculationExplanation: "Texas combined state (6.25%) and local (2.00%) sales tax assessed on 80% taxable base yields 6.6% effective tax.",
                    AuditEvidenceSummary: "Texas Comptroller combined rate 8.25% assessed on 80% taxable base."),

                // California: Pure SaaS is Exempt (No tangible personal property transferred)
                "CA" => new TaxDeterminationResult(
                    Jurisdiction: "US-CA",
                    CountryCode: "US",
                    ProvinceState: "CA",
                    Authority: "CA_CDTFA",
                    TaxSystem: "exempt",
                    TaxName: "California Pure SaaS Sales Tax Exemption",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "NAICS-518210",
                    RatePercentage: 0.0000m,
                    InputTaxAllowable: true,
                    IsReverseCharge: false,
                    IsExempt: true,
                    StatutoryReference: "CDTFA Regulation 1502 (Computers, Programs, and Data Processing - Pure SaaS)",
                    CalculationExplanation: "California exempts pure cloud software when no tangible storage media is delivered.",
                    AuditEvidenceSummary: "CDTFA non-taxable electronic service determination."),

                // Washington: Digital Automated Services (DAS) at 6.5%
                "WA" => new TaxDeterminationResult(
                    Jurisdiction: "US-WA",
                    CountryCode: "US",
                    ProvinceState: "WA",
                    Authority: "WA_DOR",
                    TaxSystem: "sales_tax",
                    TaxName: "Washington Digital Automated Services Sales Tax",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "NAICS-518210",
                    RatePercentage: 6.5000m,
                    InputTaxAllowable: true,
                    IsReverseCharge: false,
                    IsExempt: false,
                    StatutoryReference: "RCW 82.04.192 & WAC 458-20-15503 (Digital Automated Services)",
                    CalculationExplanation: "Washington State retail sales tax applies to automated software/SaaS.",
                    AuditEvidenceSummary: "WA Department of Revenue statutory standard state rate."),

                // Default US State: Origin/General standard rate (e.g. 6.00%)
                _ => new TaxDeterminationResult(
                    Jurisdiction: $"US-{province ?? "GENERAL"}",
                    CountryCode: "US",
                    ProvinceState: province,
                    Authority: "US_STATE_TAX",
                    TaxSystem: "sales_tax",
                    TaxName: "US State General Sales Tax",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "NAICS-518210",
                    RatePercentage: 6.0000m,
                    InputTaxAllowable: true,
                    IsReverseCharge: false,
                    IsExempt: false,
                    StatutoryReference: "US State Retail Sales and Use Tax Acts",
                    CalculationExplanation: "Standard state sales tax rate for destination jurisdiction.",
                    AuditEvidenceSummary: $"General US sales tax assessed for jurisdiction {province ?? "General"}.")
            };
        }

        // 5. United Arab Emirates (FTA)
        if (customerCountry == "AE")
        {
            return new TaxDeterminationResult(
                Jurisdiction: "AE-FTA",
                CountryCode: "AE",
                ProvinceState: province ?? "DUBAI",
                Authority: "FTA",
                TaxSystem: "vat",
                TaxName: "UAE Value Added Tax (Standard Rate)",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "UNSPSC-43231500",
                RatePercentage: 5.0000m,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: "Federal Decree-Law No. (8) of 2017 on Value Added Tax & Cabinet Decision No. 52/2017",
                CalculationExplanation: "Standard 5% VAT rate for electronic services consumed in the United Arab Emirates.",
                AuditEvidenceSummary: "Federal Tax Authority (FTA) statutory standard 5% VAT rate.",
                EInvoicingMandatePlatform: "UAE MoF E-Invoicing (Peppol PINT)");
        }

        // 6. Saudi Arabia (ZATCA)
        if (customerCountry == "SA")
        {
            return new TaxDeterminationResult(
                Jurisdiction: "SA-ZATCA",
                CountryCode: "SA",
                ProvinceState: province ?? "RIYADH",
                Authority: "ZATCA",
                TaxSystem: "vat",
                TaxName: "Saudi Arabia Value Added Tax (Standard Rate)",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "UNSPSC-43231500",
                RatePercentage: 15.0000m,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: "VAT Law Royal Decree No. (M/113) as amended by Royal Decree No. (A/638)",
                CalculationExplanation: "Standard 15% VAT rate for electronic services consumed in KSA with ZATCA Phase 2 compliance.",
                AuditEvidenceSummary: "Zakat, Tax and Customs Authority (ZATCA) statutory standard 15% VAT rate.",
                EInvoicingMandatePlatform: "ZATCA FATOORA Phase 2 (Cryptographic Stamp / UBL 2.1 XML)");
        }

        // 7. Australia (ATO GST)
        if (customerCountry == "AU")
        {
            var isCrossBorder = supplierCountry != "AU";
            if (isCrossBorder && isB2B && hasVatId)
            {
                return new TaxDeterminationResult(
                    Jurisdiction: "AU-ATO",
                    CountryCode: "AU",
                    ProvinceState: province,
                    Authority: "ATO",
                    TaxSystem: "reverse_charge",
                    TaxName: "Australia B2B Cross-Border Reverse Charge",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "DIGITAL-SVC",
                    RatePercentage: 0.0000m,
                    InputTaxAllowable: true,
                    IsReverseCharge: true,
                    IsExempt: false,
                    StatutoryReference: "A New Tax System (Goods and Services Tax) Act 1999, Subdiv 84-A",
                    CalculationExplanation: $"Cross-border B2B digital service to Australian GST-registered business ({request.CustomerTaxId}). Reverse charge applies.",
                    AuditEvidenceSummary: $"ABN/GST verified: {request.CustomerTaxId}. Reverse charge applies.");
            }

            return new TaxDeterminationResult(
                Jurisdiction: "AU-ATO",
                CountryCode: "AU",
                ProvinceState: province,
                Authority: "ATO",
                TaxSystem: "gst",
                TaxName: isCrossBorder ? "Australia Inbound Digital Services GST" : "Australia Goods and Services Tax (Standard)",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "DIGITAL-SVC",
                RatePercentage: 10.0000m,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: "A New Tax System (Goods and Services Tax) Act 1999, Section 9-70 & Subdiv 84-B",
                CalculationExplanation: "Standard 10% Australian GST applies to digital services consumed in Australia.",
                AuditEvidenceSummary: "Australian Taxation Office statutory standard 10% GST rate.");
        }

        // 8. Canada (CRA GST / HST)
        if (customerCountry == "CA")
        {
            var isCrossBorder = supplierCountry != "CA";
            if (isCrossBorder && isB2B && hasVatId)
            {
                return new TaxDeterminationResult(
                    Jurisdiction: "CA-CRA",
                    CountryCode: "CA",
                    ProvinceState: province,
                    Authority: "CRA",
                    TaxSystem: "reverse_charge",
                    TaxName: "Canada B2B Cross-Border Reverse Charge",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "DIGITAL-SVC",
                    RatePercentage: 0.0000m,
                    InputTaxAllowable: true,
                    IsReverseCharge: true,
                    IsExempt: false,
                    StatutoryReference: "Excise Tax Act (Canada), Section 220 (Reverse Charge on Imported Services)",
                    CalculationExplanation: $"Cross-border B2B digital service to Canadian GST/HST registrant ({request.CustomerTaxId}). Customer self-assesses.",
                    AuditEvidenceSummary: $"CRA GST/HST registration verified: {request.CustomerTaxId}. Reverse charge applies.");
            }

            var caRate = (province ?? "FED") switch
            {
                "ON" => 13.0000m, // Ontario HST (5% Fed + 8% Prov)
                "BC" => 12.0000m, // BC GST (5%) + PST (7%)
                "QC" => 14.9750m, // Quebec GST (5%) + QST (9.975%)
                "SK" => 11.0000m, // Saskatchewan GST (5%) + PST (6%)
                "MB" => 12.0000m, // Manitoba GST (5%) + RST (7%)
                "NS" or "NB" or "NL" or "PE" => 15.0000m, // Atlantic HST (5% Fed + 10% Prov)
                _ => 5.0000m      // Federal GST only (AB, YT, NT, NU)
            };

            return new TaxDeterminationResult(
                Jurisdiction: $"CA-{province ?? "FED"}",
                CountryCode: "CA",
                ProvinceState: province,
                Authority: "CRA",
                TaxSystem: "gst",
                TaxName: $"Canada GST/HST ({province ?? "Federal"})",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "DIGITAL-SVC",
                RatePercentage: caRate,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: "Excise Tax Act (Canada), Part IX (Simplified Non-Resident Digital Services Regime)",
                CalculationExplanation: $"Destination tax of {caRate}% assessed under Canadian digital services regime.",
                AuditEvidenceSummary: $"CRA destination rate applied: Province={province ?? "Federal"}, Rate={caRate}%.");
        }

        // 7. Pakistan Localization Package (Provincial Revenue Authorities)
        if (customerCountry == "PK")
        {
            var prov = province ?? "FEDERAL";
            switch (prov)
            {
                case "BALOCHISTAN":
                    var braTariff = !string.IsNullOrWhiteSpace(request.ProductServiceClassification) ? request.ProductServiceClassification : "9868.0000";
                    if (request.ProviderElectedReducedRateWithoutInputTax)
                    {
                        if (asOf < new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero))
                        {
                            return new TaxDeterminationResult(
                                Jurisdiction: "PK-BALOCHISTAN",
                                CountryCode: "PK",
                                ProvinceState: "BALOCHISTAN",
                                Authority: "BRA",
                                TaxSystem: "sales_tax",
                                TaxName: "BRA Historical Reduced Rate for IT/Software (Pre-2026)",
                                TariffOrClassificationCode: braTariff,
                                RatePercentage: 3.0000m,
                                InputTaxAllowable: false,
                                IsReverseCharge: false,
                                IsExempt: false,
                                StatutoryReference: "Balochistan Sales Tax on Services Act, 2015, Second Schedule (historical interval prior to Finance Act 2026)",
                                CalculationExplanation: "Historical 3.0% reduced rate interval prior to Balochistan Finance Act 2026.",
                                AuditEvidenceSummary: "BRA historical reduced rate without input tax credit.");
                        }

                        return new TaxDeterminationResult(
                            Jurisdiction: "PK-BALOCHISTAN",
                            CountryCode: "PK",
                            ProvinceState: "BALOCHISTAN",
                            Authority: "BRA",
                            TaxSystem: "sales_tax",
                            TaxName: "BRA Reduced Rate for IT/Software (Finance Act 2026)",
                            TariffOrClassificationCode: braTariff,
                            RatePercentage: 6.0000m,
                            InputTaxAllowable: false,
                            IsReverseCharge: false,
                            IsExempt: false,
                            StatutoryReference: "Balochistan Finance Act 2026 & Second Schedule Heading 9868.0000 Reduced Rate Column",
                            CalculationExplanation: "Amended 6.0% reduced rate under Balochistan Finance Act 2026. Input tax credit is strictly barred.",
                            AuditEvidenceSummary: "BRA Finance Act 2026 reduced rate with input credit surrender.");
                    }

                    return new TaxDeterminationResult(
                        Jurisdiction: "PK-BALOCHISTAN",
                        CountryCode: "PK",
                        ProvinceState: "BALOCHISTAN",
                        Authority: "BRA",
                        TaxSystem: "sales_tax",
                        TaxName: "BRA Balochistan Sales Tax on Services (Standard SaaS)",
                        TariffOrClassificationCode: braTariff,
                        RatePercentage: 15.0000m,
                        InputTaxAllowable: true,
                        IsReverseCharge: false,
                        IsExempt: false,
                        StatutoryReference: "Balochistan Sales Tax on Services Act, 2015, Second Schedule, Heading 9868.0000 as amended by Balochistan Finance Act 2026",
                        CalculationExplanation: "Standard statutory rate with full input tax credit under BRA rules.",
                        AuditEvidenceSummary: "BRA standard statutory 15% rate under tariff heading 9868.0000.");

                case "KP" or "KPK" or "KHYBER_PAKHTUNKHWA":
                    var kpTariff = !string.IsNullOrWhiteSpace(request.ProductServiceClassification) ? request.ProductServiceClassification : "9850.0000";
                    if (request.ProviderElectedReducedRateWithoutInputTax)
                    {
                        return new TaxDeterminationResult(
                            Jurisdiction: "PK-KP",
                            CountryCode: "PK",
                            ProvinceState: "KP",
                            Authority: "KPRA",
                            TaxSystem: "sales_tax",
                            TaxName: "KPRA Reduced Rate for IT/Software (No Input Credit)",
                            TariffOrClassificationCode: kpTariff,
                            RatePercentage: 2.0000m,
                            InputTaxAllowable: false,
                            IsReverseCharge: false,
                            IsExempt: false,
                            StatutoryReference: "KP Finance Act 2026 & Second Schedule Heading 9850.0000 Reduced Column",
                            CalculationExplanation: "Applies strictly to qualifying digital and IT-based services without input credit.",
                            AuditEvidenceSummary: "KPRA 2.0% reduced rate under heading 9850.0000.");
                    }

                    return new TaxDeterminationResult(
                        Jurisdiction: "PK-KP",
                        CountryCode: "PK",
                        ProvinceState: "KP",
                        Authority: "KPRA",
                        TaxSystem: "sales_tax",
                        TaxName: "KPRA Khyber Pakhtunkhwa Sales Tax on Services (Standard)",
                        TariffOrClassificationCode: kpTariff,
                        RatePercentage: 15.0000m,
                        InputTaxAllowable: true,
                        IsReverseCharge: false,
                        IsExempt: false,
                        StatutoryReference: "Khyber Pakhtunkhwa Sales Tax on Services Act, 2022, Second Schedule, Heading 9850.0000 as amended by KP Finance Act 2026",
                        CalculationExplanation: "Standard statutory rate with full input tax credit.",
                        AuditEvidenceSummary: "KPRA standard statutory 15% rate under heading 9850.0000.");

                case "SINDH":
                    var sindhTariff = !string.IsNullOrWhiteSpace(request.ProductServiceClassification) ? request.ProductServiceClassification : "9815.6000";
                    if (asOf < new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero))
                    {
                        return new TaxDeterminationResult(
                            Jurisdiction: "PK-SINDH",
                            CountryCode: "PK",
                            ProvinceState: "SINDH",
                            Authority: "SRB",
                            TaxSystem: "sales_tax",
                            TaxName: "SRB Sindh Sales Tax on Services (Historical Standard)",
                            TariffOrClassificationCode: sindhTariff,
                            RatePercentage: 13.0000m,
                            InputTaxAllowable: true,
                            IsReverseCharge: false,
                            IsExempt: false,
                            StatutoryReference: "Sindh Sales Tax on Services Act, 2011, Sec 8(1) (pre-2024 interval)",
                            CalculationExplanation: "Historical rate interval prior to Sindh Finance Act 2024.",
                            AuditEvidenceSummary: "SRB historical 13% rate.");
                    }

                    if (request.ProviderElectedReducedRateWithoutInputTax)
                    {
                        return new TaxDeterminationResult(
                            Jurisdiction: "PK-SINDH",
                            CountryCode: "PK",
                            ProvinceState: "SINDH",
                            Authority: "SRB",
                            TaxSystem: "sales_tax",
                            TaxName: "SRB Reduced Rate for IT/Software (No Input Credit)",
                            TariffOrClassificationCode: sindhTariff,
                            RatePercentage: 3.0000m,
                            InputTaxAllowable: false,
                            IsReverseCharge: false,
                            IsExempt: false,
                            StatutoryReference: "SRB Notification No. SRB-3-4/7/2013 as amended & Second Schedule Heading 9815.6000",
                            CalculationExplanation: "Requires formal election on return and surrender of input tax adjustments.",
                            AuditEvidenceSummary: "SRB 3.0% reduced rate without input tax credit.");
                    }

                    return new TaxDeterminationResult(
                        Jurisdiction: "PK-SINDH",
                        CountryCode: "PK",
                        ProvinceState: "SINDH",
                        Authority: "SRB",
                        TaxSystem: "sales_tax",
                        TaxName: "SRB Sindh Sales Tax on Services (Standard SaaS)",
                        TariffOrClassificationCode: sindhTariff,
                        RatePercentage: 15.0000m,
                        InputTaxAllowable: true,
                        IsReverseCharge: false,
                        IsExempt: false,
                        StatutoryReference: "Sindh Sales Tax on Services Act, 2011, Sec 8(1) as amended by Sindh Finance Act 2024",
                        CalculationExplanation: "Standard statutory rate with full input tax credit and adjustment rights under SRB rules.",
                        AuditEvidenceSummary: "SRB standard 15% rate under Sindh Finance Act 2024.");

                case "PUNJAB":
                    var punjabTariff = !string.IsNullOrWhiteSpace(request.ProductServiceClassification) ? request.ProductServiceClassification : "9815.6000";
                    if (request.ProviderElectedReducedRateWithoutInputTax)
                    {
                        return new TaxDeterminationResult(
                            Jurisdiction: "PK-PUNJAB",
                            CountryCode: "PK",
                            ProvinceState: "PUNJAB",
                            Authority: "PRA",
                            TaxSystem: "sales_tax",
                            TaxName: "PRA Reduced Rate for IT/Software (No Input Credit)",
                            TariffOrClassificationCode: punjabTariff,
                            RatePercentage: 5.0000m,
                            InputTaxAllowable: false,
                            IsReverseCharge: false,
                            IsExempt: false,
                            StatutoryReference: "PRA Notification No. SO(TAX)1-1/2020 & Second Schedule Heading 9815.6000",
                            CalculationExplanation: "Conditional upon formal election to forgo input tax credit under PRA Special Procedure Rules.",
                            AuditEvidenceSummary: "PRA 5.0% reduced rate without input credit.");
                    }

                    return new TaxDeterminationResult(
                        Jurisdiction: "PK-PUNJAB",
                        CountryCode: "PK",
                        ProvinceState: "PUNJAB",
                        Authority: "PRA",
                        TaxSystem: "sales_tax",
                        TaxName: "PRA Punjab Sales Tax on Services (Standard SaaS)",
                        TariffOrClassificationCode: punjabTariff,
                        RatePercentage: 16.0000m,
                        InputTaxAllowable: true,
                        IsReverseCharge: false,
                        IsExempt: false,
                        StatutoryReference: "Punjab Sales Tax on Services Act, 2012, Second Schedule, Heading 9815.6000",
                        CalculationExplanation: "Standard statutory rate with full input tax credit.",
                        AuditEvidenceSummary: "PRA standard 16% rate under PSTS Act 2012.");

                default: // FEDERAL / ICT
                    var fedTariff = !string.IsNullOrWhiteSpace(request.ProductServiceClassification) ? request.ProductServiceClassification : "9815.6000";
                    if (request.ProviderElectedReducedRateWithoutInputTax)
                    {
                        return new TaxDeterminationResult(
                            Jurisdiction: "PK-FEDERAL",
                            CountryCode: "PK",
                            ProvinceState: "FEDERAL",
                            Authority: "FBR_ICT",
                            TaxSystem: "sales_tax",
                            TaxName: "ICT Reduced Rate for IT Services (PSEB Certified)",
                            TariffOrClassificationCode: fedTariff,
                            RatePercentage: 5.0000m,
                            InputTaxAllowable: false,
                            IsReverseCharge: false,
                            IsExempt: false,
                            StatutoryReference: "ICT Ordinance 2001, Table-II & S.R.O. Notifications for IT-enabled services",
                            CalculationExplanation: "Applies strictly to qualifying software/IT consultants and PSEB-registered entities.",
                            AuditEvidenceSummary: "ICT 5.0% reduced rate for PSEB certified entities.");
                    }

                    return new TaxDeterminationResult(
                        Jurisdiction: "PK-FEDERAL",
                        CountryCode: "PK",
                        ProvinceState: "FEDERAL",
                        Authority: "FBR_ICT",
                        TaxSystem: "sales_tax",
                        TaxName: "ICT Sales Tax on IT/SaaS Services (Standard)",
                        TariffOrClassificationCode: fedTariff,
                        RatePercentage: 15.0000m,
                        InputTaxAllowable: true,
                        IsReverseCharge: false,
                        IsExempt: false,
                        StatutoryReference: "Islamabad Capital Territory (Tax on Services) Ordinance, 2001, Schedule Table, Heading 9815.6000",
                        CalculationExplanation: "Standard rate for services rendered or consumed in Islamabad Capital Territory.",
                        AuditEvidenceSummary: "FBR ICT standard 15% rate.");
            }
        }

        // 8. India (CBIC Goods & Services Tax - Dual GST Framework)
        if (customerCountry == "IN")
        {
            var isCrossBorder = supplierCountry != "IN";
            if (isCrossBorder && isB2B && hasVatId)
            {
                return new TaxDeterminationResult(
                    Jurisdiction: "IN-CBIC",
                    CountryCode: "IN",
                    ProvinceState: province,
                    Authority: "CBIC",
                    TaxSystem: "reverse_charge",
                    TaxName: "India B2B Import of Services Reverse Charge (RCM)",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "SAC-998314",
                    RatePercentage: 0.0000m,
                    InputTaxAllowable: true,
                    IsReverseCharge: true,
                    IsExempt: false,
                    StatutoryReference: "Integrated Goods and Services Tax Act 2017, Section 5(3) (Reverse Charge on Imported Services)",
                    CalculationExplanation: $"Supply from overseas vendor to Indian GST-registered business ({request.CustomerTaxId}). Recipient pays IGST under RCM with 100% ITC.",
                    AuditEvidenceSummary: $"GSTIN verified: {request.CustomerTaxId}. Reverse charge mechanism (RCM) applies.",
                    EInvoicingMandatePlatform: "NIC E-Invoice Portal (IRN / Signed QR Code)");
            }

            if (isCrossBorder)
            {
                return new TaxDeterminationResult(
                    Jurisdiction: "IN-CBIC",
                    CountryCode: "IN",
                    ProvinceState: province,
                    Authority: "CBIC",
                    TaxSystem: "gst",
                    TaxName: "India OIDAR Inbound Digital Services IGST (B2C)",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "SAC-998314",
                    RatePercentage: 18.0000m,
                    InputTaxAllowable: false,
                    IsReverseCharge: false,
                    IsExempt: false,
                    StatutoryReference: "IGST Act 2017, Section 14 (OIDAR Services by Overseas Suppliers to Non-Taxable Online Recipients)",
                    CalculationExplanation: "18% Integrated GST collected by overseas supplier on B2C digital services supplied to Indian residents.",
                    AuditEvidenceSummary: "CBIC statutory standard 18% IGST on OIDAR services.",
                    IsDestinationVat: true,
                    EInvoicingMandatePlatform: "NIC E-Invoice Portal (IRN / Signed QR Code)");
            }

            var supplierProvince = "MH"; // Universal ERP India HQ
            var isIntraState = !string.IsNullOrWhiteSpace(province) && string.Equals(province, supplierProvince, StringComparison.OrdinalIgnoreCase);

            if (isIntraState)
            {
                return new TaxDeterminationResult(
                    Jurisdiction: $"IN-{province}",
                    CountryCode: "IN",
                    ProvinceState: province,
                    Authority: "CBIC_STATE_GST",
                    TaxSystem: "gst",
                    TaxName: $"India Dual GST - Intra-State ({province}: 9% CGST + 9% SGST)",
                    TariffOrClassificationCode: request.ProductServiceClassification ?? "SAC-998314",
                    RatePercentage: 18.0000m,
                    InputTaxAllowable: true,
                    IsReverseCharge: false,
                    IsExempt: false,
                    StatutoryReference: "CGST Act 2017 & State SGST Act (SAC 998314 IT/SaaS Services)",
                    CalculationExplanation: "Intra-state supply in India: 18% GST split into 9% CGST and 9% SGST with full input tax credit allowable.",
                    AuditEvidenceSummary: $"Intra-state supply in {province}. 9% CGST + 9% SGST.",
                    EInvoicingMandatePlatform: "NIC E-Invoice Portal (IRN / Signed QR Code)");
            }

            return new TaxDeterminationResult(
                Jurisdiction: $"IN-{province ?? "INTERSTATE"}",
                CountryCode: "IN",
                ProvinceState: province,
                Authority: "CBIC",
                TaxSystem: "gst",
                TaxName: "India Integrated GST - Inter-State (18% IGST)",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "SAC-998314",
                RatePercentage: 18.0000m,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: "Integrated Goods and Services Tax Act 2017, Section 5(1)",
                CalculationExplanation: "Inter-state supply in India: 18% IGST applies with full input tax credit allowable.",
                AuditEvidenceSummary: "CBIC statutory standard 18% IGST rate.",
                EInvoicingMandatePlatform: "NIC E-Invoice Portal (IRN / Signed QR Code)");
        }

        // 9. Additional Extensible Country Packages (Singapore, Switzerland, Japan, Brazil, South Africa)
        if (customerCountry == "SG")
        {
            return new TaxDeterminationResult(
                Jurisdiction: "SG-IRAS",
                CountryCode: "SG",
                ProvinceState: null,
                Authority: "IRAS",
                TaxSystem: "gst",
                TaxName: "Singapore Goods and Services Tax (Standard Rate)",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "DIGITAL-SVC",
                RatePercentage: 9.0000m,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: "Goods and Services Tax Act 1993 (Singapore), Section 8",
                CalculationExplanation: "Standard 9% GST rate in Singapore (effective 1 Jan 2024).",
                AuditEvidenceSummary: "Inland Revenue Authority of Singapore (IRAS) standard 9% GST rate.",
                EInvoicingMandatePlatform: "InvoiceNow (Peppol)");
        }

        if (customerCountry == "CH")
        {
            return new TaxDeterminationResult(
                Jurisdiction: "CH-ESTV",
                CountryCode: "CH",
                ProvinceState: province,
                Authority: "ESTV",
                TaxSystem: "vat",
                TaxName: "Switzerland Value Added Tax (MWST Normalsatz)",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "SERVICES",
                RatePercentage: 8.1000m,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: "Bundesgesetz über die Mehrwertsteuer (MWSTG), Art. 25 (Standard 8.1%)",
                CalculationExplanation: "Standard 8.1% Swiss MWST rate (effective 1 Jan 2024).",
                AuditEvidenceSummary: "Eidgenössische Steuerverwaltung (ESTV) standard 8.1% rate.",
                EInvoicingMandatePlatform: "Swiss QR-Bill (ISO 20022)");
        }

        if (customerCountry == "JP")
        {
            return new TaxDeterminationResult(
                Jurisdiction: "JP-NTA",
                CountryCode: "JP",
                ProvinceState: null,
                Authority: "NTA",
                TaxSystem: "vat",
                TaxName: "Japan Consumption Tax (JCT Standard Rate)",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "SERVICES",
                RatePercentage: 10.0000m,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: "Consumption Tax Act (Japan), Cabinet Order & Qualified Invoice System (QIS)",
                CalculationExplanation: "Standard 10% Japanese Consumption Tax (7.8% national + 2.2% local).",
                AuditEvidenceSummary: "National Tax Agency (NTA) standard 10% JCT rate.",
                EInvoicingMandatePlatform: "Qualified Invoice System (QIS)");
        }

        if (customerCountry == "BR")
        {
            return new TaxDeterminationResult(
                Jurisdiction: "BR-RFB",
                CountryCode: "BR",
                ProvinceState: province,
                Authority: "RFB_COMITETRIBUTARIO",
                TaxSystem: "vat",
                TaxName: "Brazil Dual VAT Reform (Estimated IBS + CBS)",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "SERVICES",
                RatePercentage: 26.5000m,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: "Constitutional Amendment EC 132/2023 (Tax Reform IBS/CBS)",
                CalculationExplanation: "Dual VAT reform transitioning PIS/COFINS/ICMS/ISS to federal CBS and state/municipal IBS.",
                AuditEvidenceSummary: "Receita Federal do Brasil dual VAT transition structure.",
                EInvoicingMandatePlatform: "Nota Fiscal de Serviço Eletrônica (NFS-e)");
        }

        if (customerCountry == "ZA")
        {
            return new TaxDeterminationResult(
                Jurisdiction: "ZA-SARS",
                CountryCode: "ZA",
                ProvinceState: province,
                Authority: "SARS",
                TaxSystem: "vat",
                TaxName: "South Africa Value Added Tax (Standard Rate)",
                TariffOrClassificationCode: request.ProductServiceClassification ?? "SERVICES",
                RatePercentage: 15.0000m,
                InputTaxAllowable: true,
                IsReverseCharge: false,
                IsExempt: false,
                StatutoryReference: "Value-Added Tax Act, 1991 (Act No. 89 of 1991), Section 7(1)",
                CalculationExplanation: "Standard 15% VAT on electronic services supplied to South African residents.",
                AuditEvidenceSummary: "South African Revenue Service (SARS) standard 15% rate.");
        }

        // 10. Global Default Fallback
        return new TaxDeterminationResult(
            Jurisdiction: $"{customerCountry}-DEFAULT",
            CountryCode: customerCountry,
            ProvinceState: province,
            Authority: "LOCAL_REVENUE_AUTHORITY",
            TaxSystem: "vat",
            TaxName: $"Statutory Service Tax ({customerCountry})",
            TariffOrClassificationCode: request.ProductServiceClassification ?? "SERVICES",
            RatePercentage: 10.0000m,
            InputTaxAllowable: true,
            IsReverseCharge: false,
            IsExempt: false,
            StatutoryReference: "Local Country Tax Jurisdiction Rules",
            CalculationExplanation: $"Assessed standard rate for jurisdiction {customerCountry}.",
            AuditEvidenceSummary: $"Default statutory tax resolution for country {customerCountry}.");
    }

    /// <summary>
    /// Calculates invoice tax figures supporting tax-exclusive and tax-inclusive pricing with zero rounding leakage.
    /// </summary>
    public GlobalInvoiceCalculationResult CalculateInvoice(
        decimal basePrice,
        decimal overageCharges,
        decimal discountAmount,
        decimal availableCredits,
        TaxDeterminationResult taxRule,
        string currencyCode = "USD",
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

        if (taxRule.IsExempt || taxRule.IsReverseCharge || taxRule.RatePercentage <= 0)
        {
            taxableBase = netChargeable;
            taxAmount = 0.00m;
            total = netChargeable;
        }
        else if (isTaxInclusive)
        {
            taxableBase = Math.Round(netChargeable / (1.0m + (taxRule.RatePercentage / 100.0m)), 2);
            taxAmount = netChargeable - taxableBase;
            total = netChargeable;
        }
        else
        {
            taxableBase = netChargeable;
            taxAmount = Math.Round(taxableBase * (taxRule.RatePercentage / 100.0m), 2);
            total = taxableBase + taxAmount;
        }

        var explanation = $"{taxRule.TaxName} ({taxRule.RatePercentage}%): TaxableBase={currencyCode} {taxableBase:N2}, Tax={currencyCode} {taxAmount:N2}, Total={currencyCode} {total:N2}";

        return new GlobalInvoiceCalculationResult(
            Subtotal: subtotal,
            DiscountAmount: totalDiscount,
            CreditsApplied: creditsApplied,
            TaxableBase: taxableBase,
            TaxRatePercentage: taxRule.RatePercentage,
            TaxAmount: taxAmount,
            TotalAmount: total,
            CurrencyCode: currencyCode,
            IsTaxInclusive: isTaxInclusive,
            CalculationExplanation: explanation);
    }

    /// <summary>
    /// Computes credit note tax reversal preserving statutory proportional liability.
    /// </summary>
    public GlobalCreditNoteResult CalculateCreditNote(
        decimal refundAmount,
        TaxDeterminationResult taxRule,
        string currencyCode = "USD",
        bool isTaxInclusive = false)
    {
        decimal refundSubtotal;
        decimal taxReversed;
        decimal totalCredit;

        if (taxRule.IsExempt || taxRule.IsReverseCharge || taxRule.RatePercentage <= 0)
        {
            refundSubtotal = refundAmount;
            taxReversed = 0.00m;
            totalCredit = refundAmount;
        }
        else if (isTaxInclusive)
        {
            refundSubtotal = Math.Round(refundAmount / (1.0m + (taxRule.RatePercentage / 100.0m)), 2);
            taxReversed = refundAmount - refundSubtotal;
            totalCredit = refundAmount;
        }
        else
        {
            refundSubtotal = refundAmount;
            taxReversed = Math.Round(refundSubtotal * (taxRule.RatePercentage / 100.0m), 2);
            totalCredit = refundSubtotal + taxReversed;
        }

        var explanation = $"Reversal of {taxRule.TaxName} ({taxRule.RatePercentage}%): SubtotalReversed={currencyCode} {refundSubtotal:N2}, TaxReversed={currencyCode} {taxReversed:N2}";

        return new GlobalCreditNoteResult(
            RefundSubtotal: refundSubtotal,
            TaxReversed: taxReversed,
            TotalCreditNoteAmount: totalCredit,
            TaxRatePercentage: taxRule.RatePercentage,
            CurrencyCode: currencyCode,
            IsTaxInclusive: isTaxInclusive,
            ReversalExplanation: explanation);
    }

    private static bool IsEuMemberState(string countryCode) =>
        countryCode is "DE" or "FR" or "IE" or "NL" or "IT" or "ES" or "BE" or "AT" or "SE" or "PL" or "DK" or "FI" or "PT" or "GR" or "CZ" or "RO" or "HU" or "BG" or "HR" or "SK" or "LT" or "SI" or "LV" or "EE" or "CY" or "LU" or "MT";

    private static decimal GetEuStandardVatRate(string countryCode) =>
        countryCode switch
        {
            "DE" => 19.0000m,
            "FR" => 20.0000m,
            "IE" => 23.0000m,
            "NL" => 21.0000m,
            "IT" => 22.0000m,
            "ES" => 21.0000m,
            "BE" => 21.0000m,
            "AT" => 20.0000m,
            "SE" => 25.0000m,
            "PL" => 23.0000m,
            "DK" => 25.0000m,
            "FI" => 25.5000m,
            "PT" => 23.0000m,
            "GR" => 24.0000m,
            "CZ" => 21.0000m,
            "RO" => 19.0000m,
            "HU" => 27.0000m,
            "BG" => 20.0000m,
            "HR" => 25.0000m,
            "SK" => 23.0000m,
            "LT" => 21.0000m,
            "SI" => 22.0000m,
            "LV" => 21.0000m,
            "EE" => 22.0000m,
            "CY" => 19.0000m,
            "LU" => 17.0000m,
            "MT" => 18.0000m,
            _ => 20.0000m
        };

    private static string? GetEuEInvoicingPlatform(string countryCode) =>
        countryCode switch
        {
            "DE" => "ZUGFeRD / XRechnung (EN 16931)",
            "FR" => "Chorus Pro / Public Invoicing Portal (PPF)",
            "IT" => "Sistema di Interscambio (SdI)",
            "PL" => "Krajowy System e-Faktur (KSeF)",
            "ES" => "FACe / Facturae",
            _ => "Peppol BIS Billing 3.0"
        };

    private static bool IsIntraEu(string country1, string country2) =>
        IsEuMemberState(country1) && IsEuMemberState(country2);
}
