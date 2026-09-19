namespace PRCockpit.Api.Persistence.Entities;

/// <summary>
/// A generated Summary kept as the whole validated response, plus the head commit it was
/// generated from so the UI can tell the reader when it went out of date.
/// </summary>
public sealed class PrSummaryRow : PullRequestScopedRow
{
    public string? HeadCommitSha { get; set; }
    public string ResponseJson { get; set; } = "";
    public DateTimeOffset SavedAt { get; set; }
}
