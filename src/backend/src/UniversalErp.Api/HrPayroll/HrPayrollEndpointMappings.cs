using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using UniversalErp.Application.Abstractions;
using UniversalErp.Application.HrPayroll;
using UniversalErp.Domain.Tenancy;

namespace UniversalErp.Api.HrPayroll;

public static class HrPayrollEndpointMappings
{
    public static IEndpointRouteBuilder MapHrPayrollEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment)
    {
        var hrGroup = endpoints.MapGroup("/api/v1/hr").WithTags("HR");
        var payrollGroup = endpoints.MapGroup("/api/v1/payroll").WithTags("Payroll");

        // ── 1. Departments & Designations ────────────────────────────────────

        hrGroup.MapGet("/departments", async (
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetDepartmentsAsync(ctx.TenantId, ctx.CompanyId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        hrGroup.MapPost("/departments", async (
            [FromBody] CreateDepartmentRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var dept = await service.CreateDepartmentAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/hr/departments/{dept.Id}", dept);
        });

        hrGroup.MapGet("/designations", async (
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetDesignationsAsync(ctx.TenantId, ctx.CompanyId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        hrGroup.MapPost("/designations", async (
            [FromBody] CreateDesignationRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var desig = await service.CreateDesignationAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/hr/designations/{desig.Id}", desig);
        });

        // ── 2. Employees ─────────────────────────────────────────────────────

        hrGroup.MapGet("/employees", async (
            [FromQuery] string? status,
            [FromQuery] Guid? departmentId,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetEmployeesAsync(ctx.TenantId, ctx.CompanyId, status, departmentId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        hrGroup.MapPost("/employees", async (
            [FromBody] CreateEmployeeRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var emp = await service.CreateEmployeeAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/hr/employees/{emp.Id}", emp);
        });

        hrGroup.MapGet("/employees/{id:guid}", async (
            Guid id,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var emp = await service.GetEmployeeByIdAsync(ctx.TenantId, ctx.CompanyId, id, httpContext.RequestAborted);
            return emp != null ? Results.Ok(emp) : Results.NotFound();
        });

        hrGroup.MapPut("/employees/{id:guid}", async (
            Guid id,
            [FromBody] UpdateEmployeeRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var emp = await service.UpdateEmployeeAsync(ctx.TenantId, ctx.CompanyId, id, req, httpContext.RequestAborted);
            return Results.Ok(emp);
        });

        hrGroup.MapPost("/employees/{id:guid}/compensation", async (
            Guid id,
            [FromBody] SetCompensationRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var comp = await service.SetCompensationAsync(ctx.TenantId, ctx.CompanyId, req with { EmployeeId = id }, httpContext.RequestAborted);
            return Results.Ok(comp);
        });

        // ── 3. Shifts & Attendance ───────────────────────────────────────────

        hrGroup.MapGet("/shifts", async (
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetShiftsAsync(ctx.TenantId, ctx.CompanyId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        hrGroup.MapPost("/shifts", async (
            [FromBody] CreateShiftRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var shift = await service.CreateShiftAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/hr/shifts/{shift.Id}", shift);
        });

        hrGroup.MapPost("/shifts/assign", async (
            [FromBody] AssignShiftRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            await service.AssignShiftAsync(ctx.TenantId, ctx.CompanyId, req.EmployeeId, req.ShiftId, req.EffectiveFrom, req.EffectiveTo, httpContext.RequestAborted);
            return Results.Ok(new { message = "Shift assigned successfully." });
        });

        hrGroup.MapPost("/attendance/check-in", async (
            [FromBody] RecordCheckInRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var rec = await service.RecordCheckInAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Ok(rec);
        });

        hrGroup.MapPost("/attendance/check-out", async (
            [FromBody] RecordCheckOutRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var rec = await service.RecordCheckOutAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Ok(rec);
        });

        hrGroup.MapPost("/attendance/regularize", async (
            [FromBody] RegularizeAttendanceRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var rec = await service.RegularizeAttendanceAsync(ctx.TenantId, ctx.CompanyId, req, ctx.UserId, httpContext.RequestAborted);
            return Results.Ok(rec);
        });

        hrGroup.MapGet("/attendance", async (
            [FromQuery] DateOnly fromDate,
            [FromQuery] DateOnly toDate,
            [FromQuery] Guid? employeeId,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetAttendanceRecordsAsync(ctx.TenantId, ctx.CompanyId, fromDate, toDate, employeeId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        // ── 4. Leaves & Holidays ─────────────────────────────────────────────

        hrGroup.MapGet("/leave-types", async (
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetLeaveTypesAsync(ctx.TenantId, ctx.CompanyId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        hrGroup.MapPost("/leave-types", async (
            [FromBody] CreateLeaveTypeRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var lt = await service.CreateLeaveTypeAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/hr/leave-types/{lt.Id}", lt);
        });

        hrGroup.MapGet("/leaves/allocations", async (
            [FromQuery] Guid employeeId,
            [FromQuery] int year,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetLeaveAllocationsAsync(ctx.TenantId, ctx.CompanyId, employeeId, year, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        hrGroup.MapPost("/leaves/allocations", async (
            [FromBody] AllocateLeaveRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var alloc = await service.AllocateLeavesAsync(ctx.TenantId, ctx.CompanyId, req.EmployeeId, req.LeaveTypeId, req.Year, req.Days, httpContext.RequestAborted);
            return Results.Ok(alloc);
        });

        hrGroup.MapGet("/leaves/requests", async (
            [FromQuery] string? status,
            [FromQuery] Guid? employeeId,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetLeaveRequestsAsync(ctx.TenantId, ctx.CompanyId, status, employeeId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        hrGroup.MapPost("/leaves/requests", async (
            [FromBody] SubmitLeaveRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var leaveReq = await service.SubmitLeaveRequestAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/hr/leaves/requests/{leaveReq.Id}", leaveReq);
        });

        hrGroup.MapPost("/leaves/requests/{id:guid}/process", async (
            Guid id,
            [FromBody] ApproveLeaveRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var res = await service.ProcessLeaveApprovalAsync(ctx.TenantId, ctx.CompanyId, req with { LeaveRequestId = id }, ctx.UserId, httpContext.RequestAborted);
            return Results.Ok(res);
        });

        hrGroup.MapGet("/holidays", async (
            [FromQuery] int? year,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetHolidaysAsync(ctx.TenantId, ctx.CompanyId, year, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        hrGroup.MapPost("/holidays", async (
            [FromBody] CreateHolidayRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var holiday = await service.CreateHolidayAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/hr/holidays/{holiday.Id}", holiday);
        });

        // ── 5. Statutory & Tax Slabs ─────────────────────────────────────────

        hrGroup.MapGet("/statutory-configs", async (
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetStatutoryConfigsAsync(ctx.TenantId, ctx.CompanyId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        hrGroup.MapPost("/statutory-configs", async (
            [FromBody] SetStatutoryConfigRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var conf = await service.SetStatutoryConfigAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Ok(conf);
        });

        hrGroup.MapGet("/tax-slabs", async (
            [FromQuery] int? taxYear,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetTaxSlabsAsync(ctx.TenantId, ctx.CompanyId, taxYear, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        hrGroup.MapPost("/tax-slabs", async (
            [FromBody] CreateTaxSlabRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var slab = await service.CreateTaxSlabAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/hr/tax-slabs/{slab.Id}", slab);
        });

        // ── 6. Salary Loans ──────────────────────────────────────────────────

        hrGroup.MapGet("/loans", async (
            [FromQuery] Guid? employeeId,
            [FromQuery] string? status,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetSalaryLoansAsync(ctx.TenantId, ctx.CompanyId, employeeId, status, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        hrGroup.MapPost("/loans", async (
            [FromBody] CreateSalaryLoanRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var loan = await service.CreateSalaryLoanAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/hr/loans/{loan.Id}", loan);
        });

        // ── 7. Payroll Periods & Runs ────────────────────────────────────────

        payrollGroup.MapGet("/periods", async (
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetPayrollPeriodsAsync(ctx.TenantId, ctx.CompanyId, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        payrollGroup.MapPost("/periods", async (
            [FromBody] CreatePayrollPeriodRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var period = await service.CreatePayrollPeriodAsync(ctx.TenantId, ctx.CompanyId, req, httpContext.RequestAborted);
            return Results.Created($"/api/v1/payroll/periods/{period.Id}", period);
        });

        payrollGroup.MapGet("/periods/{id:guid}", async (
            Guid id,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var period = await service.GetPayrollPeriodByIdAsync(ctx.TenantId, ctx.CompanyId, id, httpContext.RequestAborted);
            return period != null ? Results.Ok(period) : Results.NotFound();
        });

        payrollGroup.MapPost("/periods/{id:guid}/process", async (
            Guid id,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var period = await service.ProcessPayrollRunAsync(ctx.TenantId, ctx.CompanyId, id, ctx.UserId, httpContext.RequestAborted);
            return Results.Ok(period);
        });

        payrollGroup.MapPost("/periods/{id:guid}/approve", async (
            Guid id,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var period = await service.ApprovePayrollRunAsync(ctx.TenantId, ctx.CompanyId, id, ctx.UserId, httpContext.RequestAborted);
            return Results.Ok(period);
        });

        payrollGroup.MapPost("/periods/{id:guid}/post-gl", async (
            Guid id,
            [FromBody] PostPayrollToGlRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var period = await service.PostPayrollToGlAsync(ctx.TenantId, ctx.CompanyId, req with { PayrollPeriodId = id }, ctx.UserId, httpContext.RequestAborted);
            return Results.Ok(period);
        });

        payrollGroup.MapPost("/periods/{id:guid}/reverse", async (
            Guid id,
            [FromBody] ReversePayrollRunRequest req,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var rev = await service.ReversePayrollRunAsync(ctx.TenantId, ctx.CompanyId, req with { PayrollPeriodId = id }, ctx.UserId, httpContext.RequestAborted);
            return Results.Ok(rev);
        });

        payrollGroup.MapGet("/periods/{id:guid}/payslips", async (
            Guid id,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var list = await service.GetPayslipsForPeriodAsync(ctx.TenantId, ctx.CompanyId, id, httpContext.RequestAborted);
            return Results.Ok(list);
        });

        payrollGroup.MapGet("/payslips/{id:guid}", async (
            Guid id,
            HrPayrollService service,
            ITenantContextAccessor ctxAccessor,
            HttpContext httpContext) =>
        {
            var ctx = ResolveContext(httpContext, ctxAccessor);
            var slip = await service.GetPayslipByIdAsync(ctx.TenantId, ctx.CompanyId, id, httpContext.RequestAborted);
            return slip != null ? Results.Ok(slip) : Results.NotFound();
        });

        return endpoints;
    }

    private static TenantContext ResolveContext(HttpContext httpContext, ITenantContextAccessor accessor)
    {
        var tenantIdStr = httpContext.Request.Headers["X-Tenant-Id"].ToString();
        var companyIdStr = httpContext.Request.Headers["X-Company-Id"].ToString();
        var userIdStr = httpContext.Request.Headers["X-User-Id"].ToString();

        var tenantId = Guid.TryParse(tenantIdStr, out var tid) && tid != Guid.Empty ? tid : accessor.Current.TenantId;
        var companyId = Guid.TryParse(companyIdStr, out var cid) && cid != Guid.Empty ? cid : accessor.Current.CompanyId;
        var userId = Guid.TryParse(userIdStr, out var uid) && uid != Guid.Empty ? uid : accessor.Current.UserId;

        return new TenantContext(tenantId, companyId, userId);
    }
}

public record AssignShiftRequest(Guid EmployeeId, Guid ShiftId, DateOnly EffectiveFrom, DateOnly? EffectiveTo = null);
public record AllocateLeaveRequest(Guid EmployeeId, Guid LeaveTypeId, int Year, decimal Days);
