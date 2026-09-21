using PRCockpit.Application.Ports;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Infrastructure.AzureDevOps;

public sealed class AzureDevOpsClient(HttpClient http, IConfiguration configuration) : IAzureDevOpsClient
{
    private const string ApiVersion = "api-version=7.1";
    private const int MaxFileBytes = 256 * 1024;
    private const int MaxCommentLength = 10_000;

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
            string? baseCommit = null;
            string? sourceCommit = null;
            PrIteration[] iterationList;
            using (iterations)
            {
                iterationList = Values(iterations.RootElement).EnumerateArray()
                    .Select(item => new PrIteration(item.GetProperty("id").GetInt32(),
                        item.TryGetProperty("sourceRefCommit", out _) ? CommitId(item, "sourceRefCommit") : null))
                    .OrderBy(item => item.Id).ToArray();
                var latest = Values(iterations.RootElement).EnumerateArray()
                    .OrderByDescending(item => item.GetProperty("id").GetInt32()).FirstOrDefault();
                lastId = latest.ValueKind == JsonValueKind.Undefined ? 0 : latest.GetProperty("id").GetInt32();
                if (latest.ValueKind != JsonValueKind.Undefined &&
                    latest.TryGetProperty("commonRefCommit", out _))
                    baseCommit = CommitId(latest, "commonRefCommit");
                if (latest.ValueKind != JsonValueKind.Undefined &&
                    latest.TryGetProperty("sourceRefCommit", out _))
                    sourceCommit = CommitId(latest, "sourceRefCommit");
            }
            var changedFiles = lastId == 0
                ? []
                : await GetChangedFilesAsync(path, lastId, ct);
            var commits = await GetCommitsAsync(path, ct);
            var workItems = await GetWorkItemsAsync(path, ct);
            return AzureDevOpsMapper.Details(pr.RootElement, changedFiles, commits, workItems) with
            {
                BaseCommitSha = baseCommit,
                HeadCommitSha = sourceCommit,
                Iterations = iterationList
            };
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

