import { useState, useEffect, type FormEvent } from "react";
import { apiFetch, type TenantCompanyContext } from "./auth";
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
} from "./hrHelpers";

export type HRWorkspaceProps = {
  enabled: boolean;
  workspace?: TenantCompanyContext | null;
};

// Types matching backend DTOs
export interface DepartmentDto {
  id: string;
  code: string;
  name: string;
  costCenterCode?: string | null;
  parentDepartmentId?: string | null;
  managerEmployeeId?: string | null;
  isActive: boolean;
}

export interface DesignationDto {
  id: string;
  code: string;
  title: string;
  gradeLevel: number;
  minBaseSalaryPkr: number;
  maxBaseSalaryPkr: number;
  isActive: boolean;
}

export interface EmployeeDto {
  id: string;
  employeeCode: string;
  firstName: string;
  lastName: string;
  fullName: string;
  fatherOrHusbandName?: string | null;
  cnic: string;
  dateOfBirth?: string | null;
  gender: string;
  maritalStatus?: string | null;
  officialEmail?: string | null;
  personalEmail?: string | null;
  mobilePhone?: string | null;
  departmentId: string;
  departmentName?: string | null;
  designationId: string;
  designationTitle?: string | null;
  managerEmployeeId?: string | null;
  managerName?: string | null;
  employmentType: string;
  joinDate: string;
  status: string;
}

export interface EmployeeCompensationDto {
  id: string;
  employeeId: string;
  baseSalaryPkr: number;
  houseRentAllowancePkr: number;
  medicalAllowancePkr: number;
  utilityAllowancePkr: number;
  specialAllowancePkr: number;
  grossSalaryPkr: number;
  paymentMethod: string;
  bankName?: string | null;
  bankAccountNumber?: string | null;
  iban?: string | null;
  taxExempt: boolean;
  eobiApplicable: boolean;
  pfApplicable: boolean;
  pfRatePercent: number;
}

export interface ShiftTemplateDto {
  id: string;
  shiftCode: string;
  shiftName: string;
  startTime: string;
  endTime: string;
  gracePeriodMinutes: number;
  halfDayThresholdHours: number;
  isOvernight: boolean;
  isActive: boolean;
}

export interface AttendanceRecordDto {
  id: string;
  employeeId: string;
  employeeCode?: string | null;
  employeeName?: string | null;
  attendanceDate: string;
  checkInTime?: string | null;
  checkOutTime?: string | null;
  totalWorkedMinutes: number;
  lateMinutes: number;
  overtimeMinutes: number;
  attendanceStatus: string;
  isRegularized: boolean;
}

export interface LeaveTypeDto {
  id: string;
  code: string;
  name: string;
  isPaid: boolean;
  maxDaysPerYear: number;
  carryForwardAllowed: boolean;
  maxCarryForwardDays: number;
  isActive: boolean;
}

export interface LeaveAllocationDto {
  id: string;
  employeeId: string;
  employeeCode?: string | null;
  employeeName?: string | null;
  leaveTypeId: string;
  leaveTypeCode?: string | null;
  leaveTypeName?: string | null;
  allocationYear: number;
  allocatedDays: number;
  carriedOverDays: number;
  usedDays: number;
  remainingDays: number;
}

export interface LeaveRequestDto {
  id: string;
  employeeId: string;
  employeeCode?: string | null;
  employeeName?: string | null;
  leaveTypeId: string;
  leaveTypeCode?: string | null;
  leaveTypeName?: string | null;
  startDate: string;
  endDate: string;
  totalDays: number;
  reason?: string | null;
  status: string;
  approverRemarks?: string | null;
  createdAt: string;
}

export interface HolidayDto {
  id: string;
  holidayDate: string;
  holidayName: string;
  description?: string | null;
  isRecurring: boolean;
}

export interface SalaryLoanDto {
  id: string;
  employeeId: string;
  employeeCode?: string | null;
  employeeName?: string | null;
  loanNumber: string;
  loanType: string;
  principalAmountPkr: number;
  monthlyInstallmentPkr: number;
  repaidAmountPkr: number;
  remainingBalancePkr: number;
  totalInstallments: number;
  paidInstallments: number;
  status: string;
  createdAt: string;
}

export interface PayrollPeriodDto {
  id: string;
  periodCode: string;
  periodName: string;
  startDate: string;
  endDate: string;
  payDate: string;
  status: string;
  totalGrossPkr: number;
  totalNetPkr: number;
  glJournalId?: string | null;
}

export interface PayslipDto {
  id: string;
  payrollPeriodId: string;
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  departmentName?: string | null;
  designationTitle?: string | null;
  cnic?: string | null;
  baseSalaryPkr: number;
  allowancesPkr: number;
  overtimeHours: number;
  overtimePayPkr: number;
  bonusPkr: number;
  grossSalaryPkr: number;
  incomeTaxPkr: number;
  eobiEmployeePkr: number;
  pfEmployeePkr: number;
  loanDeductionPkr: number;
  otherDeductionsPkr: number;
  totalDeductionsPkr: number;
  netPayablePkr: number;
  eobiEmployerPkr: number;
  pfEmployerPkr: number;
  totalEmployerCostPkr: number;
  paymentMethod: string;
  status: string;
}

