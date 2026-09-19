using Microsoft.Extensions.Configuration;
using PRCockpit.Api.Checklists;

namespace PRCockpit.Api.Tests;

public sealed class ReviewProgressStoreTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"pr-cockpit-tests-{Guid.NewGuid():N}.db");

    private const string Sha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
    private const string OtherSha = "00112233445566778899aabbccddeeff00112233";

    private ReviewProgressStore Store() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureDevOps:Organization"] = "contoso",
            ["Checklist:DatabasePath"] = _databasePath,
        })
        .Build());

    private static FileReviewUpdate Mark(string path, bool reviewed = true, string? blobId = Sha) =>
        new(path, reviewed, blobId, Sha, 3);

    [Fact]
    public async Task RemembersAReviewedFileAcrossConnections()
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
        var store = Store();
        await store.SetFileAsync("proj", "repo", 7, Mark("/src/One.cs"), CancellationToken.None);

        var result = await store.SetFileAsync("proj", "repo", 7, Mark("/src/One.cs", reviewed: false), CancellationToken.None);

        Assert.Null(result.Entry);
        Assert.Equal(0, result.ReviewedCount);
        Assert.Empty((await store.GetAsync("proj", "repo", 7, CancellationToken.None)).Files);
    }

    [Fact]
    public async Task ReMarkingRestampsTheStoredIdentity()
    {
        var store = Store();
        await store.SetFileAsync("proj", "repo", 7, Mark("/src/One.cs"), CancellationToken.None);

        var result = await store.SetFileAsync("proj", "repo", 7,
            new FileReviewUpdate("/src/One.cs", true, OtherSha, OtherSha, 3), CancellationToken.None);

        Assert.Equal(OtherSha, result.Entry!.BlobId);
        Assert.Equal(1, result.ReviewedCount);
    }

    [Fact]
    public async Task KeepsStateSeparatePerPullRequestAndRepository()
    {
        var store = Store();
        await store.SetFileAsync("proj", "repo", 7, Mark("/src/One.cs"), CancellationToken.None);
        await store.SetFileAsync("proj", "other", 7, Mark("/src/One.cs"), CancellationToken.None);
        await store.SetFileAsync("proj", "repo", 8, Mark("/src/Two.cs"), CancellationToken.None);

        var progress = await store.GetProgressAsync("proj", "repo", CancellationToken.None);

        Assert.Equal(2, progress.Count);
        Assert.Equal(1, progress.Single(item => item.PullRequestId == 7).ReviewedCount);
        Assert.Equal(3, progress.Single(item => item.PullRequestId == 7).ChangedFilesCount);
    }

    [Fact]
    public async Task ReportsNoProgressForAPullRequestThatWasNeverRead()
    {
        var store = Store();
        await store.SetFileAsync("proj", "repo", 7, Mark("/src/One.cs"), CancellationToken.None);

        var progress = await store.GetProgressAsync("proj", "repo", CancellationToken.None);

        Assert.DoesNotContain(progress, item => item.PullRequestId == 99);
    }

    [Fact]
    public async Task StoresTheReadingPathInOrderAndReplacesItWholesale()
    {
        var store = Store();
        await store.SetReadingPathAsync("proj", "repo", 7, ["/b.cs", "/a.cs"], CancellationToken.None);
        await store.SetReadingPathAsync("proj", "repo", 7, ["/a.cs"], CancellationToken.None);

        var state = await store.GetAsync("proj", "repo", 7, CancellationToken.None);

        Assert.Equal(["/a.cs"], state.ReadingPath);
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

    [Theory]
    [InlineData(11)]  // over the ten-file limit
    public async Task RejectsAnOversizeReadingPath(int count)
    {
        var paths = Enumerable.Range(0, count).Select(index => $"/file{index}.cs").ToArray();

        var failure = await Assert.ThrowsAsync<ChecklistException>(() =>
            Store().SetReadingPathAsync("proj", "repo", 7, paths, CancellationToken.None));

        Assert.Equal(400, failure.StatusCode);
    }

    [Fact]
    public async Task RejectsADuplicateInTheReadingPath()
    {
        var failure = await Assert.ThrowsAsync<ChecklistException>(() =>
            Store().SetReadingPathAsync("proj", "repo", 7, ["/a.cs", "/a.cs"], CancellationToken.None));

        Assert.Equal(400, failure.StatusCode);
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
        Assert.Empty(state.ReadingPath);
        Assert.Null(state.UpdatedAt);
    }

    [Fact]
    public async Task StopsAtTheRowCapWithoutDisturbingExistingRows()
    {
        var store = Store();
        // The cap is 2000; filling it honestly keeps the test truthful about the real bound.
        for (var index = 0; index < 2000; index++)
            await store.SetFileAsync("proj", "repo", 7, Mark($"/file{index}.cs"), CancellationToken.None);

        var failure = await Assert.ThrowsAsync<ChecklistException>(() =>
            store.SetFileAsync("proj", "repo", 7, Mark("/one-too-many.cs"), CancellationToken.None));

        Assert.Equal(400, failure.StatusCode);
        // An existing file must still be updatable once the cap is reached.
        var again = await store.SetFileAsync("proj", "repo", 7, Mark("/file0.cs"), CancellationToken.None);
        Assert.Equal(2000, again.ReviewedCount);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_databasePath); } catch (IOException) { }
    }
}
