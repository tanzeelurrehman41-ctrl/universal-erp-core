/**
 * Universal ERP - Phase 11 Enterprise HR, Attendance & Payroll Helpers
 * Statutory rules, Pakistani tax slabs, EOBI, Provident Fund, Overtime, and Leave Calculations
 */

export interface SocialSecurityResult {
  employee: number;
  employer: number;
  total: number;
}

export interface EobiResult {
  employee: number;
  employer: number;
  total: number;
}

export interface PfResult {
  employee: number;
  employer: number;
  total: number;
}

export interface ShiftEvaluation {
  shiftCode: string;
  isLate: boolean;
  lateMinutes: number;
  isOvertime: boolean;
  overtimeMinutes: number;
  totalWorkedMinutes: number;
  attendanceStatus: 'present' | 'half_day' | 'absent' | 'leave';
}

export interface LeaveBalanceCheck {
  allowed: boolean;
  remainingAfter: number;
  message?: string;
}

/**
 * Calculates annual income tax under Pakistan Income Tax Ordinance, 2001
 * Official FBR Tax Year 2027 Withholding Rate Card (Finance Act 2026 - Salaried Individuals)
 * Schedule (First Schedule, Part I, Division I):
 * 1. Up to PKR 600,000: 0%
 * 2. PKR 600,001 - 1,200,000: 1% of amount exceeding PKR 600,000
 * 3. PKR 1,200,001 - 2,200,000: PKR 6,000 + 11% of amount exceeding PKR 1,200,000
 * 4. PKR 2,200,001 - 3,200,000: PKR 116,000 + 20% of amount exceeding PKR 2,200,000
 * 5. PKR 3,200,001 - 4,100,000: PKR 316,000 + 25% of amount exceeding PKR 3,200,000
 * 6. PKR 4,100,001 - 5,600,000: PKR 541,000 + 29% of amount exceeding PKR 4,100,000
 * 7. PKR 5,600,001 - 7,000,000: PKR 976,000 + 32% of amount exceeding PKR 5,600,000
 * 8. Exceeding PKR 7,000,000: PKR 1,424,000 + 35% of amount exceeding PKR 7,000,000
 */
export function calculateAnnualIncomeTaxPkr(annualTaxableIncome: number): number {
  if (annualTaxableIncome <= 600000) return 0;
  if (annualTaxableIncome <= 1200000) {
    return Math.round((annualTaxableIncome - 600000) * 0.01);
  }
  if (annualTaxableIncome <= 2200000) {
    return Math.round(6000 + (annualTaxableIncome - 1200000) * 0.11);
  }
  if (annualTaxableIncome <= 3200000) {
    return Math.round(116000 + (annualTaxableIncome - 2200000) * 0.20);
  }
  if (annualTaxableIncome <= 4100000) {
    return Math.round(316000 + (annualTaxableIncome - 3200000) * 0.25);
  }
  if (annualTaxableIncome <= 5600000) {
    return Math.round(541000 + (annualTaxableIncome - 4100000) * 0.29);
  }
  if (annualTaxableIncome <= 7000000) {
    return Math.round(976000 + (annualTaxableIncome - 5600000) * 0.32);
  }
  return Math.round(1424000 + (annualTaxableIncome - 7000000) * 0.35);
}

/**
 * Calculates monthly tax withholding in PKR based on annual salaried tax slabs.
 */
export function calculateMonthlyIncomeTaxPkr(monthlyTaxableIncome: number): number {
  if (monthlyTaxableIncome <= 0) return 0;
  const annualTax = calculateAnnualIncomeTaxPkr(monthlyTaxableIncome * 12);
  return Math.round(annualTax / 12);
}

/**
 * Resolves effective statutory minimum wage for a specific Pakistani administrative jurisdiction.
 * Supports current TY2027 rates and historical payroll periods (< 2026-07-01).
 */
