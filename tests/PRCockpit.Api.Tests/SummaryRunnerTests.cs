using Microsoft.Extensions.Logging.Abstractions;
using PRCockpit.Application.Analysis;
using PRCockpit.Domain.Analysis;
using PRCockpit.Infrastructure.Analysis;
using PRCockpit.Application.PullRequests;
using PRCockpit.Domain.PullRequests;
using PRCockpit.Infrastructure.AzureDevOps;
using Microsoft.Extensions.Configuration;
using PRCockpit.Application.Ports;

namespace PRCockpit.Api.Tests;

public class SummaryRunnerTests
{
    [Fact]
    public async Task PassesThePreparedContextToTheAdapterAndReportsOmissions()
    {
        var context = Context();
        var analyzer = new FakeAnalyzer(new SummaryDraft(2, ["  Zmieniono przepływ faktur.", "Dodano testy.  "],
            [new CriticalFile("/src/file.cs", "  Wejście do wysyłki.  ", "Początek flow.")]));

        var result = await SummaryRunner.RunAsync(context, analyzer, CancellationToken.None);

        Assert.Same(context, analyzer.ReceivedContext);
        Assert.Equal(1, analyzer.Calls);
        Assert.Equal("Zmieniono przepływ faktur. Dodano testy.", result.Summary);
        Assert.Equal(new string('b', 40), result.HeadCommitSha);
        Assert.Equal(2, result.ContextReport.ChangedFiles);
        Assert.Equal(1, result.ContextReport.IncludedFiles);
        Assert.Equal(3, result.ContextReport.IncludedDiffCharacters);
        Assert.True(result.ContextReport.WasLimited);
        Assert.Equal("lockFile", Assert.Single(result.ContextReport.OmittedFiles).Reason);
        var critical = Assert.Single(result.CriticalFiles);
        Assert.Equal("/src/file.cs", critical.Path);
        Assert.Equal("Wejście do wysyłki.", critical.Role);
    }

    [Fact]
    public async Task AcceptsAnEmptyOrMissingCriticalFileList()
    {
        foreach (var draft in new[]
        {
            new SummaryDraft(2, ["One.", "Two."]),
            new SummaryDraft(2, ["One.", "Two."], []),
        })
        {
            var result = await SummaryRunner.RunAsync(Context(), new FakeAnalyzer(draft), CancellationToken.None);
            Assert.Empty(result.CriticalFiles);
        }
    }

    // The path allowlist is the guard against a model sending the reader to a file that is
    // not in the pull request at all.
    [Fact]
    public async Task RejectsCriticalFilesOutsideTheContract()
    {
        var good = new CriticalFile("/src/file.cs", "Rola.", "Powód.");
        IReadOnlyList<CriticalFile>[] invalid =
        [
            [new CriticalFile("/src/invented.cs", "Rola.", "Powód.")],          // not in the PR
            [good, new CriticalFile("/src/file.cs", "Inna.", "Inny.")],         // duplicate path
            [new CriticalFile("/src/file.cs", " ", "Powód.")],                  // blank role
            [new CriticalFile("/src/file.cs", "Rola.", new string('x', 201))],  // over the length limit
            [.. Enumerable.Range(0, 11).Select(_ => good)],                     // over ten entries
        ];

        foreach (var files in invalid)
        {
            var analyzer = new FakeAnalyzer(new SummaryDraft(2, ["One.", "Two."], files));
            var error = await Assert.ThrowsAsync<SummaryAnalysisException>(() =>
                SummaryRunner.RunAsync(Context(), analyzer, CancellationToken.None));
            Assert.Equal(502, error.StatusCode);
        }
    }

