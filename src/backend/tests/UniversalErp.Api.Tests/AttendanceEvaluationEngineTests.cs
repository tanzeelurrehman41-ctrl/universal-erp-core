using System;
using System.Collections.Generic;
using UniversalErp.Application.HrPayroll;
using Xunit;

namespace UniversalErp.Api.Tests;

public class AttendanceEvaluationEngineTests
{
    private static ShiftDto CreateStandardShift() => new ShiftDto(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "MORNING", "Morning Shift",
        new TimeOnly(9, 0), new TimeOnly(18, 0),
        BreakDurationMinutes: 60,
        GracePeriodMinutes: 15,
        HalfDayThresholdHours: 4.0m,
        FullDayHours: 8.0m,
        IsActive: true,
        CreatedAt: DateTimeOffset.UtcNow
    );

    [Fact]
    public void EvaluateAttendance_OnTimeAndFullDay_MarksPresentWithZeroLateAndOvertime()
    {
        var shift = CreateStandardShift();
        var today = DateTime.UtcNow.Date;

        // Check in 09:05 (within 15 min grace), check out 18:05 (9 hours total - 1 hour break = 8 hours net)
        var cin = new DateTimeOffset(today.AddHours(9).AddMinutes(5), TimeSpan.Zero);
        var cout = new DateTimeOffset(today.AddHours(18).AddMinutes(5), TimeSpan.Zero);

        var result = AttendanceEvaluationEngine.EvaluateAttendance(shift, cin, cout);

        Assert.Equal("present", result.Status);
        Assert.Equal(0, result.LateMinutes);
        Assert.Equal(480, result.TotalWorkMinutes); // 8 hours
        Assert.Equal(0, result.OvertimeMinutes);
    }

    [Fact]
    public void EvaluateAttendance_LateCheckInAndOvertime_CalculatesBothAccurately()
    {
        var shift = CreateStandardShift();
        var today = DateTime.UtcNow.Date;

        // Check in 09:30 (grace ends 09:15, so 30 min late relative to shift start)
        // Check out 20:30 (11 hours duration - 1 hour break = 10 hours net -> 2 hours overtime)
        var cin = new DateTimeOffset(today.AddHours(9).AddMinutes(30), TimeSpan.Zero);
        var cout = new DateTimeOffset(today.AddHours(20).AddMinutes(30), TimeSpan.Zero);

        var result = AttendanceEvaluationEngine.EvaluateAttendance(shift, cin, cout);

        Assert.Equal("present", result.Status);
        Assert.Equal(30, result.LateMinutes);
        Assert.Equal(600, result.TotalWorkMinutes); // 10 hours
        Assert.Equal(120, result.OvertimeMinutes);  // 2 hours overtime
    }

    [Fact]
    public void EvaluateAttendance_PartialHours_ClassifiesHalfDayOrAbsent()
    {
        var shift = CreateStandardShift();
        var today = DateTime.UtcNow.Date;

        // Worked 5 hours gross - 1 hr break = 4 hours net -> qualifies for half_day (threshold 4.0)
        var cin = new DateTimeOffset(today.AddHours(9), TimeSpan.Zero);
        var coutHalf = new DateTimeOffset(today.AddHours(14), TimeSpan.Zero);
        var halfResult = AttendanceEvaluationEngine.EvaluateAttendance(shift, cin, coutHalf);
        Assert.Equal("half_day", halfResult.Status);

        // Worked 3 hours gross - 1 hr break = 2 hours net (< 4.0 threshold) -> absent
        var coutShort = new DateTimeOffset(today.AddHours(12), TimeSpan.Zero);
        var shortResult = AttendanceEvaluationEngine.EvaluateAttendance(shift, cin, coutShort);
        Assert.Equal("absent", shortResult.Status);

        // No check-in at all -> absent
        var noCheckInResult = AttendanceEvaluationEngine.EvaluateAttendance(shift, null, null);
        Assert.Equal("absent", noCheckInResult.Status);
    }

    [Fact]
    public void SummarizeAttendance_AggregatesPeriodCountsAndPayableDays()
    {
        var empId = Guid.NewGuid();
        var start = new DateOnly(2026, 11, 1);
        var end = new DateOnly(2026, 11, 5); // 5 days

        var records = new List<AttendanceRecordDto>
        {
            new AttendanceRecordDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), empId, "EMP-1", "A", new DateOnly(2026, 11, 1), null, null, 480, 0, "present", 0, false, null, null, DateTimeOffset.UtcNow),
            new AttendanceRecordDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), empId, "EMP-1", "A", new DateOnly(2026, 11, 2), null, null, 240, 0, "half_day", 0, false, null, null, DateTimeOffset.UtcNow),
            new AttendanceRecordDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), empId, "EMP-1", "A", new DateOnly(2026, 11, 3), null, null, 0, 0, "on_leave", 0, false, null, null, DateTimeOffset.UtcNow),
        };

        var summary = AttendanceEvaluationEngine.SummarizeAttendance(
            empId, "EMP-1", "Ali Khan", start, end, records, holidayDates: new[] { new DateOnly(2026, 11, 4) }
        );

        Assert.Equal(5, summary.TotalDaysInPeriod);
        Assert.Equal(1, summary.PresentDays);
        Assert.Equal(1, summary.HalfDays);
        Assert.Equal(1, summary.LeaveDays);
        Assert.Equal(1, summary.HolidayDays);
        Assert.Equal(1, summary.AbsentDays); // Day 5 has no record and is not holiday
        // Payable days = 1 present + (1 * 0.5 half day) + 1 leave + 1 holiday = 3.5 days
        Assert.Equal(3.5m, summary.PayableDays);
    }
}
