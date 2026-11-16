using System;
using System.Linq;
using UniversalErp.Application.Localization;
using Xunit;

namespace UniversalErp.Api.Tests;

public class GlobalLocalizationTests
{
    private readonly GlobalTaxEngine _taxEngine = new();
    private readonly GlobalPayrollEngine _payrollEngine = new();
    private readonly CurrencyFormattingService _currencyFormatter = new();

    // ── 1. Global Tax Engine: United States Sales Tax ────────────────────────

    [Theory]
    [InlineData("NY", 8.8750, "NYS_TAX", "sales_tax", false)]
    [InlineData("TX", 6.6000, "TX_COMPTROLLER", "sales_tax", false)]
    [InlineData("CA", 0.0000, "CA_CDTFA", "exempt", true)]
    [InlineData("WA", 6.5000, "WA_DOR", "sales_tax", false)]
    public void DetermineTax_UnitedStatesDestinations_ResolvesStateSpecificRules(
        string state, decimal expectedRate, string expectedAuthority, string expectedTaxSystem, bool expectedExempt)
    {
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "US",
            SupplierTaxId: "US-EIN-123456789",
            CustomerCountryCode: "US",
            CustomerProvinceState: state,
            CustomerPostalCode: "10001",
            CustomerType: "b2b",
            ProductServiceClassification: "NAICS-518210",
            IsSaaSOrDigitalService: true));

        Assert.Equal("US", result.CountryCode);
        Assert.Equal(expectedRate, result.RatePercentage);
        Assert.Equal(expectedAuthority, result.Authority);
        Assert.Equal(expectedTaxSystem, result.TaxSystem);
        Assert.Equal(expectedExempt, result.IsExempt);
        Assert.DoesNotContain("Pakistan", result.StatutoryReference);
    }

    [Fact]
    public void DetermineTax_UnitedStatesWithResaleCertificate_GrantsTaxExemption()
    {
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "US",
            SupplierTaxId: "US-EIN-123456789",
            CustomerCountryCode: "US",
            CustomerProvinceState: "NY",
            CustomerPostalCode: "10001",
            CustomerType: "b2b",
            ExemptionCertificateNumber: "ST-120-NY-987654321"));

        Assert.Equal(0.0000m, result.RatePercentage);
        Assert.True(result.IsExempt);
        Assert.Contains("ST-120-NY-987654321", result.CalculationExplanation);
    }

    // ── 2. Global Tax Engine: United Kingdom HMRC VAT ────────────────────────

    [Fact]
    public void DetermineTax_UnitedKingdomDomesticSaaS_AppliesStandard20PercentVAT()
    {
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "GB",
            SupplierTaxId: "GB123456789",
            CustomerCountryCode: "GB",
            CustomerProvinceState: "ENGLAND",
            CustomerPostalCode: "EC1A 1BB",
            CustomerType: "all",
            ProductServiceClassification: "CPA-62.01",
            IsSaaSOrDigitalService: true));

        Assert.Equal("GB", result.CountryCode);
        Assert.Equal(20.0000m, result.RatePercentage);
        Assert.Equal("HMRC", result.Authority);
        Assert.Equal("vat", result.TaxSystem);
        Assert.True(result.InputTaxAllowable);
    }

    // ── 3. Global Tax Engine: European Union OSS & Reverse Charge ─────────────

    [Theory]
    [InlineData("DE", 19.0000, "EU_REVENUE_DE")]
    [InlineData("FR", 20.0000, "EU_REVENUE_FR")]
    [InlineData("IE", 23.0000, "EU_REVENUE_IE")]
    public void DetermineTax_EuropeanUnionB2CDestination_ResolvesMemberStateOSSRates(
        string euCountry, decimal expectedRate, string expectedAuthority)
    {
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "IE",
            SupplierTaxId: "IE9876543W",
            CustomerCountryCode: euCountry,
            CustomerProvinceState: null,
            CustomerPostalCode: null,
            CustomerType: "b2c",
            ProductServiceClassification: "CPA-62.01",
            IsSaaSOrDigitalService: true));

        Assert.Equal(euCountry, result.CountryCode);
        Assert.Equal(expectedRate, result.RatePercentage);
        Assert.Equal(expectedAuthority, result.Authority);
        Assert.Equal("vat", result.TaxSystem);
    }

    [Fact]
    public void DetermineTax_IntraEuB2BSupply_AppliesReverseChargeUnderArticle196()
    {
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "IE",
            SupplierTaxId: "IE9876543W",
            CustomerCountryCode: "DE",
            CustomerProvinceState: "BERLIN",
            CustomerPostalCode: "10115",
            CustomerType: "b2b",
            CustomerTaxId: "DE123456789", // Verified VIES VAT ID
            ProductServiceClassification: "CPA-62.01",
            IsSaaSOrDigitalService: true));

        Assert.Equal("DE", result.CountryCode);
        Assert.Equal(0.0000m, result.RatePercentage);
        Assert.True(result.IsReverseCharge);
        Assert.Equal("reverse_charge", result.TaxSystem);
        Assert.Contains("Article 196", result.StatutoryReference);
    }

    // ── 4. Global Tax Engine: UAE & Saudi Arabia VAT ──────────────────────────

    [Fact]
    public void DetermineTax_UnitedArabEmirates_AppliesStandard5PercentFTAVAT()
    {
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "AE",
            SupplierTaxId: "100123456700003",
            CustomerCountryCode: "AE",
            CustomerProvinceState: "DUBAI",
            CustomerPostalCode: null,
            CustomerType: "all",
            ProductServiceClassification: "UNSPSC-43231500",
            IsSaaSOrDigitalService: true));

        Assert.Equal("AE", result.CountryCode);
        Assert.Equal(5.0000m, result.RatePercentage);
        Assert.Equal("FTA", result.Authority);
        Assert.Equal("vat", result.TaxSystem);
        Assert.Contains("Cabinet Decision No. 52/2017", result.StatutoryReference);
    }

    [Fact]
    public void DetermineTax_SaudiArabia_AppliesStandard15PercentZATCAVAT()
    {
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "SA",
            SupplierTaxId: "300123456700003",
            CustomerCountryCode: "SA",
            CustomerProvinceState: "RIYADH",
            CustomerPostalCode: null,
            CustomerType: "all",
            ProductServiceClassification: "UNSPSC-43231500",
            IsSaaSOrDigitalService: true));

        Assert.Equal("SA", result.CountryCode);
        Assert.Equal(15.0000m, result.RatePercentage);
        Assert.Equal("ZATCA", result.Authority);
        Assert.Equal("vat", result.TaxSystem);
        Assert.Contains("Royal Decree No. (A/638)", result.StatutoryReference);
    }

    // ── 5. Cross-Country Isolation: Zero Contamination Tests ─────────────────

    [Fact]
    public void DetermineTax_UnitedStatesTransaction_IsCompletelyIsolatedFromPakistanRules()
    {
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "US",
            SupplierTaxId: "US-123",
            CustomerCountryCode: "US",
            CustomerProvinceState: "NY",
            CustomerPostalCode: "10001"));

        // Must NOT reference FBR, SRB, PRA, Balochistan, or Pakistani tariff codes
        Assert.DoesNotContain("FBR", result.Authority);
        Assert.DoesNotContain("SRB", result.Authority);
        Assert.DoesNotContain("9815.6000", result.TariffOrClassificationCode);
        Assert.DoesNotContain("9868.0000", result.TariffOrClassificationCode);
        Assert.Equal("US", result.CountryCode);
    }

    // ── 6. Global Payroll Engine: United States (FICA & FIT) ─────────────────

    [Fact]
    public void CalculatePayroll_UnitedStates_ComputesFicaMedicareAndFit()
    {
        var context = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "US",
            WorkJurisdiction: "CA",
            SkillCategory: "skilled",
            BaseSalary: 8000.00m, // $8,000 / month = $96,000 / year
            Allowances: 500.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 1, 1),
            CurrencyCode: "USD");

        var result = _payrollEngine.CalculatePayroll(context);

        Assert.Equal("US", result.CountryCode);
        Assert.Equal("USD", result.CurrencyCode);
        Assert.Equal(8500.00m, result.GrossPay);

        // FICA: 6.2% of $8,500 = $527.00
        Assert.Equal(527.00m, result.PayLines.First(l => l.Category == "fica_social_security").Amount);
        // Medicare: 1.45% of $8,500 = $123.25
        Assert.Equal(123.25m, result.PayLines.First(l => l.Category == "fica_medicare").Amount);
        // FIT Withholding > 0
        Assert.True(result.IncomeTaxWithholding > 0);
        // Employer FICA matching
        Assert.Equal(527.00m, result.PayLines.First(l => l.Category == "fica_social_security_er").Amount);
        // Minimum wage compliance check ($16/hr * 173.33 = $2,773.28)
        Assert.True(result.MinimumWageCompliant);
    }

    // ── 7. Global Payroll Engine: United Kingdom (HMRC PAYE & NIC) ────────────

    [Fact]
    public void CalculatePayroll_UnitedKingdom_ComputesPayeAndNic()
    {
        var context = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "GB",
            WorkJurisdiction: "UK",
            SkillCategory: "skilled",
            BaseSalary: 4000.00m, // £4,000 / month
            Allowances: 0.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 4, 6),
            CurrencyCode: "GBP");

        var result = _payrollEngine.CalculatePayroll(context);

        Assert.Equal("GB", result.CountryCode);
        Assert.Equal("GBP", result.CurrencyCode);
        Assert.Equal(4000.00m, result.GrossPay);

        // PAYE Taxable = £4,000 - £1,047.50 = £2,952.50 at 20% = £590.50
        Assert.Equal(590.50m, result.IncomeTaxWithholding);
        // Employee NIC: (£4,000 - £1,048) * 8% = £236.16
        Assert.Equal(236.16m, result.EmployeeSocialSecurity);
        // Net pay verified
        Assert.Equal(4000.00m - 590.50m - 236.16m, result.NetPay);
    }

    // ── 8. Global Payroll Engine: United Arab Emirates (GPSSA & EOSG) ─────────

    [Fact]
    public void CalculatePayroll_UnitedArabEmiratesNational_ComputesGpssaPensionAndZeroTax()
    {
        var context = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "AE",
            WorkJurisdiction: "DUBAI",
            SkillCategory: "skilled",
            BaseSalary: 20000.00m, // AED 20,000
            Allowances: 5000.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 1, 1),
            CurrencyCode: "AED",
            IsNationalEmployee: true);

        var result = _payrollEngine.CalculatePayroll(context);

        Assert.Equal("AE", result.CountryCode);
        Assert.Equal("AED", result.CurrencyCode);
        Assert.Equal(25000.00m, result.GrossPay);
        // UAE personal income tax is strictly 0.00
        Assert.Equal(0.00m, result.IncomeTaxWithholding);
        // GPSSA Employee Share: 5% of AED 25,000 = AED 1,250.00
        Assert.Equal(1250.00m, result.EmployeePension);
        // GPSSA Employer Share: 12.5% of AED 25,000 = AED 3,125.00
        Assert.Equal(3125.00m, result.EmployerPension);
        Assert.Equal(23750.00m, result.NetPay);
    }

    // ── 9. Global Payroll Engine: Pakistan (FBR TY2027 & EOBI) ───────────────

    [Fact]
    public void CalculatePayroll_Pakistan_ComputesFbrSalariedSlabsAndEobi()
    {
        var context = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "PK",
            WorkJurisdiction: "SINDH",
            SkillCategory: "unskilled",
            BaseSalary: 100000.00m, // PKR 100,000 / month = PKR 1,200,000 / year (Slab 2 top)
            Allowances: 0.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 7, 1),
            CurrencyCode: "PKR");

        var result = _payrollEngine.CalculatePayroll(context);

        Assert.Equal("PK", result.CountryCode);
        Assert.Equal("PKR", result.CurrencyCode);
        Assert.Equal(100000.00m, result.GrossPay);

        // FBR TY2027 Slab 2: 1% of (1,200,000 - 600,000) = PKR 6,000 / 12 = PKR 500.00
        Assert.Equal(500.00m, result.IncomeTaxWithholding);
        // EOBI Employee Share: 1% of PKR 37,000 base = PKR 370.00
        Assert.Equal(370.00m, result.EmployeePension);
        // EOBI Employer Share: 5% of PKR 37,000 base = PKR 1,850.00
        Assert.Equal(1850.00m, result.EmployerPension);
        // Provincial Social Security Employer Share: 6% of PKR 43,000 Sindh ceiling = PKR 2,580.00
        Assert.Equal(2580.00m, result.EmployerSocialSecurity);
    }

    // ── 10. Multi-Currency Formatting Service ────────────────────────────────

    [Theory]
    [InlineData(1250.50, "USD", "$1,250.50")]
    [InlineData(950.00, "EUR", "€950.00")]
    [InlineData(450.75, "GBP", "£450.75")]
    [InlineData(5000.00, "AED", "AED 5,000.00")]
    [InlineData(10000.00, "SAR", "SAR 10,000.00")]
    [InlineData(75000.00, "PKR", "PKR 75,000.00")]
    [InlineData(15000.00, "JPY", "¥15,000")] // 0 minor units
    [InlineData(123.456, "BHD", "BHD 123.456")] // 3 minor units
    public void CurrencyFormattingService_FormatsAllGlobalCurrenciesWithCorrectDecimals(
        decimal amount, string currency, string expectedFormatted)
    {
        var formatted = _currencyFormatter.Format(amount, currency, "en-US");
        Assert.Equal(expectedFormatted, formatted);
    }
}
