namespace PRCockpit.Api.AzureDevOps;

public record Project(string Id, string Name);
public record Repository(string Id, string Name);
public record Reviewer(string Name, int Vote);
public record WorkItem(string Id, string Url);

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
    int CommitsCount,
    IReadOnlyList<WorkItem> WorkItems);

public sealed class AzureDevOpsException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
