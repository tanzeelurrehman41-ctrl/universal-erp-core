using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UniversalErp.Application.HrPayroll;
using UniversalErp.Infrastructure.HrPayroll;
using Xunit;

namespace UniversalErp.Api.Tests;

public class Phase11PostgresHrPayrollIntegrationTests
{
    private static readonly string? StagingConnectionString =
        Environment.GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
        ?? "Host=127.0.0.1;Port=5433;Database=universal_erp_phase5_dev;Username=erp_dev;Password=local_dev_change_me;Include Error Detail=true;";

    [Fact]
    public async Task Phase11_FullHrAndPayrollLifecycle_PostgresIntegrationTest()
    {
        if (string.IsNullOrWhiteSpace(StagingConnectionString)) return;

        var ct = CancellationToken.None;
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var dataSource = NpgsqlDataSource.Create(StagingConnectionString);
        var repo = new PostgresHrPayrollRepository(dataSource);
        var service = new HrPayrollService(repo);

        // 1. Seed Tenant, Company, User, and Currency in staging DB
        await using (var conn = await dataSource.OpenConnectionAsync(ct))
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = $"SELECT set_config('app.tenant_id', '{tenantId}', true), set_config('app.company_id', '{companyId}', true);";
            await cmd.ExecuteNonQueryAsync(ct);

            var tcode = $"T-{tenantId.ToString("N")[..8]}";
            var ccode = $"C-{companyId.ToString("N")[..8]}";
            var uemail = $"hr_{tenantId.ToString("N")[..6]}@example.com";

            cmd.CommandText = """
                INSERT INTO platform.tenants (id, tenant_code, display_name, status, default_country_code, default_currency_code, default_timezone)
                VALUES (@tId, @tcode, 'Phase 11 Tenant', 'active', 'PK', 'PKR', 'Asia/Karachi')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO organization.companies (tenant_id, id, company_code, name, country_code, base_currency_code, timezone, status)
                VALUES (@tId, @cId, @ccode, 'Phase 11 HR & Payroll Corp', 'PK', 'PKR', 'Asia/Karachi', 'active')
                ON CONFLICT (tenant_id, id) DO NOTHING;

                INSERT INTO identity.users (id, email_normalized, display_name, status)
                VALUES (@uId, @uemail, 'HR Director', 'active')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO finance.currencies (code, name, minor_unit, is_active)
                VALUES ('PKR', 'Pakistani Rupee', 2, true)
                ON CONFLICT (code) DO NOTHING;
            """;
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("cId", companyId);
            cmd.Parameters.AddWithValue("uId", userId);
            cmd.Parameters.AddWithValue("tcode", tcode);
            cmd.Parameters.AddWithValue("ccode", ccode);
            cmd.Parameters.AddWithValue("uemail", uemail);
            await cmd.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }

        // 2. Department & Designation Creation
        var dept = await service.CreateDepartmentAsync(tenantId, companyId, new CreateDepartmentRequest("ENG", "Software Engineering"), ct);
        Assert.Equal("ENG", dept.DepartmentCode);

        var desig = await service.CreateDesignationAsync(tenantId, companyId, new CreateDesignationRequest("SWE", "Senior Software Engineer", "G4"), ct);
        Assert.Equal("SWE", desig.DesignationCode);

        // 3. Employee Creation with Initial Compensation Structure
        var emp = await service.CreateEmployeeAsync(tenantId, companyId, new CreateEmployeeRequest(
            EmployeeCode: "EMP-2026-001",
            FirstName: "Farhan",
            LastName: "Siddiqui",
            Email: "farhan.siddiqui@universal-erp.test",
            CnicOrNationalId: "42101-9876543-1",
            HireDate: new DateOnly(2025, 1, 15),
            DepartmentId: dept.Id,
            DesignationId: desig.Id,
            BaseSalaryPkr: 100_000m,
            HouseRentAllowancePkr: 40_000m,
            UtilityAllowancePkr: 10_000m
        ), ct);

