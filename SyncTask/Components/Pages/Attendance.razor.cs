using System.Globalization;
using Microsoft.AspNetCore.Components;
using SyncTask.Data;
using SyncTask.Services;

namespace SyncTask.Components.Pages;

public partial class Attendance
{
    [Inject] private DailyLogService DailyLogService { get; set; } = default!;
    [Inject] private IHolidayService HolidayService { get; set; } = default!;

    private AttendanceMonthlyReport report = new(
        new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
        new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1).AddDays(20),
        new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(19),
        Array.Empty<AttendanceDaySummary>(),
        0,
        0);

    private DateTime targetMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string? statusMessage;

    private string TargetMonthInput => targetMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    protected override async Task OnInitializedAsync()
    {
        await LoadReportAsync();
    }

    private async Task OnMonthChangedAsync(ChangeEventArgs args)
    {
        var raw = args?.Value?.ToString();
        if (DateTime.TryParseExact(raw, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            targetMonth = new DateTime(parsed.Year, parsed.Month, 1);
            await LoadReportAsync();
        }
    }

    private async Task MoveMonthAsync(int offset)
    {
        targetMonth = targetMonth.AddMonths(offset);
        await LoadReportAsync();
    }

    private async Task LoadReportAsync()
    {
        report = await DailyLogService.GetAttendanceMonthlyReportAsync(targetMonth);
        statusMessage = $"{report.TargetMonth:yyyy年M月} 締め ({report.PeriodStartDate:MM/dd} ～ {report.PeriodEndDate:MM/dd})";
    }

    private static string GetWeekdayLabel(DateTime date)
    {
        return date.DayOfWeek switch
        {
            DayOfWeek.Sunday => "日",
            DayOfWeek.Monday => "月",
            DayOfWeek.Tuesday => "火",
            DayOfWeek.Wednesday => "水",
            DayOfWeek.Thursday => "木",
            DayOfWeek.Friday => "金",
            DayOfWeek.Saturday => "土",
            _ => string.Empty
        };
    }

    private string GetDayRowClass(DateTime date)
    {
        if (HolidayService.IsHoliday(date) || date.DayOfWeek == DayOfWeek.Sunday)
        {
            return "attendance-day--holiday";
        }

        if (date.DayOfWeek == DayOfWeek.Saturday)
        {
            return "attendance-day--saturday";
        }

        return string.Empty;
    }

    private static string FormatTime(TimeSpan? time)
    {
        return time.HasValue ? time.Value.ToString("hh\\:mm", CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string FormatHours(decimal hours)
    {
        return hours == 0 ? string.Empty : hours.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
