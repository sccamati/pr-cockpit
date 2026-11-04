using PRCockpit.Application.Ports;
using PRCockpit.Application.PullRequests;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;
using PRCockpit.Domain.Review;

namespace PRCockpit.Application.Analysis;

/// <summary>
/// The quiz that closes a review. Composed once per head — the browser starts it in the
/// background when a walkthrough starts, and a second call for the same head costs nothing —
/// and graded one answer at a time. The answer key never leaves this service before the
/// question is passed.
/// </summary>
public sealed class QuizService(
    IAzureDevOpsClient client,
    PullRequestContextService contexts,
    IAiSummaryAnalyzer analyzer,
    IQuizStore store,
    IReviewProgressStore progress,
    ISummaryStore summaries)
{
    public async Task<QuizView?> GetAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct) =>
        (await store.GetAsync(project, repositoryId, pullRequestId, ct))?.ToView();

    public async Task<QuizView> ComposeAsync(
        string project, string repositoryId, int pullRequestId, bool regenerate, CancellationToken ct)
    {
        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        var stored = await store.GetAsync(project, repositoryId, pullRequestId, ct);
        if (!regenerate && stored is not null && IsCurrent(stored, details))
            return stored.ToView();

        // The reading path is what the reviewer chose to read: the model asks mostly about it,
        // and it gets the context budget first, so a big pull request cannot push it out.
        var reviews = await progress.GetAsync(project, repositoryId, pullRequestId, ct);
        var changed = details.ChangedFiles.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var focus = reviews.ReadingPath.Paths.Where(changed.Contains).ToArray();
        var summary = await summaries.GetAsync(project, repositoryId, pullRequestId, ct);
        var context = await contexts.BuildAsync(project, repositoryId, details, ContextBudget.Default, ct,
            readFirst: focus.ToHashSet(StringComparer.Ordinal)) with
        {
            Summary = SummaryDigest.Current(summary, details.HeadCommitSha)
        };
        var quiz = await QuizRunner.RunComposeAsync(context, focus, analyzer, ct);
        return (await store.SaveAsync(project, repositoryId, pullRequestId, quiz, ct)).ToView();
    }

    public async Task<QuizView> AnswerAsync(
        string project, string repositoryId, int pullRequestId, string? questionId, string? answer, CancellationToken ct)
    {
        // Checked before any I/O: a blank or oversized answer must not cost a model run.
        var text = (answer ?? "").Trim();
        if (text.Length == 0)
            throw new ReviewRequestException("Write an answer first.", 400);
        if (text.Length > QuizAnswerRequest.MaxAnswerLength)
            throw new ReviewRequestException(
                $"The answer may be at most {QuizAnswerRequest.MaxAnswerLength} characters.", 400);

        var stored = await store.GetAsync(project, repositoryId, pullRequestId, ct)
            ?? throw new ReviewRequestException("This pull request has no quiz yet.", 404);
        var item = stored.Items.FirstOrDefault(entry => entry.Question.Id == questionId)
            ?? throw new ReviewRequestException("No such question in this quiz.", 404);
        if (item.Passed) return stored.ToView();

        // A question about code that has since changed would be graded against files that no
        // longer say what it asks about — so after a push the quiz is recomposed, not answered.
        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        if (!IsCurrent(stored, details))
            throw new ReviewRequestException("The quiz was made for an older commit. Compose a new one.", 409);

        var context = await contexts.BuildAsync(project, repositoryId, details, ContextBudget.OneFile, ct,
            item.Question.Files);
        var attempt = await QuizRunner.RunGradeAsync(context, item.Question, text, analyzer, ct);
        var updated = stored.WithAttempt(item.Question.Id, attempt);
        return (await store.SaveAsync(project, repositoryId, pullRequestId, updated, ct)).ToView();
    }

    private static bool IsCurrent(Quiz quiz, PullRequestDetails details) =>
        ContentFreshness.IsCurrent(null, null, quiz.HeadCommitSha, details.HeadCommitSha);
}
