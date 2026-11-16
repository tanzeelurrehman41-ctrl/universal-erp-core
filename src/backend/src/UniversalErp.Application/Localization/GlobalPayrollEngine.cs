using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalErp.Application.Localization;

public sealed record PayrollCalculationContext(
    Guid EmployeeId,
    string CountryCode,
    string? WorkJurisdiction,
    string SkillCategory,
    decimal BaseSalary,
    decimal Allowances,
    decimal OvertimeHours,
    decimal OvertimeHourlyRate,
    decimal Bonus,
    DateOnly EffectiveDate,
    string CurrencyCode = "USD",
    bool IsNationalEmployee = true,
    IReadOnlyList<ConfigurableTaxSlab>? CustomSlabs = null,
    string FilingStatus = "single", // "single", "married_joint", "head_of_household"
    string UkCategoryLetter = "A", // "A", "B", "C", "H", "M", "J"
    string? StudentLoanPlan = null, // null, "Plan1", "Plan2", "Plan4", "Plan5", "Postgraduate"
    bool PensionAutoEnrolment = false,
    decimal PensionEmployeeRate = 0.05m,
    decimal PensionEmployerRate = 0.03m,
    decimal SutaRate = 0.027m,
    // US Form W-4 Settings (IRS Pub 15-T)
    bool W4Step2cMultipleJobs = false,
    decimal W4Step3DependentCredits = 0.00m,
    decimal W4Step4aOtherIncome = 0.00m,
    decimal W4Step4bOtherDeductions = 0.00m,
    decimal W4Step4cExtraWithholding = 0.00m,
    // UAE Pension Cohort & Jurisdiction Settings
    string? UaePensionScheme = null, // "Law7_1999", "DecreeLaw57_2023", "ADPF", "SSSF"
    string UaeSector = "private", // "private", "government"
    // Australia Superannuation Settings
    bool AustraliaPaydaySuper = false,
    decimal OrdinaryTimeEarnings = 0.00m,
    // Saudi Arabia EOSA & Canada TD1 Settings
    int YearsOfService = 1,
    decimal ProvincialClaimAmount = 0.00m);

public sealed record ConfigurableTaxSlab(
    int Order,
    decimal MinAnnualIncome,
    decimal? MaxAnnualIncome,
    decimal FixedTaxAmount,
    decimal MarginalRatePercentage,
    string? Description = null);

public sealed record PayrollPayLine(
    string LineType, // "earning", "deduction", "employer_contribution"
    string Category,
    string Description,
    decimal Amount,
    string CurrencyCode);

public sealed record PayrollCalculationResult(
    Guid EmployeeId,
    string CountryCode,
    string? WorkJurisdiction,
    decimal GrossPay,
    decimal IncomeTaxWithholding,
    decimal EmployeeSocialSecurity,
    decimal EmployerSocialSecurity,
    decimal EmployeePension,
    decimal EmployerPension,
    decimal OtherDeductions,
    decimal TotalEmployeeDeductions,
    decimal NetPay,
    string CurrencyCode,
    string StatutoryAuthority,
    string StatutoryCitation,
    bool MinimumWageCompliant,
    decimal ApplicableMinimumWage,
    IReadOnlyList<PayrollPayLine> PayLines);

public interface IPayrollCountryAdapter
{
    string CountryCode { get; }
    string CountryName { get; }
    string DefaultCurrencyCode { get; }
    PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context);
    decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate);
}

