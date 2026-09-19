using PRCockpit.Api.AzureDevOps;

namespace PRCockpit.Api.Analysis;

public interface IAiSummaryAnalyzer
{
    Task<SummaryDraft> AnalyzeAsync(PrContext context, CancellationToken ct);
}

public record SummaryDraft(int SchemaVersion, IReadOnlyList<string> Sentences);
public record OmittedContextFile(string Path, string Reason);
public record SummaryContextReport(
    int ChangedFiles, int IncludedFiles, int IncludedDiffCharacters,
    bool WasLimited, IReadOnlyList<OmittedContextFile> OmittedFiles);
public record SummaryResponse(
    int SchemaVersion, string Summary, string? BaseCommitSha, string? HeadCommitSha,
    SummaryContextReport ContextReport);

public sealed class SummaryAnalysisException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public static class SummaryRunner
{
    public static async Task<SummaryResponse> RunAsync(
        PrContext context, IAiSummaryAnalyzer analyzer, CancellationToken ct)
    {
        var draft = await analyzer.AnalyzeAsync(context, ct);
        if (draft.SchemaVersion != 1 || draft.Sentences is not { Count: >= 2 and <= 5 } ||
            draft.Sentences.Any(sentence => string.IsNullOrWhiteSpace(sentence) || sentence.Length > 500))
            throw new SummaryAnalysisException("AI returned an invalid Summary.", 502);

        var omitted = context.ChangedFiles
            .Where(file => file.OmissionReason is not null)
            .Select(file => new OmittedContextFile(file.Path, file.OmissionReason!)).ToArray();
        var report = new SummaryContextReport(context.ChangedFiles.Count,
            context.ChangedFiles.Count - omitted.Length, context.IncludedDiffCharacters,
            context.WasLimited, omitted);
        return new SummaryResponse(1, string.Join(" ", draft.Sentences.Select(sentence => sentence.Trim())),
            context.PullRequest.BaseCommitSha, context.PullRequest.HeadCommitSha, report);
    }
}
