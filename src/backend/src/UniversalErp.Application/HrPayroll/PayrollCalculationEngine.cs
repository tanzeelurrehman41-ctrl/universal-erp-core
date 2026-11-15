using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalErp.Application.HrPayroll;

public record ComputedPayrollItem(
    Guid EmployeeId,
    decimal BaseSalary,
    decimal Allowances,
    decimal OvertimeHours,
    decimal OvertimePay,
    decimal Bonus,
    decimal GrossPay,
    decimal IncomeTax,
    decimal EobiEmployee,
    decimal PfEmployee,
    decimal LoanDeduction,
    decimal OtherDeductions,
    decimal TotalDeductions,
    decimal NetPay,
    decimal EobiEmployer,
    decimal PfEmployer,
    List<ComputedPayLine> Lines
);

public record ComputedPayLine(
    string LineType, // "earning", "deduction", "employer_contribution"
    string Category,
    string Description,
    decimal Amount
);

public class PayrollCalculationEngine
{
    /// <summary>
    /// Computes monthly income tax withholding according to Pakistan Income Tax Ordinance 2001 (Salaried Individual Slabs).
    /// Annualizes monthly taxable income, locates the bracket, calculates annual tax, and divides by 12.
    /// </summary>
    /// <summary>
    /// Computes monthly income tax withholding according to Pakistan Income Tax Ordinance 2001 (Salaried Individual Slabs).
    /// Official FBR Tax Year 2027 Withholding Rate Card under Finance Act 2026.
    /// Annualizes monthly taxable income, locates the bracket, calculates annual tax, and divides by 12.
    /// Schedule (First Schedule, Part I, Division I - Salaried):
    /// 1. Up to PKR 600,000: 0%
    /// 2. PKR 600,001 - 1,200,000: 1% of amount exceeding PKR 600,000
    /// 3. PKR 1,200,001 - 2,200,000: PKR 6,000 + 11% of amount exceeding PKR 1,200,000
    /// 4. PKR 2,200,001 - 3,200,000: PKR 116,000 + 20% of amount exceeding PKR 2,200,000
    /// 5. PKR 3,200,001 - 4,100,000: PKR 316,000 + 25% of amount exceeding PKR 3,200,000
    /// 6. PKR 4,100,001 - 5,600,000: PKR 541,000 + 29% of amount exceeding PKR 4,100,000
    /// 7. PKR 5,600,001 - 7,000,000: PKR 976,000 + 32% of amount exceeding PKR 5,600,000
    /// 8. Exceeding PKR 7,000,000: PKR 1,424,000 + 35% of amount exceeding PKR 7,000,000
    /// Note: Obsolete 10% salary surcharge above PKR 10 million is removed per Finance Act 2026 rules.
    /// </summary>
    public static decimal CalculateMonthlyIncomeTax(decimal monthlyTaxableIncome, IReadOnlyList<TaxSlabDto>? customSlabs = null)
    {
        if (monthlyTaxableIncome <= 0) return 0m;

        decimal annualIncome = monthlyTaxableIncome * 12m;

        // If custom configurable slabs are supplied, evaluate against them
        if (customSlabs != null && customSlabs.Count > 0)
        {
            var ordered = customSlabs.OrderBy(s => s.SlabOrder).ToList();
            foreach (var slab in ordered)
            {
                if (annualIncome >= slab.MinAnnualIncomePkr && (slab.MaxAnnualIncomePkr == null || annualIncome <= slab.MaxAnnualIncomePkr.Value))
                {
                    decimal excess = annualIncome - slab.MinAnnualIncomePkr;
                    decimal annualTax = slab.FixedTaxAmountPkr + (excess * (slab.MarginalTaxRatePercentage / 100m));
                    return Math.Round(Math.Max(0, annualTax / 12m), 2);
                }
            }
        }

        // Standard Pakistan Income Tax Ordinance 2001 (Salaried Individuals) TY2027 default fallback
        // Slab 1: Up to 600,000 -> 0%
        if (annualIncome <= 600_000m)
        {
            return 0m;
        }
        // Slab 2: 600,001 to 1,200,000 -> 1% of amount exceeding 600,000
        if (annualIncome <= 1_200_000m)
        {
            decimal annualTax = (annualIncome - 600_000m) * 0.01m;
            return Math.Round(annualTax / 12m, 2);
        }
        // Slab 3: 1,200,001 to 2,200,000 -> 6,000 + 11% of amount exceeding 1,200,000
        if (annualIncome <= 2_200_000m)
        {
            decimal annualTax = 6_000m + ((annualIncome - 1_200_000m) * 0.11m);
            return Math.Round(annualTax / 12m, 2);
        }
        // Slab 4: 2,200,001 to 3,200,000 -> 116,000 + 20% of amount exceeding 2,200,000
        if (annualIncome <= 3_200_000m)
        {
            decimal annualTax = 116_000m + ((annualIncome - 2_200_000m) * 0.20m);
            return Math.Round(annualTax / 12m, 2);
        }
        // Slab 5: 3,200,001 to 4,100,000 -> 316,000 + 25% of amount exceeding 3,200,000
        if (annualIncome <= 4_100_000m)
        {
            decimal annualTax = 316_000m + ((annualIncome - 3_200_000m) * 0.25m);
            return Math.Round(annualTax / 12m, 2);
        }
        // Slab 6: 4,100,001 to 5,600,000 -> 541,000 + 29% of amount exceeding 4,100,000
        if (annualIncome <= 5_600_000m)
        {
            decimal annualTax = 541_000m + ((annualIncome - 4_100_000m) * 0.29m);
            return Math.Round(annualTax / 12m, 2);
        }
        // Slab 7: 5,600,001 to 7,000,000 -> 976,000 + 32% of amount exceeding 5,600,000
        if (annualIncome <= 7_000_000m)
        {
            decimal annualTax = 976_000m + ((annualIncome - 5_600_000m) * 0.32m);
            return Math.Round(annualTax / 12m, 2);
        }
        // Slab 8: Exceeding 7,000,000 -> 1,424,000 + 35% of amount exceeding 7,000,000
        {
            decimal annualTax = 1_424_000m + ((annualIncome - 7_000_000m) * 0.35m);
            return Math.Round(annualTax / 12m, 2);
        }
    }

