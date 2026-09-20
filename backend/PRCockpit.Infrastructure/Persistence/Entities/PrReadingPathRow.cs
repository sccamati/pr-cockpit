namespace PRCockpit.Infrastructure.Persistence.Entities;

/// <summary>
/// The user-chosen order of key files, stored as one JSON array. Ordered rows would need a
/// transaction to reorder, and this application uses none.
/// </summary>
public sealed class PrReadingPathRow : PullRequestScopedRow
{
    public string PathsJson { get; set; } = "";

    /// <summary>How far the walkthrough of this path got, so it can be resumed (US-P7).</summary>
    public int Position { get; set; }

    /// <summary>The pull request's head when the path was chosen, for "it changed since".</summary>
    public string? HeadCommitSha { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
