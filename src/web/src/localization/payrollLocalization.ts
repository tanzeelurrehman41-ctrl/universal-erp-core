/**
 * Universal ERP — Modular Global Payroll Engine
 * Multi-country statutory payroll adapters for US, UK, UAE, Pakistan, and Global.
 * Adheres to US 2026 OASDI ($184,500), Medicare + Additional Medicare (0.9%),
 * UK 2026-27 Employer Class 1 NIC (15%, £417 secondary threshold), Scottish PAYE,
 * Student Loan Plans, Pension Auto-Enrolment, and category letters.
 */

export type FilingStatus = "Single" | "MarriedFilingJointly" | "HeadOfHousehold";

export type UkCategoryLetter = "A" | "B" | "C" | "H" | "M" | "J";

export type StudentLoanPlan = "Plan1" | "Plan2" | "Plan4" | "Plan5" | "Postgraduate" | "None";

export type PayrollCalculationRequest = {
  employeeId?: string;
  countryCode: string; // ISO 3166-1 alpha-2 ("US", "GB", "AE", "PK", etc.)
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
    case "PK":
      return calculatePakistanPayroll(request);
    default:
      return calculateGenericPayroll(request);
  }
}

/**
 * United States Payroll Adapter
 * - 2026 OASDI wage base: $184,500 ($15,375.00/mo) (6.2% EE, 6.2% ER)
 * - FICA Medicare: 1.45% EE, 1.45% ER
 * - Additional Medicare Tax: 0.9% EE only on monthly gross > $16,666.67 (Single/HOH) / $20,833.33 (MFJ)
 * - 2026 FIT Withholding schedules with standard deductions ($15,700 Single, $31,400 MFJ, $23,550 HOH)
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

  // 3. Federal Income Tax (FIT Withholding) - Official 2026 Schedules
  const annualizedGross = gross * 12;
  const standardDeduction = filingStatus === "MarriedFilingJointly"
    ? 31400.0
    : filingStatus === "HeadOfHousehold"
    ? 23550.0
    : 15700.0;

  const taxableIncome = Math.max(0, annualizedGross - standardDeduction);
  let annualTax = 0;

  if (filingStatus === "MarriedFilingJointly") {
    if (taxableIncome <= 24800) {
      annualTax = taxableIncome * 0.10;
    } else if (taxableIncome <= 100800) {
      annualTax = 2480 + (taxableIncome - 24800) * 0.12;
    } else if (taxableIncome <= 215400) {
      annualTax = 11600 + (taxableIncome - 100800) * 0.22;
    } else if (taxableIncome <= 411050) {
      annualTax = 36812 + (taxableIncome - 215400) * 0.24;
    } else if (taxableIncome <= 521950) {
      annualTax = 83768 + (taxableIncome - 411050) * 0.32;
    } else if (taxableIncome <= 782400) {
      annualTax = 119256 + (taxableIncome - 521950) * 0.35;
    } else {
      annualTax = 210413.5 + (taxableIncome - 782400) * 0.37;
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
    // Single
    if (taxableIncome <= 12400) {
      annualTax = taxableIncome * 0.10;
    } else if (taxableIncome <= 50400) {
      annualTax = 1240 + (taxableIncome - 12400) * 0.12;
    } else if (taxableIncome <= 107700) {
      annualTax = 5800 + (taxableIncome - 50400) * 0.22;
    } else if (taxableIncome <= 205525) {
      annualTax = 18406 + (taxableIncome - 107700) * 0.24;
    } else if (taxableIncome <= 260975) {
      annualTax = 41884 + (taxableIncome - 205525) * 0.32;
    } else if (taxableIncome <= 652100) {
      annualTax = 59628 + (taxableIncome - 260975) * 0.35;
    } else {
      annualTax = 196521.75 + (taxableIncome - 652100) * 0.37;
    }
  }

  const monthlyFit = Math.round((annualTax / 12) * 100) / 100;
  lines.push({
    code: "US_FIT",
    description: `Federal Income Tax (FIT 2026 - ${filingStatus})`,
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

  // 5. SUTA (State Unemployment Tax Act - default 2.7% on state wage base)
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

  const totalEmpDed = ssEmployee + totalMedicareEe;
  const totalEmplr = ssEmployer + medEmployerBase + futaEmployer + sutaEmployer;
  const net = Math.max(0, Math.round((gross - monthlyFit - totalEmpDed) * 100) / 100);

  return {
    grossSalary: gross,
    employeeIncomeTax: monthlyFit,
    employeeStatutoryDeductions: totalEmpDed,
    employerStatutoryContributions: totalEmplr,
    netSalary: net,
    currencyCode,
    countryCode: "US",
    statutoryLines: lines,
    explanation: `US Federal Payroll calculated under IRS Pub 15-T (2026), OASDI cap $${annualWageBase.toLocaleString()}, status=${filingStatus}.`,
  };
}

/**
 * United Kingdom Payroll Adapter
 * - 2026-27 Employer Class 1 NIC: 15.0% above £417.00/month (£5,000/yr / 12)
 * - Employee Class 1 NIC: 8% (£1,048–£4,189/mo) and 2% above UEL
 * - Category letters (A, B, C, H, M, J)
 * - Scottish 6-band Income Tax vs Rest of UK
 * - Student Loan Plans (Plan 1, Plan 2, Plan 4, Plan 5, Postgraduate)
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
  const secondaryThreshold = is2026Reform ? 417.0 : 758.0; // £5,000/yr vs £9,100/yr
  const employerNicRate = is2026Reform ? 0.150 : 0.138;

  // 2. Class 1 Employee NIC by Category Letter
  let employeeNic = 0;
  if (categoryLetter === "C") {
    // Over State Pension age: exempt from employee NIC
    employeeNic = 0;
  } else if (categoryLetter === "B") {
    // Reduced rate for married women / widows with election: 5.85% between PT and UEL, 2% > UEL
    if (gross > primaryThreshold) {
      const band1 = Math.min(gross, upperEarningsLimit) - primaryThreshold;
      employeeNic += band1 * 0.0585;
      if (gross > upperEarningsLimit) {
        employeeNic += (gross - upperEarningsLimit) * 0.02;
      }
    }
  } else if (categoryLetter === "J") {
    // Deferment: 2% throughout
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
    // Apprentice under 25 (H) / Employee under 21 (M): 0% employer NIC up to UEL
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

  // 4. PAYE Income Tax (Annualized)
  const annualizedGross = gross * 12;
  const personalAllowance = annualizedGross > 100000
    ? Math.max(0, 12570 - (annualizedGross - 100000) * 0.5)
    : 12570;

  const taxablePaye = Math.max(0, annualizedGross - personalAllowance);
  let annualPaye = 0;

  if (isScottish) {
    // Scottish 6-Band Schedule (2026/27)
    // Starter: 19% (£0 - £2,306)
    // Basic: 20% (£2,306 - £13,991)
    // Intermediate: 21% (£13,991 - £31,092)
    // Higher: 42% (£31,092 - £62,430)
    // Advanced: 45% (£62,430 - £125,140)
    // Top: 48% (over £125,140)
    if (taxablePaye <= 2306) {
      annualPaye = taxablePaye * 0.19;
    } else if (taxablePaye <= 13991) {
      annualPaye = 438.14 + (taxablePaye - 2306) * 0.20;
    } else if (taxablePaye <= 31092) {
      annualPaye = 2775.14 + (taxablePaye - 13991) * 0.21;
    } else if (taxablePaye <= 62430) {
      annualPaye = 6366.35 + (taxablePaye - 31092) * 0.42;
    } else if (taxablePaye <= 125140) {
      annualPaye = 19528.31 + (taxablePaye - 62430) * 0.45;
    } else {
      annualPaye = 47747.81 + (taxablePaye - 125140) * 0.48;
    }
  } else {
    // Rest of UK (Basic 20%, Higher 40%, Additional 45%)
    if (taxablePaye <= 37700) {
      annualPaye = taxablePaye * 0.20;
    } else if (taxablePaye <= 125140) {
      annualPaye = 7540 + (taxablePaye - 37700) * 0.40;
    } else {
      annualPaye = 42516 + (taxablePaye - 125140) * 0.45;
    }
  }

  const monthlyPaye = Math.round((annualPaye / 12) * 100) / 100;
  lines.push({
    code: "UK_PAYE",
    description: isScottish
      ? "Scottish Pay As You Earn (PAYE 6-Band Schedule)"
      : "Pay As You Earn (PAYE Income Tax)",
    employeeAmount: monthlyPaye,
    employerAmount: 0,
    ratePct: Math.round((monthlyPaye / (gross || 1)) * 1000) / 10,
    authority: "HM Revenue & Customs (HMRC)",
  });

  // 5. Student Loan Repayments
  let studentLoanDed = 0;
  const slPlan = request.studentLoanPlan || "None";
  if (slPlan !== "None") {
    let slThreshold = 0;
    let slRate = 0.09;

    switch (slPlan) {
      case "Plan1":
        slThreshold = 2082.91; // £24,995 / 12
        break;
      case "Plan2":
        slThreshold = 2274.58; // £27,295 / 12
        break;
      case "Plan4": // Scottish Student Loans
        slThreshold = 2616.25; // £31,395 / 12
        break;
      case "Plan5":
        slThreshold = 2083.33; // £25,000 / 12
        break;
      case "Postgraduate":
        slThreshold = 1750.0;  // £21,000 / 12
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
    const qualLowerThreshold = 520.0; // £6,240 / 12
    const qualUpperThreshold = 4189.0; // £50,270 / 12

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

  const totalEmpDed = employeeNic + studentLoanDed + pensionEe;
  const totalEmplr = employerNic + pensionEr;
  const net = Math.max(0, Math.round((gross - monthlyPaye - totalEmpDed) * 100) / 100);

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
 * 0% Income Tax. UAE National GPSSA pension or Expatriate End of Service Gratuity (EOSG) accrual.
 */
