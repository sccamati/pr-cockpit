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
        if (draft.SchemaVersion != SummaryContract.SchemaVersion ||
            draft.Sentences is not { Count: >= 2 and <= 5 } ||
            draft.Sentences.Any(sentence => string.IsNullOrWhiteSpace(sentence) || sentence.Length > 500))
            throw new SummaryAnalysisException("AI returned an invalid Summary.", 502);

        var criticalFiles = ValidateCriticalFiles(draft.CriticalFiles, context);

        var omitted = context.ChangedFiles
            .Where(file => file.OmissionReason is not null)
            .Select(file => new OmittedContextFile(file.Path, file.OmissionReason!)).ToArray();
        var report = new SummaryContextReport(context.ChangedFiles.Count,
            context.ChangedFiles.Count - omitted.Length, context.IncludedDiffCharacters,
            context.WasLimited, omitted);
        var sentences = draft.Sentences.Select(sentence => sentence.Trim()).ToArray();
        return new SummaryResponse(SummaryContract.SchemaVersion, string.Join(" ", sentences),
            context.PullRequest.BaseCommitSha, context.PullRequest.HeadCommitSha, report, sentences,
            criticalFiles);
    }

    /// <summary>
    /// The per-file explanation. Shorter than a Summary and about one file, but validated
    /// with the same suspicion: the path comes from the context, never from the model.
    /// </summary>
    public static async Task<FileExplanation> RunFileAsync(
        PrContext context, IAiSummaryAnalyzer analyzer, CancellationToken ct)
    {
        var file = context.ChangedFiles.Count == 1 ? context.ChangedFiles[0]
            : throw new InvalidOperationException("A file explanation needs a context of exactly one file.");

        var draft = await analyzer.ExplainFileAsync(context, ct);
        if (draft.SchemaVersion != SummaryContract.SchemaVersion ||
            draft.Sentences is not { Count: >= 1 and <= 3 } ||
            draft.Sentences.Any(sentence => string.IsNullOrWhiteSpace(sentence) || sentence.Length > 500))
            throw new SummaryAnalysisException("AI returned an invalid file explanation.", 502);

        return new FileExplanation(SummaryContract.SchemaVersion, file.Path,
            context.PullRequest.HeadCommitSha,
            draft.Sentences.Select(sentence => sentence.Trim()).ToArray());
    }

    /// <summary>
    /// A path the model invented would send the reader to a file that is not in the pull
    /// request, so the list is checked against the context we built rather than trusted.
    /// </summary>
    private static IReadOnlyList<CriticalFile> ValidateCriticalFiles(
        IReadOnlyList<CriticalFile>? files, PrContext context)
    {
        if (files is null || files.Count == 0) return [];
        if (files.Count > SummaryContract.MaxCriticalFiles)
            throw new SummaryAnalysisException("AI returned an invalid Summary.", 502);

        var allowed = context.ChangedFiles.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CriticalFile>(files.Count);
        foreach (var file in files)
        {
            if (file is null || !allowed.Contains(file.Path) || !seen.Add(file.Path) ||
                Invalid(file.Role) || Invalid(file.Why))
                throw new SummaryAnalysisException("AI returned an invalid Summary.", 502);
            result.Add(new CriticalFile(file.Path, file.Role.Trim(), file.Why.Trim()));
        }
        return result;

        static bool Invalid(string? text) =>
            string.IsNullOrWhiteSpace(text) || text.Length > SummaryContract.MaxCriticalFileTextLength;
    }
}

/// <summary>The contract the adapter must meet. Shared by the runner, the store and the prompt.</summary>
public static class SummaryContract
{
    public const int SchemaVersion = 2;
    public const int MaxCriticalFiles = 10;
    public const int MaxCriticalFileTextLength = 200;
}
