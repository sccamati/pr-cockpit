using PRCockpit.Application.PullRequests;
using PRCockpit.Domain.PullRequests;
using PRCockpit.Infrastructure.AzureDevOps;

namespace PRCockpit.Api.Tests;

public class PrContextBuilderTests
{
    // The file tree groups on this value, so it has to agree with what the context builder
    // drops — one rule, two callers.
    [Theory]
    [InlineData("/src/Program.cs", null)]
    [InlineData("/package-lock.json", "lockFile")]
    [InlineData("/src/Generated.g.cs", "generated")]
    [InlineData("/tests/__snapshots__/view.txt", "snapshot")]
    [InlineData("/dist/app.js", "buildOutput")]
    [InlineData("/app.min.js", "minified")]
    public void ChangedFileExposesTheSameCategoryTheContextBuilderUses(string path, string? expected) =>
        Assert.Equal(expected, new ChangedFile(path, "edit", null).Category);

    [Fact]
    public async Task KeepsMetadataAndSkipsExcludedTypesWithoutFetchingTheirDiffs()
    {
        var details = Details(
            new ChangedFile("/src/Program.cs", "edit", null),
            new ChangedFile("/package-lock.json", "edit", null),
            new ChangedFile("/src/Generated.g.cs", "add", null),
            new ChangedFile("/tests/__snapshots__/view.txt", "edit", null),
            new ChangedFile("/dist/app.js", "add", null),
            new ChangedFile("/app.min.js", "edit", null));
        var fetched = new List<string>();

        var context = await PrContextBuilder.BuildAsync(details, (path, _) =>
        {
            fetched.Add(path);
            return Task.FromResult(new FileDiff(path, null, "text", "old", "new"));
        }, new ContextBudget(20, 20), CancellationToken.None);

        Assert.Equal(["/src/Program.cs"], fetched);
        Assert.Equal(details.Title, context.PullRequest.Title);
        Assert.Equal(details.Description, context.PullRequest.Description);
        Assert.Equal(details.SourceBranch, context.PullRequest.SourceBranch);
        Assert.Equal("Ada", Assert.Single(context.PullRequest.Reviewers).Name);
        Assert.Equal("42", Assert.Single(context.PullRequest.WorkItems).Id);
        Assert.Equal(["First change", "Second change"], context.CommitTitles);
        Assert.Equal(6, context.ChangedFiles.Count);
        Assert.Equal([null, "lockFile", "generated", "snapshot", "buildOutput", "minified"],
            context.ChangedFiles.Select(file => file.OmissionReason));
        Assert.Equal("old", context.ChangedFiles[0].OriginalText);
        Assert.All(context.ChangedFiles.Skip(1), file =>
        {
            Assert.Null(file.OriginalText);
            Assert.Null(file.ModifiedText);
        });
        Assert.True(context.WasLimited);
    }

    [Fact]
    public async Task AppliesPerFileAndTotalLimitsWithoutTruncatingDiffs()
    {
        var details = Details(
            new ChangedFile("/exact.txt", "edit", null),
            new ChangedFile("/oversized.txt", "edit", null),
            new ChangedFile("/remaining.txt", "rename", "/old.txt"),
            new ChangedFile("/last.txt", "add", null));
        var fetched = new List<string>();
        var diffs = new Dictionary<string, FileDiff>
        {
            ["/exact.txt"] = new("/exact.txt", null, "text", "abc", "def"),
            ["/oversized.txt"] = new("/oversized.txt", null, "text", "1234567", ""),
            ["/remaining.txt"] = new("/remaining.txt", "/old.txt", "text", "123", "456"),
            ["/last.txt"] = new("/last.txt", null, "text", "", "x")
        };

        var context = await PrContextBuilder.BuildAsync(details, (path, _) =>
        {
            fetched.Add(path);
            return Task.FromResult(diffs[path]);
        }, new ContextBudget(6, 12), CancellationToken.None);

        Assert.Equal(12, context.IncludedDiffCharacters);
        Assert.Equal(["/exact.txt", "/oversized.txt", "/remaining.txt"], fetched);
        Assert.Equal([null, "fileCharacterLimit", null, "pullRequestCharacterLimit"],
            context.ChangedFiles.Select(file => file.OmissionReason));
        Assert.Equal("/old.txt", context.ChangedFiles[2].OriginalPath);
        Assert.Null(context.ChangedFiles[1].ModifiedText);
        Assert.Equal("456", context.ChangedFiles[2].ModifiedText);
    }

    [Fact]
    public async Task ReportsDiffSourceLimitsAndBinaryFilesThenContinues()
    {
        var details = Details(
            new ChangedFile("/image.png", "add", null),
            new ChangedFile("/large.txt", "edit", null),
            new ChangedFile("/small.txt", "add", null));
        var kinds = new[] { "binary", "tooLarge", "text" };
        var index = 0;

        var context = await PrContextBuilder.BuildAsync(details, (path, _) =>
            Task.FromResult(new FileDiff(path, null, kinds[index++],
                path == "/small.txt" ? "" : null,
                path == "/small.txt" ? "hello" : null)),
            new ContextBudget(5, 5), CancellationToken.None);

        Assert.Equal(3, index);
        Assert.Equal(["binary", "sourceTooLarge", null],
            context.ChangedFiles.Select(file => file.OmissionReason));
        Assert.Equal(5, context.IncludedDiffCharacters);
        Assert.Equal("hello", context.ChangedFiles[2].ModifiedText);
    }

    [Fact]
    public async Task CountsBothVersionsAgainstTotalLimit()
    {
        var context = await PrContextBuilder.BuildAsync(Details(new ChangedFile("/file.txt", "edit", null)),
            (path, _) => Task.FromResult(new FileDiff(path, null, "text", "abcd", "efgh")),
            new ContextBudget(10, 7), CancellationToken.None);

        Assert.Equal("pullRequestCharacterLimit", Assert.Single(context.ChangedFiles).OmissionReason);
        Assert.Equal(0, context.IncludedDiffCharacters);
    }

    private static PullRequestDetails Details(params ChangedFile[] files) => new(
        123, "Change", "Description", "Author", "Repository", "feature", "main", "active",
        DateTimeOffset.Parse("2026-09-01T12:00:00Z"), [new Reviewer("Ada", 10)],
        files.Length, files, 2,
        [new Commit("a", "First change\nbody", "Author", null),
         new Commit("b", "Second change", "Author", null)],
        [new WorkItem("42", "https://example.test/42")]);
}
