namespace PRCockpit.Domain.Review;

public record ChecklistState(
    bool AiReview, bool Quality, bool Understand, bool Architecture, bool Debug, bool Ready,
    DateTimeOffset? UpdatedAt);

public record ChecklistUpdate(bool? Completed);
public record ChecklistProgress(int PullRequestId, int CompletedCount);

public record FileReviewEntry(string Path, string? BlobId, string? HeadSha, DateTimeOffset UpdatedAt);

public record FileReviewState(
    IReadOnlyList<FileReviewEntry> Files, IReadOnlyList<string> ReadingPath, DateTimeOffset? UpdatedAt);

public record FileReviewUpdate(
    string? Path, bool? Reviewed, string? BlobId, string? HeadCommitSha, int? ChangedFilesCount);

public record FileReviewResult(FileReviewEntry? Entry, int ReviewedCount);
public record ReadingPathUpdate(IReadOnlyList<string>? Paths);
public record ReadingPathState(IReadOnlyList<string> Paths, DateTimeOffset? UpdatedAt);
public record FileReviewProgress(int PullRequestId, int ReviewedCount, int ChangedFilesCount);

/// <summary>
/// Raised by anything storing the reviewer's own decisions. One type for all of them,
/// so the API failure mapper needs a single arm.
/// </summary>
public sealed class ChecklistException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