    /// <summary>
    /// Resolves official statutory minimum wage for a specific Pakistani jurisdiction and skill category.
    /// Supports current TY2027 rates and historical intervals:
    /// - 2024-07-01: Federal/ICT 37,000; Punjab 37,000; Sindh 37,000; KP 36,000; Balochistan 37,000
    /// - 2025-07-01: Punjab 40,000; Sindh 40,000; KP 40,000; Federal/ICT 37,000; Balochistan 37,000
    /// - 2026-07-01: ICT/Federal 40,700; Sindh 43,000 (unskilled) / 44,484 / 53,350 / 55,626; Punjab 40,000; KP 40,000; Balochistan 37,000
    /// </summary>
    public static decimal GetJurisdictionalMinimumWage(string jurisdiction = "FEDERAL", string skillCategory = "unskilled", DateOnly? effectiveDate = null)
    {
        var jur = (jurisdiction ?? "FEDERAL").Trim().ToUpperInvariant();
        var skill = (skillCategory ?? "unskilled").Trim().ToLowerInvariant();
        var date = effectiveDate ?? new DateOnly(2026, 7, 1);

        // Historical Period 1: 2024-07-01 to 2025-06-30
        if (date < new DateOnly(2025, 7, 1))
        {
            return jur switch
            {
                "KPK" or "KP" => 36_000m,
                _ => 37_000m // Federal, ICT, Punjab, Sindh, Balochistan
            };
        }

        // Historical Period 2: 2025-07-01 to 2026-06-30
        if (date < new DateOnly(2026, 7, 1))
        {
            return jur switch
            {
                "PUNJAB" => 40_000m,
                "SINDH" => 40_000m,
                "KPK" or "KP" => 40_000m,
                _ => 37_000m // Federal, ICT, Balochistan
            };
        }

        // Current TY2027 Period: 2026-07-01 onwards
        if (jur == "SINDH")
        {
            return skill switch
            {
                "semi_skilled" or "semiskilled" => 44_484m,
                "skilled" => 53_350m,
                "highly_skilled" or "highlyskilled" => 55_626m,
                _ => 43_000m // Unskilled default
            };
        }

        return jur switch
        {
            "FEDERAL" => 40_700m,
            "ICT" => 40_700m,
            "PUNJAB" => 40_000m,
            "KPK" or "KP" => 40_000m,
            "BALOCHISTAN" => 37_000m,
            _ => 40_700m
        };
    }

