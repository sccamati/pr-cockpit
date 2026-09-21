using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PRCockpit.Application.Analysis;
using PRCockpit.Application.Ports;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.Review;
using PRCockpit.Infrastructure.Persistence.Entities;

namespace PRCockpit.Infrastructure.Persistence;

public sealed class FileQuestionStore(PrCockpitContext context, IConfiguration configuration)
    : IFileQuestionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<FileQuestionTurn>> GetAsync(
        string project, string repositoryId, int pullRequestId, string path, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId, path);
        var rows = await context.FileQuestions
            .AsNoTracking()
            .Where(entry =>
                entry.Organization == organization && entry.Project == project &&
                entry.RepositoryId == repositoryId && entry.PullRequestId == pullRequestId &&
                entry.FilePath == path)
            // By the identity column, not the clock: the table only ever appends, so insertion
            // order is chronological order, and it sorts on every provider.
            .OrderBy(entry => entry.Id)
            .ToListAsync(ct);

        var turns = new List<FileQuestionTurn>(rows.Count);
        foreach (var row in rows)
        {
            FileQuestionTurn? turn;
            try
            {
                // Re-validated on read like every other stored payload here: a corrupt row
                // must not reach the UI as if it were an answer.
                turn = JsonSerializer.Deserialize<FileQuestionTurn>(row.TurnJson, JsonOptions);
                if (turn is null || turn.Sentences is not { Count: >= 1 and <= 6 }) throw new JsonException();
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                throw new SummaryAnalysisException("Saved conversation is invalid.", 503);
            }
            // A turn from an older contract is skipped rather than a 503: one missing line in
            // a thread is not worth blocking the file on.
            if (turn.SchemaVersion == SummaryContract.SchemaVersion) turns.Add(turn);
        }
        return turns;
    }

    public async Task<FileQuestionTurn> SaveAsync(
        string project, string repositoryId, int pullRequestId, FileQuestionTurn turn, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId, turn.Path);
        // Appended, never updated: the previous turns are what makes the next answer work.
        context.FileQuestions.Add(new PrFileQuestionRow
        {
            Organization = organization,
            Project = project,
            RepositoryId = repositoryId,
            PullRequestId = pullRequestId,
            FilePath = turn.Path,
            AskedAt = turn.AskedAt,
            TurnJson = JsonSerializer.Serialize(turn, JsonOptions),
        });
        await context.SaveChangesAsync(ct);
        return turn;
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
