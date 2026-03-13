using SQLite;

namespace SyncTask.Data;

public class WorkLog
{
    [PrimaryKey]
    [AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public DateTime WorkDate { get; set; }

    public RedmineSyncStatus RedmineSyncStatus { get; set; } = RedmineSyncStatus.None;

    public DateTime? RedmineSyncedAt { get; set; }

    public string RedmineSyncMessage { get; set; } = string.Empty;

    public string? StartTime { get; set; }

    public string? EndTime { get; set; }

    public decimal? ManualWorkingHours { get; set; }
}
