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

        // Standard Pakistan Income Tax Ordinance 2001 (Salaried Individuals) default fallback
        // Slab 1: Up to 600,000 -> 0%
        if (annualIncome <= 600_000m)
        {
            return 0m;
        }
        // Slab 2: 600,001 to 1,200,000 -> 5% of amount exceeding 600,000
        if (annualIncome <= 1_200_000m)
        {
            decimal annualTax = (annualIncome - 600_000m) * 0.05m;
            return Math.Round(annualTax / 12m, 2);
        }
        // Slab 3: 1,200,001 to 2,200,000 -> 30,000 + 15% of amount exceeding 1,200,000
        if (annualIncome <= 2_200_000m)
        {
            decimal annualTax = 30_000m + ((annualIncome - 1_200_000m) * 0.15m);
            return Math.Round(annualTax / 12m, 2);
        }
        // Slab 4: 2,200,001 to 3,200,000 -> 180,000 + 25% of amount exceeding 2,200,000
        if (annualIncome <= 3_200_000m)
        {
            decimal annualTax = 180_000m + ((annualIncome - 2_200_000m) * 0.25m);
            return Math.Round(annualTax / 12m, 2);
        }
        // Slab 5: 3,200,001 to 4,100,000 -> 430,000 + 30% of amount exceeding 3,200,000
        if (annualIncome <= 4_100_000m)
        {
            decimal annualTax = 430_000m + ((annualIncome - 3_200_000m) * 0.30m);
            return Math.Round(annualTax / 12m, 2);
        }
        // Slab 6: Exceeding 4,100,000 -> 700,000 + 35% of amount exceeding 4,100,000
        {
            decimal annualTax = 700_000m + ((annualIncome - 4_100_000m) * 0.35m);
            return Math.Round(annualTax / 12m, 2);
        }
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
        decimal statutoryMinWage = 37_000m,
        decimal pfRatePercentage = 8.33m,
        decimal overtimeMultiplier = 2.0m)
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

        // 2. EOBI
        var (eobiEmp, eobiEmplr) = CalculateEobi(employee.IsEobiApplicable, statutoryMinWage);
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
