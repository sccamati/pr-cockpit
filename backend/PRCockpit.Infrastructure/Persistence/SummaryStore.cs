using PRCockpit.Application.Analysis;
using PRCockpit.Application.Ports;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PRCockpit.Domain.Review;
using PRCockpit.Infrastructure.Persistence;
using PRCockpit.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Configuration;
using PRCockpit.Domain.Analysis;

namespace PRCockpit.Infrastructure.Persistence;

public sealed class SummaryStore(PrCockpitContext context, IConfiguration configuration) : ISummaryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StoredSummary?> GetAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        var row = await context.Summaries
            .AsNoTracking()
            .FirstOrDefaultAsync(entry =>
                entry.Organization == organization && entry.Project == project &&
                entry.RepositoryId == repositoryId && entry.PullRequestId == pullRequestId, ct);
        if (row is null) return null;

        try
        {
            // Re-validated on read: a corrupt row must not reach the UI as if it were a summary.
            var result = JsonSerializer.Deserialize<SummaryResponse>(row.ResponseJson, JsonOptions);
            if (result is null || result.Sentences is not { Count: >= 2 and <= 5 })
                throw new JsonException();
            // A row written against an older contract is not an error, it is simply out of
            // date: report "no summary" so the UI offers to generate one, instead of a 503
            // that would block the whole pull request on a stale row.
            if (result.SchemaVersion != SummaryContract.SchemaVersion) return null;
            return new StoredSummary(result, row.SavedAt);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new SummaryAnalysisException("Saved Summary is invalid.", 503);
        }
    }

    public async Task<StoredSummary> SaveAsync(
        string project, string repositoryId, int pullRequestId, SummaryResponse result, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        var savedAt = DateTimeOffset.UtcNow;

        var row = await context.Summaries.FirstOrDefaultAsync(entry =>
            entry.Organization == organization && entry.Project == project &&
            entry.RepositoryId == repositoryId && entry.PullRequestId == pullRequestId, ct);
        if (row is null)
        {
            row = new PrSummaryRow
            {
                Organization = organization,
                Project = project,
                RepositoryId = repositoryId,
                PullRequestId = pullRequestId,
            };
            context.Summaries.Add(row);
        }

        row.HeadCommitSha = result.HeadCommitSha;
        row.ResponseJson = JsonSerializer.Serialize(result, JsonOptions);
        row.SavedAt = savedAt;
        await context.SaveChangesAsync(ct);
        return new StoredSummary(result, savedAt);
    }

    private string ValidateKey(string project, string repositoryId, int pullRequestId)
    {
        var organization = configuration["AzureDevOps:Organization"];
        if (string.IsNullOrWhiteSpace(organization))
            throw new ChecklistException("Configure AzureDevOps:Organization on the backend.", 503);
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(repositoryId) || pullRequestId <= 0)
            throw new ChecklistException("Invalid pull request location.", 400);
        return organization;
    }
}
