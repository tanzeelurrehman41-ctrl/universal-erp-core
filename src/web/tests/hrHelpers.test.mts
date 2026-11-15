import test from 'node:test';
import assert from 'node:assert/strict';
import {
  calculateAnnualIncomeTaxPkr,
  calculateMonthlyIncomeTaxPkr,
  getJurisdictionalMinimumWage,
  calculateEobiContributions,
  calculateProvincialSocialSecurity,
  calculatePfContributions,
  calculateOvertimePay,
  calculateNetPay,
  formatCnic,
  isValidCnic,
  evaluateShiftAttendance,
  canApplyLeave,
  maskCnic,
  maskBankAccount,
  maskIban,
  maskSalary,
} from '../src/hrHelpers.ts';

test('calculateAnnualIncomeTaxPkr applies 0% up to PKR 600,000 (Slab 1)', () => {
  assert.equal(calculateAnnualIncomeTaxPkr(0), 0);
  assert.equal(calculateAnnualIncomeTaxPkr(500000), 0);
  assert.equal(calculateAnnualIncomeTaxPkr(600000), 0);
});

test('calculateAnnualIncomeTaxPkr applies 1% for PKR 600,001 - 1,200,000 (Slab 2)', () => {
  // At 600,001: 1 * 0.01 = 0
  assert.equal(calculateAnnualIncomeTaxPkr(600001), 0);
  // At 900,000: 1% of 300,000 = 3,000
  assert.equal(calculateAnnualIncomeTaxPkr(900000), 3000);
  // At 1,200,000: 1% of 600,000 = 6,000
  assert.equal(calculateAnnualIncomeTaxPkr(1200000), 6000);
});

test('calculateAnnualIncomeTaxPkr applies 11% slab for PKR 1,200,001 - 2,200,000 (Slab 3)', () => {
  // At 1,700,000: 6,000 + 11% of 500,000 (55,000) = 61,000
  assert.equal(calculateAnnualIncomeTaxPkr(1700000), 61000);
  // At 2,200,000: 6,000 + 11% of 1,000,000 (110,000) = 116,000
  assert.equal(calculateAnnualIncomeTaxPkr(2200000), 116000);
});

test('calculateAnnualIncomeTaxPkr applies 20% slab for PKR 2,200,001 - 3,200,000 (Slab 4)', () => {
  // At 2,700,000: 116,000 + 20% of 500,000 (100,000) = 216,000
  assert.equal(calculateAnnualIncomeTaxPkr(2700000), 216000);
  // At 3,200,000: 116,000 + 20% of 1,000,000 (200,000) = 316,000
  assert.equal(calculateAnnualIncomeTaxPkr(3200000), 316000);
});

test('calculateAnnualIncomeTaxPkr applies 25% slab for PKR 3,200,001 - 4,100,000 (Slab 5)', () => {
  // At 3,650,000: 316,000 + 25% of 450,000 (112,500) = 428,500
  assert.equal(calculateAnnualIncomeTaxPkr(3650000), 428500);
  // At 4,100,000: 316,000 + 25% of 900,000 (225,000) = 541,000
  assert.equal(calculateAnnualIncomeTaxPkr(4100000), 541000);
});

test('calculateAnnualIncomeTaxPkr applies 29% slab for PKR 4,100,001 - 5,600,000 (Slab 6)', () => {
  // At 4,850,000: 541,000 + 29% of 750,000 (217,500) = 758,500
  assert.equal(calculateAnnualIncomeTaxPkr(4850000), 758500);
  // At 5,600,000: 541,000 + 29% of 1,500,000 (435,000) = 976,000
  assert.equal(calculateAnnualIncomeTaxPkr(5600000), 976000);
});

test('calculateAnnualIncomeTaxPkr applies 32% slab for PKR 5,600,001 - 7,000,000 (Slab 7)', () => {
  // At 6,300,000: 976,000 + 32% of 700,000 (224,000) = 1,200,000
  assert.equal(calculateAnnualIncomeTaxPkr(6300000), 1200000);
  // At 7,000,000: 976,000 + 32% of 1,400,000 (448,000) = 1,424,000
  assert.equal(calculateAnnualIncomeTaxPkr(7000000), 1424000);
});

