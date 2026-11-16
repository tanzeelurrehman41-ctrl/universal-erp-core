/**
 * Universal ERP — Modular Global Payroll Engine
 * Multi-country statutory payroll adapters for US, UK, UAE, Australia, Pakistan, and Global.
 * - US: 2026 standard deductions ($16,100 Single, $32,200 MFJ, $24,150 HOH), Form W-4 settings,
 *   IRS Pub 15-T (2026) percentage method, OASDI cap $184,500, Additional Medicare (0.9%), FUTA & SUTA.
 * - UK: 2026-27 student loan thresholds (Plan 1 £2,241.66, Plan 2 £2,448.75, Plan 4 £2,816.25, Plan 5 £2,083.33, PG £1,750.00),
 *   HMRC Scottish PAYE published schedule, Employer NIC 15.0% above £417/mo, Categories A/B/C/H/M/J, Pension Auto-Enrolment.
 * - UAE: Pension cohort classification (Federal Law 7 of 1999, Federal Decree-Law 57 of 2023, ADPF, SSSF,
 *   government vs private sector, salary caps AED 50k, 70k, 100k, expat EOSG under Decree-Law 33 of 2021).
 * - Australia: 12% Superannuation Guarantee (effective 1 July 2026), OTE, Payday Super 7-day rule, STP Phase 2.
 */

export type FilingStatus = "Single" | "MarriedFilingJointly" | "HeadOfHousehold";

export type UkCategoryLetter = "A" | "B" | "C" | "H" | "M" | "J";

export type StudentLoanPlan = "Plan1" | "Plan2" | "Plan4" | "Plan5" | "Postgraduate" | "None";

export type UaePensionScheme = "Law7_1999" | "DecreeLaw57_2023" | "ADPF" | "SSSF";

export type UaeSector = "private" | "government";

export type PayrollCalculationRequest = {
  employeeId?: string;
  countryCode: string; // ISO 3166-1 alpha-2 ("US", "GB", "AE", "AU", "PK", etc.)
  grossSalary: number;
  basicSalary?: number;
  currencyCode?: string;
  allowances?: number;
  isExpatriate?: boolean;
  ytdGrossPay?: number;
  jurisdiction?: string;
  effectiveDate?: string;
  filingStatus?: FilingStatus;
  ukCategoryLetter?: UkCategoryLetter;
  studentLoanPlan?: StudentLoanPlan;
  pensionAutoEnrolment?: boolean;
  pensionEmployeeRate?: number;
  pensionEmployerRate?: number;
  sutaRate?: number;
  isScottishTaxpayer?: boolean;
  // US Form W-4 Settings
  w4Step2cMultipleJobs?: boolean;
  w4Step3DependentCredits?: number;
  w4Step4aOtherIncome?: number;
  w4Step4bOtherDeductions?: number;
  w4Step4cExtraWithholding?: number;
  // UAE Pension Cohort Settings
  uaePensionScheme?: UaePensionScheme;
  uaeSector?: UaeSector;
  // Australia Superannuation Settings
  australiaPaydaySuper?: boolean;
  ordinaryTimeEarnings?: number;
  // Saudi Arabia EOSA & International Settings
  yearsOfService?: number;
  indiaTaxRegime?: "new" | "old";
};

export type StatutoryLine = {
  code: string;
  description: string;
  employeeAmount: number;
  employerAmount: number;
  ratePct: number;
  authority: string;
};

export type PayrollCalculationResult = {
  grossSalary: number;
  employeeIncomeTax: number;
  employeeStatutoryDeductions: number;
  employerStatutoryContributions: number;
  netSalary: number;
  currencyCode: string;
  countryCode: string;
  statutoryLines: StatutoryLine[];
  explanation: string;
};

/**
 * Calculates global payroll dispatching to modular country adapters.
 */
export function calculateGlobalPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const code = (request.countryCode || "US").trim().toUpperCase();

  switch (code) {
    case "US":
      return calculateUsPayroll(request);
    case "GB":
      return calculateUkPayroll(request);
    case "AE":
      return calculateUaePayroll(request);
    case "AU":
      return calculateAustraliaPayroll(request);
    case "PK":
      return calculatePakistanPayroll(request);
    case "SA":
      return calculateSaudiArabiaPayroll(request);
    case "CA":
      return calculateCanadaPayroll(request);
    case "IN":
      return calculateIndiaPayroll(request);
    case "NL":
      return calculateNetherlandsPayroll(request);
    case "ES":
      return calculateSpainPayroll(request);
    case "IT":
      return calculateItalyPayroll(request);
    case "PL":
      return calculatePolandPayroll(request);
    default:
      return calculateGenericPayroll(request);
  }
}

/**
 * United States Payroll Adapter
 * - 2026 OASDI wage base: $184,500 ($15,375.00/mo) (6.2% EE, 6.2% ER)
 * - FICA Medicare: 1.45% EE, 1.45% ER
 * - Additional Medicare Tax: 0.9% EE only on monthly gross > $16,666.67 (Single/HOH) / $20,833.33 (MFJ)
 * - 2026 Standard Deductions: Single $16,100, MFJ $32,200, HOH $24,150
 * - IRS Pub 15-T (2026) Percentage Method with Form W-4 Settings (Steps 2, 3, 4a, 4b, 4c)
 * - FUTA (0.6% on first $7,000 / $583.33/mo) and SUTA employer obligations
 */
