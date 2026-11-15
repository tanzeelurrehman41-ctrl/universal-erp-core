using System;
using System.Collections.Generic;

namespace UniversalErp.Application.HrPayroll;

// ── Department & Designation Models ──────────────────────────────────────────

public record DepartmentDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string DepartmentCode,
    string Name,
    Guid? ParentDepartmentId,
    Guid? ManagerId,
    string? ManagerName,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record CreateDepartmentRequest(
    string DepartmentCode,
    string Name,
    Guid? ParentDepartmentId = null,
    Guid? ManagerId = null
);

public record DesignationDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string DesignationCode,
    string Title,
    string GradeLevel,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record CreateDesignationRequest(
    string DesignationCode,
    string Title,
    string GradeLevel = "G1"
);

// ── Employee Models ──────────────────────────────────────────────────────────

public record EmployeeDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string EmployeeCode,
    string FirstName,
    string LastName,
    string FullName,
    string Email,
    string? Phone,
    string CnicOrNationalId,
    DateOnly? DateOfBirth,
    string? Gender,
    DateOnly HireDate,
    DateOnly? TerminationDate,
    string Status,
    string EmploymentType,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? DesignationId,
    string? DesignationTitle,
    Guid? ManagerId,
    string? ManagerName,
    Guid? UserId,
    string? BankName,
    string? BankAccountTitle,
    string? BankAccountNumber,
    string? BankIban,
    string? TaxIdentificationNumber,
    bool IsEobiApplicable,
    bool IsPfApplicable,
    CompensationStructureDto? CurrentCompensation,
    DateTimeOffset CreatedAt
);

public record CreateEmployeeRequest(
    string EmployeeCode,
    string FirstName,
    string LastName,
    string Email,
    string CnicOrNationalId,
    DateOnly HireDate,
    string EmploymentType = "full_time",
    string Status = "active",
    string? Phone = null,
    DateOnly? DateOfBirth = null,
    string? Gender = null,
    Guid? DepartmentId = null,
    Guid? DesignationId = null,
    Guid? ManagerId = null,
    Guid? UserId = null,
    string? BankName = null,
    string? BankAccountTitle = null,
    string? BankAccountNumber = null,
    string? BankIban = null,
    string? TaxIdentificationNumber = null,
    bool IsEobiApplicable = true,
    bool IsPfApplicable = true,
    decimal BaseSalaryPkr = 0,
    decimal HouseRentAllowancePkr = 0,
    decimal UtilityAllowancePkr = 0,
    decimal ConveyanceAllowancePkr = 0,
    decimal MedicalAllowancePkr = 0,
    decimal SpecialAllowancePkr = 0
);

public record UpdateEmployeeRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone = null,
    string? Status = null,
    Guid? DepartmentId = null,
    Guid? DesignationId = null,
    Guid? ManagerId = null,
    string? BankName = null,
    string? BankAccountTitle = null,
    string? BankAccountNumber = null,
    string? BankIban = null,
    string? TaxIdentificationNumber = null,
    bool? IsEobiApplicable = null,
    bool? IsPfApplicable = null
);

public record CompensationStructureDto(
    Guid Id,
    Guid EmployeeId,
    DateOnly EffectiveDate,
    decimal BaseSalaryPkr,
    decimal HouseRentAllowancePkr,
    decimal UtilityAllowancePkr,
    decimal ConveyanceAllowancePkr,
    decimal MedicalAllowancePkr,
    decimal SpecialAllowancePkr,
    decimal GrossSalaryPkr,
    string PaymentFrequency,
    string CurrencyCode,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record SetCompensationRequest(
    Guid EmployeeId,
    DateOnly EffectiveDate,
    decimal BaseSalaryPkr,
    decimal HouseRentAllowancePkr = 0,
    decimal UtilityAllowancePkr = 0,
    decimal ConveyanceAllowancePkr = 0,
    decimal MedicalAllowancePkr = 0,
    decimal SpecialAllowancePkr = 0,
    string PaymentFrequency = "monthly"
);

// ── Shifts & Attendance Models ───────────────────────────────────────────────

public record ShiftDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string ShiftCode,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakDurationMinutes,
    int GracePeriodMinutes,
    decimal HalfDayThresholdHours,
    decimal FullDayHours,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record CreateShiftRequest(
    string ShiftCode,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakDurationMinutes = 60,
    int GracePeriodMinutes = 15,
    decimal HalfDayThresholdHours = 4.0m,
    decimal FullDayHours = 8.0m
);

public record AttendanceRecordDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    Guid EmployeeId,
    string? EmployeeCode,
    string? EmployeeName,
    DateOnly AttendanceDate,
    DateTimeOffset? CheckInTime,
    DateTimeOffset? CheckOutTime,
    int TotalWorkMinutes,
    int OvertimeMinutes,
    string Status,
    int LateMinutes,
    bool IsRegularized,
    string? RegularizationReason,
    Guid? RegularizedBy,
    DateTimeOffset CreatedAt
);

