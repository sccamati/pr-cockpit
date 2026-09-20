namespace PRCockpit.Domain.PullRequests;

public record Project(string Id, string Name);
public record Repository(string Id, string Name);
public record Reviewer(string Name, int Vote);
public record WorkItem(string Id, string Url);
// ObjectId is the blob id of the file at this iteration. It changes if and only if this
// file's content changed, which is what lets a reviewed marker survive an unrelated commit.
// Optional so every existing construction site, including the tests, keeps compiling.
public record ChangedFile(string Path, string ChangeType, string? OriginalPath, string? ObjectId = null);
public record Commit(string Id, string Message, string Author, DateTimeOffset? AuthoredAt);
public record FileDiff(string Path, string? OriginalPath, string Kind, string? OriginalText, string? ModifiedText);

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
