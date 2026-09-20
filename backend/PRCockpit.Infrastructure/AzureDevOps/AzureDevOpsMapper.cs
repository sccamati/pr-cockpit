using System.Text.Json;
using PRCockpit.Domain.PullRequests;

namespace PRCockpit.Infrastructure.AzureDevOps;

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
        value.TryGetProperty("originalPath", out var originalPath) ? originalPath.GetString() : null,
        // Tolerant on purpose: a delete has no blob, and the HTTP fakes in the tests omit it.
        value.GetProperty("item").TryGetProperty("objectId", out var objectId) ? objectId.GetString() : null);

    /// <summary>
    /// Deliberately different from every other mapper here: strict on `id` and tolerant on
    /// everything else. Azure DevOps emits system threads with no `status` and soft-deleted
    /// comments with no `content`, and one of those inside a list must not turn the whole
    /// list into a 500.
    /// </summary>
    public static PrCommentThread CommentThread(JsonElement value)
    {
        var comments = value.TryGetProperty("comments", out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
                .Where(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                .Select(Comment).ToArray()
            : [];

        string? filePath = null;
        int? rightLine = null;
        int? leftLine = null;
        if (value.TryGetProperty("threadContext", out var context) && context.ValueKind == JsonValueKind.Object)
        {
            filePath = OptionalString(context, "filePath");
            rightLine = Line(context, "rightFileStart");
            leftLine = Line(context, "leftFileStart");
        }

        // A thread whose every comment is a system comment is Azure DevOps talking to
        // itself. An empty thread counts as system too: there is nothing to read in it.
        var isSystem = comments.Length == 0 ||
            comments.All(comment => string.Equals(comment.CommentType, "system", StringComparison.OrdinalIgnoreCase));

        // Azure DevOps nests this one deep, and it is absent on a thread that is not tied
        // to a diff at all, so every hop is checked.
        int? iteration = null;
        if (value.TryGetProperty("pullRequestThreadContext", out var prContext) &&
            prContext.ValueKind == JsonValueKind.Object &&
            prContext.TryGetProperty("iterationContext", out var iterationContext) &&
            iterationContext.ValueKind == JsonValueKind.Object)
        {
            iteration = OptionalInt(iterationContext, "secondComparingIteration") ??
                OptionalInt(iterationContext, "firstComparingIteration");
        }

        return new PrCommentThread(RequiredInt(value, "id"), OptionalString(value, "status"),
            filePath, rightLine, leftLine, isSystem, comments, iteration);
    }

    private static PrComment Comment(JsonElement value) => new(
        RequiredInt(value, "id"),
        value.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object
            ? OptionalString(author, "displayName") : null,
        OptionalString(value, "content"),
        OptionalString(value, "commentType"),
        value.TryGetProperty("publishedDate", out var date) && date.ValueKind == JsonValueKind.String
            ? date.GetDateTimeOffset() : null);

    private static int? Line(JsonElement context, string name) =>
        context.TryGetProperty(name, out var position) && position.ValueKind == JsonValueKind.Object &&
        position.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Number
            ? line.GetInt32() : null;

    private static int? OptionalInt(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32() : null;

    private static string? OptionalString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;

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
