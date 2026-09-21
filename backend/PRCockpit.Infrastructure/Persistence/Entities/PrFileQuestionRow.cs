namespace PRCockpit.Infrastructure.Persistence.Entities;

/// <summary>
/// One turn of the conversation about one file. This is the first table here that appends
/// rather than replaces: a question is a new fact, not a correction of the last one, and
/// the history is the feature. That is also why it is the first with a surrogate key — the
/// natural key would have to include the clock, and two rows in the same tick would be a
/// primary key violation instead of a second question. Which version of the file a turn is
/// about lives inside the JSON; nothing queries it, so it is not a column.
/// </summary>
public sealed class PrFileQuestionRow : PullRequestScopedRow
{
    public int Id { get; set; }
    public string FilePath { get; set; } = "";
    public DateTimeOffset AskedAt { get; set; }
    public string TurnJson { get; set; } = "";
}
