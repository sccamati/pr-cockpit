using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Application.PullRequests;

public static class PrContextBuilder
{
    public static async Task<PrContext> BuildAsync(
        PullRequestDetails details,
        Func<string, CancellationToken, Task<FileDiff>> getDiff,
        ContextBudget budget,
        CancellationToken ct,
        string? onlyPath = null)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(getDiff);
        ArgumentNullException.ThrowIfNull(budget);
        if (budget.MaxDiffCharactersPerFile <= 0 || budget.MaxDiffCharactersPerPullRequest <= 0)
            throw new ArgumentOutOfRangeException(nameof(budget), "Diff character limits must be positive.");

        var files = new List<ContextChangedFile>(details.ChangedFilesCount);
        var used = 0;
        // onlyPath narrows the package to a single file for the per-file explanation. The
        // budget rules stay exactly the same, so one file cannot be handled more loosely
        // than the same file inside a whole-PR package.
        foreach (var change in details.ChangedFiles.Where(file => onlyPath is null || file.Path == onlyPath))
        {
            ct.ThrowIfCancellationRequested();
            var reason = FileCategory.Of(change.Path) ??
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
}
