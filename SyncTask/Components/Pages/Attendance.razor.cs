using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SyncTask.Data;
using SyncTask.Services;

namespace SyncTask.Components.Pages;

public partial class Attendance
{
    [Inject] private DailyLogService DailyLogService { get; set; } = default!;
    [Inject] private IHolidayService HolidayService { get; set; } = default!;
    [Inject] private RedmineService RedmineService { get; set; } = default!;
    [Inject] private AttendanceExcelService ExcelService { get; set; } = default!;
    [Inject] private IFileSaveService FileSaveService { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    private AttendanceMonthlyReport report = new(
        new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
        new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1).AddDays(20),
        new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(19),
        [],
        0,
        0);

    private DateTime targetMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string? statusMessage;
    private List<AttendanceEditableRow> editableDays = [];
    private List<ProjectHourEntry> projectHours = [];
    private bool isLoadingRedmine;
    private bool isGenerating;
    private bool isReminderDismissed;
    private const string ReminderLocalStorageKey = "attendance_reminder_dismissed_month";

    private bool ShowReminder => !isReminderDismissed && DateTime.Today.Day >= 21;

    private string TargetMonthInput => targetMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    private string PeriodLabel => $"{report.TargetMonth:yyyy年M月} 締め ({report.PeriodStartDate:MM/dd} ～ {report.PeriodEndDate:MM/dd})";
    private int WorkingDays => editableDays.Count(day => day.WorkingHours > 0);
    private decimal TotalWorkingHours => editableDays.Sum(day => day.WorkingHours);

    protected override async Task OnInitializedAsync()
    {
        var dismissedMonth = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", ReminderLocalStorageKey);
        var currentMonth = DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        isReminderDismissed = dismissedMonth == currentMonth;

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
        editableDays = [.. report.Days.Select(day => new AttendanceEditableRow
        {
            Date = day.Date,
            HasAttendance = day.HasAttendance,
            StartTimeInput = ToTimeInput(day.StartTime),
            EndTimeInput = ToTimeInput(day.EndTime),
            WorkingHoursInput = ToHoursInput(day.WorkingHours),
            WorkingHours = day.WorkingHours
        })];
        projectHours = [];
    }

    private async Task SaveDayAsync(AttendanceEditableRow day)
    {
        if (!TryParseNullableTime(day.StartTimeInput, out var startTime)
            || !TryParseNullableTime(day.EndTimeInput, out var endTime)
            || !TryParseNullableHours(day.WorkingHoursInput, out var workingHours))
        {
            statusMessage = $"{day.Date:MM/dd} の入力形式が正しくありません。時刻は HH:mm または HH:mm:ss、勤務時間は数値で入力してください。";
            return;
        }

        day.IsSaving = true;
        statusMessage = null;

        try
        {
            await DailyLogService.SaveAttendanceManualAsync(day.Date, startTime, endTime, workingHours);
            day.HasAttendance = startTime.HasValue || endTime.HasValue || workingHours.GetValueOrDefault() > 0;
            day.WorkingHours = workingHours.GetValueOrDefault();
            statusMessage = $"{day.Date:MM/dd} を保存しました。";
        }
        finally
        {
            day.IsSaving = false;
        }
    }

    private static void OnStartTimeInputChanged(AttendanceEditableRow day, ChangeEventArgs args)
    {
        day.StartTimeInput = args?.Value?.ToString() ?? string.Empty;
    }

    private static void OnEndTimeInputChanged(AttendanceEditableRow day, ChangeEventArgs args)
    {
        day.EndTimeInput = args?.Value?.ToString() ?? string.Empty;
    }

    private static void OnWorkingHoursInputChanged(AttendanceEditableRow day, ChangeEventArgs args)
    {
        day.WorkingHoursInput = args?.Value?.ToString() ?? string.Empty;
    }

    private static bool TryParseNullableTime(string? raw, out TimeSpan? value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = null;
            return true;
        }

        var normalized = raw.Trim();
        var formats = new[] { @"hh\:mm", @"hh\:mm\:ss", @"h\:mm", @"h\:mm\:ss" };

        if (TimeSpan.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, out var parsed)
            || TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out parsed)
            || TimeSpan.TryParse(normalized, CultureInfo.CurrentCulture, out parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryParseNullableHours(string? raw, out decimal? value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = null;
            return true;
        }

        if (decimal.TryParse(raw, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var invariantParsed)
            || decimal.TryParse(raw, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out invariantParsed))
        {
            value = Math.Max(0, invariantParsed);
            return true;
        }

        value = null;
        return false;
    }

    private static string ToTimeInput(TimeSpan? value)
    {
        return value.HasValue ? value.Value.ToString(@"hh\:mm", CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string ToHoursInput(decimal value)
    {
        return value == 0 ? string.Empty : value.ToString("0.##", CultureInfo.InvariantCulture);
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

    private async Task LoadRedmineHoursAsync()
    {
        isLoadingRedmine = true;
        statusMessage = null;
        StateHasChanged();

        try
        {
            var settings = await DailyLogService.GetOrCreateRedmineSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                statusMessage = "Redmineの設定が未入力です。設定画面でBaseURLとAPIキーを入力してください。";
                return;
            }

            var entries = await RedmineService.GetTimeEntriesForPeriodAsync(
                settings, report.PeriodStartDate, report.PeriodEndDate);

            projectHours = entries
                .Where(e => e.Project is not null)
                .GroupBy(e => e.Project!.Name)
                .Select(g => new ProjectHourEntry(g.Key, g.Sum(e => e.Hours)))
                .OrderByDescending(e => e.Hours)
                .ToList();
        }
        catch (Exception ex)
        {
            statusMessage = $"Redmine工数の取得に失敗しました: {ex.Message}";
        }
        finally
        {
            isLoadingRedmine = false;
        }
    }

    private static string FormatHours(decimal hours)
    {
        return hours == 0 ? string.Empty : hours.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private async Task DismissReminder()
    {
        isReminderDismissed = true;
        var currentMonth = DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        await JSRuntime.InvokeVoidAsync("localStorage.setItem", ReminderLocalStorageKey, currentMonth);
    }

    private async Task GenerateExcelAsync()
    {
        isGenerating = true;
        statusMessage = null;
        StateHasChanged();

        try
        {
            // 月変更直後などで projectHours が空の場合、生成前に最新を取得する。
            if (projectHours.Count == 0)
            {
                await LoadRedmineHoursAsync();
            }

            var bytes = await ExcelService.GenerateAsync(report, projectHours);
            var fileName = $"出勤簿_{targetMonth:yyyy_MM}.xlsx";
            var saved = await FileSaveService.SaveFileAsync(fileName, bytes);
            statusMessage = saved ? $"{fileName} を保存しました。" : null;
        }
        catch (Exception ex)
        {
            statusMessage = $"出勤簿の生成に失敗しました: {ex.Message}";
        }
        finally
        {
            isGenerating = false;
        }
    }

    private sealed class AttendanceEditableRow
    {
        public DateTime Date { get; set; }

        public bool HasAttendance { get; set; }

        public string StartTimeInput { get; set; } = string.Empty;

        public string EndTimeInput { get; set; } = string.Empty;

        public string WorkingHoursInput { get; set; } = string.Empty;

        public decimal WorkingHours { get; set; }

        public bool IsSaving { get; set; }
    }

}