// ─────────────────────────────────────────────────────────────────────────────
// 1. United States Payroll Adapter (IRS Pub 15-T / SSA / DOL)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class UnitedStatesPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "US";
    public string CountryName => "United States";
    public string DefaultCurrencyCode => "USD";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        var state = (jurisdiction ?? "").Trim().ToUpperInvariant();
        var hourly = state switch
        {
            "CA" => 16.50m, // California 2026 minimum wage
            "NY" => 16.50m, // New York 2026 minimum wage
            "WA" => 16.66m, // Washington State 2026 minimum wage
            _ => 7.25m      // US Federal Fair Labor Standards Act (FLSA)
        };
        return Math.Round(hourly * 173.33m, 2);
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 1.5m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        // 1. FICA Social Security (OASDI): 6.2% EE, 6.2% ER
        // 2026 Wage Base = $184,500 ($15,375.00/mo). 2025 = $176,100 ($14,675.00/mo). Pre-2025 = $168,600 ($14,050.00/mo).
        decimal monthlyFicaCap;
        if (context.EffectiveDate >= new DateOnly(2026, 1, 1))
            monthlyFicaCap = 15375.00m; // $184,500 / 12
        else if (context.EffectiveDate >= new DateOnly(2025, 1, 1))
            monthlyFicaCap = 14675.00m; // $176,100 / 12
        else
            monthlyFicaCap = 14050.00m; // $168,600 / 12

        var ficaSubjectWage = Math.Min(grossPay, monthlyFicaCap);
        var ficaEe = Math.Round(ficaSubjectWage * 0.062m, 2);
        var ficaEr = Math.Round(ficaSubjectWage * 0.062m, 2);

        // 2. Medicare (HI): 1.45% EE, 1.45% ER
        // Additional Medicare Tax: 0.9% EE only on earnings over $200,000 (Single/HOH = $16,666.67/mo) or $250,000 (MFJ = $20,833.33/mo)
        var isMfj = string.Equals(context.FilingStatus, "married_joint", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(context.FilingStatus, "MarriedFilingJointly", StringComparison.OrdinalIgnoreCase);
        var isHoh = string.Equals(context.FilingStatus, "head_of_household", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(context.FilingStatus, "HeadOfHousehold", StringComparison.OrdinalIgnoreCase);

        var monthlyAdditionalMedicareThreshold = isMfj ? 20833.33m : 16666.67m;
        var baseMedicareEe = Math.Round(grossPay * 0.0145m, 2);
        var additionalMedicareEe = 0.00m;
        if (grossPay > monthlyAdditionalMedicareThreshold)
        {
            additionalMedicareEe = Math.Round((grossPay - monthlyAdditionalMedicareThreshold) * 0.009m, 2);
        }
        var totalMedicareEe = baseMedicareEe + additionalMedicareEe;
        var medicareEr = Math.Round(grossPay * 0.0145m, 2); // Employer does not match 0.9% additional tax

        // 3. Federal Income Tax (FIT) Withholding (IRS Publication 15-T 2026 Percentage Method)
        // Adjusted Wage = Gross + Step4a/12 - Step4b/12
        var monthlyAdjustedWage = Math.Max(0.00m, grossPay + (context.W4Step4aOtherIncome / 12.0m) - (context.W4Step4bOtherDeductions / 12.0m));
        var annualAdjustedWage = monthlyAdjustedWage * 12.0m;

        // 2026 Official Standard Deductions
        decimal standardDeduction;
        if (context.EffectiveDate >= new DateOnly(2026, 1, 1))
        {
            standardDeduction = isMfj ? 32200.00m :
                isHoh ? 24150.00m : 16100.00m;
        }
        else
        {
            standardDeduction = isMfj ? 30000.00m : 15000.00m;
        }

        var annualTaxable = Math.Max(0.00m, annualAdjustedWage - standardDeduction);
        decimal annualFit;

        if (isMfj)
        {
            // 2026 Married Filing Jointly Brackets (IRS Pub 15-T)
            if (annualTaxable <= 24800m)
                annualFit = annualTaxable * 0.10m;
            else if (annualTaxable <= 100800m)
                annualFit = 2480m + ((annualTaxable - 24800m) * 0.12m);
            else if (annualTaxable <= 211400m)
                annualFit = 11600m + ((annualTaxable - 100800m) * 0.22m);
            else if (annualTaxable <= 403500m)
                annualFit = 35932m + ((annualTaxable - 211400m) * 0.24m);
            else if (annualTaxable <= 512400m)
                annualFit = 82036m + ((annualTaxable - 403500m) * 0.32m);
            else if (annualTaxable <= 768700m)
                annualFit = 116884m + ((annualTaxable - 512400m) * 0.35m);
            else
                annualFit = 206589m + ((annualTaxable - 768700m) * 0.37m);
        }
        else if (isHoh)
        {
            // 2026 Head of Household Brackets (IRS Pub 15-T)
            if (annualTaxable <= 17700m)
                annualFit = annualTaxable * 0.10m;
            else if (annualTaxable <= 67500m)
                annualFit = 1770m + ((annualTaxable - 17700m) * 0.12m);
            else if (annualTaxable <= 107700m)
                annualFit = 7746m + ((annualTaxable - 67500m) * 0.22m);
            else if (annualTaxable <= 205500m)
                annualFit = 16590m + ((annualTaxable - 107700m) * 0.24m);
            else if (annualTaxable <= 260950m)
                annualFit = 40062m + ((annualTaxable - 205500m) * 0.32m);
            else if (annualTaxable <= 652050m)
                annualFit = 57806m + ((annualTaxable - 260950m) * 0.35m);
            else
                annualFit = 194691m + ((annualTaxable - 652050m) * 0.37m);
        }
        else
        {
            // 2026 Single / Married Filing Separately Brackets (IRS Pub 15-T)
            if (annualTaxable <= 12400m)
                annualFit = annualTaxable * 0.10m;
            else if (annualTaxable <= 50400m)
                annualFit = 1240m + ((annualTaxable - 12400m) * 0.12m);
            else if (annualTaxable <= 105700m)
                annualFit = 5800m + ((annualTaxable - 50400m) * 0.22m);
            else if (annualTaxable <= 201750m)
                annualFit = 17966m + ((annualTaxable - 105700m) * 0.24m);
            else if (annualTaxable <= 256200m)
                annualFit = 41018m + ((annualTaxable - 201750m) * 0.32m);
            else if (annualTaxable <= 640600m)
                annualFit = 58442m + ((annualTaxable - 256200m) * 0.35m);
            else
                annualFit = 192982m + ((annualTaxable - 640600m) * 0.37m);
        }

        // Apply Form W-4 Step 3 Dependent Credits and Step 4c Extra Withholding
        var tentativeMonthlyFit = Math.Round(annualFit / 12.0m, 2);
        var monthlyCredits = Math.Round(context.W4Step3DependentCredits / 12.0m, 2);
        var monthlyFit = Math.Max(0.00m, tentativeMonthlyFit - monthlyCredits) + context.W4Step4cExtraWithholding;

        // 4. Unemployment Obligations (FUTA & SUTA - Employer Only)
        // FUTA: 0.6% on first $7,000/yr ($583.33/mo)
        var monthlyFutaWage = Math.Min(grossPay, 583.33m);
        var futaEr = Math.Round(monthlyFutaWage * 0.006m, 2);

        // SUTA: Employer state obligation on state taxable base (default $833.33/mo base e.g. $10,000)
        var sutaRate = context.SutaRate > 0 ? context.SutaRate : 0.027m;
        var monthlySutaWage = Math.Min(grossPay, 833.33m);
        var sutaEr = Math.Round(monthlySutaWage * sutaRate, 2);

        var totalEeDeductions = ficaEe + totalMedicareEe + monthlyFit;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);

        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);
        var isCompliant = grossPay >= minWage;

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Base Salary", context.BaseSalary, "USD"),
            new("earning", "allowances", "Allowances", context.Allowances, "USD"),
            new("deduction", "fica_social_security", $"FICA Social Security EE (6.2% up to ${monthlyFicaCap * 12:N0}/yr)", ficaEe, "USD"),
            new("employer_contribution", "fica_social_security_er", "FICA Social Security ER (6.2%)", ficaEr, "USD"),
            new("deduction", "fica_medicare", "FICA Medicare EE (1.45%)", baseMedicareEe, "USD"),
            new("employer_contribution", "fica_medicare_er", "FICA Medicare ER (1.45%)", medicareEr, "USD")
        };

        if (additionalMedicareEe > 0)
        {
            payLines.Add(new("deduction", "additional_medicare", $"Additional Medicare Tax (0.9% over ${monthlyAdditionalMedicareThreshold * 12:N0}/yr)", additionalMedicareEe, "USD"));
        }

        payLines.Add(new("deduction", "federal_income_tax", $"Federal Income Tax (IRS Pub 15-T {(isMfj ? "MFJ" : isHoh ? "HOH" : "Single")} StdDed ${standardDeduction:N0})", monthlyFit, "USD"));
        payLines.Add(new("employer_contribution", "futa_tax_er", "FUTA Federal Unemployment (0.6% on first $7,000/yr)", futaEr, "USD"));
        payLines.Add(new("employer_contribution", "suta_tax_er", $"SUTA State Unemployment ({(sutaRate * 100):F2}%)", sutaEr, "USD"));

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "US",
            WorkJurisdiction: context.WorkJurisdiction ?? "FEDERAL",
            GrossPay: grossPay,
            IncomeTaxWithholding: monthlyFit,
            EmployeeSocialSecurity: ficaEe + totalMedicareEe,
            EmployerSocialSecurity: ficaEr + medicareEr + futaEr + sutaEr,
            EmployeePension: 0.00m,
            EmployerPension: 0.00m,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "USD",
            StatutoryAuthority: "Internal Revenue Service (IRS) & Social Security Administration (SSA)",
            StatutoryCitation: "IRC § 3402, IRS Pub 15-T (2026), FICA § 3101/3111 (2026 OASDI Base $184,500), FUTA § 3301",
            MinimumWageCompliant: isCompliant,
            ApplicableMinimumWage: minWage,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. United Kingdom Payroll Adapter (HMRC PAYE & NIC 2026–27)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class UnitedKingdomPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "GB";
    public string CountryName => "United Kingdom";
    public string DefaultCurrencyCode => "GBP";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        // UK National Living Wage 2026: £12.21/hr * 162.5 monthly hours = £1,984.13
        return 1984.13m;
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 1.5m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        // 1. HMRC PAYE Income Tax (Emergency Code 1257L: Personal allowance £12,570/yr = £1,047.50/mo)
        // Subject to £1 tapering for every £2 over £100,000/yr (£8,333.33/mo)
        var annualGross = grossPay * 12.0m;
        var personalAllowanceAnnual = annualGross > 100000m
            ? Math.Max(0.00m, 12570m - ((annualGross - 100000m) * 0.5m))
            : 12570m;
        var monthlyPersonalAllowance = Math.Round(personalAllowanceAnnual / 12.0m, 2);
        var taxableIncome = Math.Max(0.00m, grossPay - monthlyPersonalAllowance);
        decimal monthlyTax = 0.00m;

        var isScotland = string.Equals(context.WorkJurisdiction, "SCOTLAND", StringComparison.OrdinalIgnoreCase);

        if (isScotland)
        {
            // Scottish Income Tax 2026–27 Published Schedule (Monthly Taxable thresholds):
            // Starter: 19% on £0 to £192.17 (band £192.17, annual £2,306)
            // Basic: 20% on £192.17 to £1,165.92 (band £973.75, annual £13,991)
            // Intermediate: 21% on £1,165.92 to £2,591.00 (band £1,425.08, annual £31,092)
            // Higher: 42% on £2,591.00 to £5,202.50 (band £2,611.50, annual £62,430)
            // Advanced: 45% on £5,202.50 to £10,428.33 (band £5,225.83, annual £125,140)
            // Top: 48% on over £10,428.33 (over £125,140 annual)
            if (taxableIncome > 0)
            {
                var rem = taxableIncome;
                var band1 = Math.Min(rem, 192.17m);
                monthlyTax += band1 * 0.19m;
                rem -= band1;

                if (rem > 0)
                {
                    var band2 = Math.Min(rem, 973.75m);
                    monthlyTax += band2 * 0.20m;
                    rem -= band2;
                }
                if (rem > 0)
                {
                    var band3 = Math.Min(rem, 1425.08m);
                    monthlyTax += band3 * 0.21m;
                    rem -= band3;
                }
                if (rem > 0)
                {
                    var band4 = Math.Min(rem, 2611.50m);
                    monthlyTax += band4 * 0.42m;
                    rem -= band4;
                }
                if (rem > 0)
                {
                    var band5 = Math.Min(rem, 5225.83m);
                    monthlyTax += band5 * 0.45m;
                    rem -= band5;
                }
                if (rem > 0)
                {
                    monthlyTax += rem * 0.48m;
                }
            }
        }
        else
        {
            // Rest of UK (rUK) 3-Band Tax Schedule 2026–27:
            // Basic Rate: 20% on £0 to £3,141.67/mo (annual £37,700)
            // Higher Rate: 40% on £3,141.67 to £10,428.33/mo (annual £37,700 to £125,140)
            // Additional Rate: 45% on over £10,428.33/mo (over £125,140)
            if (taxableIncome > 0)
            {
                var rem = taxableIncome;
                var basicBand = Math.Min(rem, 3141.67m);
                monthlyTax += basicBand * 0.20m;
                rem -= basicBand;

                if (rem > 0)
                {
                    var higherBand = Math.Min(rem, 7286.66m);
                    monthlyTax += higherBand * 0.40m;
                    rem -= higherBand;
                }
                if (rem > 0)
                {
                    monthlyTax += rem * 0.45m;
                }
            }
        }
        monthlyTax = Math.Round(monthlyTax, 2);

        // 2. Class 1 National Insurance Contributions (NIC)
        var pt = 1048.00m;  // Primary Threshold (£12,570/yr / 12)
        var uel = 4189.00m; // Upper Earnings Limit (£50,270/yr / 12)
        var cat = (context.UkCategoryLetter ?? "A").Trim().ToUpperInvariant();

        decimal nicEe = 0.00m;
        if (cat == "C")
        {
            // Category C: State Pension Age -> 0% EE NIC
            nicEe = 0.00m;
        }
        else if (cat == "B")
        {
            // Category B: Married Women / Widows reduced rate -> 5.85% between PT and UEL, 2% above UEL
            if (grossPay > pt)
            {
                var mainBand = Math.Min(grossPay, uel) - pt;
                nicEe += Math.Round(mainBand * 0.0585m, 2);
                if (grossPay > uel)
                {
                    nicEe += Math.Round((grossPay - uel) * 0.02m, 2);
                }
            }
        }
        else if (cat == "J")
        {
            // Category J: Deferment -> 2% above PT
            if (grossPay > pt)
            {
                nicEe = Math.Round((grossPay - pt) * 0.02m, 2);
            }
        }
        else
        {
            // Category A (Standard), H (Apprentice < 25), M (Under 21): 8% between PT and UEL, 2% above UEL
            if (grossPay > pt)
            {
                var mainBand = Math.Min(grossPay, uel) - pt;
                nicEe += Math.Round(mainBand * 0.08m, 2);
                if (grossPay > uel)
                {
                    nicEe += Math.Round((grossPay - uel) * 0.02m, 2);
                }
            }
        }

        // Employer Class 1 NIC:
        // 2026–27 Statutory Reform: Rate = 15.0%, Secondary Threshold = £417.00/mo (£5,000/yr / 12)
        // Historical (pre-2025-04-06): Rate = 13.8%, Secondary Threshold = £758.00/mo
        decimal employerNicRate;
        decimal secondaryThreshold;

        if (context.EffectiveDate >= new DateOnly(2025, 4, 6))
        {
            employerNicRate = 0.15m;
            secondaryThreshold = 417.00m;
        }
        else
        {
            employerNicRate = 0.138m;
            secondaryThreshold = 758.00m;
        }

        decimal nicEr = 0.00m;
        if (cat == "H" || cat == "M")
        {
            // Apprentice under 25 / Employee under 21: 0% ER NIC up to Upper Secondary Threshold (£4,189/mo)
            if (grossPay > uel)
            {
                nicEr = Math.Round((grossPay - uel) * employerNicRate, 2);
            }
        }
        else
        {
            if (grossPay > secondaryThreshold)
            {
                nicEr = Math.Round((grossPay - secondaryThreshold) * employerNicRate, 2);
            }
        }

        // 3. Student Loan Deductions (HMRC / DfE 2026-27 Rates)
        decimal studentLoanDeduction = 0.00m;
        if (!string.IsNullOrWhiteSpace(context.StudentLoanPlan))
        {
            var plan = context.StudentLoanPlan.Trim().ToUpperInvariant();
            var (thresh, rate) = plan switch
            {
                "PLAN1" => (2241.66m, 0.09m),
                "PLAN2" => (2448.75m, 0.09m),
                "PLAN4" => (2816.25m, 0.09m),
                "PLAN5" => (2083.33m, 0.09m),
                "POSTGRADUATE" or "PLAN_PG" => (1750.00m, 0.06m),
                _ => (0.00m, 0.00m)
            };

            if (rate > 0 && grossPay > thresh)
            {
                studentLoanDeduction = Math.Round((grossPay - thresh) * rate, 2);
            }
        }

        // 4. Workplace Pension Auto-Enrolment (Qualifying Earnings £520 to £4,189/mo)
        decimal pensionEe = 0.00m;
        decimal pensionEr = 0.00m;
        if (context.PensionAutoEnrolment && grossPay > 520.00m)
        {
            var qualifyingWage = Math.Min(grossPay, 4189.00m) - 520.00m;
            pensionEe = Math.Round(qualifyingWage * context.PensionEmployeeRate, 2);
            pensionEr = Math.Round(qualifyingWage * context.PensionEmployerRate, 2);
        }

        var totalEeDeductions = monthlyTax + nicEe + studentLoanDeduction + pensionEe;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);

        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);
        var isCompliant = grossPay >= minWage;

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Basic Salary", context.BaseSalary, "GBP"),
            new("earning", "allowances", "Allowances", context.Allowances, "GBP"),
            new("deduction", "paye_income_tax", $"HMRC PAYE Tax ({(isScotland ? "Scottish 6-Band" : "UK Standard")})", monthlyTax, "GBP"),
            new("deduction", "nic_class1_ee", $"Class 1 NIC EE (Category {cat})", nicEe, "GBP"),
            new("employer_contribution", "nic_class1_er", $"Class 1 NIC ER ({(employerNicRate * 100):F1}% above £{secondaryThreshold:N0}/mo)", nicEr, "GBP")
        };

        if (studentLoanDeduction > 0)
        {
            payLines.Add(new("deduction", "student_loan", $"Student Loan Repayment ({context.StudentLoanPlan})", studentLoanDeduction, "GBP"));
        }
        if (pensionEe > 0)
        {
            payLines.Add(new("deduction", "workplace_pension_ee", "Employee Workplace Pension (Auto-Enrolment)", pensionEe, "GBP"));
            payLines.Add(new("employer_contribution", "workplace_pension_er", "Employer Workplace Pension", pensionEr, "GBP"));
        }

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "GB",
            WorkJurisdiction: isScotland ? "SCOTLAND" : (context.WorkJurisdiction ?? "UK"),
            GrossPay: grossPay,
            IncomeTaxWithholding: monthlyTax,
            EmployeeSocialSecurity: nicEe,
            EmployerSocialSecurity: nicEr,
            EmployeePension: pensionEe,
            EmployerPension: pensionEr,
            OtherDeductions: studentLoanDeduction,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "GBP",
            StatutoryAuthority: "HM Revenue and Customs (HMRC)",
            StatutoryCitation: "Income Tax (Earnings and Pensions) Act 2003 & NIC 2026–27 15% Employer Rate (Secondary Threshold £417/mo)",
            MinimumWageCompliant: isCompliant,
            ApplicableMinimumWage: minWage,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. United Arab Emirates Payroll Adapter (GPSSA Cohorts, ADPF, SSSF & EOSG)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class UnitedArabEmiratesPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "AE";
    public string CountryName => "United Arab Emirates";
    public string DefaultCurrencyCode => "AED";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        return 5000.00m;
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 1.25m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        // 1. Personal Income Tax in UAE is strictly 0.00%
        decimal incomeTax = 0.00m;

        // 2. Statutory Pension & EOSG Evaluation
        decimal pensionEe = 0.00m;
        decimal pensionEr = 0.00m;
        decimal govtSubsidy = 0.00m;
        decimal eosgMonthlyAccrual = 0.00m;

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Basic Salary", context.BaseSalary, "AED"),
            new("earning", "allowances", "Housing and Transport Allowances", context.Allowances, "AED")
        };

        if (context.IsNationalEmployee)
        {
            var isGov = string.Equals(context.UaeSector, "government", StringComparison.OrdinalIgnoreCase);
            var scheme = context.UaePensionScheme?.Trim().ToUpperInvariant() ??
                (context.EffectiveDate >= new DateOnly(2023, 10, 14) ? "DECREELAW57_2023" : "LAW7_1999");

            var pensionableSalary = context.BaseSalary + context.Allowances;
            decimal capAmount;
            decimal eeRate;
            decimal erRate;

            switch (scheme)
            {
                case "LAW7_1999":
                    // Federal Law No. 7 of 1999 (Legacy GPSSA)
                    // Cap: AED 50,000 / month
                    capAmount = 50000.00m;
                    eeRate = 0.05m;
                    if (isGov)
                    {
                        erRate = 0.15m;
                        govtSubsidy = 0.00m;
                    }
                    else
                    {
                        erRate = 0.125m;
                        govtSubsidy = Math.Round(Math.Min(pensionableSalary, capAmount) * 0.025m, 2);
                    }
                    break;

                case "ADPF":
                    // Abu Dhabi Pension Fund (Law No. 2 of 2000 as amended)
                    capAmount = 100000.00m;
                    eeRate = 0.11m;
                    erRate = 0.15m;
                    govtSubsidy = 0.00m;
                    break;

                case "SSSF":
                    // Sharjah Social Security Fund (Law No. 5 of 2018)
                    capAmount = 50000.00m;
                    eeRate = 0.05m;
                    erRate = 0.15m;
                    govtSubsidy = 0.00m;
                    break;

                case "DECREELAW57_2023":
                default:
                    // Federal Decree-Law No. 57 of 2023 (New GPSSA)
                    capAmount = isGov ? 100000.00m : 70000.00m;
                    eeRate = 0.11m;
                    if (isGov)
                    {
                        erRate = 0.15m;
                        govtSubsidy = 0.00m;
                    }
                    else
                    {
                        // In private sector, government contributes 2.5% subsidy for UAE nationals earning < AED 20,000
                        if (pensionableSalary < 20000.00m)
                        {
                            erRate = 0.125m;
                            govtSubsidy = Math.Round(pensionableSalary * 0.025m, 2);
                        }
                        else
                        {
                            erRate = 0.15m;
                            govtSubsidy = 0.00m;
                        }
                    }
                    break;
            }

            var cappedSubjectSalary = Math.Min(pensionableSalary, capAmount);
            pensionEe = Math.Round(cappedSubjectSalary * eeRate, 2);
            pensionEr = Math.Round(cappedSubjectSalary * erRate, 2);

            payLines.Add(new("deduction", "uae_national_pension_ee", $"UAE Pension EE ({scheme} {(eeRate * 100):F1}% up to AED {capAmount:N0})", pensionEe, "AED"));
            payLines.Add(new("employer_contribution", "uae_national_pension_er", $"UAE Pension ER ({(erRate * 100):F1}%)", pensionEr, "AED"));

            if (govtSubsidy > 0)
            {
                payLines.Add(new("employer_contribution", "uae_govt_pension_subsidy", "UAE Government Pension Subsidy (2.5%)", govtSubsidy, "AED"));
            }
        }
        else
        {
            // 3. Expatriate End of Service Gratuity (EOSG) Accrual under Federal Decree-Law No. 33 of 2021
            // 21 days basic salary per year / 12 months = 1.75 days basic pay/month (~5.75%)
            eosgMonthlyAccrual = Math.Round((context.BaseSalary / 30.0m) * 1.75m, 2);
            payLines.Add(new("employer_contribution", "eosg_accrual_er", "MOHRE End of Service Gratuity Accrual (1.75 days basic pay)", eosgMonthlyAccrual, "AED"));
        }

        var totalEeDeductions = pensionEe;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);

        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);
        var isCompliant = grossPay >= minWage;

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "AE",
            WorkJurisdiction: context.WorkJurisdiction ?? "FEDERAL",
            GrossPay: grossPay,
            IncomeTaxWithholding: incomeTax,
            EmployeeSocialSecurity: 0.00m,
            EmployerSocialSecurity: 0.00m,
            EmployeePension: pensionEe,
            EmployerPension: pensionEr + govtSubsidy + eosgMonthlyAccrual,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "AED",
            StatutoryAuthority: "General Pension and Social Security Authority (GPSSA) & MOHRE",
            StatutoryCitation: context.IsNationalEmployee
                ? "Federal Law No. 7 of 1999 & Federal Decree-Law No. 57 of 2023 on Pension and Social Security"
                : "Federal Decree-Law No. 33 of 2021 on the Regulation of Labour Relations (Article 51 EOSG)",
            MinimumWageCompliant: isCompliant,
            ApplicableMinimumWage: minWage,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. Australia Payroll Adapter (ATO Superannuation & PAYG)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class AustraliaPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "AU";
    public string CountryName => "Australia";
    public string DefaultCurrencyCode => "AUD";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        // Australian National Minimum Wage 2026: $24.10/hr * 164.67 monthly hours = $3,968.55
        return 3968.55m;
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 1.5m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        // 1. Superannuation Guarantee (12.0% effective 1 July 2026, 11.5% prior)
        var superRate = context.EffectiveDate >= new DateOnly(2026, 7, 1) ? 0.12m : 0.115m;
        // Ordinary Time Earnings (OTE) excludes overtime
        var ote = context.OrdinaryTimeEarnings > 0 ? context.OrdinaryTimeEarnings : (context.BaseSalary + context.Allowances);
        // Maximum Super Contribution Base (MSCB): quarterly $65,070 ($21,690/mo)
        var cappedOte = Math.Min(ote, 21690.00m);
        var superEr = Math.Round(cappedOte * superRate, 2);

        // 2. PAYG Withholding (ATO 2026 Schedule - Annual Tax-Free Threshold $18,200)
        var annualGross = grossPay * 12.0m;
        var annualPayg = 0.00m;
        if (annualGross > 18200m)
        {
            var rem = annualGross - 18200m;
            if (rem <= 26800m) // $18,201 to $45,000
                annualPayg = rem * 0.16m;
            else if (rem <= 116800m) // $45,001 to $135,000
                annualPayg = 4288m + ((rem - 26800m) * 0.30m);
            else if (rem <= 171800m) // $135,001 to $190,000
                annualPayg = 31288m + ((rem - 116800m) * 0.37m);
            else // Over $190,000
                annualPayg = 51638m + ((rem - 171800m) * 0.45m);
        }
        var monthlyPayg = Math.Round(annualPayg / 12.0m, 2);

        var totalEeDeductions = monthlyPayg;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);

        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);
        var isCompliant = grossPay >= minWage;

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Base Salary", context.BaseSalary, "AUD"),
            new("earning", "allowances", "Allowances", context.Allowances, "AUD"),
            new("deduction", "payg_withholding", "ATO PAYG Withholding", monthlyPayg, "AUD"),
            new("employer_contribution", "superannuation_guarantee", $"Superannuation Guarantee ({(superRate * 100):F1}% OTE)", superEr, "AUD")
        };

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "AU",
            WorkJurisdiction: context.WorkJurisdiction ?? "ATO",
            GrossPay: grossPay,
            IncomeTaxWithholding: monthlyPayg,
            EmployeeSocialSecurity: 0.00m,
            EmployerSocialSecurity: 0.00m,
            EmployeePension: 0.00m,
            EmployerPension: superEr,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "AUD",
            StatutoryAuthority: "Australian Taxation Office (ATO)",
            StatutoryCitation: "Superannuation Guarantee (Administration) Act 1992 & Payday Super Mandate 2026",
            MinimumWageCompliant: isCompliant,
            ApplicableMinimumWage: minWage,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 5. Pakistan Payroll Adapter (FBR Finance Act 2026, EOBI & Provincial WSS)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class PakistanPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "PK";
    public string CountryName => "Pakistan";
    public string DefaultCurrencyCode => "PKR";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        var prov = (jurisdiction ?? "").Trim().ToUpperInvariant();
        var skill = (skillCategory ?? "").Trim().ToLowerInvariant();

        if (prov == "SINDH")
        {
            return skill switch
            {
                "semi-skilled" or "semiskilled" => 44484.00m,
                "skilled" => 53350.00m,
                "highly-skilled" or "highlyskilled" => 55626.00m,
                _ => 43000.00m
            };
        }

        if (prov == "ICT" || prov == "FEDERAL")
        {
            return 40700.00m;
        }

        if (prov == "BALOCHISTAN")
        {
            return 37000.00m;
        }

        return 40000.00m; // Punjab, KP, general standard
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 2.0m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        // 1. FBR Tax Year 2027 Salaried Slabs (Finance Act 2026)
        var annualGross = grossPay * 12.0m;
        decimal annualTax;

        if (annualGross <= 600000m)
            annualTax = 0.00m;
        else if (annualGross <= 1200000m)
            annualTax = (annualGross - 600000m) * 0.01m;
        else if (annualGross <= 2200000m)
            annualTax = 6000m + ((annualGross - 1200000m) * 0.11m);
        else if (annualGross <= 3200000m)
            annualTax = 116000m + ((annualGross - 2200000m) * 0.20m);
        else if (annualGross <= 4100000m)
            annualTax = 316000m + ((annualGross - 3200000m) * 0.25m);
        else if (annualGross <= 5600000m)
            annualTax = 541000m + ((annualGross - 4100000m) * 0.29m);
        else if (annualGross <= 7000000m)
            annualTax = 976000m + ((annualGross - 5600000m) * 0.32m);
        else
            annualTax = 1424000m + ((annualGross - 7000000m) * 0.35m);

        var monthlyTax = Math.Round(annualTax / 12.0m, 2);

        // 2. EOBI (Statutory Base PKR 37,000: 1% EE = PKR 370, 5% ER = PKR 1,850)
        var eobiEe = 370.00m;
        var eobiEr = 1850.00m;

        // 3. Provincial Social Security (SESSI/PESSI: Employer 5% on wage ceiling PKR 40,000 = PKR 2,000 max)
        var sessiWage = Math.Min(grossPay, 40000.00m);
        var sessiEr = Math.Round(sessiWage * 0.05m, 2);

        var totalEeDeductions = monthlyTax + eobiEe;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);

        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);
        var isCompliant = grossPay >= minWage;

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Base Salary", context.BaseSalary, "PKR"),
            new("earning", "allowances", "Allowances", context.Allowances, "PKR"),
            new("deduction", "fbr_income_tax", "FBR Salaried Income Tax (Finance Act 2026)", monthlyTax, "PKR"),
            new("deduction", "eobi_ee", "EOBI Employee Contribution (1% of statutory PKR 37,000 base)", eobiEe, "PKR"),
            new("employer_contribution", "eobi_er", "EOBI Employer Contribution (5% of statutory PKR 37,000 base)", eobiEr, "PKR"),
            new("employer_contribution", "sessi_er", "Provincial Social Security (5% up to PKR 40k ceiling)", sessiEr, "PKR")
        };

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "PK",
            WorkJurisdiction: context.WorkJurisdiction ?? "FEDERAL",
            GrossPay: grossPay,
            IncomeTaxWithholding: monthlyTax,
            EmployeeSocialSecurity: 0.00m,
            EmployerSocialSecurity: sessiEr,
            EmployeePension: eobiEe,
            EmployerPension: eobiEr,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "PKR",
            StatutoryAuthority: "Federal Board of Revenue (FBR) & EOBI",
            StatutoryCitation: "Finance Act 2026 First Schedule & EOBI Act 1976",
            MinimumWageCompliant: isCompliant,
            ApplicableMinimumWage: minWage,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 6. Saudi Arabia Payroll Adapter (GOSI & Saudi Labor Law Art. 84 EOSA)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class SaudiArabiaPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "SA";
    public string CountryName => "Saudi Arabia";
    public string DefaultCurrencyCode => "SAR";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        // Saudi Nitaqat citizen benchmark SAR 4,000.00/mo; general floor SAR 1,500.00/mo
        return 4000.00m;
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 1.5m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        decimal employeePension = 0.00m;
        decimal employerPension = 0.00m;
        decimal employerSocialSecurity = 0.00m;
        decimal eosaAccrual = 0.00m;

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Base Salary", context.BaseSalary, "SAR"),
            new("earning", "allowances", "Housing and Transport Allowances", context.Allowances, "SAR")
        };

        if (overtimePay > 0)
        {
            payLines.Add(new("earning", "overtime", "Overtime (1.5x Hourly Rate)", overtimePay, "SAR"));
        }

        if (context.IsNationalEmployee)
        {
            // GOSI Social Insurance for Saudi Nationals
            // Annuities: 9.0% EE, 9.0% ER (cap SAR 45,000.00)
            // SANED Unemployment: 0.75% EE, 0.75% ER (cap SAR 45,000.00)
            // Occupational Hazards: 2.0% ER only
            var pensionableWage = Math.Min(context.BaseSalary + context.Allowances, 45000.00m);
            var gosiAnnuitiesEe = Math.Round(pensionableWage * 0.09m, 2);
            var gosiAnnuitiesEr = Math.Round(pensionableWage * 0.09m, 2);
            var sanedEe = Math.Round(pensionableWage * 0.0075m, 2);
            var sanedEr = Math.Round(pensionableWage * 0.0075m, 2);
            var occHazardsEr = Math.Round(pensionableWage * 0.02m, 2);

            employeePension = gosiAnnuitiesEe + sanedEe;
            employerPension = gosiAnnuitiesEr + sanedEr;
            employerSocialSecurity = occHazardsEr;

            payLines.Add(new("deduction", "gosi_annuities_ee", "GOSI Annuities Branch (9.0% up to SAR 45k cap)", gosiAnnuitiesEe, "SAR"));
            payLines.Add(new("deduction", "gosi_saned_ee", "SANED Unemployment Insurance (0.75% up to SAR 45k cap)", sanedEe, "SAR"));
            payLines.Add(new("employer_contribution", "gosi_annuities_er", "GOSI Annuities Branch ER (9.0%)", gosiAnnuitiesEr, "SAR"));
            payLines.Add(new("employer_contribution", "gosi_saned_er", "SANED Unemployment Insurance ER (0.75%)", sanedEr, "SAR"));
            payLines.Add(new("employer_contribution", "gosi_occ_hazards_er", "GOSI Occupational Hazards ER (2.0%)", occHazardsEr, "SAR"));
        }
        else
        {
            // Expatriates: Occupational Hazards 2.0% ER, 0% EE
            var occWage = Math.Min(context.BaseSalary + context.Allowances, 45000.00m);
            var occHazardsEr = Math.Round(occWage * 0.02m, 2);
            employerSocialSecurity = occHazardsEr;
            payLines.Add(new("employer_contribution", "gosi_occ_hazards_er", "GOSI Occupational Hazards ER (2.0%)", occHazardsEr, "SAR"));

            // Saudi Labor Law Art. 84 EOSA (End of Service Award)
            // First 5 years: Half-month wage/yr (0.5/12 = 4.167% of basic salary)
            // Beyond 5 years: Full-month wage/yr (1.0/12 = 8.333% of basic salary)
            var monthlyEosaRate = context.YearsOfService > 5 ? (1.0m / 12.0m) : (0.5m / 12.0m);
            eosaAccrual = Math.Round(context.BaseSalary * monthlyEosaRate, 2);
            employerPension = eosaAccrual;
            payLines.Add(new("employer_contribution", "eosa_accrual_er", $"Saudi Labor Law Art 84 EOSA Accrual ({(context.YearsOfService > 5 ? "Full Month" : "Half Month")} Wage/Yr)", eosaAccrual, "SAR"));
        }

        var totalEeDeductions = employeePension;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);
        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "SA",
            WorkJurisdiction: context.WorkJurisdiction ?? "RIYADH",
            GrossPay: grossPay,
            IncomeTaxWithholding: 0.00m,
            EmployeeSocialSecurity: 0.00m,
            EmployerSocialSecurity: employerSocialSecurity,
            EmployeePension: employeePension,
            EmployerPension: employerPension,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "SAR",
            StatutoryAuthority: "General Organization for Social Insurance (GOSI) & Ministry of Human Resources",
            StatutoryCitation: context.IsNationalEmployee
                ? "Social Insurance Law (Royal Decree No. M/33) & SANED (Royal Decree No. M/18)"
                : "Social Insurance Law (Occupational Hazards) & Saudi Labor Law Royal Decree No. M/51 Art 84 (EOSA)",
            MinimumWageCompliant: grossPay >= minWage,
            ApplicableMinimumWage: minWage,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 7. Canada Payroll Adapter (CRA CPP, CPP2, EI & Provincial Taxes)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class CanadaPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "CA";
    public string CountryName => "Canada";
    public string DefaultCurrencyCode => "CAD";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        var prov = (jurisdiction ?? "ON").Trim().ToUpperInvariant();
        return prov switch
        {
            "BC" => 2827.50m, // $17.40/hr * 162.5
            "AB" => 2437.50m, // $15.00/hr * 162.5
            "QC" => 2559.38m, // $15.75/hr * 162.5
            _ => 2795.00m     // ON $17.20/hr * 162.5
        };
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 1.5m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        // 1. Canada Pension Plan (CPP 2026 CRA Formulas)
        // Basic exemption = $3,500/yr ($291.67/mo)
        // YMPE 2026 = $71,300/yr ($5,941.67/mo) -> Base CPP 5.95% EE & ER (max $336.18/mo)
        // YAMPE 2026 = $81,200/yr ($6,766.67/mo) -> CPP2 4.0% EE & ER on excess above YMPE (max $33.00/mo)
        var monthlyExemption = 291.67m;
        var monthlyYmpe = 5941.67m;
        var monthlyYampe = 6766.67m;

        decimal cppBaseEe = 0.00m;
        decimal cppBaseEr = 0.00m;
        decimal cpp2Ee = 0.00m;
        decimal cpp2Er = 0.00m;

        if (grossPay > monthlyExemption)
        {
            var pensionableBase = Math.Min(grossPay, monthlyYmpe) - monthlyExemption;
            cppBaseEe = Math.Round(pensionableBase * 0.0595m, 2);
            cppBaseEr = cppBaseEe;
        }

        if (grossPay > monthlyYmpe)
        {
            var pensionableTier2 = Math.Min(grossPay, monthlyYampe) - monthlyYmpe;
            cpp2Ee = Math.Round(pensionableTier2 * 0.04m, 2);
            cpp2Er = cpp2Ee;
        }

        var totalCppEe = cppBaseEe + cpp2Ee;
        var totalCppEr = cppBaseEr + cpp2Er;

        // 2. Employment Insurance (EI 2026 CRA Formulas)
        // Maximum Annual Insurable Earnings = $65,700/yr ($5,475.00/mo)
        // Employee = 1.63% (max $89.24/mo). Employer = 1.4x = 2.282% (max $124.94/mo).
        var monthlyEiMax = 5475.00m;
        var insurableEiWage = Math.Min(grossPay, monthlyEiMax);
        var eiEe = Math.Round(insurableEiWage * 0.0163m, 2);
        var eiEr = Math.Round(insurableEiWage * 0.02282m, 2);

        // 3. Federal & Provincial Income Tax Withholding (TD1 2026)
        var annualGross = grossPay * 12.0m;
        var federalBpa = 16129.00m;
        var taxableAnnual = Math.Max(0.00m, annualGross - federalBpa);
        decimal annualFedTax;

        if (taxableAnnual <= 57375m)
            annualFedTax = taxableAnnual * 0.15m;
        else if (taxableAnnual <= 114750m)
            annualFedTax = 8606.25m + ((taxableAnnual - 57375m) * 0.205m);
        else if (taxableAnnual <= 177882m)
            annualFedTax = 20368.13m + ((taxableAnnual - 114750m) * 0.26m);
        else if (taxableAnnual <= 253414m)
            annualFedTax = 36782.45m + ((taxableAnnual - 177882m) * 0.29m);
        else
            annualFedTax = 58686.73m + ((taxableAnnual - 253414m) * 0.33m);

        // Provincial Income Tax (Ontario default ~5.05%)
        var annualProvTax = taxableAnnual * 0.0505m;
        var monthlyIncomeTax = Math.Round((annualFedTax + annualProvTax) / 12.0m, 2);

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Base Salary", context.BaseSalary, "CAD"),
            new("earning", "allowances", "Taxable Allowances", context.Allowances, "CAD"),
            new("deduction", "cpp_base_ee", "Canada Pension Plan Base EE (5.95%)", cppBaseEe, "CAD"),
            new("employer_contribution", "cpp_base_er", "Canada Pension Plan Base ER (5.95%)", cppBaseEr, "CAD")
        };

        if (cpp2Ee > 0)
        {
            payLines.Add(new("deduction", "cpp2_enhanced_ee", "CPP2 Second Tier EE (4.0% on YAMPE band)", cpp2Ee, "CAD"));
            payLines.Add(new("employer_contribution", "cpp2_enhanced_er", "CPP2 Second Tier ER (4.0% on YAMPE band)", cpp2Er, "CAD"));
        }

        payLines.Add(new("deduction", "ei_premium_ee", "Employment Insurance EE (1.63%)", eiEe, "CAD"));
        payLines.Add(new("employer_contribution", "ei_premium_er", "Employment Insurance ER (2.282% / 1.4x)", eiEr, "CAD"));
        payLines.Add(new("deduction", "income_tax_fed_prov", "Federal & Provincial Income Tax (CRA TD1)", monthlyIncomeTax, "CAD"));

        var totalEeDeductions = totalCppEe + eiEe + monthlyIncomeTax;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);
        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "CA",
            WorkJurisdiction: context.WorkJurisdiction ?? "ON",
            GrossPay: grossPay,
            IncomeTaxWithholding: monthlyIncomeTax,
            EmployeeSocialSecurity: eiEe,
            EmployerSocialSecurity: eiEr,
            EmployeePension: totalCppEe,
            EmployerPension: totalCppEr,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "CAD",
            StatutoryAuthority: "Canada Revenue Agency (CRA) & Employment and Social Development Canada (ESDC)",
            StatutoryCitation: "Canada Pension Plan Act (R.S.C., 1985, c. C-8) & Employment Insurance Act (S.C. 1996, c. 23)",
            MinimumWageCompliant: grossPay >= minWage,
            ApplicableMinimumWage: minWage,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 8. India Payroll Adapter (CBIC GST, EPFO, ESIC & Sec 115BAC New Tax Regime)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class IndiaPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "IN";
    public string CountryName => "India";
    public string DefaultCurrencyCode => "INR";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        return 15000.00m;
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 2.0m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        // 1. Employees' Provident Fund (EPF & MP Act 1952)
        // Employee: 12% on PF wage (Basic + DA)
        // Employer: 12% split into 8.33% EPS (capped at ₹15,000 wage = ₹1,250 max) + EPF balance
        var pfWage = context.BaseSalary;
        var epfEe = Math.Round(pfWage * 0.12m, 2);

        var epsBase = Math.Min(pfWage, 15000.00m);
        var epsEr = Math.Round(epsBase * 0.0833m, 2); // max ₹1,250.00
        var epfEr = epfEe - epsEr;
        var edliEr = Math.Round(Math.Min(pfWage, 15000.00m) * 0.005m, 2); // 0.5% EDLI max ₹75

        // 2. Employees' State Insurance (ESI Act 1948)
        // Applies only if gross pay <= ₹21,000/month: EE 0.75%, ER 3.25%
        decimal esiEe = 0.00m;
        decimal esiEr = 0.00m;
        if (grossPay <= 21000.00m)
        {
            esiEe = Math.Round(grossPay * 0.0075m, 2);
            esiEr = Math.Round(grossPay * 0.0325m, 2);
        }

        // 3. Professional Tax (PT - State specific e.g. Maharashtra ₹200/mo)
        decimal profTax = 0.00m;
        var state = (context.WorkJurisdiction ?? "MH").Trim().ToUpperInvariant();
        if (grossPay > 10000.00m)
        {
            profTax = (state == "MH" && context.EffectiveDate.Month == 2) ? 300.00m : 200.00m;
        }

        // 4. Income Tax TDS under Section 192 (Section 115BAC New Tax Regime)
        var annualGross = grossPay * 12.0m;
        var taxableAnnual = Math.Max(0.00m, annualGross - 75000.00m); // Standard Deduction ₹75,000
        decimal annualTax = 0.00m;

        if (taxableAnnual > 300000m)
        {
            if (taxableAnnual <= 700000m)
            {
                annualTax = 0.00m; // Sec 87A rebate
            }
            else
            {
                annualTax += 20000m; // 3-7L @ 5%
                var rem = taxableAnnual - 700000m;

                if (rem <= 300000m)
                    annualTax += rem * 0.10m;
                else
                {
                    annualTax += 30000m; // 7-10L @ 10%
                    rem -= 300000m;

                    if (rem <= 200000m)
                        annualTax += rem * 0.15m;
                    else
                    {
                        annualTax += 30000m; // 10-12L @ 15%
                        rem -= 200000m;

                        if (rem <= 300000m)
                            annualTax += rem * 0.20m;
                        else
                        {
                            annualTax += 60000m; // 12-15L @ 20%
                            rem -= 300000m;
                            annualTax += rem * 0.30m; // >15L @ 30%
                        }
                    }
                }

                annualTax = Math.Round(annualTax * 1.04m, 2); // 4% Health & Education Cess
            }
        }

        var monthlyTds = Math.Round(annualTax / 12.0m, 2);

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Basic Salary (PF Wage)", context.BaseSalary, "INR"),
            new("earning", "allowances", "Special & HRA Allowances", context.Allowances, "INR"),
            new("deduction", "epf_ee", "Employees' Provident Fund EE (12.0%)", epfEe, "INR"),
            new("employer_contribution", "epf_er", "EPF Employer Share (3.67% + balance)", epfEr, "INR"),
            new("employer_contribution", "eps_er", "Employees' Pension Scheme ER (8.33% max ₹1,250)", epsEr, "INR"),
            new("employer_contribution", "edli_er", "EDLI Insurance Contribution ER (0.50%)", edliEr, "INR")
        };

        if (esiEe > 0)
        {
            payLines.Add(new("deduction", "esi_ee", "ESI Employee Contribution (0.75%)", esiEe, "INR"));
            payLines.Add(new("employer_contribution", "esi_er", "ESI Employer Contribution (3.25%)", esiEr, "INR"));
        }

        if (profTax > 0)
        {
            payLines.Add(new("deduction", "professional_tax", $"State Professional Tax ({state})", profTax, "INR"));
        }

        if (monthlyTds > 0)
        {
            payLines.Add(new("deduction", "income_tax_tds", "Income Tax TDS (Sec 115BAC New Tax Regime)", monthlyTds, "INR"));
        }

        var totalEeDeductions = epfEe + esiEe + profTax + monthlyTds;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);
        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "IN",
            WorkJurisdiction: context.WorkJurisdiction ?? "MH",
            GrossPay: grossPay,
            IncomeTaxWithholding: monthlyTds,
            EmployeeSocialSecurity: esiEe + profTax,
            EmployerSocialSecurity: esiEr + edliEr,
            EmployeePension: epfEe,
            EmployerPension: epsEr + epfEr,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "INR",
            StatutoryAuthority: "Employees' Provident Fund Organisation (EPFO), ESIC & Income Tax Department",
            StatutoryCitation: "Employees' Provident Funds and Miscellaneous Provisions Act 1952 & Income Tax Act Section 115BAC",
            MinimumWageCompliant: grossPay >= minWage,
            ApplicableMinimumWage: minWage,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 9. European Payroll Adapters (Netherlands, Spain, Italy, Poland)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class NetherlandsPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "NL";
    public string CountryName => "Netherlands";
    public string DefaultCurrencyCode => "EUR";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        // Dutch statutory minimum wage 2026: €13.68/hr * 160 = €2,188.80/month
        return 2188.80m;
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + context.Bonus);
        var annualGross = grossPay * 12.0m;

        // Loonheffing (Dutch Wage Tax 2026 2-bracket): 36.97% up to €75,518; 49.50% above
        decimal annualTax;
        if (annualGross <= 75518m)
            annualTax = annualGross * 0.3697m;
        else
            annualTax = (75518m * 0.3697m) + ((annualGross - 75518m) * 0.4950m);

        // General tax credit (Algemene heffingskorting max €3,362, tapered)
        var generalCredit = Math.Max(0.00m, 3362m - (Math.Max(0.00m, annualGross - 24812m) * 0.0663m));
        var netAnnualTax = Math.Max(0.00m, annualTax - generalCredit);
        var monthlyTax = Math.Round(netAnnualTax / 12.0m, 2);

        // Employer Social Premiums (AOF, Awf, Whk ~18.5%)
        var erPremiums = Math.Round(grossPay * 0.185m, 2);

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Base Salary", context.BaseSalary, "EUR"),
            new("deduction", "loonheffing", "Loonheffing (Wage Tax & National Insurance)", monthlyTax, "EUR"),
            new("employer_contribution", "social_premiums_er", "Werkgeverspremies (AOF / Awf / Whk 18.5%)", erPremiums, "EUR")
        };

        var netPay = Math.Max(0.00m, grossPay - monthlyTax);
        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "NL",
            WorkJurisdiction: context.WorkJurisdiction ?? "AMSTERDAM",
            GrossPay: grossPay,
            IncomeTaxWithholding: monthlyTax,
            EmployeeSocialSecurity: 0.00m,
            EmployerSocialSecurity: erPremiums,
            EmployeePension: 0.00m,
            EmployerPension: 0.00m,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: monthlyTax,
            NetPay: netPay,
            CurrencyCode: "EUR",
            StatutoryAuthority: "Belastingdienst (Dutch Tax Administration) & UWV",
            StatutoryCitation: "Wet op de loonbelasting 1964 & Wet financiering sociale verzekeringen",
            MinimumWageCompliant: grossPay >= 2188.80m,
            ApplicableMinimumWage: 2188.80m,
            PayLines: payLines);
    }
}

