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
    public void Slab2_600kTo1200k_OnePercentOnExcess()
    {
        // 600,001 annual -> 1 PKR excess * 1% = 0.01 / 12 ~ 0.00
        Assert.Equal(0m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(600_001m / 12m));

        // 900,000 annual (75,000/mo) -> 300,000 excess * 1% = 3,000 / 12 = 250/mo
        Assert.Equal(250m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(75_000m));

        // Boundary: exactly 1,200,000 annual (100,000/mo) -> 600,000 excess * 1% = 6,000 / 12 = 500/mo
        Assert.Equal(500m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(100_000m));
    }

    [Fact]
    public void Slab3_1200kTo2200k_Fixed6kPlusElevenPercentOnExcess()
    {
        // 1,200,001 annual -> 6,000 + 0.11 / 12 = 500.01/mo
        Assert.Equal(500.01m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(1_200_001m / 12m));

        // 1,700,000 annual -> 6,000 + (500,000 * 0.11 = 55,000) = 61,000 / 12 = 5,083.33/mo
        Assert.Equal(5083.33m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(1_700_000m / 12m));

        // Boundary: exactly 2,200,000 annual -> 6,000 + (1,000,000 * 0.11 = 110,000) = 116,000 / 12 = 9,666.67/mo
        Assert.Equal(9666.67m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(2_200_000m / 12m));
    }

    [Fact]
    public void Slab4_2200kTo3200k_Fixed116kPlusTwentyPercentOnExcess()
    {
        // 2,700,000 annual -> 116,000 + (500,000 * 0.20 = 100,000) = 216,000 / 12 = 18,000/mo
        Assert.Equal(18000m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(2_700_000m / 12m));

        // Boundary: exactly 3,200,000 annual -> 116,000 + (1,000,000 * 0.20 = 200,000) = 316,000 / 12 = 26,333.33/mo
        Assert.Equal(26333.33m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(3_200_000m / 12m));
    }

    [Fact]
    public void Slab5_3200kTo4100k_Fixed316kPlusTwentyFivePercentOnExcess()
    {
        // 3,650,000 annual -> 316,000 + (450,000 * 0.25 = 112,500) = 428,500 / 12 = 35,708.33/mo
        Assert.Equal(35708.33m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(3_650_000m / 12m));

        // Boundary: exactly 4,100,000 annual -> 316,000 + (900,000 * 0.25 = 225,000) = 541,000 / 12 = 45,083.33/mo
        Assert.Equal(45083.33m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(4_100_000m / 12m));
    }

    [Fact]
    public void Slab6_4100kTo5600k_Fixed541kPlusTwentyNinePercentOnExcess()
    {
        // 4,850,000 annual -> 541,000 + (750,000 * 0.29 = 217,500) = 758,500 / 12 = 63,208.33/mo
        Assert.Equal(63208.33m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(4_850_000m / 12m));

        // Boundary: exactly 5,600,000 annual -> 541,000 + (1,500,000 * 0.29 = 435,000) = 976,000 / 12 = 81,333.33/mo
        Assert.Equal(81333.33m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(5_600_000m / 12m));
    }

    [Fact]
    public void Slab7_5600kTo7000k_Fixed976kPlusThirtyTwoPercentOnExcess()
    {
        // 6,300,000 annual -> 976,000 + (700,000 * 0.32 = 224,000) = 1,200,000 / 12 = 100,000/mo
        Assert.Equal(100000m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(6_300_000m / 12m));

        // Boundary: exactly 7,000,000 annual -> 976,000 + (1,400,000 * 0.32 = 448,000) = 1,424,000 / 12 = 118,666.67/mo
        Assert.Equal(118666.67m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(7_000_000m / 12m));
    }

    [Fact]
    public void Slab8_Exceeding7000k_Fixed1424kPlusThirtyFivePercentOnExcess_NoSurcharge()
    {
        // 8,000,000 annual -> 1,424,000 + (1,000,000 * 0.35 = 350,000) = 1,774,000 / 12 = 147,833.33/mo
        Assert.Equal(147833.33m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(8_000_000m / 12m));

        // 10,000,000 annual -> 1,424,000 + (3,000,000 * 0.35 = 1,050,000) = 2,474,000 / 12 = 206,166.67/mo
        Assert.Equal(206166.67m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(10_000_000m / 12m));

        // High earner: 12,000,000 annual (1,000,000/mo) -> 1,424,000 + (5,000,000 * 0.35 = 1,750,000) = 3,174,000 / 12 = 264,500/mo
        // Verified: NO obsolete 10% salary surcharge applied under Finance Act 2026 rules
        Assert.Equal(264500m, PayrollCalculationEngine.CalculateMonthlyIncomeTax(1_000_000m));
    }

    [Fact]
    public void ProvincialSocialSecurity_SixPercentEmployerOnlyUpToWageCeiling()
    {
        // Punjab gross 50,000 with 37,000 ceiling -> 37,000 * 6% = 2,220 ER, 0 EE
        var (ee, er) = PayrollCalculationEngine.CalculateSocialSecurity(true, 50_000m, 37_000m);
        Assert.Equal(0m, ee);
        Assert.Equal(2220m, er);

        // KPK gross 50,000 with 36,000 ceiling -> 36,000 * 6% = 2,160 ER, 0 EE
        var (kpEe, kpEr) = PayrollCalculationEngine.CalculateSocialSecurity(true, 50_000m, 36_000m);
        Assert.Equal(0m, kpEe);
        Assert.Equal(2160m, kpEr);

        // Disabled social security
        var (disEe, disEr) = PayrollCalculationEngine.CalculateSocialSecurity(false, 50_000m, 37_000m);
        Assert.Equal(0m, disEe);
        Assert.Equal(0m, disEr);
    }

    [Fact]
    public void Eobi_StatutoryCalculations_OnePercentEmployeeFivePercentEmployer()
    {
        // Standard minimum wage / contribution base 37,000
        var (ee, er) = PayrollCalculationEngine.CalculateEobi(true, 37_000m);
        Assert.Equal(370m, ee);
        Assert.Equal(1850m, er);

        // Historical period contribution base 32,000
        var (histEe, histEr) = PayrollCalculationEngine.CalculateEobi(true, 32_000m);
        Assert.Equal(320m, histEe);
        Assert.Equal(1600m, histEr);

        // Disabled EOBI
        var (disEe, disEr) = PayrollCalculationEngine.CalculateEobi(false, 37_000m);
        Assert.Equal(0m, disEe);
        Assert.Equal(0m, disEr);
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
