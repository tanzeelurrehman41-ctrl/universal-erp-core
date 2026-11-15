using System;

namespace UniversalErp.Application.HrPayroll;

public class LeaveAccrualEngine
{
    /// <summary>
    /// Validates if employee has enough remaining leave balance for a requested leave duration.
    /// </summary>
    public static bool CanApproveLeave(LeaveAllocationDto allocation, decimal requestedDays, out string? errorMessage)
    {
        if (requestedDays <= 0)
        {
            errorMessage = "Requested leave days must be greater than zero.";
            return false;
        }

        if (allocation.RemainingDays < requestedDays)
        {
            errorMessage = $"Insufficient leave balance. Requested: {requestedDays} days, Available: {allocation.RemainingDays} days.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Computes the new remaining balance after deducting approved leave days.
    /// </summary>
    public static (decimal NewUsedDays, decimal NewRemainingDays) ApplyLeaveDeduction(
        LeaveAllocationDto allocation,
        decimal daysToDeduct)
    {
        decimal newUsed = allocation.UsedDays + daysToDeduct;
        decimal newRemaining = Math.Max(0, (allocation.AllocatedDays + allocation.CarriedForwardDays) - newUsed);
        return (newUsed, newRemaining);
    }

    /// <summary>
    /// Calculates carry-forward rollover into a new calendar year subject to configured limits.
    /// </summary>
    public static decimal CalculateCarryForward(
        LeaveTypeDto leaveType,
        decimal previousYearRemaining)
    {
        if (!leaveType.IsCarryForwardAllowed || previousYearRemaining <= 0)
        {
            return 0m;
        }

        if (leaveType.MaxCarryForwardDays > 0)
        {
            return Math.Min(previousYearRemaining, leaveType.MaxCarryForwardDays);
        }

        return previousYearRemaining;
    }
}
