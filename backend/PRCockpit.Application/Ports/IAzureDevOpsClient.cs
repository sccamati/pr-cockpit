using PRCockpit.Domain.Analysis;
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
    /// The three write operations, all guarded by AzureDevOps:AllowComments. Each returns
    /// the thread as Azure DevOps stored it, never as we hoped it was stored.
    /// </summary>
    Task<PrCommentThread> CreateCommentThreadAsync(
        string project, string repositoryId, int pullRequestId, NewCommentThread thread, CancellationToken ct);

    Task<PrCommentThread> ReplyToThreadAsync(
        string project, string repositoryId, int pullRequestId, int threadId, NewComment comment,
        CancellationToken ct);

    /// <summary>
    /// Editing and deleting reach only your own comments — Azure DevOps refuses the rest,
    /// which is why every comment carries whether it is yours.
    /// </summary>
    Task<PrCommentThread> UpdateCommentAsync(
        string project, string repositoryId, int pullRequestId, int threadId, int commentId,
        EditComment comment, CancellationToken ct);

    Task<PrCommentThread> DeleteCommentAsync(
        string project, string repositoryId, int pullRequestId, int threadId, int commentId,
        CancellationToken ct);

    Task<PrCommentThread> SetThreadStatusAsync(
        string project, string repositoryId, int pullRequestId, int threadId, string? status,
        CancellationToken ct);

    /// <summary>
    /// Serves a diff from an already-fetched pull request, so building a context package
    /// does not refetch the iteration and change list once per file.
    /// </summary>
    Task<FileDiff> GetFileDiffAsync(
        string project, string repositoryId, PullRequestDetails details, string path, CancellationToken ct);

    /// <summary>
    /// The same file, compared between one iteration of the pull request and its head —
    /// what changed since somebody left a comment on that iteration.
    /// </summary>
    Task<FileDiff> GetFileDiffSinceIterationAsync(
        string project, string repositoryId, int pullRequestId, string path, int iterationId,
        CancellationToken ct);

    /// <summary>
    /// Which files changed after a given iteration — the list behind the "changes since
    /// update N" filter. Paths only: the caller already holds the full change list and needs
    /// nothing from here but which of its rows to keep.
    /// </summary>
    Task<IReadOnlyList<string>> GetChangedPathsSinceIterationAsync(
        string project, string repositoryId, int pullRequestId, int iterationId, CancellationToken ct);

    /// <summary>
    /// Every source file of the repository at one commit (see <see cref="SourceFiles"/>), for
    /// finding where a declaration is used. Refuses with 413 rather than return part of an
    /// oversized repository, because a partial snapshot would report "unused" falsely.
    /// </summary>
    Task<SourceSnapshot> GetSourceSnapshotAsync(
        string project, string repositoryId, string commitSha, CancellationToken ct);
}