export function calculateUaePayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const basic = Math.max(0, request.basicSalary || gross * 0.6);
  const currencyCode = request.currencyCode || "AED";
  const isExpatriate = request.isExpatriate !== false; // default true for UAE
  const lines: StatutoryLine[] = [];

  let employeeDeduction = 0;
  let employerContribution = 0;

  if (!isExpatriate) {
    // UAE National GPSSA Pension
    employeeDeduction = Math.round(gross * 0.05 * 100) / 100;
    employerContribution = Math.round(gross * 0.125 * 100) / 100;

    lines.push({
      code: "AE_GPSSA",
      description: "General Pension & Social Security Authority (GPSSA)",
      employeeAmount: employeeDeduction,
      employerAmount: employerContribution,
      ratePct: 5.0,
      authority: "General Pension & Social Security Authority (GPSSA)",
    });
  } else {
    // Expatriate End of Service Gratuity (EOSG) monthly accrual (21 days basic pay / 365 * 30.4)
    // ~5.75% of basic salary
    employerContribution = Math.round(basic * (21 / 365) * (365 / 12) / 30 * 100) / 100;
    lines.push({
      code: "AE_EOSG",
      description: "End of Service Gratuity (EOSG Statutory Accrual)",
      employeeAmount: 0,
      employerAmount: employerContribution,
      ratePct: 5.75,
      authority: "UAE Ministry of Human Resources and Emiratisation (MOHRE)",
    });
  }

  const net = gross - employeeDeduction;

  return {
    grossSalary: gross,
    employeeIncomeTax: 0,
    employeeStatutoryDeductions: employeeDeduction,
    employerStatutoryContributions: employerContribution,
    netSalary: net,
    currencyCode,
    countryCode: "AE",
    statutoryLines: lines,
    explanation: isExpatriate
      ? "UAE Expatriate Payroll: 0% personal income tax, MOHRE End of Service Gratuity monthly accrual."
      : "UAE National Payroll: 0% personal income tax, GPSSA pension (5% employee, 12.5% employer).",
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

  const totalEmpDed = eobiEmployee;
  const totalEmplr = eobiEmployer + sessiEmployer;
  const net = Math.max(0, Math.round((gross - monthlyTax - totalEmpDed) * 100) / 100);

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
