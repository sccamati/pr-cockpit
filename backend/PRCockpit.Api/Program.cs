using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PRCockpit.Application.Analysis;
using PRCockpit.Application.Ports;
using PRCockpit.Application.PullRequests;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;
using PRCockpit.Domain.Review;
using PRCockpit.Infrastructure;
using PRCockpit.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<PullRequestContextService>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddScoped<FileExplanationService>();

var app = builder.Build();

// A single-user local tool: applying migrations on start beats asking the user to run
// `dotnet ef database update` after every pull.
// ponytail: fine for one machine and one process. A shared deployment would need the
// migration to run as its own deliberate step instead.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<PrCockpitContext>().Database.MigrateAsync();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");
const string PullRequests = "/projects/{project}/repositories/{repositoryId}/pull-requests";

// What the frontend has to know before it offers an action it cannot perform. Writing
// comments is off by default, and finding that out from a 503 means finding it out after
// the comment is already typed.
api.MapGet("/config", (IConfiguration configuration) =>
    Results.Ok(new { commentsEnabled = configuration.GetValue("AzureDevOps:AllowComments", false) }));

api.MapGet("/projects", async (IAzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetProjectsAsync(ct)));

api.MapGet("/projects/{project}/repositories", async (
    string project, IAzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetRepositoriesAsync(project, ct)));

api.MapGet(PullRequests, async (
    string project, string repositoryId, IAzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetPullRequestsAsync(project, repositoryId, ct)));

// The literal segments below win over the {pullRequestId:int} routes, the way route
// matching prefers a literal over a constrained parameter.
api.MapGet($"{PullRequests}/checklist-progress", async (
    string project, string repositoryId, IChecklistStore store, CancellationToken ct) =>
    await Execute(() => store.GetProgressAsync(project, repositoryId, ct)));

api.MapGet($"{PullRequests}/file-review-progress", async (
    string project, string repositoryId, IReviewProgressStore store, CancellationToken ct) =>
    await Execute(() => store.GetProgressAsync(project, repositoryId, ct)));

api.MapGet($"{PullRequests}/{{pullRequestId:int}}", async (
    string project, string repositoryId, int pullRequestId, IAzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct)));

api.MapGet($"{PullRequests}/{{pullRequestId:int}}/threads", async (
    string project, string repositoryId, int pullRequestId, IAzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetCommentThreadsAsync(project, repositoryId, pullRequestId, ct)));

api.MapPost($"{PullRequests}/{{pullRequestId:int}}/threads", async (
    string project, string repositoryId, int pullRequestId, NewCommentThread thread,
    IAzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.CreateCommentThreadAsync(project, repositoryId, pullRequestId, thread, ct)));

api.MapPost($"{PullRequests}/{{pullRequestId:int}}/threads/{{threadId:int}}/comments", async (
    string project, string repositoryId, int pullRequestId, int threadId, NewComment comment,
    IAzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.ReplyToThreadAsync(project, repositoryId, pullRequestId, threadId, comment, ct)));

api.MapPatch($"{PullRequests}/{{pullRequestId:int}}/threads/{{threadId:int}}/comments/{{commentId:int}}", async (
    string project, string repositoryId, int pullRequestId, int threadId, int commentId,
    EditComment comment, IAzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.UpdateCommentAsync(project, repositoryId, pullRequestId, threadId, commentId, comment, ct)));

api.MapDelete($"{PullRequests}/{{pullRequestId:int}}/threads/{{threadId:int}}/comments/{{commentId:int}}", async (
    string project, string repositoryId, int pullRequestId, int threadId, int commentId,
    IAzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.DeleteCommentAsync(project, repositoryId, pullRequestId, threadId, commentId, ct)));

api.MapPatch($"{PullRequests}/{{pullRequestId:int}}/threads/{{threadId:int}}", async (
    string project, string repositoryId, int pullRequestId, int threadId, ThreadStatusUpdate update,
    IAzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.SetThreadStatusAsync(project, repositoryId, pullRequestId, threadId, update.Status, ct)));

api.MapGet($"{PullRequests}/{{pullRequestId:int}}/diff", async (
    string project, string repositoryId, int pullRequestId, string path, int? sinceIteration,
    IAzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => sinceIteration is null
        ? client.GetFileDiffAsync(project, repositoryId, pullRequestId, path, ct)
        : client.GetFileDiffSinceIterationAsync(project, repositoryId, pullRequestId, path, sinceIteration.Value, ct)));

api.MapPost("/csharp/hovers", async (CSharpHoverRequest request, ICSharpHoverAnalyzer analyzer) =>
    await Execute(() => Task.FromResult(analyzer.Build(request))));

