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
        var analyzer = new FakeAnalyzer(new SummaryDraft(1, ["  Zmieniono przepływ faktur.", "Dodano testy.  "]));

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
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    [InlineData(1, 6)]
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
            var analyzer = new FakeAnalyzer(new SummaryDraft(1, ["Good.", invalid]));
            await Assert.ThrowsAsync<SummaryAnalysisException>(() =>
                SummaryRunner.RunAsync(Context(), analyzer, CancellationToken.None));
        }
    }

    [Fact]
    public async Task CliRequiresBackendConfiguration()
    {
        var config = new ConfigurationBuilder().Build();

        var error = await Assert.ThrowsAsync<SummaryAnalysisException>(() =>
            new CliSummaryAnalyzer(config).AnalyzeAsync(Context(), CancellationToken.None));

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
            new CliSummaryAnalyzer(config).AnalyzeAsync(Context(), CancellationToken.None));

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
    }
}
