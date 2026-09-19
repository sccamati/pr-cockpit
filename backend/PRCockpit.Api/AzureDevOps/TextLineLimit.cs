namespace PRCockpit.Api.AzureDevOps;

internal static class TextLineLimit
{
    private const int MaxLines = 4000;

    public static bool Exceeded(string original, string modified) =>
        Count(original) + Count(modified) > MaxLines;

    private static int Count(string text)
    {
        if (text.Length == 0) return 0;
        var lines = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            if (i + 1 < text.Length) lines++;
        }
        return lines;
    }
}
