using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PRCockpit.Application.Analysis;
using PRCockpit.Application.Ports;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.Review;
using PRCockpit.Infrastructure.Persistence.Entities;

namespace PRCockpit.Infrastructure.Persistence;

// ponytail: read-modify-write of one row, last write wins. The UI grades one answer at a
// time, so two writes to one quiz only race across two open tabs.
public sealed class QuizStore(PrCockpitContext context, IConfiguration configuration) : IQuizStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Quiz?> GetAsync(string project, string repositoryId, int pullRequestId, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        var row = await context.Quizzes.AsNoTracking().FirstOrDefaultAsync(entry =>
            entry.Organization == organization && entry.Project == project &&
            entry.RepositoryId == repositoryId && entry.PullRequestId == pullRequestId, ct);
        if (row is null) return null;

        try
        {
            var quiz = JsonSerializer.Deserialize<Quiz>(row.QuizJson, JsonOptions);
            if (quiz?.Items is not { Count: > 0 }) throw new JsonException();
            // A quiz written against an older contract is out of date, not broken: "no quiz"
            // lets the UI offer a new one instead of blocking the pull request on a 503.
            return quiz.SchemaVersion == SummaryContract.SchemaVersion ? quiz : null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new SummaryAnalysisException("Saved quiz is invalid.", 503);
        }
    }

    public async Task<Quiz> SaveAsync(
        string project, string repositoryId, int pullRequestId, Quiz quiz, CancellationToken ct)
    {
        var organization = ValidateKey(project, repositoryId, pullRequestId);
        var row = await context.Quizzes.FirstOrDefaultAsync(entry =>
            entry.Organization == organization && entry.Project == project &&
            entry.RepositoryId == repositoryId && entry.PullRequestId == pullRequestId, ct);
        if (row is null)
        {
            row = new PrQuizRow
            {
                Organization = organization,
                Project = project,
                RepositoryId = repositoryId,
                PullRequestId = pullRequestId,
            };
            context.Quizzes.Add(row);
        }

        row.HeadCommitSha = quiz.HeadCommitSha;
        row.QuizJson = JsonSerializer.Serialize(quiz, JsonOptions);
        row.Correct = quiz.Correct;
        row.Total = quiz.Items.Count;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
        return quiz;
    }

    public async Task<IReadOnlyList<QuizProgress>> GetProgressAsync(
        string project, string repositoryId, CancellationToken ct)
    {
        var organization = configuration["AzureDevOps:Organization"];
        if (string.IsNullOrWhiteSpace(organization))
            throw new ReviewRequestException("Configure AzureDevOps:Organization on the backend.", 503);
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(repositoryId))
            throw new ReviewRequestException("Invalid repository location.", 400);

        return await context.Quizzes.AsNoTracking()
            .Where(entry => entry.Organization == organization && entry.Project == project &&
                entry.RepositoryId == repositoryId)
            .Select(entry => new QuizProgress(entry.PullRequestId, entry.Correct, entry.Total))
            .ToListAsync(ct);
    }

    private string ValidateKey(string project, string repositoryId, int pullRequestId)
    {
        var organization = configuration["AzureDevOps:Organization"];
        if (string.IsNullOrWhiteSpace(organization))
            throw new ReviewRequestException("Configure AzureDevOps:Organization on the backend.", 503);
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(repositoryId) || pullRequestId <= 0)
            throw new ReviewRequestException("Invalid pull request location.", 400);
        return organization;
    }
}
