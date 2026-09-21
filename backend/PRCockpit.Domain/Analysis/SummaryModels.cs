namespace PRCockpit.Domain.Analysis;

/// <summary>A file the model says is worth reading first, and why. A proposal, never a fact.</summary>
public record CriticalFile(string Path, string Role, string Why);

/// <summary>What an analyzer returns, before validation.</summary>
public record SummaryDraft(
    int SchemaVersion, IReadOnlyList<string> Sentences, IReadOnlyList<CriticalFile>? CriticalFiles = null,
    IReadOnlyList<string>? ReadingOrder = null);

public record OmittedContextFile(string Path, string Reason);

public record SummaryContextReport(
    int ChangedFiles, int IncludedFiles, int IncludedDiffCharacters,
    bool WasLimited, IReadOnlyList<OmittedContextFile> OmittedFiles);

/// <summary>
/// <c>CriticalFiles</c> is the shortlist and <c>ReadingOrder</c> is the whole pull request:
/// every changed file exactly once, in the order that tells the story of the change, noise
/// last. Empty on a Summary generated before the field existed — the browser offers to
/// recompute rather than pretending an order it does not have.
/// </summary>
public record SummaryResponse(
    int SchemaVersion, string Summary, string? BaseCommitSha, string? HeadCommitSha,
    SummaryContextReport ContextReport, IReadOnlyList<string> Sentences,
    IReadOnlyList<CriticalFile> CriticalFiles, IReadOnlyList<string> ReadingOrder);

public record StoredSummary(SummaryResponse Result, DateTimeOffset SavedAt);

/// <summary>What one file does and what changed in it. Same draft shape as a Summary.</summary>
public record FileExplanation(
    int SchemaVersion, string Path, string? HeadCommitSha, IReadOnlyList<string> Sentences);

public record StoredFileExplanation(FileExplanation Result, DateTimeOffset SavedAt);

/// <summary>The request body of the per-file explanation endpoint.</summary>
public record FileExplanationRequest(string? Path);

/// <summary>
/// The request body of the per-file question endpoint. The two limits are constants rather
/// than literals in the service because the browser enforces the same ones.
/// </summary>
public record FileQuestionRequest(string? Path, string? Question, string? Selection)
{
    public const int MaxQuestionLength = 1_000;
    public const int MaxSelectionLength = 4_000;
}

/// <summary>
/// One turn of the conversation about one file: what was asked, what came back, and which
/// version of the file it was about. Stored whole as JSON like a Summary, so it is
/// re-validated on read rather than trusted.
/// </summary>
public record FileQuestionTurn(
    int SchemaVersion, string Path, string Question, string? Selection,
    IReadOnlyList<string> Sentences, string? BlobId, string? HeadCommitSha, DateTimeOffset AskedAt);

/// <summary>
/// What the ask task needs on top of the one-file context. The history is assembled from
/// what we stored — the client never sends it — so a forged "you previously said" cannot be
/// slipped into the prompt.
/// </summary>
public record FileQuestion(
    string Question, string? Selection, string? BlobId, IReadOnlyList<FileQuestionTurn> History);

public sealed class SummaryAnalysisException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
