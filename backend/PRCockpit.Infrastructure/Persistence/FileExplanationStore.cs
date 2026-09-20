using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PRCockpit.Application.Analysis;
using PRCockpit.Application.Ports;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.Review;
using PRCockpit.Infrastructure.Persistence.Entities;

namespace PRCockpit.Infrastructure.Persistence;

public sealed class FileExplanationStore(PrCockpitContext context, IConfiguration configuration)
    : IFileExplanationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StoredFileExplanation?> GetAsync(
        string project, string repositoryId, int pullRequestId, string path, string? headCommitSha,
        CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId, path);
        var row = await context.FileExplanations
            .AsNoTracking()
            .FirstOrDefaultAsync(entry =>
                entry.Organization == organization && entry.Project == project &&
                entry.RepositoryId == repositoryId && entry.PullRequestId == pullRequestId &&
                entry.FilePath == path, ct);
        // A row from another head describes a file that has since changed, so it is not an
        // answer to the current question. Treat it as absent and let it be overwritten.
        if (row is null || row.HeadCommitSha != headCommitSha) return null;

        try
        {
            var result = JsonSerializer.Deserialize<FileExplanation>(row.ResponseJson, JsonOptions);
            if (result is null || result.Sentences is not { Count: >= 1 and <= 3 }) throw new JsonException();
            if (result.SchemaVersion != SummaryContract.SchemaVersion) return null;
            return new StoredFileExplanation(result, row.SavedAt);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new SummaryAnalysisException("Saved file explanation is invalid.", 503);
        }
    }

    public async Task<StoredFileExplanation> SaveAsync(
        string project, string repositoryId, int pullRequestId, FileExplanation result, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId, result.Path);
        var savedAt = DateTimeOffset.UtcNow;

        var row = await context.FileExplanations.FirstOrDefaultAsync(entry =>
            entry.Organization == organization && entry.Project == project &&
            entry.RepositoryId == repositoryId && entry.PullRequestId == pullRequestId &&
            entry.FilePath == result.Path, ct);
        if (row is null)
        {
            row = new PrFileExplanationRow
            {
                Organization = organization,
                Project = project,
                RepositoryId = repositoryId,
                PullRequestId = pullRequestId,
                FilePath = result.Path,
            };
            context.FileExplanations.Add(row);
        }

        row.HeadCommitSha = result.HeadCommitSha;
        row.ResponseJson = JsonSerializer.Serialize(result, JsonOptions);
        row.SavedAt = savedAt;
        await context.SaveChangesAsync(ct);
        return new StoredFileExplanation(result, savedAt);
    }

    private string ValidateKey(string project, string repositoryId, int pullRequestId, string path)
    {
        var organization = configuration["AzureDevOps:Organization"];
        if (string.IsNullOrWhiteSpace(organization))
            throw new ChecklistException("Configure AzureDevOps:Organization on the backend.", 503);
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(repositoryId) || pullRequestId <= 0)
            throw new ChecklistException("Invalid pull request location.", 400);
        if (string.IsNullOrWhiteSpace(path) || path.Length > PrCockpitContext.MaxPathLength)
            throw new ChecklistException("Invalid file path.", 400);
        return organization;
    }
}
