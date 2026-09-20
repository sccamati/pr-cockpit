namespace PRCockpit.Infrastructure.Persistence.Entities;

/// <summary>
/// One AI explanation per file of a pull request. The head commit is a column rather than
/// part of the key: a new head means the old explanation is stale and gets overwritten,
/// so the table never grows one row per iteration per file.
/// </summary>
public sealed class PrFileExplanationRow : PullRequestScopedRow
{
    public string FilePath { get; set; } = "";
    public string? HeadCommitSha { get; set; }
    public string ResponseJson { get; set; } = "";
    public DateTimeOffset SavedAt { get; set; }
}
