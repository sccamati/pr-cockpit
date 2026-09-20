using PRCockpit.Application.Ports;
using PRCockpit.Application.PullRequests;
using PRCockpit.Domain.Analysis;

namespace PRCockpit.Application.Analysis;

/// <summary>
/// The only path that runs AI. A fresh context is built, handed to the adapter, validated,
/// and only then stored — a failed run never replaces a good saved summary.
/// </summary>
public sealed class SummaryService(
    IAzureDevOpsClient client,
    PullRequestContextService contexts,
    IAiSummaryAnalyzer analyzer,
    ISummaryStore store)
{
    public async Task<SummaryResponse> GenerateAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct)
    {
        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        var context = await contexts.BuildAsync(project, repositoryId, details, ct);
        var result = await SummaryRunner.RunAsync(context, analyzer, ct);
        await store.SaveAsync(project, repositoryId, pullRequestId, result, ct);
        return result;
    }

    public Task<StoredSummary?> GetSavedAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct) =>
        store.GetAsync(project, repositoryId, pullRequestId, ct);
}