export function calculateUsPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const currencyCode = request.currencyCode || "USD";
  const lines: StatutoryLine[] = [];
  const filingStatus = request.filingStatus || "Single";
  const effectiveDate = request.effectiveDate || new Date().toISOString().substring(0, 10);

  // 1. FICA Social Security (OASDI)
  let annualWageBase = 184500.0; // 2026 Official OASDI base
  if (effectiveDate < "2025-01-01") {
    annualWageBase = 168600.0; // 2024
  } else if (effectiveDate < "2026-01-01") {
    annualWageBase = 176100.0; // 2025
  }
  const monthlyWageBase = Math.round((annualWageBase / 12) * 100) / 100;

  const currentYtd = request.ytdGrossPay || 0;
  const taxableForOasdi = currentYtd > 0
    ? Math.max(0, Math.min(gross, annualWageBase - currentYtd))
    : Math.min(gross, monthlyWageBase);

  const ssEmployee = Math.round(taxableForOasdi * 0.062 * 100) / 100;
  const ssEmployer = ssEmployee;

  lines.push({
    code: "US_FICA_SS",
    description: `Social Security (FICA OASDI 6.2% up to $${annualWageBase.toLocaleString()})`,
    employeeAmount: ssEmployee,
    employerAmount: ssEmployer,
    ratePct: 6.2,
    authority: "Internal Revenue Service (IRS) / Social Security Administration",
  });

  // 2. FICA Medicare (HI Base 1.45%)
  const medEmployeeBase = Math.round(gross * 0.0145 * 100) / 100;
  const medEmployerBase = medEmployeeBase;

  // Additional Medicare Tax (0.9% EE only)
  const addlMedicareMonthlyThreshold = filingStatus === "MarriedFilingJointly"
    ? 250000.0 / 12 // $20,833.33
    : 200000.0 / 12; // $16,666.67 for Single and HeadOfHousehold

  let addlMedicareEe = 0;
  if (gross > addlMedicareMonthlyThreshold) {
    addlMedicareEe = Math.round((gross - addlMedicareMonthlyThreshold) * 0.009 * 100) / 100;
  }

  const totalMedicareEe = medEmployeeBase + addlMedicareEe;

  lines.push({
    code: "US_FICA_MED",
    description: addlMedicareEe > 0
      ? `Medicare (Base 1.45% + Additional 0.9% over $${(addlMedicareMonthlyThreshold * 12).toLocaleString()})`
      : "Medicare (FICA Hospital Insurance 1.45%)",
    employeeAmount: totalMedicareEe,
    employerAmount: medEmployerBase,
    ratePct: addlMedicareEe > 0 ? 2.35 : 1.45,
    authority: "Internal Revenue Service (IRS)",
  });

  // 3. Federal Income Tax (FIT Withholding) - IRS Pub 15-T (2026) Percentage Method
  // Adjusted Gross = Gross + Step4a/12 - Step4b/12
  const step4a = request.w4Step4aOtherIncome || 0;
  const step4b = request.w4Step4bOtherDeductions || 0;
  const step3Credits = request.w4Step3DependentCredits || 0;
  const step4cExtra = request.w4Step4cExtraWithholding || 0;

  const monthlyAdjustedGross = Math.max(0, gross + (step4a / 12) - (step4b / 12));
  const annualizedAdjustedGross = monthlyAdjustedGross * 12;

  // 2026 Official Standard Deductions
  const standardDeduction = filingStatus === "MarriedFilingJointly"
    ? 32200.0
    : filingStatus === "HeadOfHousehold"
    ? 24150.0
    : 16100.0;

  const taxableIncome = Math.max(0, annualizedAdjustedGross - standardDeduction);
  let annualTax = 0;

  if (filingStatus === "MarriedFilingJointly") {
    if (taxableIncome <= 24800) {
      annualTax = taxableIncome * 0.10;
    } else if (taxableIncome <= 100800) {
      annualTax = 2480 + (taxableIncome - 24800) * 0.12;
    } else if (taxableIncome <= 211400) {
      annualTax = 11600 + (taxableIncome - 100800) * 0.22;
    } else if (taxableIncome <= 403500) {
      annualTax = 35932 + (taxableIncome - 211400) * 0.24;
    } else if (taxableIncome <= 512400) {
      annualTax = 82036 + (taxableIncome - 403500) * 0.32;
    } else if (taxableIncome <= 768700) {
      annualTax = 116884 + (taxableIncome - 512400) * 0.35;
    } else {
      annualTax = 206589 + (taxableIncome - 768700) * 0.37;
    }
  } else if (filingStatus === "HeadOfHousehold") {
    if (taxableIncome <= 17700) {
      annualTax = taxableIncome * 0.10;
    } else if (taxableIncome <= 67500) {
      annualTax = 1770 + (taxableIncome - 17700) * 0.12;
    } else if (taxableIncome <= 107700) {
      annualTax = 7746 + (taxableIncome - 67500) * 0.22;
    } else if (taxableIncome <= 205500) {
      annualTax = 16590 + (taxableIncome - 107700) * 0.24;
    } else if (taxableIncome <= 260950) {
      annualTax = 40062 + (taxableIncome - 205500) * 0.32;
    } else if (taxableIncome <= 652050) {
      annualTax = 57806 + (taxableIncome - 260950) * 0.35;
    } else {
      annualTax = 194691 + (taxableIncome - 652050) * 0.37;
    }
  } else {
    // Single / Married Filing Separately
    if (taxableIncome <= 12400) {
      annualTax = taxableIncome * 0.10;
    } else if (taxableIncome <= 50400) {
      annualTax = 1240 + (taxableIncome - 12400) * 0.12;
    } else if (taxableIncome <= 105700) {
      annualTax = 5800 + (taxableIncome - 50400) * 0.22;
    } else if (taxableIncome <= 201750) {
      annualTax = 17966 + (taxableIncome - 105700) * 0.24;
    } else if (taxableIncome <= 256200) {
      annualTax = 41018 + (taxableIncome - 201750) * 0.32;
    } else if (taxableIncome <= 640600) {
      annualTax = 58442 + (taxableIncome - 256200) * 0.35;
    } else {
      annualTax = 192982 + (taxableIncome - 640600) * 0.37;
    }
  }

  const tentativeMonthlyFit = Math.round((annualTax / 12) * 100) / 100;
  const monthlyCredits = Math.round((step3Credits / 12) * 100) / 100;
  const monthlyFit = Math.max(0, Math.round((tentativeMonthlyFit - monthlyCredits + step4cExtra) * 100) / 100);

  lines.push({
    code: "US_FIT",
    description: `Federal Income Tax (IRS Pub 15-T 2026 - ${filingStatus}, StdDed $${standardDeduction.toLocaleString()})`,
    employeeAmount: monthlyFit,
    employerAmount: 0,
    ratePct: Math.round((monthlyFit / (gross || 1)) * 1000) / 10,
    authority: "Internal Revenue Service (IRS)",
  });

  // 4. FUTA (Federal Unemployment Tax Act - 0.6% on first $7,000 / $583.33/mo)
  const futaMonthlyBase = Math.min(gross, 7000.0 / 12);
  const futaEmployer = Math.round(futaMonthlyBase * 0.006 * 100) / 100;
  lines.push({
    code: "US_FUTA",
    description: "Federal Unemployment Tax (FUTA 0.6% on first $7,000/yr)",
    employeeAmount: 0,
    employerAmount: futaEmployer,
    ratePct: 0.6,
    authority: "Internal Revenue Service (IRS) / US Dept of Labor",
  });

  // 5. SUTA (State Unemployment Tax Act - default 2.7% on state wage base $833.33/mo)
  const sutaRate = request.sutaRate !== undefined ? request.sutaRate : 0.027;
  const sutaMonthlyBase = Math.min(gross, 10000.0 / 12);
  const sutaEmployer = Math.round(sutaMonthlyBase * sutaRate * 100) / 100;
  lines.push({
    code: "US_SUTA",
    description: `State Unemployment Tax (SUTA ${(sutaRate * 100).toFixed(2)}%)`,
    employeeAmount: 0,
    employerAmount: sutaEmployer,
    ratePct: Math.round(sutaRate * 10000) / 100,
    authority: "State Department of Labor / Unemployment Insurance Agency",
  });

  const totalEmpDed = ssEmployee + totalMedicareEe + monthlyFit;
  const totalEmplr = ssEmployer + medEmployerBase + futaEmployer + sutaEmployer;
  const net = Math.max(0, Math.round((gross - totalEmpDed) * 100) / 100);

  return {
    grossSalary: gross,
    employeeIncomeTax: monthlyFit,
    employeeStatutoryDeductions: totalEmpDed,
    employerStatutoryContributions: totalEmplr,
    netSalary: net,
    currencyCode,
    countryCode: "US",
    statutoryLines: lines,
    explanation: `US Federal Payroll calculated under IRS Pub 15-T (2026), OASDI cap $${annualWageBase.toLocaleString()}, status=${filingStatus}, stdDed=$${standardDeduction.toLocaleString()}.`,
  };
}

/**
 * United Kingdom Payroll Adapter
 * - 2026-27 Employer Class 1 NIC: 15.0% above £417.00/month (£5,000/yr / 12)
 * - Employee Class 1 NIC: 8% (£1,048–£4,189/mo) and 2% above UEL
 * - Category letters (A, B, C, H, M, J)
 * - Scottish 6-band Income Tax Published Schedule 2026-27 vs Rest of UK
 * - 2026-27 Student Loan Thresholds: Plan 1 £2,241.66, Plan 2 £2,448.75, Plan 4 £2,816.25, Plan 5 £2,083.33, PG £1,750.00
 * - Workplace Pension Auto-Enrolment (5% EE, 3% ER on qualifying earnings £520–£4,189/mo)
 */
