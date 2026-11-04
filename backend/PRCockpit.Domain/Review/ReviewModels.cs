namespace PRCockpit.Domain.Review;

public record FileReviewEntry(string Path, string? BlobId, string? HeadSha, DateTimeOffset UpdatedAt);

public record FileReviewState(
    IReadOnlyList<FileReviewEntry> Files, ReadingPathState ReadingPath, DateTimeOffset? UpdatedAt);

public record FileReviewUpdate(
    string? Path, bool? Reviewed, string? BlobId, string? HeadCommitSha, int? ChangedFilesCount);

public record FileReviewResult(FileReviewEntry? Entry, int ReviewedCount);
/// <summary>
/// The reading path, plus where the walkthrough of it stopped. Position is an index into
/// Paths, so Position == Paths.Count means the walkthrough reached the end and there is
/// nothing to resume (US-P7). HeadCommitSha is the pull request's head when the path was
/// chosen: a different head is what "the PR changed since you picked these files" means.
/// </summary>
public record ReadingPathUpdate(IReadOnlyList<string>? Paths, int? Position = null, string? HeadCommitSha = null);
public record ReadingPathState(
    IReadOnlyList<string> Paths, int Position, string? HeadCommitSha, DateTimeOffset? UpdatedAt);
public record FileReviewProgress(int PullRequestId, int ReviewedCount, int ChangedFilesCount);

/// <summary>
/// Raised for a request about the reviewer's own data that cannot be honoured — bad input,
/// a missing configuration, a quiz from an older commit. One type for all of them, so the
/// API failure mapper needs a single arm.
/// </summary>
public sealed class ReviewRequestException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
