using Microsoft.EntityFrameworkCore;
using PRCockpit.Api.Persistence;
using PRCockpit.Api.Persistence.Entities;

namespace PRCockpit.Api.Checklists;

public record ChecklistState(
    bool AiReview, bool Quality, bool Understand, bool Architecture, bool Debug, bool Ready,
    DateTimeOffset? UpdatedAt);

public record ChecklistUpdate(bool? Completed);
public record ChecklistProgress(int PullRequestId, int CompletedCount);

public sealed class ChecklistException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class ChecklistStore(PrCockpitContext context, IConfiguration configuration)
{
    public async Task<ChecklistState> GetAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        var row = await context.Checklists
            .AsNoTracking()
            .FirstOrDefaultAsync(Match(organization, project, repositoryId, pullRequestId), ct);
        return row is null ? new(false, false, false, false, false, false, null) : ToState(row);
    }

    public async Task<IReadOnlyList<ChecklistProgress>> GetProgressAsync(
        string project, string repositoryId, CancellationToken ct)
    {
        var organization = ValidateLocation(project, repositoryId);
        // Summed in SQL, one query for the whole repository, the way the list view needs it.
        return await context.Checklists
            .AsNoTracking()
            .Where(row => row.Organization == organization && row.Project == project &&
                          row.RepositoryId == repositoryId)
            .Select(row => new ChecklistProgress(
                row.PullRequestId,
                (row.AiReview ? 1 : 0) + (row.Quality ? 1 : 0) + (row.Understand ? 1 : 0) +
                (row.Architecture ? 1 : 0) + (row.Debug ? 1 : 0) + (row.Ready ? 1 : 0)))
            .ToListAsync(ct);
    }

    public async Task<ChecklistState> SetAsync(
        string project, string repositoryId, int pullRequestId, string item, bool? completed,
        CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        if (completed is null) throw new ChecklistException("Specify completed as true or false.", 400);

        var row = await context.Checklists
            .FirstOrDefaultAsync(Match(organization, project, repositoryId, pullRequestId), ct);
        if (row is null)
        {
            row = new PrChecklistRow
            {
                Organization = organization,
                Project = project,
                RepositoryId = repositoryId,
                PullRequestId = pullRequestId,
            };
            context.Checklists.Add(row);
        }

        // A closed set of names, so an unknown item can never reach the database.
        switch (item.ToLowerInvariant())
        {
            case "ai-review": row.AiReview = completed.Value; break;
            case "quality": row.Quality = completed.Value; break;
            case "understand": row.Understand = completed.Value; break;
            case "architecture": row.Architecture = completed.Value; break;
            case "debug": row.Debug = completed.Value; break;
            case "ready": row.Ready = completed.Value; break;
            default: throw new ChecklistException("Unknown checklist item.", 400);
        }

        row.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        return ToState(row);
    }

    private static ChecklistState ToState(PrChecklistRow row) => new(
        row.AiReview, row.Quality, row.Understand, row.Architecture, row.Debug, row.Ready, row.UpdatedAt);

    private static System.Linq.Expressions.Expression<Func<PrChecklistRow, bool>> Match(
        string organization, string project, string repositoryId, int pullRequestId) =>
        row => row.Organization == organization && row.Project == project &&
               row.RepositoryId == repositoryId && row.PullRequestId == pullRequestId;

    private string ValidateKey(string project, string repositoryId, int pullRequestId)
    {
        var organization = ValidateLocation(project, repositoryId);
        if (pullRequestId <= 0)
            throw new ChecklistException("Invalid pull request location.", 400);
        return organization;
    }

    private string ValidateLocation(string project, string repositoryId)
    {
        var organization = configuration["AzureDevOps:Organization"];
        if (string.IsNullOrWhiteSpace(organization))
            throw new ChecklistException("Configure AzureDevOps:Organization on the backend.", 503);
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(repositoryId))
            throw new ChecklistException("Invalid pull request location.", 400);
        return organization;
    }
}
