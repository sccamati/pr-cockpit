using PRCockpit.Application.Analysis;
using PRCockpit.Application.Ports;
using PRCockpit.Application.PullRequests;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;
using PRCockpit.Domain.Review;

namespace PRCockpit.Api.Tests;

public class QuizTests
{
    private static readonly string Head = new('b', 40);
    private static readonly string OlderHead = new('c', 40);

    // ---- QuizRunner: what the model returns is checked before anything is stored ----

    // No upper bound, by the owner's decision: a change with many design decisions earns
    // a question about each. The ids are ours, whatever the model numbered.
    [Fact]
    public async Task AcceptsALongQuizAndNumbersTheQuestionsItself()
    {
        var questions = new[] { Question("intent"), Question("mechanism") }
            .Concat(Enumerable.Range(0, 10).Select(_ => Question("design"))).ToArray();
        var analyzer = new FakeAnalyzer { Quiz = new QuizDraft(2, questions) };

        var quiz = await QuizRunner.RunComposeAsync(Context(), [], analyzer, CancellationToken.None);

        Assert.Equal(12, quiz.Items.Count);
        Assert.Equal(Enumerable.Range(1, 12).Select(number => $"q{number}"), quiz.Items.Select(item => item.Question.Id));
        Assert.Equal(Head, quiz.HeadCommitSha);
        Assert.All(quiz.Items, item => Assert.Empty(item.Attempts));
    }

