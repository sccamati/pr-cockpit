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
}

/// <summary>A generated summary, kept so reopening a pull request costs no model run.</summary>
public interface ISummaryStore
{
    Task<StoredSummary?> GetAsync(string project, string repositoryId, int pullRequestId, CancellationToken ct);

    Task<StoredSummary> SaveAsync(
        string project, string repositoryId, int pullRequestId, SummaryResponse result, CancellationToken ct);
}

/// <summary>Which files were read, and the order the reviewer wants to read them in.</summary>
public interface IReviewProgressStore
{
    Task<FileReviewState> GetAsync(string project, string repositoryId, int pullRequestId, CancellationToken ct);

    Task<FileReviewResult> SetFileAsync(
        string project, string repositoryId, int pullRequestId, FileReviewUpdate update, CancellationToken ct);

    Task<ReadingPathState> SetReadingPathAsync(
        string project, string repositoryId, int pullRequestId, IReadOnlyList<string>? paths, CancellationToken ct);

    Task<IReadOnlyList<FileReviewProgress>> GetProgressAsync(
        string project, string repositoryId, CancellationToken ct);
}