api.MapGet($"{PullRequests}/{{pullRequestId:int}}/context", async (
    string project, string repositoryId, int pullRequestId,
    PullRequestContextService contexts, CancellationToken ct) =>
    await Execute(() => contexts.BuildAsync(project, repositoryId, pullRequestId, ct)));

api.MapGet($"{PullRequests}/{{pullRequestId:int}}/summary", async (
    string project, string repositoryId, int pullRequestId, SummaryService summaries, CancellationToken ct) =>
    await Execute(async () => new { stored = await summaries.GetSavedAsync(project, repositoryId, pullRequestId, ct) }));

api.MapPost($"{PullRequests}/{{pullRequestId:int}}/summary", async (
    string project, string repositoryId, int pullRequestId, SummaryService summaries, CancellationToken ct) =>
    await Execute(() => summaries.GenerateAsync(project, repositoryId, pullRequestId, ct)));

// The path travels in the body, not the query string: it is user-supplied input that the
// service checks against this pull request's file list before anything else happens.
api.MapPost($"{PullRequests}/{{pullRequestId:int}}/summary/file", async (
    string project, string repositoryId, int pullRequestId, FileExplanationRequest request,
    FileExplanationService explanations, CancellationToken ct) =>
    await Execute(() => explanations.ExplainAsync(project, repositoryId, pullRequestId, request.Path ?? "", ct)));

api.MapGet($"{PullRequests}/{{pullRequestId:int}}/checklist", async (
    string project, string repositoryId, int pullRequestId, IChecklistStore store, CancellationToken ct) =>
    await Execute(() => store.GetAsync(project, repositoryId, pullRequestId, ct)));

// A literal segment beats the {item} parameter in routing, so this stays a separate
// endpoint rather than a seventh name inside the checklist item switch.
api.MapPut($"{PullRequests}/{{pullRequestId:int}}/checklist/debug-note", async (
    string project, string repositoryId, int pullRequestId, DebugNoteUpdate update,
    IChecklistStore store, CancellationToken ct) =>
    await Execute(() => store.SetDebugNoteAsync(project, repositoryId, pullRequestId, update.Note, ct)));

api.MapPut($"{PullRequests}/{{pullRequestId:int}}/checklist/{{item}}", async (
    string project, string repositoryId, int pullRequestId, string item, ChecklistUpdate update,
    IChecklistStore store, CancellationToken ct) =>
    await Execute(() => store.SetAsync(project, repositoryId, pullRequestId, item, update.Completed, ct)));

api.MapGet($"{PullRequests}/{{pullRequestId:int}}/file-reviews", async (
    string project, string repositoryId, int pullRequestId, IReviewProgressStore store, CancellationToken ct) =>
    await Execute(() => store.GetAsync(project, repositoryId, pullRequestId, ct)));

// The file path travels in the body: a route segment cannot carry a slash-laden path safely.
api.MapPut($"{PullRequests}/{{pullRequestId:int}}/file-reviews", async (
    string project, string repositoryId, int pullRequestId, FileReviewUpdate update,
    IReviewProgressStore store, CancellationToken ct) =>
    await Execute(() => store.SetFileAsync(project, repositoryId, pullRequestId, update, ct)));

api.MapPut($"{PullRequests}/{{pullRequestId:int}}/reading-path", async (
    string project, string repositoryId, int pullRequestId, ReadingPathUpdate update,
    IReviewProgressStore store, CancellationToken ct) =>
    await Execute(() => store.SetReadingPathAsync(project, repositoryId, pullRequestId, update.Paths, ct)));

app.Run();

// Every failure kind is mapped here rather than in a try/catch per endpoint, so a new one
// has exactly one place to be handled.
static async Task<IResult> Execute<T>(Func<Task<T>> action)
{
    try
    {
        return Results.Ok(await action());
    }
    catch (AzureDevOpsException ex)
    {
        return Results.Problem(ex.Message, statusCode: ex.StatusCode);
    }
    catch (SummaryAnalysisException ex)
    {
        return Results.Problem(ex.Message, statusCode: ex.StatusCode);
    }
    catch (ChecklistException ex)
    {
        return Results.Problem(ex.Message, statusCode: ex.StatusCode);
    }
    catch (SqlException)
    {
        return Results.Problem("Local storage is unavailable.", statusCode: 503);
    }
    catch (DbUpdateException)
    {
        return Results.Problem("Local storage is unavailable.", statusCode: 503);
    }
    catch (IOException)
    {
        return Results.Problem("Local storage is unavailable.", statusCode: 503);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Problem("Local storage is unavailable.", statusCode: 503);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Azure DevOps is unavailable.", statusCode: 502);
    }
    catch (TaskCanceledException)
    {
        return Results.Problem("Azure DevOps request timed out.", statusCode: 504);
    }
}

public partial class Program;
