using SQLite;
using SyncTask.Data;

namespace SyncTask.Services;

public class DailyLogService
{
    private SQLiteAsyncConnection? _database;

    private sealed class TableInfoRow
    {
        public int Cid { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public int NotNull { get; set; }

        public string? DfltValue { get; set; }

        public int Pk { get; set; }
    }

    private async Task<SQLiteAsyncConnection> GetDatabaseAsync()
    {
        if (_database is not null)
        {
            return _database;
        }

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "synctask.db3");
        _database = new SQLiteAsyncConnection(databasePath);

        await _database.CreateTableAsync<WorkLog>();
        await _database.CreateTableAsync<WorkLogEntry>();
        await _database.CreateTableAsync<RedmineSettings>();
        await EnsureWorkLogEntrySchemaAsync(_database);

        return _database;
    }

    private static async Task EnsureWorkLogEntrySchemaAsync(SQLiteAsyncConnection database)
    {
        var tableInfo = await database.QueryAsync<TableInfoRow>("PRAGMA table_info('WorkLogEntry')");
        var columnNames = tableInfo.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!columnNames.Contains("RedmineProjectId"))
        {
            await database.ExecuteAsync("ALTER TABLE WorkLogEntry ADD COLUMN RedmineProjectId INTEGER NULL");
        }

        if (!columnNames.Contains("RedmineStoryIssueId"))
        {
            await database.ExecuteAsync("ALTER TABLE WorkLogEntry ADD COLUMN RedmineStoryIssueId INTEGER NULL");
        }

        if (!columnNames.Contains("RedmineTaskIssueId"))
        {
            await database.ExecuteAsync("ALTER TABLE WorkLogEntry ADD COLUMN RedmineTaskIssueId INTEGER NULL");
        }
    }

    public async Task<WorkLog> GetOrCreateDailyLogAsync(DateTime workDate)
    {
        var database = await GetDatabaseAsync();
        var date = workDate.Date;
        var existing = await database.Table<WorkLog>()
            .Where(log => log.WorkDate == date)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            return existing;
        }

        var newLog = new WorkLog
        {
            WorkDate = date
        };

        await database.InsertAsync(newLog);
        return newLog;
    }

    public async Task SaveDailyLogAsync(WorkLog workLog)
    {
        var database = await GetDatabaseAsync();
        if (workLog.Id == 0)
        {
            await database.InsertAsync(workLog);
        }
        else
        {
            await database.UpdateAsync(workLog);
        }
    }

    public async Task<List<WorkLogEntry>> GetEntriesAsync(int workLogId)
    {
        var database = await GetDatabaseAsync();
        return await database.Table<WorkLogEntry>()
            .Where(entry => entry.WorkLogId == workLogId)
            .ToListAsync();
    }

    public async Task SaveEntryAsync(WorkLogEntry entry)
    {
        var database = await GetDatabaseAsync();
        if (entry.Id == 0)
        {
            await database.InsertAsync(entry);
        }
        else
        {
            await database.UpdateAsync(entry);
        }
    }

    public async Task DeleteEntryAsync(WorkLogEntry entry)
    {
        if (entry.Id == 0)
        {
            return;
        }

        var database = await GetDatabaseAsync();
        await database.DeleteAsync(entry);
    }

    public async Task<RedmineSettings> GetOrCreateRedmineSettingsAsync()
    {
        var database = await GetDatabaseAsync();
        var settings = await database.FindAsync<RedmineSettings>(1);
        if (settings is not null)
        {
            return settings;
        }

        var newSettings = new RedmineSettings();
        await database.InsertAsync(newSettings);
        return newSettings;
    }

    public async Task SaveRedmineSettingsAsync(RedmineSettings settings)
    {
        var database = await GetDatabaseAsync();
        settings.Id = 1;
        await database.InsertOrReplaceAsync(settings);
    }
}