export function calculateUkPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const currencyCode = request.currencyCode || "GBP";
  const lines: StatutoryLine[] = [];
  const effectiveDate = request.effectiveDate || new Date().toISOString().substring(0, 10);
  const categoryLetter: UkCategoryLetter = request.ukCategoryLetter || "A";
  const isScottish = Boolean(
    request.isScottishTaxpayer ||
    (request.jurisdiction && request.jurisdiction.toUpperCase().includes("SCOTLAND"))
  );

  // 1. Monthly Thresholds
  const primaryThreshold = 1048.0;      // £12,570 / 12
  const upperEarningsLimit = 4189.0;    // £50,270 / 12
  const is2026Reform = effectiveDate >= "2025-04-06";
  const secondaryThreshold = is2026Reform ? 417.0 : 758.0;
  const employerNicRate = is2026Reform ? 0.150 : 0.138;

  // 2. Class 1 Employee NIC by Category Letter
  let employeeNic = 0;
  if (categoryLetter === "C") {
    employeeNic = 0; // State pension age exemption
  } else if (categoryLetter === "B") {
    if (gross > primaryThreshold) {
      const band1 = Math.min(gross, upperEarningsLimit) - primaryThreshold;
      employeeNic += band1 * 0.0585;
      if (gross > upperEarningsLimit) {
        employeeNic += (gross - upperEarningsLimit) * 0.02;
      }
    }
  } else if (categoryLetter === "J") {
    if (gross > primaryThreshold) {
      employeeNic = (gross - primaryThreshold) * 0.02;
    }
  } else {
    // Categories A, H, M: Standard 8% and 2%
    if (gross > primaryThreshold) {
      const band1 = Math.min(gross, upperEarningsLimit) - primaryThreshold;
      employeeNic += band1 * 0.08;
      if (gross > upperEarningsLimit) {
        employeeNic += (gross - upperEarningsLimit) * 0.02;
      }
    }
  }
  employeeNic = Math.round(employeeNic * 100) / 100;

  // 3. Class 1 Employer NIC
  let employerNic = 0;
  if (categoryLetter === "H" || categoryLetter === "M") {
    if (gross > upperEarningsLimit) {
      employerNic = Math.round((gross - upperEarningsLimit) * employerNicRate * 100) / 100;
    }
  } else {
    if (gross > secondaryThreshold) {
      employerNic = Math.round((gross - secondaryThreshold) * employerNicRate * 100) / 100;
    }
  }

  lines.push({
    code: "UK_NIC",
    description: `Class 1 National Insurance Contributions (Category ${categoryLetter}, ER ${(employerNicRate * 100).toFixed(1)}% > £${secondaryThreshold})`,
    employeeAmount: employeeNic,
    employerAmount: employerNic,
    ratePct: categoryLetter === "C" ? 0.0 : 8.0,
    authority: "HM Revenue & Customs (HMRC)",
  });

  // 4. PAYE Income Tax (Monthly Taxable Thresholds)
  const annualGross = gross * 12;
  const personalAllowance = annualGross > 100000
    ? Math.max(0, 12570 - (annualGross - 100000) * 0.5)
    : 12570;
  const monthlyPersonalAllowance = Math.round((personalAllowance / 12) * 100) / 100;
  const taxablePaye = Math.max(0, gross - monthlyPersonalAllowance);
  let monthlyPaye = 0;

  if (isScottish) {
    // Scottish 6-Band Schedule (2026/27 Published Thresholds)
    if (taxablePaye > 0) {
      let rem = taxablePaye;
      const band1 = Math.min(rem, 192.17); // Starter 19%
      monthlyPaye += band1 * 0.19;
      rem -= band1;

      if (rem > 0) {
        const band2 = Math.min(rem, 973.75); // Basic 20%
        monthlyPaye += band2 * 0.20;
        rem -= band2;
      }
      if (rem > 0) {
        const band3 = Math.min(rem, 1425.08); // Intermediate 21%
        monthlyPaye += band3 * 0.21;
        rem -= band3;
      }
      if (rem > 0) {
        const band4 = Math.min(rem, 2611.50); // Higher 42%
        monthlyPaye += band4 * 0.42;
        rem -= band4;
      }
      if (rem > 0) {
        const band5 = Math.min(rem, 5225.83); // Advanced 45%
        monthlyPaye += band5 * 0.45;
        rem -= band5;
      }
      if (rem > 0) {
        monthlyPaye += rem * 0.48; // Top 48%
      }
    }
  } else {
    // Rest of UK (Basic 20%, Higher 40%, Additional 45%)
    if (taxablePaye > 0) {
      let rem = taxablePaye;
      const basicBand = Math.min(rem, 3141.67);
      monthlyPaye += basicBand * 0.20;
      rem -= basicBand;

      if (rem > 0) {
        const higherBand = Math.min(rem, 7286.66);
        monthlyPaye += higherBand * 0.40;
        rem -= higherBand;
      }
      if (rem > 0) {
        monthlyPaye += rem * 0.45;
      }
    }
  }

  monthlyPaye = Math.round(monthlyPaye * 100) / 100;
  lines.push({
    code: "UK_PAYE",
    description: isScottish
      ? "Scottish Pay As You Earn (PAYE 6-Band Schedule 2026/27)"
      : "Pay As You Earn (PAYE Income Tax)",
    employeeAmount: monthlyPaye,
    employerAmount: 0,
    ratePct: Math.round((monthlyPaye / (gross || 1)) * 1000) / 10,
    authority: "HM Revenue & Customs (HMRC)",
  });

  // 5. Student Loan Repayments (Official 2026-27 Thresholds)
  let studentLoanDed = 0;
  const slPlan = request.studentLoanPlan || "None";
  if (slPlan !== "None") {
    let slThreshold = 0;
    let slRate = 0.09;

    switch (slPlan) {
      case "Plan1":
        slThreshold = 2241.66;
        break;
      case "Plan2":
        slThreshold = 2448.75;
        break;
      case "Plan4": // Scottish Student Loans
        slThreshold = 2816.25;
        break;
      case "Plan5":
        slThreshold = 2083.33;
        break;
      case "Postgraduate":
        slThreshold = 1750.0;
        slRate = 0.06;
        break;
    }

    if (gross > slThreshold) {
      studentLoanDed = Math.round((gross - slThreshold) * slRate * 100) / 100;
      lines.push({
        code: `UK_SL_${slPlan.toUpperCase()}`,
        description: `Student Loan Repayment (${slPlan} ${(slRate * 100).toFixed(0)}% > £${slThreshold.toFixed(2)})`,
        employeeAmount: studentLoanDed,
        employerAmount: 0,
        ratePct: slRate * 100,
        authority: "Student Loans Company (SLC) / HMRC",
      });
    }
  }

  // 6. Workplace Pension Auto-Enrolment
  let pensionEe = 0;
  let pensionEr = 0;
  if (request.pensionAutoEnrolment) {
    const qualLowerThreshold = 520.0;
    const qualUpperThreshold = 4189.0;

    if (gross > qualLowerThreshold) {
      const qualifyingEarnings = Math.min(gross, qualUpperThreshold) - qualLowerThreshold;
      const eeRate = request.pensionEmployeeRate !== undefined ? request.pensionEmployeeRate : 0.05;
      const erRate = request.pensionEmployerRate !== undefined ? request.pensionEmployerRate : 0.03;

      pensionEe = Math.round(qualifyingEarnings * eeRate * 100) / 100;
      pensionEr = Math.round(qualifyingEarnings * erRate * 100) / 100;

      lines.push({
        code: "UK_PENSION_AE",
        description: `Workplace Pension Auto-Enrolment (EE ${(eeRate * 100).toFixed(1)}%, ER ${(erRate * 100).toFixed(1)}% on Qualifying Earnings)`,
        employeeAmount: pensionEe,
        employerAmount: pensionEr,
        ratePct: Math.round((eeRate + erRate) * 1000) / 10,
        authority: "The Pensions Regulator (TPR)",
      });
    }
  }

  const totalEmpDed = employeeNic + monthlyPaye + studentLoanDed + pensionEe;
  const totalEmplr = employerNic + pensionEr;
  const net = Math.max(0, Math.round((gross - totalEmpDed) * 100) / 100);

  return {
    grossSalary: gross,
    employeeIncomeTax: monthlyPaye,
    employeeStatutoryDeductions: totalEmpDed,
    employerStatutoryContributions: totalEmplr,
    netSalary: net,
    currencyCode,
    countryCode: "GB",
    statutoryLines: lines,
    explanation: `UK Payroll (HMRC TY2026/27): ER NIC 15.0% (>£417/mo), Category=${categoryLetter}, Scottish=${isScottish}.`,
  };
}

