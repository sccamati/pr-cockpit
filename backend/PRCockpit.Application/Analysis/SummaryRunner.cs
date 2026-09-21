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
        if (draft.SchemaVersion != SummaryContract.SchemaVersion)
            throw Invalid($"schemaVersion {draft.SchemaVersion} instead of {SummaryContract.SchemaVersion}");
        if (draft.Sentences is not { Count: >= 2 and <= 5 })
            throw Invalid($"{draft.Sentences?.Count ?? 0} sentences instead of 2 to 5");
        if (draft.Sentences.Any(string.IsNullOrWhiteSpace))
            throw Invalid("a blank sentence");
        if (draft.Sentences.Any(sentence => sentence.Length > 500))
            throw Invalid("a sentence longer than 500 characters");

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
    /// The third task: an answer to a free-text question about one file. Same suspicion as
    /// the explanation, with a wider sentence range — "why does this exist" can need more
    /// room than "what does this file do" — and the path still comes from the context,
    /// never from the model or from the request.
    /// </summary>
    public static async Task<FileQuestionTurn> RunAskAsync(
        PrContext context, FileQuestion question, IAiSummaryAnalyzer analyzer, CancellationToken ct)
    {
        var file = context.ChangedFiles.Count == 1 ? context.ChangedFiles[0]
            : throw new InvalidOperationException("A file question needs a context of exactly one file.");

        var draft = await analyzer.AskFileAsync(context, question, ct);
        if (draft.SchemaVersion != SummaryContract.SchemaVersion ||
            draft.Sentences is not { Count: >= 1 and <= 6 } ||
            draft.Sentences.Any(sentence => string.IsNullOrWhiteSpace(sentence) || sentence.Length > 500))
            throw new SummaryAnalysisException("AI returned an invalid answer.", 502);

        return new FileQuestionTurn(SummaryContract.SchemaVersion, file.Path, question.Question,
            question.Selection, [.. draft.Sentences.Select(sentence => sentence.Trim())],
            question.BlobId, context.PullRequest.HeadCommitSha, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// A path the model invented would send the reader to a file that is not in the pull
    /// request, so the list is checked against the context we built rather than trusted.
    /// </summary>
    private static IReadOnlyList<CriticalFile> ValidateCriticalFiles(
        IReadOnlyList<CriticalFile>? files, PrContext context)
    {
        if (files is null || files.Count == 0) return [];
        var limit = SummaryContract.CriticalFileLimit(context.ChangedFiles.Count);
        if (files.Count > limit)
            throw Invalid($"criticalFiles holds {files.Count} entries, {limit} allowed here");

        var allowed = context.ChangedFiles.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CriticalFile>(files.Count);
        foreach (var file in files)
        {
            if (file is null) throw Invalid("an empty entry in criticalFiles");
            if (!allowed.Contains(file.Path))
                throw Invalid("a criticalFiles path that is not among the changed files");
            if (!seen.Add(file.Path)) throw Invalid("the same file twice in criticalFiles");
            if (TooLong(file.Role) || TooLong(file.Why))
                throw Invalid($"a criticalFiles role or why that is blank or over {SummaryContract.MaxCriticalFileTextLength} characters");
            result.Add(new CriticalFile(file.Path, file.Role.Trim(), file.Why.Trim()));
        }
        return result;

        static bool TooLong(string? text) =>
            string.IsNullOrWhiteSpace(text) || text.Length > SummaryContract.MaxCriticalFileTextLength;
    }

    /// <summary>
    /// Every rejection says which rule broke, and never by quoting the model: the reason is
    /// written here, the numbers are ours. One message for nine different faults was a 502
    /// nobody could act on, which is the same lesson the invalid-JSON error already taught.
    /// </summary>
    private static SummaryAnalysisException Invalid(string reason) =>
        new($"AI returned an invalid Summary: {reason}.", 502);

    /// <summary>
    /// The whole pull request in reading order — hints, not a contract. Unlike the shortlist,
    /// a bad entry here is dropped rather than rejected: this list is one line per changed
    /// file, so on a 90-file pull request a single mistyped or repeated path would otherwise
    /// throw away the sentences, the shortlist and the order together, and charge for the
    /// run again. Nothing is lost by dropping one — a path that is not in the pull request
    /// does not belong in a walk through it, and a repeat is already placed. The guarantee
    /// the walkthrough needs (every path real, each exactly once, none missing) is produced
    /// here rather than believed: whatever the model left out is appended in the order the
    /// change list arrived. Noise ends up last whatever the model said, because a lockfile
    /// is never where a change is understood.
    /// </summary>
    private static IReadOnlyList<string> ValidateReadingOrder(
        IReadOnlyList<string>? paths, PrContext context)
    {
        var allowed = context.ChangedFiles.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>(context.ChangedFiles.Count);
        foreach (var path in paths ?? [])
            if (path is not null && allowed.Contains(path) && seen.Add(path))
                order.Add(path);

        order.AddRange(context.ChangedFiles.Select(file => file.Path).Where(path => !seen.Contains(path)));
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
