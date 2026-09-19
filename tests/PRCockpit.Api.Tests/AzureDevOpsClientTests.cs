using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PRCockpit.Api.AzureDevOps;

namespace PRCockpit.Api.Tests;

public sealed class AzureDevOpsClientTests
{
    [Fact]
    public void MapsPullRequestFieldsAndReviewers()
    {
        using var json = JsonDocument.Parse("""
            {
              "pullRequestId": 123,
              "title": "Send invoices",
              "description": "Dispatch flow",
              "status": "active",
              "creationDate": "2026-09-01T12:00:00Z",
              "createdBy": { "displayName": "Anna" },
              "repository": { "name": "Billing" },
              "sourceRefName": "refs/heads/feature/invoices",
              "targetRefName": "refs/heads/main",
              "reviewers": [{ "displayName": "Jan", "vote": 10 }]
            }
            """);

        var summary = AzureDevOpsMapper.Summary(json.RootElement);
        var details = AzureDevOpsMapper.Details(json.RootElement, 7, 2,
            [new WorkItem("45", "https://example.test/45")]);

        Assert.Equal(123, summary.Id);
        Assert.Equal("Anna", summary.Author);
        Assert.Equal("Billing", summary.Repository);
        Assert.Equal("feature/invoices", details.SourceBranch);
        Assert.Equal("main", details.TargetBranch);
        Assert.Equal(7, details.ChangedFilesCount);
        Assert.Equal(2, details.CommitsCount);
        Assert.Equal(10, Assert.Single(details.Reviewers).Vote);
        Assert.Equal("45", Assert.Single(details.WorkItems).Id);
    }

    [Fact]
    public async Task LoadsDetailsAndCountsFilesAcrossChangePages()
    {
        var visited = new List<string>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("dev.azure.com", request.RequestUri!.Host);
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes(":test-pat")),
                request.Headers.Authorization?.Parameter);
            var path = request.RequestUri.PathAndQuery;
            visited.Add(path);
            var body = path switch
            {
                var p when p.Contains("/iterations/2/changes") && p.Contains("$skip=0") =>
                    """{"changeEntries":[{},{}],"nextSkip":2}""",
                var p when p.Contains("/iterations/2/changes") && p.Contains("$skip=2") =>
                    """{"changeEntries":[{}],"nextSkip":0}""",
                var p when p.Contains("/iterations?") =>
                    """{"value":[{"id":1},{"id":2}]}""",
                var p when p.Contains("/commits?") =>
                    """{"value":[{"commitId":"a"},{"commitId":"b"}]}""",
                var p when p.Contains("/workitems?") =>
                    """{"value":[{"id":"78","url":"https://example.test/78"}]}""",
                _ => """
                    {"pullRequestId":123,"title":"Send invoices","status":"active",
                     "creationDate":"2026-09-01T12:00:00Z",
                     "createdBy":{"displayName":"Anna"},"repository":{"name":"Billing"},
                     "sourceRefName":"refs/heads/feature","targetRefName":"refs/heads/main"}
                    """
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureDevOps:Organization"] = "example",
            ["AzureDevOps:Pat"] = "test-pat"
        }).Build();

        var details = await new AzureDevOpsClient(http, config)
            .GetPullRequestAsync("Project A", "repository-id", 123, CancellationToken.None);

        Assert.Equal(3, details.ChangedFilesCount);
        Assert.Equal(2, details.CommitsCount);
        Assert.Equal("78", Assert.Single(details.WorkItems).Id);
        Assert.Contains(visited, path => path.Contains("Project%20A"));
        Assert.Equal(6, visited.Count);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
