using System.Text;
using System.Text.RegularExpressions;
using PRCockpit.Domain.Analysis;

namespace PRCockpit.Infrastructure.Analysis;

/// <summary>
/// Usages in TypeScript, JavaScript and Vue by identifier text. A language service would not
/// do better on the case that matters most: a function in &lt;script setup&gt; that only the
/// &lt;template&gt; calls, which plain TypeScript never sees. So the match is by name, with a
/// scope that keeps namesakes down — a symbol that is not exported is looked for in its own
/// file only, an exported one also in the files that import from its module.
///
/// ponytail: declarations come from line patterns, not a parser. Ceiling: class methods,
/// object-literal members and destructured bindings get no count, and a same-named property
/// in scope is counted as a usage — which is why the UI marks these counts with "~".
/// </summary>
public static partial class NameUsages
{
    // Functions at any depth (composables declare theirs inside the use… function), other
    // bindings only at the top level or when they hold a function, so a local counter does
    // not get a count of its own.
    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<export>export[ \t]+(?:default[ \t]+)?)?(?:declare[ \t]+)?(?:abstract[ \t]+)?(?:async[ \t]+)?(?<kind>function(?:[ \t]*\*[ \t]*|[ \t]+)|(?:const|let|var|class|interface|type|enum)[ \t]+)(?<name>[A-Za-z_$][\w$]*)(?<rest>[^\n]*)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^\s*(?::[^=]*)?=\s*(?:async\s+)?(?:function\b|\([^)]*\)\s*(?::[^=]*)?=>|[A-Za-z_$][\w$]*\s*=>)", RegexOptions.CultureInvariant)]
    private static partial Regex FunctionInitializer();

    [GeneratedRegex(@"\bexport\s*\{(?<names>[^}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex ExportList();

    [GeneratedRegex(@"\bexport\s+default\s+(?<name>[A-Za-z_$][\w$]*)\s*;?\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ExportDefault();

    [GeneratedRegex(@"(?:\bfrom\s*|\bimport\s*\(\s*)['""](?<spec>[^'""]+)['""]", RegexOptions.CultureInvariant)]
    private static partial Regex ImportSpecifier();

    [GeneratedRegex(@"<script\b[^>]*>(?<body>[\s\S]*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptBlock();

    private static readonly HashSet<string> Keywords =
        ["enum", "function", "class", "interface", "type", "const", "let", "var", "async", "await", "new", "of", "in"];

    public static CodeUsagesResponse Find(SourceSnapshot snapshot, string path)
    {
        if (!snapshot.Files.TryGetValue(path, out var text)) return new CodeUsagesResponse("name", [], snapshot.SkippedFiles);

        var code = Mask(path, text);
        var lines = new LineIndex(code);
        var found = Declarations(path, code).ToArray();
        var exported = ExportedNames(code);
        var module = ModuleName(path);
        // The importers are the same for every declaration of the file, so they are found once.
        var importers = snapshot.Files
            .Where(file => file.Key != path && SourceFiles.IsNameMatched(file.Key))
            .Select(file => (file.Key, Code: Mask(file.Key, file.Value)))
            .Where(file => ImportSpecifier().Matches(file.Code)
                .Any(match => SpecifierModule(match.Groups["spec"].Value) == module))
            .ToArray();
        var declarationsAt = found.Select(item => (item.Name, item.Index)).ToHashSet();

        var declarations = found.Select(item =>
        {
            var scope = item.Exported || exported.Contains(item.Name)
                ? importers.Prepend((path, code))
                : [(path, code)];
            var usages = scope
                .SelectMany(file => Occurrences(file.Item1, file.Item2, item.Name)
                    .Where(hit => file.Item1 != path || !declarationsAt.Contains((item.Name, hit.Index)))
                    .Select(hit => hit.Location))
                .ToArray();
            var at = lines.Position(item.Index);
            return new CodeDeclaration(at.Line, at.Column, at.Column + item.Name.Length, item.Name, usages);
        }).ToArray();
        return new CodeUsagesResponse("name", declarations, snapshot.SkippedFiles);
    }

    private sealed record Found(string Name, int Index, bool Exported);

    private static IEnumerable<Found> Declarations(string path, string code)
    {
        foreach (Match match in Declaration().Matches(code))
        {
            if (path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase) && !InsideScript(code, match.Index)) continue;
            var name = match.Groups["name"].Value;
            if (Keywords.Contains(name)) continue;
            var kind = match.Groups["kind"].Value.Trim();
            var isBinding = kind is "const" or "let" or "var";
            if (isBinding && match.Groups["indent"].Length > 0 && !FunctionInitializer().IsMatch(match.Groups["rest"].Value))
                continue;
            yield return new Found(name, match.Groups["name"].Index, match.Groups["export"].Success);
        }
    }

    private static HashSet<string> ExportedNames(string code)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in ExportList().Matches(code))
            foreach (var part in match.Groups["names"].Value.Split(','))
            {
                var name = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (name is not null) names.Add(name);
            }
        foreach (Match match in ExportDefault().Matches(code)) names.Add(match.Groups["name"].Value);
        return names;
    }

    private static IEnumerable<(int Index, CodeLocation Location)> Occurrences(string path, string code, string name)
    {
        var pattern = new Regex($@"(?<![\w$]){Regex.Escape(name)}(?![\w$])", RegexOptions.CultureInvariant);
        var matches = pattern.Matches(code);
        if (matches.Count == 0) yield break;
        var lines = new LineIndex(code);
        foreach (Match match in matches)
        {
            var at = lines.Position(match.Index);
            yield return (match.Index, new CodeLocation(path, at.Line, at.Column, at.Column + name.Length));
        }
    }

    /// <summary>"api" for ./api, ./api.ts or ./api/index.ts — what an import names the module by.</summary>
    private static string ModuleName(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var name = StripExtension(segments[^1]);
        return name == "index" && segments.Length > 1 ? segments[^2] : name;
    }

    private static string SpecifierModule(string specifier)
    {
        var segments = specifier.Split('?')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return "";
        var name = StripExtension(segments[^1]);
        return name == "index" && segments.Length > 1 ? segments[^2] : name;
    }

    private static string StripExtension(string name)
    {
        foreach (var extension in new[] { ".d.ts", ".ts", ".tsx", ".js", ".mjs", ".vue" })
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return name[..^extension.Length];
        return name;
    }

    private static bool InsideScript(string code, int index) =>
        ScriptBlock().Matches(code).Any(block =>
            index >= block.Groups["body"].Index && index < block.Groups["body"].Index + block.Groups["body"].Length);

    /// <summary>1-based line and column of an offset, by binary search over line starts.</summary>
    private sealed class LineIndex
    {
        private readonly List<int> starts = [0];

        public LineIndex(string text)
        {
            for (var i = 0; i < text.Length; i++)
                if (text[i] == '\n') starts.Add(i + 1);
        }

        public (int Line, int Column) Position(int index)
        {
            var found = starts.BinarySearch(index);
            var line = found >= 0 ? found : ~found - 1;
            return (line + 1, index - starts[line] + 1);
        }
    }

    /// <summary>
    /// The text with comments blanked out, same length and same line breaks, so positions
    /// stay true. Strings are kept: in a template, @click="save" is exactly the usage we want.
    /// In a .vue file the script blocks are scanned as code and the rest only loses its
    /// HTML comments — an apostrophe in template prose must not open a string.
    /// </summary>
    internal static string Mask(string path, string text)
    {
        var output = new StringBuilder(text);
        if (!path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase))
        {
            MaskScript(text, output, 0, text.Length);
            return output.ToString();
        }

        var covered = 0;
        foreach (Match block in ScriptBlock().Matches(text))
        {
            MaskHtmlComments(text, output, covered, block.Index);
            var body = block.Groups["body"];
            MaskScript(text, output, body.Index, body.Index + body.Length);
            covered = block.Index + block.Length;
        }
        MaskHtmlComments(text, output, covered, text.Length);
        return output.ToString();
    }

    private static void MaskScript(string text, StringBuilder output, int start, int end)
    {
        var i = start;
        while (i < end)
        {
            var c = text[i];
            var next = i + 1 < end ? text[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < end && text[i] != '\n') Blank(output, i++);
            }
            else if (c == '/' && next == '*')
            {
                var close = text.IndexOf("*/", i + 2, end - i - 2, StringComparison.Ordinal);
                var stop = close < 0 ? end : close + 2;
                while (i < stop) Blank(output, i++);
            }
            else if (c is '\'' or '"' or '`')
            {
                i++;
                while (i < end && text[i] != c && (c == '`' || text[i] != '\n'))
                    i += text[i] == '\\' ? 2 : 1;
                i++;
            }
            else i++;
        }
    }

    private static void MaskHtmlComments(string text, StringBuilder output, int start, int end)
    {
        var i = start;
        while (i < end)
        {
            var open = text.IndexOf("<!--", i, end - i, StringComparison.Ordinal);
            if (open < 0) return;
            var close = text.IndexOf("-->", open + 4, end - open - 4, StringComparison.Ordinal);
            var stop = close < 0 ? end : close + 3;
            for (var j = open; j < stop; j++) Blank(output, j);
            i = stop;
        }
    }

    private static void Blank(StringBuilder output, int index)
    {
        if (index < output.Length && output[index] != '\n' && output[index] != '\r') output[index] = ' ';
    }
}
