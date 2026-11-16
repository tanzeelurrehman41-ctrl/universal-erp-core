/**
 * Universal ERP — Modular Global Payroll Engine
 * Multi-country statutory payroll adapters for US, UK, UAE, Pakistan, and Global.
 */

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
 * FICA Social Security (6.2%), FICA Medicare (1.45%), Federal Income Tax withholding.
 */
export function calculateUsPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const currencyCode = request.currencyCode || "USD";
  const lines: StatutoryLine[] = [];

  // FICA Social Security (OASDI)
  const socialSecurityWageCap = 176100;
  const currentYtd = request.ytdGrossPay || 0;
  const taxableForOasdi = Math.max(0, Math.min(gross, socialSecurityWageCap - currentYtd));
  const ssEmployee = Math.round(taxableForOasdi * 0.062 * 100) / 100;
  const ssEmployer = ssEmployee;

  lines.push({
    code: "US_FICA_SS",
    description: "Social Security (FICA OASDI 6.2%)",
    employeeAmount: ssEmployee,
    employerAmount: ssEmployer,
    ratePct: 6.2,
    authority: "Internal Revenue Service (IRS) / Social Security Administration",
  });

  // FICA Medicare (HI)
  const medEmployee = Math.round(gross * 0.0145 * 100) / 100;
  const medEmployer = medEmployee;

  lines.push({
    code: "US_FICA_MED",
    description: "Medicare (FICA HI 1.45%)",
    employeeAmount: medEmployee,
    employerAmount: medEmployer,
    ratePct: 1.45,
    authority: "Internal Revenue Service (IRS)",
  });

  // Annualized Federal Income Tax withholding estimate (Single filer)
  const annualized = gross * 12;
  let annualTax = 0;
  if (annualized <= 11600) {
    annualTax = annualized * 0.10;
  } else if (annualized <= 47150) {
    annualTax = 1160 + (annualized - 11600) * 0.12;
  } else if (annualized <= 100525) {
    annualTax = 5426 + (annualized - 47150) * 0.22;
  } else if (annualized <= 191950) {
    annualTax = 17168.5 + (annualized - 100525) * 0.24;
  } else if (annualized <= 243725) {
    annualTax = 39110.5 + (annualized - 191950) * 0.32;
  } else {
    annualTax = 55678.5 + (annualized - 243725) * 0.35;
  }

  const monthlyFit = Math.round((annualTax / 12) * 100) / 100;
  lines.push({
    code: "US_FIT",
    description: "Federal Income Tax (FIT Withholding)",
    employeeAmount: monthlyFit,
    employerAmount: 0,
    ratePct: Math.round((monthlyFit / (gross || 1)) * 1000) / 10,
    authority: "Internal Revenue Service (IRS)",
  });

  const totalEmpDed = ssEmployee + medEmployee;
  const totalEmplr = ssEmployer + medEmployer;
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
    explanation: "US Federal Payroll calculated under IRS Publication 15 (Circular E).",
  };
}

/**
 * United Kingdom Payroll Adapter
 * PAYE Income Tax & Class 1 National Insurance Contributions (NIC).
 */
export function calculateUkPayroll(request: PayrollCalculationRequest): PayrollCalculationResult {
  const gross = Math.max(0, request.grossSalary);
  const currencyCode = request.currencyCode || "GBP";
  const lines: StatutoryLine[] = [];

  // Monthly thresholds
  const monthlyPersonalAllowance = 1047.5; // £12,570 / 12
  const monthlyHigherThreshold = 4189.17;  // £50,270 / 12

  // PAYE
  let payeTax = 0;
  if (gross > monthlyPersonalAllowance) {
    const taxable = gross - monthlyPersonalAllowance;
    if (taxable <= (monthlyHigherThreshold - monthlyPersonalAllowance)) {
      payeTax = taxable * 0.20;
    } else {
      const basicBand = monthlyHigherThreshold - monthlyPersonalAllowance;
      const higherTaxable = taxable - basicBand;
      payeTax = basicBand * 0.20 + higherTaxable * 0.40;
    }
  }
  payeTax = Math.round(payeTax * 100) / 100;

  lines.push({
    code: "UK_PAYE",
    description: "Pay As You Earn (PAYE Income Tax)",
    employeeAmount: payeTax,
    employerAmount: 0,
    ratePct: Math.round((payeTax / (gross || 1)) * 1000) / 10,
    authority: "HM Revenue & Customs (HMRC)",
  });

  // Class 1 Employee NIC (8% between £1,048 and £4,189; 2% above £4,189)
  let employeeNic = 0;
  if (gross > monthlyPersonalAllowance) {
    const band1 = Math.min(gross, monthlyHigherThreshold) - monthlyPersonalAllowance;
    employeeNic += band1 * 0.08;
    if (gross > monthlyHigherThreshold) {
      employeeNic += (gross - monthlyHigherThreshold) * 0.02;
    }
  }
  employeeNic = Math.round(employeeNic * 100) / 100;

  // Employer NIC (13.8% above Secondary Threshold £758/mo)
  const monthlySecondaryThreshold = 758.0;
  let employerNic = 0;
  if (gross > monthlySecondaryThreshold) {
    employerNic = Math.round((gross - monthlySecondaryThreshold) * 0.138 * 100) / 100;
  }

  lines.push({
    code: "UK_NIC",
    description: "Class 1 National Insurance Contributions (NIC)",
    employeeAmount: employeeNic,
    employerAmount: employerNic,
    ratePct: 8.0,
    authority: "HM Revenue & Customs (HMRC)",
  });

  const net = Math.max(0, Math.round((gross - payeTax - employeeNic) * 100) / 100);

  return {
    grossSalary: gross,
    employeeIncomeTax: payeTax,
    employeeStatutoryDeductions: employeeNic,
    employerStatutoryContributions: employerNic,
    netSalary: net,
    currencyCode,
    countryCode: "GB",
    statutoryLines: lines,
    explanation: "UK Payroll calculated under HMRC PAYE & National Insurance guidelines.",
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