public record RecordCheckInRequest(
    Guid EmployeeId,
    DateTimeOffset CheckInTime,
    DateOnly? AttendanceDate = null
);

public record RecordCheckOutRequest(
    Guid EmployeeId,
    DateTimeOffset CheckOutTime,
    DateOnly? AttendanceDate = null
);

public record RegularizeAttendanceRequest(
    Guid AttendanceRecordId,
    DateTimeOffset CheckInTime,
    DateTimeOffset CheckOutTime,
    string Status,
    string Reason
);

public record AttendanceSummaryDto(
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    int TotalDaysInPeriod,
    int PresentDays,
    int HalfDays,
    int LeaveDays,
    int AbsentDays,
    int HolidayDays,
    int RestDays,
    decimal TotalWorkHours,
    decimal TotalOvertimeHours,
    decimal PayableDays
);

// ── Leaves & Holidays Models ─────────────────────────────────────────────────

public record LeaveTypeDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string Code,
    string Name,
    decimal DefaultAnnualQuota,
    bool IsPaid,
    bool IsCarryForwardAllowed,
    decimal MaxCarryForwardDays,
    bool IsEncashable,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record CreateLeaveTypeRequest(
    string Code,
    string Name,
    decimal DefaultAnnualQuota = 14.0m,
    bool IsPaid = true,
    bool IsCarryForwardAllowed = false,
    decimal MaxCarryForwardDays = 0,
    bool IsEncashable = false
);

public record LeaveAllocationDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    int Year,
    decimal AllocatedDays,
    decimal UsedDays,
    decimal PendingDays,
    decimal CarriedForwardDays,
    decimal RemainingDays,
    DateTimeOffset CreatedAt
);

public record LeaveRequestDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    Guid EmployeeId,
    string? EmployeeCode,
    string? EmployeeName,
    Guid LeaveTypeId,
    string? LeaveTypeName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    string Reason,
    string Status,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string? RejectionReason,
    DateTimeOffset CreatedAt
);

public record SubmitLeaveRequest(
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    string Reason
);

public record ApproveLeaveRequest(
    Guid LeaveRequestId,
    bool Approved,
    string? Notes = null
);

public record HolidayCalendarDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    DateOnly HolidayDate,
    string Name,
    string? Description,
    bool IsRecurring,
    DateTimeOffset CreatedAt
);

public record CreateHolidayRequest(
    DateOnly HolidayDate,
    string Name,
    string? Description = null,
    bool IsRecurring = false
);

// ── Statutory Rules & Tax Slabs ──────────────────────────────────────────────

public record StatutoryConfigDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string ConfigKey,
    string ConfigName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal? RatePercentage,
    decimal? ThresholdAmountPkr,
    decimal? EmployerRatePercentage,
    decimal? EmployerFixedAmountPkr,
    decimal? EmployeeFixedAmountPkr,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record SetStatutoryConfigRequest(
    string ConfigKey,
    string ConfigName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    decimal? RatePercentage = null,
    decimal? ThresholdAmountPkr = null,
    decimal? EmployerRatePercentage = null,
    decimal? EmployerFixedAmountPkr = null,
    decimal? EmployeeFixedAmountPkr = null
);

