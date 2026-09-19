using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PRCockpit.Api.Analysis;
using PRCockpit.Api.AzureDevOps;
using PRCockpit.Api.Checklists;
using PRCockpit.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<AzureDevOpsClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IAiSummaryAnalyzer, CliSummaryAnalyzer>();
builder.Services.AddDbContext<PrCockpitContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PrCockpit")));
// Scoped, because they now depend on the scoped DbContext.
builder.Services.AddScoped<ChecklistStore>();
builder.Services.AddScoped<SummaryStore>();
builder.Services.AddScoped<ReviewProgressStore>();

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

api.MapGet("/projects", async (AzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetProjectsAsync(ct)));

api.MapGet("/projects/{project}/repositories", async (string project, AzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetRepositoriesAsync(project, ct)));

api.MapGet("/projects/{project}/repositories/{repositoryId}/pull-requests", async (
    string project, string repositoryId, AzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetPullRequestsAsync(project, repositoryId, ct)));

api.MapGet("/projects/{project}/repositories/{repositoryId}/pull-requests/checklist-progress", async (
    string project, string repositoryId, ChecklistStore store, CancellationToken ct) =>
    await Execute(() => store.GetProgressAsync(project, repositoryId, ct)));

api.MapGet("/projects/{project}/repositories/{repositoryId}/pull-requests/{pullRequestId:int}", async (
    string project, string repositoryId, int pullRequestId, AzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct)));

api.MapGet("/projects/{project}/repositories/{repositoryId}/pull-requests/{pullRequestId:int}/diff", async (
    string project, string repositoryId, int pullRequestId, string path, AzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetFileDiffAsync(project, repositoryId, pullRequestId, path, ct)));

api.MapPost("/csharp/hovers", async (CSharpHoverRequest request) =>
    await Execute(() => Task.FromResult(CSharpHovers.Build(request))));

api.MapGet("/projects/{project}/repositories/{repositoryId}/pull-requests/{pullRequestId:int}/context", async (
    string project, string repositoryId, int pullRequestId, AzureDevOpsClient client, CancellationToken ct) =>
    await Execute(async () =>
    {
        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        return await PrContextBuilder.BuildAsync(details,
            (path, token) => client.GetFileDiffAsync(project, repositoryId, details, path, token),
            ContextBudget.Default, ct);
    }));

api.MapGet("/projects/{project}/repositories/{repositoryId}/pull-requests/{pullRequestId:int}/summary", async (
    string project, string repositoryId, int pullRequestId, SummaryStore store, CancellationToken ct) =>
    await Execute(async () => new { stored = await store.GetAsync(project, repositoryId, pullRequestId, ct) }));

api.MapPost("/projects/{project}/repositories/{repositoryId}/pull-requests/{pullRequestId:int}/summary", async (
    string project, string repositoryId, int pullRequestId, AzureDevOpsClient client,
    IAiSummaryAnalyzer analyzer, SummaryStore store, CancellationToken ct) =>
    await Execute(async () =>
    {
        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        var context = await PrContextBuilder.BuildAsync(details,
            (path, token) => client.GetFileDiffAsync(project, repositoryId, details, path, token),
            ContextBudget.Default, ct);
        var result = await SummaryRunner.RunAsync(context, analyzer, ct);
        await store.SaveAsync(project, repositoryId, pullRequestId, result, ct);
        return result;
    }));

api.MapGet("/projects/{project}/repositories/{repositoryId}/pull-requests/file-review-progress", async (
    string project, string repositoryId, ReviewProgressStore store, CancellationToken ct) =>
    await Execute(() => store.GetProgressAsync(project, repositoryId, ct)));

api.MapGet("/projects/{project}/repositories/{repositoryId}/pull-requests/{pullRequestId:int}/file-reviews", async (
    string project, string repositoryId, int pullRequestId, ReviewProgressStore store, CancellationToken ct) =>
    await Execute(() => store.GetAsync(project, repositoryId, pullRequestId, ct)));

// The file path travels in the body: a route segment cannot carry a slash-laden path safely.
api.MapPut("/projects/{project}/repositories/{repositoryId}/pull-requests/{pullRequestId:int}/file-reviews", async (
    string project, string repositoryId, int pullRequestId, FileReviewUpdate update,
    ReviewProgressStore store, CancellationToken ct) =>
    await Execute(() => store.SetFileAsync(project, repositoryId, pullRequestId, update, ct)));

api.MapPut("/projects/{project}/repositories/{repositoryId}/pull-requests/{pullRequestId:int}/reading-path", async (
    string project, string repositoryId, int pullRequestId, ReadingPathUpdate update,
    ReviewProgressStore store, CancellationToken ct) =>
    await Execute(() => store.SetReadingPathAsync(project, repositoryId, pullRequestId, update.Paths, ct)));

api.MapGet("/projects/{project}/repositories/{repositoryId}/pull-requests/{pullRequestId:int}/checklist", async (
    string project, string repositoryId, int pullRequestId, ChecklistStore store, CancellationToken ct) =>
    await Execute(() => store.GetAsync(project, repositoryId, pullRequestId, ct)));

api.MapPut("/projects/{project}/repositories/{repositoryId}/pull-requests/{pullRequestId:int}/checklist/{item}", async (
    string project, string repositoryId, int pullRequestId, string item, ChecklistUpdate update,
    ChecklistStore store, CancellationToken ct) =>
    await Execute(() => store.SetAsync(project, repositoryId, pullRequestId, item, update.Completed, ct)));

app.Run();

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
