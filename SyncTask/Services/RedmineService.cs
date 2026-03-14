using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SyncTask.Data;

namespace SyncTask.Services;

public class RedmineService
{
    private readonly HttpClient _httpClient;

    public RedmineService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<RedmineProject>> GetProjectsAsync(RedmineSettings settings, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(settings, "projects.json?limit=100");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RedmineProjectsResponse>(cancellationToken: cancellationToken);
        return payload?.Projects?
            .Where(p => p.Status == RedmineProjectStatus.Active)
            .OrderBy(p => p.Name)
            .ToList() ?? new List<RedmineProject>();
    }

    public async Task<RedmineIssueSplitResult> GetIssuesForProjectAsync(
        RedmineSettings settings,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var allIssues = new List<RedmineIssue>();
        var offset = 0;
        const int limit = 100;

        while (true)
        {
            var query = $"issues.json?project_id={projectId}&status_id=*&limit={limit}&offset={offset}";
            using var request = BuildRequest(settings, query);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<RedmineIssuesResponse>(cancellationToken: cancellationToken);
            if (payload?.Issues is null || payload.Issues.Count == 0)
            {
                break;
            }

            allIssues.AddRange(payload.Issues);
            offset += payload.Limit;

            if (offset >= payload.TotalCount)
            {
                break;
            }
        }

        var storyTrackers = ParseTrackerNames(settings.StoryTrackerNames);
        var taskTrackers = ParseTrackerNames(settings.TaskTrackerNames);

        var stories = allIssues
            .Where(issue => IsTrackerMatched(issue.Tracker?.Name, storyTrackers))
            .OrderBy(issue => issue.Id)
            .ToList();

        var tasks = allIssues
            .Where(issue => IsTrackerMatched(issue.Tracker?.Name, taskTrackers))
            .OrderBy(issue => issue.Id)
            .ToList();

        return new RedmineIssueSplitResult(stories, tasks);
    }

    public async Task<RedmineTimeEntryBulkResult> RegisterSpentTimeBulkAsync(
        RedmineSettings settings,
        DateTime spentOn,
        IEnumerable<RedmineTimeEntryDraft> drafts,
        CancellationToken cancellationToken = default)
    {
        var successCount = 0;
        var errors = new List<string>();

        foreach (var draft in drafts)
        {
            if (draft.Hours <= 0)
            {
                continue;
            }

            try
            {
                await RegisterSpentTimeAsync(settings, draft, spentOn, cancellationToken);
                successCount++;
            }
            catch (Exception ex)
            {
                errors.Add($"Issue #{draft.IssueId}: {ex.Message}");
            }
        }

        return new RedmineTimeEntryBulkResult(successCount, errors);
    }

    public async Task<Dictionary<int, bool>> GetIssueClosedStatusMapAsync(
        RedmineSettings settings,
        IEnumerable<int> issueIds,
        CancellationToken cancellationToken = default)
    {
        var issueIdList = issueIds
            .Distinct()
            .ToList();

        var statusMap = new Dictionary<int, bool>();

        foreach (var issueId in issueIdList)
        {
            using var request = BuildRequest(settings, $"issues/{issueId}.json");
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            var payload = await response.Content.ReadFromJsonAsync<RedmineIssueResponse>(cancellationToken: cancellationToken);
            if (payload?.Issue?.Status is null)
            {
                continue;
            }

            statusMap[issueId] = payload.Issue.Status.IsClosed;
        }

        return statusMap;
    }

    public async Task<List<RedmineTimeEntry>> GetTimeEntriesForPeriodAsync(
        RedmineSettings settings,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var allEntries = new List<RedmineTimeEntry>();
        var offset = 0;
        const int limit = 100;

        while (true)
        {
            var query = $"time_entries.json?user_id=me&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&limit={limit}&offset={offset}";
            using var request = BuildRequest(settings, query);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<RedmineTimeEntriesResponse>(cancellationToken: cancellationToken);
            if (payload?.TimeEntries is null || payload.TimeEntries.Count == 0)
            {
                break;
            }

            allEntries.AddRange(payload.TimeEntries);
            offset += payload.Limit;

            if (offset >= payload.TotalCount)
            {
                break;
            }
        }

        return allEntries;
    }

