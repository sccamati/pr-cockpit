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
        var readingOrder = ValidateReadingOrder(draft.ReadingOrder, context);

        var omitted = context.ChangedFiles
            .Where(file => file.OmissionReason is not null)
            .Select(file => new OmittedContextFile(file.Path, file.OmissionReason!)).ToArray();
        var report = new SummaryContextReport(context.ChangedFiles.Count,
            context.ChangedFiles.Count - omitted.Length, context.IncludedDiffCharacters,
            context.WasLimited, omitted);
        var sentences = draft.Sentences.Select(sentence => sentence.Trim()).ToArray();
        return new SummaryResponse(SummaryContract.SchemaVersion, string.Join(" ", sentences),
            context.PullRequest.BaseCommitSha, context.PullRequest.HeadCommitSha, report, sentences,
            criticalFiles, readingOrder);
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
        if (files.Count > SummaryContract.CriticalFileLimit(context.ChangedFiles.Count))
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

    /// <summary>
    /// The whole pull request in reading order. Checked against the context the same way the
    /// shortlist is — an invented or repeated path is a rejection — but what the model left
    /// out is appended by us rather than refused: a missing file is our gap to fill, not the
    /// model's claim to believe. Noise ends up last whatever the model said, because a
    /// lockfile is never where a change is understood.
    /// </summary>
    private static IReadOnlyList<string> ValidateReadingOrder(
        IReadOnlyList<string>? paths, PrContext context)
    {
        var order = new List<string>(context.ChangedFiles.Count);
        if (paths is not null)
        {
            if (paths.Count > context.ChangedFiles.Count)
                throw new SummaryAnalysisException("AI returned an invalid Summary.", 502);

            var allowed = context.ChangedFiles.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                if (path is null || !allowed.Contains(path) || !seen.Add(path))
                    throw new SummaryAnalysisException("AI returned an invalid Summary.", 502);
                order.Add(path);
            }
        }

        var listed = order.ToHashSet(StringComparer.Ordinal);
        order.AddRange(context.ChangedFiles.Select(file => file.Path).Where(path => !listed.Contains(path)));
        // A stable partition, so the order the model chose survives inside each half.
        return [.. order.Where(path => FileCategory.Of(path) is null),
                .. order.Where(path => FileCategory.Of(path) is not null)];
    }
}

/// <summary>The contract the adapter must meet. Shared by the runner, the store and the prompt.</summary>
public static class SummaryContract
{
    public const int SchemaVersion = 2;
    public const int MaxCriticalFileTextLength = 200;

    /// <summary>
    /// Ten files is enough to open a pull request of twenty and far too few to open one of
    /// eighty: at that size a ranking of ten is not a starting point, it is a sample. So the
    /// ranking grows with the change, roughly one named file per four changed ones, and
    /// stops at <see cref="MaxCriticalFiles"/> — past that the list stops being a shortlist.
    /// <para>The same rule runs in the browser (criticalFileLimit in App.vue). Keep them in step.</para>
    /// </summary>
    public const int MinCriticalFiles = 10;
    public const int MaxCriticalFiles = 25;

    public static int CriticalFileLimit(int changedFileCount) =>
        Math.Clamp((changedFileCount + 3) / 4, MinCriticalFiles, MaxCriticalFiles);
}
