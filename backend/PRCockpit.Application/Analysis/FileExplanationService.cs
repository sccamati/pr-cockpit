using PRCockpit.Application.Ports;
using PRCockpit.Application.PullRequests;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Application.Analysis;

/// <summary>
/// The second AI path: one file, on demand. The saved explanation carries the file's
/// content identity, so a file that has not changed is explained once and read back for
/// free — including after a commit that touched other files (US-P2).
/// </summary>
public sealed class FileExplanationService(
    IAzureDevOpsClient client,
    PullRequestContextService contexts,
    IAiSummaryAnalyzer analyzer,
    IFileExplanationStore store)
{
    public async Task<FileExplanation> ExplainAsync(
        string project, string repositoryId, int pullRequestId, string path, CancellationToken ct)
    {
        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        // The same allowlist the diff endpoint uses: a path outside this pull request's
        // file list never reaches Azure DevOps or the model.
        var changed = details.ChangedFiles.FirstOrDefault(file => file.Path == path);
        if (changed is null)
            throw new AzureDevOpsException("File is no longer in this pull request.", 404);

        var saved = await store.GetAsync(
            project, repositoryId, pullRequestId, path, changed.ObjectId, details.HeadCommitSha, ct);
        if (saved is not null) return saved.Result;

        var context = await contexts.BuildAsync(project, repositoryId, details, ct, path);
        var result = await SummaryRunner.RunFileAsync(context, analyzer, ct);
        await store.SaveAsync(project, repositoryId, pullRequestId, result, changed.ObjectId, ct);
        return result;
    }
}
