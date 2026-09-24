using PRCockpit.Application.Analysis;
using PRCockpit.Application.Ports;
using PRCockpit.Application.PullRequests;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;
using PRCockpit.Domain.Review;

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

    private static (FileQuestionService Service, FakeAnalyzer Analyzer, FakeClient Client) Build()
    {
        var analyzer = new FakeAnalyzer();
        var client = new FakeClient();
        var service = new FileQuestionService(
            client, new PullRequestContextService(client), analyzer, new FakeStore());
        return (service, analyzer, client);
    }

    private sealed class FakeAnalyzer : IAiSummaryAnalyzer
    {
        public int Calls { get; private set; }

        private Task<SummaryDraft> Answer()
        {
            Calls++;
            return Task.FromResult(new SummaryDraft(2, ["Odpowiedź."]));
        }

        public Task<SummaryDraft> AnalyzeAsync(PrContext context, CancellationToken ct) => Answer();
        public Task<SummaryDraft> ExplainFileAsync(PrContext context, CancellationToken ct) => Answer();
        public Task<SummaryDraft> AskFileAsync(PrContext context, FileQuestion question, CancellationToken ct) => Answer();
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

        public Task<PullRequestDetails> GetPullRequestAsync(
            string project, string repositoryId, int pullRequestId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new PullRequestDetails(1, "Change", null, "Author", "Repo", "feature", "main",
                "active", DateTimeOffset.UtcNow, [], 1,
                [new ChangedFile("/src/file.cs", "edit", null, "blob-1")], 0, [], [],
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
        public Task<FileDiff> GetFileDiffSinceIterationAsync(string project, string repositoryId, int pullRequestId, string path, int iterationId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetChangedPathsSinceIterationAsync(string project, string repositoryId, int pullRequestId, int iterationId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SourceSnapshot> GetSourceSnapshotAsync(string project, string repositoryId, string commitSha, CancellationToken ct) => throw new NotSupportedException();
    }
}