public sealed class SpainPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "ES";
    public string CountryName => "Spain";
    public string DefaultCurrencyCode => "EUR";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        // Salario Mínimo Interprofesional (SMI) 2026: €1,134.00 (14 payments) = €1,323.00/month (12 payments)
        return 1323.00m;
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + context.Bonus);

        // Seguridad Social: Base capped at €4,720.50/mo
        var baseCotizacion = Math.Min(grossPay, 4720.50m);
        // Employee: 4.70% Contingencias Comunes + 1.55% Desempleo + 0.10% Formación = 6.35%
        var ssEe = Math.Round(baseCotizacion * 0.0635m, 2);
        // Employer: 23.60% CC + 5.50% Desempleo + 0.60% FP + 0.20% FOGASA = 29.90%
        var ssEr = Math.Round(baseCotizacion * 0.2990m, 2);

        // IRPF Withholding (Progressive scales ~15.0% for median salary)
        var irpfRate = grossPay > 5000m ? 0.30m : grossPay > 3000m ? 0.19m : 0.15m;
        var irpfWithholding = Math.Round(grossPay * irpfRate, 2);

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Sueldo Base", context.BaseSalary, "EUR"),
            new("deduction", "seguridad_social_ee", "Seguridad Social Trabajador (6.35%)", ssEe, "EUR"),
            new("employer_contribution", "seguridad_social_er", "Seguridad Social Empresa (29.90%)", ssEr, "EUR"),
            new("deduction", "retencion_irpf", $"Retención IRPF ({(irpfRate * 100):F1}%)", irpfWithholding, "EUR")
        };

        var totalEe = ssEe + irpfWithholding;
        var netPay = Math.Max(0.00m, grossPay - totalEe);
        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "ES",
            WorkJurisdiction: context.WorkJurisdiction ?? "MADRID",
            GrossPay: grossPay,
            IncomeTaxWithholding: irpfWithholding,
            EmployeeSocialSecurity: ssEe,
            EmployerSocialSecurity: ssEr,
            EmployeePension: 0.00m,
            EmployerPension: 0.00m,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEe,
            NetPay: netPay,
            CurrencyCode: "EUR",
            StatutoryAuthority: "Seguridad Social & Agencia Estatal de Administración Tributaria (AEAT)",
            StatutoryCitation: "Ley General de la Seguridad Social (Real Decreto Legislativo 8/2015) & Ley IRPF",
            MinimumWageCompliant: grossPay >= 1323.00m,
            ApplicableMinimumWage: 1323.00m,
            PayLines: payLines);
    }
}

