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
}
