using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PRCockpit.Api.AzureDevOps;

public sealed class AzureDevOpsClient(HttpClient http, IConfiguration configuration)
{
    private const string ApiVersion = "api-version=7.1";

    public async Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken ct)
    {
        var projects = new List<Project>();
        string? continuation = null;
        do
        {
            var path = "_apis/projects?$top=100&" + ApiVersion;
            if (continuation is not null) path += "&continuationToken=" + Uri.EscapeDataString(continuation);
            var (json, next) = await GetAsync(path, ct);
            using (json) projects.AddRange(Values(json.RootElement).EnumerateArray().Select(AzureDevOpsMapper.Project));
            continuation = next;
        } while (continuation is not null);
        return projects;
    }

    public async Task<IReadOnlyList<Repository>> GetRepositoriesAsync(string project, CancellationToken ct)
    {
        var (json, _) = await GetAsync($"{Segment(project)}/_apis/git/repositories?{ApiVersion}", ct);
        using (json) return Values(json.RootElement).EnumerateArray().Select(AzureDevOpsMapper.Repository).ToArray();
    }

    public async Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsAsync(
        string project, string repositoryId, CancellationToken ct)
    {
        var path = PullRequestPath(project, repositoryId);
        var results = new List<PullRequestSummary>();
        const int pageSize = 100;
        for (var skip = 0; ; skip += pageSize)
        {
            var (json, _) = await GetAsync($"{path}?searchCriteria.status=active&$top={pageSize}&$skip={skip}&{ApiVersion}", ct);
            using (json)
            {
                var page = Values(json.RootElement).EnumerateArray().Select(AzureDevOpsMapper.Summary).ToArray();
                results.AddRange(page);
                if (page.Length < pageSize) break;
            }
        }
        return results;
    }

    public async Task<PullRequestDetails> GetPullRequestAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct)
    {
        if (pullRequestId <= 0) throw new AzureDevOpsException("Invalid pull request ID.", 400);
        var path = $"{PullRequestPath(project, repositoryId)}/{pullRequestId}";
        var (pr, _) = await GetAsync($"{path}?{ApiVersion}", ct);
        using (pr)
        {
            var (iterations, _) = await GetAsync($"{path}/iterations?{ApiVersion}", ct);
            int changedFiles;
            using (iterations)
            {
                var lastId = Values(iterations.RootElement).EnumerateArray()
                    .Select(item => item.GetProperty("id").GetInt32())
                    .DefaultIfEmpty(0).Max();
                changedFiles = lastId == 0 ? 0 : await CountChangedFilesAsync(path, lastId, ct);
            }
            var commitsCount = await CountCommitsAsync(path, ct);
            var workItems = await GetWorkItemsAsync(path, ct);
            return AzureDevOpsMapper.Details(pr.RootElement, changedFiles, commitsCount, workItems);
        }
    }

    private async Task<int> CountChangedFilesAsync(string path, int iterationId, CancellationToken ct)
    {
        var count = 0;
        var skip = 0;
        while (true)
        {
            var (json, _) = await GetAsync($"{path}/iterations/{iterationId}/changes?$top=2000&$skip={skip}&$compareTo=0&{ApiVersion}", ct);
            using (json)
            {
                count += json.RootElement.GetProperty("changeEntries").GetArrayLength();
                var nextSkip = json.RootElement.TryGetProperty("nextSkip", out var next)
                    ? next.GetInt32() : 0;
                if (nextSkip <= skip) return count;
                skip = nextSkip;
            }
        }
    }

    private async Task<int> CountCommitsAsync(string path, CancellationToken ct)
    {
        var count = 0;
        string? continuation = null;
        do
        {
            var url = $"{path}/commits?$top=1000&{ApiVersion}";
            if (continuation is not null) url += "&continuationToken=" + Uri.EscapeDataString(continuation);
            var (json, next) = await GetAsync(url, ct);
            using (json) count += Values(json.RootElement).GetArrayLength();
            continuation = next;
        } while (continuation is not null);
        return count;
    }

    private async Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(string path, CancellationToken ct)
    {
        var (json, _) = await GetAsync($"{path}/workitems?{ApiVersion}", ct);
        using (json) return Values(json.RootElement).EnumerateArray().Select(AzureDevOpsMapper.WorkItem).ToArray();
    }

    private async Task<(JsonDocument Json, string? Continuation)> GetAsync(string path, CancellationToken ct)
    {
        var organization = configuration["AzureDevOps:Organization"];
        var pat = configuration["AzureDevOps:Pat"];
        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(pat))
            throw new AzureDevOpsException("Configure AzureDevOps:Organization and AzureDevOps:Pat on the backend.", 503);
        if (!System.Text.RegularExpressions.Regex.IsMatch(organization, "^[A-Za-z0-9][A-Za-z0-9-]*$"))
            throw new AzureDevOpsException("Invalid Azure DevOps organization name.", 503);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://dev.azure.com/{organization}/{path}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + pat)));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => 502,
                HttpStatusCode.NotFound => 404,
                HttpStatusCode.TooManyRequests => 503,
                _ => 502
            };
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Azure DevOps rejected the configured credentials or access.",
                HttpStatusCode.NotFound => "Azure DevOps resource was not found.",
                HttpStatusCode.TooManyRequests => "Azure DevOps rate limit reached. Try again later.",
                _ => "Azure DevOps request failed."
            };
            throw new AzureDevOpsException(message, status);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        JsonDocument json;
        try
        {
            json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (JsonException)
        {
            throw new AzureDevOpsException("Azure DevOps returned an unexpected response.", 502);
        }
        var continuation = response.Headers.TryGetValues("x-ms-continuationtoken", out var values)
            ? values.FirstOrDefault() : null;
        return (json, string.IsNullOrEmpty(continuation) ? null : continuation);
    }

    private static JsonElement Values(JsonElement root) => root.ValueKind == JsonValueKind.Array
        ? root : root.GetProperty("value");

    private static string PullRequestPath(string project, string repositoryId) =>
        $"{Segment(project)}/_apis/git/repositories/{Segment(repositoryId)}/pullrequests";

    private static string Segment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or "..")
            throw new AzureDevOpsException("Invalid project or repository.", 400);
        return Uri.EscapeDataString(value);
    }
}
