using System.Globalization;
using OfficeOpenXml;
using SyncTask.Data;

namespace SyncTask.Services;

public class AttendanceExcelService
{
    // -----------------------------------------------------------------------
    // セル番地定数 — テンプレートに合わせてここを修正
    // -----------------------------------------------------------------------
    private const int DataStartRow = 9;           // 日付データ開始行
    private const string DateColumn = "B";        // 日付列
    private const string DayOfWeekColumn = "C";   // 曜日列
    private const string AttendanceColumn = "D";  // 出勤○列
    private const string StartTimeColumn = "F";   // 出勤時刻列
    private const string EndTimeColumn = "G";     // 退勤時刻列
    private const string WorkHoursColumn = "H";   // 勤務時間列
    private const string TargetMonthCellLeft = "H3";     // 対象月(左)
    private const string TargetMonthCellRight = "P3";    // 対象月(右)
    private const string TargetYearCellLeft = "G3";      // 対象年(左)
    private const string TargetYearCellRight = "O3";     // 対象年(右)
    private const string SubmittedAtCellLeft = "K2";     // 提出日(左)
    private const string SubmittedAtCellRight = "S2";    // 提出日(右)
    private const int ProjectHoursStartRow = 9;           // プロジェクト別工数開始行
    private const string ProjectNameColumn = "P";        // プロジェクト名列
    private const string ProjectHoursColumn = "R";       // プロジェクト工数列
    // -----------------------------------------------------------------------

    private const string TemplateName = "attendance_template.xlsx";

    public async Task<byte[]> GenerateAsync(
        AttendanceMonthlyReport report,
        IReadOnlyList<ProjectHourEntry> projectHours)
    {
        ExcelPackage.License.SetNonCommercialPersonal("Takashi Oka");

        await using var templateStream = await FileSystem.OpenAppPackageFileAsync(TemplateName);
        using var package = new ExcelPackage(templateStream);

        var sheet = package.Workbook.Worksheets[0];
        var generatedAt = DateTime.Now;

        sheet.Cells[TargetMonthCellLeft].Value = report.TargetMonth.Month;
        sheet.Cells[TargetMonthCellRight].Value = report.TargetMonth.Month;
        sheet.Cells[TargetYearCellLeft].Value = $"{report.TargetMonth:yyyy}年";
        sheet.Cells[TargetYearCellRight].Value = $"{report.TargetMonth:yyyy}年";
        sheet.Cells[SubmittedAtCellLeft].Value = $"{generatedAt:yyyy年M月d日}提出";
        sheet.Cells[SubmittedAtCellRight].Value = $"{generatedAt:yyyy年M月d日}提出";

        for (var i = 0; i < report.Days.Count; i++)
        {
            var day = report.Days[i];
            var row = DataStartRow + i;

            // 日付・曜日
            sheet.Cells[$"{DateColumn}{row}"].Value = day.Date.Day;
            sheet.Cells[$"{DayOfWeekColumn}{row}"].Value = GetWeekdayLabel(day.Date);

            // 出勤○
            sheet.Cells[$"{AttendanceColumn}{row}"].Value =
                day.HasAttendance ? "○" : string.Empty;

            // 出勤・退勤時刻（HH:mm 文字列）
            sheet.Cells[$"{StartTimeColumn}{row}"].Value =
                FormatTime(day.StartTime);
            sheet.Cells[$"{EndTimeColumn}{row}"].Value =
                FormatTime(day.EndTime);

            // 勤務時間（数値）
            var workHoursCell = sheet.Cells[$"{WorkHoursColumn}{row}"];
            if (day.WorkingHours > 0)
            {
                // Excel は 1.0 = 24時間 のため、時間を日数に変換して設定する。
                workHoursCell.Value = (double)(day.WorkingHours / 24m);
                workHoursCell.Style.Numberformat.Format = "[h]:mm";
            }
            else
            {
                workHoursCell.Value = string.Empty;
            }
        }

        // プロジェクト別工数は同一シートの P/R 列へ出力
        WriteProjectHours(sheet, projectHours);

        return await package.GetAsByteArrayAsync();
    }

    private static void WriteProjectHours(
        ExcelWorksheet sheet,
        IReadOnlyList<ProjectHourEntry> projectHours)
    {
        if (projectHours.Count == 0)
        {
            return;
        }

        for (var i = 0; i < projectHours.Count; i++)
        {
            var entry = projectHours[i];
            var row = ProjectHoursStartRow + i;

            sheet.Cells[$"{ProjectNameColumn}{row}"].Value = entry.ProjectName;

            var hoursCell = sheet.Cells[$"{ProjectHoursColumn}{row}"];
            hoursCell.Value = (double)(entry.Hours / 24m);
            hoursCell.Style.Numberformat.Format = "[h]:mm";
        }
    }

    private static string GetWeekdayLabel(DateTime date) =>
        date.DayOfWeek switch
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

    private static string FormatTime(TimeSpan? value) =>
        value.HasValue
            ? value.Value.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
            : string.Empty;
}

/// <summary>プロジェクト名と工数の集計エントリ（Attendance.razor.cs の内部 record と共有）。</summary>
/// <remarks>
/// Attendance.razor.cs の <c>ProjectHourEntry</c> はネスト private のため、
/// このサービスが受け取れるよう public record として定義。
/// Attendance.razor.cs 側も同じ型に置き換えること。
/// </remarks>
public sealed record ProjectHourEntry(string ProjectName, decimal Hours);