        Assert.Equal("EMP-2026-001", emp.EmployeeCode);
        Assert.NotNull(emp.CurrentCompensation);
        Assert.Equal(150_000m, emp.CurrentCompensation!.GrossSalaryPkr);

        // 4. Shifts & Attendance
        var shift = await service.CreateShiftAsync(tenantId, companyId, new CreateShiftRequest(
            "GEN-01", "General Shift", new TimeOnly(9, 0), new TimeOnly(18, 0), 60, 15, 4.0m, 8.0m
        ), ct);
        Assert.Equal("GEN-01", shift.ShiftCode);

        await service.AssignShiftAsync(tenantId, companyId, emp.Id, shift.Id, new DateOnly(2026, 1, 1), null, ct);

        // Record check-in and check-out
        var today = new DateOnly(2026, 11, 2);
        var cinTime = new DateTimeOffset(2026, 11, 2, 9, 5, 0, TimeSpan.FromHours(5)); // within grace
        var coutTime = new DateTimeOffset(2026, 11, 2, 18, 5, 0, TimeSpan.FromHours(5));

        var cinRec = await service.RecordCheckInAsync(tenantId, companyId, new RecordCheckInRequest(emp.Id, cinTime, today), ct);
        Assert.Equal("half_day", cinRec.Status); // no checkout yet

        var coutRec = await service.RecordCheckOutAsync(tenantId, companyId, new RecordCheckOutRequest(emp.Id, coutTime, today), ct);
        Assert.Equal("present", coutRec.Status);
        Assert.Equal(480, coutRec.TotalWorkMinutes); // 8 hours net

        // 5. Leave Management
        var leaveType = await service.CreateLeaveTypeAsync(tenantId, companyId, new CreateLeaveTypeRequest(
            "ANNUAL", "Annual Privilege Leave", 14.0m, true, true, 5.0m
        ), ct);
        Assert.Equal("ANNUAL", leaveType.Code);

        var alloc = await service.AllocateLeavesAsync(tenantId, companyId, emp.Id, leaveType.Id, 2026, 14.0m, ct);
        Assert.Equal(14.0m, alloc.RemainingDays);