public record TaxSlabDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    int TaxYear,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    int SlabOrder,
    decimal MinAnnualIncomePkr,
    decimal? MaxAnnualIncomePkr,
    decimal FixedTaxAmountPkr,
    decimal MarginalTaxRatePercentage,
    string? Description,
    DateTimeOffset CreatedAt
);

public record CreateTaxSlabRequest(
    int TaxYear,
    DateOnly EffectiveFrom,
    int SlabOrder,
    decimal MinAnnualIncomePkr,
    decimal? MaxAnnualIncomePkr,
    decimal FixedTaxAmountPkr,
    decimal MarginalTaxRatePercentage,
    string? Description = null,
    DateOnly? EffectiveTo = null
);

// ── Salary Loans & Advances Models ───────────────────────────────────────────

public record SalaryLoanDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string LoanNumber,
    Guid EmployeeId,
    string? EmployeeCode,
    string? EmployeeName,
    decimal PrincipalAmountPkr,
    decimal MonthlyInstallmentPkr,
    int TotalInstallments,
    int PaidInstallments,
    decimal RemainingBalancePkr,
    string Status,
    Guid? ApprovedBy,
    DateTimeOffset? DisbursedAt,
    DateTimeOffset CreatedAt
);

public record CreateSalaryLoanRequest(
    Guid EmployeeId,
    decimal PrincipalAmountPkr,
    decimal MonthlyInstallmentPkr,
    int TotalInstallments,
    string? LoanNumber = null
);

// ── Payroll Run & Payslip Models ─────────────────────────────────────────────

public record PayrollPeriodDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    string PeriodName,
    string PeriodCode,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    decimal TotalGrossPkr,
    decimal TotalDeductionsPkr,
    decimal TotalNetPkr,
    decimal TotalEmployerContributionsPkr,
    Guid? GlJournalId,
    Guid? ProcessedBy,
    DateTimeOffset? ProcessedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    Guid? PostedBy,
    DateTimeOffset? PostedAt,
    int TotalPayslips,
    DateTimeOffset CreatedAt
);

public record CreatePayrollPeriodRequest(
    string PeriodName,
    string PeriodCode,
    DateOnly StartDate,
    DateOnly EndDate
);

public record PayslipDto(
    Guid Id,
    Guid TenantId,
    Guid CompanyId,
    Guid PayrollPeriodId,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string? DesignationTitle,
    string? DepartmentName,
    string PayslipNumber,
    string CurrencyCode,
    decimal WorkingDays,
    decimal AttendedDays,
    decimal LeaveDays,
    decimal AbsentDays,
    decimal OvertimeHours,
    decimal BaseSalaryPkr,
    decimal AllowancesPkr,
    decimal OvertimePayPkr,
    decimal BonusPkr,
    decimal GrossPayPkr,
    decimal TaxDeductionPkr,
    decimal EobiEmployeePkr,
    decimal PfEmployeePkr,
    decimal LoanDeductionPkr,
    decimal OtherDeductionsPkr,
    decimal TotalDeductionsPkr,
    decimal NetPayPkr,
    decimal EobiEmployerPkr,
    decimal PfEmployerPkr,
    string PaymentStatus,
    string PaymentMethod,
    string? PaymentReference,
    DateTimeOffset? PaidAt,
    List<PayslipLineDto>? Lines,
    DateTimeOffset CreatedAt
);

public record PayslipLineDto(
    Guid Id,
    Guid PayslipId,
    string LineType,
    string Category,
    string Description,
    decimal AmountPkr,
    DateTimeOffset CreatedAt
);

public record ProcessPayrollRunRequest(
    Guid PayrollPeriodId
);

public record ApprovePayrollRunRequest(
    Guid PayrollPeriodId
);

public record PostPayrollToGlRequest(
    Guid PayrollPeriodId,
    DateOnly JournalDate,
    string Description = "Payroll posting"
);

public record ReversePayrollRunRequest(
    Guid PayrollPeriodId,
    string Reason
);

public record PayrollReversalDto(
    Guid Id,
    Guid PayrollPeriodId,
    string ReversalReason,
    Guid? ReversalJournalId,
    Guid? ReversedBy,
    DateTimeOffset ReversedAt
);
