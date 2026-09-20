namespace PRCockpit.Infrastructure.Persistence.Entities;

/// <summary>
/// One row per file marked as read. The row existing is what "reviewed" means, so
/// unmarking deletes it. The stored blob id is the file's content identity at the moment
/// it was read, which is how an unrelated commit avoids clearing the marker.
/// </summary>
public sealed class PrFileReviewRow : PullRequestScopedRow
{
    public string FilePath { get; set; } = "";
    public string? ReviewedBlobId { get; set; }
    public string? ReviewedHeadSha { get; set; }

    /// <summary>
    /// Denormalised from the frontend on every toggle, because the Azure DevOps pull
    /// request list does not carry a changed file count and fetching one per row would
    /// break the on-demand rule.
    /// </summary>
    public int ChangedFilesCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
