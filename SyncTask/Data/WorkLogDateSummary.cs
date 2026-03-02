namespace SyncTask.Data;

public sealed record WorkLogDateSummary(DateTime WorkDate, bool HasData, RedmineSyncStatus SyncStatus);
