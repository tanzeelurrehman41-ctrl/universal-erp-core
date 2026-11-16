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
    [InlineData("TX_STATE", 5.0000, "TX_COMPTROLLER", "sales_tax", false)]
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
        Assert.True(result.TaxSystem is "eu_oss" or "vat");
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
        // 2026–27 Employer NIC: 15% on (£4,000 - £417) = 15% of £3,583 = £537.45
        Assert.Equal(537.45m, result.EmployerSocialSecurity);
        // Net pay verified
        Assert.Equal(4000.00m - 590.50m - 236.16m, result.NetPay);
    }

    [Fact]
    public void CalculatePayroll_UnitedStates2026_AppliesUpdatedOasdiCapAndAdditionalMedicare()
    {
        // High earner: $25,000 / month ($300,000 / year)
        var context = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "US",
            WorkJurisdiction: "NY",
            SkillCategory: "executive",
            BaseSalary: 25000.00m,
            Allowances: 0.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 1, 1),
            CurrencyCode: "USD",
            FilingStatus: "single");

        var result = _payrollEngine.CalculatePayroll(context);

        Assert.Equal("US", result.CountryCode);
        Assert.Equal(25000.00m, result.GrossPay);

        // 2026 OASDI Wage Base = $184,500 ($15,375.00/mo) -> 6.2% of $15,375 = $953.25
        var ssLine = result.PayLines.First(l => l.Category == "fica_social_security");
        Assert.Equal(953.25m, ssLine.Amount);

        // Base Medicare = 1.45% of $25,000 = $362.50
        var medLine = result.PayLines.First(l => l.Category == "fica_medicare");
        Assert.Equal(362.50m, medLine.Amount);

        // Additional Medicare (0.9% on earnings over $16,666.67/mo = $8,333.33 * 0.009 = $75.00)
        var addMedLine = result.PayLines.FirstOrDefault(l => l.Category == "additional_medicare");
        Assert.NotNull(addMedLine);
        Assert.Equal(75.00m, addMedLine.Amount);

        // FUTA employer tax (0.6% on $583.33 = $3.50)
        var futaLine = result.PayLines.FirstOrDefault(l => l.Category == "futa_tax_er");
        Assert.NotNull(futaLine);
        Assert.Equal(3.50m, futaLine.Amount);

        // Recalculated 2026 Federal Income Tax (IRS Pub 15-T Single, StdDed $16,100):
        // Taxable = $300,000 - $16,100 = $283,900. Bracket tax = $58,442 + 35% on $27,700 = $68,137 / 12 = $5,678.08
        Assert.Equal(5678.08m, result.IncomeTaxWithholding);

        // Total Employee Deductions = $953.25 + $362.50 + $75.00 + $5,678.08 = $7,068.83
        Assert.Equal(7068.83m, result.TotalEmployeeDeductions);

        // Net Take-Home Pay = $25,000.00 - $7,068.83 = $17,931.17
        Assert.Equal(17931.17m, result.NetPay);

        // Total Employer Obligations = $953.25 (SS) + $362.50 (Med) + $3.50 (FUTA) + $22.50 (SUTA) = $1,341.75
        Assert.Equal(1341.75m, result.EmployerSocialSecurity);
    }

    [Fact]
    public void CalculatePayroll_UnitedStatesMarriedFilingJointly_AppliesMfjStandardDeductionAndBrackets()
    {
        var context = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "US",
            WorkJurisdiction: "TX",
            SkillCategory: "skilled",
            BaseSalary: 10000.00m, // $120,000 / year
            Allowances: 0.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 1, 1),
            CurrencyCode: "USD",
            FilingStatus: "married_joint");

        var result = _payrollEngine.CalculatePayroll(context);

        Assert.Equal("US", result.CountryCode);
        // Annual gross: $120,000. 2026 MFJ Standard Deduction = $32,200. Taxable = $87,800.
        // Tax = $2,480 (10% on $24,800) + 12% on ($87,800 - $24,800 = $63,000) = $2,480 + $7,560 = $10,040 / 12 = $836.67
        Assert.Equal(836.67m, result.IncomeTaxWithholding);
    }

    [Fact]
    public void CalculatePayroll_UnitedKingdomScottishTaxAndStudentLoans_CalculatesAccurately()
    {
        var context = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "GB",
            WorkJurisdiction: "SCOTLAND",
            SkillCategory: "professional",
            BaseSalary: 5000.00m, // £5,000 / month = £60,000 / year
            Allowances: 0.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 4, 6),
            CurrencyCode: "GBP",
            StudentLoanPlan: "Plan4", // Scottish student loan
            PensionAutoEnrolment: true);

        var result = _payrollEngine.CalculatePayroll(context);

        Assert.Equal("GB", result.CountryCode);
        Assert.Equal("SCOTLAND", result.WorkJurisdiction);
        Assert.Equal(5000.00m, result.GrossPay);

        // Scottish 6-band Income Tax: Personal allowance £1,047.50 -> Taxable £3,952.50
        // Starter 19% on £192.17 (£36.51) + Basic 20% on £973.75 (£194.75) + Intermediate 21% on £1,425.08 (£299.27) + Higher 42% on £1,361.50 (£571.83) = £1,102.36
        Assert.Equal(1102.36m, result.IncomeTaxWithholding);

        // Student loan Plan 4: 9% over £2,816.25/mo -> (£5,000 - £2,816.25) * 0.09 = £196.54
        var slLine = result.PayLines.FirstOrDefault(l => l.Category == "student_loan");
        Assert.NotNull(slLine);
        Assert.Equal(196.54m, slLine.Amount);

        // Workplace pension auto-enrolment: Qualifying earnings between £520 and £4,189 = £3,669
        // EE 5% = £183.45. ER 3% = £110.07.
        var penEe = result.PayLines.FirstOrDefault(l => l.Category == "workplace_pension_ee");
        Assert.NotNull(penEe);
        Assert.Equal(183.45m, penEe.Amount);
    }

    [Fact]
    public void CalculatePayroll_UnitedKingdomCategoryLetters_AppliesCorrectExceptions()
    {
        // Category C: Over State Pension Age (0% EE NIC, standard 15% ER NIC)
        var contextCatC = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "GB",
            WorkJurisdiction: "ENGLAND",
            SkillCategory: "senior",
            BaseSalary: 3000.00m,
            Allowances: 0.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 4, 6),
            CurrencyCode: "GBP",
            UkCategoryLetter: "C");

        var resultCatC = _payrollEngine.CalculatePayroll(contextCatC);
        Assert.Equal(0.00m, resultCatC.EmployeeSocialSecurity);
        // ER NIC: (£3,000 - £417) * 15% = £387.45
        Assert.Equal(387.45m, resultCatC.EmployerSocialSecurity);
    }

    [Fact]
    public void DetermineTax_CrossBorderEuB2C_AppliesDestinationVatUnderOss()
    {
        // US SaaS provider selling to German consumer (B2C)
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "US",
            SupplierTaxId: "US-12345",
            CustomerCountryCode: "DE",
            CustomerProvinceState: "BAVARIA",
            CustomerPostalCode: "80331",
            CustomerType: "b2c",
            ProductServiceClassification: "SAAS-SUBSCRIPTION",
            IsSaaSOrDigitalService: true));

        // Must NOT be 0% export: Non-Union OSS destination principle charges German 19% MwSt
        Assert.Equal("DE", result.CountryCode);
        Assert.Equal(19.0000m, result.RatePercentage);
        Assert.Equal("eu_oss", result.TaxSystem);
        Assert.True(result.IsDestinationVat);
        Assert.False(result.IsReverseCharge);
    }

    [Fact]
    public void DetermineTax_CrossBorderUkB2C_AppliesUkDestinationVat()
    {
        // US SaaS provider selling to UK consumer (B2C)
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "US",
            SupplierTaxId: "US-12345",
            CustomerCountryCode: "GB",
            CustomerProvinceState: "LONDON",
            CustomerPostalCode: "W1A 1AA",
            CustomerType: "b2c",
            ProductServiceClassification: "SAAS-SUBSCRIPTION",
            IsSaaSOrDigitalService: true));

        // Must NOT be 0% export: UK overseas vendor rules require 20% UK VAT
        Assert.Equal("GB", result.CountryCode);
        Assert.Equal(20.0000m, result.RatePercentage);
        Assert.Equal("vat", result.TaxSystem);
        Assert.True(result.IsDestinationVat);
    }

    // ── 8. Global Payroll Engine: United Arab Emirates (GPSSA & EOSG) ─────────

    [Fact]
    public void CalculatePayroll_UnitedArabEmiratesLaw7_1999_ComputesLegacyRatesAndGovtSubsidy()
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
            IsNationalEmployee: true,
            UaePensionScheme: "LAW7_1999",
            UaeSector: "private");

        var result = _payrollEngine.CalculatePayroll(context);

        Assert.Equal("AE", result.CountryCode);
        Assert.Equal("AED", result.CurrencyCode);
        Assert.Equal(25000.00m, result.GrossPay);
        // UAE personal income tax is strictly 0.00
        Assert.Equal(0.00m, result.IncomeTaxWithholding);
        // Law 7 of 1999: Employee Share 5% of AED 25,000 = AED 1,250.00
        Assert.Equal(1250.00m, result.EmployeePension);
        // Law 7 of 1999: Employer Share 12.5% of AED 25,000 = AED 3,125.00
        var erPensionLine = result.PayLines.First(l => l.Category == "uae_national_pension_er");
        Assert.Equal(3125.00m, erPensionLine.Amount);
        // Law 7 of 1999: Government Subsidy 2.5% of AED 25,000 = AED 625.00
        var govSubsidyLine = result.PayLines.First(l => l.Category == "uae_govt_pension_subsidy");
        Assert.Equal(625.00m, govSubsidyLine.Amount);
        Assert.Equal(23750.00m, result.NetPay);
    }

    [Fact]
    public void CalculatePayroll_UnitedArabEmiratesDecreeLaw57_2023_ComputesNewGpssaRates()
    {
        var context = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "AE",
            WorkJurisdiction: "DUBAI",
            SkillCategory: "skilled",
            BaseSalary: 20000.00m,
            Allowances: 5000.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 1, 1),
            CurrencyCode: "AED",
            IsNationalEmployee: true,
            UaePensionScheme: "DECREELAW57_2023",
            UaeSector: "private");

        var result = _payrollEngine.CalculatePayroll(context);

        Assert.Equal("AE", result.CountryCode);
        Assert.Equal(25000.00m, result.GrossPay);
        // Decree-Law 57/2023: EE Share 11% of AED 25,000 = AED 2,750.00
        Assert.Equal(2750.00m, result.EmployeePension);
        // Decree-Law 57/2023: ER Share 15% (for salary >= 20k) = AED 3,750.00
        var erPensionLine = result.PayLines.First(l => l.Category == "uae_national_pension_er");
        Assert.Equal(3750.00m, erPensionLine.Amount);
        Assert.Equal(22250.00m, result.NetPay);
    }

    [Fact]
    public void CalculatePayroll_UnitedArabEmiratesAbuDhabiAndSharjahFunds_CalculatesSpecificRates()
    {
        // Abu Dhabi Pension Fund (ADPF: 11% EE, 15% ER, cap AED 100k)
        var contextAdpf = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "AE",
            WorkJurisdiction: "ABU_DHABI",
            SkillCategory: "skilled",
            BaseSalary: 30000.00m,
            Allowances: 10000.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 1, 1),
            CurrencyCode: "AED",
            IsNationalEmployee: true,
            UaePensionScheme: "ADPF");

        var resultAdpf = _payrollEngine.CalculatePayroll(contextAdpf);
        Assert.Equal(4400.00m, resultAdpf.EmployeePension); // 11% of 40,000
        Assert.Equal(6000.00m, resultAdpf.PayLines.First(l => l.Category == "uae_national_pension_er").Amount); // 15% of 40,000

        // Sharjah Social Security Fund (SSSF: 5% EE, 15% ER, cap AED 50k)
        var contextSssf = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "AE",
            WorkJurisdiction: "SHARJAH",
            SkillCategory: "skilled",
            BaseSalary: 30000.00m,
            Allowances: 10000.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 1, 1),
            CurrencyCode: "AED",
            IsNationalEmployee: true,
            UaePensionScheme: "SSSF");

        var resultSssf = _payrollEngine.CalculatePayroll(contextSssf);
        Assert.Equal(2000.00m, resultSssf.EmployeePension); // 5% of 40,000
        Assert.Equal(6000.00m, resultSssf.PayLines.First(l => l.Category == "uae_national_pension_er").Amount); // 15% of 40,000
    }

    [Fact]
    public void CalculatePayroll_UnitedArabEmiratesExpatriate_AccruesEndOfServiceGratuity()
    {
        var context = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "AE",
            WorkJurisdiction: "DUBAI",
            SkillCategory: "skilled",
            BaseSalary: 12000.00m,
            Allowances: 3000.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 1, 1),
            CurrencyCode: "AED",
            IsNationalEmployee: false);

        var result = _payrollEngine.CalculatePayroll(context);

        Assert.Equal(15000.00m, result.GrossPay);
        Assert.Equal(0.00m, result.EmployeePension);
        Assert.Equal(0.00m, result.IncomeTaxWithholding);
        // Expatriate EOSG: (12,000 / 30) * 1.75 days = AED 700.00
        var eosgLine = result.PayLines.First(l => l.Category == "eosg_accrual_er");
        Assert.Equal(700.00m, eosgLine.Amount);
        Assert.Equal(15000.00m, result.NetPay);
    }

    // ── 8B. Global Payroll Engine: Australia (12% Super & PAYG) ───────────────

    [Fact]
    public void CalculatePayroll_Australia_Computes12PercentSuperAndPaygWithholding()
    {
        var context = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "AU",
            WorkJurisdiction: "NSW",
            SkillCategory: "skilled",
            BaseSalary: 8000.00m, // AUD 8,000 / month = AUD 96,000 / year
            Allowances: 500.00m,
            OvertimeHours: 10.00m,
            OvertimeHourlyRate: 50.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 7, 1), // Post-1 July 2026 12.0% Super mandate
            CurrencyCode: "AUD",
            OrdinaryTimeEarnings: 8500.00m,
            AustraliaPaydaySuper: true);

        var result = _payrollEngine.CalculatePayroll(context);

        Assert.Equal("AU", result.CountryCode);
        Assert.Equal("AUD", result.CurrencyCode);
        // Gross pay = 8000 + 500 + (10 * 50 * 1.5 = 750) = AUD 9,250.00
        Assert.Equal(9250.00m, result.GrossPay);

        // Superannuation Guarantee: 12.0% on OTE ($8,500.00) = AUD 1,020.00
        var superLine = result.PayLines.First(l => l.Category == "superannuation_guarantee");
        Assert.Equal(1020.00m, superLine.Amount);
        Assert.Equal(1020.00m, result.EmployerPension);

        // PAYG Withholding > 0 on annual gross $111,000
        Assert.True(result.IncomeTaxWithholding > 0);
        Assert.Contains("Payday Super", result.StatutoryCitation);
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
        // Provincial Social Security Employer Share: 5% of PKR 40,000 ceiling = PKR 2,000.00
        Assert.Equal(2000.00m, result.EmployerSocialSecurity);
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

    // ── 11. Phase 16: Saudi Arabia Localization (VAT, ZATCA, GOSI & EOSA) ───

    [Fact]
    public void DetermineTax_SaudiArabia_Applies15PercentVATAndZatcaPhase2Metadata()
    {
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "SA",
            SupplierTaxId: "300123456789013",
            CustomerCountryCode: "SA",
            CustomerProvinceState: "RIYADH",
            CustomerPostalCode: "12345",
            CustomerType: "b2b",
            ProductServiceClassification: "ISIC-6201",
            IsSaaSOrDigitalService: true));

        Assert.Equal("SA", result.CountryCode);
        Assert.Equal(15.0000m, result.RatePercentage);
        Assert.Equal("ZATCA", result.Authority);
        Assert.NotNull(result.EInvoicingMandatePlatform);
        Assert.Contains("ZATCA FATOORA Phase 2", result.EInvoicingMandatePlatform);
    }

    [Fact]
    public void CalculatePayroll_SaudiArabia_NationalWithinCapAndExpatEOSA()
    {
        // 1. National within cap (SAR 20,000 gross)
        var nationalContext = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "SA",
            WorkJurisdiction: "RIYADH",
            SkillCategory: "skilled",
            BaseSalary: 12000.00m,
            Allowances: 8000.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 1, 1),
            CurrencyCode: "SAR",
            IsNationalEmployee: true);

        var nationalRes = _payrollEngine.CalculatePayroll(nationalContext);

        Assert.Equal("SA", nationalRes.CountryCode);
        Assert.Equal(20000.00m, nationalRes.GrossPay);
        // EE: 9% annuities (1800) + 0.75% SANED (150) = 1950
        Assert.Equal(1950.00m, nationalRes.EmployeePension);
        // ER: 9% annuities (1800) + 0.75% SANED (150) = 1950
        Assert.Equal(1950.00m, nationalRes.EmployerPension);
        // ER hazards: 2% = 400
        Assert.Equal(400.00m, nationalRes.EmployerSocialSecurity);
        Assert.Equal(18050.00m, nationalRes.NetPay);

        // 2. Expatriate senior employee (SAR 15,000 gross, 7 years service)
        var expatContext = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "SA",
            WorkJurisdiction: "RIYADH",
            SkillCategory: "skilled",
            BaseSalary: 15000.00m,
            Allowances: 0.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 1, 1),
            CurrencyCode: "SAR",
            IsNationalEmployee: false,
            YearsOfService: 7);

        var expatRes = _payrollEngine.CalculatePayroll(expatContext);

        Assert.Equal(0.00m, expatRes.TotalEmployeeDeductions);
        Assert.Equal(15000.00m, expatRes.NetPay);
        // Hazards 2% = 300
        Assert.Equal(300.00m, expatRes.EmployerSocialSecurity);
        // EOSA senior: 15,000 * 1.0 / 12 = 1,250
        Assert.Equal(1250.00m, expatRes.EmployerPension);
    }

    // ── 12. Phase 16: Canada Localization (GST/HST/PST, CPP/CPP2, EI & TD1) ──

    [Theory]
    [InlineData("ON", 13.0000, "CRA")]
    [InlineData("QC", 14.9750, "CRA")]
    [InlineData("BC", 12.0000, "CRA")]
    [InlineData("SK", 11.0000, "CRA")]
    [InlineData("MB", 12.0000, "CRA")]
    [InlineData("AB", 5.0000, "CRA")]
    public void DetermineTax_Canada_ResolvesProvincialHarmonizedAndDualTaxes(
        string province, decimal expectedRate, string expectedAuthority)
    {
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "CA",
            SupplierTaxId: "123456789RT0001",
            CustomerCountryCode: "CA",
            CustomerProvinceState: province,
            CustomerPostalCode: "M5V 2T6",
            CustomerType: "b2b",
            ProductServiceClassification: "CPA-62.01",
            IsSaaSOrDigitalService: true));

        Assert.Equal("CA", result.CountryCode);
        Assert.Equal(expectedRate, result.RatePercentage);
        Assert.Equal(expectedAuthority, result.Authority);
    }

    [Fact]
    public void CalculatePayroll_Canada_ComputesCppBase_Cpp2_EiAndTd1Withholding()
    {
        var context = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "CA",
            WorkJurisdiction: "ON",
            SkillCategory: "skilled",
            BaseSalary: 6000.00m, // CAD 6,000 / month ($72,000 / year)
            Allowances: 0.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 1, 1),
            CurrencyCode: "CAD");

        var result = _payrollEngine.CalculatePayroll(context);

        Assert.Equal("CA", result.CountryCode);
        Assert.Equal(6000.00m, result.GrossPay);

        // CPP Base (336.18) + CPP2 (2.33) = 338.51
        Assert.Equal(338.51m, result.EmployeePension);
        Assert.Equal(338.51m, result.EmployerPension);

        // EI Employee (89.24 max capped) and Employer (124.94 max capped)
        Assert.Equal(89.24m, result.EmployeeSocialSecurity);
        Assert.Equal(124.94m, result.EmployerSocialSecurity);

        // Income tax withheld > 0
        Assert.True(result.IncomeTaxWithholding > 0);
        Assert.True(result.NetPay < result.GrossPay);
    }

    // ── 13. Phase 16: India Localization (Dual GST, EPF, ESI, PT & 115BAC) ──

    [Fact]
    public void DetermineTax_India_IntraStateSplitsCgstSgst_AndInterStateAppliesIgst()
    {
        // 1. Intra-State (MH to MH): 18% CGST + SGST split
        var intra = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "IN",
            SupplierTaxId: "27AAAAA0000A1Z5",
            CustomerCountryCode: "IN",
            CustomerProvinceState: "MH",
            CustomerPostalCode: "400001",
            CustomerType: "b2b",
            ProductServiceClassification: "SAC-998314",
            IsSaaSOrDigitalService: true));

        Assert.Equal("IN", intra.CountryCode);
        Assert.Equal(18.0000m, intra.RatePercentage);
        Assert.Contains("9% CGST and 9% SGST", intra.CalculationExplanation);

        // 2. Inter-State (MH to KA): 18% IGST
        var inter = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "IN",
            SupplierTaxId: "27AAAAA0000A1Z5",
            CustomerCountryCode: "IN",
            CustomerProvinceState: "KA",
            CustomerPostalCode: "560001",
            CustomerType: "b2b",
            ProductServiceClassification: "SAC-998314",
            IsSaaSOrDigitalService: true));

        Assert.Equal(18.0000m, inter.RatePercentage);
        Assert.Contains("18% IGST", inter.CalculationExplanation);

        // 3. Section 5(3) Reverse Charge
        var rcm = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: "US",
            SupplierTaxId: "US-EIN-123456789",
            CustomerCountryCode: "IN",
            CustomerProvinceState: "DL",
            CustomerPostalCode: "110001",
            CustomerType: "b2b",
            CustomerTaxId: "07AAAAA0000A1Z5",
            ProductServiceClassification: "SAC-998314",
            IsSaaSOrDigitalService: true));

        Assert.Equal(0.0000m, rcm.RatePercentage);
        Assert.True(rcm.IsReverseCharge);
        Assert.Contains("Section 5(3)", rcm.StatutoryReference);
    }

    [Fact]
    public void CalculatePayroll_India_ComputesEpfSplit_EsiWageCeiling_AndPt()
    {
        // 1. Gross ₹60,000, Basic ₹40,000 (exceeds ₹21k ESI ceiling, MH state)
        var highContext = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "IN",
            WorkJurisdiction: "MH",
            SkillCategory: "skilled",
            BaseSalary: 40000.00m,
            Allowances: 20000.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 4, 1),
            CurrencyCode: "INR");

        var highRes = _payrollEngine.CalculatePayroll(highContext);

        Assert.Equal("IN", highRes.CountryCode);
        Assert.Equal(60000.00m, highRes.GrossPay);
        // EPF EE: 12% of 40,000 = 4,800
        Assert.Equal(4800.00m, highRes.EmployeePension);
        // EPF ER: 12% = 4,800
        Assert.Equal(4800.00m, highRes.EmployerPension);
        // EDLI 0.5% = 75
        Assert.Equal(75.00m, highRes.EmployerSocialSecurity);
        // Professional tax = 200 (in employee social security)
        Assert.Equal(200.00m, highRes.EmployeeSocialSecurity);
        Assert.True(highRes.NetPay > 0);
    }

    // ── 14. Phase 16: European Expansion Payroll Adapters ────────────────────

    [Fact]
    public void CalculatePayroll_EuropeanExpansion_ComputesNlEsItPlDeductions()
    {
        // 1. Netherlands (Loonheffing & Werkgeverspremies)
        var nlContext = new PayrollCalculationContext(
            EmployeeId: Guid.NewGuid(),
            CountryCode: "NL",
            WorkJurisdiction: "NL_ALL",
            SkillCategory: "skilled",
            BaseSalary: 4000.00m,
            Allowances: 0.00m,
            OvertimeHours: 0.00m,
            OvertimeHourlyRate: 0.00m,
            Bonus: 0.00m,
            EffectiveDate: new DateOnly(2026, 1, 1),
            CurrencyCode: "EUR");

        var nlRes = _payrollEngine.CalculatePayroll(nlContext);
        Assert.Equal("NL", nlRes.CountryCode);
        Assert.True(nlRes.IncomeTaxWithholding > 0);
        Assert.Equal(740.00m, nlRes.EmployerSocialSecurity); // 18.5%

        // 2. Spain (Seguridad Social 6.35% EE / 29.9% ER)
        var esContext = nlContext with { CountryCode = "ES", WorkJurisdiction = "ES_ALL", BaseSalary = 3000.00m };
        var esRes = _payrollEngine.CalculatePayroll(esContext);
        Assert.Equal("ES", esRes.CountryCode);
        Assert.Equal(190.50m, esRes.EmployeeSocialSecurity); // 6.35% of 3000
        Assert.Equal(897.00m, esRes.EmployerSocialSecurity); // 29.9% of 3000

        // 3. Italy (INPS 9.19% EE / 30% ER + TFR 6.91%)
        var itContext = nlContext with { CountryCode = "IT", WorkJurisdiction = "IT_ALL", BaseSalary = 3000.00m };
        var itRes = _payrollEngine.CalculatePayroll(itContext);
        Assert.Equal("IT", itRes.CountryCode);
        Assert.Equal(275.70m, itRes.EmployeeSocialSecurity); // 9.19% of 3000
        Assert.Equal(1122.22m, itRes.EmployerSocialSecurity); // 30.0% + TFR 3000 / 13.5
        Assert.Equal(222.22m, itRes.EmployerPension); // TFR 3000 / 13.5

        // 4. Poland (ZUS 13.71% EE / 20.38% ER)
        var plContext = nlContext with { CountryCode = "PL", WorkJurisdiction = "PL_ALL", BaseSalary = 8000.00m, CurrencyCode = "PLN" };
        var plRes = _payrollEngine.CalculatePayroll(plContext);
        Assert.Equal("PL", plRes.CountryCode);
        Assert.Equal(1630.40m, plRes.EmployerSocialSecurity); // 20.38% of 8000
        Assert.True(plRes.EmployeeSocialSecurity > 1000m);
    }

    // ── 15. Phase 16: Additional Extensible Countries ────────────────────────

    [Theory]
    [InlineData("SG", 9.0000, "IRAS", "InvoiceNow (Peppol)")]
    [InlineData("CH", 8.1000, "ESTV", "Swiss QR-Bill (ISO 20022)")]
    [InlineData("JP", 10.0000, "NTA", "Qualified Invoice System (QIS)")]
    [InlineData("BR", 26.5000, "RFB_COMITETRIBUTARIO", "Nota Fiscal de Serviço Eletrônica (NFS-e)")]
    [InlineData("ZA", 15.0000, "SARS", null)]
    public void DetermineTax_ExtensibleCountries_ResolvesStatutoryRatesAndEInvoicing(
        string country, decimal expectedRate, string expectedAuthority, string? expectedPlatform)
    {
        var result = _taxEngine.DetermineTax(new TaxDeterminationRequest(
            SupplierCountryCode: country,
            SupplierTaxId: "SUPPLIER-TAX-001",
            CustomerCountryCode: country,
            CustomerProvinceState: null,
            CustomerPostalCode: "00000",
            CustomerType: "b2b",
            ProductServiceClassification: "TECH-SVC",
            IsSaaSOrDigitalService: true));

        Assert.Equal(country, result.CountryCode);
        Assert.Equal(expectedRate, result.RatePercentage);
        Assert.Equal(expectedAuthority, result.Authority);
        Assert.Equal(expectedPlatform, result.EInvoicingMandatePlatform);
    }
}
