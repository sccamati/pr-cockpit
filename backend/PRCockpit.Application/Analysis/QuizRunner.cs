using PRCockpit.Application.Ports;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Application.Analysis;

/// <summary>
/// Validates the quiz and its grades before anything is stored or shown, with the same
/// suspicion as <see cref="SummaryRunner"/>: paths must be this pull request's, ids are ours.
/// </summary>
public static class QuizRunner
{
    public static readonly IReadOnlyList<string> Kinds =
        ["intent", "mechanism", "domain", "runtime", "design", "impact", "edge", "debug", "free"];

    // No upper bound on the number of questions, by the owner's decision: a change with three
    // design decisions earns three design questions. The adapter's output cap is the ceiling,
    // and rejecting a long quiz would throw away a paid run.
    public const int MinQuestions = 2;
    public const int MaxFilesPerQuestion = 5;
    public const int MaxExpectedPoints = 4;
    public const int MaxQuestionLength = 500;
    public const int MaxPointLength = 300;

    public static async Task<Quiz> RunComposeAsync(
        PrContext context, IReadOnlyList<string> focusFiles, IAiSummaryAnalyzer analyzer, CancellationToken ct)
    {
        var draft = await analyzer.ComposeQuizAsync(context, focusFiles, ct);
        if (draft.SchemaVersion != SummaryContract.SchemaVersion)
            throw Invalid($"schemaVersion {draft.SchemaVersion} instead of {SummaryContract.SchemaVersion}");
        var drafts = draft.Questions ?? [];
        if (drafts.Count < MinQuestions)
            throw Invalid($"{drafts.Count} questions instead of at least {MinQuestions}");

        var changed = context.ChangedFiles.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var questions = new List<QuizQuestion>(drafts.Count);
        for (var index = 0; index < drafts.Count; index++)
        {
            var number = index + 1;
            var question = drafts[index] ?? throw Invalid($"question {number} is empty");
            var kind = question.Kind?.Trim().ToLowerInvariant();
            if (kind is null || !Kinds.Contains(kind))
                throw Invalid($"question {number} has an unknown kind");
            var text = question.Question?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length > MaxQuestionLength)
                throw Invalid($"question {number} is blank or longer than {MaxQuestionLength} characters");
            var files = question.Files ?? [];
            if (files.Count is < 1 or > MaxFilesPerQuestion)
                throw Invalid($"question {number} points at {files.Count} files instead of 1 to {MaxFilesPerQuestion}");
            if (files.Any(path => path is null || !changed.Contains(path)))
                throw Invalid($"question {number} points at a file outside this pull request");
            if (files.Distinct(StringComparer.Ordinal).Count() != files.Count)
                throw Invalid($"question {number} repeats a file");
            var points = (question.ExpectedPoints ?? []).Select(point => point?.Trim() ?? "").ToArray();
            if (points.Length is < 1 or > MaxExpectedPoints)
                throw Invalid($"question {number} has {points.Length} expected points instead of 1 to {MaxExpectedPoints}");
            if (points.Any(point => point.Length is 0 or > MaxPointLength))
                throw Invalid($"question {number} has a blank expected point or one longer than {MaxPointLength} characters");
            questions.Add(new QuizQuestion($"q{number}", kind, text, [.. files], points));
        }
        // What the change is for and how it works are asked every time; the rest is the
        // model's call, because it depends on the pull request.
        if (!questions.Any(question => question.Kind == "intent"))
            throw Invalid("no intent question");
        if (!questions.Any(question => question.Kind == "mechanism"))
            throw Invalid("no mechanism question");

        return new Quiz(SummaryContract.SchemaVersion, context.PullRequest.HeadCommitSha,
            [.. questions.Select(question => new QuizItem(question, []))], DateTimeOffset.UtcNow);
    }

    public static async Task<QuizAttempt> RunGradeAsync(
        PrContext context, QuizQuestion question, string answer, IAiSummaryAnalyzer analyzer, CancellationToken ct)
    {
        var draft = await analyzer.GradeQuizAnswerAsync(context, question, answer, ct);
        if (draft.SchemaVersion != SummaryContract.SchemaVersion || draft.Correct is null ||
            draft.Sentences is not { Count: >= 1 and <= 4 } ||
            draft.Sentences.Any(sentence => string.IsNullOrWhiteSpace(sentence) || sentence.Length > 500))
            throw new SummaryAnalysisException("AI returned an invalid quiz grade.", 502);

        return new QuizAttempt(answer, draft.Correct.Value,
            [.. draft.Sentences.Select(sentence => sentence.Trim())], DateTimeOffset.UtcNow);
    }

    private static SummaryAnalysisException Invalid(string reason) =>
        new($"AI returned an invalid quiz: {reason}.", 502);
}
