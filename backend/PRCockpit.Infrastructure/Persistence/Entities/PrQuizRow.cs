namespace PRCockpit.Infrastructure.Persistence.Entities;

/// <summary>
/// The quiz as one validated JSON document, plus the counts the pull request list needs —
/// columns, so the list never parses a quiz to say "2 / 4".
/// </summary>
public sealed class PrQuizRow : PullRequestScopedRow
{
    public string? HeadCommitSha { get; set; }
    public string QuizJson { get; set; } = "";
    public int Correct { get; set; }
    public int Total { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
