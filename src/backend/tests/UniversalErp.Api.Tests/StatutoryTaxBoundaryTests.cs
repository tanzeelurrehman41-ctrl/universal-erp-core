using System;
using System.Collections.Generic;
using UniversalErp.Application.HrPayroll;
using Xunit;

namespace UniversalErp.Api.Tests;

public class StatutoryTaxBoundaryTests
{
    [Fact]
    public void Slab1_ZeroTo600k_TaxIsZero()
    {
        // Monthly 0
        Assert.Equal(0m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(0m));
        // Boundary: exactly 600,000 / 12 = 50,000/mo
        Assert.Equal(0m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(50_000m));
        // 40,000/mo = 480,000/yr -> 0 tax
        Assert.Equal(0m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(40_000m));
    }

    [Fact]
    public void Slab2_600kTo1200k_FivePercentOnExcess()
    {
        // 600,001 annual -> 1 PKR excess * 5% = 0.05 / 12 ~ 0.00
        Assert.Equal(0m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(600_001m / 12m));

        // 900,000 annual (75,000/mo) -> 300,000 excess * 5% = 15,000 / 12 = 1,250/mo
        Assert.Equal(1250m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(75_000m));

        // Boundary: exactly 1,200,000 annual (100,000/mo) -> 600,000 excess * 5% = 30,000 / 12 = 2,500/mo
        Assert.Equal(2500m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(100_000m));
    }

    [Fact]
    public void Slab3_1200kTo2200k_Fixed30kPlusFifteenPercentOnExcess()
    {
        // 1,200,001 annual -> 30,000 + 0.15 = 30,000.15 / 12 = 2,500.01/mo
        Assert.Equal(2500.01m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(1_200_001m / 12m));

        // 1,800,000 annual (150,000/mo) -> 30,000 + (600,000 * 0.15 = 90,000) = 120,000 / 12 = 10,000/mo
        Assert.Equal(10_000m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(150_000m));

        // Boundary: exactly 2,200,000 annual -> 30,000 + (1,000,000 * 0.15 = 150,000) = 180,000 / 12 = 15,000/mo
        Assert.Equal(15_000m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(2_200_000m / 12m));
    }

    [Fact]
    public void Slab4_2200kTo3200k_Fixed180kPlusTwentyFivePercentOnExcess()
    {
        // 2,200,001 annual -> 180,000 + 0.25 = 180,000.25 / 12 = 15,000.02/mo
        Assert.Equal(15000.02m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(2_200_001m / 12m));

        // 2,800,000 annual -> 180,000 + (600,000 * 0.25 = 150,000) = 330,000 / 12 = 27,500/mo
        Assert.Equal(27_500m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(2_800_000m / 12m));

        // Boundary: exactly 3,200,000 annual -> 180,000 + (1,000,000 * 0.25 = 250,000) = 430,000 / 12 = 35,833.33/mo
        Assert.Equal(35833.33m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(3_200_000m / 12m));
    }

    [Fact]
    public void Slab5_3200kTo4100k_Fixed430kPlusThirtyPercentOnExcess()
    {
        // 3,200,001 annual -> 430,000 + 0.30 / 12 = 35,833.36/mo
        Assert.Equal(35833.36m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(3_200_001m / 12m));

        // Boundary: exactly 4,100,000 annual -> 430,000 + (900,000 * 0.30 = 270,000) = 700,000 / 12 = 58,333.33/mo
        Assert.Equal(58333.33m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(4_100_000m / 12m));
    }

    [Fact]
    public void Slab6_Exceeding4100k_Fixed700kPlusThirtyFivePercentOnExcess()
    {
        // 5,100,000 annual -> 700,000 + (1,000,000 * 0.35 = 350,000) = 1,050,000 / 12 = 87,500/mo
        Assert.Equal(87_500m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(5_100_000m / 12m));
    }

    [Fact]
    public void Surcharge_Exceeding10MillionAnnual_AppliesTenPercentSurcharge()
    {
        // 10,000,000 annual (no surcharge threshold met)
        // Tax = 700k + (5,900,000 * 0.35 = 2,065,000) = 2,765,000 / 12 = 230,416.67/mo
        decimal taxAt10M = PayrollCalculationEngine.CalculateMonthlyIncomeTax(10_000_000m / 12m);
        Assert.Equal(230416.67m, taxAt10M);

        // 12,000,000 annual (1,000,000/mo) -> Exceeds 10M, 10% surcharge applies
        // Base Tax = 700k + (7,900,000 * 0.35 = 2,765,000) = 3,465,000
        // Surcharge 10% = 346,500
        // Total Tax = 3,811,500 / 12 = 317,625/mo
        decimal taxAt12M = PayrollCalculationEngine.CalculateMonthlyIncomeTax(1_000_000m);
        Assert.Equal(317625m, taxAt12M);
    }

    [Fact]
    public void Eobi_StatutoryCalculations_OnePercentEmployeeFivePercentEmployer()
    {
        // Standard minimum wage 37,000
        var (ee, er) = PayrollCalculationEngine.CalculateEobi(true, 37_000m);
        Assert.Equal(370m, ee);
        Assert.Equal(1850m, er);

        // Not applicable
        var (noneEe, noneEr) = PayrollCalculationEngine.CalculateEobi(false, 37_000m);
        Assert.Equal(0m, noneEe);
        Assert.Equal(0m, noneEr);
    }

    [Fact]
    public void ProvidentFund_MatchingContributions_AccurateRounding()
    {
        // Base 120,000 at 8.33% = 9,996
        var (ee, er) = PayrollCalculationEngine.CalculateProvidentFund(true, 120_000m, 8.33m);
        Assert.Equal(9996m, ee);
        Assert.Equal(9996m, er);

        // Inactive
        var (offEe, offEr) = PayrollCalculationEngine.CalculateProvidentFund(false, 120_000m);
        Assert.Equal(0m, offEe);
        Assert.Equal(0m, offEr);
    }

    [Fact]
    public void OvertimePay_DoubleStandardRate_Divisor208Hours()
    {
        // Base 104,000 / 208 = 500/hr. 2.0x = 1,000/hr. 8 hrs = 8,000
        decimal ot = PayrollCalculationEngine.CalculateOvertimePay(104_000m, 8m, 2.0m, 208m);
        Assert.Equal(8000m, ot);

        // Zero hours
        Assert.Equal(0m, PayrollCalculationEngine.CalculateOvertimePay(104_000m, 0m));
    }
}