    [Fact]
    public async Task ExplainsOneFileAndStampsItWithTheHeadCommit()
    {
        var analyzer = new FakeAnalyzer(new SummaryDraft(2, ["  Serwis wysyłki faktur.  "]));

        var result = await SummaryRunner.RunFileAsync(SingleFileContext(), analyzer, CancellationToken.None);

        Assert.Equal("/src/file.cs", result.Path);
        Assert.Equal(new string('b', 40), result.HeadCommitSha);
        Assert.Equal("Serwis wysyłki faktur.", Assert.Single(result.Sentences));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 4)]
    [InlineData(1, 2)]
    public async Task RejectsFileExplanationsOutsideTheContract(int version, int count)
    {
        var analyzer = new FakeAnalyzer(new SummaryDraft(version,
            Enumerable.Repeat("Zdanie.", count).ToArray()));

        var error = await Assert.ThrowsAsync<SummaryAnalysisException>(() =>
            SummaryRunner.RunFileAsync(SingleFileContext(), analyzer, CancellationToken.None));

        Assert.Equal(502, error.StatusCode);
    }

    [Fact]
    public async Task AnswersAQuestionAboutOneFileAndCarriesTheHistoryToTheAdapter()
    {
        var analyzer = new FakeAnalyzer(new SummaryDraft(2, ["  Trzyma limity.  ", "Wywoływane raz."]));
        var earlier = new FileQuestionTurn(2, "/src/file.cs", "Po co to?", null,
            ["Bo tak."], null, null, DateTimeOffset.UtcNow);
        var question = new FileQuestion("A ten fragment?", "var x = 1;", "blob-1", [earlier]);

        var turn = await SummaryRunner.RunAskAsync(SingleFileContext(), question, analyzer, CancellationToken.None);

        // The path is the context's, never the model's or the request's.
        Assert.Equal("/src/file.cs", turn.Path);
        Assert.Equal(new string('b', 40), turn.HeadCommitSha);
        Assert.Equal("blob-1", turn.BlobId);
        Assert.Equal("A ten fragment?", turn.Question);
        Assert.Equal("var x = 1;", turn.Selection);
        Assert.Equal(["Trzyma limity.", "Wywoływane raz."], turn.Sentences);
        Assert.Same(earlier, Assert.Single(analyzer.ReceivedQuestion!.History));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 7)]
    [InlineData(1, 2)]
    public async Task RejectsAnswersOutsideTheContract(int version, int count)
    {
        var analyzer = new FakeAnalyzer(new SummaryDraft(version,
            Enumerable.Repeat("Zdanie.", count).ToArray()));

        var error = await Assert.ThrowsAsync<SummaryAnalysisException>(() =>
            SummaryRunner.RunAskAsync(SingleFileContext(),
                new FileQuestion("Po co to?", null, null, []), analyzer, CancellationToken.None));

        Assert.Equal(502, error.StatusCode);
    }

    // The path in the result comes from the context, so a context that is not exactly one
    // file is a programming error here rather than something to guess around.
    [Fact]
    public async Task RefusesToExplainAContextThatIsNotExactlyOneFile()
    {
        var analyzer = new FakeAnalyzer(new SummaryDraft(2, ["Zdanie."]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SummaryRunner.RunFileAsync(Context(), analyzer, CancellationToken.None));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 6)]
    public async Task RejectsUnsupportedSchemaAndSentenceCounts(int version, int count)
    {
        var analyzer = new FakeAnalyzer(new SummaryDraft(version,
            Enumerable.Repeat("Sentence.", count).ToArray()));

        var error = await Assert.ThrowsAsync<SummaryAnalysisException>(() =>
            SummaryRunner.RunAsync(Context(), analyzer, CancellationToken.None));

        Assert.Equal(502, error.StatusCode);
    }

    [Fact]
    public async Task RejectsBlankOrOversizedSentences()
    {
        foreach (var invalid in new[] { " ", new string('x', 501) })
        {
            var analyzer = new FakeAnalyzer(new SummaryDraft(2, ["Good.", invalid]));
            await Assert.ThrowsAsync<SummaryAnalysisException>(() =>
                SummaryRunner.RunAsync(Context(), analyzer, CancellationToken.None));
        }
    }

    [Fact]
    public async Task CliRequiresBackendConfiguration()
    {
        var config = new ConfigurationBuilder().Build();

        var error = await Assert.ThrowsAsync<SummaryAnalysisException>(() =>
            new CliSummaryAnalyzer(config, NullLogger<CliSummaryAnalyzer>.Instance).AnalyzeAsync(Context(), CancellationToken.None));

        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task CliRejectsInvalidJsonFromARealProcess()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Summary:Executable"] = "dotnet",
            ["Ai:Summary:Arguments:0"] = "--version"
        }).Build();

        var error = await Assert.ThrowsAsync<SummaryAnalysisException>(() =>
            new CliSummaryAnalyzer(config, NullLogger<CliSummaryAnalyzer>.Instance).AnalyzeAsync(Context(), CancellationToken.None));

        Assert.Equal(502, error.StatusCode);
        Assert.Equal("AI CLI returned invalid JSON.", error.Message);
    }

    // Ten files opens a pull request of twenty; it only samples one of eighty.
    [Theory]
    [InlineData(1, 10)]
    [InlineData(20, 10)]
    [InlineData(40, 10)]
    [InlineData(44, 11)]
    [InlineData(84, 21)]
    [InlineData(100, 25)]
    [InlineData(208, 25)]   // past this the shortlist stops being short
    public void TheRankingGrowsWithTheChangeAndThenStops(int changedFiles, int expected)
    {
        Assert.Equal(expected, SummaryContract.CriticalFileLimit(changedFiles));
    }

    [Fact]
    public async Task ARankingLongerThanThisPullRequestAllowsIsRejected()
    {
        // Two changed files, so the floor of ten applies and eleven names is too many.
        IReadOnlyList<CriticalFile> files = [.. Enumerable.Range(0, 11)
            .Select(index => new CriticalFile($"/src/file{index}.cs", "Rola.", "Powód."))];
        var analyzer = new FakeAnalyzer(new SummaryDraft(2, ["Zdanie jedno.", "Zdanie dwa."], files));

        var error = await Assert.ThrowsAsync<SummaryAnalysisException>(() =>
            SummaryRunner.RunAsync(Context(), analyzer, CancellationToken.None));

        Assert.Equal(502, error.StatusCode);
    }

    // The whole pull request in reading order: the model's order is kept, whatever it left
    // out is filled in by us, and noise lands last however it was listed.
    [Fact]
    public async Task ReadingOrderKeepsTheModelsOrderFillsTheGapsAndSinksTheNoise()
    {
        var analyzer = new FakeAnalyzer(new SummaryDraft(2, ["One.", "Two."], null,
            ["/package-lock.json", "/src/c.cs", "/src/a.cs"]));

        var result = await SummaryRunner.RunAsync(WideContext(), analyzer, CancellationToken.None);

        Assert.Equal(["/src/c.cs", "/src/a.cs", "/src/b.cs", "/package-lock.json"], result.ReadingOrder);
    }

    // No order from the model is not an empty walkthrough: it is our own order, in the
    // order the change list arrived, so "all files" still means all files.
    [Fact]
    public async Task AMissingOrEmptyReadingOrderStillCoversThePullRequest()
    {
        foreach (var draft in new[]
        {
            new SummaryDraft(2, ["One.", "Two."]),
            new SummaryDraft(2, ["One.", "Two."], null, []),
        })
        {
            var result = await SummaryRunner.RunAsync(WideContext(), new FakeAnalyzer(draft), CancellationToken.None);
            Assert.Equal(["/src/a.cs", "/src/b.cs", "/src/c.cs", "/package-lock.json"], result.ReadingOrder);
        }
    }

    // One bad entry among ninety is not worth throwing away the sentences, the shortlist and
    // the order together — and charging for the run again. The bad entry is dropped, and the
    // result is still every file of the pull request exactly once.
    [Fact]
    public async Task DropsBadEntriesFromTheReadingOrderInsteadOfRejectingIt()
    {
        IReadOnlyList<string>[] sloppy =
        [
            ["/src/c.cs", "/src/invented.cs", "/src/a.cs"],   // a path that is not in the PR
            ["/src/c.cs", "/src/c.cs", "/src/a.cs"],          // the same file twice
            ["/src/c.cs", "/src/a.cs", "/src/a.cs", "/src/invented.cs", "/src/c.cs"],
        ];

        foreach (var order in sloppy)
        {
            var analyzer = new FakeAnalyzer(new SummaryDraft(2, ["One.", "Two."], null, order));
            var result = await SummaryRunner.RunAsync(WideContext(), analyzer, CancellationToken.None);
            Assert.Equal(["/src/c.cs", "/src/a.cs", "/src/b.cs", "/package-lock.json"], result.ReadingOrder);
        }
    }

    // Nine different faults behind one message was a 502 nobody could act on.
    [Fact]
    public async Task SaysWhichRuleTheAnswerBroke()
    {
        (SummaryDraft Draft, string Expected)[] cases =
        [
            (new SummaryDraft(3, ["One.", "Two."]), "schemaVersion 3"),
            (new SummaryDraft(2, ["One."]), "1 sentences"),
            (new SummaryDraft(2, ["One.", "   "]), "blank sentence"),
            (new SummaryDraft(2, ["One.", new string('x', 501)]), "longer than 500"),
            (new SummaryDraft(2, ["One.", "Two."],
                [.. Enumerable.Range(0, 11).Select(_ => new CriticalFile("/src/a.cs", "R.", "P."))]),
                "11 entries"),
            (new SummaryDraft(2, ["One.", "Two."], [new CriticalFile("/src/invented.cs", "R.", "P.")]),
                "not among the changed files"),
            (new SummaryDraft(2, ["One.", "Two."],
                [new CriticalFile("/src/a.cs", "R.", "P."), new CriticalFile("/src/a.cs", "R.", "P.")]),
                "same file twice"),
            (new SummaryDraft(2, ["One.", "Two."], [new CriticalFile("/src/a.cs", "R.", new string('x', 201))]),
                "over 200 characters"),
        ];

        foreach (var (draft, expected) in cases)
        {
            var error = await Assert.ThrowsAsync<SummaryAnalysisException>(() =>
                SummaryRunner.RunAsync(WideContext(), new FakeAnalyzer(draft), CancellationToken.None));
            Assert.Equal(502, error.StatusCode);
            Assert.Contains(expected, error.Message);
        }
    }

    private static PrContext WideContext() => new(
        new PrContextMetadata(123, "Change", "Description", "Author", "Repo", "feature", "main",
            "active", DateTimeOffset.Parse("2026-09-01T12:00:00Z"), [], [],
            new string('a', 40), new string('b', 40)),
        ["Change"],
        [new ContextChangedFile("/src/a.cs", "edit", null, "old", "new", null),
         new ContextChangedFile("/src/b.cs", "edit", null, "old", "new", null),
         new ContextChangedFile("/src/c.cs", "edit", null, "old", "new", null),
         new ContextChangedFile("/package-lock.json", "edit", null, null, null, "lockFile")],
        new ContextBudget(20_000, 100_000), 12, false);

    private static PrContext Context() => new(
        new PrContextMetadata(123, "Change", "Description", "Author", "Repo", "feature", "main",
            "active", DateTimeOffset.Parse("2026-09-01T12:00:00Z"), [], [],
            new string('a', 40), new string('b', 40)),
        ["Change"],
        [new ContextChangedFile("/src/file.cs", "edit", null, "old", "", null),
         new ContextChangedFile("/package-lock.json", "edit", null, null, null, "lockFile")],
        new ContextBudget(20_000, 100_000), 3, true);

    private static PrContext SingleFileContext() => new(
        new PrContextMetadata(123, "Change", "Description", "Author", "Repo", "feature", "main",
            "active", DateTimeOffset.Parse("2026-09-01T12:00:00Z"), [], [],
            new string('a', 40), new string('b', 40)),
        ["Change"],
        [new ContextChangedFile("/src/file.cs", "edit", null, "old", "", null)],
        new ContextBudget(20_000, 100_000), 3, false);

    private sealed class FakeAnalyzer(SummaryDraft response) : IAiSummaryAnalyzer
    {
        public int Calls { get; private set; }
        public PrContext? ReceivedContext { get; private set; }
        public FileQuestion? ReceivedQuestion { get; private set; }

        public Task<SummaryDraft> AnalyzeAsync(PrContext context, CancellationToken ct)
        {
            Calls++;
            ReceivedContext = context;
            return Task.FromResult(response);
        }

        public Task<SummaryDraft> ExplainFileAsync(PrContext context, CancellationToken ct) =>
            AnalyzeAsync(context, ct);

        public Task<SummaryDraft> AskFileAsync(PrContext context, FileQuestion question, CancellationToken ct)
        {
            ReceivedQuestion = question;
            return AnalyzeAsync(context, ct);
        }
    }
}