    /// <summary>
    /// Resolves official Provincial Social Security wage ceilings:
    /// - PESSI (Punjab): PKR 40,000
    /// - SESSI (Sindh): PKR 43,000
    /// - KP ESSI (KP): PKR 40,000
    /// - BESSI (Balochistan): PKR 37,000
    /// - Federal / ICT: PKR 40,700
    /// </summary>
    public static decimal GetProvincialSocialSecurityCeiling(string jurisdiction = "FEDERAL")
    {
        var norm = (jurisdiction ?? "FEDERAL").Trim().ToUpperInvariant();
        return norm switch
        {
            "PUNJAB" => 40_000m,
            "SINDH" => 43_000m,
            "KPK" or "KP" => 40_000m,
            "BALOCHISTAN" => 37_000m,
            "ICT" => 40_700m,
            "FEDERAL" => 40_700m,
            _ => 40_700m
        };
    }

    /// <summary>
    /// Computes Provincial Social Security (PESSI / SESSI / KP ESSI / BESSI) contributions.
    /// Under provincial labour laws: Employer pays 6% of wages up to the provincial statutory ceiling; Employee pays 0%.
    /// </summary>
    public static (decimal EmployeeShare, decimal EmployerShare) CalculateSocialSecurity(
        bool isApplicable,
        decimal grossSalary,
        decimal wageCeiling = 40_700m)
    {
        if (!isApplicable || grossSalary <= 0 || wageCeiling <= 0) return (0m, 0m);

        decimal applicableWage = Math.Min(grossSalary, wageCeiling);
        decimal employer = Math.Round(applicableWage * 0.06m, 2);
        return (0m, employer);
    }

    /// <summary>
    /// Computes EOBI (Employees' Old-Age Benefits Institution) statutory contributions.
    /// Under Pakistani law: Employee pays 1% of statutory minimum wage, Employer pays 5%.
    /// </summary>
    public static (decimal EmployeeShare, decimal EmployerShare) CalculateEobi(
        bool isEobiApplicable,
        decimal statutoryMinWage = 37_000m,
        decimal? customEmployeeAmount = null,
        decimal? customEmployerAmount = null)
    {
        if (!isEobiApplicable) return (0m, 0m);

        decimal employee = customEmployeeAmount ?? Math.Round(statutoryMinWage * 0.01m, 2);
        decimal employer = customEmployerAmount ?? Math.Round(statutoryMinWage * 0.05m, 2);

        return (employee, employer);
    }

    /// <summary>
    /// Computes Provident Fund (PF) deductions and employer matching contribution.
    /// Default standard is 8.33% or 10% of base salary.
    /// </summary>
    public static (decimal EmployeePf, decimal EmployerPf) CalculateProvidentFund(
        bool isPfApplicable,
        decimal baseSalary,
        decimal pfPercentage = 8.33m)
    {
        if (!isPfApplicable || baseSalary <= 0 || pfPercentage <= 0) return (0m, 0m);

        decimal contribution = Math.Round(baseSalary * (pfPercentage / 100m), 2);
        return (contribution, contribution); // Employer matches employee contribution
    }

    /// <summary>
    /// Computes overtime pay based on hourly rate and statutory multiplier (default 2.0x standard hourly rate).
    /// Standard divisor is 208 hours (26 days * 8 hours).
    /// </summary>
    public static decimal CalculateOvertimePay(
        decimal baseSalary,
        decimal overtimeHours,
        decimal hourlyMultiplier = 2.0m,
        decimal standardMonthlyHours = 208m)
    {
        if (baseSalary <= 0 || overtimeHours <= 0) return 0m;

        decimal hourlyRate = baseSalary / (standardMonthlyHours > 0 ? standardMonthlyHours : 208m);
        decimal overtimePay = overtimeHours * hourlyRate * (hourlyMultiplier > 0 ? hourlyMultiplier : 2.0m);
        return Math.Round(overtimePay, 2);
    }

