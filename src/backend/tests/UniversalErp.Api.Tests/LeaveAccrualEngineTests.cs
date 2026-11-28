using System;
using UniversalErp.Application.HrPayroll;
using Xunit;

namespace UniversalErp.Api.Tests;

public class LeaveAccrualEngineTests
{
    [Fact]
    public void CanApproveLeave_ValidatesRemainingBalance()
    {
        var alloc = new LeaveAllocationDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "ANNUAL", "Annual Leave", 2026,
            AllocatedDays: 14m,
            UsedDays: 4m,
            PendingDays: 0m,
            CarriedForwardDays: 0m,
            RemainingDays: 10m,
            CreatedAt: DateTimeOffset.UtcNow
        );

        // Request 5 days (<= 10) -> Valid
        bool canApprove = LeaveAccrualEngine.CanApproveLeave(alloc, 5m, out var err);
        Assert.True(canApprove);
        Assert.Null(err);

        // Request 12 days (> 10) -> Fails
        bool cannotApprove = LeaveAccrualEngine.CanApproveLeave(alloc, 12m, out var errOver);
        Assert.False(cannotApprove);
        Assert.NotNull(errOver);
        Assert.Contains("Insufficient leave balance", errOver);
    }

    [Fact]
    public void ApplyLeaveDeduction_DeductsFromRemainingAndIncrementsUsed()
    {
        var alloc = new LeaveAllocationDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "SICK", "Sick Leave", 2026,
            AllocatedDays: 10m,
            UsedDays: 2m,
            PendingDays: 0m,
            CarriedForwardDays: 2m,
            RemainingDays: 10m, // 10 + 2 - 2 = 10
            CreatedAt: DateTimeOffset.UtcNow
        );

        var (newUsed, newRemaining) = LeaveAccrualEngine.ApplyLeaveDeduction(alloc, 3m);

        Assert.Equal(5m, newUsed);     // 2 + 3 = 5
        Assert.Equal(7m, newRemaining); // 12 - 5 = 7
    }

    [Fact]
    public void CalculateCarryForward_EnforcesConfiguredMaxLimit()
    {
        var leaveTypeWithCap = new LeaveTypeDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ANNUAL", "Annual Leave",
            DefaultAnnualQuota: 14m,
            IsPaid: true,
            IsCarryForwardAllowed: true,
            MaxCarryForwardDays: 5m,
            IsEncashable: false,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow
        );

        // 8 remaining days rollover -> capped at 5
        decimal carryCapped = LeaveAccrualEngine.CalculateCarryForward(leaveTypeWithCap, 8m);
        Assert.Equal(5m, carryCapped);

        // 3 remaining days rollover -> full 3
        decimal carryUnder = LeaveAccrualEngine.CalculateCarryForward(leaveTypeWithCap, 3m);
        Assert.Equal(3m, carryUnder);

        // If carry-forward is not allowed
        var leaveTypeNoCarry = leaveTypeWithCap with { IsCarryForwardAllowed = false };
        decimal carryBlocked = LeaveAccrualEngine.CalculateCarryForward(leaveTypeNoCarry, 8m);
        Assert.Equal(0m, carryBlocked);
    }
}