test('calculateAnnualIncomeTaxPkr applies 35% slab exceeding PKR 7,000,000 (Slab 8) without surcharge', () => {
  // At 8,000,000: 1,424,000 + 35% of 1,000,000 (350,000) = 1,774,000
  assert.equal(calculateAnnualIncomeTaxPkr(8000000), 1774000);
  // At 10,000,000: 1,424,000 + 35% of 3,000,000 (1,050,000) = 2,474,000
  assert.equal(calculateAnnualIncomeTaxPkr(10000000), 2474000);
  // High earner at 12,000,000: 1,424,000 + 35% of 5,000,000 (1,750,000) = 3,174,000 (No obsolete surcharge)
  assert.equal(calculateAnnualIncomeTaxPkr(12000000), 3174000);
});

test('calculateMonthlyIncomeTaxPkr divides annual tax evenly across 12 months', () => {
  // Monthly 100,000 -> Annual 1,200,000 -> Annual tax 6,000 -> Monthly 500
  assert.equal(calculateMonthlyIncomeTaxPkr(100000), 500);
  // Monthly 50,000 -> Annual 600,000 -> Tax 0
  assert.equal(calculateMonthlyIncomeTaxPkr(50000), 0);
  // Monthly 0 or negative -> Tax 0
  assert.equal(calculateMonthlyIncomeTaxPkr(0), 0);
});

test('getJurisdictionalMinimumWage resolves provincial and historical rates', () => {
  assert.equal(getJurisdictionalMinimumWage('FEDERAL'), 37000);
  assert.equal(getJurisdictionalMinimumWage('PUNJAB'), 37000);
  assert.equal(getJurisdictionalMinimumWage('SINDH'), 37000);
  assert.equal(getJurisdictionalMinimumWage('BALOCHISTAN'), 37000);
  assert.equal(getJurisdictionalMinimumWage('KPK'), 36000);
  assert.equal(getJurisdictionalMinimumWage('KP'), 36000);

  // Historical prior period check (< 2026-07-01)
  assert.equal(getJurisdictionalMinimumWage('PUNJAB', '2025-06-30'), 32000);
  assert.equal(getJurisdictionalMinimumWage('KPK', '2025-06-30'), 30000);
});

test('calculateProvincialSocialSecurity computes 6% employer contribution up to wage ceiling', () => {
  // Punjab gross 50,000 -> capped at 37,000 ceiling * 6% = 2,220 ER, 0 EE
  const punjab = calculateProvincialSocialSecurity(true, 50000, 'PUNJAB');
  assert.equal(punjab.employee, 0);
  assert.equal(punjab.employer, 2220);
  assert.equal(punjab.total, 2220);

  // KP gross 50,000 -> capped at 36,000 ceiling * 6% = 2,160 ER, 0 EE
  const kp = calculateProvincialSocialSecurity(true, 50000, 'KPK');
  assert.equal(kp.employee, 0);
  assert.equal(kp.employer, 2160);
  assert.equal(kp.total, 2160);

  // Disabled social security
  const disabled = calculateProvincialSocialSecurity(false, 50000, 'PUNJAB');
  assert.equal(disabled.total, 0);
});

test('calculateEobiContributions computes 1% EE and 5% ER on statutory minimum wage', () => {
  const result = calculateEobiContributions(true, 37000);
  assert.equal(result.employee, 370);
  assert.equal(result.employer, 1850);
  assert.equal(result.total, 2220);

  const disabled = calculateEobiContributions(false, 37000);
  assert.equal(disabled.employee, 0);
  assert.equal(disabled.employer, 0);
  assert.equal(disabled.total, 0);
});

test('calculatePfContributions computes matching employee and employer contributions', () => {
  // Base 100,000 at 8.33% = 8,330 each
  const result = calculatePfContributions(true, 100000, 8.33);
  assert.equal(result.employee, 8330);
  assert.equal(result.employer, 8330);
  assert.equal(result.total, 16660);

  const disabled = calculatePfContributions(false, 100000);
  assert.equal(disabled.employee, 0);
  assert.equal(disabled.employer, 0);
  assert.equal(disabled.total, 0);
});