public sealed class ItalyPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "IT";
    public string CountryName => "Italy";
    public string DefaultCurrencyCode => "EUR";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        // CCNL national sectoral contract minimum benchmark (approx €1,600.00/mo)
        return 1600.00m;
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + context.Bonus);

        // INPS Social Contributions: Employee 9.19%, Employer ~30.0%
        var inpsEe = Math.Round(grossPay * 0.0919m, 2);
        var inpsEr = Math.Round(grossPay * 0.3000m, 2);

        // TFR Accrual (Trattamento di Fine Rapporto = Gross / 13.5 = ~7.41%)
        var tfrAccrual = Math.Round(grossPay / 13.5m, 2);

        // IRPEF Withholding (2026 3-bracket: 23% to 28k, 35% 28-50k, 43% > 50k)
        var taxableMonthly = Math.Max(0.00m, grossPay - inpsEe);
        var irpefWithholding = Math.Round(taxableMonthly * 0.23m, 2);

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Retribuzione Base", context.BaseSalary, "EUR"),
            new("deduction", "contributi_inps_ee", "Contributi Previdenziali INPS (9.19%)", inpsEe, "EUR"),
            new("employer_contribution", "contributi_inps_er", "Contributi Previdenziali INPS Datore (30.0%)", inpsEr, "EUR"),
            new("employer_contribution", "accantonamento_tfr", "Accantonamento TFR (Retribuzione / 13.5)", tfrAccrual, "EUR"),
            new("deduction", "ritenute_irpef", "Ritenute Fiscali IRPEF (Scaglione Base 23%)", irpefWithholding, "EUR")
        };

        var totalEe = inpsEe + irpefWithholding;
        var netPay = Math.Max(0.00m, grossPay - totalEe);
        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "IT",
            WorkJurisdiction: context.WorkJurisdiction ?? "ROMA",
            GrossPay: grossPay,
            IncomeTaxWithholding: irpefWithholding,
            EmployeeSocialSecurity: inpsEe,
            EmployerSocialSecurity: inpsEr + tfrAccrual,
            EmployeePension: 0.00m,
            EmployerPension: tfrAccrual,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEe,
            NetPay: netPay,
            CurrencyCode: "EUR",
            StatutoryAuthority: "Istituto Nazionale Previdenza Sociale (INPS) & Agenzia delle Entrate",
            StatutoryCitation: "Testo Unico delle Imposte sui Redditi (TUIR) & CCNL Metalmeccanico/Terziario",
            MinimumWageCompliant: grossPay >= 1600.00m,
            ApplicableMinimumWage: 1600.00m,
            PayLines: payLines);
    }
}

