using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PRCockpit.Domain.Review;
using PRCockpit.Infrastructure.Persistence;

namespace PRCockpit.Api.Tests;

/// <summary>
/// Runs against SQLite in memory so `dotnet test` needs no database on the machine. The
/// application itself runs on SQL Server; the schema those migrations produce is verified
/// by applying them, not here. What these tests pin is the store's own behaviour.
/// </summary>
public sealed class ReviewProgressStoreTests : IDisposable
{
    private const string Sha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
    private const string OtherSha = "00112233445566778899aabbccddeeff00112233";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PrCockpitContext> _options;
    private readonly List<PrCockpitContext> _contexts = [];

    public ReviewProgressStoreTests()
    {
        // The in-memory database lives as long as this connection does.
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<PrCockpitContext>().UseSqlite(_connection).Options;
        using var context = new PrCockpitContext(_options);
        context.Database.EnsureCreated();
    }

    // A fresh context per call, the way a scoped one behaves per request.
    private ReviewProgressStore Store()
    {
        var context = new PrCockpitContext(_options);
        _contexts.Add(context);
        return new ReviewProgressStore(context, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureDevOps:Organization"] = "contoso",
            })
            .Build());
    }

    private static FileReviewUpdate Mark(string path, bool reviewed = true, string? blobId = Sha) =>
        new(path, reviewed, blobId, Sha, 3);

    [Fact]
    public async Task RemembersAReviewedFileAcrossContexts()
    {
        await Store().SetFileAsync("proj", "repo", 7, Mark("/src/One.cs"), CancellationToken.None);

        var state = await Store().GetAsync("proj", "repo", 7, CancellationToken.None);

        var entry = Assert.Single(state.Files);
        Assert.Equal("/src/One.cs", entry.Path);
        Assert.Equal(Sha, entry.BlobId);
        Assert.Equal(Sha, entry.HeadSha);
    }

    [Fact]
    public async Task UnmarkingRemovesTheRowRatherThanStoringAZero()
    {
        await Store().SetFileAsync("proj", "repo", 7, Mark("/src/One.cs"), CancellationToken.None);

        var result = await Store().SetFileAsync("proj", "repo", 7,
            Mark("/src/One.cs", reviewed: false), CancellationToken.None);

        Assert.Null(result.Entry);
        Assert.Equal(0, result.ReviewedCount);
        Assert.Empty((await Store().GetAsync("proj", "repo", 7, CancellationToken.None)).Files);
    }

    [Fact]
    public async Task ReMarkingRestampsTheStoredIdentity()
    {
        await Store().SetFileAsync("proj", "repo", 7, Mark("/src/One.cs"), CancellationToken.None);

        var result = await Store().SetFileAsync("proj", "repo", 7,
            new FileReviewUpdate("/src/One.cs", true, OtherSha, OtherSha, 3), CancellationToken.None);

        Assert.Equal(OtherSha, result.Entry!.BlobId);
        Assert.Equal(1, result.ReviewedCount);
    }

    [Fact]
    public async Task KeepsStateSeparatePerPullRequestAndRepository()
    {
        await Store().SetFileAsync("proj", "repo", 7, Mark("/src/One.cs"), CancellationToken.None);
        await Store().SetFileAsync("proj", "other", 7, Mark("/src/One.cs"), CancellationToken.None);
        await Store().SetFileAsync("proj", "repo", 8, Mark("/src/Two.cs"), CancellationToken.None);

        var progress = await Store().GetProgressAsync("proj", "repo", CancellationToken.None);

        Assert.Equal(2, progress.Count);
        Assert.Equal(1, progress.Single(item => item.PullRequestId == 7).ReviewedCount);
        Assert.Equal(3, progress.Single(item => item.PullRequestId == 7).ChangedFilesCount);
    }

    [Fact]
    public async Task ReportsNoProgressForAPullRequestThatWasNeverRead()
    {
        await Store().SetFileAsync("proj", "repo", 7, Mark("/src/One.cs"), CancellationToken.None);

        var progress = await Store().GetProgressAsync("proj", "repo", CancellationToken.None);

        Assert.DoesNotContain(progress, item => item.PullRequestId == 99);
    }

    [Fact]
    public async Task StoresTheReadingPathInOrderAndReplacesItWholesale()
    {
        await Store().SetReadingPathAsync("proj", "repo", 7, new ReadingPathUpdate(["/b.cs", "/a.cs"]), CancellationToken.None);
        await Store().SetReadingPathAsync("proj", "repo", 7, new ReadingPathUpdate(["/a.cs"]), CancellationToken.None);

        var state = await Store().GetAsync("proj", "repo", 7, CancellationToken.None);

        Assert.Equal(["/a.cs"], state.ReadingPath.Paths);
    }

    [Fact]
    public async Task KeepsTheReadingPathOrderExactly()
    {
        await Store().SetReadingPathAsync("proj", "repo", 7, new ReadingPathUpdate(["/c.cs", "/a.cs", "/b.cs"]), CancellationToken.None);

        var state = await Store().GetAsync("proj", "repo", 7, CancellationToken.None);

        Assert.Equal(["/c.cs", "/a.cs", "/b.cs"], state.ReadingPath.Paths);
    }

    [Theory]
    [InlineData("")]
    [InlineData("src/One.cs")]      // must start with a slash
    [InlineData("/src/\0One.cs")]   // no NUL
    public async Task RejectsAMalformedPath(string path)
    {
        var failure = await Assert.ThrowsAsync<ChecklistException>(() =>
            Store().SetFileAsync("proj", "repo", 7, Mark(path), CancellationToken.None));

        Assert.Equal(400, failure.StatusCode);
    }

    [Fact]
    public async Task RejectsABlobIdThatIsNotACommitSha()
    {
        var failure = await Assert.ThrowsAsync<ChecklistException>(() =>
            Store().SetFileAsync("proj", "repo", 7,
                new FileReviewUpdate("/src/One.cs", true, "not-a-sha", Sha, 3), CancellationToken.None));

        Assert.Equal(400, failure.StatusCode);
    }

    [Fact]
    public async Task RejectsAnUnspecifiedReviewedFlag()
    {
        var failure = await Assert.ThrowsAsync<ChecklistException>(() =>
            Store().SetFileAsync("proj", "repo", 7,
                new FileReviewUpdate("/src/One.cs", null, null, null, 3), CancellationToken.None));

        Assert.Equal(400, failure.StatusCode);
    }

    [Fact]
    public async Task RejectsAnOversizeReadingPath()
    {
        var paths = Enumerable.Range(0, 11).Select(index => $"/file{index}.cs").ToArray();

        var failure = await Assert.ThrowsAsync<ChecklistException>(() =>
            Store().SetReadingPathAsync("proj", "repo", 7, new ReadingPathUpdate(paths), CancellationToken.None));

        Assert.Equal(400, failure.StatusCode);
    }

    [Fact]
    public async Task RejectsADuplicateInTheReadingPath()
    {
        var failure = await Assert.ThrowsAsync<ChecklistException>(() =>
            Store().SetReadingPathAsync("proj", "repo", 7, new ReadingPathUpdate(["/a.cs", "/a.cs"]), CancellationToken.None));

        Assert.Equal(400, failure.StatusCode);
    }

    // US-P7: an interrupted walkthrough waits where it stopped.
    [Fact]
    public async Task KeepsThePositionAndTheHeadTheWalkthroughStartedFrom()
    {
        await Store().SetReadingPathAsync("proj", "repo", 7,
            new ReadingPathUpdate(["/a.cs", "/b.cs", "/c.cs"], 2, Sha), CancellationToken.None);

        var state = (await Store().GetAsync("proj", "repo", 7, CancellationToken.None)).ReadingPath;

        Assert.Equal(2, state.Position);
        Assert.Equal(Sha, state.HeadCommitSha);
        Assert.NotNull(state.UpdatedAt);
    }

    [Fact]
    public async Task APositionPastTheEndOfThePathIsRefused()
    {
        var failure = await Assert.ThrowsAsync<ChecklistException>(() =>
            Store().SetReadingPathAsync("proj", "repo", 7,
                new ReadingPathUpdate(["/a.cs"], 2), CancellationToken.None));

        Assert.Equal(400, failure.StatusCode);
    }

    // Position == length is the walkthrough that reached its end, and nothing to resume.
    [Fact]
    public async Task APositionAtTheEndOfThePathIsAllowed()
    {
        await Store().SetReadingPathAsync("proj", "repo", 7,
            new ReadingPathUpdate(["/a.cs"], 1), CancellationToken.None);

        Assert.Equal(1, (await Store().GetAsync("proj", "repo", 7, CancellationToken.None)).ReadingPath.Position);
    }

    // A shorter path saved over a longer one would otherwise resume nowhere.
    [Fact]
    public async Task APositionIsClampedToTheStoredPathOnRead()
    {
        await Store().SetReadingPathAsync("proj", "repo", 7,
            new ReadingPathUpdate(["/a.cs", "/b.cs", "/c.cs"], 3), CancellationToken.None);
        await Store().SetReadingPathAsync("proj", "repo", 7,
            new ReadingPathUpdate(["/a.cs"]), CancellationToken.None);

        Assert.Equal(0, (await Store().GetAsync("proj", "repo", 7, CancellationToken.None)).ReadingPath.Position);
    }

    [Fact]
    public async Task RejectsAnInvalidPullRequestId()
    {
        var failure = await Assert.ThrowsAsync<ChecklistException>(() =>
            Store().GetAsync("proj", "repo", 0, CancellationToken.None));

        Assert.Equal(400, failure.StatusCode);
    }

    [Fact]
    public async Task ReturnsAnEmptyStateForAPullRequestWithNoRows()
    {
        var state = await Store().GetAsync("proj", "repo", 123, CancellationToken.None);

        Assert.Empty(state.Files);
        Assert.Empty(state.ReadingPath.Paths);
        Assert.Null(state.UpdatedAt);
    }

    [Fact]
    public async Task StopsAtTheRowCapWithoutBlockingAnExistingFile()
    {
        var store = Store();
        // The cap is 2000; filling it honestly keeps the test truthful about the real bound.
        for (var index = 0; index < 2000; index++)
            await store.SetFileAsync("proj", "repo", 7, Mark($"/file{index}.cs"), CancellationToken.None);

        var failure = await Assert.ThrowsAsync<ChecklistException>(() =>
            store.SetFileAsync("proj", "repo", 7, Mark("/one-too-many.cs"), CancellationToken.None));
        Assert.Equal(400, failure.StatusCode);

        var again = await store.SetFileAsync("proj", "repo", 7, Mark("/file0.cs"), CancellationToken.None);
        Assert.Equal(2000, again.ReviewedCount);
    }

    public void Dispose()
    {
        foreach (var context in _contexts) context.Dispose();
        _connection.Dispose();
    }
}
