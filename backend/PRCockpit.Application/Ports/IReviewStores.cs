using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.Review;

namespace PRCockpit.Application.Ports;

/// <summary>The reviewer's six manual steps.</summary>
public interface IChecklistStore
{
    Task<ChecklistState> GetAsync(string project, string repositoryId, int pullRequestId, CancellationToken ct);

    Task<IReadOnlyList<ChecklistProgress>> GetProgressAsync(
        string project, string repositoryId, CancellationToken ct);

    Task<ChecklistState> SetAsync(
        string project, string repositoryId, int pullRequestId, string item, bool? completed, CancellationToken ct);

    Task<ChecklistState> SetDebugNoteAsync(
        string project, string repositoryId, int pullRequestId, string? note, CancellationToken ct);
}

/// <summary>A generated summary, kept so reopening a pull request costs no model run.</summary>
public interface ISummaryStore
{
    Task<StoredSummary?> GetAsync(string project, string repositoryId, int pullRequestId, CancellationToken ct);

    Task<StoredSummary> SaveAsync(
        string project, string repositoryId, int pullRequestId, SummaryResponse result, CancellationToken ct);
}

/// <summary>
/// One explanation per file, so reopening a file costs no model run. Keyed by path; the
/// file's blob id and the head commit are columns, and a row describing other content is
/// simply regenerated over. Staleness follows <see cref="ContentFreshness"/>, so one
/// commit no longer throws away the explanations of files nobody touched (US-P2).
/// </summary>
public interface IFileExplanationStore
{
    Task<StoredFileExplanation?> GetAsync(
        string project, string repositoryId, int pullRequestId, string path,
        string? blobId, string? headCommitSha, CancellationToken ct);

    Task<StoredFileExplanation> SaveAsync(
        string project, string repositoryId, int pullRequestId, FileExplanation result,
        string? blobId, CancellationToken ct);
}

/// <summary>Which files were read, and the order the reviewer wants to read them in.</summary>
public interface IReviewProgressStore
{
    Task<FileReviewState> GetAsync(string project, string repositoryId, int pullRequestId, CancellationToken ct);

    Task<FileReviewResult> SetFileAsync(
        string project, string repositoryId, int pullRequestId, FileReviewUpdate update, CancellationToken ct);

    Task<ReadingPathState> SetReadingPathAsync(
        string project, string repositoryId, int pullRequestId, ReadingPathUpdate update, CancellationToken ct);

    Task<IReadOnlyList<FileReviewProgress>> GetProgressAsync(
        string project, string repositoryId, CancellationToken ct);
}