public sealed class PolandPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "PL";
    public string CountryName => "Poland";
    public string DefaultCurrencyCode => "PLN";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        // Minimalne wynagrodzenie za pracę 2026: 4,300.00 PLN
        return 4300.00m;
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + context.Bonus);

        // ZUS Social Insurance:
        // Employee: Emerytalne 9.76% + Rentowe 1.50% + Chorobowe 2.45% = 13.71%
        var zusEe = Math.Round(grossPay * 0.1371m, 2);
        // Employer: Emerytalne 9.76% + Rentowe 6.50% + Wypadkowe 1.67% + FP/FS 2.45% = 20.38%
        var zusEr = Math.Round(grossPay * 0.2038m, 2);

        // Health Insurance (Składka zdrowotna NFZ): 9.0% on (Gross - ZUS EE)
        var healthBase = grossPay - zusEe;
        var healthEe = Math.Round(healthBase * 0.09m, 2);

        // PIT Income Tax: 12% on taxable base minus tax-reducing amount (kwota zmniejszająca 300 PLN/mo)
        var pitBase = Math.Max(0.00m, healthBase - 250.00m); // 250 PLN standard tax deductible costs
        var pitGross = Math.Round(pitBase * 0.12m, 2);
        var pitWithholding = Math.Max(0.00m, pitGross - 300.00m);

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Wynagrodzenie Zasadnicze", context.BaseSalary, "PLN"),
            new("deduction", "zus_spoleczne_ee", "Składki na ubezpieczenia społeczne ZUS (13.71%)", zusEe, "PLN"),
            new("deduction", "skladka_zdrowotna_ee", "Składka na ubezpieczenie zdrowotne NFZ (9.0%)", healthEe, "PLN"),
            new("deduction", "zaliczka_pit", "Zaliczka na podatek dochodowy PIT (12% - 300 PLN)", pitWithholding, "PLN"),
            new("employer_contribution", "zus_pracodawca_er", "Składki ZUS finansowane przez pracodawcę (20.38%)", zusEr, "PLN")
        };

        var totalEe = zusEe + healthEe + pitWithholding;
        var netPay = Math.Max(0.00m, grossPay - totalEe);
        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "PL",
            WorkJurisdiction: context.WorkJurisdiction ?? "WARSZAWA",
            GrossPay: grossPay,
            IncomeTaxWithholding: pitWithholding,
            EmployeeSocialSecurity: zusEe + healthEe,
            EmployerSocialSecurity: zusEr,
            EmployeePension: 0.00m,
            EmployerPension: 0.00m,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEe,
            NetPay: netPay,
            CurrencyCode: "PLN",
            StatutoryAuthority: "Zakład Ubezpieczeń Społecznych (ZUS) & Krajowa Administracja Skarbowa (KAS)",
            StatutoryCitation: "Ustawa o systemie ubezpieczeń społecznych & Ustawa o podatku dochodowym od osób fizycznych (PIT)",
            MinimumWageCompliant: grossPay >= 4300.00m,
            ApplicableMinimumWage: 4300.00m,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 10. Global Dispatching Engine
