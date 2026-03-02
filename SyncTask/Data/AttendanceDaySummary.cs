namespace SyncTask.Data;

public sealed record AttendanceDaySummary(
    DateTime Date,
    bool HasAttendance,
    TimeSpan? StartTime,
    TimeSpan? EndTime,
    decimal WorkingHours);

public sealed record AttendanceMonthlyReport(
    DateTime TargetMonth,
    DateTime PeriodStartDate,
    DateTime PeriodEndDate,
    IReadOnlyList<AttendanceDaySummary> Days,
    int WorkingDays,
    decimal TotalWorkingHours);
