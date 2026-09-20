namespace PRCockpit.Domain.PullRequests;

/// <summary>
/// How much diff text may be handed to an analyzer. Text is either included whole or
/// dropped whole — never truncated — so a model never reasons about half a file.
/// </summary>
public record ContextBudget(int MaxDiffCharactersPerFile, int MaxDiffCharactersPerPullRequest)
{
    public static ContextBudget Default { get; } = new(20_000, 100_000);
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

public record PrContext(
    PrContextMetadata PullRequest, IReadOnlyList<string> CommitTitles,
    IReadOnlyList<ContextChangedFile> ChangedFiles, ContextBudget Budget,
    int IncludedDiffCharacters, bool WasLimited);
