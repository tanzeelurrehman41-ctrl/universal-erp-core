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
    decimal SutaRate = 0.034m);

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
// 1. United States Payroll Adapter (IRS / SSA / DOL)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class UnitedStatesPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "US";
    public string CountryName => "United States";
    public string DefaultCurrencyCode => "USD";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        var state = (jurisdiction ?? "").Trim().ToUpperInvariant();
        // State minimum wage overrides federal $7.25/hr
        var hourly = state switch
        {
            "CA" => 16.50m, // California 2026 minimum wage
            "NY" => 16.50m, // New York 2026 minimum wage
            "WA" => 16.66m, // Washington State 2026 minimum wage
            _ => 7.25m      // US Federal Fair Labor Standards Act (FLSA)
        };
        // Standard 173.33 monthly working hours
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
        var isMfj = string.Equals(context.FilingStatus, "married_joint", StringComparison.OrdinalIgnoreCase);
        var monthlyAdditionalMedicareThreshold = isMfj ? 20833.33m : 16666.67m;

        var baseMedicareEe = Math.Round(grossPay * 0.0145m, 2);
        var additionalMedicareEe = 0.00m;
        if (grossPay > monthlyAdditionalMedicareThreshold)
        {
            additionalMedicareEe = Math.Round((grossPay - monthlyAdditionalMedicareThreshold) * 0.009m, 2);
        }
        var totalMedicareEe = baseMedicareEe + additionalMedicareEe;
        var medicareEr = Math.Round(grossPay * 0.0145m, 2); // Employer does not match 0.9% additional tax

        // 3. Federal Income Tax (FIT) Withholding (2026 Official Schedules)
        var annualGross = grossPay * 12.0m;
        decimal standardDeduction;
        if (context.EffectiveDate >= new DateOnly(2026, 1, 1))
        {
            standardDeduction = isMfj ? 31400.00m :
                string.Equals(context.FilingStatus, "head_of_household", StringComparison.OrdinalIgnoreCase) ? 23550.00m : 15700.00m;
        }
        else
        {
            standardDeduction = isMfj ? 30000.00m : 15000.00m;
        }

        var annualTaxable = Math.Max(0.00m, annualGross - standardDeduction);
        decimal annualFit;

        if (isMfj)
        {
            // 2026 Married Filing Jointly Brackets
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
        else
        {
            // 2026 Single / Head of Household Brackets
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

        var monthlyFit = Math.Round(annualFit / 12.0m, 2);

        // 4. Unemployment Obligations (FUTA & SUTA - Employer Only)
        // FUTA: 0.6% on first $7,000/yr ($583.33/mo)
        var monthlyFutaWage = Math.Min(grossPay, 583.33m);
        var futaEr = Math.Round(monthlyFutaWage * 0.006m, 2);

        // SUTA: Employer state obligation on state taxable base (default $833.33/mo base e.g. $10,000)
        var sutaRate = context.SutaRate > 0 ? context.SutaRate : 0.034m;
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
            new("deduction", "federal_income_tax", $"Federal Income Tax (FIT 2026 - {context.FilingStatus})", monthlyFit, "USD"),
            new("deduction", "fica_social_security", "FICA Social Security (6.2%)", ficaEe, "USD"),
            new("deduction", "fica_medicare", "Medicare Tax (1.45%)", baseMedicareEe, "USD")
        };

        if (additionalMedicareEe > 0)
        {
            payLines.Add(new("deduction", "additional_medicare", "Additional Medicare Tax (0.9%)", additionalMedicareEe, "USD"));
        }

        payLines.Add(new("employer_contribution", "fica_social_security_er", "Employer FICA Social Security", ficaEr, "USD"));
        payLines.Add(new("employer_contribution", "fica_medicare_er", "Employer Medicare", medicareEr, "USD"));
        payLines.Add(new("employer_contribution", "futa_tax_er", "Federal Unemployment Tax (FUTA 0.6%)", futaEr, "USD"));
        payLines.Add(new("employer_contribution", "suta_tax_er", $"State Unemployment Tax (SUTA {sutaRate:P1})", sutaEr, "USD"));

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "US",
            WorkJurisdiction: context.WorkJurisdiction,
            GrossPay: grossPay,
            IncomeTaxWithholding: monthlyFit,
            EmployeeSocialSecurity: ficaEe + totalMedicareEe,
            EmployerSocialSecurity: ficaEr + medicareEr,
            EmployeePension: 0.00m,
            EmployerPension: futaEr + sutaEr,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "USD",
            StatutoryAuthority: "Internal Revenue Service (IRS) & Social Security Administration (SSA)",
            StatutoryCitation: "IRC § 3402, FICA § 3101/3111 (2026 OASDI Base $184,500), FUTA § 3301",
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
        var monthlyPersonalAllowance = 1047.50m;
        var taxableIncome = Math.Max(0.00m, grossPay - monthlyPersonalAllowance);
        decimal monthlyTax = 0.00m;

        var isScotland = string.Equals(context.WorkJurisdiction, "SCOTLAND", StringComparison.OrdinalIgnoreCase);

        if (isScotland)
        {
            // Scottish Income Tax 2026–27 Bands (Monthly Taxable thresholds):
            // Starter: 19% on £0 to £192.08
            // Basic: 20% on £192.09 to £1,165.83 (band £973.75)
            // Intermediate: 21% on £1,165.84 to £2,590.83 (band £1,425.00)
            // Higher: 42% on £2,590.84 to £5,202.33 (band £2,611.50)
            // Advanced: 45% on £5,202.34 to £9,380.67 (band £4,178.33)
            // Top: 48% on over £9,380.67
            if (taxableIncome > 0)
            {
                var rem = taxableIncome;
                var band1 = Math.Min(rem, 192.08m);
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
                    var band3 = Math.Min(rem, 1425.00m);
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
                    var band5 = Math.Min(rem, 4178.33m);
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
            // Rest of UK (England, Wales, Northern Ireland) Bands:
            // Basic: 20% up to £3,141.67 (£37,700/yr)
            // Higher: 40% between £3,141.67 and £9,380.83 (£74,870/yr)
            // Additional: 45% over £9,380.83
            var basicThreshold = 3141.67m;
            var higherThreshold = 9380.83m;

            if (taxableIncome <= basicThreshold)
            {
                monthlyTax = taxableIncome * 0.20m;
            }
            else if (taxableIncome <= higherThreshold)
            {
                monthlyTax = (basicThreshold * 0.20m) + ((taxableIncome - basicThreshold) * 0.40m);
            }
            else
            {
                monthlyTax = (basicThreshold * 0.20m) + ((higherThreshold - basicThreshold) * 0.40m) +
                             ((taxableIncome - higherThreshold) * 0.45m);
            }
        }
        monthlyTax = Math.Round(monthlyTax, 2);

        // 2. Class 1 National Insurance Contributions (NIC)
        // Primary Threshold (PT): £1,048/mo. Upper Earnings Limit (UEL): £4,189/mo.
        var pt = 1048.00m;
        var uel = 4189.00m;
        var cat = (context.UkCategoryLetter ?? "A").Trim().ToUpperInvariant();

        decimal nicEe = 0.00m;
        if (cat == "C")
        {
            // Over state pension age: 0% employee NIC
            nicEe = 0.00m;
        }
        else if (cat == "B")
        {
            // Married women reduced rate: 5.85% between PT and UEL, 2% above UEL
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
            // Deferment: 2% above UEL only
            if (grossPay > uel)
            {
                nicEe = Math.Round((grossPay - uel) * 0.02m, 2);
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

        // 3. Student Loan Deductions
        decimal studentLoanDeduction = 0.00m;
        if (!string.IsNullOrWhiteSpace(context.StudentLoanPlan))
        {
            var plan = context.StudentLoanPlan.Trim().ToUpperInvariant();
            var (thresh, rate) = plan switch
            {
                "PLAN1" => (2082.50m, 0.09m),
                "PLAN2" => (2274.58m, 0.09m),
                "PLAN4" => (2616.25m, 0.09m),
                "PLAN5" => (2083.33m, 0.09m),
                "POSTGRADUATE" => (1750.00m, 0.06m),
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
            var qualEarnings = Math.Min(grossPay, 4189.00m) - 520.00m;
            var eeRate = context.PensionEmployeeRate > 0 ? context.PensionEmployeeRate : 0.05m;
            var erRate = context.PensionEmployerRate > 0 ? context.PensionEmployerRate : 0.03m;
            pensionEe = Math.Round(qualEarnings * eeRate, 2);
            pensionEr = Math.Round(qualEarnings * erRate, 2);
        }

        var totalEeDeductions = monthlyTax + nicEe + studentLoanDeduction + pensionEe;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);

        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);
        var isCompliant = grossPay >= minWage;

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Base Salary", context.BaseSalary, "GBP"),
            new("deduction", "paye_income_tax", $"PAYE Income Tax ({(isScotland ? "Scottish" : "Rest of UK")})", monthlyTax, "GBP"),
            new("deduction", "nic_employee", $"Employee National Insurance (Class 1 Category {cat})", nicEe, "GBP"),
            new("employer_contribution", "nic_employer", $"Employer National Insurance ({employerNicRate:P0} above £{secondaryThreshold:F0})", nicEr, "GBP")
        };

        if (studentLoanDeduction > 0)
        {
            payLines.Add(new("deduction", "student_loan", $"Student Loan ({context.StudentLoanPlan})", studentLoanDeduction, "GBP"));
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
// 3. United Arab Emirates Payroll Adapter (GPSSA & EOSG)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class UnitedArabEmiratesPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "AE";
    public string CountryName => "United Arab Emirates";
    public string DefaultCurrencyCode => "AED";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        // UAE Labour Law does not specify a numerical national minimum wage, but MOHRE guidelines recommend AED 5,000 for skilled
        return 5000.00m;
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 1.25m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        // 1. Personal Income Tax in UAE is strictly 0.00%
        decimal incomeTax = 0.00m;

        // 2. GPSSA Pension for UAE Nationals (5% Employee, 12.5% Employer of Basic Salary + Housing)
        decimal gpssaEe = 0.00m;
        decimal gpssaEr = 0.00m;

        if (context.IsNationalEmployee)
        {
            var pensionableSalary = context.BaseSalary + context.Allowances;
            gpssaEe = Math.Round(pensionableSalary * 0.05m, 2);
            gpssaEr = Math.Round(pensionableSalary * 0.125m, 2);
        }

        // 3. End of Service Gratuity (EOSG) Accrual for Expats (21 days/yr = 1.75 days/mo = ~5.75% of basic)
        decimal eosgMonthlyAccrual = 0.00m;
        if (!context.IsNationalEmployee)
        {
            eosgMonthlyAccrual = Math.Round((context.BaseSalary / 30.0m) * 1.75m, 2);
        }

        var totalEeDeductions = gpssaEe;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);

        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);
        var isCompliant = grossPay >= minWage;

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Basic Salary", context.BaseSalary, "AED"),
            new("earning", "allowances", "Housing and Transport Allowances", context.Allowances, "AED")
        };

        if (context.IsNationalEmployee)
        {
            payLines.Add(new("deduction", "gpssa_ee", "GPSSA Pension Contribution (5%)", gpssaEe, "AED"));
            payLines.Add(new("employer_contribution", "gpssa_er", "Employer GPSSA Pension (12.5%)", gpssaEr, "AED"));
        }
        else
        {
            payLines.Add(new("employer_contribution", "eosg_accrual", "Monthly End of Service Gratuity Accrual", eosgMonthlyAccrual, "AED"));
        }

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "AE",
            WorkJurisdiction: context.WorkJurisdiction ?? "DUBAI",
            GrossPay: grossPay,
            IncomeTaxWithholding: incomeTax,
            EmployeeSocialSecurity: 0.00m,
            EmployerSocialSecurity: 0.00m,
            EmployeePension: gpssaEe,
            EmployerPension: gpssaEr + eosgMonthlyAccrual,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "AED",
            StatutoryAuthority: "Ministry of Human Resources and Emiratisation (MOHRE) & GPSSA",
            StatutoryCitation: "UAE Federal Decree-Law No. (33) of 2021 & Federal Law No. (7) of 1999 (Pensions)",
            MinimumWageCompliant: isCompliant,
            ApplicableMinimumWage: minWage,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. Pakistan Statutory Payroll Adapter (FBR, EOBI, SESSI/PESSI)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class PakistanPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "PK";
    public string CountryName => "Pakistan";
    public string DefaultCurrencyCode => "PKR";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        var jur = (jurisdiction ?? "FEDERAL").Trim().ToUpperInvariant();
        var skill = (skillCategory ?? "unskilled").Trim().ToLowerInvariant();

        if (jur == "SINDH")
        {
            return skill switch
            {
                "semi_skilled" or "semiskilled" => 44484m,
                "skilled" => 53350m,
                "highly_skilled" or "highlyskilled" => 55626m,
                _ => 43000m
            };
        }

        return jur switch
        {
            "FEDERAL" or "ICT" => 40700m,
            "PUNJAB" => 40000m,
            "KPK" or "KP" => 40000m,
            "BALOCHISTAN" => 37000m,
            _ => 40700m
        };
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 2.0m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        // 1. FBR Income Tax Withholding (Tax Year 2027 Salaried Slabs)
        var annualIncome = grossPay * 12.0m;
        decimal annualTax;

        if (annualIncome <= 600000m)
            annualTax = 0.00m;
        else if (annualIncome <= 1200000m)
            annualTax = (annualIncome - 600000m) * 0.01m;
        else if (annualIncome <= 2200000m)
            annualTax = 6000m + ((annualIncome - 1200000m) * 0.11m);
        else if (annualIncome <= 3200000m)
            annualTax = 116000m + ((annualIncome - 2200000m) * 0.20m);
        else if (annualIncome <= 4100000m)
            annualTax = 316000m + ((annualIncome - 3200000m) * 0.25m);
        else if (annualIncome <= 5600000m)
            annualTax = 541000m + ((annualIncome - 4100000m) * 0.29m);
        else if (annualIncome <= 7000000m)
            annualTax = 976000m + ((annualIncome - 5600000m) * 0.32m);
        else
            annualTax = 1424000m + ((annualIncome - 7000000m) * 0.35m);

        var monthlyTax = Math.Round(annualTax / 12.0m, 2);

        // 2. EOBI (PKR 37,000 statutory minimum wage base: 1% Employee = PKR 370, 5% Employer = PKR 1,850)
        var eobiBase = 37000.00m;
        var eobiEe = Math.Round(eobiBase * 0.01m, 2);
        var eobiEr = Math.Round(eobiBase * 0.05m, 2);

        // 3. Provincial Social Security (6% Employer up to provincial wage ceiling)
        var ceiling = (context.WorkJurisdiction ?? "FEDERAL").Trim().ToUpperInvariant() switch
        {
            "SINDH" => 43000.00m,
            "PUNJAB" => 40000.00m,
            "KPK" or "KP" => 40000.00m,
            "BALOCHISTAN" => 37000.00m,
            _ => 40700.00m
        };
        var socialSecurityWage = Math.Min(grossPay, ceiling);
        var socialSecurityEr = Math.Round(socialSecurityWage * 0.06m, 2);

        var totalEeDeductions = monthlyTax + eobiEe;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);

        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);
        var isCompliant = grossPay >= minWage;

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Basic Salary", context.BaseSalary, "PKR"),
            new("earning", "allowances", "Allowances", context.Allowances, "PKR"),
            new("deduction", "income_tax", "Income Tax Withholding (TY2027)", monthlyTax, "PKR"),
            new("deduction", "eobi_employee", "EOBI Employee Share (1%)", eobiEe, "PKR"),
            new("employer_contribution", "eobi_employer", "EOBI Employer Share (5%)", eobiEr, "PKR"),
            new("employer_contribution", "social_security_er", "Provincial Social Security Employer Share (6%)", socialSecurityEr, "PKR")
        };

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "PK",
            WorkJurisdiction: context.WorkJurisdiction ?? "FEDERAL",
            GrossPay: grossPay,
            IncomeTaxWithholding: monthlyTax,
            EmployeeSocialSecurity: 0.00m,
            EmployerSocialSecurity: socialSecurityEr,
            EmployeePension: eobiEe,
            EmployerPension: eobiEr,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "PKR",
            StatutoryAuthority: "Federal Board of Revenue (FBR) & EOBI",
            StatutoryCitation: "Income Tax Ordinance 2001 (Finance Act 2026) & EOBI Act 1976",
            MinimumWageCompliant: isCompliant,
            ApplicableMinimumWage: minWage,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 5. Global Configurable Payroll Adapter (Worldwide Generic Fallback)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class GlobalConfigurablePayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "GLOBAL";
    public string CountryName => "Global International";
    public string DefaultCurrencyCode => "USD";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate) => 1500.00m;

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 1.5m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        decimal incomeTax = 0.00m;

        // Evaluate against custom slabs if supplied
        if (context.CustomSlabs != null && context.CustomSlabs.Count > 0)
        {
            var annual = grossPay * 12.0m;
            foreach (var slab in context.CustomSlabs.OrderBy(s => s.Order))
            {
                if (annual >= slab.MinAnnualIncome && (slab.MaxAnnualIncome == null || annual <= slab.MaxAnnualIncome.Value))
                {
                    var excess = annual - slab.MinAnnualIncome;
                    var annualTax = slab.FixedTaxAmount + (excess * (slab.MarginalRatePercentage / 100.0m));
                    incomeTax = Math.Round(annualTax / 12.0m, 2);
                    break;
                }
            }
        }
        else
        {
            // Default flat 10% withholding
            incomeTax = Math.Round(grossPay * 0.10m, 2);
        }

        var totalDeductions = incomeTax;
        var netPay = Math.Max(0.00m, grossPay - totalDeductions);
        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Base Salary", context.BaseSalary, context.CurrencyCode),
            new("deduction", "income_tax", "Statutory Withholding Tax", incomeTax, context.CurrencyCode)
        };

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: context.CountryCode,
            WorkJurisdiction: context.WorkJurisdiction,
            GrossPay: grossPay,
            IncomeTaxWithholding: incomeTax,
            EmployeeSocialSecurity: 0.00m,
            EmployerSocialSecurity: 0.00m,
            EmployeePension: 0.00m,
            EmployerPension: 0.00m,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalDeductions,
            NetPay: netPay,
            CurrencyCode: context.CurrencyCode,
            StatutoryAuthority: "Local Jurisdiction Authority",
            StatutoryCitation: "Standard International Payroll Specification",
            MinimumWageCompliant: grossPay >= minWage,
            ApplicableMinimumWage: minWage,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Global Payroll Engine Factory / Dispatcher
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
            ["PK"] = new PakistanPayrollAdapter(),
            ["GLOBAL"] = new GlobalConfigurablePayrollAdapter()
        };
    }

    public IPayrollCountryAdapter GetAdapter(string? countryCode)
    {
        var code = (countryCode ?? "GLOBAL").Trim().ToUpperInvariant();
        if (_adapters.TryGetValue(code, out var adapter))
        {
            return adapter;
        }
        return _adapters["GLOBAL"];
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var adapter = GetAdapter(context.CountryCode);
        return adapter.CalculatePayroll(context);
    }
}
