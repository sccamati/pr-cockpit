using System.Text.RegularExpressions;

namespace PRCockpit.Domain.PullRequests;

/// <summary>
/// Files a reviewer almost never reads: lockfiles, snapshots, generated and minified
/// output. One rule serves two callers — the AI context drops their text, and the file
/// tree folds them into a collapsed "noise" group. Null means ordinary code.
/// </summary>
public static class FileCategory
{
    // The same two rules as isTestPath in frontend/src/lib/format.ts, so "a test" means one
    // thing on both sides: tests/, __tests__/, Billing.Tests/, UnitTests/, App.test.ts,
    // x.spec.js, InvoiceTests.cs — but not Contest.cs or latest/.
    private static readonly Regex[] TestPaths =
    [
        new(@"(^|/)(__)?tests?(__)?/|[.\-_]tests?/|\.(test|spec)\.[cm]?[jt]sx?$", RegexOptions.IgnoreCase),
        new(@"Tests?(/|\.cs$)")
    ];

    public static bool IsTest(string path) => TestPaths.Any(pattern => pattern.IsMatch(path));

    /// <summary>
    /// Which files get a context budget first when it cannot hold them all: the code that is
    /// the change, then the notes that explain it, then the tests that check it. A large pull
    /// request otherwise spends the budget on whatever Azure DevOps happened to list first.
    /// </summary>
    public static int ReadPriority(string path) =>
        IsTest(path) ? 2 : path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

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
