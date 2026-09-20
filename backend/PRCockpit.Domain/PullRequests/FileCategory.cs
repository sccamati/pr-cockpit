namespace PRCockpit.Domain.PullRequests;

/// <summary>
/// Files a reviewer almost never reads: lockfiles, snapshots, generated and minified
/// output. One rule serves two callers — the AI context drops their text, and the file
/// tree folds them into a collapsed "noise" group. Null means ordinary code.
/// </summary>
public static class FileCategory
{
    public static string? Of(string path)
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