export function getJurisdictionalMinimumWage(jurisdiction: string = 'FEDERAL', effectiveDate?: string): number {
  const norm = (jurisdiction || 'FEDERAL').trim().toUpperCase();
  const isHistorical = effectiveDate ? effectiveDate < '2026-07-01' : false;

  if (isHistorical) {
    if (norm === 'KPK' || norm === 'KP') return 30000;
    return 32000; // Historical Federal, Punjab, Sindh, Balochistan
  }

  switch (norm) {
    case 'KPK':
    case 'KP':
      return 36000; // Official KP notification
    case 'PUNJAB':
    case 'SINDH':
    case 'BALOCHISTAN':
    case 'ICT':
    case 'FEDERAL':
    default:
      return 37000; // Official Federal, Punjab, Sindh, Balochistan notification
  }
}

/**
 * Calculates Employees' Old-Age Benefits Institution (EOBI) contributions.
 * Standard statutory rates under EOBI Act 1976:
 * - Employee pays 1% of statutory contribution base
 * - Employer pays 5% of statutory contribution base
 */
export function calculateEobiContributions(applicable: boolean, contributionBase: number = 37000): EobiResult {
  if (!applicable || contributionBase <= 0) return { employee: 0, employer: 0, total: 0 };
  const employee = Math.round(contributionBase * 0.01);
  const employer = Math.round(contributionBase * 0.05);
  return { employee, employer, total: employee + employer };
}

/**
 * Calculates Provincial Social Security (PESSI / SESSI / KP ESSI / BESSI) contributions.
 * Under provincial labour statutes:
 * - Employer pays 6% of wages up to the statutory provincial wage ceiling
 * - Employee contribution is 0%
 */
export function calculateProvincialSocialSecurity(
  applicable: boolean,
  grossSalary: number,
  jurisdiction: string = 'FEDERAL',
  effectiveDate?: string
): SocialSecurityResult {
  if (!applicable || grossSalary <= 0) return { employee: 0, employer: 0, total: 0 };
  const ceiling = getJurisdictionalMinimumWage(jurisdiction, effectiveDate);
  const applicableWage = Math.min(grossSalary, ceiling);
  const employer = Math.round(applicableWage * 0.06);
  return { employee: 0, employer, total: employer };
}

/**
 * Calculates Provident Fund contributions (matching employee deduction and employer contribution).
 * Company-specific policy (not universal statutory mandate).
 * Standard policy rate: 8.33% (or 10%) on base salary.
 */
export function calculatePfContributions(applicable: boolean, baseSalary: number, ratePercent: number = 8.33): PfResult {
  if (!applicable || baseSalary <= 0 || ratePercent <= 0) return { employee: 0, employer: 0, total: 0 };
  const employee = Math.round(baseSalary * (ratePercent / 100));
  const employer = employee; // Employer matching contribution per company policy
  return { employee, employer, total: employee + employer };
}

/**
 * Calculates overtime pay according to Pakistan statutory standard (double the ordinary hourly rate).
 * Standard monthly work hours: 26 days * 8 hours = 208 hours.
 */
export function calculateOvertimePay(
  baseSalary: number,
  overtimeHours: number,
  multiplier: number = 2.0,
  monthlyStandardHours: number = 208
): number {
  if (baseSalary <= 0 || overtimeHours <= 0 || monthlyStandardHours <= 0) return 0;
  const hourlyRate = baseSalary / monthlyStandardHours;
  return Math.round(overtimeHours * hourlyRate * multiplier);
}

/**
 * Calculates net payable salary.
 */
export function calculateNetPay(gross: number, totalDeductions: number): number {
  return Math.max(0, Math.round(gross - totalDeductions));
}

/**
 * Formats a 13-digit Pakistani CNIC to standardized XXXXX-XXXXXXX-X format.
 */
export function formatCnic(cnic: string): string {
  if (!cnic) return '';
  const digits = cnic.replace(/\D/g, '').slice(0, 13);
  if (digits.length <= 5) return digits;
  if (digits.length <= 12) return `${digits.slice(0, 5)}-${digits.slice(5)}`;
  return `${digits.slice(0, 5)}-${digits.slice(5, 12)}-${digits.slice(12, 13)}`;
}

/**
 * Validates whether a CNIC string has 13 valid digits.
 */
export function isValidCnic(cnic: string): boolean {
  if (!cnic) return false;
  const digits = cnic.replace(/\D/g, '');
  return digits.length === 13;
}

/**
 * Evaluates shift attendance: late minutes, overtime minutes, and attendance status.
 */
