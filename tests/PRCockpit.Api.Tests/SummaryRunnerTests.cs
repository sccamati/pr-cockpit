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

        public Task<SummaryDraft> AnalyzeAsync(PrContext context, CancellationToken ct)
        {
            Calls++;
            ReceivedContext = context;
            return Task.FromResult(response);
        }

        public Task<SummaryDraft> ExplainFileAsync(PrContext context, CancellationToken ct) =>
            AnalyzeAsync(context, ct);
    }
}
