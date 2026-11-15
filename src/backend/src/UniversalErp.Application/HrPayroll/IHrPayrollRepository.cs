using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalErp.Application.HrPayroll;

public interface IHrPayrollRepository
{
    // Departments & Designations
    Task<DepartmentDto> CreateDepartmentAsync(Guid tenantId, Guid companyId, CreateDepartmentRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);
    Task<DesignationDto> CreateDesignationAsync(Guid tenantId, Guid companyId, CreateDesignationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<DesignationDto>> GetDesignationsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);

    // Employees
    Task<EmployeeDto> CreateEmployeeAsync(Guid tenantId, Guid companyId, CreateEmployeeRequest request, CancellationToken ct = default);
    Task<EmployeeDto?> GetEmployeeByIdAsync(Guid tenantId, Guid companyId, Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(Guid tenantId, Guid companyId, string? status = null, Guid? departmentId = null, CancellationToken ct = default);
    Task<EmployeeDto> UpdateEmployeeAsync(Guid tenantId, Guid companyId, Guid employeeId, UpdateEmployeeRequest request, CancellationToken ct = default);

    // Compensation
    Task<CompensationStructureDto> SetCompensationAsync(Guid tenantId, Guid companyId, SetCompensationRequest request, CancellationToken ct = default);
    Task<CompensationStructureDto?> GetActiveCompensationAsync(Guid tenantId, Guid companyId, Guid employeeId, CancellationToken ct = default);

    // Shifts & Attendance
    Task<ShiftDto> CreateShiftAsync(Guid tenantId, Guid companyId, CreateShiftRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ShiftDto>> GetShiftsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);
    Task AssignShiftAsync(Guid tenantId, Guid companyId, Guid employeeId, Guid shiftId, DateOnly effectiveFrom, DateOnly? effectiveTo = null, CancellationToken ct = default);
    Task<AttendanceRecordDto> RecordAttendanceAsync(Guid tenantId, Guid companyId, Guid employeeId, DateTimeOffset timestamp, bool isCheckIn, DateOnly? attendanceDate = null, CancellationToken ct = default);
    Task<AttendanceRecordDto> RegularizeAttendanceAsync(Guid tenantId, Guid companyId, RegularizeAttendanceRequest request, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceRecordDto>> GetAttendanceRecordsAsync(Guid tenantId, Guid companyId, DateOnly fromDate, DateOnly toDate, Guid? employeeId = null, CancellationToken ct = default);

    // Leaves & Holidays
    Task<LeaveTypeDto> CreateLeaveTypeAsync(Guid tenantId, Guid companyId, CreateLeaveTypeRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveTypeDto>> GetLeaveTypesAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);
    Task<LeaveAllocationDto> AllocateLeavesAsync(Guid tenantId, Guid companyId, Guid employeeId, Guid leaveTypeId, int year, decimal days, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveAllocationDto>> GetLeaveAllocationsAsync(Guid tenantId, Guid companyId, Guid employeeId, int year, CancellationToken ct = default);
    Task<LeaveRequestDto> SubmitLeaveRequestAsync(Guid tenantId, Guid companyId, SubmitLeaveRequest request, CancellationToken ct = default);
    Task<LeaveRequestDto> ProcessLeaveApprovalAsync(Guid tenantId, Guid companyId, ApproveLeaveRequest request, Guid approvedBy, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveRequestDto>> GetLeaveRequestsAsync(Guid tenantId, Guid companyId, string? status = null, Guid? employeeId = null, CancellationToken ct = default);
    Task<HolidayCalendarDto> CreateHolidayAsync(Guid tenantId, Guid companyId, CreateHolidayRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<HolidayCalendarDto>> GetHolidaysAsync(Guid tenantId, Guid companyId, int? year = null, CancellationToken ct = default);

    // Statutory Rules & Tax Slabs
    Task<StatutoryConfigDto> SetStatutoryConfigAsync(Guid tenantId, Guid companyId, SetStatutoryConfigRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<StatutoryConfigDto>> GetStatutoryConfigsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);
    Task<TaxSlabDto> CreateTaxSlabAsync(Guid tenantId, Guid companyId, CreateTaxSlabRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TaxSlabDto>> GetTaxSlabsAsync(Guid tenantId, Guid companyId, int? taxYear = null, CancellationToken ct = default);

    // Salary Loans
    Task<SalaryLoanDto> CreateSalaryLoanAsync(Guid tenantId, Guid companyId, CreateSalaryLoanRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SalaryLoanDto>> GetSalaryLoansAsync(Guid tenantId, Guid companyId, Guid? employeeId = null, string? status = null, CancellationToken ct = default);

    // Payroll Processing & Payslips
    Task<PayrollPeriodDto> CreatePayrollPeriodAsync(Guid tenantId, Guid companyId, CreatePayrollPeriodRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PayrollPeriodDto>> GetPayrollPeriodsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default);
    Task<PayrollPeriodDto?> GetPayrollPeriodByIdAsync(Guid tenantId, Guid companyId, Guid periodId, CancellationToken ct = default);
    Task<PayrollPeriodDto> ProcessPayrollRunAsync(Guid tenantId, Guid companyId, Guid periodId, Guid processedBy, CancellationToken ct = default);
    Task<PayrollPeriodDto> ApprovePayrollRunAsync(Guid tenantId, Guid companyId, Guid periodId, Guid approvedBy, CancellationToken ct = default);
    Task<PayrollPeriodDto> PostPayrollToGlAsync(Guid tenantId, Guid companyId, PostPayrollToGlRequest request, Guid postedBy, CancellationToken ct = default);
    Task<PayrollReversalDto> ReversePayrollRunAsync(Guid tenantId, Guid companyId, ReversePayrollRunRequest request, Guid reversedBy, CancellationToken ct = default);
    Task<IReadOnlyList<PayslipDto>> GetPayslipsForPeriodAsync(Guid tenantId, Guid companyId, Guid periodId, CancellationToken ct = default);
    Task<PayslipDto?> GetPayslipByIdAsync(Guid tenantId, Guid companyId, Guid payslipId, CancellationToken ct = default);
}
