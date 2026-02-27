using SQLite;

namespace SyncTask.Data;

public class RedmineSettings
{
    [PrimaryKey]
    public int Id { get; set; } = 1;

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string StoryTrackerNames { get; set; } = "Story,ストーリー";

    public string TaskTrackerNames { get; set; } = "Task,タスク";
}
