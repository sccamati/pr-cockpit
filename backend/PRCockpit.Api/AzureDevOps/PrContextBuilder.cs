namespace PRCockpit.Api.AzureDevOps;

public record ContextBudget(int MaxDiffCharactersPerFile, int MaxDiffCharactersPerPullRequest)
{
    public static ContextBudget Default { get; } = new(20_000, 100_000);
}

public record PrContextMetadata(
    int Id, string Title, string? Description, string Author, string Repository,
    string SourceBranch, string TargetBranch, string Status, DateTimeOffset CreatedAt,
    IReadOnlyList<Reviewer> Reviewers, IReadOnlyList<WorkItem> WorkItems,
    string? BaseCommitSha, string? HeadCommitSha);

public record ContextChangedFile(
    string Path, string ChangeType, string? OriginalPath,
    string? OriginalText, string? ModifiedText, string? OmissionReason);

public record PrContext(
    PrContextMetadata PullRequest, IReadOnlyList<string> CommitTitles,
    IReadOnlyList<ContextChangedFile> ChangedFiles, ContextBudget Budget,
    int IncludedDiffCharacters, bool WasLimited);

public static class PrContextBuilder
{
    public static async Task<PrContext> BuildAsync(
        PullRequestDetails details,
        Func<string, CancellationToken, Task<FileDiff>> getDiff,
        ContextBudget budget,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(getDiff);
        ArgumentNullException.ThrowIfNull(budget);
        if (budget.MaxDiffCharactersPerFile <= 0 || budget.MaxDiffCharactersPerPullRequest <= 0)
            throw new ArgumentOutOfRangeException(nameof(budget), "Diff character limits must be positive.");

        var files = new List<ContextChangedFile>(details.ChangedFilesCount);
        var used = 0;
        foreach (var change in details.ChangedFiles)
        {
            ct.ThrowIfCancellationRequested();
            var reason = ExcludedType(change.Path) ??
                (used >= budget.MaxDiffCharactersPerPullRequest ? "pullRequestCharacterLimit" : null);
            if (reason is not null)
            {
                files.Add(Omitted(change, reason));
                continue;
            }

            var diff = await getDiff(change.Path, ct);
            if (diff.Kind != "text")
            {
                files.Add(Omitted(change, diff.Kind switch
                {
                    "binary" => "binary",
                    "tooLarge" => "sourceTooLarge",
                    _ => throw new InvalidOperationException($"Unexpected diff kind: {diff.Kind}")
                }));
                continue;
            }

            var original = diff.OriginalText ?? throw new InvalidOperationException("Text diff has no original text.");
            var modified = diff.ModifiedText ?? throw new InvalidOperationException("Text diff has no modified text.");
            var length = (long)original.Length + modified.Length;
            reason = length > budget.MaxDiffCharactersPerFile ? "fileCharacterLimit" :
                length > budget.MaxDiffCharactersPerPullRequest - used ? "pullRequestCharacterLimit" : null;
            if (reason is not null)
            {
                files.Add(Omitted(change, reason));
                continue;
            }

            used += (int)length;
            files.Add(new ContextChangedFile(change.Path, change.ChangeType, change.OriginalPath,
                original, modified, null));
        }

        var metadata = new PrContextMetadata(details.Id, details.Title, details.Description,
            details.Author, details.Repository, details.SourceBranch, details.TargetBranch,
            details.Status, details.CreatedAt, details.Reviewers, details.WorkItems,
            details.BaseCommitSha, details.HeadCommitSha);
        var titles = details.Commits.Select(commit => commit.Message.Split(['\r', '\n'], 2)[0]).ToArray();
        return new PrContext(metadata, titles, files, budget, used,
            files.Any(file => file.OmissionReason is not null));
    }

    private static ContextChangedFile Omitted(ChangedFile change, string reason) =>
        new(change.Path, change.ChangeType, change.OriginalPath, null, null, reason);

    private static string? ExcludedType(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("npm-shrinkwrap.json", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("yarn.lock", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            return "lockFile";
        if (name.EndsWith(".snap", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/__snapshots__/", StringComparison.OrdinalIgnoreCase))
            return "snapshot";
        if (name.Contains(".generated.", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            return "generated";
        if (name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase))
            return "minified";
        if (path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment =>
            segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("dist", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            return "buildOutput";
        return null;
    }
}
