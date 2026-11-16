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
    decimal OrdinaryTimeEarnings = 0.00m);

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
// 6. Global Dispatching Engine
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
            ["PK"] = new PakistanPayrollAdapter()
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
