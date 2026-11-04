namespace PRCockpit.Domain.Analysis;

/// <summary>One question as the model wrote it, before validation. Everything may be missing.</summary>
public record QuizDraftQuestion(
    string? Kind, string? Question, IReadOnlyList<string>? Files, IReadOnlyList<string>? ExpectedPoints);

/// <summary>What the quiz task returns, before validation.</summary>
public record QuizDraft(int SchemaVersion, IReadOnlyList<QuizDraftQuestion?>? Questions);

/// <summary>What the grading task returns, before validation.</summary>
public record QuizGradeDraft(int SchemaVersion, bool? Correct, IReadOnlyList<string>? Sentences);

/// <summary>
/// A validated question. The id is ours, never the model's; ExpectedPoints are what a right
/// answer has to say, and stay on the server until the question is passed.
/// </summary>
public record QuizQuestion(
    string Id, string Kind, string Question, IReadOnlyList<string> Files, IReadOnlyList<string> ExpectedPoints);

public record QuizAttempt(string Answer, bool Correct, IReadOnlyList<string> Feedback, DateTimeOffset AnsweredAt);

public record QuizItem(QuizQuestion Question, IReadOnlyList<QuizAttempt> Attempts)
{
    public bool Passed => Attempts.Any(attempt => attempt.Correct);
}

/// <summary>
/// The understanding check that closes a review: the pull request counts as done only when
/// every question is passed, and only for the head the quiz was made for. There is no skip —
/// the way out of a bad question is the grader, which takes the code over the expected points.
/// </summary>
public record Quiz(int SchemaVersion, string? HeadCommitSha, IReadOnlyList<QuizItem> Items, DateTimeOffset CreatedAt)
{
    public int Correct => Items.Count(item => item.Passed);

    public Quiz WithAttempt(string questionId, QuizAttempt attempt) => this with
    {
        Items = [.. Items.Select(item => item.Question.Id == questionId
            ? item with { Attempts = [.. item.Attempts, attempt] }
            : item)]
    };

    // The expected points are the answer key: shown once the question is passed, as the model
    // answer to compare with, never before — a wrong attempt gets the grader's hint instead.
    public QuizView ToView() => new(HeadCommitSha, CreatedAt, Correct, Items.Count, Correct == Items.Count,
        [.. Items.Select(item => new QuizQuestionView(
            item.Question.Id, item.Question.Kind, item.Question.Question, item.Question.Files,
            item.Passed ? item.Question.ExpectedPoints : null, item.Passed,
            item.Attempts.Count, item.Attempts.Count > 0 ? item.Attempts[^1] : null))]);
}

public record QuizQuestionView(
    string Id, string Kind, string Question, IReadOnlyList<string> Files,
    IReadOnlyList<string>? ExpectedPoints, bool Passed, int AttemptCount, QuizAttempt? LastAttempt);

public record QuizView(
    string? HeadCommitSha, DateTimeOffset CreatedAt, int Correct, int Total, bool Passed,
    IReadOnlyList<QuizQuestionView> Questions);

public record QuizProgress(int PullRequestId, int Correct, int Total);

public record QuizComposeRequest(bool? Regenerate);

public record QuizAnswerRequest(string? QuestionId, string? Answer)
{
    public const int MaxAnswerLength = 2_000;
}
