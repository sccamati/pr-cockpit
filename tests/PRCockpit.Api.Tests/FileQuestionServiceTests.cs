using PRCockpit.Application.Analysis;
using PRCockpit.Application.Ports;
using PRCockpit.Application.PullRequests;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;
using PRCockpit.Domain.Review;
using PRCockpit.Infrastructure.Analysis;

namespace PRCockpit.Api.Tests;

public class FileQuestionServiceTests
{
    // The point of this one: validation runs before the expensive work, not behind it. A
    // blank or oversized question must cost neither an Azure DevOps call nor a model run.
    [Fact]
    public async Task RefusesABadQuestionBeforeSpendingAnything()
    {
        var (service, analyzer, client) = Build();
        var longQuestion = new string('q', FileQuestionRequest.MaxQuestionLength + 1);
        var longSelection = new string('s', FileQuestionRequest.MaxSelectionLength + 1);

        foreach (var (question, selection) in new (string?, string?)[]
            { (null, null), ("", null), ("   ", null), (longQuestion, null), ("Po co to?", longSelection) })
        {
            var rejected = await Assert.ThrowsAsync<ChecklistException>(() => service.AskAsync(
                "project", "repo", 1, "/src/file.cs", question, selection, CancellationToken.None));
            Assert.Equal(400, rejected.StatusCode);
        }

        Assert.Equal(0, analyzer.Calls);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task RefusesAPathThatIsNotInThisPullRequest()
    {
        var (service, analyzer, _) = Build();

        var error = await Assert.ThrowsAsync<AzureDevOpsException>(() => service.AskAsync(
            "project", "repo", 1, "/src/elsewhere.cs", "Po co to?", null, CancellationToken.None));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(0, analyzer.Calls);
    }

    // The history filter: only turns about the file as it is now go back to the model, and
    // only the last few of those. Everything else stays in the thread the reviewer reads.
    [Fact]
    public void SendsOnlyTheLastThreeTurnsThatAreStillAboutThisFile()
    {
        FileQuestionTurn Turn(string question, string? blobId, string? headSha) =>
            new(2, "/src/file.cs", question, null, ["Odpowiedź."], blobId, headSha, DateTimeOffset.UtcNow);

        var history = new[]
        {
            Turn("pierwsze", "blob-1", "head-1"),
            Turn("o starej wersji", "blob-0", "head-1"),
            Turn("drugie", "blob-1", "head-1"),
            Turn("bez bloba, inny commit", null, "head-0"),
            Turn("trzecie", "blob-1", "head-1"),
            Turn("czwarte", "blob-1", "head-1"),
        };

        var sent = FileQuestionService.Current(history, "blob-1", "head-1");

        Assert.Equal(["drugie", "trzecie", "czwarte"], sent.Select(turn => turn.Question));
    }

    // What calls the file travels as data in the context the analyzer gets; the instruction
    // is a constant in CliSummaryAnalyzer and never sees it.
    [Fact]
    public async Task AsksWithTheCodeThatUsesTheFile()
    {
        var (service, analyzer, _) = Build();

        await service.AskAsync("project", "repo", 1, "/src/file.cs", "Po co to?", null, CancellationToken.None);

        var usages = analyzer.LastContext?.Usages;
        Assert.NotNull(usages);
        var excerpt = Assert.Single(usages.Excerpts);
        Assert.Equal(("/src/Caller.cs", false), (excerpt.Path, excerpt.InPullRequest));
        Assert.Contains("thing.Run()", excerpt.Text);
    }

    [Fact]
    public async Task StillAnswersWhenTheRepositoryCannotBeRead()
    {
        var (service, analyzer, client) = Build();
        client.SnapshotFails = true;

        await service.AskAsync("project", "repo", 1, "/src/file.cs", "Po co to?", null, CancellationToken.None);

        Assert.Equal(1, analyzer.Calls);
        Assert.Null(analyzer.LastContext?.Usages);
        Assert.Null(analyzer.LastContext?.RelatedFiles);
    }

    // The rest of the pull request rides along, so "co to woła?" has an answer: the caller
    // that is changed here first, the file asked about never twice.
    [Fact]
    public async Task AsksWithTheOtherFilesOfThePullRequest()
    {
        var (service, analyzer, client) = Build();
        client.AlsoChanged = ["/src/Unrelated.cs", "/src/Caller.cs"];

        await service.AskAsync("project", "repo", 1, "/src/file.cs", "Kto to woła?", null, CancellationToken.None);

        var related = analyzer.LastContext?.RelatedFiles;
        Assert.NotNull(related);
        Assert.Equal(["/src/Caller.cs", "/src/Unrelated.cs"], related.Select(file => file.Path));
        Assert.Contains("thing.Run()", related[0].Text);
    }

    [Theory]
    [InlineData('b', true)]
    [InlineData('c', false)]
    public async Task AsksWithTheSummaryOnlyWhileItDescribesTheCurrentHead(char head, bool expected)
    {
        var (service, analyzer, _) = Build(new SummaryResponse(2, "Zmiana.", new string('a', 40), new string(head, 40),
            new SummaryContextReport(1, 1, 6, false, []), ["Zmiana."],
            [new CriticalFile("/src/file.cs", "Rdzeń.", "Od niego zacząć.")], ["/src/file.cs"]));

        await service.AskAsync("project", "repo", 1, "/src/file.cs", "Po co to?", null, CancellationToken.None);

        var summary = analyzer.LastContext?.Summary;
        Assert.Equal(expected, summary is not null);
        if (expected) Assert.Equal("Rdzeń.", Assert.Single(summary!.CriticalFiles).Role);
    }

    private static (FileQuestionService Service, FakeAnalyzer Analyzer, FakeClient Client) Build(
        SummaryResponse? summary = null)
    {
        var analyzer = new FakeAnalyzer();
        var client = new FakeClient();
        var service = new FileQuestionService(
            client, new PullRequestContextService(client), analyzer, new FakeStore(),
            new CodeUsageService(client, new MemorySourceSnapshotCache(), new CodeUsageFinder()),
            new FakeSummaryStore(summary));
        return (service, analyzer, client);
    }

    private sealed class FakeSummaryStore(SummaryResponse? summary) : ISummaryStore
    {
        public Task<StoredSummary?> GetAsync(string project, string repositoryId, int pullRequestId, CancellationToken ct) =>
            Task.FromResult(summary is null ? null : new StoredSummary(summary, DateTimeOffset.UtcNow));

        public Task<StoredSummary> SaveAsync(
            string project, string repositoryId, int pullRequestId, SummaryResponse result, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAnalyzer : IAiSummaryAnalyzer
    {
        public int Calls { get; private set; }
        public PrContext? LastContext { get; private set; }

        private Task<SummaryDraft> Answer()
        {
            Calls++;
            return Task.FromResult(new SummaryDraft(2, ["Odpowiedź."]));
        }

        public Task<SummaryDraft> AnalyzeAsync(PrContext context, CancellationToken ct) => Answer();
        public Task<SummaryDraft> ExplainFileAsync(PrContext context, CancellationToken ct) => Answer();
        public Task<SummaryDraft> AskFileAsync(PrContext context, FileQuestion question, CancellationToken ct)
        {
            LastContext = context;
            return Answer();
        }
    }

    private sealed class FakeStore : IFileQuestionStore
    {
        public Task<IReadOnlyList<FileQuestionTurn>> GetAsync(
            string project, string repositoryId, int pullRequestId, string path, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FileQuestionTurn>>([]);

        public Task<FileQuestionTurn> SaveAsync(
            string project, string repositoryId, int pullRequestId, FileQuestionTurn turn, CancellationToken ct) =>
            Task.FromResult(turn);
    }

    // Wide port, narrow fake: only the two calls this path makes are implemented, so a
    // change that starts reaching for anything else fails loudly instead of quietly.
    private sealed class FakeClient : IAzureDevOpsClient
    {
        public int Calls { get; private set; }
        public string[] AlsoChanged { get; set; } = [];

        public Task<PullRequestDetails> GetPullRequestAsync(
            string project, string repositoryId, int pullRequestId, CancellationToken ct)
        {
            Calls++;
            ChangedFile[] files = [new ChangedFile("/src/file.cs", "edit", null, "blob-1"),
                .. AlsoChanged.Select(path => new ChangedFile(path, "edit", null))];
            return Task.FromResult(new PullRequestDetails(1, "Change", null, "Author", "Repo", "feature", "main",
                "active", DateTimeOffset.UtcNow, [], files.Length, files, 0, [], [],
                new string('a', 40), new string('b', 40)));
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
        public bool SnapshotFails { get; set; }

        public Task<SourceSnapshot> GetSourceSnapshotAsync(string project, string repositoryId, string commitSha, CancellationToken ct) =>
            SnapshotFails
                ? throw new AzureDevOpsException("Azure DevOps request failed.", 502)
                : Task.FromResult(new SourceSnapshot(commitSha, new Dictionary<string, string>
                {
                    ["/src/file.cs"] = "namespace App; public class Thing { public void Run() { } }",
                    ["/src/Caller.cs"] = "namespace App;\npublic class Caller\n{\n    public void Go(Thing thing) => thing.Run();\n}",
                }, 0));
    }
}