export function evaluateShiftAttendance(
  checkInTimeStr?: string,
  checkOutTimeStr?: string,
  shiftStartTimeStr: string = '09:00',
  shiftEndTimeStr: string = '18:00',
  graceMinutes: number = 15,
  halfDayThresholdHours: number = 4
): ShiftEvaluation {
  if (!checkInTimeStr) {
    return {
      shiftCode: 'GEN',
      isLate: false,
      lateMinutes: 0,
      isOvertime: false,
      overtimeMinutes: 0,
      totalWorkedMinutes: 0,
      attendanceStatus: 'absent'
    };
  }

  const [inH, inM] = checkInTimeStr.split(':').map(Number);
  const [sH, sM] = shiftStartTimeStr.split(':').map(Number);
  const checkInMins = inH * 60 + (inM || 0);
  const shiftStartMins = sH * 60 + (sM || 0);

  const lateMinutes = Math.max(0, checkInMins - (shiftStartMins + graceMinutes));
  const isLate = lateMinutes > 0;

  let totalWorkedMinutes = 0;
  let overtimeMinutes = 0;

  if (checkOutTimeStr) {
    const [outH, outM] = checkOutTimeStr.split(':').map(Number);
    const [eH, eM] = shiftEndTimeStr.split(':').map(Number);
    const checkOutMins = outH * 60 + (outM || 0);
    const shiftEndMins = eH * 60 + (eM || 0);

    totalWorkedMinutes = Math.max(0, checkOutMins - checkInMins);
    overtimeMinutes = Math.max(0, checkOutMins - shiftEndMins);
  }

  let attendanceStatus: 'present' | 'half_day' | 'absent' | 'leave' = 'present';
  if (checkOutTimeStr && totalWorkedMinutes < halfDayThresholdHours * 60) {
    attendanceStatus = 'half_day';
  }

  return {
    shiftCode: 'GEN',
    isLate,
    lateMinutes,
    isOvertime: overtimeMinutes > 0,
    overtimeMinutes,
    totalWorkedMinutes,
    attendanceStatus
  };
}

/**
 * Validates leave balance availability and remaining days after application.
 */
export function canApplyLeave(
  allocatedDays: number,
  usedDays: number,
  requestedDays: number
): LeaveBalanceCheck {
  const currentRemaining = allocatedDays - usedDays;
  if (requestedDays <= 0) {
    return { allowed: false, remainingAfter: currentRemaining, message: 'Requested leave days must be greater than zero.' };
  }
  if (requestedDays > currentRemaining) {
    return {
      allowed: false,
      remainingAfter: currentRemaining,
      message: `Insufficient leave balance. Available: ${currentRemaining} days, Requested: ${requestedDays} days.`
    };
  }
  return { allowed: true, remainingAfter: currentRemaining - requestedDays };
}

/**
 * Masks a Pakistani CNIC to protect employee PII (e.g., 42101-*****67-1).
 */
export function maskCnic(cnic?: string | null): string {
  if (!cnic) return '';
  const digits = cnic.replace(/\D/g, '');
  if (digits.length !== 13) return '*****';
  return `${digits.slice(0, 5)}-*****${digits.slice(10, 12)}-${digits.slice(12, 13)}`;
}

/**
 * Masks a bank account number showing only the last 4 digits (e.g., ****1234).
 */
export function maskBankAccount(account?: string | null): string {
  if (!account) return '';
  const trimmed = account.trim();
  if (trimmed.length <= 4) return '****';
  return `****${trimmed.slice(-4)}`;
}

/**
 * Masks an IBAN preserving country code and last 4 digits (e.g., PK**...****1234).
 */
export function maskIban(iban?: string | null): string {
  if (!iban) return '';
  const clean = iban.replace(/\s+/g, '').toUpperCase();
  if (clean.length < 8) return '****';
  const prefix = clean.slice(0, 2);
  const suffix = clean.slice(-4);
  return `${prefix}**...****${suffix}`;
}

/**
 * Masks confidential salary figures unless the user holds payroll view privileges.
 */
export function maskSalary(salary: number, canViewSalary: boolean): string {
  if (!canViewSalary) return '••••••••';
  return `PKR ${salary.toLocaleString()}`;
}
