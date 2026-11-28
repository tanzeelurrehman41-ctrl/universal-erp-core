using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalErp.Application.HrPayroll;

public record EvaluatedAttendance(
    int TotalWorkMinutes,
    int OvertimeMinutes,
    int LateMinutes,
    string Status // "present", "half_day", "absent"
);

public class AttendanceEvaluationEngine
{
    /// <summary>
    /// Evaluates check-in/out timestamps against shift parameters to determine status, late arrival, work duration, and overtime.
    /// </summary>
    public static EvaluatedAttendance EvaluateAttendance(
        ShiftDto shift,
        DateTimeOffset? checkIn,
        DateTimeOffset? checkOut,
        bool isOnApprovedLeave = false,
        bool isHoliday = false)
    {
        if (isOnApprovedLeave)
        {
            return new EvaluatedAttendance(0, 0, 0, "on_leave");
        }

        if (isHoliday)
        {
            return new EvaluatedAttendance(0, 0, 0, "holiday");
        }

        if (!checkIn.HasValue)
        {
            return new EvaluatedAttendance(0, 0, 0, "absent");
        }

        // 1. Late arrival evaluation
        int lateMinutes = 0;
        var checkInLocalTime = TimeOnly.FromTimeSpan(checkIn.Value.TimeOfDay);
        var shiftStartTime = shift.StartTime;
        var graceEnd = shiftStartTime.AddMinutes(shift.GracePeriodMinutes);

        if (checkInLocalTime > graceEnd)
        {
            var diff = checkInLocalTime.ToTimeSpan() - shiftStartTime.ToTimeSpan();
            lateMinutes = Math.Max(0, (int)diff.TotalMinutes);
        }

        // If no check-out recorded yet
        if (!checkOut.HasValue)
        {
            return new EvaluatedAttendance(0, 0, lateMinutes, "half_day");
        }

        // 2. Work duration evaluation
        var duration = checkOut.Value - checkIn.Value;
        int rawMinutes = Math.Max(0, (int)duration.TotalMinutes);
        int netWorkMinutes = Math.Max(0, rawMinutes - shift.BreakDurationMinutes);
        decimal netWorkHours = netWorkMinutes / 60.0m;

        // 3. Status determination based on thresholds
        string status;
        int overtimeMinutes = 0;

        if (netWorkHours >= shift.FullDayHours)
        {
            status = "present";
            decimal overtimeHours = netWorkHours - shift.FullDayHours;
            overtimeMinutes = Math.Max(0, (int)(overtimeHours * 60m));
        }
        else if (netWorkHours >= shift.HalfDayThresholdHours)
        {
            status = "half_day";
        }
        else
        {
            status = "absent";
        }

        return new EvaluatedAttendance(netWorkMinutes, overtimeMinutes, lateMinutes, status);
    }

    /// <summary>
    /// Aggregates attendance records across a payroll period for an employee.
    /// </summary>
    public static AttendanceSummaryDto SummarizeAttendance(
        Guid employeeId,
        string employeeCode,
        string employeeName,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<AttendanceRecordDto> records,
        IReadOnlyList<DateOnly>? holidayDates = null)
    {
        int totalDays = (periodEnd.DayNumber - periodStart.DayNumber) + 1;
        var recordMap = records.ToDictionary(r => r.AttendanceDate);
        var holidaySet = (holidayDates ?? Array.Empty<DateOnly>()).ToHashSet();

        int presentCount = 0;
        int halfDayCount = 0;
        int leaveCount = 0;
        int absentCount = 0;
        int holidayCount = 0;
        int restDayCount = 0;
        int totalWorkMinutes = 0;
        int totalOvertimeMinutes = 0;

        for (var date = periodStart; date <= periodEnd; date = date.AddDays(1))
        {
            if (recordMap.TryGetValue(date, out var record))
            {
                totalWorkMinutes += record.TotalWorkMinutes;
                totalOvertimeMinutes += record.OvertimeMinutes;

                switch (record.Status)
                {
                    case "present":
                        presentCount++;
                        break;
                    case "half_day":
                        halfDayCount++;
                        break;
                    case "on_leave":
                        leaveCount++;
                        break;
                    case "holiday":
                        holidayCount++;
                        break;
                    case "rest_day":
                        restDayCount++;
                        break;
                    default:
                        absentCount++;
                        break;
                }
            }
            else if (holidaySet.Contains(date))
            {
                holidayCount++;
            }
            else if (date.DayOfWeek == DayOfWeek.Sunday)
            {
                restDayCount++;
            }
            else
            {
                absentCount++;
            }
        }

        decimal payableDays = presentCount + (halfDayCount * 0.5m) + leaveCount + holidayCount + restDayCount;

        return new AttendanceSummaryDto(
            employeeId,
            employeeCode,
            employeeName,
            totalDays,
            presentCount,
            halfDayCount,
            leaveCount,
            absentCount,
            holidayCount,
            restDayCount,
            Math.Round(totalWorkMinutes / 60.0m, 2),
            Math.Round(totalOvertimeMinutes / 60.0m, 2),
            payableDays
        );
    }
}