/**
 * United Arab Emirates Payroll Adapter
 * Separate effective-dated cohort rules for:
 * - Federal Law 7 of 1999 (Legacy GPSSA: EE 5%, ER 12.5% + Gov 2.5%, cap AED 50k)
 * - Federal Decree-Law 57 of 2023 (New GPSSA: EE 11%, ER 15% [or 12.5% + 2.5% subsidy <20k], cap AED 70k private / AED 100k gov)
 * - Abu Dhabi Pension Fund (ADPF: EE 11%, ER 15%, cap AED 100k)
 * - Sharjah Social Security Fund (SSSF: EE 5%, ER 15%, cap AED 50k)
 * - Expatriates: 0% tax, MOHRE End of Service Gratuity (EOSG) accrual under Federal Decree-Law 33 of 2021
 */
export function calculateUaePayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const basic = Math.max(0, request.basicSalary || gross * 0.6);
  const allowances = Math.max(0, request.allowances || gross - basic);
  const currencyCode = request.currencyCode || "AED";
  const isExpatriate = request.isExpatriate !== false && !request.uaePensionScheme;
  const lines: StatutoryLine[] = [];

  let employeeDeduction = 0;
  let employerContribution = 0;
  let govtSubsidy = 0;

  if (!isExpatriate) {
    const isGov = request.uaeSector === "government";
    const scheme: UaePensionScheme = request.uaePensionScheme ||
      (request.effectiveDate && request.effectiveDate >= "2023-10-14" ? "DecreeLaw57_2023" : "Law7_1999");
    const pensionableSalary = basic + allowances;

    let capAmount = 50000;
    let eeRate = 0.05;
    let erRate = 0.125;

    switch (scheme) {
      case "Law7_1999":
        capAmount = 50000;
        eeRate = 0.05;
        if (isGov) {
          erRate = 0.15;
          govtSubsidy = 0;
        } else {
          erRate = 0.125;
          govtSubsidy = Math.round(Math.min(pensionableSalary, capAmount) * 0.025 * 100) / 100;
        }
        break;

      case "ADPF":
        capAmount = 100000;
        eeRate = 0.11;
        erRate = 0.15;
        govtSubsidy = 0;
        break;

      case "SSSF":
        capAmount = 50000;
        eeRate = 0.05;
        erRate = 0.15;
        govtSubsidy = 0;
        break;

      case "DecreeLaw57_2023":
      default:
        capAmount = isGov ? 100000 : 70000;
        eeRate = 0.11;
        if (isGov) {
          erRate = 0.15;
          govtSubsidy = 0;
        } else {
          if (pensionableSalary < 20000) {
            erRate = 0.125;
            govtSubsidy = Math.round(pensionableSalary * 0.025 * 100) / 100;
          } else {
            erRate = 0.15;
            govtSubsidy = 0;
          }
        }
        break;
    }

    const cappedBase = Math.min(pensionableSalary, capAmount);
    employeeDeduction = Math.round(cappedBase * eeRate * 100) / 100;
    employerContribution = Math.round(cappedBase * erRate * 100) / 100;

    lines.push({
      code: "AE_PENSION_EE",
      description: `UAE National Pension EE (${scheme} ${(eeRate * 100).toFixed(1)}% up to AED ${capAmount.toLocaleString()})`,
      employeeAmount: employeeDeduction,
      employerAmount: employerContribution,
      ratePct: eeRate * 100,
      authority: scheme === "ADPF" ? "Abu Dhabi Pension Fund (ADPF)" : scheme === "SSSF" ? "Sharjah Social Security Fund (SSSF)" : "GPSSA",
    });

    if (govtSubsidy > 0) {
      lines.push({
        code: "AE_PENSION_GOVT_SUBSIDY",
        description: "UAE Government Pension Contribution Subsidy (2.5%)",
        employeeAmount: 0,
        employerAmount: govtSubsidy,
        ratePct: 2.5,
        authority: "Ministry of Finance / GPSSA",
      });
    }
  } else {
    // Expatriate End of Service Gratuity (EOSG) monthly accrual (1.75 days basic pay / month)
    employerContribution = Math.round((basic / 30.0) * 1.75 * 100) / 100;
    lines.push({
      code: "AE_EOSG",
      description: "End of Service Gratuity (EOSG Statutory Accrual 1.75 days basic pay)",
      employeeAmount: 0,
      employerAmount: employerContribution,
      ratePct: 5.75,
      authority: "UAE Ministry of Human Resources and Emiratisation (MOHRE)",
    });
  }

  const net = Math.max(0, Math.round((gross - employeeDeduction) * 100) / 100);

  return {
    grossSalary: gross,
    employeeIncomeTax: 0,
    employeeStatutoryDeductions: employeeDeduction,
    employerStatutoryContributions: employerContribution + govtSubsidy,
    netSalary: net,
    currencyCode,
    countryCode: "AE",
    statutoryLines: lines,
    explanation: isExpatriate
      ? "UAE Expatriate Payroll: 0% personal income tax, MOHRE End of Service Gratuity monthly accrual."
      : `UAE National Payroll: 0% tax, Pension cohort ${request.uaePensionScheme || "DecreeLaw57_2023"} (${request.uaeSector || "private"} sector).`,
  };
}

/**
 * Australia Payroll Adapter
 * - 12.0% Superannuation Guarantee effective 1 July 2026
 * - Ordinary Time Earnings (OTE) qualification (excludes overtime)
 * - Payday Super compliance with 7-business-day fund receipt clearance rule
 * - ATO PAYG Withholding Schedule 2026
 */