export default function HRWorkspace({ enabled: _enabled, workspace }: HRWorkspaceProps) {
  const [activeTab, setActiveTab] = useState<
    "employees" | "departments" | "attendance" | "leaves" | "payroll" | "payslips" | "loans" | "reports"
  >("employees");

  const [loading, setLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState("");
  const [successMessage, setSuccessMessage] = useState("");

  // Data collections
  const [employees, setEmployees] = useState<EmployeeDto[]>([]);
  const [departments, setDepartments] = useState<DepartmentDto[]>([]);
  const [designations, setDesignations] = useState<DesignationDto[]>([]);
  const [shifts, setShifts] = useState<ShiftTemplateDto[]>([]);
  const [attendanceRecords, setAttendanceRecords] = useState<AttendanceRecordDto[]>([]);
  const [leaveTypes, setLeaveTypes] = useState<LeaveTypeDto[]>([]);
  const [leaveAllocations, setLeaveAllocations] = useState<LeaveAllocationDto[]>([]);
  const [leaveRequests, setLeaveRequests] = useState<LeaveRequestDto[]>([]);
  const [holidays, setHolidays] = useState<HolidayDto[]>([]);
  const [loans, setLoans] = useState<SalaryLoanDto[]>([]);
  const [payrollPeriods, setPayrollPeriods] = useState<PayrollPeriodDto[]>([]);
  const [selectedPeriodId, setSelectedPeriodId] = useState<string>("");
  const [payslips, setPayslips] = useState<PayslipDto[]>([]);
  const [selectedPayslip, setSelectedPayslip] = useState<PayslipDto | null>(null);

  // Filters & search
  const [empSearch, setEmpSearch] = useState("");
  const [empDeptFilter, setEmpDeptFilter] = useState("");
  const [empStatusFilter, setEmpStatusFilter] = useState("");

  // Modal / Selection states
  const [showAddEmpModal, setShowAddEmpModal] = useState(false);
  const [selectedEmpForComp, setSelectedEmpForComp] = useState<EmployeeDto | null>(null);
  const [empComp, setEmpComp] = useState<EmployeeCompensationDto | null>(null);

  // New Employee Form State
  const [newEmpCode, setNewEmpCode] = useState("");
  const [newEmpFirstName, setNewEmpFirstName] = useState("");
  const [newEmpLastName, setNewEmpLastName] = useState("");
  const [newEmpFatherName, setNewEmpFatherName] = useState("");
  const [newEmpCnic, setNewEmpCnic] = useState("");
  const [newEmpGender, setNewEmpGender] = useState("male");
  const [newEmpMaritalStatus, setNewEmpMaritalStatus] = useState("single");
  const [newEmpEmail, setNewEmpEmail] = useState("");
  const [newEmpPhone, setNewEmpPhone] = useState("");
  const [newEmpDeptId, setNewEmpDeptId] = useState("");
  const [newEmpDesigId, setNewEmpDesigId] = useState("");
  const [newEmpType, setNewEmpType] = useState("permanent");
  const [newEmpJoinDate, setNewEmpJoinDate] = useState(new Date().toISOString().slice(0, 10));

  // Compensation Form State
  const [compBaseSalary, setCompBaseSalary] = useState(60000);
  const [compHouseRent, setCompHouseRent] = useState(24000);
  const [compMedical, setCompMedical] = useState(6000);
  const [compUtility, setCompUtility] = useState(5000);
  const [compSpecial, setCompSpecial] = useState(5000);
  const [compPaymentMethod, setCompPaymentMethod] = useState("bank_transfer");
  const [compBankName, setCompBankName] = useState("Habib Bank Limited (HBL)");
  const [compAccountNo, setCompAccountNo] = useState("");
  const [compIban, setCompIban] = useState("");
  const [compTaxExempt, setCompTaxExempt] = useState(false);
  const [compEobi, setCompEobi] = useState(true);
  const [compPf, setCompPf] = useState(true);
  const [compPfRate, setCompPfRate] = useState(8.33);

  // Attendance Punch State
  const [punchEmpId, setPunchEmpId] = useState("");
  const [punchDate, setPunchDate] = useState(new Date().toISOString().slice(0, 10));
  const [punchInTime, setPunchInTime] = useState("09:00");
  const [punchOutTime, setPunchOutTime] = useState("18:00");

  // Leave Form State
  const [leaveEmpId, setLeaveEmpId] = useState("");
  const [leaveTypeId, setLeaveTypeId] = useState("");
  const [leaveStartDate, setLeaveStartDate] = useState(new Date().toISOString().slice(0, 10));
  const [leaveEndDate, setLeaveEndDate] = useState(new Date().toISOString().slice(0, 10));
  const [leaveDays, setLeaveDays] = useState(1);
  const [leaveReason, setLeaveReason] = useState("");

  // Loan Form State
  const [loanEmpId, setLoanEmpId] = useState("");
  const [loanType, setLoanType] = useState("salary_advance");
  const [loanPrincipal, setLoanPrincipal] = useState(50000);
  const [loanInstallment, setLoanInstallment] = useState(10000);
  const [loanReason, setLoanReason] = useState("");

  // Payroll Period Form State
  const [newPeriodCode, setNewPeriodCode] = useState(`2026-${String(new Date().getMonth() + 1).padStart(2, "0")}`);
  const [newPeriodName, setNewPeriodName] = useState(`Payroll Period ${new Date().toLocaleDateString("en-US", { month: "short", year: "numeric" })}`);
  const [newPeriodStart, setNewPeriodStart] = useState("2026-11-01");
  const [newPeriodEnd, setNewPeriodEnd] = useState("2026-11-30");
  const [newPeriodPayDate, setNewPeriodPayDate] = useState("2026-11-30");

  // Initial Data Fetch
  useEffect(() => {
    loadAllData();
  }, [workspace]);

  async function loadAllData() {
    setLoading(true);
    setErrorMessage("");
    try {
      await Promise.all([
        fetchEmployees(),
        fetchDepartments(),
        fetchDesignations(),
        fetchShifts(),
        fetchAttendance(),
        fetchLeaveTypes(),
        fetchLeaveAllocations(),
        fetchLeaveRequests(),
        fetchHolidays(),
        fetchLoans(),
        fetchPayrollPeriods(),
      ]);
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to load HR data");
    } finally {
      setLoading(false);
    }
  }

  async function fetchEmployees() {
    const res = await apiFetch("/api/v1/hr/employees");
    if (res.ok) {
      const data = await res.json();
      setEmployees(data || []);
      if (data && data.length > 0 && !punchEmpId) setPunchEmpId(data[0].id);
      if (data && data.length > 0 && !leaveEmpId) setLeaveEmpId(data[0].id);
      if (data && data.length > 0 && !loanEmpId) setLoanEmpId(data[0].id);
    }
  }

  async function fetchDepartments() {
    const res = await apiFetch("/api/v1/hr/departments");
    if (res.ok) setDepartments((await res.json()) || []);
  }

  async function fetchDesignations() {
    const res = await apiFetch("/api/v1/hr/designations");
    if (res.ok) setDesignations((await res.json()) || []);
  }

  async function fetchShifts() {
    const res = await apiFetch("/api/v1/hr/attendance/shifts");
    if (res.ok) setShifts((await res.json()) || []);
  }

  async function fetchAttendance() {
    const res = await apiFetch("/api/v1/hr/attendance/records");
    if (res.ok) setAttendanceRecords((await res.json()) || []);
  }

  async function fetchLeaveTypes() {
    const res = await apiFetch("/api/v1/hr/leave/types");
    if (res.ok) {
      const data = await res.json();
      setLeaveTypes(data || []);
      if (data && data.length > 0 && !leaveTypeId) setLeaveTypeId(data[0].id);
    }
  }

  async function fetchLeaveAllocations() {
    const res = await apiFetch("/api/v1/hr/leave/allocations");
    if (res.ok) setLeaveAllocations((await res.json()) || []);
  }

  async function fetchLeaveRequests() {
    const res = await apiFetch("/api/v1/hr/leave/requests");
    if (res.ok) setLeaveRequests((await res.json()) || []);
  }

  async function fetchHolidays() {
    const res = await apiFetch("/api/v1/hr/leave/holidays");
    if (res.ok) setHolidays((await res.json()) || []);
  }

  async function fetchLoans() {
    const res = await apiFetch("/api/v1/hr/loans");
    if (res.ok) setLoans((await res.json()) || []);
  }

  async function fetchPayrollPeriods() {
    const res = await apiFetch("/api/v1/payroll/periods");
    if (res.ok) {
      const data = await res.json();
      setPayrollPeriods(data || []);
      if (data && data.length > 0) {
        setSelectedPeriodId(data[0].id);
        fetchPayslipsForPeriod(data[0].id);
      }
    }
  }

  async function fetchPayslipsForPeriod(periodId: string) {
    if (!periodId) return;
    const res = await apiFetch(`/api/v1/payroll/periods/${periodId}/payslips`);
    if (res.ok) {
      setPayslips((await res.json()) || []);
    }
  }

  // Action handlers
  async function handleCreateEmployee(e: FormEvent) {
    e.preventDefault();
    setErrorMessage("");
    setSuccessMessage("");
    if (!isValidCnic(newEmpCnic)) {
      setErrorMessage("CNIC must contain 13 digits (e.g. 42101-1234567-1).");
      return;
    }
    try {
      const body = {
        employeeCode: newEmpCode,
        firstName: newEmpFirstName,
        lastName: newEmpLastName,
        fatherOrHusbandName: newEmpFatherName || null,
        cnic: formatCnic(newEmpCnic),
        gender: newEmpGender,
        maritalStatus: newEmpMaritalStatus,
        officialEmail: newEmpEmail || null,
        mobilePhone: newEmpPhone || null,
        departmentId: newEmpDeptId,
        designationId: newEmpDesigId,
        employmentType: newEmpType,
        joinDate: newEmpJoinDate,
      };
      const res = await apiFetch("/api/v1/hr/employees", {
        method: "POST",
        body: JSON.stringify(body),
      });
      if (!res.ok) {
        const err = await res.text();
        throw new Error(err || "Failed to create employee.");
      }
      setSuccessMessage("Employee created successfully!");
      setShowAddEmpModal(false);
      await fetchEmployees();
      // Reset form
      setNewEmpCode("");
      setNewEmpFirstName("");
      setNewEmpLastName("");
      setNewEmpCnic("");
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Error creating employee.");
    }
  }

  async function openCompensationModal(emp: EmployeeDto) {
    setSelectedEmpForComp(emp);
    setErrorMessage("");
    try {
      const res = await apiFetch(`/api/v1/hr/employees/${emp.id}/compensation`);
      if (res.ok) {
        const comp: EmployeeCompensationDto = await res.json();
        setEmpComp(comp);
        setCompBaseSalary(comp.baseSalaryPkr);
        setCompHouseRent(comp.houseRentAllowancePkr);
        setCompMedical(comp.medicalAllowancePkr);
        setCompUtility(comp.utilityAllowancePkr);
        setCompSpecial(comp.specialAllowancePkr);
        setCompPaymentMethod(comp.paymentMethod);
        setCompBankName(comp.bankName || "");
        setCompAccountNo(comp.bankAccountNumber || "");
        setCompIban(comp.iban || "");
        setCompTaxExempt(comp.taxExempt);
        setCompEobi(comp.eobiApplicable);
        setCompPf(comp.pfApplicable);
        setCompPfRate(comp.pfRatePercent);
      } else {
        setEmpComp(null);
      }
    } catch {
      setEmpComp(null);
    }
  }

  async function handleSaveCompensation(e: FormEvent) {
    e.preventDefault();
    if (!selectedEmpForComp) return;
    setErrorMessage("");
    setSuccessMessage("");
    try {
      const body = {
        baseSalaryPkr: Number(compBaseSalary),
        houseRentAllowancePkr: Number(compHouseRent),
        medicalAllowancePkr: Number(compMedical),
        utilityAllowancePkr: Number(compUtility),
        specialAllowancePkr: Number(compSpecial),
        paymentMethod: compPaymentMethod,
        bankName: compBankName || null,
        bankAccountNumber: compAccountNo || null,
        iban: compIban || null,
        taxExempt: compTaxExempt,
        eobiApplicable: compEobi,
        pfApplicable: compPf,
        pfRatePercent: Number(compPfRate),
      };
      const res = await apiFetch(`/api/v1/hr/employees/${selectedEmpForComp.id}/compensation`, {
        method: "PUT",
        body: JSON.stringify(body),
      });
      if (!res.ok) throw new Error(await res.text());
      setSuccessMessage(`Compensation updated for ${selectedEmpForComp.fullName}!`);
      setSelectedEmpForComp(null);
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to update compensation.");
    }
  }

  async function handleRecordPunch(e: FormEvent) {
    e.preventDefault();
    if (!punchEmpId) return;
    setErrorMessage("");
    setSuccessMessage("");
    try {
      const evaluation = evaluateShiftAttendance(punchInTime, punchOutTime);
      const body = {
        employeeId: punchEmpId,
        attendanceDate: punchDate,
        checkInTime: punchInTime ? `${punchDate}T${punchInTime}:00` : null,
        checkOutTime: punchOutTime ? `${punchDate}T${punchOutTime}:00` : null,
        punchSource: "web_portal",
      };
      const res = await apiFetch("/api/v1/hr/attendance/records", {
        method: "POST",
        body: JSON.stringify(body),
      });
      if (!res.ok) throw new Error(await res.text());
      setSuccessMessage(
        `Attendance recorded! Status: ${evaluation.attendanceStatus.toUpperCase()}, Late: ${evaluation.lateMinutes} mins, OT: ${evaluation.overtimeMinutes} mins.`
      );
      await fetchAttendance();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to record attendance.");
    }
  }

  async function handleApplyLeave(e: FormEvent) {
    e.preventDefault();
    if (!leaveEmpId || !leaveTypeId) return;
    setErrorMessage("");
    setSuccessMessage("");

    // Check balance
    const alloc = leaveAllocations.find(
      (a) => a.employeeId === leaveEmpId && a.leaveTypeId === leaveTypeId
    );
    if (alloc) {
      const check = canApplyLeave(alloc.allocatedDays + alloc.carriedOverDays, alloc.usedDays, Number(leaveDays));
      if (!check.allowed) {
        setErrorMessage(check.message || "Insufficient leave balance.");
        return;
      }
    }

    try {
      const body = {
        employeeId: leaveEmpId,
        leaveTypeId: leaveTypeId,
        startDate: leaveStartDate,
        endDate: leaveEndDate,
        totalDays: Number(leaveDays),
        reason: leaveReason || "Personal",
      };
      const res = await apiFetch("/api/v1/hr/leave/requests", {
        method: "POST",
        body: JSON.stringify(body),
      });
      if (!res.ok) throw new Error(await res.text());
      setSuccessMessage("Leave application submitted successfully!");
      setLeaveReason("");
      await fetchLeaveRequests();
      await fetchLeaveAllocations();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to submit leave application.");
    }
  }

  async function handleReviewLeave(requestId: string, status: "approved" | "rejected") {
    setErrorMessage("");
    setSuccessMessage("");
    try {
      const res = await apiFetch(`/api/v1/hr/leave/requests/${requestId}/review`, {
        method: "POST",
        body: JSON.stringify({ status, remarks: `Action taken via web workspace (${status})` }),
      });
      if (!res.ok) throw new Error(await res.text());
      setSuccessMessage(`Leave request marked as ${status}!`);
      await fetchLeaveRequests();
      await fetchLeaveAllocations();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : `Failed to ${status} leave.`);
    }
  }

  async function handleIssueLoan(e: FormEvent) {
    e.preventDefault();
    if (!loanEmpId) return;
    setErrorMessage("");
    setSuccessMessage("");
    try {
      const body = {
        employeeId: loanEmpId,
        loanType: loanType,
        principalAmountPkr: Number(loanPrincipal),
        monthlyInstallmentPkr: Number(loanInstallment),
        reason: loanReason || "Personal loan request",
      };
      const res = await apiFetch("/api/v1/hr/loans", {
        method: "POST",
        body: JSON.stringify(body),
      });
      if (!res.ok) throw new Error(await res.text());
      setSuccessMessage("Staff loan approved and issued successfully!");
      setLoanReason("");
      await fetchLoans();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to issue loan.");
    }
  }

  async function handleCreatePayrollPeriod(e: FormEvent) {
    e.preventDefault();
    setErrorMessage("");
    setSuccessMessage("");
    try {
      const body = {
        periodCode: newPeriodCode,
        periodName: newPeriodName,
        startDate: newPeriodStart,
        endDate: newPeriodEnd,
        payDate: newPeriodPayDate,
      };
      const res = await apiFetch("/api/v1/payroll/periods", {
        method: "POST",
        body: JSON.stringify(body),
      });
      if (!res.ok) throw new Error(await res.text());
      const created = await res.json();
      setSuccessMessage(`Payroll period ${newPeriodCode} initialized!`);
      await fetchPayrollPeriods();
      setSelectedPeriodId(created.id);
      fetchPayslipsForPeriod(created.id);
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to create payroll period.");
    }
  }

  async function handleCalculatePayroll(periodId: string) {
    setLoading(true);
    setErrorMessage("");
    setSuccessMessage("");
    try {
      const res = await apiFetch(`/api/v1/payroll/periods/${periodId}/calculate`, {
        method: "POST",
      });
      if (!res.ok) throw new Error(await res.text());
      const period = await res.json();
      setSuccessMessage(`Payroll calculated! Gross: PKR ${period.totalGrossPkr.toLocaleString()}, Net: PKR ${period.totalNetPkr.toLocaleString()}.`);
      await fetchPayrollPeriods();
      await fetchPayslipsForPeriod(periodId);
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to calculate payroll.");
    } finally {
      setLoading(false);
    }
  }

  async function handleApprovePayroll(periodId: string) {
    setLoading(true);
    setErrorMessage("");
    setSuccessMessage("");
    try {
      const res = await apiFetch(`/api/v1/payroll/periods/${periodId}/approve`, {
        method: "POST",
      });
      if (!res.ok) throw new Error(await res.text());
      setSuccessMessage("Payroll run approved successfully!");
      await fetchPayrollPeriods();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to approve payroll.");
    } finally {
      setLoading(false);
    }
  }

  async function handlePostGlPayroll(periodId: string) {
    setLoading(true);
    setErrorMessage("");
    setSuccessMessage("");
    try {
      const res = await apiFetch(`/api/v1/payroll/periods/${periodId}/post-gl`, {
        method: "POST",
        body: JSON.stringify({
          journalDate: new Date().toISOString().slice(0, 10),
          description: "Monthly Payroll Expense & Statutory Liabilities Posting",
        }),
      });
      if (!res.ok) throw new Error(await res.text());
      setSuccessMessage("Balanced double-entry journal posted into General Ledger! Salaries Payable, Tax Withholding, EOBI and PF recorded.");
      await fetchPayrollPeriods();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to post payroll to GL.");
    } finally {
      setLoading(false);
    }
  }

  async function handleReversePayroll(periodId: string) {
    const reason = prompt("Enter reason for reversing this payroll run:");
    if (!reason) return;
    setLoading(true);
    setErrorMessage("");
    setSuccessMessage("");
    try {
      const res = await apiFetch("/api/v1/payroll/reversals", {
        method: "POST",
        body: JSON.stringify({
          payrollPeriodId: periodId,
          reason: reason,
        }),
      });
      if (!res.ok) throw new Error(await res.text());
      setSuccessMessage("Payroll run reversed! Reversing GL journal created with swapped debits & credits.");
      await fetchPayrollPeriods();
      await fetchPayslipsForPeriod(periodId);
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to reverse payroll run.");
    } finally {
      setLoading(false);
    }
  }

  // Filtered employees
  const filteredEmployees = employees.filter((emp) => {
    const matchesSearch =
      !empSearch ||
      emp.fullName.toLowerCase().includes(empSearch.toLowerCase()) ||
      emp.employeeCode.toLowerCase().includes(empSearch.toLowerCase()) ||
      emp.cnic.includes(empSearch);
    const matchesDept = !empDeptFilter || emp.departmentId === empDeptFilter;
    const matchesStatus = !empStatusFilter || emp.status === empStatusFilter;
    return matchesSearch && matchesDept && matchesStatus;
  });

  const selectedPeriod = payrollPeriods.find((p) => p.id === selectedPeriodId);

  return (
    <div style={{ padding: "20px", fontFamily: "system-ui, -apple-system, sans-serif" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "20px" }}>
        <div>
          <h1 style={{ margin: "0 0 6px 0", fontSize: "24px", color: "#0f172a" }}>
            👥 Enterprise HR, Attendance & Payroll
          </h1>
          <p style={{ margin: 0, color: "#64748b", fontSize: "14px" }}>
            Statutory Pakistani Tax Slabs (2024-25), EOBI (1% EE / 5% ER), Provident Fund, Biometric Attendance, and General Ledger balanced posting.
          </p>
        </div>
        <button
          onClick={loadAllData}
          disabled={loading}
          style={{
            padding: "8px 16px",
            background: "#f1f5f9",
            border: "1px solid #cbd5e1",
            borderRadius: "6px",
            cursor: "pointer",
            fontWeight: 600,
          }}
        >
          {loading ? "Refreshing…" : "🔄 Refresh"}
        </button>
      </div>

      {/* Messages */}
      {errorMessage && (
        <div style={{ padding: "12px 16px", background: "#fee2e2", border: "1px solid #ef4444", borderRadius: "6px", color: "#991b1b", marginBottom: "16px", fontSize: "14px" }}>
          ❌ {errorMessage}
        </div>
      )}
      {successMessage && (
        <div style={{ padding: "12px 16px", background: "#dcfce7", border: "1px solid #22c55e", borderRadius: "6px", color: "#166534", marginBottom: "16px", fontSize: "14px" }}>
          ✅ {successMessage}
        </div>
      )}

      {/* Tabs */}
      <div style={{ display: "flex", gap: "8px", borderBottom: "2px solid #e2e8f0", paddingBottom: "8px", marginBottom: "20px", flexWrap: "wrap" }}>
        {[
          { id: "employees", label: "👨‍💼 Employee Directory" },
          { id: "departments", label: "🏢 Org & Hierarchy" },
          { id: "attendance", label: "⏱️ Attendance & Shifts" },
          { id: "leaves", label: "🏖️ Leave Management" },
          { id: "payroll", label: "💵 Payroll Engine" },
          { id: "payslips", label: "📄 Payslips & Breakdown" },
          { id: "loans", label: "💳 Loans & Advances" },
          { id: "reports", label: "📊 Statutory Reports" },
        ].map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id as typeof activeTab)}
            style={{
              padding: "10px 16px",
              borderRadius: "6px",
              border: "none",
              background: activeTab === tab.id ? "#2563eb" : "#f1f5f9",
              color: activeTab === tab.id ? "#ffffff" : "#475569",
              fontWeight: 600,
              cursor: "pointer",
              fontSize: "13px",
            }}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* TAB 1: EMPLOYEES */}
      {activeTab === "employees" && (
        <div>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "16px", flexWrap: "wrap", gap: "10px" }}>
            <div style={{ display: "flex", gap: "10px", alignItems: "center" }}>
              <input
                type="text"
                placeholder="Search name, code, CNIC…"
                value={empSearch}
                onChange={(e) => setEmpSearch(e.target.value)}
                style={{ padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px", minWidth: "240px" }}
              />
              <select
                value={empDeptFilter}
                onChange={(e) => setEmpDeptFilter(e.target.value)}
                style={{ padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
              >
                <option value="">All Departments</option>
                {departments.map((d) => (
                  <option key={d.id} value={d.id}>{d.name}</option>
                ))}
              </select>
              <select
                value={empStatusFilter}
                onChange={(e) => setEmpStatusFilter(e.target.value)}
                style={{ padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
              >
                <option value="">All Statuses</option>
                <option value="active">Active</option>
                <option value="on_leave">On Leave</option>
                <option value="probation">Probation</option>
                <option value="terminated">Terminated</option>
              </select>
            </div>
            <button
              onClick={() => setShowAddEmpModal(true)}
              style={{
                padding: "8px 16px",
                background: "#2563eb",
                color: "#fff",
                border: "none",
                borderRadius: "6px",
                cursor: "pointer",
                fontWeight: 600,
              }}
            >
              + Add Employee
            </button>
          </div>

          <table style={{ width: "100%", borderCollapse: "collapse", background: "#fff", border: "1px solid #e2e8f0", borderRadius: "6px" }}>
            <thead>
              <tr style={{ background: "#f8fafc", textAlign: "left", fontSize: "12px", color: "#64748b", borderBottom: "1px solid #e2e8f0" }}>
                <th style={{ padding: "10px 14px" }}>Code</th>
                <th style={{ padding: "10px 14px" }}>Name</th>
                <th style={{ padding: "10px 14px" }}>CNIC</th>
                <th style={{ padding: "10px 14px" }}>Department</th>
                <th style={{ padding: "10px 14px" }}>Designation</th>
                <th style={{ padding: "10px 14px" }}>Type</th>
                <th style={{ padding: "10px 14px" }}>Status</th>
                <th style={{ padding: "10px 14px" }}>Joined</th>
                <th style={{ padding: "10px 14px" }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredEmployees.length === 0 ? (
                <tr>
                  <td colSpan={9} style={{ padding: "24px", textAlign: "center", color: "#94a3b8" }}>
                    No employees found matching criteria.
                  </td>
                </tr>
              ) : (
                filteredEmployees.map((emp) => (
                  <tr key={emp.id} style={{ borderBottom: "1px solid #f1f5f9", fontSize: "13px" }}>
                    <td style={{ padding: "10px 14px", fontWeight: 600, color: "#1e293b" }}>{emp.employeeCode}</td>
                    <td style={{ padding: "10px 14px" }}>{emp.fullName}</td>
                    <td style={{ padding: "10px 14px", fontFamily: "monospace" }}>{formatCnic(emp.cnic)}</td>
                    <td style={{ padding: "10px 14px" }}>{emp.departmentName || "—"}</td>
                    <td style={{ padding: "10px 14px" }}>{emp.designationTitle || "—"}</td>
                    <td style={{ padding: "10px 14px" }}>
                      <span style={{ textTransform: "capitalize" }}>{emp.employmentType}</span>
                    </td>
                    <td style={{ padding: "10px 14px" }}>
                      <span
                        style={{
                          padding: "3px 8px",
                          borderRadius: "12px",
                          fontSize: "11px",
                          fontWeight: 600,
                          background: emp.status === "active" ? "#dcfce7" : "#fee2e2",
                          color: emp.status === "active" ? "#166534" : "#991b1b",
                        }}
                      >
                        {emp.status.toUpperCase()}
                      </span>
                    </td>
                    <td style={{ padding: "10px 14px", color: "#64748b" }}>{emp.joinDate.slice(0, 10)}</td>
                    <td style={{ padding: "10px 14px" }}>
                      <button
                        onClick={() => openCompensationModal(emp)}
                        style={{
                          padding: "4px 8px",
                          background: "#f1f5f9",
                          border: "1px solid #cbd5e1",
                          borderRadius: "4px",
                          cursor: "pointer",
                          fontSize: "11px",
                          fontWeight: 600,
                        }}
                      >
                        ⚙️ Salary & Package
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* TAB 2: DEPARTMENTS & DESIGNATIONS */}
      {activeTab === "departments" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "20px" }}>
          {/* Departments */}
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>🏢 Departments</h3>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
              <thead>
                <tr style={{ background: "#f8fafc", textAlign: "left", color: "#64748b" }}>
                  <th style={{ padding: "8px" }}>Code</th>
                  <th style={{ padding: "8px" }}>Name</th>
                  <th style={{ padding: "8px" }}>Cost Center</th>
                  <th style={{ padding: "8px" }}>Status</th>
                </tr>
              </thead>
              <tbody>
                {departments.map((d) => (
                  <tr key={d.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                    <td style={{ padding: "8px", fontWeight: 600 }}>{d.code}</td>
                    <td style={{ padding: "8px" }}>{d.name}</td>
                    <td style={{ padding: "8px", color: "#64748b" }}>{d.costCenterCode || "CC-CORP"}</td>
                    <td style={{ padding: "8px" }}>
                      <span style={{ color: d.isActive ? "#166534" : "#991b1b" }}>
                        {d.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Designations */}
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>🎖️ Designations & Grades</h3>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
              <thead>
                <tr style={{ background: "#f8fafc", textAlign: "left", color: "#64748b" }}>
                  <th style={{ padding: "8px" }}>Code</th>
                  <th style={{ padding: "8px" }}>Title</th>
                  <th style={{ padding: "8px" }}>Grade Level</th>
                  <th style={{ padding: "8px" }}>Base Salary Band</th>
                </tr>
              </thead>
              <tbody>
                {designations.map((d) => (
                  <tr key={d.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                    <td style={{ padding: "8px", fontWeight: 600 }}>{d.code}</td>
                    <td style={{ padding: "8px" }}>{d.title}</td>
                    <td style={{ padding: "8px" }}>Level {d.gradeLevel}</td>
                    <td style={{ padding: "8px", color: "#64748b" }}>
                      PKR {d.minBaseSalaryPkr.toLocaleString()} – {d.maxBaseSalaryPkr.toLocaleString()}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* TAB 3: ATTENDANCE & SHIFTS */}
      {activeTab === "attendance" && (
        <div style={{ display: "grid", gridTemplateColumns: "350px 1fr", gap: "20px" }}>
          {/* Check-in Form */}
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>⏱️ Web Attendance Check-in</h3>
            <form onSubmit={handleRecordPunch} style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
              <div>
                <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Employee</label>
                <select
                  value={punchEmpId}
                  onChange={(e) => setPunchEmpId(e.target.value)}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  required
                >
                  {employees.map((emp) => (
                    <option key={emp.id} value={emp.id}>{emp.employeeCode} - {emp.fullName}</option>
                  ))}
                </select>
              </div>

              <div>
                <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Date</label>
                <input
                  type="date"
                  value={punchDate}
                  onChange={(e) => setPunchDate(e.target.value)}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  required
                />
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Check In (HH:mm)</label>
                  <input
                    type="time"
                    value={punchInTime}
                    onChange={(e) => setPunchInTime(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Check Out (HH:mm)</label>
                  <input
                    type="time"
                    value={punchOutTime}
                    onChange={(e) => setPunchOutTime(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  />
                </div>
              </div>

              <button
                type="submit"
                style={{
                  marginTop: "8px",
                  padding: "10px",
                  background: "#2563eb",
                  color: "#fff",
                  border: "none",
                  borderRadius: "6px",
                  cursor: "pointer",
                  fontWeight: 600,
                }}
              >
                Record Attendance
              </button>
            </form>
          </div>

          {/* Attendance Log */}
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>📋 Attendance Logs & Punctuality</h3>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
              <thead>
                <tr style={{ background: "#f8fafc", textAlign: "left", color: "#64748b" }}>
                  <th style={{ padding: "8px" }}>Employee</th>
                  <th style={{ padding: "8px" }}>Date</th>
                  <th style={{ padding: "8px" }}>In / Out</th>
                  <th style={{ padding: "8px" }}>Worked</th>
                  <th style={{ padding: "8px" }}>Late</th>
                  <th style={{ padding: "8px" }}>Overtime</th>
                  <th style={{ padding: "8px" }}>Status</th>
                </tr>
              </thead>
              <tbody>
                {attendanceRecords.length === 0 ? (
                  <tr>
                    <td colSpan={7} style={{ padding: "20px", textAlign: "center", color: "#94a3b8" }}>
                      No attendance records found.
                    </td>
                  </tr>
                ) : (
                  attendanceRecords.map((r) => (
                    <tr key={r.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                      <td style={{ padding: "8px", fontWeight: 600 }}>{r.employeeName || r.employeeCode}</td>
                      <td style={{ padding: "8px" }}>{r.attendanceDate.slice(0, 10)}</td>
                      <td style={{ padding: "8px", fontFamily: "monospace" }}>
                        {r.checkInTime ? r.checkInTime.slice(11, 16) : "—"} / {r.checkOutTime ? r.checkOutTime.slice(11, 16) : "—"}
                      </td>
                      <td style={{ padding: "8px" }}>{Math.floor(r.totalWorkedMinutes / 60)}h {r.totalWorkedMinutes % 60}m</td>
                      <td style={{ padding: "8px", color: r.lateMinutes > 0 ? "#b91c1c" : "#166534" }}>
                        {r.lateMinutes > 0 ? `+${r.lateMinutes}m` : "On time"}
                      </td>
                      <td style={{ padding: "8px", color: r.overtimeMinutes > 0 ? "#15803d" : "#64748b" }}>
                        {r.overtimeMinutes > 0 ? `${r.overtimeMinutes}m` : "—"}
                      </td>
                      <td style={{ padding: "8px" }}>
                        <span
                          style={{
                            padding: "2px 6px",
                            borderRadius: "10px",
                            fontSize: "11px",
                            fontWeight: 600,
                            background: r.attendanceStatus === "present" ? "#dcfce7" : "#fef3c7",
                            color: r.attendanceStatus === "present" ? "#166534" : "#92400e",
                          }}
                        >
                          {r.attendanceStatus.toUpperCase()}
                        </span>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* TAB 4: LEAVE MANAGEMENT */}
      {activeTab === "leaves" && (
        <div style={{ display: "grid", gridTemplateColumns: "340px 1fr", gap: "20px" }}>
          {/* Apply Leave Form */}
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>🏖️ Apply for Leave</h3>
            <form onSubmit={handleApplyLeave} style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
              <div>
                <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Employee</label>
                <select
                  value={leaveEmpId}
                  onChange={(e) => setLeaveEmpId(e.target.value)}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  required
                >
                  {employees.map((emp) => (
                    <option key={emp.id} value={emp.id}>{emp.employeeCode} - {emp.fullName}</option>
                  ))}
                </select>
              </div>

              <div>
                <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Leave Type</label>
                <select
                  value={leaveTypeId}
                  onChange={(e) => setLeaveTypeId(e.target.value)}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  required
                >
                  {leaveTypes.map((t) => (
                    <option key={t.id} value={t.id}>{t.name} ({t.maxDaysPerYear} days/yr)</option>
                  ))}
                </select>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Start Date</label>
                  <input
                    type="date"
                    value={leaveStartDate}
                    onChange={(e) => setLeaveStartDate(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                    required
                  />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>End Date</label>
                  <input
                    type="date"
                    value={leaveEndDate}
                    onChange={(e) => setLeaveEndDate(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                    required
                  />
                </div>
              </div>

              <div>
                <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Total Days</label>
                <input
                  type="number"
                  min="1"
                  max="60"
                  value={leaveDays}
                  onChange={(e) => setLeaveDays(Number(e.target.value))}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  required
                />
              </div>

              <div>
                <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Reason</label>
                <input
                  type="text"
                  placeholder="Medical, family emergency, etc."
                  value={leaveReason}
                  onChange={(e) => setLeaveReason(e.target.value)}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                />
              </div>

              <button
                type="submit"
                style={{
                  marginTop: "8px",
                  padding: "10px",
                  background: "#2563eb",
                  color: "#fff",
                  border: "none",
                  borderRadius: "6px",
                  cursor: "pointer",
                  fontWeight: 600,
                }}
              >
                Submit Leave Application
              </button>
            </form>
          </div>

          {/* Leave Requests & Balances */}
          <div style={{ display: "flex", flexDirection: "column", gap: "20px" }}>
            {/* Requests */}
            <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
              <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>📝 Pending & Reviewed Leave Requests</h3>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
                <thead>
                  <tr style={{ background: "#f8fafc", textAlign: "left", color: "#64748b" }}>
                    <th style={{ padding: "8px" }}>Employee</th>
                    <th style={{ padding: "8px" }}>Type</th>
                    <th style={{ padding: "8px" }}>Dates</th>
                    <th style={{ padding: "8px" }}>Days</th>
                    <th style={{ padding: "8px" }}>Status</th>
                    <th style={{ padding: "8px" }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {leaveRequests.length === 0 ? (
                    <tr>
                      <td colSpan={6} style={{ padding: "16px", textAlign: "center", color: "#94a3b8" }}>
                        No leave requests submitted.
                      </td>
                    </tr>
                  ) : (
                    leaveRequests.map((req) => (
                      <tr key={req.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                        <td style={{ padding: "8px", fontWeight: 600 }}>{req.employeeName || req.employeeCode}</td>
                        <td style={{ padding: "8px" }}>{req.leaveTypeName || req.leaveTypeCode}</td>
                        <td style={{ padding: "8px" }}>{req.startDate.slice(0, 10)} to {req.endDate.slice(0, 10)}</td>
                        <td style={{ padding: "8px" }}>{req.totalDays} days</td>
                        <td style={{ padding: "8px" }}>
                          <span
                            style={{
                              padding: "2px 6px",
                              borderRadius: "10px",
                              fontSize: "11px",
                              fontWeight: 600,
                              background:
                                req.status === "approved" ? "#dcfce7" : req.status === "rejected" ? "#fee2e2" : "#fef3c7",
                              color:
                                req.status === "approved" ? "#166534" : req.status === "rejected" ? "#991b1b" : "#92400e",
                            }}
                          >
                            {req.status.toUpperCase()}
                          </span>
                        </td>
                        <td style={{ padding: "8px" }}>
                          {req.status === "pending" && (
                            <div style={{ display: "flex", gap: "6px" }}>
                              <button
                                onClick={() => handleReviewLeave(req.id, "approved")}
                                style={{
                                  padding: "3px 8px",
                                  background: "#dcfce7",
                                  border: "1px solid #22c55e",
                                  color: "#166534",
                                  borderRadius: "4px",
                                  cursor: "pointer",
                                  fontSize: "11px",
                                  fontWeight: 600,
                                }}
                              >
                                Approve
                              </button>
                              <button
                                onClick={() => handleReviewLeave(req.id, "rejected")}
                                style={{
                                  padding: "3px 8px",
                                  background: "#fee2e2",
                                  border: "1px solid #ef4444",
                                  color: "#991b1b",
                                  borderRadius: "4px",
                                  cursor: "pointer",
                                  fontSize: "11px",
                                  fontWeight: 600,
                                }}
                              >
                                Reject
                              </button>
                            </div>
                          )}
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            {/* Leave Balances */}
            <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
              <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>📊 Leave Allocation & Balances</h3>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
                <thead>
                  <tr style={{ background: "#f8fafc", textAlign: "left", color: "#64748b" }}>
                    <th style={{ padding: "8px" }}>Employee</th>
                    <th style={{ padding: "8px" }}>Type</th>
                    <th style={{ padding: "8px" }}>Year</th>
                    <th style={{ padding: "8px" }}>Allocated</th>
                    <th style={{ padding: "8px" }}>Used</th>
                    <th style={{ padding: "8px" }}>Remaining</th>
                  </tr>
                </thead>
                <tbody>
                  {leaveAllocations.map((a) => (
                    <tr key={a.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                      <td style={{ padding: "8px", fontWeight: 600 }}>{a.employeeName || a.employeeCode}</td>
                      <td style={{ padding: "8px" }}>{a.leaveTypeName || a.leaveTypeCode}</td>
                      <td style={{ padding: "8px" }}>{a.allocationYear}</td>
                      <td style={{ padding: "8px" }}>{a.allocatedDays + a.carriedOverDays}</td>
                      <td style={{ padding: "8px", color: "#b91c1c" }}>{a.usedDays}</td>
                      <td style={{ padding: "8px", fontWeight: 600, color: "#166534" }}>{a.remainingDays} days</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* TAB 5: PAYROLL ENGINE */}
      {activeTab === "payroll" && (
        <div>
          {/* Create Period & Controls */}
          <div style={{ display: "grid", gridTemplateColumns: "360px 1fr", gap: "20px", marginBottom: "20px" }}>
            <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
              <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>📅 New Payroll Period</h3>
              <form onSubmit={handleCreatePayrollPeriod} style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Period Code</label>
                  <input
                    type="text"
                    value={newPeriodCode}
                    onChange={(e) => setNewPeriodCode(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                    placeholder="2026-11"
                    required
                  />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Period Name</label>
                  <input
                    type="text"
                    value={newPeriodName}
                    onChange={(e) => setNewPeriodName(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                    required
                  />
                </div>
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                  <div>
                    <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Start Date</label>
                    <input
                      type="date"
                      value={newPeriodStart}
                      onChange={(e) => setNewPeriodStart(e.target.value)}
                      style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                      required
                    />
                  </div>
                  <div>
                    <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>End Date</label>
                    <input
                      type="date"
                      value={newPeriodEnd}
                      onChange={(e) => setNewPeriodEnd(e.target.value)}
                      style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                      required
                    />
                  </div>
                </div>
                <button
                  type="submit"
                  style={{
                    marginTop: "6px",
                    padding: "10px",
                    background: "#2563eb",
                    color: "#fff",
                    border: "none",
                    borderRadius: "6px",
                    cursor: "pointer",
                    fontWeight: 600,
                  }}
                >
                  Create Payroll Run
                </button>
              </form>
            </div>

            {/* Current Period Operational Card */}
            <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
                <h3 style={{ margin: 0, fontSize: "16px", color: "#1e293b" }}>⚡ Selected Payroll Execution</h3>
                <select
                  value={selectedPeriodId}
                  onChange={(e) => {
                    setSelectedPeriodId(e.target.value);
                    fetchPayslipsForPeriod(e.target.value);
                  }}
                  style={{ padding: "6px 12px", border: "1px solid #cbd5e1", borderRadius: "6px", fontWeight: 600 }}
                >
                  {payrollPeriods.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.periodCode} - {p.periodName} ({p.status.toUpperCase()})
                    </option>
                  ))}
                </select>
              </div>

              {selectedPeriod ? (
                <div>
                  <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: "12px", marginBottom: "16px" }}>
                    <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                      <span style={{ fontSize: "11px", color: "#64748b" }}>Status</span>
                      <div style={{ fontSize: "16px", fontWeight: 700, marginTop: "4px" }}>
                        <span
                          style={{
                            padding: "3px 8px",
                            borderRadius: "12px",
                            fontSize: "12px",
                            background:
                              selectedPeriod.status === "posted" ? "#dcfce7" : selectedPeriod.status === "approved" ? "#e0e7ff" : "#fef3c7",
                            color:
                              selectedPeriod.status === "posted" ? "#166534" : selectedPeriod.status === "approved" ? "#3730a3" : "#92400e",
                          }}
                        >
                          {selectedPeriod.status.toUpperCase()}
                        </span>
                      </div>
                    </div>

                    <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                      <span style={{ fontSize: "11px", color: "#64748b" }}>Gross Salaries</span>
                      <div style={{ fontSize: "16px", fontWeight: 700, color: "#1e293b", marginTop: "4px" }}>
                        PKR {selectedPeriod.totalGrossPkr.toLocaleString()}
                      </div>
                    </div>

                    <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                      <span style={{ fontSize: "11px", color: "#64748b" }}>Net Payable</span>
                      <div style={{ fontSize: "16px", fontWeight: 700, color: "#2563eb", marginTop: "4px" }}>
                        PKR {selectedPeriod.totalNetPkr.toLocaleString()}
                      </div>
                    </div>

                    <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                      <span style={{ fontSize: "11px", color: "#64748b" }}>GL Balanced Posting</span>
                      <div style={{ fontSize: "14px", fontWeight: 600, color: selectedPeriod.glJournalId ? "#166534" : "#94a3b8", marginTop: "4px" }}>
                        {selectedPeriod.glJournalId ? "✅ Journal Posted" : "⏳ Pending Post"}
                      </div>
                    </div>
                  </div>

                  {/* Execution Lifecycle Buttons */}
                  <div style={{ display: "flex", gap: "10px", flexWrap: "wrap" }}>
                    <button
                      onClick={() => handleCalculatePayroll(selectedPeriod.id)}
                      disabled={loading || selectedPeriod.status === "posted"}
                      style={{
                        padding: "10px 16px",
                        background: "#0284c7",
                        color: "#fff",
                        border: "none",
                        borderRadius: "6px",
                        cursor: "pointer",
                        fontWeight: 600,
                      }}
                    >
                      🧮 1. Calculate Payroll Slabs
                    </button>

                    <button
                      onClick={() => handleApprovePayroll(selectedPeriod.id)}
                      disabled={loading || selectedPeriod.status !== "calculated"}
                      style={{
                        padding: "10px 16px",
                        background: "#16a34a",
                        color: "#fff",
                        border: "none",
                        borderRadius: "6px",
                        cursor: "pointer",
                        fontWeight: 600,
                      }}
                    >
                      👍 2. Approve Run
                    </button>

                    <button
                      onClick={() => handlePostGlPayroll(selectedPeriod.id)}
                      disabled={loading || selectedPeriod.status !== "approved"}
                      style={{
                        padding: "10px 16px",
                        background: "#7c3aed",
                        color: "#fff",
                        border: "none",
                        borderRadius: "6px",
                        cursor: "pointer",
                        fontWeight: 600,
                      }}
                    >
                      📒 3. Post to General Ledger
                    </button>

                    {selectedPeriod.status === "posted" && (
                      <button
                        onClick={() => handleReversePayroll(selectedPeriod.id)}
                        disabled={loading}
                        style={{
                          padding: "10px 16px",
                          background: "#dc2626",
                          color: "#fff",
                          border: "none",
                          borderRadius: "6px",
                          cursor: "pointer",
                          fontWeight: 600,
                        }}
                      >
                        ↩️ Reverse Run & GL
                      </button>
                    )}
                  </div>
                </div>
              ) : (
                <p style={{ color: "#94a3b8" }}>Select or create a payroll period to proceed.</p>
              )}
            </div>
          </div>

          {/* Periods History Table */}
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>📜 Payroll Periods Register</h3>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
              <thead>
                <tr style={{ background: "#f8fafc", textAlign: "left", color: "#64748b" }}>
                  <th style={{ padding: "8px" }}>Code</th>
                  <th style={{ padding: "8px" }}>Name</th>
                  <th style={{ padding: "8px" }}>Date Range</th>
                  <th style={{ padding: "8px" }}>Total Gross</th>
                  <th style={{ padding: "8px" }}>Total Net</th>
                  <th style={{ padding: "8px" }}>Status</th>
                  <th style={{ padding: "8px" }}>GL Journal</th>
                </tr>
              </thead>
              <tbody>
                {payrollPeriods.map((p) => (
                  <tr key={p.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                    <td style={{ padding: "8px", fontWeight: 600 }}>{p.periodCode}</td>
                    <td style={{ padding: "8px" }}>{p.periodName}</td>
                    <td style={{ padding: "8px" }}>{p.startDate.slice(0, 10)} to {p.endDate.slice(0, 10)}</td>
                    <td style={{ padding: "8px" }}>PKR {p.totalGrossPkr.toLocaleString()}</td>
                    <td style={{ padding: "8px", fontWeight: 600, color: "#2563eb" }}>PKR {p.totalNetPkr.toLocaleString()}</td>
                    <td style={{ padding: "8px" }}>
                      <span
                        style={{
                          padding: "2px 6px",
                          borderRadius: "10px",
                          fontSize: "11px",
                          fontWeight: 600,
                          background: p.status === "posted" ? "#dcfce7" : "#f1f5f9",
                          color: p.status === "posted" ? "#166534" : "#475569",
                        }}
                      >
                        {p.status.toUpperCase()}
                      </span>
                    </td>
                    <td style={{ padding: "8px", fontFamily: "monospace", fontSize: "11px" }}>
                      {p.glJournalId ? `✅ ${p.glJournalId.slice(0, 8)}…` : "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* TAB 6: PAYSLIPS & EMPLOYEE BREAKDOWN */}
      {activeTab === "payslips" && (
        <div>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "16px" }}>
            <h3 style={{ margin: 0, fontSize: "16px", color: "#1e293b" }}>
              📄 Payslips Register ({selectedPeriod ? selectedPeriod.periodName : "No Period Selected"})
            </h3>
            <select
              value={selectedPeriodId}
              onChange={(e) => {
                setSelectedPeriodId(e.target.value);
                fetchPayslipsForPeriod(e.target.value);
              }}
              style={{ padding: "6px 12px", border: "1px solid #cbd5e1", borderRadius: "6px", fontWeight: 600 }}
            >
              {payrollPeriods.map((p) => (
                <option key={p.id} value={p.id}>{p.periodCode} - {p.periodName}</option>
              ))}
            </select>
          </div>

          <table style={{ width: "100%", borderCollapse: "collapse", background: "#fff", border: "1px solid #e2e8f0", borderRadius: "6px", fontSize: "13px" }}>
            <thead>
              <tr style={{ background: "#f8fafc", textAlign: "left", color: "#64748b" }}>
                <th style={{ padding: "10px" }}>Employee</th>
                <th style={{ padding: "10px" }}>Base Salary</th>
                <th style={{ padding: "10px" }}>Allowances</th>
                <th style={{ padding: "10px" }}>OT Pay</th>
                <th style={{ padding: "10px" }}>Gross Pay</th>
                <th style={{ padding: "10px" }}>Income Tax</th>
                <th style={{ padding: "10px" }}>EOBI (EE)</th>
                <th style={{ padding: "10px" }}>PF (EE)</th>
                <th style={{ padding: "10px" }}>Loan Ded.</th>
                <th style={{ padding: "10px" }}>Net Payable</th>
                <th style={{ padding: "10px" }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {payslips.length === 0 ? (
                <tr>
                  <td colSpan={11} style={{ padding: "20px", textAlign: "center", color: "#94a3b8" }}>
                    No payslips generated for this period. Click "Calculate Payroll Slabs" in the Payroll Engine tab.
                  </td>
                </tr>
              ) : (
                payslips.map((slip) => (
                  <tr key={slip.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                    <td style={{ padding: "10px", fontWeight: 600 }}>{slip.employeeName}</td>
                    <td style={{ padding: "10px" }}>{slip.baseSalaryPkr.toLocaleString()}</td>
                    <td style={{ padding: "10px" }}>{slip.allowancesPkr.toLocaleString()}</td>
                    <td style={{ padding: "10px" }}>{slip.overtimePayPkr.toLocaleString()}</td>
                    <td style={{ padding: "10px", fontWeight: 600 }}>{slip.grossSalaryPkr.toLocaleString()}</td>
                    <td style={{ padding: "10px", color: "#b91c1c" }}>{slip.incomeTaxPkr.toLocaleString()}</td>
                    <td style={{ padding: "10px", color: "#b91c1c" }}>{slip.eobiEmployeePkr.toLocaleString()}</td>
                    <td style={{ padding: "10px", color: "#b91c1c" }}>{slip.pfEmployeePkr.toLocaleString()}</td>
                    <td style={{ padding: "10px", color: "#b91c1c" }}>{slip.loanDeductionPkr.toLocaleString()}</td>
                    <td style={{ padding: "10px", fontWeight: 700, color: "#166534" }}>PKR {slip.netPayablePkr.toLocaleString()}</td>
                    <td style={{ padding: "10px" }}>
                      <button
                        onClick={() => setSelectedPayslip(slip)}
                        style={{
                          padding: "4px 8px",
                          background: "#f1f5f9",
                          border: "1px solid #cbd5e1",
                          borderRadius: "4px",
                          cursor: "pointer",
                          fontSize: "11px",
                          fontWeight: 600,
                        }}
                      >
                        👁️ View Slip
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* TAB 7: LOANS & ADVANCES */}
      {activeTab === "loans" && (
        <div style={{ display: "grid", gridTemplateColumns: "340px 1fr", gap: "20px" }}>
          {/* Issue Loan Form */}
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>💳 Issue Staff Loan / Advance</h3>
            <form onSubmit={handleIssueLoan} style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
              <div>
                <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Employee</label>
                <select
                  value={loanEmpId}
                  onChange={(e) => setLoanEmpId(e.target.value)}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  required
                >
                  {employees.map((emp) => (
                    <option key={emp.id} value={emp.id}>{emp.employeeCode} - {emp.fullName}</option>
                  ))}
                </select>
              </div>

              <div>
                <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Loan Type</label>
                <select
                  value={loanType}
                  onChange={(e) => setLoanType(e.target.value)}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                >
                  <option value="salary_advance">Salary Advance (Short Term)</option>
                  <option value="personal_loan">Personal Staff Loan</option>
                  <option value="emergency_aid">Emergency Financial Aid</option>
                </select>
              </div>

              <div>
                <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Principal Amount (PKR)</label>
                <input
                  type="number"
                  min="5000"
                  step="1000"
                  value={loanPrincipal}
                  onChange={(e) => setLoanPrincipal(Number(e.target.value))}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  required
                />
              </div>

              <div>
                <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Monthly Installment (PKR)</label>
                <input
                  type="number"
                  min="1000"
                  step="500"
                  value={loanInstallment}
                  onChange={(e) => setLoanInstallment(Number(e.target.value))}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  required
                />
              </div>

              <div>
                <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Reason / Purpose</label>
                <input
                  type="text"
                  placeholder="Medical, family need, etc."
                  value={loanReason}
                  onChange={(e) => setLoanReason(e.target.value)}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                />
              </div>

              <button
                type="submit"
                style={{
                  marginTop: "8px",
                  padding: "10px",
                  background: "#2563eb",
                  color: "#fff",
                  border: "none",
                  borderRadius: "6px",
                  cursor: "pointer",
                  fontWeight: 600,
                }}
              >
                Approve & Issue Loan
              </button>
            </form>
          </div>

          {/* Loans Register */}
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>📜 Staff Loans Register & Recovery</h3>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
              <thead>
                <tr style={{ background: "#f8fafc", textAlign: "left", color: "#64748b" }}>
                  <th style={{ padding: "8px" }}>Loan #</th>
                  <th style={{ padding: "8px" }}>Employee</th>
                  <th style={{ padding: "8px" }}>Principal</th>
                  <th style={{ padding: "8px" }}>Installment</th>
                  <th style={{ padding: "8px" }}>Recovered</th>
                  <th style={{ padding: "8px" }}>Balance</th>
                  <th style={{ padding: "8px" }}>Status</th>
                </tr>
              </thead>
              <tbody>
                {loans.length === 0 ? (
                  <tr>
                    <td colSpan={7} style={{ padding: "16px", textAlign: "center", color: "#94a3b8" }}>
                      No active or past staff loans.
                    </td>
                  </tr>
                ) : (
                  loans.map((l) => (
                    <tr key={l.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                      <td style={{ padding: "8px", fontWeight: 600 }}>{l.loanNumber}</td>
                      <td style={{ padding: "8px" }}>{l.employeeName || l.employeeCode}</td>
                      <td style={{ padding: "8px" }}>PKR {l.principalAmountPkr.toLocaleString()}</td>
                      <td style={{ padding: "8px" }}>PKR {l.monthlyInstallmentPkr.toLocaleString()}</td>
                      <td style={{ padding: "8px", color: "#166534" }}>PKR {l.repaidAmountPkr.toLocaleString()} ({l.paidInstallments} inst)</td>
                      <td style={{ padding: "8px", fontWeight: 700, color: l.remainingBalancePkr > 0 ? "#b91c1c" : "#64748b" }}>
                        PKR {l.remainingBalancePkr.toLocaleString()}
                      </td>
                      <td style={{ padding: "8px" }}>
                        <span
                          style={{
                            padding: "2px 6px",
                            borderRadius: "10px",
                            fontSize: "11px",
                            fontWeight: 600,
                            background: l.status === "active" ? "#e0e7ff" : "#dcfce7",
                            color: l.status === "active" ? "#3730a3" : "#166534",
                          }}
                        >
                          {l.status.toUpperCase()}
                        </span>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* TAB 8: STATUTORY REPORTS */}
      {activeTab === "reports" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "20px" }}>
          {/* Statutory Deductions Summary */}
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>🏛️ Statutory Deductions Summary (Pakistan)</h3>
            <div style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
              <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px" }}>
                <span style={{ fontSize: "12px", color: "#64748b" }}>Income Tax Withholding (ITO 2001 Salaried Slabs)</span>
                <div style={{ fontSize: "18px", fontWeight: 700, color: "#1e293b", marginTop: "4px" }}>
                  PKR {payslips.reduce((acc, s) => acc + s.incomeTaxPkr, 0).toLocaleString()}
                </div>
              </div>

              <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px" }}>
                <span style={{ fontSize: "12px", color: "#64748b" }}>EOBI Contributions (EE 1% + ER 5% on PKR 37,000)</span>
                <div style={{ fontSize: "18px", fontWeight: 700, color: "#1e293b", marginTop: "4px" }}>
                  PKR {(
                    payslips.reduce((acc, s) => acc + s.eobiEmployeePkr, 0) +
                    payslips.reduce((acc, s) => acc + s.eobiEmployerPkr, 0)
                  ).toLocaleString()}
                </div>
                <div style={{ fontSize: "11px", color: "#64748b", marginTop: "4px" }}>
                  Employee: PKR {payslips.reduce((acc, s) => acc + s.eobiEmployeePkr, 0).toLocaleString()} | Employer: PKR {payslips.reduce((acc, s) => acc + s.eobiEmployerPkr, 0).toLocaleString()}
                </div>
              </div>

              <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px" }}>
                <span style={{ fontSize: "12px", color: "#64748b" }}>Provident Fund (EE Deduction + ER Matching)</span>
                <div style={{ fontSize: "18px", fontWeight: 700, color: "#1e293b", marginTop: "4px" }}>
                  PKR {(
                    payslips.reduce((acc, s) => acc + s.pfEmployeePkr, 0) +
                    payslips.reduce((acc, s) => acc + s.pfEmployerPkr, 0)
                  ).toLocaleString()}
                </div>
                <div style={{ fontSize: "11px", color: "#64748b", marginTop: "4px" }}>
                  Employee: PKR {payslips.reduce((acc, s) => acc + s.pfEmployeePkr, 0).toLocaleString()} | Employer: PKR {payslips.reduce((acc, s) => acc + s.pfEmployerPkr, 0).toLocaleString()}
                </div>
              </div>
            </div>
          </div>

          {/* Department Headcount Breakdown */}
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px 0", fontSize: "16px", color: "#1e293b" }}>📊 Headcount & Organization Metrics</h3>
            <div style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
              <div style={{ display: "flex", justifyContent: "space-between", padding: "10px", background: "#f8fafc", borderRadius: "6px" }}>
                <span style={{ fontWeight: 600 }}>Total Active Employees:</span>
                <span style={{ fontWeight: 700, color: "#2563eb" }}>{employees.filter((e) => e.status === "active").length}</span>
              </div>
              <div style={{ display: "flex", justifyContent: "space-between", padding: "10px", background: "#f8fafc", borderRadius: "6px" }}>
                <span style={{ fontWeight: 600 }}>Total Departments:</span>
                <span style={{ fontWeight: 700 }}>{departments.length}</span>
              </div>
              <div style={{ display: "flex", justifyContent: "space-between", padding: "10px", background: "#f8fafc", borderRadius: "6px" }}>
                <span style={{ fontWeight: 600 }}>Total Designations:</span>
                <span style={{ fontWeight: 700 }}>{designations.length}</span>
              </div>
              <div style={{ display: "flex", justifyContent: "space-between", padding: "10px", background: "#f8fafc", borderRadius: "6px" }}>
                <span style={{ fontWeight: 600 }}>Outstanding Staff Loans:</span>
                <span style={{ fontWeight: 700, color: "#b91c1c" }}>
                  PKR {loans.reduce((acc, l) => acc + l.remainingBalancePkr, 0).toLocaleString()}
                </span>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* MODAL: ADD EMPLOYEE */}
      {showAddEmpModal && (
        <div style={{ position: "fixed", top: 0, left: 0, right: 0, bottom: 0, background: "rgba(0,0,0,0.5)", display: "flex", justifyContent: "center", alignItems: "center", zIndex: 1000 }}>
          <div style={{ background: "#fff", borderRadius: "8px", padding: "24px", width: "560px", maxHeight: "90vh", overflowY: "auto" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "16px" }}>
              <h2 style={{ margin: 0, fontSize: "18px" }}>+ Add New Employee</h2>
              <button onClick={() => setShowAddEmpModal(false)} style={{ border: "none", background: "none", fontSize: "18px", cursor: "pointer" }}>✕</button>
            </div>

            <form onSubmit={handleCreateEmployee} style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Employee Code *</label>
                  <input
                    type="text"
                    value={newEmpCode}
                    onChange={(e) => setNewEmpCode(e.target.value)}
                    placeholder="EMP-001"
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                    required
                  />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>CNIC (13 digits) *</label>
                  <input
                    type="text"
                    value={newEmpCnic}
                    onChange={(e) => setNewEmpCnic(e.target.value)}
                    placeholder="42101-1234567-1"
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                    required
                  />
                </div>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>First Name *</label>
                  <input
                    type="text"
                    value={newEmpFirstName}
                    onChange={(e) => setNewEmpFirstName(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                    required
                  />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Last Name *</label>
                  <input
                    type="text"
                    value={newEmpLastName}
                    onChange={(e) => setNewEmpLastName(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                    required
                  />
                </div>
              </div>

              <div>
                <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Father / Husband Name</label>
                <input
                  type="text"
                  value={newEmpFatherName}
                  onChange={(e) => setNewEmpFatherName(e.target.value)}
                  style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                />
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Department *</label>
                  <select
                    value={newEmpDeptId}
                    onChange={(e) => setNewEmpDeptId(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                    required
                  >
                    <option value="">Select Department</option>
                    {departments.map((d) => (
                      <option key={d.id} value={d.id}>{d.name}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Designation *</label>
                  <select
                    value={newEmpDesigId}
                    onChange={(e) => setNewEmpDesigId(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                    required
                  >
                    <option value="">Select Designation</option>
                    {designations.map((d) => (
                      <option key={d.id} value={d.id}>{d.title}</option>
                    ))}
                  </select>
                </div>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Official Email</label>
                  <input
                    type="email"
                    value={newEmpEmail}
                    onChange={(e) => setNewEmpEmail(e.target.value)}
                    placeholder="emp@company.com"
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Mobile Phone</label>
                  <input
                    type="text"
                    value={newEmpPhone}
                    onChange={(e) => setNewEmpPhone(e.target.value)}
                    placeholder="0300-1234567"
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  />
                </div>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Employment Type</label>
                  <select
                    value={newEmpType}
                    onChange={(e) => setNewEmpType(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  >
                    <option value="permanent">Permanent</option>
                    <option value="contract">Contractual</option>
                    <option value="probation">Probationary</option>
                    <option value="intern">Internship</option>
                  </select>
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Join Date</label>
                  <input
                    type="date"
                    value={newEmpJoinDate}
                    onChange={(e) => setNewEmpJoinDate(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                    required
                  />
                </div>
              </div>

              <div style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "16px" }}>
                <button
                  type="button"
                  onClick={() => setShowAddEmpModal(false)}
                  style={{ padding: "8px 16px", border: "1px solid #cbd5e1", background: "#fff", borderRadius: "6px", cursor: "pointer" }}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  style={{ padding: "8px 16px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "6px", cursor: "pointer", fontWeight: 600 }}
                >
                  Save Employee
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* MODAL: EMPLOYEE COMPENSATION */}
      {selectedEmpForComp && (
        <div style={{ position: "fixed", top: 0, left: 0, right: 0, bottom: 0, background: "rgba(0,0,0,0.5)", display: "flex", justifyContent: "center", alignItems: "center", zIndex: 1000 }}>
          <div style={{ background: "#fff", borderRadius: "8px", padding: "24px", width: "600px", maxHeight: "90vh", overflowY: "auto" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "16px" }}>
              <div>
                <h2 style={{ margin: "0 0 4px 0", fontSize: "18px" }}>⚙️ Salary Package & Bank Details</h2>
                <span style={{ fontSize: "13px", color: "#64748b" }}>{selectedEmpForComp.fullName} ({selectedEmpForComp.employeeCode})</span>
              </div>
              <button onClick={() => setSelectedEmpForComp(null)} style={{ border: "none", background: "none", fontSize: "18px", cursor: "pointer" }}>✕</button>
            </div>

            <form onSubmit={handleSaveCompensation} style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Base Salary (PKR) *</label>
                  <input
                    type="number"
                    min="10000"
                    step="1000"
                    value={compBaseSalary}
                    onChange={(e) => setCompBaseSalary(Number(e.target.value))}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                    required
                  />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>House Rent Allowance (PKR)</label>
                  <input
                    type="number"
                    min="0"
                    step="500"
                    value={compHouseRent}
                    onChange={(e) => setCompHouseRent(Number(e.target.value))}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  />
                </div>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: "10px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Medical Allowance</label>
                  <input
                    type="number"
                    min="0"
                    value={compMedical}
                    onChange={(e) => setCompMedical(Number(e.target.value))}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Utility Allowance</label>
                  <input
                    type="number"
                    min="0"
                    value={compUtility}
                    onChange={(e) => setCompUtility(Number(e.target.value))}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Special Allowance</label>
                  <input
                    type="number"
                    min="0"
                    value={compSpecial}
                    onChange={(e) => setCompSpecial(Number(e.target.value))}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  />
                </div>
              </div>

              <div style={{ background: "#f8fafc", padding: "10px", borderRadius: "6px", fontSize: "13px", fontWeight: 600 }}>
                Total Monthly Gross Package: PKR {(compBaseSalary + compHouseRent + compMedical + compUtility + compSpecial).toLocaleString()}
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Bank Name</label>
                  <input
                    type="text"
                    value={compBankName}
                    onChange={(e) => setCompBankName(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "12px", color: "#64748b", marginBottom: "4px" }}>Account Number</label>
                  <input
                    type="text"
                    value={compAccountNo}
                    onChange={(e) => setCompAccountNo(e.target.value)}
                    style={{ width: "100%", padding: "8px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
                  />
                </div>
              </div>

              {/* Statutory Checkboxes */}
              <div style={{ display: "flex", gap: "20px", marginTop: "8px" }}>
                <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px", cursor: "pointer" }}>
                  <input
                    type="checkbox"
                    checked={compEobi}
                    onChange={(e) => setCompEobi(e.target.checked)}
                  />
                  EOBI Applicable (1% EE / 5% ER)
                </label>
                <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px", cursor: "pointer" }}>
                  <input
                    type="checkbox"
                    checked={compPf}
                    onChange={(e) => setCompPf(e.target.checked)}
                  />
                  Provident Fund Applicable
                </label>
                <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px", cursor: "pointer" }}>
                  <input
                    type="checkbox"
                    checked={compTaxExempt}
                    onChange={(e) => setCompTaxExempt(e.target.checked)}
                  />
                  Tax Exempt
                </label>
              </div>

              <div style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "16px" }}>
                <button
                  type="button"
                  onClick={() => setSelectedEmpForComp(null)}
                  style={{ padding: "8px 16px", border: "1px solid #cbd5e1", background: "#fff", borderRadius: "6px", cursor: "pointer" }}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  style={{ padding: "8px 16px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "6px", cursor: "pointer", fontWeight: 600 }}
                >
                  Update Compensation
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* MODAL: VIEW ITEMIZE PAYSLIP */}
      {selectedPayslip && (
        <div style={{ position: "fixed", top: 0, left: 0, right: 0, bottom: 0, background: "rgba(0,0,0,0.5)", display: "flex", justifyContent: "center", alignItems: "center", zIndex: 1000 }}>
          <div style={{ background: "#fff", borderRadius: "8px", padding: "28px", width: "560px", maxHeight: "90vh", overflowY: "auto", fontFamily: "system-ui" }}>
            <div style={{ borderBottom: "2px solid #0f172a", paddingBottom: "12px", marginBottom: "16px", display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
              <div>
                <h2 style={{ margin: 0, fontSize: "20px", color: "#0f172a" }}>UNIVERSAL LOGISTICS (PVT) LTD</h2>
                <span style={{ fontSize: "12px", color: "#64748b" }}>Monthly Salary Payslip · Confidential</span>
              </div>
              <button onClick={() => setSelectedPayslip(null)} style={{ border: "none", background: "none", fontSize: "20px", cursor: "pointer" }}>✕</button>
            </div>

            {/* Employee Info Header */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px", background: "#f8fafc", padding: "12px", borderRadius: "6px", marginBottom: "16px", fontSize: "12px" }}>
              <div><strong>Employee:</strong> {selectedPayslip.employeeName}</div>
              <div><strong>Employee Code:</strong> {selectedPayslip.employeeCode}</div>
              <div><strong>CNIC:</strong> {selectedPayslip.cnic ? formatCnic(selectedPayslip.cnic) : "—"}</div>
              <div><strong>Department:</strong> {selectedPayslip.departmentName || "Operations"}</div>
              <div><strong>Designation:</strong> {selectedPayslip.designationTitle || "Officer"}</div>
              <div><strong>Payment Method:</strong> {selectedPayslip.paymentMethod.toUpperCase()}</div>
            </div>

            {/* Breakdown Earnings & Deductions */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px", marginBottom: "16px", fontSize: "13px" }}>
              {/* Earnings */}
              <div style={{ border: "1px solid #e2e8f0", borderRadius: "6px", padding: "12px" }}>
                <h4 style={{ margin: "0 0 8px 0", color: "#166534", borderBottom: "1px solid #e2e8f0", paddingBottom: "4px" }}>EARNINGS</h4>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                  <span>Basic Salary:</span>
                  <span>PKR {selectedPayslip.baseSalaryPkr.toLocaleString()}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                  <span>Allowances:</span>
                  <span>PKR {selectedPayslip.allowancesPkr.toLocaleString()}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                  <span>Overtime Pay ({selectedPayslip.overtimeHours} hrs):</span>
                  <span>PKR {selectedPayslip.overtimePayPkr.toLocaleString()}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                  <span>Bonus:</span>
                  <span>PKR {selectedPayslip.bonusPkr.toLocaleString()}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginTop: "8px", paddingTop: "6px", borderTop: "1px dashed #cbd5e1", fontWeight: 700 }}>
                  <span>Gross Salary:</span>
                  <span>PKR {selectedPayslip.grossSalaryPkr.toLocaleString()}</span>
                </div>
              </div>

              {/* Deductions */}
              <div style={{ border: "1px solid #e2e8f0", borderRadius: "6px", padding: "12px" }}>
                <h4 style={{ margin: "0 0 8px 0", color: "#991b1b", borderBottom: "1px solid #e2e8f0", paddingBottom: "4px" }}>DEDUCTIONS</h4>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                  <span>Income Tax:</span>
                  <span>PKR {selectedPayslip.incomeTaxPkr.toLocaleString()}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                  <span>EOBI (1%):</span>
                  <span>PKR {selectedPayslip.eobiEmployeePkr.toLocaleString()}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                  <span>Provident Fund:</span>
                  <span>PKR {selectedPayslip.pfEmployeePkr.toLocaleString()}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                  <span>Loan Recovery:</span>
                  <span>PKR {selectedPayslip.loanDeductionPkr.toLocaleString()}</span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginTop: "8px", paddingTop: "6px", borderTop: "1px dashed #cbd5e1", fontWeight: 700, color: "#991b1b" }}>
                  <span>Total Deductions:</span>
                  <span>PKR {selectedPayslip.totalDeductionsPkr.toLocaleString()}</span>
                </div>
              </div>
            </div>

            {/* Net Pay Card */}
            <div style={{ background: "#dcfce7", border: "1px solid #22c55e", borderRadius: "6px", padding: "12px", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <span style={{ fontSize: "14px", fontWeight: 700, color: "#166534" }}>NET PAYABLE SALARY:</span>
              <span style={{ fontSize: "18px", fontWeight: 800, color: "#166534" }}>PKR {selectedPayslip.netPayablePkr.toLocaleString()}</span>
            </div>

            {/* Employer Contributions Note */}
            <div style={{ marginTop: "12px", fontSize: "11px", color: "#64748b", borderTop: "1px solid #e2e8f0", paddingTop: "8px" }}>
              * Employer Contributions: EOBI PKR {selectedPayslip.eobiEmployerPkr.toLocaleString()} | PF Match PKR {selectedPayslip.pfEmployerPkr.toLocaleString()} | Total Cost to Company: PKR {selectedPayslip.totalEmployerCostPkr.toLocaleString()}
            </div>

            <div style={{ display: "flex", justifyContent: "flex-end", marginTop: "16px" }}>
              <button
                onClick={() => window.print()}
                style={{
                  padding: "8px 16px",
                  background: "#2563eb",
                  color: "#fff",
                  border: "none",
                  borderRadius: "6px",
                  cursor: "pointer",
                  fontWeight: 600,
                }}
              >
                🖨️ Print Payslip
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
