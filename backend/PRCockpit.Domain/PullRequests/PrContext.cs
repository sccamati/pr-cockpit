namespace PRCockpit.Domain.PullRequests;

/// <summary>
/// How much diff text may be handed to an analyzer. Text is either included whole or
/// dropped whole — never truncated — so a model never reasons about half a file.
/// Both versions count, so a file's share is its old text plus its new text.
/// </summary>
public record ContextBudget(int MaxDiffCharactersPerFile, int MaxDiffCharactersPerPullRequest)
{
    // ponytail: sized to stay near 130k tokens at worst, inside the 200k window the CLI assumes
    // for a model it does not know yet; raise both once the adapter asks for [1m].
    // The whole-PR Summary: the old 20 000 per file dropped any file over ~250 lines.
    public static ContextBudget Default { get; } = new(60_000, 400_000);

    // One file, explained or asked about: the file is the whole point, so it is never left
    // out for size unless it is enormous.
    public static ContextBudget OneFile { get; } = new(300_000, 300_000);
}

public record PrContextMetadata(
    int Id, string Title, string? Description, string Author, string Repository,
    string SourceBranch, string TargetBranch, string Status, DateTimeOffset CreatedAt,
    IReadOnlyList<Reviewer> Reviewers, IReadOnlyList<WorkItem> WorkItems,
    string? BaseCommitSha, string? HeadCommitSha);

/// <summary>A changed file with both versions, or the reason its text was left out.</summary>
public record ContextChangedFile(
    string Path, string ChangeType, string? OriginalPath,
    string? OriginalText, string? ModifiedText, string? OmissionReason);

/// <summary>
/// Another file the pull request changed, as it reads at the head commit. Text is null when it
/// was left out — not a source file, deleted, over the budget — and never cut short.
/// </summary>
public record RelatedFile(string Path, string ChangeType, string? Text);

/// <summary>
/// <c>Usages</c> is filled only for the one-file tasks (explain, ask), and only for a file
/// usages exist for; the whole-PR Summary never carries it. <c>RelatedFiles</c> and
/// <c>Summary</c> are filled only for a question, the one task asked to connect files.
/// </summary>
public record PrContext(
    PrContextMetadata PullRequest, IReadOnlyList<string> CommitTitles,
    IReadOnlyList<ContextChangedFile> ChangedFiles, ContextBudget Budget,
    int IncludedDiffCharacters, bool WasLimited, Analysis.UsageContext? Usages = null,
    IReadOnlyList<RelatedFile>? RelatedFiles = null, Analysis.SummaryDigest? Summary = null);
