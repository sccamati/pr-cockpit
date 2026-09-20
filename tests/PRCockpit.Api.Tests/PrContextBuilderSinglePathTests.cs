using PRCockpit.Application.PullRequests;
using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Api.Tests;

public class PrContextBuilderSinglePathTests
{
    [Fact]
    public async Task NarrowsToOneFileAndFetchesNothingElse()
    {
        var details = Details(
            new ChangedFile("/src/Program.cs", "edit", null),
            new ChangedFile("/src/Other.cs", "edit", null));
        var fetched = new List<string>();

        var context = await PrContextBuilder.BuildAsync(details, (path, _) =>
        {
            fetched.Add(path);
            return Task.FromResult(new FileDiff(path, null, "text", "old", "new"));
        }, ContextBudget.Default, CancellationToken.None, "/src/Other.cs");

        Assert.Equal(["/src/Other.cs"], fetched);
        Assert.Equal("/src/Other.cs", Assert.Single(context.ChangedFiles).Path);
    }

    // The same budget applies to a single file as inside a whole-PR package: over budget
    // means the text is dropped whole and the reason is reported, never truncated.
    [Fact]
    public async Task KeepsTheBudgetForASingleFile()
    {
        var details = Details(new ChangedFile("/src/Big.cs", "edit", null));

        var context = await PrContextBuilder.BuildAsync(details,
            (path, _) => Task.FromResult(new FileDiff(path, null, "text", new string('x', 30), "")),
            new ContextBudget(10, 100), CancellationToken.None, "/src/Big.cs");

        var file = Assert.Single(context.ChangedFiles);
        Assert.Equal("fileCharacterLimit", file.OmissionReason);
        Assert.Null(file.ModifiedText);
    }

    private static PullRequestDetails Details(params ChangedFile[] files) => new(
        123, "Change", null, "Author", "Repo", "feature", "main", "active",
        DateTimeOffset.Parse("2026-09-01T12:00:00Z"), [], files.Length, files, 0, [], [],
        new string('a', 40), new string('b', 40));
}
