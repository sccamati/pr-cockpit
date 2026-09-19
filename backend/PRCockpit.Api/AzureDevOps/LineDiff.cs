namespace PRCockpit.Api.AzureDevOps;

internal static class LineDiff
{
    public const int MaxLines = 4000;
    private sealed record TextLine(string Text, bool HasNewline);

    public static IReadOnlyList<DiffLine>? Compare(string oldText, string newText)
    {
        var before = SplitLines(oldText);
        var after = SplitLines(newText);
        if (before.Length + after.Length > MaxLines) return null;

        // The line cap also bounds the largest table to about 4 million cells.
        var width = after.Length + 1;
        var lengths = new int[(before.Length + 1) * width];
        for (var i = before.Length - 1; i >= 0; i--)
            for (var j = after.Length - 1; j >= 0; j--)
                lengths[i * width + j] = before[i] == after[j]
                    ? lengths[(i + 1) * width + j + 1] + 1
                    : Math.Max(lengths[(i + 1) * width + j], lengths[i * width + j + 1]);

        var lines = new List<DiffLine>(before.Length + after.Length);
        var oldIndex = 0;
        var newIndex = 0;
        while (oldIndex < before.Length || newIndex < after.Length)
        {
            if (oldIndex < before.Length && newIndex < after.Length && before[oldIndex] == after[newIndex])
            {
                lines.Add(new DiffLine("context", oldIndex + 1, newIndex + 1,
                    before[oldIndex].Text, before[oldIndex].HasNewline));
                oldIndex++;
                newIndex++;
            }
            else if (oldIndex < before.Length && (newIndex == after.Length ||
                     lengths[(oldIndex + 1) * width + newIndex] >= lengths[oldIndex * width + newIndex + 1]))
            {
                lines.Add(new DiffLine("remove", oldIndex + 1, null,
                    before[oldIndex].Text, before[oldIndex].HasNewline));
                oldIndex++;
            }
            else
            {
                lines.Add(new DiffLine("add", null, newIndex + 1,
                    after[newIndex].Text, after[newIndex].HasNewline));
                newIndex++;
            }
        }
        return lines;
    }

    private static TextLine[] SplitLines(string text)
    {
        if (text.Length == 0) return [];
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var endsWithNewline = normalized.EndsWith('\n');
        if (endsWithNewline) lines = lines[..^1];
        return lines.Select((line, index) => new TextLine(line, index < lines.Length - 1 || endsWithNewline)).ToArray();
    }
}
