namespace PRCockpit.Domain.Analysis;

/// <summary>
/// The repository's source files at one commit, keyed by the same "/"-rooted path Azure
/// DevOps uses for pull request changes. <see cref="SkippedFiles"/> counts sources that were
/// left out whole (too large, binary), so a count of zero can say it may be incomplete.
/// </summary>
public sealed record SourceSnapshot(string CommitSha, IReadOnlyDictionary<string, string> Files, int SkippedFiles);

/// <summary>
/// Where each declaration of one file is used. Mode is "semantic" when a compiler resolved
/// the symbols (C#) and "name" when the match is by identifier text (TypeScript, JavaScript,
/// Vue) — the second can count a namesake, and the UI says so.
/// </summary>
public sealed record CodeUsagesResponse(
    string Mode, IReadOnlyList<CodeDeclaration> Declarations, int SkippedFiles);

public sealed record CodeDeclaration(
    int Line, int StartColumn, int EndColumn, string Name, IReadOnlyList<CodeLocation> Usages);

public sealed record CodeLocation(string Path, int Line, int StartColumn, int EndColumn);

/// <summary>
/// Who uses one changed file, handed to the per-file AI tasks: what calls a function says
/// what it is for. Every declaration with its usage count (zero included — nobody calls it
/// yet), and a few usages as code. An excerpt is sent whole or left out and counted in
/// <see cref="OmittedExcerpts"/>, never cut.
/// </summary>
public sealed record UsageContext(
    string Mode, IReadOnlyList<UsageCount> Declarations, IReadOnlyList<UsageExcerpt> Excerpts, int OmittedExcerpts);

public sealed record UsageCount(string Name, int Line, int Usages);

/// <summary>A few lines around one usage. InPullRequest: the using file is changed here too.</summary>
public sealed record UsageExcerpt(string Name, string Path, int Line, int StartLine, bool InPullRequest, string Text);

public sealed record CodeUsagesRequest(string? Path);

public sealed record CodeSourceRequest(string? Path);

public sealed record CodeSource(string Path, string Text);

/// <summary>Which files take part in usages, decided once for the fetch and the analysis.</summary>
public static class SourceFiles
{
    private static readonly string[] NameExtensions = [".ts", ".tsx", ".js", ".mjs", ".vue"];
    private static readonly string[] SkippedDirectories = ["bin", "obj", "node_modules", "dist", ".git", ".vs"];

    public static bool IsCSharp(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    public static bool IsNameMatched(string path) =>
        NameExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) &&
        !path.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase);

    public static bool IsSource(string path) =>
        (IsCSharp(path) || IsNameMatched(path)) &&
        !path.Split('/').Any(segment => SkippedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
}
