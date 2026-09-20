namespace PRCockpit.Domain.PullRequests;

public record Project(string Id, string Name);
public record Repository(string Id, string Name);
public record Reviewer(string Name, int Vote);
public record WorkItem(string Id, string Url);
// ObjectId is the blob id of the file at this iteration. It changes if and only if this
// file's content changed, which is what lets a reviewed marker survive an unrelated commit.
// Optional so every existing construction site, including the tests, keeps compiling.
public record ChangedFile(string Path, string ChangeType, string? OriginalPath, string? ObjectId = null)
{
    // Computed, not a constructor parameter: the rule is a pure function of the path, so
    // every construction site stays untouched and the value cannot drift out of sync.
    public string? Category => FileCategory.Of(Path);
}
public record Commit(string Id, string Message, string Author, DateTimeOffset? AuthoredAt);
public record FileDiff(string Path, string? OriginalPath, string Kind, string? OriginalText, string? ModifiedText);

/// <summary>
/// One comment in a pull request thread. Everything except the id is optional because
/// Azure DevOps really does omit it: a soft-deleted comment arrives without content.
/// </summary>
public record PrComment(
    int Id, string? Author, string? Content, string? CommentType, DateTimeOffset? PublishedAt);

/// <summary>
/// A comment thread, anchored to a file and line when it has a thread context. System
/// threads (votes, merge attempts, reviewer changes) are marked rather than dropped here,
/// so the filtering decision stays with the caller.
/// </summary>
public record PrCommentThread(
    int Id, string? Status, string? FilePath, int? RightLine, int? LeftLine,
    bool IsSystem, IReadOnlyList<PrComment> Comments);

public record PullRequestSummary(
    int Id,
    string Title,
    string Author,
    string Repository,
    string Status,
    DateTimeOffset CreatedAt);

public record PullRequestDetails(
    int Id,
    string Title,
    string? Description,
    string Author,
    string Repository,
    string SourceBranch,
    string TargetBranch,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Reviewer> Reviewers,
    int ChangedFilesCount,
    IReadOnlyList<ChangedFile> ChangedFiles,
    int CommitsCount,
    IReadOnlyList<Commit> Commits,
    IReadOnlyList<WorkItem> WorkItems,
    string? BaseCommitSha = null,
    string? HeadCommitSha = null);

public sealed class AzureDevOpsException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
