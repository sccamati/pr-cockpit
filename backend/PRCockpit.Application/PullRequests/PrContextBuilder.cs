using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Application.PullRequests;

public static class PrContextBuilder
{
    public static async Task<PrContext> BuildAsync(
        PullRequestDetails details,
        Func<string, CancellationToken, Task<FileDiff>> getDiff,
        ContextBudget budget,
        CancellationToken ct,
        IReadOnlyCollection<string>? onlyPaths = null,
        IReadOnlySet<string>? readFirst = null)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(getDiff);
        ArgumentNullException.ThrowIfNull(budget);
        if (budget.MaxDiffCharactersPerFile <= 0 || budget.MaxDiffCharactersPerPullRequest <= 0)
            throw new ArgumentOutOfRangeException(nameof(budget), "Diff character limits must be positive.");

        var used = 0;
        // onlyPaths narrows the package to the files a task is about — one for explain and ask,
        // a quiz question's few for grading. The rules stay the same — whole or nothing — and
        // only the budget the caller passes differs.
        var selected = details.ChangedFiles.Where(file => onlyPaths is null || onlyPaths.Contains(file.Path)).ToList();
        var decided = new Dictionary<string, ContextChangedFile>(StringComparer.Ordinal);
        // The budget is handed out to readFirst (the files the reviewer actually read), then code,
        // then notes, then tests (FileCategory.ReadPriority) — a stable sort, so files of one kind
        // keep the pull request's order. The package itself still lists them in that order; only
        // who gets the budget changes.
        foreach (var change in selected
            .OrderBy(file => readFirst?.Contains(file.Path) == true ? 0 : 1)
            .ThenBy(file => FileCategory.ReadPriority(file.Path)))
        {
            ct.ThrowIfCancellationRequested();
            var reason = FileCategory.Of(change.Path) ??
                (used >= budget.MaxDiffCharactersPerPullRequest ? "pullRequestCharacterLimit" : null);
            if (reason is not null)
            {
                decided[change.Path] = Omitted(change, reason);
                continue;
            }

            var diff = await getDiff(change.Path, ct);
            if (diff.Kind != "text")
            {
                decided[change.Path] = Omitted(change, diff.Kind switch
                {
                    "binary" => "binary",
                    "tooLarge" => "sourceTooLarge",
                    _ => throw new InvalidOperationException($"Unexpected diff kind: {diff.Kind}")
                });
                continue;
            }

            var original = diff.OriginalText ?? throw new InvalidOperationException("Text diff has no original text.");
            var modified = diff.ModifiedText ?? throw new InvalidOperationException("Text diff has no modified text.");
            var length = (long)original.Length + modified.Length;
            reason = length > budget.MaxDiffCharactersPerFile ? "fileCharacterLimit" :
                length > budget.MaxDiffCharactersPerPullRequest - used ? "pullRequestCharacterLimit" : null;
            if (reason is not null)
            {
                decided[change.Path] = Omitted(change, reason);
                continue;
            }

            used += (int)length;
            decided[change.Path] = new ContextChangedFile(change.Path, change.ChangeType, change.OriginalPath,
                original, modified, null);
        }
        var files = selected.Select(change => decided[change.Path]).ToList();

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
}
