using PRCockpit.Api.AzureDevOps;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<AzureDevOpsClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

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