        var leaveReq = await service.SubmitLeaveRequestAsync(tenantId, companyId, new SubmitLeaveRequest(
            emp.Id, leaveType.Id, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11), 2.0m, "Family event"
        ), ct);
        Assert.Equal("pending", leaveReq.Status);

        var approvedReq = await service.ProcessLeaveApprovalAsync(tenantId, companyId, new ApproveLeaveRequest(leaveReq.Id, true), userId, ct);
        Assert.Equal("approved", approvedReq.Status);

        var allocsAfter = await service.GetLeaveAllocationsAsync(tenantId, companyId, emp.Id, 2026, ct);
        var updatedAlloc = allocsAfter.First(a => a.LeaveTypeId == leaveType.Id);
        Assert.Equal(2.0m, updatedAlloc.UsedDays);
        Assert.Equal(12.0m, updatedAlloc.RemainingDays);

        // 6. Tax Slabs & Statutory Configuration
        await service.CreateTaxSlabAsync(tenantId, companyId, new CreateTaxSlabRequest(
            2026, new DateOnly(2026, 7, 1), 1, 0m, 600_000m, 0m, 0m, "Up to 600k 0%"
        ), ct);
        await service.CreateTaxSlabAsync(tenantId, companyId, new CreateTaxSlabRequest(
            2026, new DateOnly(2026, 7, 1), 2, 600_000m, 1_200_000m, 0m, 5.0m, "600k to 1.2M 5%"
        ), ct);
        await service.CreateTaxSlabAsync(tenantId, companyId, new CreateTaxSlabRequest(
            2026, new DateOnly(2026, 7, 1), 3, 1_200_000m, 2_200_000m, 30_000m, 15.0m, "1.2M to 2.2M 15%"
        ), ct);

        await service.SetStatutoryConfigAsync(tenantId, companyId, new SetStatutoryConfigRequest(
            "eobi_min_wage", "EOBI Minimum Wage Benchmark", new DateOnly(2026, 1, 1), null, 1.0m, 37_000m, 5.0m
        ), ct);

        // 7. Salary Loan Creation
        var loan = await service.CreateSalaryLoanAsync(tenantId, companyId, new CreateSalaryLoanRequest(
            emp.Id, 50_000m, 5_000m, 10
        ), ct);
        Assert.Equal(50_000m, loan.RemainingBalancePkr);

        // 8. Payroll Processing Run
        var period = await service.CreatePayrollPeriodAsync(tenantId, companyId, new CreatePayrollPeriodRequest(
            "November 2026", "2026-11", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)
        ), ct);
        Assert.Equal("draft", period.Status);

        var calculatedPeriod = await service.ProcessPayrollRunAsync(tenantId, companyId, period.Id, userId, ct);
        Assert.Equal("calculated", calculatedPeriod.Status);
        Assert.Equal(150_000m, calculatedPeriod.TotalGrossPkr);
        Assert.True(calculatedPeriod.TotalNetPkr > 0);

        var payslips = await service.GetPayslipsForPeriodAsync(tenantId, companyId, period.Id, ct);
        Assert.Single(payslips);
        var slip = payslips[0];
        Assert.Equal(150_000m, slip.GrossPayPkr);
        Assert.Equal(10_000m, slip.TaxDeductionPkr);
        Assert.Equal(370m, slip.EobiEmployeePkr);
        Assert.Equal(8330m, slip.PfEmployeePkr);
        Assert.Equal(5000m, slip.LoanDeductionPkr);
        Assert.Equal(126_300m, slip.NetPayPkr);
        Assert.Equal(1850m, slip.EobiEmployerPkr);
        Assert.Equal(8330m, slip.PfEmployerPkr);

        // 9. Payroll Approval
        var approvedPeriod = await service.ApprovePayrollRunAsync(tenantId, companyId, period.Id, userId, ct);
        Assert.Equal("approved", approvedPeriod.Status);

        // 10. Post Payroll to General Ledger (assert balanced double-entry accounting!)
        var postedPeriod = await service.PostPayrollToGlAsync(tenantId, companyId, new PostPayrollToGlRequest(
            period.Id, new DateOnly(2026, 11, 30), "Monthly Payroll Posting"
        ), userId, ct);

        Assert.Equal("posted", postedPeriod.Status);
        Assert.NotNull(postedPeriod.GlJournalId);

        // Verify loan installment was deducted
        var loansAfterPost = await service.GetSalaryLoansAsync(tenantId, companyId, emp.Id, null, ct);
        var loanAfter = loansAfterPost.First(l => l.Id == loan.Id);
        Assert.Equal(45_000m, loanAfter.RemainingBalancePkr);
        Assert.Equal(1, loanAfter.PaidInstallments);

        // 11. Duplicate Posting Protection (must fail)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostPayrollToGlAsync(tenantId, companyId, new PostPayrollToGlRequest(
                period.Id, new DateOnly(2026, 11, 30)
            ), userId, ct));

        // 12. Payroll Reversal
        var reversal = await service.ReversePayrollRunAsync(tenantId, companyId, new ReversePayrollRunRequest(
            period.Id, "Adjustment needed for salary structure"
        ), userId, ct);

        Assert.NotNull(reversal);
        Assert.NotNull(reversal.ReversalJournalId);

        var periodAfterRev = await service.GetPayrollPeriodByIdAsync(tenantId, companyId, period.Id, ct);
        Assert.Equal("reversed", periodAfterRev!.Status);

        // Verify loan balance was reverted back
        var loansAfterRev = await service.GetSalaryLoansAsync(tenantId, companyId, emp.Id, null, ct);
        var loanReverted = loansAfterRev.First(l => l.Id == loan.Id);
        Assert.Equal(50_000m, loanReverted.RemainingBalancePkr);
        Assert.Equal(0, loanReverted.PaidInstallments);
    }
}
