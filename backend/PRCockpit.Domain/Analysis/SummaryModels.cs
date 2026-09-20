namespace PRCockpit.Domain.Analysis;

/// <summary>What an analyzer returns, before validation.</summary>
public record SummaryDraft(int SchemaVersion, IReadOnlyList<string> Sentences);

public record OmittedContextFile(string Path, string Reason);

public record SummaryContextReport(
    int ChangedFiles, int IncludedFiles, int IncludedDiffCharacters,
    bool WasLimited, IReadOnlyList<OmittedContextFile> OmittedFiles);

public record SummaryResponse(
    int SchemaVersion, string Summary, string? BaseCommitSha, string? HeadCommitSha,
    SummaryContextReport ContextReport, IReadOnlyList<string> Sentences);

public record StoredSummary(SummaryResponse Result, DateTimeOffset SavedAt);

public sealed class SummaryAnalysisException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
