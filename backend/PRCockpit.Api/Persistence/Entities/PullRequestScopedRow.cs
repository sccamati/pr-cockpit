namespace PRCockpit.Api.Persistence.Entities;

/// <summary>
/// Every row is keyed by where it came from in Azure DevOps. Nothing stored here mirrors
/// Azure DevOps data itself — only the decisions the reviewer made about it.
/// </summary>
public abstract class PullRequestScopedRow
{
    public string Organization { get; set; } = "";
    public string Project { get; set; } = "";
    public string RepositoryId { get; set; } = "";
    public int PullRequestId { get; set; }
}
