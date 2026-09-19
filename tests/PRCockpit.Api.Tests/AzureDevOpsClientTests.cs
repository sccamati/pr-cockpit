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
        var details = AzureDevOpsMapper.Details(json.RootElement,
            [new ChangedFile("/src/invoices.cs", "edit", null)], 2,
            [new WorkItem("45", "https://example.test/45")]);

        Assert.Equal(123, summary.Id);
        Assert.Equal("Anna", summary.Author);
        Assert.Equal("Billing", summary.Repository);
        Assert.Equal("feature/invoices", details.SourceBranch);
        Assert.Equal("main", details.TargetBranch);
        Assert.Equal(1, details.ChangedFilesCount);
        Assert.Equal("/src/invoices.cs", Assert.Single(details.ChangedFiles).Path);
        Assert.Equal(2, details.CommitsCount);
        Assert.Equal(10, Assert.Single(details.Reviewers).Vote);
        Assert.Equal("45", Assert.Single(details.WorkItems).Id);
    }

    [Fact]
    public async Task LoadsChangedFilesAcrossChangePages()
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
                    """
                    {"changeEntries":[
                      {"item":{"path":"/src/new.cs"},"changeType":"add"},
                      {"item":{"path":"/src/renamed.cs"},"originalPath":"/src/old.cs","changeType":"rename"},
                      {"item":{"path":"/src","isFolder":true},"changeType":"edit"}
                    ],"nextSkip":3}
                    """,
                var p when p.Contains("/iterations/2/changes") && p.Contains("$skip=3") =>
                    """{"changeEntries":[{"item":{"path":"/src/obsolete.cs"},"changeType":"delete"}],"nextSkip":0}""",
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
        Assert.Equal(["/src/new.cs", "/src/renamed.cs", "/src/obsolete.cs"],
            details.ChangedFiles.Select(file => file.Path));
        Assert.Equal("add", details.ChangedFiles[0].ChangeType);
        Assert.Equal("/src/old.cs", details.ChangedFiles[1].OriginalPath);
        Assert.Equal("delete", details.ChangedFiles[2].ChangeType);
        Assert.Equal(2, details.CommitsCount);
        Assert.Equal("78", Assert.Single(details.WorkItems).Id);
        Assert.Contains(visited, path => path.Contains("Project%20A"));
        Assert.Equal(6, visited.Count);
    }

    [Fact]
    public async Task DiffUsesCommonAndSourceCommitsAndOriginalPathForRename()
    {
        var requestedItems = new List<string>();
        var baseCommit = new string('a', 40);
        var sourceCommit = new string('b', 40);
        var oldBlob = new string('c', 40);
        var newBlob = new string('d', 40);
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            var url = request.RequestUri!;
            var path = url.PathAndQuery;
            if (path.Contains("/iterations?"))
                return Json("""{"value":[{"id":1},{"id":2,"commonRefCommit":{"commitId":"BASE"},"sourceRefCommit":{"commitId":"SOURCE"}}]}"""
                    .Replace("BASE", baseCommit).Replace("SOURCE", sourceCommit));
            if (path.Contains("/iterations/2/changes"))
            {
                Assert.Contains("$compareTo=0", path);
                return Json("""{"changeEntries":[{"item":{"path":"/new name.cs"},"originalPath":"/old name.cs","changeType":"rename"}],"nextSkip":0}""");
            }
            if (path.Contains("/items?"))
            {
                requestedItems.Add(Uri.UnescapeDataString(path));
                return Json("""{"objectId":"BLOB","contentMetadata":{"isBinary":false}}"""
                    .Replace("BLOB", requestedItems.Count == 1 ? oldBlob : newBlob));
            }
            if (path.Contains($"/blobs/{oldBlob}")) return Bytes("same\nold\n");
            if (path.Contains($"/blobs/{newBlob}")) return Bytes("same\nnew\n");
            throw new Xunit.Sdk.XunitException($"Unexpected request: {path}");
        }));

        var diff = await Client(http).GetFileDiffAsync("Project A", "repo", 123, "/new name.cs", CancellationToken.None);

        Assert.Equal("text", diff.Kind);
        Assert.Equal("/old name.cs", diff.OriginalPath);
        Assert.Equal("same\nold\n", diff.OriginalText);
        Assert.Equal("same\nnew\n", diff.ModifiedText);
        Assert.Contains($"path=/old name.cs&versionDescriptor.version={baseCommit}", requestedItems[0]);
        Assert.Contains($"path=/new name.cs&versionDescriptor.version={sourceCommit}", requestedItems[1]);
    }

    [Theory]
    [InlineData("add", "", "line\n", 0, 1)]
    [InlineData("delete", "line\n", "", 1, 0)]
    public async Task DiffTreatsAddedAndDeletedFilesAsEmptyOnMissingSide(
        string changeType, string expectedOriginal, string expectedModified, int expectedOldRequests, int expectedNewRequests)
    {
        var itemRequests = 0;
        var blob = new string('c', 40);
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/iterations?")) return Iteration();
            if (path.Contains("/changes?"))
                return Json("""{"changeEntries":[{"item":{"path":"/file.txt"},"changeType":"TYPE"}],"nextSkip":0}"""
                    .Replace("TYPE", changeType));
            if (path.Contains("/items?"))
            {
                itemRequests++;
                return Json("""{"objectId":"BLOB","contentMetadata":{"isBinary":false}}""".Replace("BLOB", blob));
            }
            if (path.Contains("/blobs/")) return Bytes("line\n");
            throw new Xunit.Sdk.XunitException($"Unexpected request: {path}");
        }));

        var diff = await Client(http).GetFileDiffAsync("project", "repo", 123, "/file.txt", CancellationToken.None);

        Assert.Equal("text", diff.Kind);
        Assert.Equal(expectedOriginal, diff.OriginalText);
        Assert.Equal(expectedModified, diff.ModifiedText);
        Assert.Equal(expectedOldRequests + expectedNewRequests, itemRequests);
    }

    [Theory]
    [InlineData("binary", true, 1)]
    [InlineData("binary", false, 1)]
    [InlineData("tooLarge", false, 262145)]
    public async Task DiffReportsBinaryAndOversizedFiles(string expectedKind, bool isBinary, int bytes)
    {
        var blob = new string('c', 40);
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/iterations?")) return Iteration();
            if (path.Contains("/changes?"))
                return Json("""{"changeEntries":[{"item":{"path":"/file.dat"},"changeType":"add"}],"nextSkip":0}""");
            if (path.Contains("/items?"))
                return Json("""{"objectId":"BLOB","contentMetadata":{"isBinary":BINARY}}"""
                    .Replace("BLOB", blob).Replace("BINARY", isBinary ? "true" : "false"));
            if (path.Contains("/blobs/")) return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[bytes])
            };
            throw new Xunit.Sdk.XunitException($"Unexpected request: {path}");
        }));

        var diff = await Client(http).GetFileDiffAsync("project", "repo", 123, "/file.dat", CancellationToken.None);

        Assert.Equal(expectedKind, diff.Kind);
        Assert.Null(diff.OriginalText);
        Assert.Null(diff.ModifiedText);
    }

    [Fact]
    public async Task DiffRejectsFilesOutsideCurrentChangeList()
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/iterations?")) return Iteration();
            if (path.Contains("/changes?")) return Json("""{"changeEntries":[],"nextSkip":0}""");
            throw new Xunit.Sdk.XunitException($"Unexpected request: {path}");
        }));

        var exception = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            Client(http).GetFileDiffAsync("project", "repo", 123, "/secret.txt", CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task DiffLimitsLineCountEvenWhenFileIsSmallInBytes()
    {
        var blob = new string('c', 40);
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/iterations?")) return Iteration();
            if (path.Contains("/changes?"))
                return Json("""{"changeEntries":[{"item":{"path":"/many.txt"},"changeType":"add"}],"nextSkip":0}""");
            if (path.Contains("/items?"))
                return Json("""{"objectId":"BLOB","contentMetadata":{"isBinary":false}}""".Replace("BLOB", blob));
            if (path.Contains("/blobs/")) return Bytes(string.Concat(Enumerable.Repeat("x\n", 4001)));
            throw new Xunit.Sdk.XunitException($"Unexpected request: {path}");
        }));

        var diff = await Client(http).GetFileDiffAsync("project", "repo", 123, "/many.txt", CancellationToken.None);

        Assert.Equal("tooLarge", diff.Kind);
        Assert.Null(diff.OriginalText);
        Assert.Null(diff.ModifiedText);
    }

    [Theory]
    [InlineData(2000, "text")]
    [InlineData(2001, "tooLarge")]
    public async Task DiffAppliesCombinedLineLimitToBothVersions(int modifiedLines, string expectedKind)
    {
        var oldBlob = new string('c', 40);
        var newBlob = new string('d', 40);
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/iterations?")) return Iteration();
            if (path.Contains("/changes?"))
                return Json("""{"changeEntries":[{"item":{"path":"/file.txt"},"changeType":"edit"}],"nextSkip":0}""");
            if (path.Contains("/items?"))
                return Json("""{"objectId":"BLOB"}""".Replace("BLOB",
                    path.Contains(new string('a', 40)) ? oldBlob : newBlob));
            if (path.Contains($"/blobs/{oldBlob}")) return Bytes(string.Concat(Enumerable.Repeat("old\r\n", 2000)));
            if (path.Contains($"/blobs/{newBlob}")) return Bytes(string.Concat(Enumerable.Repeat("new\r\n", modifiedLines)));
            throw new Xunit.Sdk.XunitException($"Unexpected request: {path}");
        }));

        var diff = await Client(http).GetFileDiffAsync("project", "repo", 123, "/file.txt", CancellationToken.None);

        Assert.Equal(expectedKind, diff.Kind);
        Assert.Equal(expectedKind == "text" ? string.Concat(Enumerable.Repeat("old\r\n", 2000)) : null,
            diff.OriginalText);
        Assert.Equal(expectedKind == "text" ? string.Concat(Enumerable.Repeat("new\r\n", modifiedLines)) : null,
            diff.ModifiedText);
    }

    [Fact]
    public async Task DiffDoesNotReturnOriginalTextWhenModifiedVersionIsBinary()
    {
        var oldBlob = new string('c', 40);
        var newBlob = new string('d', 40);
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/iterations?")) return Iteration();
            if (path.Contains("/changes?"))
                return Json("""{"changeEntries":[{"item":{"path":"/file.dat"},"changeType":"edit"}],"nextSkip":0}""");
            if (path.Contains("/items?"))
                return Json("""{"objectId":"BLOB","contentMetadata":{"isBinary":BINARY}}"""
                    .Replace("BLOB", path.Contains(new string('a', 40)) ? oldBlob : newBlob)
                    .Replace("BINARY", path.Contains(new string('a', 40)) ? "false" : "true"));
            if (path.Contains($"/blobs/{oldBlob}")) return Bytes("private old text");
            throw new Xunit.Sdk.XunitException($"Unexpected request: {path}");
        }));

        var diff = await Client(http).GetFileDiffAsync("project", "repo", 123, "/file.dat", CancellationToken.None);

        Assert.Equal("binary", diff.Kind);
        Assert.Null(diff.OriginalText);
        Assert.Null(diff.ModifiedText);
    }

    [Fact]
    public async Task DiffShowsMissingFinalNewline()
    {
        var oldBlob = new string('c', 40);
        var newBlob = new string('d', 40);
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/iterations?")) return Iteration();
            if (path.Contains("/changes?"))
                return Json("""{"changeEntries":[{"item":{"path":"/file.txt"},"changeType":"edit"}],"nextSkip":0}""");
            if (path.Contains("/items?"))
                return Json("""{"objectId":"BLOB"}""".Replace("BLOB",
                    path.Contains(new string('a', 40)) ? oldBlob : newBlob));
            if (path.Contains($"/blobs/{oldBlob}")) return Bytes("line\n");
            if (path.Contains($"/blobs/{newBlob}")) return Bytes("line");
            throw new Xunit.Sdk.XunitException($"Unexpected request: {path}");
        }));

        var diff = await Client(http).GetFileDiffAsync("project", "repo", 123, "/file.txt", CancellationToken.None);

        Assert.Equal("line\n", diff.OriginalText);
        Assert.Equal("line", diff.ModifiedText);
    }

    private static AzureDevOpsClient Client(HttpClient http)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureDevOps:Organization"] = "example",
            ["AzureDevOps:Pat"] = "test-pat"
        }).Build();
        return new AzureDevOpsClient(http, config);
    }

    private static HttpResponseMessage Iteration() => Json("""
        {"value":[{"id":1,"commonRefCommit":{"commitId":"BASE"},"sourceRefCommit":{"commitId":"SOURCE"}}]}
        """.Replace("BASE", new string('a', 40)).Replace("SOURCE", new string('b', 40)));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Bytes(string body) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
