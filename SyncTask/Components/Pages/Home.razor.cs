using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using SyncTask.Data;
using SyncTask.Services;
using WebDragEventArgs = Microsoft.AspNetCore.Components.Web.DragEventArgs;

namespace SyncTask.Components.Pages;

public partial class Home
{
    [Inject] private DailyLogService DailyLogService { get; set; } = default!;
    [Inject] private RedmineService RedmineService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ILogger<Home> Logger { get; set; } = default!;

    private const string MailBodyTemplateAssetPath = "report_template.txt";
    private const string DefaultMailBodyTemplate = "寺田様：\n\nお疲れ様です。岡です。\n\n${date}の作業報告をお送りいたします。\n\n■作業時間\n\n${worktime_section}\n\n■作業内容\n${content_section}\n\n■作業予定(${next_date})\n ●\n  ・";
    private const string BreakProjectOptionValue = "__BREAK_PRIVATE__";
    private const string BreakProjectName = "休憩・私用";

    private DateTime workDate = DateTime.Today;
    private WorkLog? currentLog;
    private List<WorkLogEntry> entries = new();
    private SyncTask.Data.RedmineSettings redmineSettings = new();
    private List<RedmineProject> redmineProjects = new();
    private readonly Dictionary<int, RedmineIssueSplitResult> projectIssueCache = new();
    private bool isLoadingRedmine;
    private string? redmineStatusMessage;
    private string generatedMailBody = string.Empty;
    private WorkLogEntry? draggedEntry;
    private WorkLogEntry? dragTargetEntry;
    private int? dragInsertIndex;
    private DotNetObjectReference<Home>? dotNetRef;

    protected override async Task OnInitializedAsync()
    {
        redmineSettings = await DailyLogService.GetOrCreateRedmineSettingsAsync();
        await LoadAsync();
        await LoadProjectsAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        dotNetRef ??= DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("syncTaskDnd.attachTableDropZone", "daily-log-tbody", dotNetRef);

        if (firstRender)
        {
            Logger.LogInformation("[DND:init] JS drop-zone attached");
        }
    }

