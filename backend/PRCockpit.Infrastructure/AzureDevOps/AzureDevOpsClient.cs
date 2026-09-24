using PRCockpit.Application.Ports;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Infrastructure.AzureDevOps;

public sealed class AzureDevOpsClient(HttpClient http, IConfiguration configuration) : IAzureDevOpsClient
{
    private const string ApiVersion = "api-version=7.1";
    private const int MaxFileBytes = 256 * 1024;
    private const int MaxCommentLength = 10_000;
    // Enough for a large product repository; past it the answer would be slow to fetch and
    // a partial one would lie, so the snapshot is refused instead.
    private const int MaxSnapshotFiles = 8000;
    private const long MaxSnapshotBytes = 40L * 1024 * 1024;
    private const int BlobBatchSize = 500;
    private const int BlobFallbackParallelism = 8;

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
        // Only the change list depends on another answer (it needs the last iteration), so
        // everything else is asked for at once: two round trips of latency instead of five.
        var prTask = GetAsync($"{path}?{ApiVersion}", ct);
        var commitsTask = GetCommitsAsync(path, ct);
        var workItemsTask = GetWorkItemsAsync(path, ct);
        var changesTask = IterationChangesAsync();
        try
        {
            await Task.WhenAll(prTask, commitsTask, workItemsTask, changesTask);
        }
        catch
        {
            // Every call has ended here, so none is left running unobserved; the pull request
            // document, if it arrived, is released now instead of by the using below.
            if (prTask.IsCompletedSuccessfully) prTask.Result.Json.Dispose();
            throw;
        }
        var (iterations, changedFiles) = await changesTask;
        var (pr, _) = await prTask;
        using (pr)
            return AzureDevOpsMapper.Details(pr.RootElement, changedFiles, await commitsTask, await workItemsTask) with
            {
                BaseCommitSha = iterations.BaseCommit,
                HeadCommitSha = iterations.SourceCommit,
                Iterations = iterations.All
            };

