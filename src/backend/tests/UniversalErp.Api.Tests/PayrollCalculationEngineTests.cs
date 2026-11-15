using System;
using System.Collections.Generic;
using UniversalErp.Application.HrPayroll;
using Xunit;

namespace UniversalErp.Api.Tests;

public class PayrollCalculationEngineTests
{
    [Fact]
    public void CalculateMonthlyIncomeTax_SalariedIndividual_FollowsPakistaniSlabsAccurately()
    {
        // Slab 1: Up to 600,000 annual (50,000/month) -> 0%
        decimal taxAt50k = PayrollCalculationEngine.CalculateMonthlyIncomeTax(50_000m);
        Assert.Equal(0m, taxAt50k);

        // Slab 2: 80,000/month -> 960,000 annual -> (960,000 - 600,000) * 1% = 3,600 annual -> 300/month
        decimal taxAt80k = PayrollCalculationEngine.CalculateMonthlyIncomeTax(80_000m);
        Assert.Equal(300m, taxAt80k);

        // Slab 3: 150,000/month -> 1,800,000 annual -> 6,000 + (1,800,000 - 1,200,000) * 11% = 6,000 + 66,000 = 72,000 annual -> 6,000/month
        decimal taxAt150k = PayrollCalculationEngine.CalculateMonthlyIncomeTax(150_000m);
        Assert.Equal(6000m, taxAt150k);

        // Slab 4: 220,000/month -> 2,640,000 annual -> 116,000 + (2,640,000 - 2,200,000) * 20% = 116,000 + 88,000 = 204,000 annual -> 17,000/month
        decimal taxAt220k = PayrollCalculationEngine.CalculateMonthlyIncomeTax(220_000m);
        Assert.Equal(17000m, taxAt220k);

        // Slab 5: 300,000/month -> 3,600,000 annual -> 316,000 + (3,600,000 - 3,200,000) * 25% = 316,000 + 100,000 = 416,000 annual -> 34,666.67/month
        decimal taxAt300k = PayrollCalculationEngine.CalculateMonthlyIncomeTax(300_000m);
        Assert.Equal(34666.67m, taxAt300k);

        // Slab 6: 400,000/month -> 4,800,000 annual -> 541,000 + (4,800,000 - 4,100,000) * 29% = 541,000 + 203,000 = 744,000 annual -> 62,000/month
        decimal taxAt400k = PayrollCalculationEngine.CalculateMonthlyIncomeTax(400_000m);
        Assert.Equal(62000m, taxAt400k);
    }

    [Fact]
    public void CalculateEobi_ComputesEmployee1PercentAndEmployer5Percent()
    {
        // Minimum wage 37,000 PKR
        var (ee, er) = PayrollCalculationEngine.CalculateEobi(isEobiApplicable: true, statutoryMinWage: 37_000m);
        Assert.Equal(370m, ee);   // 1%
        Assert.Equal(1850m, er);  // 5%

        // When EOBI is not applicable (e.g. director or exempt employee)
        var (eeExempt, erExempt) = PayrollCalculationEngine.CalculateEobi(isEobiApplicable: false);
        Assert.Equal(0m, eeExempt);
        Assert.Equal(0m, erExempt);
    }

    [Fact]
    public void CalculateProvidentFund_ComputesEmployeeDeductionAndEmployerMatch()
    {
        decimal baseSalary = 100_000m;
        var (eePf, erPf) = PayrollCalculationEngine.CalculateProvidentFund(isPfApplicable: true, baseSalary, pfPercentage: 8.33m);

        Assert.Equal(8330m, eePf);
        Assert.Equal(8330m, erPf);

        // When PF is not applicable
        var (eeOff, erOff) = PayrollCalculationEngine.CalculateProvidentFund(isPfApplicable: false, baseSalary);
        Assert.Equal(0m, eeOff);
        Assert.Equal(0m, erOff);
    }

    [Fact]
    public void CalculateOvertimePay_AppliesStatutoryDoubleRate()
    {
        // Base salary 104,000 PKR, 208 hours divisor -> 500 PKR/hr -> 2.0x = 1,000 PKR/hr
        // 10 hours overtime -> 10,000 PKR
        decimal otPay = PayrollCalculationEngine.CalculateOvertimePay(
            baseSalary: 104_000m,
            overtimeHours: 10m,
            hourlyMultiplier: 2.0m,
            standardMonthlyHours: 208m
        );

        Assert.Equal(10000m, otPay);
    }

    [Fact]
    public void CalculateEmployeePayroll_ComputesFullNetPayAndItemizedLines()
    {
        var comp = new CompensationStructureDto(
            Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow),
            BaseSalaryPkr: 100_000m,
            HouseRentAllowancePkr: 40_000m,
            UtilityAllowancePkr: 10_000m,
            ConveyanceAllowancePkr: 0,
            MedicalAllowancePkr: 0,
            SpecialAllowancePkr: 0,
            GrossSalaryPkr: 150_000m,
            PaymentFrequency: "monthly",
            CurrencyCode: "PKR",
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var employee = new EmployeeDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "EMP-001",
            "Ahmed", "Khan", "Ahmed Khan", "ahmed@example.com", "03001234567",
            "42101-1234567-1", null, "male", new DateOnly(2025, 1, 1), null,
            "active", "full_time", null, null, null, null, null, null, null,
            null, null, null, null, null,
            IsEobiApplicable: true,
            IsPfApplicable: true,
            CurrentCompensation: comp,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var result = PayrollCalculationEngine.CalculateEmployeePayroll(
            employee,
            workingDays: 30,
            attendedDays: 30,
            leaveDays: 0,
            absentDays: 0,
            overtimeHours: 0,
            bonusAmount: 0,
            loanInstallment: 5_000m,
            otherDeductions: 0,
            statutoryMinWage: 37_000m,
            pfRatePercentage: 8.33m
        );

        // Gross = 100,000 + 40,000 + 10,000 = 150,000
        Assert.Equal(150_000m, result.GrossPay);

        // Tax on 150k/mo = 6,000 under Finance Act 2026 / TY2027 (6,000 + 11% of 600k = 72k/yr -> 6k/mo)
        Assert.Equal(6000m, result.IncomeTax);

        // EOBI = 370
        Assert.Equal(370m, result.EobiEmployee);

        // PF on 100,000 base at 8.33% = 8,330
        Assert.Equal(8330m, result.PfEmployee);

        // Loan deduction = 5,000
        Assert.Equal(5000m, result.LoanDeduction);

        // Total deductions = 6,000 + 370 + 8,330 + 5,000 = 19,700
        Assert.Equal(19700m, result.TotalDeductions);

        // Net Pay = 150,000 - 19,700 = 130,300
        Assert.Equal(130300m, result.NetPay);

        // Employer contributions: EOBI 1,850 + PF 8,330
        Assert.Equal(1850m, result.EobiEmployer);
        Assert.Equal(8330m, result.PfEmployer);

        Assert.NotEmpty(result.Lines);
    }
}
