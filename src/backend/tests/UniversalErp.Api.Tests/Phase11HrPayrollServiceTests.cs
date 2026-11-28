using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversalErp.Application.HrPayroll;
using Xunit;

namespace UniversalErp.Api.Tests;

public class Phase11HrPayrollServiceTests
{
    private class MockHrPayrollRepository : IHrPayrollRepository
    {
        public Task<DepartmentDto> CreateDepartmentAsync(Guid tenantId, Guid companyId, CreateDepartmentRequest request, CancellationToken ct = default)
            => Task.FromResult(new DepartmentDto(Guid.NewGuid(), tenantId, companyId, request.DepartmentCode, request.Name, null, null, null, true, DateTimeOffset.UtcNow));

        public Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DepartmentDto>>(new List<DepartmentDto>());

        public Task<DesignationDto> CreateDesignationAsync(Guid tenantId, Guid companyId, CreateDesignationRequest request, CancellationToken ct = default)
            => Task.FromResult(new DesignationDto(Guid.NewGuid(), tenantId, companyId, request.DesignationCode, request.Title, request.GradeLevel, true, DateTimeOffset.UtcNow));

        public Task<IReadOnlyList<DesignationDto>> GetDesignationsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DesignationDto>>(new List<DesignationDto>());

        public Task<EmployeeDto> CreateEmployeeAsync(Guid tenantId, Guid companyId, CreateEmployeeRequest request, CancellationToken ct = default)
            => Task.FromResult(new EmployeeDto(Guid.NewGuid(), tenantId, companyId, request.EmployeeCode, request.FirstName, request.LastName, $"{request.FirstName} {request.LastName}", request.Email, null, request.CnicOrNationalId, null, null, request.HireDate, null, "active", "full_time", null, null, null, null, null, null, null, null, null, null, null, null, true, true, null, DateTimeOffset.UtcNow));

