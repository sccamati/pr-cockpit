using PRCockpit.Application.Ports;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Application.Analysis;

/// <summary>
/// "Where is this used?" for the file open in the diff — the reviewer's cue that a method
/// nobody calls yet is scaffolding, not finished logic. Answers from the repository at the
/// pull request's head commit, so a call added in the same pull request counts.
/// </summary>
public sealed class CodeUsageService(
    IAzureDevOpsClient client, ISourceSnapshotCache snapshots, ICodeUsageFinder finder)
{
    public async Task<CodeUsagesResponse> FindAsync(
        string project, string repositoryId, int pullRequestId, string path, CancellationToken ct)
    {
        if (!SourceFiles.IsSource(path))
            throw new AzureDevOpsException("Usages are available for C#, TypeScript, JavaScript and Vue files.", 400);

        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        // The same allowlist the diff uses: only a file this pull request changed, and only
        // one that still exists at its head.
        var changed = details.ChangedFiles.FirstOrDefault(file => file.Path == path);
        if (changed is null)
            throw new AzureDevOpsException("File is no longer in this pull request.", 404);
        if (changed.ChangeType.Contains("delete", StringComparison.OrdinalIgnoreCase))
            throw new AzureDevOpsException("This file is deleted in the pull request.", 404);

        var snapshot = await SnapshotAsync(project, repositoryId, details, ct);
        return await finder.FindAsync(snapshot, path, ct);
    }

    /// <summary>
    /// The text of one file that a usage points into, for the preview. Any file of the
    /// snapshot may be read — that is the point, most usages sit outside the pull request —
    /// and nothing outside it can be.
    /// </summary>
    public async Task<CodeSource> GetSourceAsync(
        string project, string repositoryId, int pullRequestId, string path, CancellationToken ct)
    {
        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        var snapshot = await SnapshotAsync(project, repositoryId, details, ct);
        return snapshot.Files.TryGetValue(path, out var text)
            ? new CodeSource(path, text)
            : throw new AzureDevOpsException("File is not among the repository's sources.", 404);
    }

    // Three callers say what a function is for; a fourth rarely adds anything. Six lines
    // either side is the call and why it is made, not the caller's whole method.
    public const int ExcerptsPerDeclaration = 3;
    public const int ExcerptRadius = 6;
    public const int MaxExcerptCharacters = 2_000;
    public const int MaxUsageCharacters = 15_000;

    /// <summary>
    /// The usages of this file as the explain and ask tasks see them. Null for a file usages
    /// do not exist for (another language, a deleted file) and null when the repository cannot
    /// be read: the answer is still about the file, only without its callers, which the
    /// instruction tells the model not to guess.
    /// </summary>
    public async Task<UsageContext?> ForAiAsync(
        string project, string repositoryId, PullRequestDetails details, string path, CancellationToken ct)
    {
        var changed = details.ChangedFiles.FirstOrDefault(file => file.Path == path);
        if (changed is null || !SourceFiles.IsSource(path) ||
            changed.ChangeType.Contains("delete", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var snapshot = await SnapshotAsync(project, repositoryId, details, ct);
            var usages = await finder.FindAsync(snapshot, path, ct);
            return Excerpts(usages, snapshot, path,
                details.ChangedFiles.Select(file => file.Path).ToHashSet(StringComparer.Ordinal));
        }
        catch (AzureDevOpsException)
        {
            return null;
        }
    }

    /// <summary>
    /// Round-robin over the declarations — everyone's first usage before anyone's second — so
    /// a file with twenty methods does not spend the budget on the first one. Within one
    /// declaration, usages in files this pull request also changed go first: they are the
    /// other half of the change. Usages inside the file itself are skipped, the file is
    /// already in the context.
    /// ponytail: two usages a few lines apart get two overlapping excerpts. Ceiling: some
    /// repeated lines inside a budget that stays bounded; merge ranges if it crowds out callers.
    /// </summary>
    public static UsageContext Excerpts(
        CodeUsagesResponse usages, SourceSnapshot snapshot, string path, IReadOnlySet<string> changedPaths)
    {
        var candidates = usages.Declarations
            .SelectMany(declaration => declaration.Usages
                .Where(usage => usage.Path != path)
                .DistinctBy(usage => (usage.Path, usage.Line))
                .OrderByDescending(usage => changedPaths.Contains(usage.Path))
                .Take(ExcerptsPerDeclaration)
                .Select((usage, rank) => (declaration.Name, Usage: usage, Rank: rank)))
            .OrderBy(item => item.Rank);

        var lines = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var shown = new HashSet<(string, int)>();
        var excerpts = new List<UsageExcerpt>();
        var used = 0;
        var omitted = 0;
        foreach (var (name, usage, _) in candidates)
        {
            // One line that uses two declarations (new Job().Run()) is shown once.
            if (!shown.Add((usage.Path, usage.Line))) continue;
            if (!snapshot.Files.TryGetValue(usage.Path, out var source)) { omitted++; continue; }
            if (!lines.TryGetValue(usage.Path, out var split))
                lines[usage.Path] = split = source.Split('\n');
            if (usage.Line < 1 || usage.Line > split.Length) { omitted++; continue; }
            var start = Math.Max(1, usage.Line - ExcerptRadius);
            var end = Math.Min(split.Length, usage.Line + ExcerptRadius);
            var text = string.Join("\n", split[(start - 1)..end].Select(line => line.TrimEnd('\r')));
            if (text.Length > MaxExcerptCharacters || used + text.Length > MaxUsageCharacters) { omitted++; continue; }
            used += text.Length;
            excerpts.Add(new UsageExcerpt(name, usage.Path, usage.Line, start, changedPaths.Contains(usage.Path), text));
        }

        return new UsageContext(usages.Mode,
            [.. usages.Declarations.Select(declaration =>
                new UsageCount(declaration.Name, declaration.Line, declaration.Usages.Count))],
            excerpts, omitted);
    }

    private Task<SourceSnapshot> SnapshotAsync(
        string project, string repositoryId, PullRequestDetails details, CancellationToken ct)
    {
        var head = details.HeadCommitSha
            ?? throw new AzureDevOpsException("Azure DevOps did not provide the pull request commit IDs.", 502);
        return snapshots.GetOrAddAsync($"{project}/{repositoryId}/{head}",
            token => client.GetSourceSnapshotAsync(project, repositoryId, head, token), ct);
    }
}