    private async Task RegisterSpentTimeAsync(
        RedmineSettings settings,
        RedmineTimeEntryDraft draft,
        DateTime spentOn,
        CancellationToken cancellationToken)
    {
        var payload = new RedmineTimeEntryCreateRequest
        {
            TimeEntry = new RedmineTimeEntryCreateBody
            {
                IssueId = draft.IssueId,
                Hours = draft.Hours,
                Comments = draft.Comments,
                SpentOn = spentOn.ToString("yyyy-MM-dd")
            }
        };

        using var request = BuildRequest(settings, "time_entries.json", HttpMethod.Post);
        request.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static HashSet<string> ParseTrackerNames(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsTrackerMatched(string? trackerName, HashSet<string> expectedNames)
    {
        if (string.IsNullOrWhiteSpace(trackerName) || expectedNames.Count == 0)
        {
            return false;
        }

        if (expectedNames.Contains(trackerName))
        {
            return true;
        }

        return expectedNames.Any(name => trackerName.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static HttpRequestMessage BuildRequest(
        RedmineSettings settings,
        string relativePath,
        HttpMethod? method = null)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("RedmineのURLが設定されていません。");
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Redmine APIキーが設定されていません。");
        }

        var baseUrl = settings.BaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, $"{baseUrl}/{relativePath}");
        request.Headers.Add("X-Redmine-API-Key", settings.ApiKey);
        return request;
    }
}

    public sealed record RedmineTimeEntryDraft(int IssueId, decimal Hours, string Comments);

    public sealed record RedmineTimeEntryBulkResult(int SuccessCount, List<string> Errors);

public sealed record RedmineIssueSplitResult(List<RedmineIssue> Stories, List<RedmineIssue> Tasks);

public sealed class RedmineProjectsResponse
{
    public List<RedmineProject>? Projects { get; set; }
}

public sealed class RedmineProject
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Identifier { get; set; } = string.Empty;

    public RedmineProjectStatus Status { get; set; }
}

public enum RedmineProjectStatus
{
    Active = 1,
    Closed = 5,
    Archived = 9
}

public sealed class RedmineIssuesResponse
{
    public List<RedmineIssue> Issues { get; set; } = new();

    public int TotalCount { get; set; }

    public int Offset { get; set; }

    public int Limit { get; set; }
}

public sealed class RedmineIssueResponse
{
    public RedmineIssue? Issue { get; set; }
}

public sealed class RedmineIssue
{
    public int Id { get; set; }

    public string Subject { get; set; } = string.Empty;

    public RedmineNamedValue? Tracker { get; set; }

    public RedmineNamedValue? Parent { get; set; }

    public RedmineIssueStatus? Status { get; set; }
}

public sealed class RedmineIssueStatus
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is_closed")]
    public bool IsClosed { get; set; }
}

public sealed class RedmineNamedValue
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class RedmineTimeEntryCreateRequest
{
    [JsonPropertyName("time_entry")]
    public RedmineTimeEntryCreateBody TimeEntry { get; set; } = new();
}

public sealed class RedmineTimeEntryCreateBody
{
    [JsonPropertyName("issue_id")]
    public int IssueId { get; set; }

    [JsonPropertyName("hours")]
    public decimal Hours { get; set; }

    [JsonPropertyName("spent_on")]
    public string SpentOn { get; set; } = string.Empty;

    [JsonPropertyName("comments")]
    public string Comments { get; set; } = string.Empty;
}

public sealed class RedmineTimeEntriesResponse
{
    [JsonPropertyName("time_entries")]
    public List<RedmineTimeEntry> TimeEntries { get; set; } = new();

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }
}

public sealed class RedmineTimeEntry
{
    public int Id { get; set; }

    public RedmineNamedValue? Project { get; set; }

    public decimal Hours { get; set; }

    [JsonPropertyName("spent_on")]
    public string SpentOn { get; set; } = string.Empty;
}