test('calculateOvertimePay computes double standard hourly rate', () => {
  // Base 104,000 / 208 hours = 500/hr. Double rate = 1,000/hr. 10 overtime hours = 10,000
  const ot = calculateOvertimePay(104000, 10, 2.0, 208);
  assert.equal(ot, 10000);

  assert.equal(calculateOvertimePay(104000, 0), 0);
});

test('calculateNetPay subtracts deductions from gross pay and does not go negative', () => {
  assert.equal(calculateNetPay(110000, 25000), 85000);
  assert.equal(calculateNetPay(50000, 60000), 0);
});

test('formatCnic formats 13 digits to standard Pakistani CNIC format', () => {
  assert.equal(formatCnic('4210112345671'), '42101-1234567-1');
  assert.equal(formatCnic('42101-1234567-1'), '42101-1234567-1');
  assert.equal(formatCnic('42101'), '42101');
});

test('isValidCnic verifies 13 digit CNIC requirement', () => {
  assert.equal(isValidCnic('42101-1234567-1'), true);
  assert.equal(isValidCnic('4210112345671'), true);
  assert.equal(isValidCnic('421011234567'), false);
  assert.equal(isValidCnic(''), false);
});

test('evaluateShiftAttendance identifies punctuality, late arrivals and overtime', () => {
  // On time (within 15 min grace): check-in 09:10
  const onTime = evaluateShiftAttendance('09:10', '18:00', '09:00', '18:00', 15);
  assert.equal(onTime.isLate, false);
  assert.equal(onTime.lateMinutes, 0);
  assert.equal(onTime.attendanceStatus, 'present');

  // Late arrival: check-in 09:30 (shift 09:00 + 15 min grace -> 15 min late)
  const late = evaluateShiftAttendance('09:30', '18:00', '09:00', '18:00', 15);
  assert.equal(late.isLate, true);
  assert.equal(late.lateMinutes, 15);
  assert.equal(late.attendanceStatus, 'present');

  // Overtime: check-out 19:30 (shift end 18:00 -> 90 min overtime)
  const ot = evaluateShiftAttendance('09:00', '19:30', '09:00', '18:00', 15);
  assert.equal(ot.isOvertime, true);
  assert.equal(ot.overtimeMinutes, 90);

  // Half-day: worked 3 hours (less than 4 hours)
  const halfDay = evaluateShiftAttendance('09:00', '12:00', '09:00', '18:00', 15, 4);
  assert.equal(halfDay.attendanceStatus, 'half_day');

  // Absent: no check-in
  const absent = evaluateShiftAttendance(undefined);
  assert.equal(absent.attendanceStatus, 'absent');
});

test('canApplyLeave checks allocated and used balance accurately', () => {
  // Allocated 14, used 4, requesting 3 -> allowed, remaining 7
  const check1 = canApplyLeave(14, 4, 3);
  assert.equal(check1.allowed, true);
  assert.equal(check1.remainingAfter, 7);

  // Insufficient: requesting 11 when only 10 remain
  const check2 = canApplyLeave(14, 4, 11);
  assert.equal(check2.allowed, false);
  assert.match(check2.message || '', /Insufficient leave balance/);

  // Zero days: invalid
  const check3 = canApplyLeave(14, 4, 0);
  assert.equal(check3.allowed, false);
});

test('maskCnic protects employee CNIC identity', () => {
  assert.equal(maskCnic('42101-1234567-1'), '42101-*****67-1');
  assert.equal(maskCnic('4210112345671'), '42101-*****67-1');
  assert.equal(maskCnic(null), '');
});

test('maskBankAccount masks all but last 4 digits', () => {
  assert.equal(maskBankAccount('12345678901234'), '****1234');
  assert.equal(maskBankAccount('1234'), '****');
  assert.equal(maskBankAccount(null), '');
});

test('maskIban preserves country prefix and trailing digits', () => {
  assert.equal(maskIban('PK36HABB0000123456789012'), 'PK**...****9012');
  assert.equal(maskIban(null), '');
});

test('maskSalary obscures numbers when unauthorized', () => {
  assert.equal(maskSalary(150000, false), '••••••••');
  assert.equal(maskSalary(150000, true), 'PKR 150,000');
});