    public static TheoryData<QuizDraftQuestion?[], string> Broken => new()
    {
        { [Question("intent")], "1 questions instead of at least 2" },
        { [Question("intent"), Question("mechanism") with { Kind = "trivia" }], "question 2 has an unknown kind" },
        { [Question("intent"), Question("mechanism") with { Files = ["/src/Elsewhere.cs"] }], "outside this pull request" },
        { [Question("intent"), Question("mechanism") with { Files = [] }], "0 files instead of 1 to 5" },
        { [Question("intent"), Question("mechanism") with { ExpectedPoints = [] }], "0 expected points" },
        { [Question("intent"), Question("mechanism") with { Question = " " }], "question 2 is blank" },
        { [Question("intent"), Question("design")], "no mechanism question" },
        { [Question("mechanism"), Question("design")], "no intent question" },
        { [Question("intent"), null], "question 2 is empty" },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public async Task RejectsAQuizThatBreaksARuleAndSaysWhich(QuizDraftQuestion?[] questions, string reason)
    {
        var analyzer = new FakeAnalyzer { Quiz = new QuizDraft(2, questions) };

        var error = await Assert.ThrowsAsync<SummaryAnalysisException>(() =>
            QuizRunner.RunComposeAsync(Context(), [], analyzer, CancellationToken.None));

        Assert.Equal(502, error.StatusCode);
        Assert.Contains(reason, error.Message);
    }

    [Theory]
    [InlineData(null, new[] { "Dobrze." })]
    [InlineData(true, new string[0])]
    [InlineData(true, new[] { " " })]
    public async Task RejectsAnInvalidGrade(bool? correct, string[] sentences)
    {
        var analyzer = new FakeAnalyzer { Grade = new QuizGradeDraft(2, correct, sentences) };

        var error = await Assert.ThrowsAsync<SummaryAnalysisException>(() => QuizRunner.RunGradeAsync(
            Context(), Stored().Items[0].Question, "Odpowiedź.", analyzer, CancellationToken.None));

        Assert.Equal(502, error.StatusCode);
    }

    // ---- QuizService ----

    [Fact]
    public async Task ReturnsTheSavedQuizForTheCurrentHeadWithoutRunningTheModel()
    {
        var (service, analyzer, store, _) = Service(Stored());

        var view = await service.ComposeAsync("p", "r", 1, regenerate: false, CancellationToken.None);

        Assert.Equal(0, analyzer.Calls);
        Assert.Equal(2, view.Total);
        Assert.Null(store.Saved);
    }

    [Theory]
    [InlineData(true, null)]       // asked for a new one
    [InlineData(false, "older")]   // the pull request moved on
    public async Task ComposesANewQuizWhenAskedOrWhenTheHeadMoved(bool regenerate, string? older)
    {
        var (service, analyzer, store, _) = Service(Stored(older is null ? Head : OlderHead));

        await service.ComposeAsync("p", "r", 1, regenerate, CancellationToken.None);

        Assert.Equal(1, analyzer.Calls);
        Assert.Equal(Head, store.Saved!.HeadCommitSha);
    }

    // The reading path is what the model asks mostly about; a path that left the pull request
    // is not sent, and the model still sees the whole pull request.
    [Fact]
    public async Task SendsTheReadingPathAsFocusFilesNextToTheWholePullRequest()
    {
        var (service, analyzer, _, _) = Service(null, readingPath: ["/src/B.cs", "/src/Gone.cs"]);

        await service.ComposeAsync("p", "r", 1, regenerate: false, CancellationToken.None);

        Assert.Equal(["/src/B.cs"], analyzer.FocusFiles);
        Assert.Equal(["/src/A.cs", "/src/B.cs"], analyzer.Context!.ChangedFiles.Select(file => file.Path));
    }

    [Fact]
    public async Task GradesAgainstTheQuestionsFilesOnlyAndKeepsTheAttempt()
    {
        var (service, analyzer, store, _) = Service(Stored());
        analyzer.Grade = new QuizGradeDraft(2, false, ["Brakuje tego, co dzieje się przy błędzie — zajrzyj do B."]);

        var view = await service.AnswerAsync("p", "r", 1, "q2", "  Przez kolejkę.  ", CancellationToken.None);

        Assert.Equal(["/src/B.cs"], analyzer.Context!.ChangedFiles.Select(file => file.Path));
        Assert.Equal("Przez kolejkę.", analyzer.Answer);
        var question = view.Questions[1];
        Assert.False(question.Passed);
        Assert.Equal(1, question.AttemptCount);
        Assert.Equal("Przez kolejkę.", question.LastAttempt!.Answer);
        Assert.Single(store.Saved!.Items[1].Attempts);
    }

    // The answer key goes out from the start, so the reviewer can read it on a pull request not
    // worth digging into — but reading it passes nothing; only a graded answer does.
    [Fact]
    public async Task HandsOutTheExpectedPointsBeforeThePassWithoutPassing()
    {
        var (service, analyzer, _, _) = Service(Stored());

        var fresh = await service.GetAsync("p", "r", 1, CancellationToken.None);
        Assert.Equal(["Wysyłka faktur."], fresh!.Questions[0].ExpectedPoints);
        Assert.False(fresh.Questions[0].Passed);
        Assert.Equal(0, fresh.Correct);

        analyzer.Grade = new QuizGradeDraft(2, false, ["Nie."]);
        var wrong = await service.AnswerAsync("p", "r", 1, "q1", "Nie wiem.", CancellationToken.None);
        Assert.Equal(["Wysyłka faktur."], wrong.Questions[0].ExpectedPoints);
        Assert.False(wrong.Questions[0].Passed);

        analyzer.Grade = new QuizGradeDraft(2, true, ["Tak."]);
        var right = await service.AnswerAsync("p", "r", 1, "q1", "Wysyłka faktur.", CancellationToken.None);
        Assert.Equal(["Wysyłka faktur."], right.Questions[0].ExpectedPoints);
        Assert.Equal(1, right.Correct);
        Assert.False(right.Passed);
    }

    [Fact]
    public async Task APassedQuestionCostsNoModelRun()
    {
        var passed = Stored().WithAttempt("q1", new QuizAttempt("Tak.", true, ["Dobrze."], DateTimeOffset.UtcNow));
        var (service, analyzer, _, client) = Service(passed);

        var view = await service.AnswerAsync("p", "r", 1, "q1", "Jeszcze raz.", CancellationToken.None);

        Assert.Equal(0, analyzer.Calls);
        Assert.Equal(0, client.Calls);
        Assert.True(view.Questions[0].Passed);
    }

    [Fact]
    public async Task RefusesToGradeAQuizMadeForAnOlderCommit()
    {
        var (service, analyzer, _, _) = Service(Stored(OlderHead));

        var error = await Assert.ThrowsAsync<ReviewRequestException>(() =>
            service.AnswerAsync("p", "r", 1, "q1", "Odpowiedź.", CancellationToken.None));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal(0, analyzer.Calls);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RejectsABlankAnswerBeforeAnyIo(string? answer)
    {
        var (service, _, _, client) = Service(Stored());

        var error = await Assert.ThrowsAsync<ReviewRequestException>(() =>
            service.AnswerAsync("p", "r", 1, "q1", answer, CancellationToken.None));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task RejectsAnOversizedAnswerBeforeAnyIo()
    {
        var (service, _, _, client) = Service(Stored());

        await Assert.ThrowsAsync<ReviewRequestException>(() => service.AnswerAsync("p", "r", 1, "q1",
            new string('x', QuizAnswerRequest.MaxAnswerLength + 1), CancellationToken.None));

        Assert.Equal(0, client.Calls);
    }

    private static QuizDraftQuestion Question(string kind) =>
        new(kind, "Co tu się dzieje?", ["/src/A.cs"], ["Coś konkretnego."]);

    private static Quiz Stored(string? head = null) => new(2, head ?? Head,
    [
        new QuizItem(new QuizQuestion("q1", "intent", "Po co?", ["/src/A.cs"], ["Wysyłka faktur."]), []),
        new QuizItem(new QuizQuestion("q2", "mechanism", "Jak?", ["/src/B.cs"], ["Przez kolejkę."]), []),
    ], DateTimeOffset.UtcNow);

    private static PrContext Context() => new(
        new PrContextMetadata(1, "Change", null, "Author", "Repo", "feature", "main", "active",
            DateTimeOffset.UtcNow, [], [], new string('a', 40), Head),
        ["Change"],
        [new ContextChangedFile("/src/A.cs", "edit", null, "old", "new", null),
         new ContextChangedFile("/src/B.cs", "edit", null, "old", "new", null)],
        ContextBudget.Default, 12, false);

    private static (QuizService, FakeAnalyzer, FakeQuizStore, FakeClient) Service(
        Quiz? stored, IReadOnlyList<string>? readingPath = null)
    {
        var analyzer = new FakeAnalyzer
        {
            Quiz = new QuizDraft(2, [Question("intent"), Question("mechanism")]),
            Grade = new QuizGradeDraft(2, true, ["Dobrze."])
        };
        var store = new FakeQuizStore(stored);
        var client = new FakeClient();
        var service = new QuizService(client, new PullRequestContextService(client), analyzer, store,
            new FakeProgressStore(readingPath ?? []), new NoSummary());
        return (service, analyzer, store, client);
    }

    private sealed class FakeAnalyzer : IAiSummaryAnalyzer
    {
        public QuizDraft Quiz { get; set; } = new(2, []);
        public QuizGradeDraft Grade { get; set; } = new(2, true, ["Dobrze."]);
        public int Calls { get; private set; }
        public PrContext? Context { get; private set; }
        public IReadOnlyList<string>? FocusFiles { get; private set; }
        public string? Answer { get; private set; }

        public Task<QuizDraft> ComposeQuizAsync(PrContext context, IReadOnlyList<string> focusFiles, CancellationToken ct)
        {
            Calls++;
            (Context, FocusFiles) = (context, focusFiles);
            return Task.FromResult(Quiz);
        }

        public Task<QuizGradeDraft> GradeQuizAnswerAsync(
            PrContext context, QuizQuestion question, string answer, CancellationToken ct)
        {
            Calls++;
            (Context, Answer) = (context, answer);
            return Task.FromResult(Grade);
        }

        public Task<SummaryDraft> AnalyzeAsync(PrContext context, CancellationToken ct) => throw new NotSupportedException();
        public Task<SummaryDraft> ExplainFileAsync(PrContext context, CancellationToken ct) => throw new NotSupportedException();
        public Task<SummaryDraft> AskFileAsync(PrContext context, FileQuestion question, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeQuizStore(Quiz? stored) : IQuizStore
    {
        public Quiz? Saved { get; private set; }

        public Task<Quiz?> GetAsync(string project, string repositoryId, int pullRequestId, CancellationToken ct) =>
            Task.FromResult(Saved ?? stored);

        public Task<Quiz> SaveAsync(string project, string repositoryId, int pullRequestId, Quiz quiz, CancellationToken ct)
        {
            Saved = quiz;
            return Task.FromResult(quiz);
        }

        public Task<IReadOnlyList<QuizProgress>> GetProgressAsync(string project, string repositoryId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeProgressStore(IReadOnlyList<string> readingPath) : IReviewProgressStore
    {
        public Task<FileReviewState> GetAsync(string project, string repositoryId, int pullRequestId, CancellationToken ct) =>
            Task.FromResult(new FileReviewState([], new ReadingPathState(readingPath, 0, null, null), null));

        public Task<FileReviewResult> SetFileAsync(string project, string repositoryId, int pullRequestId,
            FileReviewUpdate update, CancellationToken ct) => throw new NotSupportedException();
        public Task<ReadingPathState> SetReadingPathAsync(string project, string repositoryId, int pullRequestId,
            ReadingPathUpdate update, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<FileReviewProgress>> GetProgressAsync(string project, string repositoryId,
            CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoSummary : ISummaryStore
    {
        public Task<StoredSummary?> GetAsync(string project, string repositoryId, int pullRequestId, CancellationToken ct) =>
            Task.FromResult<StoredSummary?>(null);

        public Task<StoredSummary> SaveAsync(string project, string repositoryId, int pullRequestId,
            SummaryResponse result, CancellationToken ct) => throw new NotSupportedException();
    }

    // Wide port, narrow fake: the two calls the quiz makes, everything else fails loudly.
    private sealed class FakeClient : IAzureDevOpsClient
    {
        public int Calls { get; private set; }

        public Task<PullRequestDetails> GetPullRequestAsync(
            string project, string repositoryId, int pullRequestId, CancellationToken ct)
        {
            Calls++;
            ChangedFile[] files = [new("/src/A.cs", "edit", null), new("/src/B.cs", "edit", null)];
            return Task.FromResult(new PullRequestDetails(1, "Change", null, "Author", "Repo", "feature", "main",
                "active", DateTimeOffset.UtcNow, [], files.Length, files, 0, [], [], new string('a', 40), Head));
        }

        public Task<FileDiff> GetFileDiffAsync(
            string project, string repositoryId, PullRequestDetails details, string path, CancellationToken ct) =>
            Task.FromResult(new FileDiff(path, null, "text", "old", "new"));

        public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Repository>> GetRepositoriesAsync(string project, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsAsync(string project, string repositoryId, CancellationToken ct) => throw new NotSupportedException();
        public Task<FileDiff> GetFileDiffAsync(string project, string repositoryId, int pullRequestId, string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PrCommentThread>> GetCommentThreadsAsync(string project, string repositoryId, int pullRequestId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrCommentThread> CreateCommentThreadAsync(string project, string repositoryId, int pullRequestId, NewCommentThread thread, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrCommentThread> ReplyToThreadAsync(string project, string repositoryId, int pullRequestId, int threadId, NewComment comment, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrCommentThread> UpdateCommentAsync(string project, string repositoryId, int pullRequestId, int threadId, int commentId, EditComment comment, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrCommentThread> DeleteCommentAsync(string project, string repositoryId, int pullRequestId, int threadId, int commentId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrCommentThread> SetThreadStatusAsync(string project, string repositoryId, int pullRequestId, int threadId, string? status, CancellationToken ct) => throw new NotSupportedException();
        public Task<Reviewer> SetMyVoteAsync(string project, string repositoryId, int pullRequestId, int vote, CancellationToken ct) => throw new NotSupportedException();
        public Task<FileDiff> GetFileDiffSinceIterationAsync(string project, string repositoryId, int pullRequestId, string path, int iterationId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetChangedPathsSinceIterationAsync(string project, string repositoryId, int pullRequestId, int iterationId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SourceSnapshot> GetSourceSnapshotAsync(string project, string repositoryId, string commitSha, CancellationToken ct) => throw new NotSupportedException();
    }
}
