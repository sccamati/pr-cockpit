using PRCockpit.Application.Ports;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Application.Analysis;

/// <summary>
/// "Where is this used?" for the file open in the diff — the reviewer's cue that a method
/// nobody calls yet is scaffolding, not finished logic. Answers from the repository at the
/// pull request's head commit, so a call added in the same pull request counts.
/// </summary>
public sealed class CodeUsageService(
    IAzureDevOpsClient client, ISourceSnapshotCache snapshots, ICodeUsageFinder finder)
{
    public async Task<CodeUsagesResponse> FindAsync(
        string project, string repositoryId, int pullRequestId, string path, CancellationToken ct)
    {
        if (!SourceFiles.IsSource(path))
            throw new AzureDevOpsException("Usages are available for C#, TypeScript, JavaScript and Vue files.", 400);

        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        // The same allowlist the diff uses: only a file this pull request changed, and only
        // one that still exists at its head.
        var changed = details.ChangedFiles.FirstOrDefault(file => file.Path == path);
        if (changed is null)
            throw new AzureDevOpsException("File is no longer in this pull request.", 404);
        if (changed.ChangeType.Contains("delete", StringComparison.OrdinalIgnoreCase))
            throw new AzureDevOpsException("This file is deleted in the pull request.", 404);

        var snapshot = await SnapshotAsync(project, repositoryId, details, ct);
        return await finder.FindAsync(snapshot, path, ct);
    }

    /// <summary>
    /// The text of one file that a usage points into, for the preview. Any file of the
    /// snapshot may be read — that is the point, most usages sit outside the pull request —
    /// and nothing outside it can be.
    /// </summary>
    public async Task<CodeSource> GetSourceAsync(
        string project, string repositoryId, int pullRequestId, string path, CancellationToken ct)
    {
        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        var snapshot = await SnapshotAsync(project, repositoryId, details, ct);
        return snapshot.Files.TryGetValue(path, out var text)
            ? new CodeSource(path, text)
            : throw new AzureDevOpsException("File is not among the repository's sources.", 404);
    }

    private Task<SourceSnapshot> SnapshotAsync(
        string project, string repositoryId, PullRequestDetails details, CancellationToken ct)
    {
        var head = details.HeadCommitSha
            ?? throw new AzureDevOpsException("Azure DevOps did not provide the pull request commit IDs.", 502);
        return snapshots.GetOrAddAsync($"{project}/{repositoryId}/{head}",
            token => client.GetSourceSnapshotAsync(project, repositoryId, head, token), ct);
    }
}
