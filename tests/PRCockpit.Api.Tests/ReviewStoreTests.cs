using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.Review;
using PRCockpit.Infrastructure.Persistence;

namespace PRCockpit.Api.Tests;

/// <summary>
/// Same SQLite-in-memory harness as ReviewProgressStoreTests: no database on the machine,
/// and what is pinned here is the stores' own behaviour, not the SQL Server schema.
/// </summary>
public sealed class ReviewStoreTests : IDisposable
{
    private const string Sha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
    private const string OtherSha = "00112233445566778899aabbccddeeff00112233";
    private const string Blob = "1111111111111111111111111111111111111111";
    private const string OtherBlob = "2222222222222222222222222222222222222222";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PrCockpitContext> _options;
    private readonly List<PrCockpitContext> _contexts = [];

    public ReviewStoreTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<PrCockpitContext>().UseSqlite(_connection).Options;
        using var context = new PrCockpitContext(_options);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task ReadsBackAnExplanationOnlyForTheContentItWasGeneratedFrom()
    {
        var result = new FileExplanation(2, "/src/file.cs", Sha, ["Serwis wysyłki faktur."]);
        await Explanations().SaveAsync("proj", "repo", 7, result, Blob, CancellationToken.None);

        var same = await Explanations().GetAsync("proj", "repo", 7, "/src/file.cs", Blob, Sha, CancellationToken.None);
        var edited = await Explanations().GetAsync("proj", "repo", 7, "/src/file.cs", OtherBlob, OtherSha, CancellationToken.None);

        // Compared field by field: a record's list member compares by reference, not content.
        Assert.Equal(result.Path, same!.Result.Path);
        Assert.Equal(result.HeadCommitSha, same.Result.HeadCommitSha);
        Assert.Equal(result.Sentences, same.Result.Sentences);
        // The file changed under it, so the saved sentence is no longer an answer.
        Assert.Null(edited);
    }

    // US-P2: one commit used to throw away the explanations of every file in the pull
    // request, which is what makes the walkthrough's prefetch expensive.
    [Fact]
    public async Task ACommitTouchingAnotherFileLeavesThisExplanationAlone()
    {
        await Explanations().SaveAsync("proj", "repo", 7,
            new FileExplanation(2, "/src/file.cs", Sha, ["Serwis wysyłki faktur."]), Blob, CancellationToken.None);

        var stored = await Explanations().GetAsync("proj", "repo", 7, "/src/file.cs", Blob, OtherSha, CancellationToken.None);

        Assert.Equal("Serwis wysyłki faktur.", Assert.Single(stored!.Result.Sentences));
    }

    // The fallback, and the one the reviewed marker uses in the same situation.
    [Fact]
    public async Task WithoutABlobIdTheHeadCommitDecides()
    {
        await Explanations().SaveAsync("proj", "repo", 7,
            new FileExplanation(2, "/src/file.cs", Sha, ["Serwis wysyłki faktur."]), null, CancellationToken.None);

        Assert.NotNull(await Explanations().GetAsync("proj", "repo", 7, "/src/file.cs", null, Sha, CancellationToken.None));
        Assert.Null(await Explanations().GetAsync("proj", "repo", 7, "/src/file.cs", null, OtherSha, CancellationToken.None));
    }

    [Fact]
    public async Task OverwritesTheExplanationOfTheSameFileInsteadOfPilingUpRows()
    {
        await Explanations().SaveAsync("proj", "repo", 7,
            new FileExplanation(2, "/src/file.cs", Sha, ["Stare."]), Blob, CancellationToken.None);
        await Explanations().SaveAsync("proj", "repo", 7,
            new FileExplanation(2, "/src/file.cs", OtherSha, ["Nowe."]), OtherBlob, CancellationToken.None);

        using var context = new PrCockpitContext(_options);
        Assert.Equal(1, await context.FileExplanations.CountAsync());
        var stored = await Explanations().GetAsync("proj", "repo", 7, "/src/file.cs", OtherBlob, OtherSha, CancellationToken.None);
        Assert.Equal("Nowe.", Assert.Single(stored!.Result.Sentences));
    }

