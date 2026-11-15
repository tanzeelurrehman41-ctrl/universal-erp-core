import test from 'node:test';
import assert from 'node:assert/strict';
import {
  calculateAnnualIncomeTaxPkr,
  calculateMonthlyIncomeTaxPkr,
  calculateEobiContributions,
  calculatePfContributions,
  calculateOvertimePay,
  calculateNetPay,
  formatCnic,
  isValidCnic,
  evaluateShiftAttendance,
  canApplyLeave,
} from '../src/hrHelpers.ts';

test('calculateAnnualIncomeTaxPkr applies 0% up to PKR 600,000', () => {
  assert.equal(calculateAnnualIncomeTaxPkr(500000), 0);
  assert.equal(calculateAnnualIncomeTaxPkr(600000), 0);
});

test('calculateAnnualIncomeTaxPkr applies 5% for PKR 600,001 - 1,200,000', () => {
  // At 900,000: 5% of 300,000 = 15,000
  assert.equal(calculateAnnualIncomeTaxPkr(900000), 15000);
  // At 1,200,000: 5% of 600,000 = 30,000
  assert.equal(calculateAnnualIncomeTaxPkr(1200000), 30000);
});

test('calculateAnnualIncomeTaxPkr applies 15% slab for PKR 1,200,001 - 2,200,000', () => {
  // At 1,800,000: 30,000 + 15% of 600,000 (90,000) = 120,000
  assert.equal(calculateAnnualIncomeTaxPkr(1800000), 120000);
  // At 2,200,000: 30,000 + 15% of 1,000,000 (150,000) = 180,000
  assert.equal(calculateAnnualIncomeTaxPkr(2200000), 180000);
});

test('calculateAnnualIncomeTaxPkr applies 25% slab for PKR 2,200,001 - 3,200,000', () => {
  // At 2,800,000: 180,000 + 25% of 600,000 (150,000) = 330,000
  assert.equal(calculateAnnualIncomeTaxPkr(2800000), 330000);
});

test('calculateAnnualIncomeTaxPkr applies 30% slab for PKR 3,200,001 - 4,100,000', () => {
  // At 3,600,000: 430,000 + 30% of 400,000 (120,000) = 550,000
  assert.equal(calculateAnnualIncomeTaxPkr(3600000), 550000);
});

test('calculateAnnualIncomeTaxPkr applies 35% slab exceeding PKR 4,100,000', () => {
  // At 5,100,000: 700,000 + 35% of 1,000,000 (350,000) = 1,050,000
  assert.equal(calculateAnnualIncomeTaxPkr(5100000), 1050000);
});

test('calculateMonthlyIncomeTaxPkr divides annual tax evenly across 12 months', () => {
  // Monthly 100,000 -> Annual 1,200,000 -> Annual tax 30,000 -> Monthly 2,500
  assert.equal(calculateMonthlyIncomeTaxPkr(100000), 2500);
  // Monthly 50,000 -> Annual 600,000 -> Tax 0
  assert.equal(calculateMonthlyIncomeTaxPkr(50000), 0);
  // Monthly 0 or negative -> Tax 0
  assert.equal(calculateMonthlyIncomeTaxPkr(0), 0);
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
