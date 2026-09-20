namespace PRCockpit.Infrastructure.Persistence.Entities;

/// <summary>
/// One AI explanation per file of a pull request. The file's content identity is a column
/// rather than part of the key: different content means the old explanation is stale and
/// gets overwritten, so the table never grows one row per iteration per file. The head
/// commit stays as the fallback for files Azure DevOps gives no blob id for.
/// </summary>
public sealed class PrFileExplanationRow : PullRequestScopedRow
{
    public string FilePath { get; set; } = "";
    public string? BlobId { get; set; }
    public string? HeadCommitSha { get; set; }
    public string ResponseJson { get; set; } = "";
    public DateTimeOffset SavedAt { get; set; }
}
