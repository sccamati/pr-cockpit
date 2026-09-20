namespace PRCockpit.Domain.Analysis;

public sealed record CSharpHoverRequest(string OriginalText, string ModifiedText);

public sealed record CSharpHoverEntry(
    int StartLine, int StartColumn, int EndLine, int EndColumn, string Signature);

public sealed record CSharpSemanticToken(
    int Line, int StartColumn, int EndColumn, string Kind);

public sealed record CSharpHoverResponse(
    IReadOnlyList<CSharpHoverEntry> Original, IReadOnlyList<CSharpHoverEntry> Modified,
    IReadOnlyList<CSharpSemanticToken> OriginalTokens, IReadOnlyList<CSharpSemanticToken> ModifiedTokens);