// ─────────────────────────────────────────────────────────────────────────────
public sealed class GlobalPayrollEngine
{
    private readonly Dictionary<string, IPayrollCountryAdapter> _adapters;

    public GlobalPayrollEngine()
    {
        _adapters = new Dictionary<string, IPayrollCountryAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            ["US"] = new UnitedStatesPayrollAdapter(),
            ["GB"] = new UnitedKingdomPayrollAdapter(),
            ["AE"] = new UnitedArabEmiratesPayrollAdapter(),
            ["AU"] = new AustraliaPayrollAdapter(),
            ["PK"] = new PakistanPayrollAdapter(),
            ["SA"] = new SaudiArabiaPayrollAdapter(),
            ["CA"] = new CanadaPayrollAdapter(),
            ["IN"] = new IndiaPayrollAdapter(),
            ["NL"] = new NetherlandsPayrollAdapter(),
            ["ES"] = new SpainPayrollAdapter(),
            ["IT"] = new ItalyPayrollAdapter(),
            ["PL"] = new PolandPayrollAdapter()
        };
    }

    public PayrollCalculationResult Calculate(PayrollCalculationContext context)
    {
        if (_adapters.TryGetValue(context.CountryCode, out var adapter))
        {
            return adapter.CalculatePayroll(context);
        }

        // Generic International Fallback
        var gross = context.BaseSalary + context.Allowances + context.Bonus;
        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: context.CountryCode,
            WorkJurisdiction: context.WorkJurisdiction,
            GrossPay: gross,
            IncomeTaxWithholding: 0.00m,
            EmployeeSocialSecurity: 0.00m,
            EmployerSocialSecurity: 0.00m,
            EmployeePension: 0.00m,
            EmployerPension: 0.00m,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: 0.00m,
            NetPay: gross,
            CurrencyCode: context.CurrencyCode,
            StatutoryAuthority: "Standard Global Ledger",
            StatutoryCitation: "Standard Generic Employment Contract",
            MinimumWageCompliant: true,
            ApplicableMinimumWage: 0.00m,
            PayLines: new[]
            {
                new PayrollPayLine("earning", "base_salary", "Base Salary", context.BaseSalary, context.CurrencyCode),
                new PayrollPayLine("earning", "allowances", "Allowances", context.Allowances, context.CurrencyCode)
            });
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context) => Calculate(context);

    public decimal GetMinimumWage(string countryCode, string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        if (_adapters.TryGetValue(countryCode, out var adapter))
        {
            return adapter.GetStatutoryMinimumWage(jurisdiction, skillCategory, effectiveDate);
        }
        return 0.00m;
    }
}