        return await GetFileDiffAtCommitsAsync(project, repositoryId, file, baseCommit, sourceCommit, ct);
    }

    public async Task<IReadOnlyList<PrCommentThread>> GetCommentThreadsAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct)
    {
        if (pullRequestId <= 0) throw new AzureDevOpsException("Invalid pull request ID.", 400);
        var me = await CurrentUserIdAsync(ct);
        var (json, _) = await GetAsync(
            $"{PullRequestPath(project, repositoryId)}/{pullRequestId}/threads?{ApiVersion}", ct);
        using (json)
            return Values(json.RootElement).EnumerateArray()
                .Select(AzureDevOpsMapper.CommentThread)
                .Where(thread => !thread.IsSystem)
                .Select(thread => Mine(thread, me))
                .ToArray();
    }

    private static PrCommentThread Mine(PrCommentThread thread, string? me) =>
        me is null ? thread : thread with
        {
            Comments = thread.Comments
                .Select(comment => comment with
                {
                    IsMine = comment.AuthorId is not null &&
                             string.Equals(comment.AuthorId, me, StringComparison.OrdinalIgnoreCase),
                })
                .ToArray(),
        };

    // Who the PAT belongs to. Asked once per process: it cannot change while the backend
    // runs, and it would otherwise cost a round trip on every thread list.
    private async Task<string?> CurrentUserIdAsync(CancellationToken ct)
    {
        if (_currentUserAsked) return _currentUserId;
        _currentUserAsked = true;
        try
        {
            // connectionData is preview-only: plain "api-version=7.1" is rejected outright,
            // and the rejection is silent here, which is exactly how it went unnoticed.
            var (json, _) = await GetAsync($"_apis/connectionData?{ApiVersion}-preview", ct);
            using (json)
            {
                _currentUserId = json.RootElement.TryGetProperty("authenticatedUser", out var user) &&
                    user.TryGetProperty("id", out var id) ? id.GetString() : null;
            }
        }
        catch (AzureDevOpsException)
        {
            // Not knowing who we are only costs the edit and delete buttons, so a failure
            // here must not take the whole comment list down with it.
            _currentUserId = null;
        }
        return _currentUserId;
    }

    private bool _currentUserAsked;
    private string? _currentUserId;

    public async Task<PrCommentThread> UpdateCommentAsync(
        string project, string repositoryId, int pullRequestId, int threadId, int commentId,
        EditComment comment, CancellationToken ct)
    {
        var content = ValidateWrite(pullRequestId, comment.Content);
        if (threadId <= 0 || commentId <= 0) throw new AzureDevOpsException("Invalid comment.", 400);

        var path = $"{PullRequestPath(project, repositoryId)}/{pullRequestId}/threads/{threadId}";
        (await SendAsync($"{path}/comments/{commentId}?{ApiVersion}", "application/json", ct,
            HttpMethod.Patch, new { content })).Dispose();
        return await ReadThreadAsync(path, ct);
    }

    public async Task<PrCommentThread> DeleteCommentAsync(
        string project, string repositoryId, int pullRequestId, int threadId, int commentId,
        CancellationToken ct)
    {
        RequireCommentsEnabled();
        if (pullRequestId <= 0) throw new AzureDevOpsException("Invalid pull request ID.", 400);
        if (threadId <= 0 || commentId <= 0) throw new AzureDevOpsException("Invalid comment.", 400);

        var path = $"{PullRequestPath(project, repositoryId)}/{pullRequestId}/threads/{threadId}";
        // Azure DevOps soft-deletes: the comment stays in the thread, without content.
        (await SendAsync($"{path}/comments/{commentId}?{ApiVersion}", "application/json", ct,
            HttpMethod.Delete)).Dispose();
        return await ReadThreadAsync(path, ct);
    }

    public async Task<PrCommentThread> CreateCommentThreadAsync(
        string project, string repositoryId, int pullRequestId, NewCommentThread thread, CancellationToken ct)
    {
        var content = ValidateWrite(pullRequestId, thread.Content);
        object? context = null;
        if (!string.IsNullOrWhiteSpace(thread.FilePath))
        {
            if (!thread.FilePath.StartsWith('/') || thread.FilePath.Contains(' '))
                throw new AzureDevOpsException("Invalid file path.", 400);
            if (thread.Line is not null and (< 1 or > 1_000_000))
                throw new AzureDevOpsException("Invalid line number.", 400);
            // Always anchored on the right-hand side: with an inline diff only the modified
            // editor has addressable positions, so a left-side anchor could not be produced
            // by the UI in the first place. Offset 1 is what Azure DevOps' own example sends.
            context = thread.Line is null
                ? new { filePath = thread.FilePath }
                : new { filePath = thread.FilePath, rightFileStart = new { line = thread.Line, offset = 1 } };
        }

        return await WriteAsync(HttpMethod.Post,
            $"{PullRequestPath(project, repositoryId)}/{pullRequestId}/threads?{ApiVersion}",
            new
            {
                comments = new[] { new { parentCommentId = 0, content, commentType = 1 } },
                status = 1,
                threadContext = context,
            }, ct);
    }

    public async Task<PrCommentThread> ReplyToThreadAsync(
        string project, string repositoryId, int pullRequestId, int threadId, NewComment comment,
        CancellationToken ct)
    {
        var content = ValidateWrite(pullRequestId, comment.Content);
        if (threadId <= 0) throw new AzureDevOpsException("Invalid thread ID.", 400);

        var path = $"{PullRequestPath(project, repositoryId)}/{pullRequestId}/threads/{threadId}";
        // The reply endpoint answers with the comment, not the thread, so the thread is read
        // back afterwards: what the UI shows is always what Azure DevOps stores.
        (await SendAsync($"{path}/comments?{ApiVersion}", "application/json", ct,
            HttpMethod.Post, new { content, commentType = 1 })).Dispose();
        return await ReadThreadAsync(path, ct);
    }

    public async Task<PrCommentThread> SetThreadStatusAsync(
        string project, string repositoryId, int pullRequestId, int threadId, string? status,
        CancellationToken ct)
    {
        RequireCommentsEnabled();
        if (pullRequestId <= 0) throw new AzureDevOpsException("Invalid pull request ID.", 400);
        if (threadId <= 0) throw new AzureDevOpsException("Invalid thread ID.", 400);
        // A closed set: requests send the number, responses come back as the name.
        var value = status?.ToLowerInvariant() switch
        {
            "active" => 1,
            "fixed" => 2,
            "wontfix" => 3,
            "closed" => 4,
            _ => throw new AzureDevOpsException("Unknown thread status.", 400)
        };

        return await WriteAsync(HttpMethod.Patch,
            $"{PullRequestPath(project, repositoryId)}/{pullRequestId}/threads/{threadId}?{ApiVersion}",
            new { status = value }, ct);
    }

    private async Task<PrCommentThread> WriteAsync(
        HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var response = await SendAsync(path, "application/json", ct, method, body);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        try
        {
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return AzureDevOpsMapper.CommentThread(json.RootElement);
        }
        catch (JsonException)
        {
            throw new AzureDevOpsException("Azure DevOps returned an unexpected response.", 502);
        }
    }

    private async Task<PrCommentThread> ReadThreadAsync(string path, CancellationToken ct)
    {
        var (json, _) = await GetAsync($"{path}?{ApiVersion}", ct);
        using (json) return AzureDevOpsMapper.CommentThread(json.RootElement);
    }

    private string ValidateWrite(int pullRequestId, string? content)
    {
        RequireCommentsEnabled();
        if (pullRequestId <= 0) throw new AzureDevOpsException("Invalid pull request ID.", 400);
        var trimmed = content?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new AzureDevOpsException("A comment needs content.", 400);
        if (trimmed.Length > MaxCommentLength)
            throw new AzureDevOpsException($"A comment may be at most {MaxCommentLength} characters.", 400);
        return trimmed;
    }

    // Fails closed, and before anything leaves the machine: writing a comment is visible to
    // the whole team and cannot be undone, so it stays off until deliberately switched on.
    private void RequireCommentsEnabled()
    {
        if (!configuration.GetValue("AzureDevOps:AllowComments", false))
            throw new AzureDevOpsException(
                "Writing comments is switched off. Set AzureDevOps:AllowComments to true on the backend.", 503);
    }

    public Task<FileDiff> GetFileDiffAsync(
        string project, string repositoryId, PullRequestDetails details, string filePath, CancellationToken ct)
    {
        var file = details.ChangedFiles.FirstOrDefault(item => item.Path == filePath);
        if (file is null) throw new AzureDevOpsException("File is no longer in this pull request.", 404);
        if (details.BaseCommitSha is null || details.HeadCommitSha is null)
            throw new AzureDevOpsException("Azure DevOps did not provide the pull request commit IDs.", 502);
        return GetFileDiffAtCommitsAsync(project, repositoryId, file,
            details.BaseCommitSha, details.HeadCommitSha, ct);
    }

    public async Task<FileDiff> GetFileDiffSinceIterationAsync(
        string project, string repositoryId, int pullRequestId, string filePath, int iterationId,
        CancellationToken ct)
    {
        if (iterationId <= 0) throw new AzureDevOpsException("Invalid iteration.", 400);
        var details = await GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        var file = details.ChangedFiles.FirstOrDefault(item => item.Path == filePath);
        if (file is null) throw new AzureDevOpsException("File is no longer in this pull request.", 404);

        var since = details.Iterations?.FirstOrDefault(item => item.Id == iterationId);
        if (since?.SourceCommitSha is null)
            throw new AzureDevOpsException("That iteration is no longer available.", 404);
        if (details.HeadCommitSha is null)
            throw new AzureDevOpsException("Azure DevOps did not provide the pull request commit IDs.", 502);

        // Both sides are commits inside this pull request, so the file exists on each unless
        // it was added after the iteration being compared from. "edit" is the honest default
        // here: an add would suppress the old side and hide exactly what we came to show.
        return await GetFileDiffAtCommitsAsync(project, repositoryId,
            file with { ChangeType = "edit" }, since.SourceCommitSha, details.HeadCommitSha, ct);
    }

    public async Task<IReadOnlyList<string>> GetChangedPathsSinceIterationAsync(
        string project, string repositoryId, int pullRequestId, int iterationId, CancellationToken ct)
    {
        if (pullRequestId <= 0) throw new AzureDevOpsException("Invalid pull request ID.", 400);
        if (iterationId <= 0) throw new AzureDevOpsException("Invalid iteration.", 400);

        var prPath = $"{PullRequestPath(project, repositoryId)}/{pullRequestId}";
        var (iterations, _) = await GetAsync($"{prPath}/iterations?{ApiVersion}", ct);
        int lastId;
        using (iterations)
        {
            var latest = Values(iterations.RootElement).EnumerateArray()
                .OrderByDescending(item => item.GetProperty("id").GetInt32()).FirstOrDefault();
            if (latest.ValueKind == JsonValueKind.Undefined)
                throw new AzureDevOpsException("This pull request has no file changes.", 404);
            lastId = latest.GetProperty("id").GetInt32();
        }
        // Asking Azure DevOps to compare the last iteration with a later one is not an error
        // worth a page: nothing arrived after the newest push, and an empty list says exactly
        // that. Only iterations that never existed are refused.
        if (iterationId > lastId) throw new AzureDevOpsException("That iteration is no longer available.", 404);
        if (iterationId == lastId) return [];

        var files = await GetChangedFilesAsync(prPath, lastId, ct, iterationId);
        return files.Select(file => file.Path).ToArray();
    }

    private async Task<FileDiff> GetFileDiffAtCommitsAsync(
        string project, string repositoryId, ChangedFile file,
        string baseCommit, string sourceCommit, CancellationToken ct)
    {
        var changes = file.ChangeType.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var isAdded = changes.Contains("add", StringComparer.OrdinalIgnoreCase);
        var isDeleted = changes.Contains("delete", StringComparer.OrdinalIgnoreCase);
        var repositoryPath = $"{Segment(project)}/_apis/git/repositories/{Segment(repositoryId)}";
        var oldPath = file.OriginalPath ?? file.Path;
        var oldFile = isAdded ? new FileContent("text", "") :
            await GetFileContentAsync(repositoryPath, oldPath, baseCommit, ct);
        if (oldFile.Kind != "text") return new FileDiff(file.Path, file.OriginalPath, oldFile.Kind, null, null);
        var newFile = isDeleted ? new FileContent("text", "") :
            await GetFileContentAsync(repositoryPath, file.Path, sourceCommit, ct);
        if (newFile.Kind != "text") return new FileDiff(file.Path, file.OriginalPath, newFile.Kind, null, null);

        if (TextLineLimit.Exceeded(oldFile.Text, newFile.Text))
            return new FileDiff(file.Path, file.OriginalPath, "tooLarge", null, null);
        return new FileDiff(file.Path, file.OriginalPath, "text", oldFile.Text, newFile.Text);
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

    /// <summary>
    /// The files an iteration changed. <paramref name="compareTo"/> is the iteration to compare
    /// against: 0 is the merge base, so the default is the whole pull request, and any other
    /// iteration narrows it to what arrived after that push.
    /// </summary>
    private async Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(
        string path, int iterationId, CancellationToken ct, int compareTo = 0)
    {
        var files = new List<ChangedFile>();
        var skip = 0;
        while (true)
        {
            var (json, _) = await GetAsync($"{path}/iterations/{iterationId}/changes?$top=2000&$skip={skip}&$compareTo={compareTo}&{ApiVersion}", ct);
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

    private async Task<IReadOnlyList<Commit>> GetCommitsAsync(string path, CancellationToken ct)
    {
        var commits = new List<Commit>();
        string? continuation = null;
        do
        {
            var url = $"{path}/commits?$top=1000&{ApiVersion}";
            if (continuation is not null) url += "&continuationToken=" + Uri.EscapeDataString(continuation);
            var (json, next) = await GetAsync(url, ct);
            using (json) commits.AddRange(Values(json.RootElement).EnumerateArray().Select(AzureDevOpsMapper.Commit));
            continuation = next;
        } while (continuation is not null);
        return commits;
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

    /// <summary>
    /// One send path for reads and writes. The organization and PAT validation below is the
    /// critical part and must never be duplicated into a second method — a write travels the
    /// same road, it just carries a method and a body.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        string path, string accept, CancellationToken ct,
        HttpMethod? method = null, object? body = null)
    {
        var organization = configuration["AzureDevOps:Organization"];
        var pat = configuration["AzureDevOps:Pat"];
        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(pat))
            throw new AzureDevOpsException("Configure AzureDevOps:Organization and AzureDevOps:Pat on the backend.", 503);
        if (!System.Text.RegularExpressions.Regex.IsMatch(organization, "^[A-Za-z0-9][A-Za-z0-9-]*$"))
            throw new AzureDevOpsException("Invalid Azure DevOps organization name.", 503);

        using var request = new HttpRequestMessage(method ?? HttpMethod.Get,
            $"https://dev.azure.com/{organization}/{path}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + pat)));
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            // A write fails differently from a read. 401/403 on a write is almost always a
            // PAT missing the threads scope, which is a configuration fault on our side, so
            // it maps to 503 and the message names the scope instead of blaming the server.
            var isWrite = method is not null && method != HttpMethod.Get;
            // Editing or deleting somebody else's comment is refused by Azure DevOps, and
            // that is a 403 about ownership, not a configuration fault on our side.
            var isComment = isWrite && path.Contains("/comments/", StringComparison.Ordinal);
            var status = response.StatusCode switch
            {
                HttpStatusCode.Forbidden when isComment => 403,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => isWrite ? 503 : 502,
                HttpStatusCode.NotFound => 404,
                HttpStatusCode.BadRequest when isWrite => 400,
                HttpStatusCode.Conflict when isWrite => 409,
                HttpStatusCode.TooManyRequests => 503,
                _ => 502
            };
            var message = response.StatusCode switch
            {
                HttpStatusCode.Forbidden when isComment =>
                    "Azure DevOps refused this change. You can only edit or delete your own comments.",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden when isWrite =>
                    "The personal access token cannot write comments. Add the \"PR threads (read & write)\" scope; Code stays on Read.",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Azure DevOps rejected the configured credentials or access.",
                HttpStatusCode.NotFound => "Azure DevOps resource was not found.",
                HttpStatusCode.BadRequest when isWrite => "Azure DevOps rejected the comment.",
                HttpStatusCode.Conflict when isWrite => "This thread changed in Azure DevOps while you were writing. Reload the comments.",
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
