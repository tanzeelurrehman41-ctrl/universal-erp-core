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
    IReadOnlyList<ConfigurableTaxSlab>? CustomSlabs = null);

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
            "CA" => 16.00m, // California minimum wage $16.00/hr
            "NY" => 16.00m, // New York minimum wage $16.00/hr
            "WA" => 16.28m, // Washington State minimum wage
            _ => 7.25m      // US Federal Fair Labor Standards Act (FLSA)
        };
        // Standard 173.33 monthly working hours
        return Math.Round(hourly * 173.33m, 2);
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 1.5m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        // 1. FICA Social Security (6.2% EE, 6.2% ER up to annual wage base $168,600 / $14,050 mo)
        var monthlyFicaCap = 14050.00m;
        var ficaSubjectWage = Math.Min(grossPay, monthlyFicaCap);
        var ficaEe = Math.Round(ficaSubjectWage * 0.062m, 2);
        var ficaEr = Math.Round(ficaSubjectWage * 0.062m, 2);

        // 2. Medicare (1.45% EE, 1.45% ER + 0.9% additional over $200k/yr = $16,666.67/mo)
        var medicareEe = Math.Round(grossPay * 0.0145m, 2);
        if (grossPay > 16666.67m)
        {
            medicareEe += Math.Round((grossPay - 16666.67m) * 0.009m, 2);
        }
        var medicareEr = Math.Round(grossPay * 0.0145m, 2);

        // 3. Federal Income Tax (FIT) Withholding (Annualized percentage method with standard deduction)
        var annualGross = grossPay * 12.0m;
        var annualTaxable = Math.Max(0.00m, annualGross - 14600.00m); // Single standard deduction
        decimal annualFit;

        if (annualTaxable <= 11600m)
            annualFit = annualTaxable * 0.10m;
        else if (annualTaxable <= 47150m)
            annualFit = 1160m + ((annualTaxable - 11600m) * 0.12m);
        else if (annualTaxable <= 100525m)
            annualFit = 5426m + ((annualTaxable - 47150m) * 0.22m);
        else if (annualTaxable <= 191950m)
            annualFit = 17168.50m + ((annualTaxable - 100525m) * 0.24m);
        else
            annualFit = 39110.50m + ((annualTaxable - 191950m) * 0.32m);

        var monthlyFit = Math.Round(annualFit / 12.0m, 2);

        var totalEeDeductions = ficaEe + medicareEe + monthlyFit;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);

        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);
        var isCompliant = grossPay >= minWage;

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Base Salary", context.BaseSalary, "USD"),
            new("earning", "allowances", "Allowances", context.Allowances, "USD"),
            new("deduction", "federal_income_tax", "Federal Income Tax (FIT)", monthlyFit, "USD"),
            new("deduction", "fica_social_security", "FICA Social Security (6.2%)", ficaEe, "USD"),
            new("deduction", "fica_medicare", "Medicare Tax (1.45%)", medicareEe, "USD"),
            new("employer_contribution", "fica_social_security_er", "Employer FICA Social Security", ficaEr, "USD"),
            new("employer_contribution", "fica_medicare_er", "Employer Medicare", medicareEr, "USD")
        };

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "US",
            WorkJurisdiction: context.WorkJurisdiction,
            GrossPay: grossPay,
            IncomeTaxWithholding: monthlyFit,
            EmployeeSocialSecurity: ficaEe + medicareEe,
            EmployerSocialSecurity: ficaEr + medicareEr,
            EmployeePension: 0.00m,
            EmployerPension: 0.00m,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "USD",
            StatutoryAuthority: "Internal Revenue Service (IRS) & Social Security Administration (SSA)",
            StatutoryCitation: "Internal Revenue Code (IRC) § 3402 & FICA § 3101/3111",
            MinimumWageCompliant: isCompliant,
            ApplicableMinimumWage: minWage,
            PayLines: payLines);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. United Kingdom Payroll Adapter (HMRC PAYE & NIC)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class UnitedKingdomPayrollAdapter : IPayrollCountryAdapter
{
    public string CountryCode => "GB";
    public string CountryName => "United Kingdom";
    public string DefaultCurrencyCode => "GBP";

    public decimal GetStatutoryMinimumWage(string? jurisdiction, string skillCategory, DateOnly effectiveDate)
    {
        // UK National Living Wage: £11.44/hr * 162.5 monthly hours = £1,859.00
        return 1859.00m;
    }

    public PayrollCalculationResult CalculatePayroll(PayrollCalculationContext context)
    {
        var overtimePay = Math.Round(context.OvertimeHours * context.OvertimeHourlyRate * 1.5m, 2);
        var grossPay = Math.Max(0.00m, context.BaseSalary + context.Allowances + overtimePay + context.Bonus);

        // 1. HMRC PAYE Income Tax (Emergency Code 1257L: Personal allowance £12,570/yr = £1,047.50/mo)
        var monthlyPersonalAllowance = 1047.50m;
        var taxableIncome = Math.Max(0.00m, grossPay - monthlyPersonalAllowance);
        decimal monthlyTax;

        var basicThreshold = 3141.67m; // (£50,270 - £12,570) / 12
        if (taxableIncome <= basicThreshold)
        {
            monthlyTax = Math.Round(taxableIncome * 0.20m, 2);
        }
        else
        {
            var basicTax = Math.Round(basicThreshold * 0.20m, 2);
            var higherTax = Math.Round((taxableIncome - basicThreshold) * 0.40m, 2);
            monthlyTax = basicTax + higherTax;
        }

        // 2. Class 1 National Insurance Contributions (NIC)
        // Employee: 8% between PT (£1,048/mo) and UEL (£4,189/mo); 2% above UEL
        decimal nicEe = 0.00m;
        var pt = 1048.00m;
        var uel = 4189.00m;

        if (grossPay > pt)
        {
            var mainBand = Math.Min(grossPay, uel) - pt;
            nicEe += Math.Round(mainBand * 0.08m, 2);
            if (grossPay > uel)
            {
                nicEe += Math.Round((grossPay - uel) * 0.02m, 2);
            }
        }

        // Employer NIC: 13.8% above Secondary Threshold (£758/mo)
        decimal nicEr = 0.00m;
        var st = 758.00m;
        if (grossPay > st)
        {
            nicEr = Math.Round((grossPay - st) * 0.138m, 2);
        }

        var totalEeDeductions = monthlyTax + nicEe;
        var netPay = Math.Max(0.00m, grossPay - totalEeDeductions);

        var minWage = GetStatutoryMinimumWage(context.WorkJurisdiction, context.SkillCategory, context.EffectiveDate);
        var isCompliant = grossPay >= minWage;

        var payLines = new List<PayrollPayLine>
        {
            new("earning", "base_salary", "Base Salary", context.BaseSalary, "GBP"),
            new("deduction", "paye_income_tax", "PAYE Income Tax (1257L)", monthlyTax, "GBP"),
            new("deduction", "nic_employee", "Employee National Insurance (Class 1)", nicEe, "GBP"),
            new("employer_contribution", "nic_employer", "Employer National Insurance (13.8%)", nicEr, "GBP")
        };

        return new PayrollCalculationResult(
            EmployeeId: context.EmployeeId,
            CountryCode: "GB",
            WorkJurisdiction: context.WorkJurisdiction ?? "UK",
            GrossPay: grossPay,
            IncomeTaxWithholding: monthlyTax,
            EmployeeSocialSecurity: nicEe,
            EmployerSocialSecurity: nicEr,
            EmployeePension: 0.00m,
            EmployerPension: 0.00m,
            OtherDeductions: 0.00m,
            TotalEmployeeDeductions: totalEeDeductions,
            NetPay: netPay,
            CurrencyCode: "GBP",
            StatutoryAuthority: "HM Revenue and Customs (HMRC)",
            StatutoryCitation: "Income Tax (Earnings and Pensions) Act 2003 & Social Security Contributions and Benefits Act 1992",
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
