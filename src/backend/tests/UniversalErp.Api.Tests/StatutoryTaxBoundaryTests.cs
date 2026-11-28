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
    public void JurisdictionalMinimumWage_CurrentTY2027_ResolvesAccurateStatutoryBenchmarks()
    {
        // Federal civil employee minimum gross: PKR 40,700 (Finance Division OM)
        Assert.Equal(40_700m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("FEDERAL"));

        // ICT industrial/commercial unskilled: PKR 40,700 (Notification ADLW 8(20)-ICT/2024-934)
        Assert.Equal(40_700m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("ICT"));

        // Punjab: PKR 40,000 from 2025-07-01 (Retained legal rate under PMWB Gazette)
        Assert.Equal(40_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("PUNJAB"));

        // Sindh Skill Categories (Sindh Minimum Wages Board Gazette 2026)
        Assert.Equal(43_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("SINDH", "unskilled"));
        Assert.Equal(44_484m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("SINDH", "semi_skilled"));
        Assert.Equal(53_350m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("SINDH", "skilled"));
        Assert.Equal(55_626m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("SINDH", "highly_skilled"));

        // KP: PKR 40,000 from 2025-07-01 (Retained legal rate under KPMWB Notification)
        Assert.Equal(40_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("KPK"));
        Assert.Equal(40_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("KP"));

        // Balochistan: PKR 37,000 (Retained benchmark)
        Assert.Equal(37_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("BALOCHISTAN"));
    }

    [Fact]
    public void JurisdictionalMinimumWage_HistoricalIntervals_ResolvesPriorGazettedRates()
    {
        var date2024 = new DateOnly(2024, 7, 1);
        var date2025 = new DateOnly(2025, 7, 1);

        // 2024-07-01 to 2025-06-30 Interval:
        // Federal/ICT PKR 37,000; Punjab PKR 37,000; Sindh PKR 37,000; KP PKR 36,000; Balochistan PKR 37,000
        Assert.Equal(37_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("FEDERAL", "unskilled", date2024));
        Assert.Equal(37_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("ICT", "unskilled", date2024));
        Assert.Equal(37_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("PUNJAB", "unskilled", date2024));
        Assert.Equal(37_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("SINDH", "unskilled", date2024));
        Assert.Equal(36_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("KPK", "unskilled", date2024));
        Assert.Equal(37_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("BALOCHISTAN", "unskilled", date2024));

        // 2025-07-01 to 2026-06-30 Interval:
        // Punjab PKR 40,000; Sindh PKR 40,000; KP PKR 40,000; Federal/ICT PKR 37,000; Balochistan PKR 37,000
        Assert.Equal(40_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("PUNJAB", "unskilled", date2025));
        Assert.Equal(40_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("SINDH", "unskilled", date2025));
        Assert.Equal(40_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("KPK", "unskilled", date2025));
        Assert.Equal(37_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("FEDERAL", "unskilled", date2025));
        Assert.Equal(37_000m, PayrollCalculationEngine.GetJurisdictionalMinimumWage("BALOCHISTAN", "unskilled", date2025));
    }

    [Fact]
    public void ProvincialSocialSecurity_DecoupledCeilings_SixPercentEmployerOnly()
    {
        // Punjab (PESSI) ceiling 40,000 -> 40,000 * 6% = 2,400 ER, 0 EE
        decimal pessiCeiling = PayrollCalculationEngine.GetProvincialSocialSecurityCeiling("PUNJAB");
        Assert.Equal(40_000m, pessiCeiling);
        var (pEe, pEr) = PayrollCalculationEngine.CalculateSocialSecurity(true, 60_000m, pessiCeiling);
        Assert.Equal(0m, pEe);
        Assert.Equal(2400m, pEr);

        // Sindh (SESSI) ceiling 43,000 -> 43,000 * 6% = 2,580 ER, 0 EE
        decimal sessiCeiling = PayrollCalculationEngine.GetProvincialSocialSecurityCeiling("SINDH");
        Assert.Equal(43_000m, sessiCeiling);
        var (sEe, sEr) = PayrollCalculationEngine.CalculateSocialSecurity(true, 60_000m, sessiCeiling);
        Assert.Equal(0m, sEe);
        Assert.Equal(2580m, sEr);

        // KP ESSI ceiling 40,000 -> 40,000 * 6% = 2,400 ER, 0 EE
        decimal kpCeiling = PayrollCalculationEngine.GetProvincialSocialSecurityCeiling("KPK");
        Assert.Equal(40_000m, kpCeiling);
        var (kpEe, kpEr) = PayrollCalculationEngine.CalculateSocialSecurity(true, 60_000m, kpCeiling);
        Assert.Equal(0m, kpEe);
        Assert.Equal(2400m, kpEr);

        // Balochistan (BESSI) ceiling 37,000 -> 37,000 * 6% = 2,220 ER, 0 EE
        decimal bessiCeiling = PayrollCalculationEngine.GetProvincialSocialSecurityCeiling("BALOCHISTAN");
        Assert.Equal(37_000m, bessiCeiling);
        var (bEe, bEr) = PayrollCalculationEngine.CalculateSocialSecurity(true, 60_000m, bessiCeiling);
        Assert.Equal(0m, bEe);
        Assert.Equal(2220m, bEr);

        // Federal / ICT ceiling 40,700 -> 40,700 * 6% = 2,442 ER, 0 EE
        decimal fedCeiling = PayrollCalculationEngine.GetProvincialSocialSecurityCeiling("FEDERAL");
        Assert.Equal(40_700m, fedCeiling);
        var (fedEe, fedEr) = PayrollCalculationEngine.CalculateSocialSecurity(true, 60_000m, fedCeiling);
        Assert.Equal(0m, fedEe);
        Assert.Equal(2442m, fedEr);
    }

    [Fact]
    public void ProvincialSocialSecurity_SixPercentEmployerOnlyUpToWageCeiling()
    {
        // Gross 50,000 with 40,000 ceiling -> 40,000 * 6% = 2,400 ER, 0 EE
        var (ee, er) = PayrollCalculationEngine.CalculateSocialSecurity(true, 50_000m, 40_000m);
        Assert.Equal(0m, ee);
        Assert.Equal(2400m, er);

        // KPK gross 50,000 with 40,000 ceiling -> 40,000 * 6% = 2,400 ER, 0 EE
        var (kpEe, kpEr) = PayrollCalculationEngine.CalculateSocialSecurity(true, 50_000m, 40_000m);
        Assert.Equal(0m, kpEe);
        Assert.Equal(2400m, kpEr);

        // Disabled social security
        var (disEe, disEr) = PayrollCalculationEngine.CalculateSocialSecurity(false, 50_000m, 40_000m);
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
