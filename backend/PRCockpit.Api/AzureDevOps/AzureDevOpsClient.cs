using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PRCockpit.Api.AzureDevOps;

public sealed class AzureDevOpsClient(HttpClient http, IConfiguration configuration)
{
    private const string ApiVersion = "api-version=7.1";
    private const int MaxFileBytes = 256 * 1024;

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
            int lastId;
            using (iterations)
            {
                lastId = Values(iterations.RootElement).EnumerateArray()
                    .Select(item => item.GetProperty("id").GetInt32())
                    .DefaultIfEmpty(0).Max();
            }
            var changedFiles = lastId == 0
                ? []
                : await GetChangedFilesAsync(path, lastId, ct);
            var commitsCount = await CountCommitsAsync(path, ct);
            var workItems = await GetWorkItemsAsync(path, ct);
            return AzureDevOpsMapper.Details(pr.RootElement, changedFiles, commitsCount, workItems);
        }
    }

    public async Task<FileDiff> GetFileDiffAsync(
        string project, string repositoryId, int pullRequestId, string filePath, CancellationToken ct)
    {
        if (pullRequestId <= 0) throw new AzureDevOpsException("Invalid pull request ID.", 400);
        if (string.IsNullOrWhiteSpace(filePath) || !filePath.StartsWith('/') || filePath.Contains('\0'))
            throw new AzureDevOpsException("Invalid file path.", 400);

        var prPath = $"{PullRequestPath(project, repositoryId)}/{pullRequestId}";
        var (iterations, _) = await GetAsync($"{prPath}/iterations?{ApiVersion}", ct);
        int iterationId;
        string baseCommit;
        string sourceCommit;
        using (iterations)
        {
            var latest = Values(iterations.RootElement).EnumerateArray()
                .OrderByDescending(item => item.GetProperty("id").GetInt32()).FirstOrDefault();
            if (latest.ValueKind == JsonValueKind.Undefined)
                throw new AzureDevOpsException("This pull request has no file changes.", 404);
            iterationId = latest.GetProperty("id").GetInt32();
            baseCommit = CommitId(latest, "commonRefCommit");
            sourceCommit = CommitId(latest, "sourceRefCommit");
        }

        var file = (await GetChangedFilesAsync(prPath, iterationId, ct))
            .FirstOrDefault(item => item.Path == filePath);
        if (file is null) throw new AzureDevOpsException("File is no longer in this pull request.", 404);

        var changes = file.ChangeType.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var isAdded = changes.Contains("add", StringComparer.OrdinalIgnoreCase);
        var isDeleted = changes.Contains("delete", StringComparer.OrdinalIgnoreCase);
        var repositoryPath = $"{Segment(project)}/_apis/git/repositories/{Segment(repositoryId)}";
        var oldPath = file.OriginalPath ?? file.Path;
        var oldFile = isAdded ? new FileContent("text", "") :
            await GetFileContentAsync(repositoryPath, oldPath, baseCommit, ct);
        if (oldFile.Kind != "text") return new FileDiff(file.Path, file.OriginalPath, oldFile.Kind, []);
        var newFile = isDeleted ? new FileContent("text", "") :
            await GetFileContentAsync(repositoryPath, file.Path, sourceCommit, ct);
        if (newFile.Kind != "text") return new FileDiff(file.Path, file.OriginalPath, newFile.Kind, []);

        var lines = LineDiff.Compare(oldFile.Text, newFile.Text);
        return new FileDiff(file.Path, file.OriginalPath, lines is null ? "tooLarge" : "text", lines ?? []);
    }

    private static string CommitId(JsonElement iteration, string property)
    {
        var commit = iteration.GetProperty(property).GetProperty("commitId").GetString();
        if (commit is null || !System.Text.RegularExpressions.Regex.IsMatch(commit, "^[a-fA-F0-9]{40}$"))
            throw new AzureDevOpsException("Azure DevOps returned an invalid commit ID.", 502);
        return commit;
    }

    private async Task<FileContent> GetFileContentAsync(string repositoryPath, string path, string commit, CancellationToken ct)
    {
        var url = $"{repositoryPath}/items?path={Uri.EscapeDataString(path)}&versionDescriptor.version={commit}" +
            $"&versionDescriptor.versionType=commit&includeContentMetadata=true&{ApiVersion}";
        var (json, _) = await GetAsync(url, ct);
        string objectId;
        using (json)
        {
            var item = json.RootElement;
            if (item.TryGetProperty("isFolder", out var folder) && folder.GetBoolean() ||
                item.TryGetProperty("isSymLink", out var link) && link.GetBoolean() ||
                item.TryGetProperty("contentMetadata", out var metadata) &&
                metadata.TryGetProperty("isBinary", out var binary) && binary.GetBoolean())
                return new FileContent("binary", "");
            objectId = item.GetProperty("objectId").GetString() ?? "";
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(objectId, "^[a-fA-F0-9]{40}$"))
            throw new AzureDevOpsException("Azure DevOps returned an invalid blob ID.", 502);

        using var response = await SendAsync($"{repositoryPath}/blobs/{objectId}?$format=octetstream&resolveLfs=true&{ApiVersion}",
            "application/octet-stream", ct);
        if (response.Content.Headers.ContentLength > MaxFileBytes) return new FileContent("tooLarge", "");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (memory.Length + read > MaxFileBytes) return new FileContent("tooLarge", "");
            memory.Write(buffer, 0, read);
        }
        var bytes = memory.ToArray();
        if (bytes.Contains((byte)0) && !(bytes.Length >= 2 &&
            (bytes[0] == 0xff && bytes[1] == 0xfe || bytes[0] == 0xfe && bytes[1] == 0xff)))
            return new FileContent("binary", "");
        try
        {
            if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
                return new FileContent("text", new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2));
            if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
                return new FileContent("text", new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2));
            var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            return new FileContent("text", new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset));
        }
        catch (DecoderFallbackException)
        {
            return new FileContent("binary", "");
        }
    }

    private sealed record FileContent(string Kind, string Text);

    private async Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(
        string path, int iterationId, CancellationToken ct)
    {
        var files = new List<ChangedFile>();
        var skip = 0;
        while (true)
        {
            var (json, _) = await GetAsync($"{path}/iterations/{iterationId}/changes?$top=2000&$skip={skip}&$compareTo=0&{ApiVersion}", ct);
            using (json)
            {
                foreach (var entry in json.RootElement.GetProperty("changeEntries").EnumerateArray())
                {
                    var item = entry.GetProperty("item");
                    if (item.TryGetProperty("isFolder", out var isFolder) && isFolder.GetBoolean())
                        continue;
                    files.Add(AzureDevOpsMapper.ChangedFile(entry));
                }
                var nextSkip = json.RootElement.TryGetProperty("nextSkip", out var next)
                    ? next.GetInt32() : 0;
                if (nextSkip <= skip) return files;
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
        using var response = await SendAsync(path, "application/json", ct);
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

    private async Task<HttpResponseMessage> SendAsync(string path, string accept, CancellationToken ct)
    {
        var organization = configuration["AzureDevOps:Organization"];
        var pat = configuration["AzureDevOps:Pat"];
        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(pat))
            throw new AzureDevOpsException("Configure AzureDevOps:Organization and AzureDevOps:Pat on the backend.", 503);
        if (!System.Text.RegularExpressions.Regex.IsMatch(organization, "^[A-Za-z0-9][A-Za-z0-9-]*$"))
            throw new AzureDevOpsException("Invalid Azure DevOps organization name.", 503);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://dev.azure.com/{organization}/{path}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + pat)));
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
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
            response.Dispose();
            throw new AzureDevOpsException(message, status);
        }
        return response;
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