        async Task<(IterationSet, IReadOnlyList<ChangedFile>)> IterationChangesAsync()
        {
            var iterations = await GetIterationsAsync(path, ct);
            return (iterations, iterations.LatestId == 0 ? [] : await GetChangedFilesAsync(path, iterations.LatestId, ct));
        }
    }

    public async Task<FileDiff> GetFileDiffAsync(
        string project, string repositoryId, int pullRequestId, string filePath, CancellationToken ct)
    {
        if (pullRequestId <= 0) throw new AzureDevOpsException("Invalid pull request ID.", 400);
        if (string.IsNullOrWhiteSpace(filePath) || !filePath.StartsWith('/') || filePath.Contains('\0'))
            throw new AzureDevOpsException("Invalid file path.", 400);

        var prPath = $"{PullRequestPath(project, repositoryId)}/{pullRequestId}";
        var iterations = await GetIterationsAsync(prPath, ct);
        if (iterations.LatestId == 0)
            throw new AzureDevOpsException("This pull request has no file changes.", 404);
        if (iterations.BaseCommit is null || iterations.SourceCommit is null)
            throw new AzureDevOpsException("Azure DevOps did not provide the pull request commit IDs.", 502);

        var file = (await GetChangedFilesAsync(prPath, iterations.LatestId, ct))
            .FirstOrDefault(item => item.Path == filePath);
        if (file is null) throw new AzureDevOpsException("File is no longer in this pull request.", 404);

        return await GetFileDiffAtCommitsAsync(project, repositoryId, file,
            iterations.BaseCommit, iterations.SourceCommit, ct);
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
            // by the UI in the first place.
            //
            // Both ends, always. A threadContext carrying rightFileStart without rightFileEnd
            // is accepted by the API and then crashes the Azure DevOps web UI, which reads
            // rightFileEnd.line while rendering the thread — a comment posted from here broke
            // the pull request page for everyone looking at it in the browser. Offsets 1 and 2
            // are what Azure DevOps' own example sends: a one-character span on the line.
            context = thread.Line is null
                ? new { filePath = thread.FilePath }
                : new
                {
                    filePath = thread.FilePath,
                    rightFileStart = new { line = thread.Line, offset = 1 },
                    rightFileEnd = new { line = thread.Line, offset = 2 },
                };
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
        if (pullRequestId <= 0) throw new AzureDevOpsException("Invalid pull request ID.", 400);
        if (iterationId <= 0) throw new AzureDevOpsException("Invalid iteration.", 400);
        // The iterations and the change list are all this needs — not the whole details
        // package, whose pull request, commits and work items would be three wasted calls.
        var prPath = $"{PullRequestPath(project, repositoryId)}/{pullRequestId}";
        var iterations = await GetIterationsAsync(prPath, ct);
        var file = iterations.LatestId == 0 ? null : (await GetChangedFilesAsync(prPath, iterations.LatestId, ct))
            .FirstOrDefault(item => item.Path == filePath);
        if (file is null) throw new AzureDevOpsException("File is no longer in this pull request.", 404);

        var since = iterations.All.FirstOrDefault(item => item.Id == iterationId);
        if (since?.SourceCommitSha is null)
            throw new AzureDevOpsException("That iteration is no longer available.", 404);
        if (iterations.SourceCommit is null)
            throw new AzureDevOpsException("Azure DevOps did not provide the pull request commit IDs.", 502);

        // Both sides are commits inside this pull request, so the file exists on each unless
        // it was added after the iteration being compared from. "edit" is the honest default
        // here: an add would suppress the old side and hide exactly what we came to show.
        return await GetFileDiffAtCommitsAsync(project, repositoryId,
            file with { ChangeType = "edit" }, since.SourceCommitSha, iterations.SourceCommit, ct);
    }

    public async Task<IReadOnlyList<string>> GetChangedPathsSinceIterationAsync(
        string project, string repositoryId, int pullRequestId, int iterationId, CancellationToken ct)
    {
        if (pullRequestId <= 0) throw new AzureDevOpsException("Invalid pull request ID.", 400);
        if (iterationId <= 0) throw new AzureDevOpsException("Invalid iteration.", 400);

        var prPath = $"{PullRequestPath(project, repositoryId)}/{pullRequestId}";
        var lastId = (await GetIterationsAsync(prPath, ct)).LatestId;
        if (lastId == 0) throw new AzureDevOpsException("This pull request has no file changes.", 404);
        // Asking Azure DevOps to compare the last iteration with a later one is not an error
        // worth a page: nothing arrived after the newest push, and an empty list says exactly
        // that. Only iterations that never existed are refused.
        if (iterationId > lastId) throw new AzureDevOpsException("That iteration is no longer available.", 404);
        if (iterationId == lastId) return [];

        var files = await GetChangedFilesAsync(prPath, lastId, ct, iterationId);
        return files.Select(file => file.Path).ToArray();
    }

    public async Task<SourceSnapshot> GetSourceSnapshotAsync(
        string project, string repositoryId, string commitSha, CancellationToken ct)
    {
        if (!IsObjectId(commitSha)) throw new AzureDevOpsException("Invalid commit ID.", 400);
        var repositoryPath = $"{Segment(project)}/_apis/git/repositories/{Segment(repositoryId)}";

        string treeId;
        var (commit, _) = await GetAsync($"{repositoryPath}/commits/{commitSha}?{ApiVersion}", ct);
        using (commit) treeId = commit.RootElement.TryGetProperty("treeId", out var tree) ? tree.GetString() ?? "" : "";
        if (!IsObjectId(treeId)) throw new AzureDevOpsException("Azure DevOps returned an invalid tree ID.", 502);

        // The tree carries every blob's size, so the budget is decided before a byte of
        // content is downloaded.
        var wanted = new List<(string Path, string ObjectId)>();
        var skipped = 0;
        long total = 0;
        var (entries, _) = await GetAsync($"{repositoryPath}/trees/{treeId}?recursive=true&{ApiVersion}", ct);
        using (entries)
        {
            foreach (var entry in entries.RootElement.GetProperty("treeEntries").EnumerateArray())
            {
                if (entry.TryGetProperty("gitObjectType", out var type) && type.GetString() != "blob") continue;
                var path = "/" + (entry.GetProperty("relativePath").GetString() ?? "").TrimStart('/');
                if (!SourceFiles.IsSource(path)) continue;
                var size = entry.TryGetProperty("size", out var bytes) ? bytes.GetInt64() : 0;
                if (size > MaxFileBytes)
                {
                    skipped++;
                    continue;
                }
                var objectId = entry.GetProperty("objectId").GetString() ?? "";
                if (!IsObjectId(objectId)) throw new AzureDevOpsException("Azure DevOps returned an invalid blob ID.", 502);
                wanted.Add((path, objectId.ToLowerInvariant()));
                total += size;
            }
        }
        if (wanted.Count > MaxSnapshotFiles || total > MaxSnapshotBytes)
            throw new AzureDevOpsException(
                $"The repository has too many source files to find usages ({wanted.Count} files, {total / (1024 * 1024)} MB).", 413);

        var blobs = await GetBlobsAsync(repositoryPath, wanted.Select(item => item.ObjectId).Distinct().ToArray(), ct);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, objectId) in wanted)
        {
            if (blobs.TryGetValue(objectId, out var content) && content.Kind == "text") files[path] = content.Text;
            else skipped++;
        }
        return new SourceSnapshot(commitSha, files, skipped);
    }

    /// <summary>
    /// Many blobs in few round trips: Azure DevOps zips a list of blob IDs, one entry per ID.
    /// Anything the zip did not deliver is fetched one by one, so a change in how the entries
    /// are named costs speed, not correctness.
    /// </summary>
    private async Task<Dictionary<string, FileContent>> GetBlobsAsync(
        string repositoryPath, IReadOnlyList<string> objectIds, CancellationToken ct)
    {
        var blobs = new Dictionary<string, FileContent>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in objectIds.Chunk(BlobBatchSize))
        {
            try
            {
                using var response = await SendAsync($"{repositoryPath}/blobs?{ApiVersion}", "application/zip", ct,
                    HttpMethod.Post, batch, isRead: true);
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                var requested = batch.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    var id = Path.GetFileNameWithoutExtension(entry.Name);
                    if (!requested.Contains(id) || entry.Length > MaxFileBytes) continue;
                    await using var content = entry.Open();
                    var bytes = await ReadBoundedAsync(content, ct);
                    if (bytes is not null) blobs[id] = Decode(bytes);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException ||
                ex is AzureDevOpsException { StatusCode: 400 or 404 or 502 })
            {
                // The zip endpoint refused or answered with something that is not a zip; the
                // one-by-one path below covers the whole batch.
            }
        }

        using var gate = new SemaphoreSlim(BlobFallbackParallelism);
        var missing = objectIds.Where(id => !blobs.ContainsKey(id)).Select(async id =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return (id, content: await DownloadBlobAsync(repositoryPath, id, ct));
            }
            finally
            {
                gate.Release();
            }
        });
        foreach (var (id, content) in await Task.WhenAll(missing)) blobs[id] = content;
        return blobs;
    }

    private static bool IsObjectId(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-fA-F0-9]{40}$");

    private async Task<FileDiff> GetFileDiffAtCommitsAsync(
        string project, string repositoryId, ChangedFile file,
        string baseCommit, string sourceCommit, CancellationToken ct)
    {
        var changes = file.ChangeType.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var isAdded = changes.Contains("add", StringComparer.OrdinalIgnoreCase);
        var isDeleted = changes.Contains("delete", StringComparer.OrdinalIgnoreCase);
        var repositoryPath = $"{Segment(project)}/_apis/git/repositories/{Segment(repositoryId)}";
        var oldPath = file.OriginalPath ?? file.Path;
        // Both sides at once. A binary old side makes the new one a wasted call, which is
        // cheaper than paying two sequential round trips on every file that opens.
        var oldTask = isAdded ? Task.FromResult(new FileContent("text", "")) :
            GetFileContentAsync(repositoryPath, oldPath, baseCommit, ct);
        var newTask = isDeleted ? Task.FromResult(new FileContent("text", "")) :
            GetFileContentAsync(repositoryPath, file.Path, sourceCommit, ct);
        try
        {
            await Task.WhenAll(oldTask, newTask);
        }
        catch when (oldTask.IsCompletedSuccessfully && oldTask.Result.Kind != "text")
        {
            // The old side alone decides this diff, so a failure on the wasted call is not its error.
        }
        var oldFile = await oldTask;
        if (oldFile.Kind != "text") return new FileDiff(file.Path, file.OriginalPath, oldFile.Kind, null, null);
        var newFile = await newTask;
        if (newFile.Kind != "text") return new FileDiff(file.Path, file.OriginalPath, newFile.Kind, null, null);

        if (TextLineLimit.Exceeded(oldFile.Text, newFile.Text))
            return new FileDiff(file.Path, file.OriginalPath, "tooLarge", null, null);
        return new FileDiff(file.Path, file.OriginalPath, "text", oldFile.Text, newFile.Text);
    }

    private sealed record IterationSet(
        IReadOnlyList<PrIteration> All, int LatestId, string? BaseCommit, string? SourceCommit);

    /// <summary>
    /// Every iteration of the pull request, oldest first, plus the two commits of the newest
    /// one that diffs, the context package and the Summary all derive from. LatestId is 0
    /// when the pull request has no iteration yet.
    /// </summary>
    private async Task<IterationSet> GetIterationsAsync(string prPath, CancellationToken ct)
    {
        var (json, _) = await GetAsync($"{prPath}/iterations?{ApiVersion}", ct);
        using (json)
        {
            var items = Values(json.RootElement).EnumerateArray()
                .OrderBy(item => item.GetProperty("id").GetInt32()).ToArray();
            var all = items.Select(item =>
                new PrIteration(item.GetProperty("id").GetInt32(), OptionalCommitId(item, "sourceRefCommit"))).ToArray();
            return items.Length == 0
                ? new IterationSet(all, 0, null, null)
                : new IterationSet(all, all[^1].Id, OptionalCommitId(items[^1], "commonRefCommit"), all[^1].SourceCommitSha);
        }
    }

    private static string? OptionalCommitId(JsonElement iteration, string property) =>
        iteration.TryGetProperty(property, out _) ? CommitId(iteration, property) : null;

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

        return await DownloadBlobAsync(repositoryPath, objectId, ct);
    }

    private async Task<FileContent> DownloadBlobAsync(string repositoryPath, string objectId, CancellationToken ct)
    {
        using var response = await SendAsync($"{repositoryPath}/blobs/{objectId}?$format=octetstream&resolveLfs=true&{ApiVersion}",
            "application/octet-stream", ct);
        if (response.Content.Headers.ContentLength > MaxFileBytes) return new FileContent("tooLarge", "");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var bytes = await ReadBoundedAsync(stream, ct);
        return bytes is null ? new FileContent("tooLarge", "") : Decode(bytes);
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (memory.Length + read > MaxFileBytes) return null;
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    private static FileContent Decode(byte[] bytes)
    {
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
                    // A big pull request really does contain entries with no item path (a folder
                    // rename, a submodule). Nothing downstream can diff, mark or list them, so drop
                    // them here rather than carrying a null path into serialization.
                    if (!item.TryGetProperty("path", out var itemPath) || string.IsNullOrEmpty(itemPath.GetString()))
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
        HttpMethod? method = null, object? body = null, bool isRead = false)
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
            // isRead marks a POST that only reads (the blob zip), so it is not blamed on the
            // comment-writing scope.
            var isWrite = !isRead && method is not null && method != HttpMethod.Get;
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
