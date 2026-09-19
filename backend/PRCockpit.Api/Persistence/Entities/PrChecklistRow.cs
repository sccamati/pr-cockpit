namespace PRCockpit.Api.Persistence.Entities;

/// <summary>The six manual review steps. Ready is never set automatically.</summary>
public sealed class PrChecklistRow : PullRequestScopedRow
{
    public bool AiReview { get; set; }
    public bool Quality { get; set; }
    public bool Understand { get; set; }
    public bool Architecture { get; set; }
    public bool Debug { get; set; }
    public bool Ready { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