        public Task<EmployeeDto?> GetEmployeeByIdAsync(Guid tenantId, Guid companyId, Guid employeeId, CancellationToken ct = default) => Task.FromResult<EmployeeDto?>(null);
        public Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(Guid tenantId, Guid companyId, string? status = null, Guid? departmentId = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EmployeeDto>>(new List<EmployeeDto>());
        public Task<EmployeeDto> UpdateEmployeeAsync(Guid tenantId, Guid companyId, Guid employeeId, UpdateEmployeeRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CompensationStructureDto> SetCompensationAsync(Guid tenantId, Guid companyId, SetCompensationRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CompensationStructureDto?> GetActiveCompensationAsync(Guid tenantId, Guid companyId, Guid employeeId, CancellationToken ct = default) => Task.FromResult<CompensationStructureDto?>(null);
        public Task<ShiftDto> CreateShiftAsync(Guid tenantId, Guid companyId, CreateShiftRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ShiftDto>> GetShiftsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ShiftDto>>(new List<ShiftDto>());
        public Task AssignShiftAsync(Guid tenantId, Guid companyId, Guid employeeId, Guid shiftId, DateOnly effectiveFrom, DateOnly? effectiveTo = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<AttendanceRecordDto> RecordAttendanceAsync(Guid tenantId, Guid companyId, Guid employeeId, DateTimeOffset timestamp, bool isCheckIn, DateOnly? attendanceDate = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AttendanceRecordDto> RegularizeAttendanceAsync(Guid tenantId, Guid companyId, RegularizeAttendanceRequest request, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<AttendanceRecordDto>> GetAttendanceRecordsAsync(Guid tenantId, Guid companyId, DateOnly fromDate, DateOnly toDate, Guid? employeeId = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AttendanceRecordDto>>(new List<AttendanceRecordDto>());
        public Task<LeaveTypeDto> CreateLeaveTypeAsync(Guid tenantId, Guid companyId, CreateLeaveTypeRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<LeaveTypeDto>> GetLeaveTypesAsync(Guid tenantId, Guid companyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LeaveTypeDto>>(new List<LeaveTypeDto>());
        public Task<LeaveAllocationDto> AllocateLeavesAsync(Guid tenantId, Guid companyId, Guid employeeId, Guid leaveTypeId, int year, decimal days, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<LeaveAllocationDto>> GetLeaveAllocationsAsync(Guid tenantId, Guid companyId, Guid employeeId, int year, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LeaveAllocationDto>>(new List<LeaveAllocationDto>());
        public Task<LeaveRequestDto> SubmitLeaveRequestAsync(Guid tenantId, Guid companyId, SubmitLeaveRequest request, CancellationToken ct = default) => Task.FromResult(new LeaveRequestDto(Guid.NewGuid(), tenantId, companyId, request.EmployeeId, null, null, request.LeaveTypeId, null, request.StartDate, request.EndDate, request.TotalDays, request.Reason, "pending", null, null, null, DateTimeOffset.UtcNow));
        public Task<LeaveRequestDto> ProcessLeaveApprovalAsync(Guid tenantId, Guid companyId, ApproveLeaveRequest request, Guid approvedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<LeaveRequestDto>> GetLeaveRequestsAsync(Guid tenantId, Guid companyId, string? status = null, Guid? employeeId = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LeaveRequestDto>>(new List<LeaveRequestDto>());
        public Task<HolidayCalendarDto> CreateHolidayAsync(Guid tenantId, Guid companyId, CreateHolidayRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<HolidayCalendarDto>> GetHolidaysAsync(Guid tenantId, Guid companyId, int? year = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HolidayCalendarDto>>(new List<HolidayCalendarDto>());
        public Task<StatutoryConfigDto> SetStatutoryConfigAsync(Guid tenantId, Guid companyId, SetStatutoryConfigRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<StatutoryConfigDto>> GetStatutoryConfigsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StatutoryConfigDto>>(new List<StatutoryConfigDto>());
        public Task<TaxSlabDto> CreateTaxSlabAsync(Guid tenantId, Guid companyId, CreateTaxSlabRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<TaxSlabDto>> GetTaxSlabsAsync(Guid tenantId, Guid companyId, int? taxYear = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TaxSlabDto>>(new List<TaxSlabDto>());
        public Task<SalaryLoanDto> CreateSalaryLoanAsync(Guid tenantId, Guid companyId, CreateSalaryLoanRequest request, CancellationToken ct = default) => Task.FromResult(new SalaryLoanDto(Guid.NewGuid(), tenantId, companyId, "LN-01", request.EmployeeId, null, null, request.PrincipalAmountPkr, request.MonthlyInstallmentPkr, request.TotalInstallments, 0, request.PrincipalAmountPkr, "active", null, null, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<SalaryLoanDto>> GetSalaryLoansAsync(Guid tenantId, Guid companyId, Guid? employeeId = null, string? status = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SalaryLoanDto>>(new List<SalaryLoanDto>());
        public Task<PayrollPeriodDto> CreatePayrollPeriodAsync(Guid tenantId, Guid companyId, CreatePayrollPeriodRequest request, CancellationToken ct = default) => Task.FromResult(new PayrollPeriodDto(Guid.NewGuid(), tenantId, companyId, request.PeriodName, request.PeriodCode, request.StartDate, request.EndDate, "draft", 0, 0, 0, 0, null, null, null, null, null, null, null, 0, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<PayrollPeriodDto>> GetPayrollPeriodsAsync(Guid tenantId, Guid companyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PayrollPeriodDto>>(new List<PayrollPeriodDto>());
        public Task<PayrollPeriodDto?> GetPayrollPeriodByIdAsync(Guid tenantId, Guid companyId, Guid periodId, CancellationToken ct = default) => Task.FromResult<PayrollPeriodDto?>(null);
        public Task<PayrollPeriodDto> ProcessPayrollRunAsync(Guid tenantId, Guid companyId, Guid periodId, Guid processedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PayrollPeriodDto> ApprovePayrollRunAsync(Guid tenantId, Guid companyId, Guid periodId, Guid approvedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PayrollPeriodDto> PostPayrollToGlAsync(Guid tenantId, Guid companyId, PostPayrollToGlRequest request, Guid postedBy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PayrollReversalDto> ReversePayrollRunAsync(Guid tenantId, Guid companyId, ReversePayrollRunRequest request, Guid reversedBy, CancellationToken ct = default) => Task.FromResult(new PayrollReversalDto(Guid.NewGuid(), request.PayrollPeriodId, request.Reason, null, reversedBy, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<PayslipDto>> GetPayslipsForPeriodAsync(Guid tenantId, Guid companyId, Guid periodId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PayslipDto>>(new List<PayslipDto>());
        public Task<PayslipDto?> GetPayslipByIdAsync(Guid tenantId, Guid companyId, Guid payslipId, CancellationToken ct = default) => Task.FromResult<PayslipDto?>(null);
    }

    [Fact]
    public async Task CreateDepartment_ValidatesRequiredFields()
    {
        var service = new HrPayrollService(new MockHrPayrollRepository());
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateDepartmentAsync(tid, cid, new CreateDepartmentRequest("", "Human Resources")));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateDepartmentAsync(tid, cid, new CreateDepartmentRequest("HR", "")));

        var valid = await service.CreateDepartmentAsync(tid, cid, new CreateDepartmentRequest("HR", "Human Resources"));
        Assert.Equal("HR", valid.DepartmentCode);
    }

    [Fact]
    public async Task CreateEmployee_ValidatesRequiredFields()
    {
        var service = new HrPayrollService(new MockHrPayrollRepository());
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateEmployeeAsync(tid, cid, new CreateEmployeeRequest("", "Ali", "Khan", "ali@test.com", "42101-1111111-1", new DateOnly(2025, 1, 1))));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateEmployeeAsync(tid, cid, new CreateEmployeeRequest("EMP-1", "Ali", "Khan", "ali@test.com", "", new DateOnly(2025, 1, 1))));

        var emp = await service.CreateEmployeeAsync(tid, cid, new CreateEmployeeRequest("EMP-1", "Ali", "Khan", "ali@test.com", "42101-1111111-1", new DateOnly(2025, 1, 1)));
        Assert.Equal("EMP-1", emp.EmployeeCode);
    }

    [Fact]
    public async Task SubmitLeaveRequest_ValidatesDateOrderAndDays()
    {
        var service = new HrPayrollService(new MockHrPayrollRepository());
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();

        // End date before start date
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitLeaveRequestAsync(tid, cid, new SubmitLeaveRequest(
                Guid.NewGuid(), Guid.NewGuid(),
                new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 5), 5, "Vacation"
            )));

        // Zero or negative days
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitLeaveRequestAsync(tid, cid, new SubmitLeaveRequest(
                Guid.NewGuid(), Guid.NewGuid(),
                new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 10), 0, "Vacation"
            )));
    }
}
