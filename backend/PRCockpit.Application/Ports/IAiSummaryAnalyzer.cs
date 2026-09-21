using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Application.Ports;

/// <summary>
/// The replaceable AI runtime. It receives a prepared, bounded context and returns a
/// draft; validation is the application's job, never the adapter's.
/// </summary>
public interface IAiSummaryAnalyzer
{
    Task<SummaryDraft> AnalyzeAsync(PrContext context, CancellationToken ct);

    /// <summary>
    /// The second task of the same adapter: a context narrowed to one file. Deliberately
    /// not a separate port — it is the same executable, the same contract and the same
    /// configuration, so a second interface would buy nothing but another registration.
    /// </summary>
    Task<SummaryDraft> ExplainFileAsync(PrContext context, CancellationToken ct);

    /// <summary>
    /// The third task, for the reason the second one gives: same executable, same contract,
    /// same configuration. The question, the snippet and the previous turns ride inside the
    /// request's context — never folded into the instruction, which stays fixed text.
    /// </summary>
    Task<SummaryDraft> AskFileAsync(PrContext context, FileQuestion question, CancellationToken ct);
}