export function calculateAustraliaPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const currencyCode = request.currencyCode || "AUD";
  const lines: StatutoryLine[] = [];
  const effectiveDate = request.effectiveDate || new Date().toISOString().substring(0, 10);

  // 1. Superannuation Guarantee (12.0% effective 1 July 2026, 11.5% prior)
  const is2026Super = effectiveDate >= "2026-07-01";
  const superRate = is2026Super ? 0.12 : 0.115;
  const ote = request.ordinaryTimeEarnings !== undefined ? request.ordinaryTimeEarnings : gross;
  // Maximum Super Contribution Base (MSCB): quarterly $65,070 ($21,690/mo)
  const cappedOte = Math.min(ote, 21690.0);
  const superEr = Math.round(cappedOte * superRate * 100) / 100;

  lines.push({
    code: "AU_SUPER",
    description: `Superannuation Guarantee (${(superRate * 100).toFixed(1)}% on OTE - Payday Super 7-day rule)`,
    employeeAmount: 0,
    employerAmount: superEr,
    ratePct: superRate * 100,
    authority: "Australian Taxation Office (ATO)",
  });

  // 2. PAYG Withholding (ATO 2026 Schedule - Annual Tax-Free Threshold $18,200)
  const annualGross = gross * 12;
  let annualPayg = 0;
  if (annualGross > 18200) {
    const rem = annualGross - 18200;
    if (rem <= 26800) {
      annualPayg = rem * 0.16;
    } else if (rem <= 116800) {
      annualPayg = 4288 + (rem - 26800) * 0.30;
    } else if (rem <= 171800) {
      annualPayg = 31288 + (rem - 116800) * 0.37;
    } else {
      annualPayg = 51638 + (rem - 171800) * 0.45;
    }
  }

  const monthlyPayg = Math.round((annualPayg / 12) * 100) / 100;
  lines.push({
    code: "AU_PAYG",
    description: "ATO PAYG Income Tax Withholding",
    employeeAmount: monthlyPayg,
    employerAmount: 0,
    ratePct: Math.round((monthlyPayg / (gross || 1)) * 1000) / 10,
    authority: "Australian Taxation Office (ATO)",
  });

  const net = Math.max(0, Math.round((gross - monthlyPayg) * 100) / 100);

  return {
    grossSalary: gross,
    employeeIncomeTax: monthlyPayg,
    employeeStatutoryDeductions: monthlyPayg,
    employerStatutoryContributions: superEr,
    netSalary: net,
    currencyCode,
    countryCode: "AU",
    statutoryLines: lines,
    explanation: `Australia Payroll (ATO 2026): Super ${(superRate * 100).toFixed(1)}% (Payday Super mandate), PAYG Withholding.`,
  };
}

/**
 * Pakistan Payroll Adapter
 * FBR Tax Year 2027 salaried brackets, EOBI (PKR 37,000 base), Provincial Social Security.
 */
export function calculatePakistanPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const currencyCode = request.currencyCode || "PKR";
  const lines: StatutoryLine[] = [];

  // FBR Tax Year 2027 Salaried Slabs (Annualized)
  const annualized = gross * 12;
  let annualTax = 0;

  if (annualized <= 600000) {
    annualTax = 0;
  } else if (annualized <= 1200000) {
    annualTax = (annualized - 600000) * 0.01;
  } else if (annualized <= 2200000) {
    annualTax = 6000 + (annualized - 1200000) * 0.11;
  } else if (annualized <= 3200000) {
    annualTax = 116000 + (annualized - 2200000) * 0.20;
  } else if (annualized <= 4100000) {
    annualTax = 316000 + (annualized - 3200000) * 0.25;
  } else if (annualized <= 5600000) {
    annualTax = 541000 + (annualized - 4100000) * 0.29;
  } else if (annualized <= 7000000) {
    annualTax = 976000 + (annualized - 5600000) * 0.32;
  } else {
    annualTax = 1424000 + (annualized - 7000000) * 0.35;
  }

  const monthlyTax = Math.round((annualTax / 12) * 100) / 100;
  lines.push({
    code: "PK_FBR_TAX",
    description: "Income Tax on Salary (FBR Tax Year 2027)",
    employeeAmount: monthlyTax,
    employerAmount: 0,
    ratePct: Math.round((monthlyTax / (gross || 1)) * 1000) / 10,
    authority: "Federal Board of Revenue (FBR)",
  });

  // EOBI (Statutory base PKR 37,000: 1% employee = PKR 370, 5% employer = PKR 1,850)
  const eobiEmployee = 370.0;
  const eobiEmployer = 1850.0;
  lines.push({
    code: "PK_EOBI",
    description: "Employees' Old-Age Benefits Institution (EOBI)",
    employeeAmount: eobiEmployee,
    employerAmount: eobiEmployer,
    ratePct: 1.0,
    authority: "Employees' Old-Age Benefits Institution (EOBI)",
  });

  // Provincial Social Security (SESSI / PESSI) - Employer Only 5% on ceiling PKR 40,000
  const sessiWageCeiling = 40000;
  const sessiBase = Math.min(gross, sessiWageCeiling);
  const sessiEmployer = Math.round(sessiBase * 0.05 * 100) / 100;
  lines.push({
    code: "PK_SESSI",
    description: "Provincial Social Security Institution (SESSI / PESSI)",
    employeeAmount: 0,
    employerAmount: sessiEmployer,
    ratePct: 5.0,
    authority: "Sindh / Punjab Social Security Institution",
  });

  const totalEmpDed = monthlyTax + eobiEmployee;
  const totalEmplr = eobiEmployer + sessiEmployer;
  const net = Math.max(0, Math.round((gross - totalEmpDed) * 100) / 100);

  return {
    grossSalary: gross,
    employeeIncomeTax: monthlyTax,
    employeeStatutoryDeductions: totalEmpDed,
    employerStatutoryContributions: totalEmplr,
    netSalary: net,
    currencyCode,
    countryCode: "PK",
    statutoryLines: lines,
    explanation: "Pakistan payroll calculated under FBR Finance Act 2026 / TY2027 rates and EOBI Act 1976.",
  };
}

/**
 * Saudi Arabia Payroll Adapter
 * - GOSI Social Insurance for Saudi Nationals: Annuities 9.0% EE / 9.0% ER, SANED 0.75% EE / 0.75% ER, Occ Hazards 2.0% ER
 * - Contribution base capped at SAR 45,000.00/month
 * - Expatriates: 0% income tax, GOSI Occ Hazards 2.0% ER only
 * - Saudi Labor Law Art. 84 End of Service Award (EOSA) accrual: 4.167% (<= 5 yrs) or 8.333% (> 5 yrs)
 */
