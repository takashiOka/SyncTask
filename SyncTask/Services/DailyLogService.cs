using SQLite;
using SyncTask.Data;

namespace SyncTask.Services;

public class DailyLogService
{
    private const string BreakProjectName = "休憩・私用";
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
        await EnsureWorkLogSchemaAsync(_database);
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

        if (!columnNames.Contains("RedmineTaskIsClosed"))
        {
            await database.ExecuteAsync("ALTER TABLE WorkLogEntry ADD COLUMN RedmineTaskIsClosed INTEGER NULL");
        }

        if (!columnNames.Contains("TaskStatusLabel"))
        {
            await database.ExecuteAsync("ALTER TABLE WorkLogEntry ADD COLUMN TaskStatusLabel TEXT NULL");
        }

        if (!columnNames.Contains("StartTime"))
        {
            await database.ExecuteAsync("ALTER TABLE WorkLogEntry ADD COLUMN StartTime BIGINT NOT NULL DEFAULT 0");
        }

        if (!columnNames.Contains("PlannedEndTime"))
        {
            await database.ExecuteAsync("ALTER TABLE WorkLogEntry ADD COLUMN PlannedEndTime BIGINT NOT NULL DEFAULT 0");
        }

        if (!columnNames.Contains("ActualStartTime"))
        {
            await database.ExecuteAsync("ALTER TABLE WorkLogEntry ADD COLUMN ActualStartTime BIGINT NOT NULL DEFAULT 0");
        }

        if (!columnNames.Contains("ActualEndTime"))
        {
            await database.ExecuteAsync("ALTER TABLE WorkLogEntry ADD COLUMN ActualEndTime BIGINT NOT NULL DEFAULT 0");
        }
    }

    private static async Task EnsureWorkLogSchemaAsync(SQLiteAsyncConnection database)
    {
        var tableInfo = await database.QueryAsync<TableInfoRow>("PRAGMA table_info('WorkLog')");
        var columnNames = tableInfo.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!columnNames.Contains("RedmineSyncStatus"))
        {
            await database.ExecuteAsync("ALTER TABLE WorkLog ADD COLUMN RedmineSyncStatus INTEGER NOT NULL DEFAULT 0");
        }

        if (!columnNames.Contains("RedmineSyncedAt"))
        {
            await database.ExecuteAsync("ALTER TABLE WorkLog ADD COLUMN RedmineSyncedAt DATETIME NULL");
        }

        if (!columnNames.Contains("RedmineSyncMessage"))
        {
            await database.ExecuteAsync("ALTER TABLE WorkLog ADD COLUMN RedmineSyncMessage TEXT NULL");
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

    public async Task<List<WorkLogDateSummary>> GetWorkLogDateSummariesAsync(DateTime month)
    {
        var database = await GetDatabaseAsync();
        var firstDate = new DateTime(month.Year, month.Month, 1);
        var endDate = firstDate.AddMonths(1);

        var logs = await database.Table<WorkLog>()
            .Where(log => log.WorkDate >= firstDate && log.WorkDate < endDate)
            .ToListAsync();

        if (logs.Count == 0)
        {
            return new List<WorkLogDateSummary>();
        }

        var logIds = logs.Select(log => log.Id).ToHashSet();
        var entries = (await database.Table<WorkLogEntry>().ToListAsync())
            .Where(entry => logIds.Contains(entry.WorkLogId))
            .ToList();

        var entryCountByLogId = entries
            .GroupBy(entry => entry.WorkLogId)
            .ToDictionary(group => group.Key, group => group.Count());

        return logs
            .Select(log => new WorkLogDateSummary(
                log.WorkDate.Date,
                entryCountByLogId.TryGetValue(log.Id, out var count) && count > 0,
                log.RedmineSyncStatus))
            .OrderBy(summary => summary.WorkDate)
            .ToList();
    }

    public async Task<List<WorkLogEntry>> GetEntriesAsync(int workLogId)
    {
        var database = await GetDatabaseAsync();
        return await database.Table<WorkLogEntry>()
            .Where(entry => entry.WorkLogId == workLogId)
            .ToListAsync();
    }

    public async Task<AttendanceMonthlyReport> GetAttendanceMonthlyReportAsync(DateTime month)
    {
        var database = await GetDatabaseAsync();
        var targetMonth = new DateTime(month.Year, month.Month, 1);
        var periodStartDate = targetMonth.AddMonths(-1).AddDays(20);
        var periodEndDate = targetMonth.AddDays(19);
        var endDateExclusive = periodEndDate.AddDays(1);

        var logs = await database.Table<WorkLog>()
            .Where(log => log.WorkDate >= periodStartDate && log.WorkDate < endDateExclusive)
            .ToListAsync();

        var entriesByDate = new Dictionary<DateTime, List<WorkLogEntry>>();

        if (logs.Count > 0)
        {
            var logIds = logs.Select(log => log.Id).ToHashSet();
            var entries = (await database.Table<WorkLogEntry>().ToListAsync())
                .Where(entry => logIds.Contains(entry.WorkLogId))
                .ToList();

            var workDateByLogId = logs.ToDictionary(log => log.Id, log => log.WorkDate.Date);

            foreach (var entry in entries)
            {
                if (!workDateByLogId.TryGetValue(entry.WorkLogId, out var date))
                {
                    continue;
                }

                if (!entriesByDate.TryGetValue(date, out var list))
                {
                    list = new List<WorkLogEntry>();
                    entriesByDate[date] = list;
                }

                list.Add(entry);
            }
        }

        var days = new List<AttendanceDaySummary>();
        var cursor = periodStartDate;

        while (cursor < endDateExclusive)
        {
            entriesByDate.TryGetValue(cursor.Date, out var dayEntries);
            dayEntries ??= new List<WorkLogEntry>();

            var hasAttendance = dayEntries.Count > 0;

            var startCandidates = dayEntries
                .Where(entry => entry.ActualStartTime > TimeSpan.Zero)
                .Select(entry => entry.ActualStartTime)
                .ToList();

            var endCandidates = dayEntries
                .Where(entry => entry.ActualEndTime > TimeSpan.Zero)
                .Select(entry => entry.ActualEndTime)
                .ToList();

            TimeSpan? startTime = startCandidates.Count > 0 ? startCandidates.Min() : null;
            TimeSpan? endTime = endCandidates.Count > 0 ? endCandidates.Max() : null;

            var workingHours = dayEntries
                .Where(entry => !string.Equals(entry.Project, BreakProjectName, StringComparison.Ordinal))
                .Sum(entry => entry.ActualHours);

            days.Add(new AttendanceDaySummary(
                cursor.Date,
                hasAttendance,
                startTime,
                endTime,
                workingHours));

            cursor = cursor.AddDays(1);
        }

        var workingDays = days.Count(day => day.WorkingHours > 0);
        var totalWorkingHours = days.Sum(day => day.WorkingHours);

        return new AttendanceMonthlyReport(targetMonth, periodStartDate, periodEndDate, days, workingDays, totalWorkingHours);
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

    public async Task UpdateWorkLogSyncStatusAsync(int workLogId, RedmineSyncStatus status, string? message = null)
    {
        var database = await GetDatabaseAsync();
        var workLog = await database.FindAsync<WorkLog>(workLogId);
        if (workLog is null)
        {
            return;
        }

        workLog.RedmineSyncStatus = status;
        workLog.RedmineSyncedAt = status == RedmineSyncStatus.None ? null : DateTime.Now;
        workLog.RedmineSyncMessage = message ?? string.Empty;
        await database.UpdateAsync(workLog);
    }
}
