using Microsoft.Data.Sqlite;
using PRCockpit.Api.Analysis;
using PRCockpit.Api.AzureDevOps;
using PRCockpit.Api.Checklists;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<AzureDevOpsClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IAiSummaryAnalyzer, CliSummaryAnalyzer>();
builder.Services.AddSingleton<ChecklistStore>();

var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");

api.MapGet("/projects", async (AzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetProjectsAsync(ct)));

api.MapGet("/projects/{project}/repositories", async (string project, AzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetRepositoriesAsync(project, ct)));

api.MapGet("/projects/{project}/repositories/{repositoryId}/pull-requests", async (
    string project, string repositoryId, AzureDevOpsClient client, CancellationToken ct) =>
    await Execute(() => client.GetPullRequestsAsync(project, repositoryId, ct)));

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

api.MapPost("/projects/{project}/repositories/{repositoryId}/pull-requests/{pullRequestId:int}/summary", async (
    string project, string repositoryId, int pullRequestId, AzureDevOpsClient client,
    IAiSummaryAnalyzer analyzer, CancellationToken ct) =>
    await Execute(async () =>
    {
        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        var context = await PrContextBuilder.BuildAsync(details,
            (path, token) => client.GetFileDiffAsync(project, repositoryId, details, path, token),
            ContextBudget.Default, ct);
        return await SummaryRunner.RunAsync(context, analyzer, ct);
    }));

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
    catch (SqliteException)
    {
        return Results.Problem("Local checklist storage is unavailable.", statusCode: 503);
    }
    catch (IOException)
    {
        return Results.Problem("Local checklist storage is unavailable.", statusCode: 503);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Problem("Local checklist storage is unavailable.", statusCode: 503);
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