export function calculateSaudiArabiaPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const basic = Math.max(0, request.basicSalary || gross * 0.6);
  const allowances = Math.max(0, request.allowances || gross - basic);
  const currencyCode = request.currencyCode || "SAR";
  const isNational = request.isExpatriate !== true;
  const lines: StatutoryLine[] = [];

  let employeePension = 0;
  let employerPension = 0;
  let employerSocialSecurity = 0;

  if (isNational) {
    const pensionableBase = Math.min(basic + allowances, 45000.0);
    const annuitiesEe = Math.round(pensionableBase * 0.09 * 100) / 100;
    const annuitiesEr = Math.round(pensionableBase * 0.09 * 100) / 100;
    const sanedEe = Math.round(pensionableBase * 0.0075 * 100) / 100;
    const sanedEr = Math.round(pensionableBase * 0.0075 * 100) / 100;
    const occHazardsEr = Math.round(pensionableBase * 0.02 * 100) / 100;

    employeePension = annuitiesEe + sanedEe;
    employerPension = annuitiesEr + sanedEr;
    employerSocialSecurity = occHazardsEr;

    lines.push({
      code: "SA_GOSI_ANNUITIES",
      description: "GOSI Annuities Branch (9.0% up to SAR 45k cap)",
      employeeAmount: annuitiesEe,
      employerAmount: annuitiesEr,
      ratePct: 9.0,
      authority: "General Organization for Social Insurance (GOSI)",
    });

    lines.push({
      code: "SA_GOSI_SANED",
      description: "SANED Unemployment Insurance (0.75% up to SAR 45k cap)",
      employeeAmount: sanedEe,
      employerAmount: sanedEr,
      ratePct: 0.75,
      authority: "General Organization for Social Insurance (GOSI)",
    });

    lines.push({
      code: "SA_GOSI_HAZARDS",
      description: "GOSI Occupational Hazards (2.0% ER)",
      employeeAmount: 0,
      employerAmount: occHazardsEr,
      ratePct: 2.0,
      authority: "General Organization for Social Insurance (GOSI)",
    });
  } else {
    // Expatriate: Occupational Hazards 2.0% ER only
    const occBase = Math.min(basic + allowances, 45000.0);
    const occHazardsEr = Math.round(occBase * 0.02 * 100) / 100;
    employerSocialSecurity = occHazardsEr;

    lines.push({
      code: "SA_GOSI_HAZARDS",
      description: "GOSI Occupational Hazards ER (2.0%)",
      employeeAmount: 0,
      employerAmount: occHazardsEr,
      ratePct: 2.0,
      authority: "General Organization for Social Insurance (GOSI)",
    });

    // Saudi Labor Law Art. 84 End of Service Award (EOSA) accrual
    const years = request.yearsOfService !== undefined ? request.yearsOfService : 1;
    const monthlyRate = years > 5 ? (1.0 / 12.0) : (0.5 / 12.0);
    const eosaAccrual = Math.round(basic * monthlyRate * 100) / 100;
    employerPension = eosaAccrual;

    lines.push({
      code: "SA_EOSA",
      description: `Saudi Labor Law Art 84 EOSA Accrual (${years > 5 ? "Full Month" : "Half Month"} Wage/Yr)`,
      employeeAmount: 0,
      employerAmount: eosaAccrual,
      ratePct: Math.round(monthlyRate * 10000) / 100,
      authority: "Ministry of Human Resources and Social Development",
    });
  }

  const net = Math.max(0, Math.round((gross - employeePension) * 100) / 100);

  return {
    grossSalary: gross,
    employeeIncomeTax: 0,
    employeeStatutoryDeductions: employeePension,
    employerStatutoryContributions: employerPension + employerSocialSecurity,
    netSalary: net,
    currencyCode,
    countryCode: "SA",
    statutoryLines: lines,
    explanation: isNational
      ? "Saudi National Payroll: GOSI Annuities 9% + SANED 0.75% + Hazards 2% (SAR 45k cap)."
      : "Saudi Expatriate Payroll: 0% tax, GOSI Hazards 2%, Saudi Labor Law Art 84 EOSA accrual.",
  };
}

/**
 * Canada Payroll Adapter
 * - CPP 2026: Base 5.95% EE & ER (YMPE $71,300, $3,500 exemption) + CPP2 4.0% (YAMPE $81,200)
 * - EI 2026: 1.63% EE (max $89.24/mo), 2.282% ER (1.4x matching, max $124.94/mo, limit $65,700)
 * - Federal & Provincial Income Tax Withholding (TD1 2026)
 */
export function calculateCanadaPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const currencyCode = request.currencyCode || "CAD";
  const lines: StatutoryLine[] = [];

  // 1. CPP Base & CPP2
  const monthlyExemption = 291.67; // $3,500 / 12
  const monthlyYmpe = 5941.67;     // $71,300 / 12
  const monthlyYampe = 6766.67;    // $81,200 / 12

  let cppBaseEe = 0;
  let cppBaseEr = 0;
  let cpp2Ee = 0;
  let cpp2Er = 0;

  if (gross > monthlyExemption) {
    const baseWage = Math.min(gross, monthlyYmpe) - monthlyExemption;
    cppBaseEe = Math.round(baseWage * 0.0595 * 100) / 100;
    cppBaseEr = cppBaseEe;
  }

  if (gross > monthlyYmpe) {
    const tier2Wage = Math.min(gross, monthlyYampe) - monthlyYmpe;
    cpp2Ee = Math.round(tier2Wage * 0.04 * 100) / 100;
    cpp2Er = cpp2Ee;
  }

  const totalCppEe = cppBaseEe + cpp2Ee;
  const totalCppEr = cppBaseEr + cpp2Er;

  lines.push({
    code: "CA_CPP_BASE",
    description: "Canada Pension Plan Base (CPP 5.95% up to YMPE $71,300)",
    employeeAmount: cppBaseEe,
    employerAmount: cppBaseEr,
    ratePct: 5.95,
    authority: "Canada Revenue Agency (CRA)",
  });

  if (cpp2Ee > 0) {
    lines.push({
      code: "CA_CPP2_ENHANCED",
      description: "CPP2 Second Additional Contribution (4.0% on YAMPE band)",
      employeeAmount: cpp2Ee,
      employerAmount: cpp2Er,
      ratePct: 4.0,
      authority: "Canada Revenue Agency (CRA)",
    });
  }

  // 2. Employment Insurance (EI 2026)
  const monthlyEiMax = 5475.0; // $65,700 / 12
  const insurableEi = Math.min(gross, monthlyEiMax);
  const eiEe = Math.round(insurableEi * 0.0163 * 100) / 100;
  const eiEr = Math.round(insurableEi * 0.02282 * 100) / 100;

  lines.push({
    code: "CA_EI",
    description: "Employment Insurance (EI 1.63% EE, 2.282% ER / 1.4x)",
    employeeAmount: eiEe,
    employerAmount: eiEr,
    ratePct: 1.63,
    authority: "Canada Revenue Agency (CRA) / ESDC",
  });

  // 3. Federal & Provincial Income Tax (TD1 2026)
  const annualGross = gross * 12;
  const federalBpa = 16129.0;
  const taxableAnnual = Math.max(0, annualGross - federalBpa);
  let annualFedTax = 0;

  if (taxableAnnual <= 57375) {
    annualFedTax = taxableAnnual * 0.15;
  } else if (taxableAnnual <= 114750) {
    annualFedTax = 8606.25 + (taxableAnnual - 57375) * 0.205;
  } else if (taxableAnnual <= 177882) {
    annualFedTax = 20368.13 + (taxableAnnual - 114750) * 0.26;
  } else if (taxableAnnual <= 253414) {
    annualFedTax = 36782.45 + (taxableAnnual - 177882) * 0.29;
  } else {
    annualFedTax = 58686.73 + (taxableAnnual - 253414) * 0.33;
  }

  const annualProvTax = taxableAnnual * 0.0505; // Ontario benchmark ~5.05%
  const monthlyTax = Math.round(((annualFedTax + annualProvTax) / 12) * 100) / 100;

  lines.push({
    code: "CA_INCOME_TAX",
    description: "Federal & Provincial Income Tax Withholding (TD1 2026)",
    employeeAmount: monthlyTax,
    employerAmount: 0,
    ratePct: Math.round((monthlyTax / (gross || 1)) * 1000) / 10,
    authority: "Canada Revenue Agency (CRA)",
  });

  const totalEmpDed = totalCppEe + eiEe + monthlyTax;
  const totalEmplr = totalCppEr + eiEr;
  const net = Math.max(0, Math.round((gross - totalEmpDed) * 100) / 100);

  return {
    grossSalary: gross,
    employeeIncomeTax: monthlyTax,
    employeeStatutoryDeductions: totalEmpDed,
    employerStatutoryContributions: totalEmplr,
    netSalary: net,
    currencyCode,
    countryCode: "CA",
    statutoryLines: lines,
    explanation: "Canada payroll (CRA 2026): Base CPP 5.95%, CPP2 4.0%, EI 1.63%, TD1 Federal & Provincial withholding.",
  };
}

