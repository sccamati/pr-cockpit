using PRCockpit.Application.Ports;
using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Application.PullRequests;

/// <summary>
/// Builds the bounded context package for a pull request. Lives here rather than in an
/// endpoint so the fetch-then-build sequence has one home and one owner.
/// </summary>
public sealed class PullRequestContextService(IAzureDevOpsClient client)
{
    public async Task<PrContext> BuildAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct)
    {
        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        return await BuildAsync(project, repositoryId, details, ct);
    }

    public Task<PrContext> BuildAsync(
        string project, string repositoryId, PullRequestDetails details, CancellationToken ct) =>
        PrContextBuilder.BuildAsync(details,
            (path, token) => client.GetFileDiffAsync(project, repositoryId, details, path, token),
            ContextBudget.Default, ct);
}