    /// <summary>
    /// Computes full payslip calculation for an employee for a given period.
    /// </summary>
    public static ComputedPayrollItem CalculateEmployeePayroll(
        EmployeeDto employee,
        decimal workingDays,
        decimal attendedDays,
        decimal leaveDays,
        decimal absentDays,
        decimal overtimeHours,
        decimal bonusAmount,
        decimal loanInstallment,
        decimal otherDeductions,
        IReadOnlyList<TaxSlabDto>? taxSlabs = null,
        decimal statutoryMinWage = 40_700m,
        decimal pfRatePercentage = 8.33m,
        decimal overtimeMultiplier = 2.0m,
        decimal eobiContributionBase = 37_000m)
    {
        var lines = new List<ComputedPayLine>();
        var comp = employee.CurrentCompensation;

        decimal baseSalary = comp?.BaseSalaryPkr ?? 0m;
        decimal houseRent = comp?.HouseRentAllowancePkr ?? 0m;
        decimal utility = comp?.UtilityAllowancePkr ?? 0m;
        decimal conveyance = comp?.ConveyanceAllowancePkr ?? 0m;
        decimal medical = comp?.MedicalAllowancePkr ?? 0m;
        decimal special = comp?.SpecialAllowancePkr ?? 0m;

        decimal totalAllowances = houseRent + utility + conveyance + medical + special;

        // Proration for unpaid absences if applicable
        decimal effectiveBaseSalary = baseSalary;
        if (workingDays > 0 && absentDays > 0)
        {
            decimal dailyRate = baseSalary / workingDays;
            decimal absentDeduction = Math.Round(dailyRate * absentDays, 2);
            effectiveBaseSalary = Math.Max(0, baseSalary - absentDeduction);
        }

        // Overtime Pay
        decimal overtimePay = CalculateOvertimePay(baseSalary, overtimeHours, overtimeMultiplier);

        // Gross Pay
        decimal grossPay = effectiveBaseSalary + totalAllowances + overtimePay + bonusAmount;

        // Line item breakdown for earnings
        lines.Add(new ComputedPayLine("earning", "base_salary", "Basic Salary", effectiveBaseSalary));
        if (houseRent > 0) lines.Add(new ComputedPayLine("earning", "house_rent", "House Rent Allowance", houseRent));
        if (utility > 0) lines.Add(new ComputedPayLine("earning", "utility", "Utility Allowance", utility));
        if (conveyance > 0) lines.Add(new ComputedPayLine("earning", "conveyance", "Conveyance Allowance", conveyance));
        if (medical > 0) lines.Add(new ComputedPayLine("earning", "medical", "Medical Allowance", medical));
        if (special > 0) lines.Add(new ComputedPayLine("earning", "special", "Special Allowance", special));
        if (overtimePay > 0) lines.Add(new ComputedPayLine("earning", "overtime", $"Overtime Pay ({overtimeHours} hrs)", overtimePay));
        if (bonusAmount > 0) lines.Add(new ComputedPayLine("earning", "bonus", "Performance Bonus", bonusAmount));

        // Deductions
        // 1. Income Tax (Calculated on gross earnings)
        decimal incomeTax = CalculateMonthlyIncomeTax(grossPay, taxSlabs);
        if (incomeTax > 0) lines.Add(new ComputedPayLine("deduction", "tax", "Income Tax (FBR Withholding)", incomeTax));

        // 2. EOBI (Decoupled from Provincial Minimum Wage)
        var (eobiEmp, eobiEmplr) = CalculateEobi(employee.IsEobiApplicable, eobiContributionBase);
        if (eobiEmp > 0) lines.Add(new ComputedPayLine("deduction", "eobi", "EOBI (Employee Share 1%)", eobiEmp));
        if (eobiEmplr > 0) lines.Add(new ComputedPayLine("employer_contribution", "eobi_employer", "EOBI (Employer Share 5%)", eobiEmplr));

        // 3. Provident Fund
        var (pfEmp, pfEmplr) = CalculateProvidentFund(employee.IsPfApplicable, baseSalary, pfRatePercentage);
        if (pfEmp > 0) lines.Add(new ComputedPayLine("deduction", "provident_fund", $"Provident Fund ({pfRatePercentage}%)", pfEmp));
        if (pfEmplr > 0) lines.Add(new ComputedPayLine("employer_contribution", "pf_employer", $"Provident Fund Matching ({pfRatePercentage}%)", pfEmplr));

        // 4. Loan Installment
        decimal safeLoanDeduction = Math.Max(0, loanInstallment);
        if (safeLoanDeduction > 0) lines.Add(new ComputedPayLine("deduction", "loan_installment", "Salary Loan / Advance Recovery", safeLoanDeduction));

        // 5. Other Deductions
        decimal safeOtherDeductions = Math.Max(0, otherDeductions);
        if (safeOtherDeductions > 0) lines.Add(new ComputedPayLine("deduction", "other_deductions", "Other Deductions", safeOtherDeductions));

        // Total Deductions & Net Pay
        decimal totalDeductions = incomeTax + eobiEmp + pfEmp + safeLoanDeduction + safeOtherDeductions;
        decimal netPay = Math.Max(0, grossPay - totalDeductions);

        return new ComputedPayrollItem(
            employee.Id,
            effectiveBaseSalary,
            totalAllowances,
            overtimeHours,
            overtimePay,
            bonusAmount,
            grossPay,
            incomeTax,
            eobiEmp,
            pfEmp,
            safeLoanDeduction,
            safeOtherDeductions,
            totalDeductions,
            netPay,
            eobiEmplr,
            pfEmplr,
            lines
        );
    }
}
