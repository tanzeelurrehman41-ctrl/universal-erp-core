using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalErp.Application.HrPayroll;

public class HrPayrollService
{
    private readonly IHrPayrollRepository _repository;

    public HrPayrollService(IHrPayrollRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    // Departments & Designations
    public Task<DepartmentDto> CreateDepartmentAsync(Guid tenantId, Guid companyId, CreateDepartmentRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DepartmentCode))
            throw new ArgumentException("Department code is required.", nameof(request.DepartmentCode));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Department name is required.", nameof(request.Name));

        return _repository.CreateDepartmentAsync(tenantId, companyId, request, ct);
    }

    public Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
        => _repository.GetDepartmentsAsync(tenantId, companyId, ct);

    public Task<DesignationDto> CreateDesignationAsync(Guid tenantId, Guid companyId, CreateDesignationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DesignationCode))
            throw new ArgumentException("Designation code is required.", nameof(request.DesignationCode));
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Designation title is required.", nameof(request.Title));

        return _repository.CreateDesignationAsync(tenantId, companyId, request, ct);
    }

    public Task<IReadOnlyList<DesignationDto>> GetDesignationsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
        => _repository.GetDesignationsAsync(tenantId, companyId, ct);

    // Employees
    public Task<EmployeeDto> CreateEmployeeAsync(Guid tenantId, Guid companyId, CreateEmployeeRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeCode))
            throw new ArgumentException("Employee code is required.", nameof(request.EmployeeCode));
        if (string.IsNullOrWhiteSpace(request.FirstName))
            throw new ArgumentException("First name is required.", nameof(request.FirstName));
        if (string.IsNullOrWhiteSpace(request.LastName))
            throw new ArgumentException("Last name is required.", nameof(request.LastName));
        if (string.IsNullOrWhiteSpace(request.CnicOrNationalId))
            throw new ArgumentException("CNIC or National ID is required.", nameof(request.CnicOrNationalId));

        return _repository.CreateEmployeeAsync(tenantId, companyId, request, ct);
    }

    public Task<EmployeeDto?> GetEmployeeByIdAsync(Guid tenantId, Guid companyId, Guid employeeId, CancellationToken ct = default)
        => _repository.GetEmployeeByIdAsync(tenantId, companyId, employeeId, ct);

    public Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(Guid tenantId, Guid companyId, string? status = null, Guid? departmentId = null, CancellationToken ct = default)
        => _repository.GetEmployeesAsync(tenantId, companyId, status, departmentId, ct);

    public Task<EmployeeDto> UpdateEmployeeAsync(Guid tenantId, Guid companyId, Guid employeeId, UpdateEmployeeRequest request, CancellationToken ct = default)
        => _repository.UpdateEmployeeAsync(tenantId, companyId, employeeId, request, ct);

    public Task<CompensationStructureDto> SetCompensationAsync(Guid tenantId, Guid companyId, SetCompensationRequest request, CancellationToken ct = default)
    {
        if (request.BaseSalaryPkr < 0)
            throw new ArgumentException("Base salary cannot be negative.", nameof(request.BaseSalaryPkr));

        return _repository.SetCompensationAsync(tenantId, companyId, request, ct);
    }

    // Shifts & Attendance
    public Task<ShiftDto> CreateShiftAsync(Guid tenantId, Guid companyId, CreateShiftRequest request, CancellationToken ct = default)
        => _repository.CreateShiftAsync(tenantId, companyId, request, ct);

    public Task<IReadOnlyList<ShiftDto>> GetShiftsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
        => _repository.GetShiftsAsync(tenantId, companyId, ct);

    public Task AssignShiftAsync(Guid tenantId, Guid companyId, Guid employeeId, Guid shiftId, DateOnly effectiveFrom, DateOnly? effectiveTo = null, CancellationToken ct = default)
        => _repository.AssignShiftAsync(tenantId, companyId, employeeId, shiftId, effectiveFrom, effectiveTo, ct);

    public Task<AttendanceRecordDto> RecordCheckInAsync(Guid tenantId, Guid companyId, RecordCheckInRequest request, CancellationToken ct = default)
        => _repository.RecordAttendanceAsync(tenantId, companyId, request.EmployeeId, request.CheckInTime, true, request.AttendanceDate, ct);

    public Task<AttendanceRecordDto> RecordCheckOutAsync(Guid tenantId, Guid companyId, RecordCheckOutRequest request, CancellationToken ct = default)
        => _repository.RecordAttendanceAsync(tenantId, companyId, request.EmployeeId, request.CheckOutTime, false, request.AttendanceDate, ct);

    public Task<AttendanceRecordDto> RegularizeAttendanceAsync(Guid tenantId, Guid companyId, RegularizeAttendanceRequest request, Guid userId, CancellationToken ct = default)
        => _repository.RegularizeAttendanceAsync(tenantId, companyId, request, userId, ct);

    public Task<IReadOnlyList<AttendanceRecordDto>> GetAttendanceRecordsAsync(Guid tenantId, Guid companyId, DateOnly fromDate, DateOnly toDate, Guid? employeeId = null, CancellationToken ct = default)
        => _repository.GetAttendanceRecordsAsync(tenantId, companyId, fromDate, toDate, employeeId, ct);

    // Leaves
    public Task<LeaveTypeDto> CreateLeaveTypeAsync(Guid tenantId, Guid companyId, CreateLeaveTypeRequest request, CancellationToken ct = default)
        => _repository.CreateLeaveTypeAsync(tenantId, companyId, request, ct);

    public Task<IReadOnlyList<LeaveTypeDto>> GetLeaveTypesAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
        => _repository.GetLeaveTypesAsync(tenantId, companyId, ct);

    public Task<LeaveAllocationDto> AllocateLeavesAsync(Guid tenantId, Guid companyId, Guid employeeId, Guid leaveTypeId, int year, decimal days, CancellationToken ct = default)
        => _repository.AllocateLeavesAsync(tenantId, companyId, employeeId, leaveTypeId, year, days, ct);

    public Task<IReadOnlyList<LeaveAllocationDto>> GetLeaveAllocationsAsync(Guid tenantId, Guid companyId, Guid employeeId, int year, CancellationToken ct = default)
        => _repository.GetLeaveAllocationsAsync(tenantId, companyId, employeeId, year, ct);

    public Task<LeaveRequestDto> SubmitLeaveRequestAsync(Guid tenantId, Guid companyId, SubmitLeaveRequest request, CancellationToken ct = default)
    {
        if (request.EndDate < request.StartDate)
            throw new ArgumentException("End date cannot be prior to start date.", nameof(request.EndDate));
        if (request.TotalDays <= 0)
            throw new ArgumentException("Total days must be greater than zero.", nameof(request.TotalDays));

        return _repository.SubmitLeaveRequestAsync(tenantId, companyId, request, ct);
    }

    public Task<LeaveRequestDto> ProcessLeaveApprovalAsync(Guid tenantId, Guid companyId, ApproveLeaveRequest request, Guid approvedBy, CancellationToken ct = default)
        => _repository.ProcessLeaveApprovalAsync(tenantId, companyId, request, approvedBy, ct);

    public Task<IReadOnlyList<LeaveRequestDto>> GetLeaveRequestsAsync(Guid tenantId, Guid companyId, string? status = null, Guid? employeeId = null, CancellationToken ct = default)
        => _repository.GetLeaveRequestsAsync(tenantId, companyId, status, employeeId, ct);

    public Task<HolidayCalendarDto> CreateHolidayAsync(Guid tenantId, Guid companyId, CreateHolidayRequest request, CancellationToken ct = default)
        => _repository.CreateHolidayAsync(tenantId, companyId, request, ct);

    public Task<IReadOnlyList<HolidayCalendarDto>> GetHolidaysAsync(Guid tenantId, Guid companyId, int? year = null, CancellationToken ct = default)
        => _repository.GetHolidaysAsync(tenantId, companyId, year, ct);

    // Statutory & Tax Slabs
    public Task<StatutoryConfigDto> SetStatutoryConfigAsync(Guid tenantId, Guid companyId, SetStatutoryConfigRequest request, CancellationToken ct = default)
        => _repository.SetStatutoryConfigAsync(tenantId, companyId, request, ct);

    public Task<IReadOnlyList<StatutoryConfigDto>> GetStatutoryConfigsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
        => _repository.GetStatutoryConfigsAsync(tenantId, companyId, ct);

    public Task<TaxSlabDto> CreateTaxSlabAsync(Guid tenantId, Guid companyId, CreateTaxSlabRequest request, CancellationToken ct = default)
        => _repository.CreateTaxSlabAsync(tenantId, companyId, request, ct);

    public Task<IReadOnlyList<TaxSlabDto>> GetTaxSlabsAsync(Guid tenantId, Guid companyId, int? taxYear = null, CancellationToken ct = default)
        => _repository.GetTaxSlabsAsync(tenantId, companyId, taxYear, ct);

    // Loans
    public Task<SalaryLoanDto> CreateSalaryLoanAsync(Guid tenantId, Guid companyId, CreateSalaryLoanRequest request, CancellationToken ct = default)
    {
        if (request.PrincipalAmountPkr <= 0)
            throw new ArgumentException("Loan principal must be greater than zero.", nameof(request.PrincipalAmountPkr));
        if (request.TotalInstallments <= 0)
            throw new ArgumentException("Total installments must be at least 1.", nameof(request.TotalInstallments));

        return _repository.CreateSalaryLoanAsync(tenantId, companyId, request, ct);
    }

    public Task<IReadOnlyList<SalaryLoanDto>> GetSalaryLoansAsync(Guid tenantId, Guid companyId, Guid? employeeId = null, string? status = null, CancellationToken ct = default)
        => _repository.GetSalaryLoansAsync(tenantId, companyId, employeeId, status, ct);

    // Payroll Periods & Runs
    public Task<PayrollPeriodDto> CreatePayrollPeriodAsync(Guid tenantId, Guid companyId, CreatePayrollPeriodRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.PeriodCode))
            throw new ArgumentException("Period code is required.", nameof(request.PeriodCode));
        if (request.EndDate < request.StartDate)
            throw new ArgumentException("Period end date cannot be before start date.", nameof(request.EndDate));

        return _repository.CreatePayrollPeriodAsync(tenantId, companyId, request, ct);
    }

    public Task<IReadOnlyList<PayrollPeriodDto>> GetPayrollPeriodsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
        => _repository.GetPayrollPeriodsAsync(tenantId, companyId, ct);

    public Task<PayrollPeriodDto?> GetPayrollPeriodByIdAsync(Guid tenantId, Guid companyId, Guid periodId, CancellationToken ct = default)
        => _repository.GetPayrollPeriodByIdAsync(tenantId, companyId, periodId, ct);

    public Task<PayrollPeriodDto> ProcessPayrollRunAsync(Guid tenantId, Guid companyId, Guid periodId, Guid processedBy, CancellationToken ct = default)
        => _repository.ProcessPayrollRunAsync(tenantId, companyId, periodId, processedBy, ct);

    public Task<PayrollPeriodDto> ApprovePayrollRunAsync(Guid tenantId, Guid companyId, Guid periodId, Guid approvedBy, CancellationToken ct = default)
        => _repository.ApprovePayrollRunAsync(tenantId, companyId, periodId, approvedBy, ct);

    public Task<PayrollPeriodDto> PostPayrollToGlAsync(Guid tenantId, Guid companyId, PostPayrollToGlRequest request, Guid postedBy, CancellationToken ct = default)
        => _repository.PostPayrollToGlAsync(tenantId, companyId, request, postedBy, ct);

    public Task<PayrollReversalDto> ReversePayrollRunAsync(Guid tenantId, Guid companyId, ReversePayrollRunRequest request, Guid reversedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Reversal reason is required.", nameof(request.Reason));

        return _repository.ReversePayrollRunAsync(tenantId, companyId, request, reversedBy, ct);
    }

    public Task<IReadOnlyList<PayslipDto>> GetPayslipsForPeriodAsync(Guid tenantId, Guid companyId, Guid periodId, CancellationToken ct = default)
        => _repository.GetPayslipsForPeriodAsync(tenantId, companyId, periodId, ct);

    public Task<PayslipDto?> GetPayslipByIdAsync(Guid tenantId, Guid companyId, Guid payslipId, CancellationToken ct = default)
        => _repository.GetPayslipByIdAsync(tenantId, companyId, payslipId, ct);
}
