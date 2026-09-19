namespace PRCockpit.Api.Persistence.Entities;

/// <summary>
/// The user-chosen order of key files, stored as one JSON array. Ordered rows would need a
/// transaction to reorder, and this application uses none.
/// </summary>
public sealed class PrReadingPathRow : PullRequestScopedRow
{
    public string PathsJson { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
