using PRCockpit.Application.Ports;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PRCockpit.Infrastructure.Persistence;
using PRCockpit.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Configuration;
using PRCockpit.Domain.Review;

namespace PRCockpit.Infrastructure.Persistence;

/// <summary>
/// Remembers which files of a pull request have been read, and the order the user wants to
/// read them in. Reuses <see cref="ChecklistException"/> so <c>Execute</c> in Program.cs
/// needs no extra arm.
/// </summary>
public sealed class ReviewProgressStore(PrCockpitContext context, IConfiguration configuration) : IReviewProgressStore
{
    // Mirrors the $top=2000 change page the Azure DevOps client already uses, so a pull
    // request that fits on screen always fits here too.
    private const int MaxFilesPerPullRequest = 2000;
    private const int MaxReadingPath = 10;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FileReviewState> GetAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);

        var files = await context.FileReviews
            .AsNoTracking()
            .Where(Scope(organization, project, repositoryId, pullRequestId))
            .Select(row => new FileReviewEntry(row.FilePath, row.ReviewedBlobId, row.ReviewedHeadSha, row.UpdatedAt))
            .ToListAsync(ct);

        var pathRow = await context.ReadingPaths
            .AsNoTracking()
            .FirstOrDefaultAsync(row =>
                row.Organization == organization && row.Project == project &&
                row.RepositoryId == repositoryId && row.PullRequestId == pullRequestId, ct);

        var readingPath = pathRow is null ? [] : ReadPaths(pathRow.PathsJson);
        DateTimeOffset? updatedAt = files.Count == 0 ? null : files.Max(file => file.UpdatedAt);
        if (pathRow is not null && (updatedAt is null || pathRow.UpdatedAt > updatedAt))
            updatedAt = pathRow.UpdatedAt;

        return new FileReviewState(files, readingPath, updatedAt);
    }

    public async Task<FileReviewResult> SetFileAsync(
        string project, string repositoryId, int pullRequestId, FileReviewUpdate update, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        ArgumentNullException.ThrowIfNull(update);
        if (update.Reviewed is null) throw new ChecklistException("Specify reviewed as true or false.", 400);
        var path = ValidatePath(update.Path);
        var blobId = ValidateSha(update.BlobId, "blob id");
        var headSha = ValidateSha(update.HeadCommitSha, "commit sha");
        if (update.ChangedFilesCount is < 0) throw new ChecklistException("Invalid changed file count.", 400);

        var scope = Scope(organization, project, repositoryId, pullRequestId);
        var row = await context.FileReviews.FirstOrDefaultAsync(
            entry => entry.Organization == organization && entry.Project == project &&
                     entry.RepositoryId == repositoryId && entry.PullRequestId == pullRequestId &&
                     entry.FilePath == path, ct);

        FileReviewEntry? entry2 = null;
        if (update.Reviewed.Value)
        {
            if (row is null)
            {
                // An existing row is always updatable, so re-marking a file can never be
                // blocked; only a genuinely new file can run into the cap.
                var count = await context.FileReviews.CountAsync(scope, ct);
                if (count >= MaxFilesPerPullRequest)
                    throw new ChecklistException("This pull request has too many reviewed files.", 400);
                row = new PrFileReviewRow
                {
                    Organization = organization,
                    Project = project,
                    RepositoryId = repositoryId,
                    PullRequestId = pullRequestId,
                    FilePath = path,
                };
                context.FileReviews.Add(row);
            }

            row.ReviewedBlobId = blobId;
            row.ReviewedHeadSha = headSha;
            row.ChangedFilesCount = update.ChangedFilesCount ?? 0;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(ct);
            entry2 = new FileReviewEntry(row.FilePath, row.ReviewedBlobId, row.ReviewedHeadSha, row.UpdatedAt);
        }
        else if (row is not null)
        {
            context.FileReviews.Remove(row);
            await context.SaveChangesAsync(ct);
        }

        return new FileReviewResult(entry2, await context.FileReviews.CountAsync(scope, ct));
    }

    public async Task<ReadingPathState> SetReadingPathAsync(
        string project, string repositoryId, int pullRequestId, IReadOnlyList<string>? paths, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        if (paths is null) throw new ChecklistException("Specify the reading path.", 400);
        if (paths.Count > MaxReadingPath)
            throw new ChecklistException($"The reading path holds at most {MaxReadingPath} files.", 400);
        var validated = paths.Select(ValidatePath).ToArray();
        if (validated.Distinct(StringComparer.Ordinal).Count() != validated.Length)
            throw new ChecklistException("The reading path contains a duplicate file.", 400);

        var row = await context.ReadingPaths.FirstOrDefaultAsync(entry =>
            entry.Organization == organization && entry.Project == project &&
            entry.RepositoryId == repositoryId && entry.PullRequestId == pullRequestId, ct);
        if (row is null)
        {
            row = new PrReadingPathRow
            {
                Organization = organization,
                Project = project,
                RepositoryId = repositoryId,
                PullRequestId = pullRequestId,
            };
            context.ReadingPaths.Add(row);
        }

        row.PathsJson = JsonSerializer.Serialize(validated, JsonOptions);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        return new ReadingPathState(validated, row.UpdatedAt);
    }

    public async Task<IReadOnlyList<FileReviewProgress>> GetProgressAsync(
        string project, string repositoryId, CancellationToken ct)
    {
        var organization = ValidateLocation(project, repositoryId);
        // One grouped query for the whole repository, the analogue of the checklist counter.
        return await context.FileReviews
            .AsNoTracking()
            .Where(row => row.Organization == organization && row.Project == project &&
                          row.RepositoryId == repositoryId)
            .GroupBy(row => row.PullRequestId)
            .Select(group => new FileReviewProgress(
                group.Key, group.Count(), group.Max(row => row.ChangedFilesCount)))
            .ToListAsync(ct);
    }

    private static System.Linq.Expressions.Expression<Func<PrFileReviewRow, bool>> Scope(
        string organization, string project, string repositoryId, int pullRequestId) =>
        row => row.Organization == organization && row.Project == project &&
               row.RepositoryId == repositoryId && row.PullRequestId == pullRequestId;

    private static IReadOnlyList<string> ReadPaths(string json)
    {
        try
        {
            // Re-validated on read, the way a stored Summary is: a corrupt row must not
            // reach the UI as if it were a real reading path.
            var paths = JsonSerializer.Deserialize<string[]>(json, JsonOptions);
            if (paths is null || paths.Length > MaxReadingPath ||
                paths.Any(path => string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') ||
                                  path.Length > PrCockpitContext.MaxPathLength) ||
                paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
                throw new JsonException();
            return paths;
        }
        catch (JsonException)
        {
            throw new ChecklistException("Saved reading path is invalid.", 503);
        }
    }

    private static string ValidatePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') ||
            path.Contains('\0') || path.Length > PrCockpitContext.MaxPathLength)
            throw new ChecklistException("Invalid file path.", 400);
        return path;
    }

    private static string? ValidateSha(string? value, string name)
    {
        if (value is null) return null;
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-fA-F0-9]{40}$"))
            throw new ChecklistException($"Invalid {name}.", 400);
        return value;
    }

    private string ValidateKey(string project, string repositoryId, int pullRequestId)
    {
        var organization = ValidateLocation(project, repositoryId);
        if (pullRequestId <= 0) throw new ChecklistException("Invalid pull request location.", 400);
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