    // The contrast with the test above: this store appends, because the history is the
    // feature. A re-ask is another row, and the thread reads back in the order it was asked.
    [Fact]
    public async Task AppendsEveryTurnAndReadsThemOldestFirst()
    {
        var asked = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
        foreach (var (question, minute) in new[] { ("Pierwsze?", 0), ("Drugie?", 1), ("Trzecie?", 2) })
            await Questions().SaveAsync("proj", "repo", 7,
                new FileQuestionTurn(2, "/src/file.cs", question, null, ["Odpowiedź."], Blob, Sha,
                    asked.AddMinutes(minute)), CancellationToken.None);

        using var context = new PrCockpitContext(_options);
        Assert.Equal(3, await context.FileQuestions.CountAsync());
        var thread = await Questions().GetAsync("proj", "repo", 7, "/src/file.cs", CancellationToken.None);
        Assert.Equal(["Pierwsze?", "Drugie?", "Trzecie?"], thread.Select(turn => turn.Question));
    }

    [Fact]
    public async Task ReadsBackAQuizAndKeepsOneRowPerPullRequest()
    {
        await Quizzes().SaveAsync("proj", "repo", 7, Quiz(Sha), CancellationToken.None);
        var answered = Quiz(OtherSha).WithAttempt("q1",
            new QuizAttempt("Bo tak.", true, ["Dobrze."], DateTimeOffset.UtcNow));
        await Quizzes().SaveAsync("proj", "repo", 7, answered, CancellationToken.None);

        using var context = new PrCockpitContext(_options);
        Assert.Equal(1, await context.Quizzes.CountAsync());
        var stored = await Quizzes().GetAsync("proj", "repo", 7, CancellationToken.None);
        Assert.Equal(OtherSha, stored!.HeadCommitSha);
        Assert.True(stored.Items[0].Passed);
        Assert.Equal(["Po co ta zmiana?", "Jak działa?"], stored.Items.Select(item => item.Question.Question));
    }

    // The pull request list reads two columns, not the quiz itself.
    [Fact]
    public async Task ReportsProgressPerPullRequestOfOneRepository()
    {
        await Quizzes().SaveAsync("proj", "repo", 7, Quiz(Sha).WithAttempt("q2",
            new QuizAttempt("Przez kolejkę.", true, ["Tak."], DateTimeOffset.UtcNow)), CancellationToken.None);
        await Quizzes().SaveAsync("proj", "repo", 8, Quiz(Sha), CancellationToken.None);
        await Quizzes().SaveAsync("proj", "other", 9, Quiz(Sha), CancellationToken.None);

        var progress = await Quizzes().GetProgressAsync("proj", "repo", CancellationToken.None);

        Assert.Equal([new QuizProgress(7, 1, 2), new QuizProgress(8, 0, 2)],
            progress.OrderBy(entry => entry.PullRequestId));
    }

    // Saved under an older contract: out of date, not an error that blocks the pull request.
    [Fact]
    public async Task TreatsAQuizOfAnotherSchemaVersionAsNoQuiz()
    {
        await Quizzes().SaveAsync("proj", "repo", 7, Quiz(Sha) with { SchemaVersion = 1 }, CancellationToken.None);

        Assert.Null(await Quizzes().GetAsync("proj", "repo", 7, CancellationToken.None));
    }

    private static Quiz Quiz(string head) => new(2, head,
    [
        new QuizItem(new QuizQuestion("q1", "intent", "Po co ta zmiana?", ["/src/file.cs"], ["Wysyłka faktur."]), []),
        new QuizItem(new QuizQuestion("q2", "mechanism", "Jak działa?", ["/src/file.cs"], ["Przez kolejkę."]), []),
    ], DateTimeOffset.UtcNow);

    private FileExplanationStore Explanations() => new(Context(), Configuration());
    private FileQuestionStore Questions() => new(Context(), Configuration());
    private QuizStore Quizzes() => new(Context(), Configuration());

    private PrCockpitContext Context()
    {
        var context = new PrCockpitContext(_options);
        _contexts.Add(context);
        return context;
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureDevOps:Organization"] = "contoso" })
        .Build();

    public void Dispose()
    {
        foreach (var context in _contexts) context.Dispose();
        _connection.Dispose();
    }
}