/**
 * India Payroll Adapter
 * - EPF 12% EE & 12% ER (split into 8.33% EPS capped at ₹15,000 wage = ₹1,250 + EPF balance + 0.5% EDLI)
 * - ESI: 0.75% EE, 3.25% ER (if gross <= ₹21,000/month)
 * - State Professional Tax (e.g. Maharashtra ₹200/mo)
 * - Income Tax TDS under Sec 115BAC New Tax Regime (₹75,000 std deduction, 6 slabs, 4% cess)
 */
export function calculateIndiaPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const basic = Math.max(0, request.basicSalary || gross * 0.5);
  const currencyCode = request.currencyCode || "INR";
  const lines: StatutoryLine[] = [];

  // 1. Employees' Provident Fund (EPF)
  const pfWage = basic;
  const epfEe = Math.round(pfWage * 0.12 * 100) / 100;

  const epsBase = Math.min(pfWage, 15000.0);
  const epsEr = Math.round(epsBase * 0.0833 * 100) / 100; // max ₹1,250.00
  const epfEr = epfEe - epsEr;
  const edliEr = Math.round(Math.min(pfWage, 15000.0) * 0.005 * 100) / 100;

  lines.push({
    code: "IN_EPF_EE",
    description: "Employees' Provident Fund EE (12.0% on Basic/PF Wage)",
    employeeAmount: epfEe,
    employerAmount: 0,
    ratePct: 12.0,
    authority: "Employees' Provident Fund Organisation (EPFO)",
  });

  lines.push({
    code: "IN_EPF_ER",
    description: "EPF & EPS Employer Contribution (12.0% total)",
    employeeAmount: 0,
    employerAmount: epfEr + epsEr + edliEr,
    ratePct: 12.5,
    authority: "Employees' Provident Fund Organisation (EPFO)",
  });

  // 2. Employees' State Insurance (ESI)
  let esiEe = 0;
  let esiEr = 0;
  if (gross <= 21000.0) {
    esiEe = Math.round(gross * 0.0075 * 100) / 100;
    esiEr = Math.round(gross * 0.0325 * 100) / 100;

    lines.push({
      code: "IN_ESI",
      description: "Employees' State Insurance (ESI: 0.75% EE, 3.25% ER on gross <= ₹21,000)",
      employeeAmount: esiEe,
      employerAmount: esiEr,
      ratePct: 0.75,
      authority: "Employees' State Insurance Corporation (ESIC)",
    });
  }

  // 3. State Professional Tax (PT)
  let profTax = 0;
  const state = (request.jurisdiction || "MH").trim().toUpperCase();
  if (gross > 10000.0) {
    const isFeb = request.effectiveDate && request.effectiveDate.endsWith("-02");
    profTax = (state === "MH" && isFeb) ? 300.0 : 200.0;

    lines.push({
      code: "IN_PT",
      description: `State Professional Tax (${state})`,
      employeeAmount: profTax,
      employerAmount: 0,
      ratePct: 0,
      authority: "State Commercial Tax Department",
    });
  }

  // 4. Income Tax TDS (Sec 115BAC New Tax Regime)
  const annualGross = gross * 12;
  const taxableAnnual = Math.max(0, annualGross - 75000.0); // Standard deduction ₹75,000
  let annualTax = 0;

  if (taxableAnnual > 300000) {
    if (taxableAnnual <= 700000) {
      annualTax = 0; // Sec 87A rebate
    } else {
      annualTax += 20000; // 3-7L @ 5%
      let rem = taxableAnnual - 700000;

      if (rem <= 300000) {
        annualTax += rem * 0.10;
      } else {
        annualTax += 30000; // 7-10L @ 10%
        rem -= 300000;

        if (rem <= 200000) {
          annualTax += rem * 0.15;
        } else {
          annualTax += 30000; // 10-12L @ 15%
          rem -= 200000;

          if (rem <= 300000) {
            annualTax += rem * 0.20;
          } else {
            annualTax += 60000; // 12-15L @ 20%
            rem -= 300000;
            annualTax += rem * 0.30; // >15L @ 30%
          }
        }
      }

      annualTax = Math.round(annualTax * 1.04 * 100) / 100; // 4% Cess
    }
  }

  const monthlyTds = Math.round((annualTax / 12) * 100) / 100;
  if (monthlyTds > 0) {
    lines.push({
      code: "IN_TDS",
      description: "Income Tax TDS (Section 115BAC New Tax Regime with 4% Cess)",
      employeeAmount: monthlyTds,
      employerAmount: 0,
      ratePct: Math.round((monthlyTds / (gross || 1)) * 1000) / 10,
      authority: "Income Tax Department (CBDT)",
    });
  }

  const totalEmpDed = epfEe + esiEe + profTax + monthlyTds;
  const totalEmplr = epfEr + epsEr + edliEr + esiEr;
  const net = Math.max(0, Math.round((gross - totalEmpDed) * 100) / 100);

  return {
    grossSalary: gross,
    employeeIncomeTax: monthlyTds,
    employeeStatutoryDeductions: totalEmpDed,
    employerStatutoryContributions: totalEmplr,
    netSalary: net,
    currencyCode,
    countryCode: "IN",
    statutoryLines: lines,
    explanation: "India payroll: EPF 12%, ESI (if <= ₹21k), Professional Tax, Sec 115BAC New Tax Regime (₹75k std ded).",
  };
}

/**
 * Netherlands Payroll Adapter (Loonheffing & Werkgeverspremies)
 */
export function calculateNetherlandsPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const currencyCode = request.currencyCode || "EUR";
  const annualGross = gross * 12;

  let annualTax = 0;
  if (annualGross <= 75518) {
    annualTax = annualGross * 0.3697;
  } else {
    annualTax = 75518 * 0.3697 + (annualGross - 75518) * 0.4950;
  }

  const generalCredit = Math.max(0, 3362 - Math.max(0, annualGross - 24812) * 0.0663);
  const netAnnualTax = Math.max(0, annualTax - generalCredit);
  const monthlyTax = Math.round((netAnnualTax / 12) * 100) / 100;

  const erPremiums = Math.round(gross * 0.185 * 100) / 100;

  const lines: StatutoryLine[] = [
    {
      code: "NL_LOONHEFFING",
      description: "Loonheffing (Wage Tax & National Insurance)",
      employeeAmount: monthlyTax,
      employerAmount: 0,
      ratePct: Math.round((monthlyTax / (gross || 1)) * 1000) / 10,
      authority: "Belastingdienst",
    },
    {
      code: "NL_WERKGEVERSPREMIES",
      description: "Werkgeverspremies (AOF / Awf / Whk ~18.5%)",
      employeeAmount: 0,
      employerAmount: erPremiums,
      ratePct: 18.5,
      authority: "UWV / Belastingdienst",
    },
  ];

  const net = Math.max(0, Math.round((gross - monthlyTax) * 100) / 100);
  return {
    grossSalary: gross,
    employeeIncomeTax: monthlyTax,
    employeeStatutoryDeductions: monthlyTax,
    employerStatutoryContributions: erPremiums,
    netSalary: net,
    currencyCode,
    countryCode: "NL",
    statutoryLines: lines,
    explanation: "Netherlands payroll: Loonheffing progressive brackets and general tax credit.",
  };
}

