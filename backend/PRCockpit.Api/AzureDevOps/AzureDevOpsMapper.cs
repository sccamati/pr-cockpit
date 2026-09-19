using System.Text.Json;

namespace PRCockpit.Api.AzureDevOps;

public static class AzureDevOpsMapper
{
    public static Project Project(JsonElement value) => new(
        RequiredString(value, "id"), RequiredString(value, "name"));

    public static Repository Repository(JsonElement value) => new(
        RequiredString(value, "id"), RequiredString(value, "name"));

    public static PullRequestSummary Summary(JsonElement value) => new(
        RequiredInt(value, "pullRequestId"),
        RequiredString(value, "title"),
        RequiredString(value.GetProperty("createdBy"), "displayName"),
        RequiredString(value.GetProperty("repository"), "name"),
        RequiredString(value, "status"),
        RequiredDate(value, "creationDate"));

    public static PullRequestDetails Details(
        JsonElement value, IReadOnlyList<ChangedFile> changedFiles, IReadOnlyList<Commit> commits,
        IReadOnlyList<WorkItem> workItems)
    {
        var reviewers = value.TryGetProperty("reviewers", out var array)
            ? array.EnumerateArray().Select(item => new Reviewer(
                RequiredString(item, "displayName"), RequiredInt(item, "vote"))).ToArray()
            : [];

        return new PullRequestDetails(
            RequiredInt(value, "pullRequestId"),
            RequiredString(value, "title"),
            value.TryGetProperty("description", out var description) ? description.GetString() : null,
            RequiredString(value.GetProperty("createdBy"), "displayName"),
            RequiredString(value.GetProperty("repository"), "name"),
            Branch(RequiredString(value, "sourceRefName")),
            Branch(RequiredString(value, "targetRefName")),
            RequiredString(value, "status"),
            RequiredDate(value, "creationDate"),
            reviewers,
            changedFiles.Count,
            changedFiles,
            commits.Count,
            commits,
            workItems);
    }

    public static ChangedFile ChangedFile(JsonElement value) => new(
        RequiredString(value.GetProperty("item"), "path"),
        RequiredString(value, "changeType"),
        value.TryGetProperty("originalPath", out var originalPath) ? originalPath.GetString() : null);

    public static WorkItem WorkItem(JsonElement value) => new(
        RequiredString(value, "id"), RequiredString(value, "url"));

    public static Commit Commit(JsonElement value)
    {
        var author = value.GetProperty("author");
        return new Commit(
            RequiredString(value, "commitId"),
            RequiredString(value, "comment"),
            RequiredString(author, "name"),
            author.TryGetProperty("date", out var date) && date.ValueKind == JsonValueKind.String
                ? date.GetDateTimeOffset() : null);
    }

    private static string Branch(string value) => value.StartsWith("refs/heads/", StringComparison.Ordinal)
        ? value["refs/heads/".Length..]
        : value;

    private static string RequiredString(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static int RequiredInt(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static DateTimeOffset RequiredDate(JsonElement value, string name) => value.GetProperty(name).GetDateTimeOffset();
}
