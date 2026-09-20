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
public sealed class ExplanationAndDebugNoteStoreTests : IDisposable
{
    private const string Sha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
    private const string OtherSha = "00112233445566778899aabbccddeeff00112233";
    private const string Blob = "1111111111111111111111111111111111111111";
    private const string OtherBlob = "2222222222222222222222222222222222222222";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PrCockpitContext> _options;
    private readonly List<PrCockpitContext> _contexts = [];

    public ExplanationAndDebugNoteStoreTests()
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

    [Fact]
    public async Task KeepsTheDebugAnswerAndClearsItOnBlank()
    {
        var saved = await Checklists().SetDebugNoteAsync("proj", "repo", 7, "  Zacząłbym od kolejki.  ", CancellationToken.None);
        Assert.Equal("Zacząłbym od kolejki.", saved.DebugNote);
        Assert.Equal("Zacząłbym od kolejki.",
            (await Checklists().GetAsync("proj", "repo", 7, CancellationToken.None)).DebugNote);

        var cleared = await Checklists().SetDebugNoteAsync("proj", "repo", 7, "   ", CancellationToken.None);
        Assert.Null(cleared.DebugNote);
    }

    // Saving an answer is not the same as declaring the step done — the six boxes stay manual.
    [Fact]
    public async Task DoesNotTickTheDebugStep()
    {
        var saved = await Checklists().SetDebugNoteAsync("proj", "repo", 7, "Od logów.", CancellationToken.None);

        Assert.False(saved.Debug);
    }

    [Fact]
    public async Task RejectsAnAnswerOverTheLengthLimit()
    {
        var error = await Assert.ThrowsAsync<ChecklistException>(() => Checklists()
            .SetDebugNoteAsync("proj", "repo", 7, new string('x', DebugNoteUpdate.MaxLength + 1), CancellationToken.None));

        Assert.Equal(400, error.StatusCode);
    }

    private FileExplanationStore Explanations() => new(Context(), Configuration());
    private ChecklistStore Checklists() => new(Context(), Configuration());

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