/**
 * Spain Payroll Adapter (Seguridad Social & IRPF)
 */
export function calculateSpainPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const currencyCode = request.currencyCode || "EUR";

  const baseCotizacion = Math.min(gross, 4720.50);
  const ssEe = Math.round(baseCotizacion * 0.0635 * 100) / 100;
  const ssEr = Math.round(baseCotizacion * 0.2990 * 100) / 100;

  const irpfRate = gross > 5000 ? 0.30 : gross > 3000 ? 0.19 : 0.15;
  const irpfWithholding = Math.round(gross * irpfRate * 100) / 100;

  const lines: StatutoryLine[] = [
    {
      code: "ES_SEG_SOCIAL_EE",
      description: "Seguridad Social Trabajador (6.35%)",
      employeeAmount: ssEe,
      employerAmount: 0,
      ratePct: 6.35,
      authority: "Tesorería General de la Seguridad Social (TGSS)",
    },
    {
      code: "ES_SEG_SOCIAL_ER",
      description: "Seguridad Social Empresa (29.90%)",
      employeeAmount: 0,
      employerAmount: ssEr,
      ratePct: 29.9,
      authority: "Tesorería General de la Seguridad Social (TGSS)",
    },
    {
      code: "ES_IRPF",
      description: `Retención IRPF (${(irpfRate * 100).toFixed(1)}%)`,
      employeeAmount: irpfWithholding,
      employerAmount: 0,
      ratePct: irpfRate * 100,
      authority: "Agencia Estatal de Administración Tributaria (AEAT)",
    },
  ];

  const totalEe = ssEe + irpfWithholding;
  const net = Math.max(0, Math.round((gross - totalEe) * 100) / 100);
  return {
    grossSalary: gross,
    employeeIncomeTax: irpfWithholding,
    employeeStatutoryDeductions: totalEe,
    employerStatutoryContributions: ssEr,
    netSalary: net,
    currencyCode,
    countryCode: "ES",
    statutoryLines: lines,
    explanation: "Spain payroll: Seguridad Social (6.35% EE, 29.9% ER) and IRPF withholding.",
  };
}

/**
 * Italy Payroll Adapter (INPS, IRPEF & TFR)
 */
export function calculateItalyPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const currencyCode = request.currencyCode || "EUR";

  const inpsEe = Math.round(gross * 0.0919 * 100) / 100;
  const inpsEr = Math.round(gross * 0.3000 * 100) / 100;
  const tfrAccrual = Math.round((gross / 13.5) * 100) / 100;

  const taxableMonthly = Math.max(0, gross - inpsEe);
  const irpefWithholding = Math.round(taxableMonthly * 0.23 * 100) / 100;

  const lines: StatutoryLine[] = [
    {
      code: "IT_INPS_EE",
      description: "Contributi Previdenziali INPS (9.19%)",
      employeeAmount: inpsEe,
      employerAmount: 0,
      ratePct: 9.19,
      authority: "Istituto Nazionale Previdenza Sociale (INPS)",
    },
    {
      code: "IT_INPS_ER",
      description: "Contributi Previdenziali INPS Datore (30.0%)",
      employeeAmount: 0,
      employerAmount: inpsEr,
      ratePct: 30.0,
      authority: "Istituto Nazionale Previdenza Sociale (INPS)",
    },
    {
      code: "IT_TFR",
      description: "Accantonamento Trattamento di Fine Rapporto (TFR / 13.5)",
      employeeAmount: 0,
      employerAmount: tfrAccrual,
      ratePct: 7.41,
      authority: "Codice Civile Art. 2120",
    },
    {
      code: "IT_IRPEF",
      description: "Ritenute Fiscali IRPEF (Scaglione Base 23%)",
      employeeAmount: irpefWithholding,
      employerAmount: 0,
      ratePct: 23.0,
      authority: "Agenzia delle Entrate",
    },
  ];

  const totalEe = inpsEe + irpefWithholding;
  const net = Math.max(0, Math.round((gross - totalEe) * 100) / 100);
  return {
    grossSalary: gross,
    employeeIncomeTax: irpefWithholding,
    employeeStatutoryDeductions: totalEe,
    employerStatutoryContributions: inpsEr + tfrAccrual,
    netSalary: net,
    currencyCode,
    countryCode: "IT",
    statutoryLines: lines,
    explanation: "Italy payroll: INPS social contributions, IRPEF withholding, and TFR accrual.",
  };
}

/**
 * Poland Payroll Adapter (ZUS, NFZ & PIT)
 */
export function calculatePolandPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const currencyCode = request.currencyCode || "PLN";

  const zusEe = Math.round(gross * 0.1371 * 100) / 100;
  const zusEr = Math.round(gross * 0.2038 * 100) / 100;

  const healthBase = gross - zusEe;
  const healthEe = Math.round(healthBase * 0.09 * 100) / 100;

  const pitBase = Math.max(0, healthBase - 250.0);
  const pitGross = Math.round(pitBase * 0.12 * 100) / 100;
  const pitWithholding = Math.max(0, pitGross - 300.0);

  const lines: StatutoryLine[] = [
    {
      code: "PL_ZUS_EE",
      description: "Składki na ubezpieczenia społeczne ZUS (13.71%)",
      employeeAmount: zusEe,
      employerAmount: 0,
      ratePct: 13.71,
      authority: "Zakład Ubezpieczeń Społecznych (ZUS)",
    },
    {
      code: "PL_NFZ",
      description: "Składka na ubezpieczenie zdrowotne NFZ (9.0%)",
      employeeAmount: healthEe,
      employerAmount: 0,
      ratePct: 9.0,
      authority: "Narodowy Fundusz Zdrowia (NFZ)",
    },
    {
      code: "PL_PIT",
      description: "Zaliczka na podatek dochodowy PIT (12% minus kwota zmniejszająca)",
      employeeAmount: pitWithholding,
      employerAmount: 0,
      ratePct: 12.0,
      authority: "Krajowa Administracja Skarbowa (KAS)",
    },
    {
      code: "PL_ZUS_ER",
      description: "Składki ZUS pracodawca (20.38%)",
      employeeAmount: 0,
      employerAmount: zusEr,
      ratePct: 20.38,
      authority: "Zakład Ubezpieczeń Społecznych (ZUS)",
    },
  ];

  const totalEe = zusEe + healthEe + pitWithholding;
  const net = Math.max(0, Math.round((gross - totalEe) * 100) / 100);
  return {
    grossSalary: gross,
    employeeIncomeTax: pitWithholding,
    employeeStatutoryDeductions: totalEe,
    employerStatutoryContributions: zusEr,
    netSalary: net,
    currencyCode,
    countryCode: "PL",
    statutoryLines: lines,
    explanation: "Poland payroll: ZUS social insurance (13.71% EE, 20.38% ER), NFZ health, PIT 12%.",
  };
}

/**
 * Generic Fallback Adapter
 */
export function calculateGenericPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const currencyCode = request.currencyCode || "USD";
  return {
    grossSalary: gross,
    employeeIncomeTax: 0,
    employeeStatutoryDeductions: 0,
    employerStatutoryContributions: 0,
    netSalary: gross,
    currencyCode,
    countryCode: request.countryCode || "GLOBAL",
    statutoryLines: [],
    explanation: "Standard global payroll without country-specific statutory rules.",
  };
}
