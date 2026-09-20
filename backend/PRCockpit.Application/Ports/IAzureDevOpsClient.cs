using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Application.Ports;

/// <summary>
/// Everything the application needs from Azure DevOps. Implemented in Infrastructure, so
/// nothing above this line knows about HTTP, JSON or personal access tokens.
/// </summary>
public interface IAzureDevOpsClient
{
    Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken ct);

    Task<IReadOnlyList<Repository>> GetRepositoriesAsync(string project, CancellationToken ct);

    Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsAsync(
        string project, string repositoryId, CancellationToken ct);

    Task<PullRequestDetails> GetPullRequestAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct);

    Task<FileDiff> GetFileDiffAsync(
        string project, string repositoryId, int pullRequestId, string path, CancellationToken ct);

    /// <summary>
    /// The pull request's comment threads, read only. System threads are left out.
    /// </summary>
    Task<IReadOnlyList<PrCommentThread>> GetCommentThreadsAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct);

    /// <summary>
    /// Serves a diff from an already-fetched pull request, so building a context package
    /// does not refetch the iteration and change list once per file.
    /// </summary>
    Task<FileDiff> GetFileDiffAsync(
        string project, string repositoryId, PullRequestDetails details, string path, CancellationToken ct);
}
