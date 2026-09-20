using PRCockpit.Application.Ports;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Application.Analysis;

/// <summary>
/// Validates whatever the AI adapter returned before anything is stored or shown. The
/// context report is built from the context we prepared, never from the model's claims.
/// </summary>
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
        var sentences = draft.Sentences.Select(sentence => sentence.Trim()).ToArray();
        return new SummaryResponse(1, string.Join(" ", sentences),
            context.PullRequest.BaseCommitSha, context.PullRequest.HeadCommitSha, report, sentences);
    }
}