    private async Task OnWorkDateChanged(ChangeEventArgs args)
    {
        if (DateTime.TryParse(args?.Value?.ToString(), out var newDate))
        {
            workDate = newDate.Date;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        currentLog = await DailyLogService.GetOrCreateDailyLogAsync(workDate);
        entries = await DailyLogService.GetEntriesAsync(currentLog.Id);
        RecalculateStartTimesFrom(1);
        EnsureTrailingEmptyRow();
        await PreloadProjectIssuesAsync(entries);
    }

    private async Task DeleteGridRowAsync(WorkLogEntry entry)
    {
        var removedIndex = entries.IndexOf(entry);
        entries.Remove(entry);

        if (entry.Id != 0)
        {
            await DailyLogService.DeleteEntryAsync(entry);
        }

        if (removedIndex >= 0)
        {
            RecalculateStartTimesFrom(Math.Max(1, removedIndex));
        }

        EnsureTrailingEmptyRow();
    }

    private Task InsertGridRowAsync(WorkLogEntry entry)
    {
        var insertAfterIndex = entries.IndexOf(entry);
        if (insertAfterIndex < 0)
        {
            return Task.CompletedTask;
        }

        var inheritedStart = entry.ActualEndTime;
        var inserted = new WorkLogEntry
        {
            StartTime = inheritedStart,
            ActualEndTime = inheritedStart
        };

        var insertIndex = insertAfterIndex + 1;
        entries.Insert(insertIndex, inserted);
        RecalculateGridEntry(inserted);
        RecalculateStartTimesFrom(insertIndex + 1);
        EnsureTrailingEmptyRow();

        return Task.CompletedTask;
    }

    private Task GenerateMailBodyAsync()
    {
        return GenerateMailBodyFromTemplateAsync();
    }

    private async Task GenerateMailBodyFromTemplateAsync()
    {
        await RefreshTaskStatusesFromRedmineAsync();

        var templateText = await LoadMailBodyTemplateAsync();
        generatedMailBody = BuildMailBody(templateText, workDate, entries);
        redmineStatusMessage = "勤務報告メール本文を生成しました。";
    }

    private async Task RefreshTaskStatusesFromRedmineAsync()
    {
        var taskIssueIds = entries
            .Where(entry => entry.RedmineTaskIssueId.HasValue)
            .Select(entry => entry.RedmineTaskIssueId!.Value)
            .Distinct()
            .ToList();

        if (taskIssueIds.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(redmineSettings.BaseUrl) || string.IsNullOrWhiteSpace(redmineSettings.ApiKey))
        {
            return;
        }

        try
        {
            var statusMap = await RedmineService.GetIssueClosedStatusMapAsync(redmineSettings, taskIssueIds);
            var hasChanges = false;

            foreach (var entry in entries)
            {
                if (!entry.RedmineTaskIssueId.HasValue)
                {
                    continue;
                }

                if (!statusMap.TryGetValue(entry.RedmineTaskIssueId.Value, out var isClosed))
                {
                    continue;
                }

                var newStatusLabel = MapTaskStatusLabel(isClosed);
                if (entry.RedmineTaskIsClosed == isClosed && string.Equals(entry.TaskStatusLabel, newStatusLabel, StringComparison.Ordinal))
                {
                    continue;
                }

                entry.RedmineTaskIsClosed = isClosed;
                entry.TaskStatusLabel = newStatusLabel;
                await DailyLogService.SaveEntryAsync(entry);
                hasChanges = true;
            }

            if (hasChanges && currentLog is not null)
            {
                entries = await DailyLogService.GetEntriesAsync(currentLog.Id);
            }
        }
        catch
        {
            // ステータス同期失敗時は保存済み状態で本文生成を継続
        }
    }

    private async Task CopyMailBodyAsync()
    {
        if (string.IsNullOrWhiteSpace(generatedMailBody))
        {
            return;
        }

        await JS.InvokeVoidAsync("navigator.clipboard.writeText", generatedMailBody);
        redmineStatusMessage = "勤務報告メール本文をクリップボードにコピーしました。";
    }

    private async Task ComposeWorkReportMailAsync()
    {
        if (string.IsNullOrWhiteSpace(generatedMailBody))
        {
            redmineStatusMessage = "先に本文を生成してください。";
            return;
        }

        var subject = $"{workDate:MM/dd} 作業報告";

        try
        {
            var message = new EmailMessage
            {
                Subject = subject,
                Body = generatedMailBody
            };

            await Email.Default.ComposeAsync(message);
            redmineStatusMessage = "メール作成画面を開きました。";
        }
        catch (FeatureNotSupportedException)
        {
            redmineStatusMessage = "この環境ではメール作成機能がサポートされていません。";
        }
        catch (Exception ex)
        {
            redmineStatusMessage = $"メール作成画面を開けませんでした: {ex.Message}";
        }
    }

    private async Task RegisterSpentTimeToRedmineAsync()
    {
        if (currentLog is null)
        {
            return;
        }

        isLoadingRedmine = true;
        redmineStatusMessage = null;

        try
        {
            await DailyLogService.SaveRedmineSettingsAsync(redmineSettings);
            var drafts = BuildTimeEntryDrafts(entries);

            if (drafts.Count == 0)
            {
                redmineStatusMessage = "送信対象の実工数がありません。タスクまたはストーリーに紐づいた実工数を入力してください。";
                return;
            }

            var result = await RedmineService.RegisterSpentTimeBulkAsync(redmineSettings, workDate, drafts);
            redmineStatusMessage = result.Errors.Count == 0
                ? $"{result.SuccessCount} 件の実工数をRedmineへ登録しました。"
                : $"{result.SuccessCount} 件登録 / {result.Errors.Count} 件失敗: {string.Join(" | ", result.Errors)}";
        }
        catch (Exception ex)
        {
            redmineStatusMessage = $"実工数登録に失敗しました: {ex.Message}";
        }
        finally
        {
            isLoadingRedmine = false;
        }
    }

    private async Task LoadProjectsAsync()
    {
        isLoadingRedmine = true;
        redmineStatusMessage = null;

        try
        {
            await DailyLogService.SaveRedmineSettingsAsync(redmineSettings);
            redmineProjects = await RedmineService.GetProjectsAsync(redmineSettings);
            redmineStatusMessage = $"プロジェクトを {redmineProjects.Count} 件取得しました。";
        }
        catch (Exception ex)
        {
            redmineStatusMessage = $"Redmine取得に失敗しました: {ex.Message}";
        }
        finally
        {
            isLoadingRedmine = false;
        }
    }

    private async Task PreloadProjectIssuesAsync(IEnumerable<WorkLogEntry> sourceEntries)
    {
        var projectIds = sourceEntries
            .Where(entry => entry.RedmineProjectId.HasValue)
            .Select(entry => entry.RedmineProjectId!.Value)
            .Distinct()
            .ToList();

        foreach (var projectId in projectIds)
        {
            await EnsureProjectIssuesLoadedAsync(projectId);
        }
    }

    private async Task EnsureProjectIssuesLoadedAsync(int projectId)
    {
        if (projectIssueCache.ContainsKey(projectId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(redmineSettings.BaseUrl) || string.IsNullOrWhiteSpace(redmineSettings.ApiKey))
        {
            return;
        }

        var issues = await RedmineService.GetIssuesForProjectAsync(redmineSettings, projectId);
        projectIssueCache[projectId] = issues;
    }

    private IReadOnlyList<RedmineIssue> GetStoryOptions(WorkLogEntry entry)
    {
        if (!entry.RedmineProjectId.HasValue)
        {
            return Array.Empty<RedmineIssue>();
        }

        return projectIssueCache.TryGetValue(entry.RedmineProjectId.Value, out var split)
            ? split.Stories
            : Array.Empty<RedmineIssue>();
    }

    private IReadOnlyList<RedmineIssue> GetTaskOptions(WorkLogEntry entry)
    {
        if (!entry.RedmineProjectId.HasValue)
        {
            return Array.Empty<RedmineIssue>();
        }

        return projectIssueCache.TryGetValue(entry.RedmineProjectId.Value, out var split)
            ? split.Tasks
            : Array.Empty<RedmineIssue>();
    }

    private IReadOnlyList<RedmineProject> GetProjectOptions(WorkLogEntry entry)
    {
        if (entry.RedmineProjectId.HasValue
            && !string.IsNullOrWhiteSpace(entry.Project)
            && redmineProjects.All(project => project.Id != entry.RedmineProjectId.Value))
        {
            var merged = new List<RedmineProject>(redmineProjects.Count + 1)
            {
                new RedmineProject
                {
                    Id = entry.RedmineProjectId.Value,
                    Name = entry.Project
                }
            };

            merged.AddRange(redmineProjects);
            return merged;
        }

        return redmineProjects;
    }

    private async Task OnGridProjectChangedAsync(WorkLogEntry entry, ChangeEventArgs args)
    {
        var selectedValue = args?.Value?.ToString();

        if (string.Equals(selectedValue, BreakProjectOptionValue, StringComparison.Ordinal))
        {
            entry.RedmineProjectId = null;
            entry.Project = BreakProjectName;
            entry.RedmineStoryIssueId = null;
            entry.Story = string.Empty;
            entry.RedmineTaskIssueId = null;
            entry.Task = string.Empty;
            entry.RedmineTaskIsClosed = null;
            entry.TaskStatusLabel = string.Empty;
            return;
        }

        entry.RedmineProjectId = ParseNullableInt(selectedValue);
        var project = redmineProjects.FirstOrDefault(x => x.Id == entry.RedmineProjectId);
        entry.Project = project?.Name ?? string.Empty;

        entry.RedmineStoryIssueId = null;
        entry.Story = string.Empty;
        entry.RedmineTaskIssueId = null;
        entry.Task = string.Empty;
        entry.RedmineTaskIsClosed = null;
        entry.TaskStatusLabel = string.Empty;

        if (entry.RedmineProjectId.HasValue)
        {
            await EnsureProjectIssuesLoadedAsync(entry.RedmineProjectId.Value);
        }
    }

    [JSInvokable]
    public Task NotifyPointerDragStart(int sourceIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= entries.Count)
        {
            LogDragState($"pointer-start-ignore-out-of-range index={sourceIndex}");
            return Task.CompletedTask;
        }

        var sourceEntry = entries[sourceIndex];
        if (IsEntryEmpty(sourceEntry))
        {
            LogDragState($"pointer-start-ignore-empty index={sourceIndex}");
            return Task.CompletedTask;
        }

        draggedEntry = sourceEntry;
        dragTargetEntry = sourceEntry;
        LogDragState($"pointer-start index={sourceIndex}");
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task NotifyPointerDragCancel()
    {
        LogDragState("pointer-cancel");
        draggedEntry = null;
        dragTargetEntry = null;
        dragInsertIndex = null;
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task NotifyDragOverIndex(int targetIndex)
    {
        if (draggedEntry is null)
        {
            return Task.CompletedTask;
        }

        if (targetIndex < 0 || targetIndex >= entries.Count)
        {
            LogDragState($"dragover-ignore-out-of-range index={targetIndex}");
            return Task.CompletedTask;
        }

        var insertIndex = ComputeInsertIndex(targetIndex);
        if (!insertIndex.HasValue)
        {
            return Task.CompletedTask;
        }

        var targetEntry = entries[targetIndex];

        if (!ReferenceEquals(dragTargetEntry, targetEntry))
        {
            dragTargetEntry = targetEntry;
            LogDragState($"dragover-target-set index={targetIndex}");
            _ = InvokeAsync(StateHasChanged);
        }

        if (dragInsertIndex != insertIndex.Value)
        {
            dragInsertIndex = insertIndex.Value;
            LogDragState($"dragover-insert-index set={dragInsertIndex.Value}");
            _ = InvokeAsync(StateHasChanged);
        }

        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task NotifyDropIndex(int targetIndex)
    {
        LogDragState($"drop-notify index={targetIndex}");

        if (targetIndex < 0 || targetIndex >= entries.Count)
        {
            LogDragState($"drop-ignore-out-of-range index={targetIndex}");
            draggedEntry = null;
            dragTargetEntry = null;
            dragInsertIndex = null;
            await InvokeAsync(StateHasChanged);
            return;
        }

        dragTargetEntry = entries[targetIndex];
        var insertIndex = ComputeInsertIndex(targetIndex);
        if (!insertIndex.HasValue)
        {
            LogDragState($"drop-ignore-invalid-insert-index target={targetIndex}");
            draggedEntry = null;
            dragTargetEntry = null;
            dragInsertIndex = null;
            await InvokeAsync(StateHasChanged);
            return;
        }

        dragInsertIndex = insertIndex.Value;
        await HandleDropCoreAsync(insertIndex.Value);
    }

    private async Task HandleDropCoreAsync(int insertIndex)
    {
        LogDragState("drop-before-validate");
        if (draggedEntry is null || dragTargetEntry is null || ReferenceEquals(draggedEntry, dragTargetEntry))
        {
            LogDragState("drop-cancel-invalid-state");
            draggedEntry = null;
            dragTargetEntry = null;
            dragInsertIndex = null;
            await InvokeAsync(StateHasChanged);
            return;
        }

        var draggedIndex = entries.IndexOf(draggedEntry);
        if (draggedIndex < 0)
        {
            LogDragState($"drop-cancel-index-missing draggedIndex={draggedIndex} insertIndex={insertIndex}");
            draggedEntry = null;
            dragTargetEntry = null;
            dragInsertIndex = null;
            await InvokeAsync(StateHasChanged);
            return;
        }

        if (insertIndex < 0 || insertIndex > entries.Count - 1)
        {
            LogDragState($"drop-cancel-insert-out-of-range insertIndex={insertIndex} entriesCount={entries.Count}");
            draggedEntry = null;
            dragTargetEntry = null;
            dragInsertIndex = null;
            await InvokeAsync(StateHasChanged);
            return;
        }

        entries.RemoveAt(draggedIndex);

        entries.Insert(insertIndex, draggedEntry);
        RecalculateStartTimesFrom(0);

        if (currentLog is not null)
        {
            foreach (var entry in entries.Where(entry => !IsEntryEmpty(entry)))
            {
                entry.WorkLogId = currentLog.Id;
                await DailyLogService.SaveEntryAsync(entry);
            }
        }

        EnsureTrailingEmptyRow();
        LogDragState($"drop-committed draggedIndex={draggedIndex} insertIndex={insertIndex}");
        draggedEntry = null;
        dragTargetEntry = null;
        dragInsertIndex = null;
        await InvokeAsync(StateHasChanged);
    }

    private int? ComputeInsertIndex(int targetIndex)
    {
        if (draggedEntry is null)
        {
            return null;
        }

        if (targetIndex < 0 || targetIndex >= entries.Count)
        {
            return null;
        }

        var draggedIndex = entries.IndexOf(draggedEntry);
        if (draggedIndex < 0)
        {
            return null;
        }

        var effectiveTargetIndex = targetIndex;
        if (IsEntryEmpty(entries[effectiveTargetIndex]))
        {
            while (effectiveTargetIndex >= 0 && IsEntryEmpty(entries[effectiveTargetIndex]))
            {
                effectiveTargetIndex--;
            }

            if (effectiveTargetIndex < 0)
            {
                return null;
            }
        }

        var insertIndex = draggedIndex < effectiveTargetIndex
            ? effectiveTargetIndex + 1
            : effectiveTargetIndex;

        var maxInsertIndex = entries.Count - 1;
        if (insertIndex > maxInsertIndex)
        {
            insertIndex = maxInsertIndex;
        }

        if (draggedIndex < insertIndex)
        {
            insertIndex--;
        }

        return Math.Max(0, insertIndex);
    }

    private void LogDragEvent(string stage, WorkLogEntry? entry, WebDragEventArgs? args)
    {
        var transfer = args?.DataTransfer;
        var message =
            $"[DND:{stage}] entryId={entry?.Id} project={entry?.Project ?? ""} story={entry?.Story ?? ""} task={entry?.Task ?? ""} " +
            $"dropEffect={transfer?.DropEffect ?? ""} effectAllowed={transfer?.EffectAllowed ?? ""} " +
            $"types={(transfer?.Types is null ? "" : string.Join(",", transfer.Types))}";

        Console.WriteLine(message);
        System.Diagnostics.Debug.WriteLine(message);
        Logger.LogInformation(message);
    }

    private void LogDragState(string stage)
    {
        var message =
            $"[DND-STATE:{stage}] draggedEntryId={draggedEntry?.Id} dragTargetEntryId={dragTargetEntry?.Id} dragInsertIndex={dragInsertIndex?.ToString() ?? ""} entriesCount={entries.Count}";

        Console.WriteLine(message);
        System.Diagnostics.Debug.WriteLine(message);
        Logger.LogInformation(message);
    }

    private string GetRowClass(WorkLogEntry entry, int index)
    {
        if (ReferenceEquals(entry, draggedEntry))
        {
            return "dragging";
        }

        if (draggedEntry is null || !dragInsertIndex.HasValue)
        {
            return string.Empty;
        }

        if (dragInsertIndex.Value != index)
        {
            return string.Empty;
        }

        var draggedIndex = entries.IndexOf(draggedEntry);
        if (draggedIndex < 0)
        {
            return string.Empty;
        }

        if (dragInsertIndex.Value > draggedIndex)
        {
            return "drag-over-bottom";
        }

        return "drag-over-top";
    }

    private static string GetProjectSelectValue(WorkLogEntry entry)
    {
        return IsBreakEntry(entry)
            ? BreakProjectOptionValue
            : (entry.RedmineProjectId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private void OnGridStoryChanged(WorkLogEntry entry, ChangeEventArgs args)
    {
        entry.RedmineStoryIssueId = ParseNullableInt(args?.Value?.ToString());
        var selectedStory = GetStoryOptions(entry).FirstOrDefault(x => x.Id == entry.RedmineStoryIssueId);
        entry.Story = selectedStory is null ? string.Empty : $"#{selectedStory.Id} {selectedStory.Subject}";
    }

    private void OnGridTaskChanged(WorkLogEntry entry, ChangeEventArgs args)
    {
        entry.RedmineTaskIssueId = ParseNullableInt(args?.Value?.ToString());
        var selectedTask = GetTaskOptions(entry).FirstOrDefault(x => x.Id == entry.RedmineTaskIssueId);
        entry.Task = selectedTask is null ? string.Empty : $"#{selectedTask.Id} {selectedTask.Subject}";
        entry.RedmineTaskIsClosed = selectedTask?.Status?.IsClosed;
        entry.TaskStatusLabel = MapTaskStatusLabel(entry.RedmineTaskIsClosed);
    }

    private void OnGridStartTimeChanged(WorkLogEntry entry, ChangeEventArgs args)
    {
        if (entries.IndexOf(entry) > 0)
        {
            return;
        }

        entry.StartTime = ParseTimeSpan(args?.Value?.ToString());
        RecalculateGridEntry(entry);
    }

    private void OnGridPlannedHoursChanged(WorkLogEntry entry, ChangeEventArgs args)
    {
        entry.PlannedHours = ParseDecimal(args?.Value?.ToString());
        RecalculateGridEntry(entry);
    }

    private void OnGridActualEndTimeChanged(WorkLogEntry entry, ChangeEventArgs args)
    {
        entry.ActualEndTime = ParseTimeSpan(args?.Value?.ToString());
        RecalculateGridEntry(entry);

        if (entry.StartTime != TimeSpan.Zero && entry.ActualEndTime != TimeSpan.Zero)
        {
            entry.PlannedHours = entry.ActualHours;
            RecalculateGridEntry(entry);
        }

        var currentIndex = entries.IndexOf(entry);
        if (currentIndex >= 0)
        {
            RecalculateStartTimesFrom(currentIndex + 1);
        }
    }

    private async Task SaveGridRowAsync(WorkLogEntry entry)
    {
        if (currentLog is null)
        {
            return;
        }

        RecalculateGridEntry(entry);

        if (IsEntryEmpty(entry))
        {
            if (entry.Id != 0)
            {
                await DailyLogService.DeleteEntryAsync(entry);
                entries.Remove(entry);
            }

            EnsureTrailingEmptyRow();
            return;
        }

        entry.WorkLogId = currentLog.Id;
        await DailyLogService.SaveEntryAsync(entry);
        EnsureTrailingEmptyRow();
    }

    private static void RecalculateGridEntry(WorkLogEntry entry)
    {
        if (entry.StartTime != TimeSpan.Zero && entry.PlannedHours > 0)
        {
            entry.PlannedEndTime = entry.StartTime + TimeSpan.FromHours((double)entry.PlannedHours);
        }
        else
        {
            entry.PlannedEndTime = TimeSpan.Zero;
        }

        if (entry.StartTime == TimeSpan.Zero || entry.ActualEndTime == TimeSpan.Zero)
        {
            entry.ActualHours = 0;
            return;
        }

        var diff = entry.ActualEndTime - entry.StartTime;
        if (diff < TimeSpan.Zero)
        {
            diff = diff.Add(TimeSpan.FromDays(1));
        }

        var roundedHours = Math.Round(diff.TotalHours * 4, MidpointRounding.AwayFromZero) / 4;
        entry.ActualHours = (decimal)Math.Max(0, roundedHours);
    }

    private void RecalculateStartTimesFrom(int startRowIndex)
    {
        if (entries.Count == 0)
        {
            return;
        }

        if (startRowIndex < 0)
        {
            startRowIndex = 0;
        }

        if (startRowIndex == 0)
        {
            RecalculateGridEntry(entries[0]);
            startRowIndex = 1;
        }

        for (var rowIndex = startRowIndex; rowIndex < entries.Count; rowIndex++)
        {
            var previousRow = entries[rowIndex - 1];
            var currentRow = entries[rowIndex];

            currentRow.StartTime = previousRow.ActualEndTime;
            RecalculateGridEntry(currentRow);
        }
    }

    private void EnsureTrailingEmptyRow()
    {
        if (entries.Count == 0)
        {
            entries.Add(new WorkLogEntry());
            return;
        }

        if (!IsEntryEmpty(entries[^1]))
        {
            entries.Add(new WorkLogEntry());
        }
    }

    private static bool IsEntryEmpty(WorkLogEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.Project)
            && !entry.RedmineProjectId.HasValue
            && string.IsNullOrWhiteSpace(entry.Story)
            && !entry.RedmineStoryIssueId.HasValue
            && string.IsNullOrWhiteSpace(entry.Task)
            && !entry.RedmineTaskIssueId.HasValue
            && entry.PlannedHours == 0;
    }

    private static int? ParseNullableInt(string? raw)
    {
        return int.TryParse(raw, out var value) ? value : null;
    }

    private static decimal ParseDecimal(string? raw)
    {
        return decimal.TryParse(raw, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static TimeSpan ParseTimeSpan(string? raw)
    {
        return TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var value) ? value : TimeSpan.Zero;
    }

    private static string ToInputTime(TimeSpan time)
    {
        return time == TimeSpan.Zero ? string.Empty : time.ToString("hh\\:mm", CultureInfo.InvariantCulture);
    }

    private static string ToInputDecimal(decimal value)
    {
        return value == 0 ? string.Empty : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatTime(TimeSpan time)
    {
        return time == TimeSpan.Zero ? "-" : time.ToString("hh\\:mm", CultureInfo.InvariantCulture);
    }

    private static List<RedmineTimeEntryDraft> BuildTimeEntryDrafts(IEnumerable<WorkLogEntry> sourceEntries)
    {
        var grouped = sourceEntries
            .Where(entry => entry.ActualHours > 0 && !IsBreakEntry(entry))
            .Select(entry => new
            {
                IssueId = entry.RedmineTaskIssueId ?? entry.RedmineStoryIssueId,
                entry.ActualHours,
                entry.Project,
                entry.Story,
                entry.Task
            })
            .Where(entry => entry.IssueId.HasValue)
            .GroupBy(entry => entry.IssueId!.Value)
            .Select(group => new RedmineTimeEntryDraft(
                group.Key,
                group.Sum(x => x.ActualHours),
                BuildTimeEntryComment(group.First().Project, group.First().Story, group.First().Task)))
            .ToList();

        return grouped;
    }

    private static string BuildTimeEntryComment(string project, string story, string task)
    {
        var commentParts = new List<string> { "SyncTask登録" };

        if (!string.IsNullOrWhiteSpace(project))
        {
            commentParts.Add($"PJ:{project}");
        }

        if (!string.IsNullOrWhiteSpace(story))
        {
            commentParts.Add($"Story:{story}");
        }

        if (!string.IsNullOrWhiteSpace(task))
        {
            commentParts.Add($"Task:{task}");
        }

        return string.Join(" / ", commentParts);
    }

    private static string BuildMailBody(string templateText, DateTime targetDate, IReadOnlyList<WorkLogEntry> sourceEntries)
    {
        var dateText = BuildReportDateText(targetDate, DateTime.Today);
        var nextDateText = targetDate.AddDays(1).ToString("MM/dd", CultureInfo.InvariantCulture);
        var worktimeSection = BuildWorktimeSection(sourceEntries);
        var contentSection = BuildContentSection(sourceEntries);

        var normalizedTemplate = string.IsNullOrWhiteSpace(templateText)
            ? DefaultMailBodyTemplate
            : templateText;

        var mailBody = normalizedTemplate
            .Replace("${date}", dateText, StringComparison.Ordinal)
            .Replace("${worktime_section}", worktimeSection, StringComparison.Ordinal)
            .Replace("${content_section}", contentSection, StringComparison.Ordinal)
            .Replace("${next_date}", nextDateText, StringComparison.Ordinal);

        return mailBody.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }

    private static async Task<string> LoadMailBodyTemplateAsync()
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(MailBodyTemplateAssetPath);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
        catch
        {
            return DefaultMailBodyTemplate;
        }
    }

    private static string BuildReportDateText(DateTime targetDate, DateTime today)
    {
        var reportDateText = targetDate.ToString("MM/dd", CultureInfo.InvariantCulture);
        return targetDate.Date == today.Date
            ? $"本日({reportDateText})"
            : reportDateText;
    }

    private static string BuildWorktimeSection(IEnumerable<WorkLogEntry> sourceEntries)
    {
        var targetEntries = sourceEntries
            .Where(entry => !IsEntryEmpty(entry))
            .ToList();

        if (targetEntries.Count == 0)
        {
            return "（未入力）";
        }

        var firstEntry = targetEntries[0];
        var lastEntry = targetEntries[^1];
        var hasBreakEntry = targetEntries.Any(IsBreakEntry);
        var breakHours = targetEntries.Where(IsBreakEntry).Sum(entry => entry.ActualHours);
        var activeHoursFromRows = targetEntries.Where(entry => !IsBreakEntry(entry)).Sum(entry => entry.ActualHours);

        if (firstEntry.StartTime == TimeSpan.Zero || lastEntry.ActualEndTime == TimeSpan.Zero)
        {
            if (activeHoursFromRows <= 0 && !hasBreakEntry)
            {
                return "（未入力）";
            }

            var fallbackLines = new List<string>
            {
                $"稼働合計：{FormatHoursFixed(activeHoursFromRows)}h"
            };

            if (hasBreakEntry)
            {
                fallbackLines.Add($"休憩時間：{FormatHoursFixed(breakHours)}h");
            }

            return string.Join(Environment.NewLine, fallbackLines);
        }

        var elapsed = lastEntry.ActualEndTime - firstEntry.StartTime;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = elapsed.Add(TimeSpan.FromDays(1));
        }

        var elapsedRounded = (decimal)(Math.Round(elapsed.TotalHours * 4, MidpointRounding.AwayFromZero) / 4);
        var activeHours = Math.Max(0, elapsedRounded - breakHours);

        var lines = new List<string>
        {
            $"{firstEntry.StartTime:hh\\:mm}～{lastEntry.ActualEndTime:hh\\:mm}",
            $"稼働合計：{FormatHoursFixed(activeHours)}h"
        };

        if (hasBreakEntry)
        {
            lines.Add($"休憩時間：{FormatHoursFixed(breakHours)}h");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildContentSection(IEnumerable<WorkLogEntry> sourceEntries)
    {
        var workingEntries = sourceEntries
            .Where(entry => entry.ActualHours > 0 && !IsBreakEntry(entry))
            .ToList();

        if (workingEntries.Count == 0)
        {
            return " ●(作業入力なし)";
        }

        var contentBuilder = new StringBuilder();

        foreach (var projectGroup in workingEntries.GroupBy(entry => string.IsNullOrWhiteSpace(entry.Project) ? "未分類" : entry.Project))
        {
            var projectHours = projectGroup.Sum(entry => entry.ActualHours);
            contentBuilder.Append(" ●").Append(projectGroup.Key).Append("(").Append(FormatHours(projectHours)).AppendLine("h)");

            var seenStoryLines = new HashSet<string>(StringComparer.Ordinal);
            var seenTaskLines = new HashSet<string>(StringComparer.Ordinal);
            var hasTicketLine = false;

            foreach (var entry in projectGroup)
            {
                if (!string.IsNullOrWhiteSpace(entry.Story) && seenStoryLines.Add(entry.Story))
                {
                    contentBuilder.Append("  ◆").AppendLine(entry.Story);
                    hasTicketLine = true;
                }

                if (!string.IsNullOrWhiteSpace(entry.Task))
                {
                    var taskKey = string.Concat(entry.Story, "|", entry.Task);
                    if (seenTaskLines.Add(taskKey))
                    {
                        var taskLine = BuildTaskLineWithStatus(entry.Task, entry.TaskStatusLabel, entry.RedmineTaskIsClosed);
                        contentBuilder.Append("    ・").AppendLine(taskLine);
                        hasTicketLine = true;
                    }
                }
            }

            if (!hasTicketLine)
            {
                contentBuilder.AppendLine("  ・(チケット未設定)");
            }
        }

        return contentBuilder.ToString().TrimEnd();
    }

    private static string FormatHours(decimal hours)
    {
        return hours.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatHoursFixed(decimal hours)
    {
        return hours.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static bool IsBreakEntry(WorkLogEntry entry)
    {
        return string.Equals(entry.Project, BreakProjectName, StringComparison.Ordinal);
    }

    private static string MapTaskStatusLabel(bool? isClosed)
    {
        if (!isClosed.HasValue)
        {
            return string.Empty;
        }

        return isClosed.Value ? "[完了]" : "[継続]";
    }

    private static string BuildTaskLineWithStatus(string taskName, string? taskStatusLabel, bool? isClosed)
    {
        var normalizedLabel = string.IsNullOrWhiteSpace(taskStatusLabel)
            ? MapTaskStatusLabel(isClosed)
            : taskStatusLabel;

        if (string.IsNullOrWhiteSpace(normalizedLabel))
        {
            return taskName;
        }

        return $"{taskName} {normalizedLabel}";
    }

    public void Dispose()
    {
        dotNetRef?.Dispose();
        dotNetRef = null;
    }
}
