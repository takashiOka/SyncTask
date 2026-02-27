using SQLite;

namespace SyncTask.Data;

public class WorkLogEntry
{
    [PrimaryKey]
    [AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int WorkLogId { get; set; }

    public string Project { get; set; } = string.Empty;

    public int? RedmineProjectId { get; set; }

    public string Story { get; set; } = string.Empty;

    public int? RedmineStoryIssueId { get; set; }

    public string Task { get; set; } = string.Empty;

    public int? RedmineTaskIssueId { get; set; }

    public decimal PlannedHours { get; set; }

    public decimal ActualHours { get; set; }
}
