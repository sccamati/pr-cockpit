using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PRCockpit.Application.PullRequests;
using PRCockpit.Domain.PullRequests;
using PRCockpit.Infrastructure.AzureDevOps;

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
            [new ChangedFile("/src/invoices.cs", "edit", null)],
            [new Commit("a", "Add invoice dispatch", "Anna", null), new Commit("b", "Add tests", "Jan", null)],
            [new WorkItem("45", "https://example.test/45")]);

        Assert.Equal(123, summary.Id);
        Assert.Equal("Anna", summary.Author);
        Assert.Equal("Billing", summary.Repository);
        Assert.Equal("feature/invoices", details.SourceBranch);
        Assert.Equal("main", details.TargetBranch);
        Assert.Equal(1, details.ChangedFilesCount);
        Assert.Equal("/src/invoices.cs", Assert.Single(details.ChangedFiles).Path);
        Assert.Equal(2, details.CommitsCount);
        Assert.Equal("Add invoice dispatch", details.Commits[0].Message);
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
                    """{"value":[{"commitId":"a","comment":"First change","author":{"name":"Anna"}},{"commitId":"b","comment":"Second change","author":{"name":"Jan"}}]}""",
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
        Assert.Equal(["First change", "Second change"], details.Commits.Select(commit => commit.Message));
        Assert.Equal("78", Assert.Single(details.WorkItems).Id);
        Assert.Contains(visited, path => path.Contains("Project%20A"));
        Assert.Equal(6, visited.Count);
    }

    [Fact]
    public async Task LoadsPullRequestCommitsAcrossContinuationPages()
    {
        var commitRequests = new List<string>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/iterations?")) return Json("""{"value":[]}""");
            if (path.Contains("/workitems?")) return Json("""{"value":[]}""");
            if (path.Contains("/commits?"))
            {
                commitRequests.Add(path);
                if (commitRequests.Count == 1)
                {
                    var page = Json("""{"value":[{"commitId":"a","comment":"First\nDetails","author":{"name":"Anna","date":"2026-09-01T12:00:00Z"}}]}""");
                    page.Headers.TryAddWithoutValidation("x-ms-continuationtoken", "next+/page");
                    return page;
                }
                Assert.Contains("continuationToken=next%2B%2Fpage", path);
                return Json("""{"value":[{"commitId":"b","comment":"Second","author":{"name":"Jan"}}]}""");
            }
            if (path.EndsWith("/123?api-version=7.1")) return Json("""
                {"pullRequestId":123,"title":"Change","status":"active","creationDate":"2026-09-01T12:00:00Z",
                 "createdBy":{"displayName":"Anna"},"repository":{"name":"Repo"},
                 "sourceRefName":"refs/heads/feature","targetRefName":"refs/heads/main"}
                """);
            throw new Xunit.Sdk.XunitException($"Unexpected request: {path}");
        }));

        var details = await Client(http).GetPullRequestAsync("project", "repo", 123, CancellationToken.None);

        Assert.Equal(2, commitRequests.Count);
        Assert.Equal(2, details.CommitsCount);
        Assert.Equal(["a", "b"], details.Commits.Select(commit => commit.Id));
        Assert.Equal("First\nDetails", details.Commits[0].Message);
        Assert.Equal("Anna", details.Commits[0].Author);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), details.Commits[0].AuthoredAt);
        Assert.Null(details.Commits[1].AuthoredAt);
    }

    [Fact]
    public async Task ReturnsEmptyCommitListWhenPullRequestHasNoCommits()
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/iterations?")) return Json("""{"value":[]}""");
            if (path.Contains("/commits?") || path.Contains("/workitems?")) return Json("""{"value":[]}""");
            return Json("""
                {"pullRequestId":123,"title":"Change","status":"active","creationDate":"2026-09-01T12:00:00Z",
                 "createdBy":{"displayName":"Anna"},"repository":{"name":"Repo"},
                 "sourceRefName":"refs/heads/feature","targetRefName":"refs/heads/main"}
                """);
        }));

        var details = await Client(http).GetPullRequestAsync("project", "repo", 123, CancellationToken.None);

        Assert.Empty(details.Commits);
        Assert.Equal(0, details.CommitsCount);
    }

    [Fact]
    public async Task ContextReusesTheFetchedIterationAndChangeListForAllDiffs()
    {
        var visited = new List<string>();
        var head = new string('b', 40);
        var blob = new string('c', 40);
        using var http = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            visited.Add(path);
            if (path.Contains("/iterations?")) return Iteration();
            if (path.Contains("/changes?")) return Json("""
                {"changeEntries":[
                  {"item":{"path":"/a.txt"},"changeType":"add"},
                  {"item":{"path":"/b.txt"},"changeType":"add"}
                ],"nextSkip":0}
                """);
            if (path.Contains("/commits?") || path.Contains("/workitems?")) return Json("""{"value":[]}""");
            if (path.Contains("/items?"))
            {
                Assert.Contains($"versionDescriptor.version={head}", path);
                return Json("""{"objectId":"BLOB"}""".Replace("BLOB", blob));
            }
            if (path.Contains("/blobs/")) return Bytes("text");
            if (path.EndsWith("/123?api-version=7.1")) return Json("""
                {"pullRequestId":123,"title":"Change","status":"active","creationDate":"2026-09-01T12:00:00Z",
                 "createdBy":{"displayName":"Anna"},"repository":{"name":"Repo"},
                 "sourceRefName":"refs/heads/feature","targetRefName":"refs/heads/main"}
                """);
            throw new Xunit.Sdk.XunitException($"Unexpected request: {path}");
        }));
        var client = Client(http);

        var details = await client.GetPullRequestAsync("project", "repo", 123, CancellationToken.None);
        var context = await PrContextBuilder.BuildAsync(details,
            (path, ct) => client.GetFileDiffAsync("project", "repo", details, path, ct),
            ContextBudget.Default, CancellationToken.None);

        Assert.Equal(new string('a', 40), context.PullRequest.BaseCommitSha);
        Assert.Equal(head, context.PullRequest.HeadCommitSha);
        Assert.Equal(2, context.ChangedFiles.Count);
        Assert.All(context.ChangedFiles, file => Assert.Equal("text", file.ModifiedText));
        Assert.Single(visited, path => path.Contains("/iterations?"));
        Assert.Single(visited, path => path.Contains("/changes?"));
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

    // Azure DevOps mixes system threads and soft-deleted comments into the same list. One
    // of those must never take the whole list down, which is why this mapper is tolerant.
    [Fact]
    public async Task ReadsHumanThreadsAndSurvivesSystemThreadsAndDeletedComments()
    {
        string? requested = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            requested = request.RequestUri!.PathAndQuery;
            Assert.Equal(HttpMethod.Get, request.Method);
            return Json("""
                {"value":[
                  {"id":1,"status":"active",
                   "threadContext":{"filePath":"/src/invoices.cs","rightFileStart":{"line":42,"offset":1}},
                   "comments":[
                     {"id":1,"author":{"displayName":"Jan"},"content":"Czy to na pewno tutaj?","commentType":"text","publishedDate":"2026-09-01T12:00:00Z"},
                     {"id":2,"author":{"displayName":"Anna"},"commentType":"text"}]},
                  {"id":2,"comments":[{"id":1,"commentType":"system","content":"Jan voted 10"}]},
                  {"id":3,"comments":[]}
                ]}
                """);
        }))
        { BaseAddress = new Uri("https://dev.azure.com/") };

        var threads = await Client(http).GetCommentThreadsAsync("proj", "repo", 123, CancellationToken.None);

        Assert.Contains("/threads?", requested);
        var thread = Assert.Single(threads);
        Assert.Equal(1, thread.Id);
        Assert.Equal("/src/invoices.cs", thread.FilePath);
        Assert.Equal(42, thread.RightLine);
        Assert.Equal(2, thread.Comments.Count);
        // The soft-deleted comment keeps its place in the conversation, without content.
        Assert.Null(thread.Comments[1].Content);
    }

    [Fact]
    public void ReadsAThreadWithNoStatusAndNoContextWithoutFailing()
    {
        using var json = JsonDocument.Parse("""
            {"id":7,"comments":[{"id":1,"commentType":"text","content":"Ogólna uwaga."}]}
            """);

        var thread = AzureDevOpsMapper.CommentThread(json.RootElement);

        Assert.Equal(7, thread.Id);
        Assert.Null(thread.Status);
        Assert.Null(thread.FilePath);
        Assert.False(thread.IsSystem);
    }

    [Fact]
    public void ReadsTheIterationAThreadWasLeftOn()
    {
        using var json = JsonDocument.Parse("""
            {"id":4,"status":"active",
             "threadContext":{"filePath":"/src/a.cs","rightFileStart":{"line":3,"offset":1}},
             "pullRequestThreadContext":{"iterationContext":{"firstComparingIteration":1,"secondComparingIteration":2}},
             "comments":[{"id":1,"commentType":"text","content":"Uwaga."}]}
            """);

        // The second iteration is the one the comment was written against.
        Assert.Equal(2, AzureDevOpsMapper.CommentThread(json.RootElement).IterationId);
    }

    [Fact]
    public void LeavesTheIterationEmptyForAThreadWithNoDiffContext()
    {
        using var json = JsonDocument.Parse("""
            {"id":4,"comments":[{"id":1,"commentType":"text","content":"Ogólna uwaga."}]}
            """);

        Assert.Null(AzureDevOpsMapper.CommentThread(json.RootElement).IterationId);
    }

    // Offering edit and delete on somebody else's comment would be offering a button that
    // Azure DevOps is certain to refuse, so ownership is resolved before the list is served.
    [Fact]
    public async Task MarksWhichCommentsBelongToThePatOwner()
    {
        string? identityQuery = null;
        using var http = new HttpClient(new StubHandler(request =>
            request.RequestUri!.PathAndQuery.Contains("connectionData")
                ? Json((identityQuery = request.RequestUri.Query) is null ? "" :
                    """{"authenticatedUser":{"id":"ME","providerDisplayName":"Ja"}}""")
                : Json("""
                    {"value":[{"id":1,"status":"active","comments":[
                      {"id":1,"author":{"id":"ME","displayName":"Ja"},"content":"Moje","commentType":"text"},
                      {"id":2,"author":{"id":"INNY","displayName":"Ktos"},"content":"Cudze","commentType":"text"}]}]}
                    """)))
        { BaseAddress = new Uri("https://dev.azure.com/") };

        var thread = Assert.Single(await Client(http)
            .GetCommentThreadsAsync("proj", "repo", 1, CancellationToken.None));

        // connectionData is preview-only; plain api-version=7.1 is rejected and the failure
        // is swallowed, so nothing would ever be marked as mine.
        Assert.Contains("-preview", identityQuery);
        Assert.True(thread.Comments[0].IsMine);
        Assert.False(thread.Comments[1].IsMine);
    }

    // Identity is a convenience; losing it must not take the comment list with it.
    [Fact]
    public async Task StillListsThreadsWhenTheIdentityCallFails()
    {
        using var http = new HttpClient(new StubHandler(request =>
            request.RequestUri!.PathAndQuery.Contains("connectionData")
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : Json("""{"value":[{"id":1,"comments":[{"id":1,"content":"Uwaga","commentType":"text"}]}]}""")))
        { BaseAddress = new Uri("https://dev.azure.com/") };

        var thread = Assert.Single(await Client(http)
            .GetCommentThreadsAsync("proj", "repo", 1, CancellationToken.None));

        Assert.False(Assert.Single(thread.Comments).IsMine);
    }

    [Fact]
    public async Task EditsAndDeletesASingleComment()
    {
        var sent = new List<(HttpMethod Method, string Path)>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            sent.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Json("""{"id":9,"status":"active","comments":[{"id":3,"content":"Nowa tresc","commentType":"text"}]}""");
        }))
        { BaseAddress = new Uri("https://dev.azure.com/") };
        var client = Client(http, allowComments: true);

        await client.UpdateCommentAsync("proj", "repo", 1, 9, 3, new EditComment("Nowa tresc"), CancellationToken.None);
        await client.DeleteCommentAsync("proj", "repo", 1, 9, 3, CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, sent[0].Method);
        Assert.EndsWith("/threads/9/comments/3", sent[0].Path);
        Assert.Equal(HttpMethod.Delete, sent[2].Method);
        // Each write is followed by reading the thread back, never trusting the write's echo.
        Assert.Equal(HttpMethod.Get, sent[1].Method);
        Assert.Equal(HttpMethod.Get, sent[3].Method);
    }

    [Fact]
    public async Task ReportsAForbiddenCommentEditAsOwnershipRatherThanConfiguration()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)))
        { BaseAddress = new Uri("https://dev.azure.com/") };

        var error = await Assert.ThrowsAsync<AzureDevOpsException>(() => Client(http, allowComments: true)
            .DeleteCommentAsync("proj", "repo", 1, 9, 3, CancellationToken.None));

        Assert.Equal(403, error.StatusCode);
        Assert.Contains("your own comments", error.Message);
    }

    [Fact]
    public async Task RefusesToWriteWhileTheSwitchIsOff()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            throw new InvalidOperationException("Nothing may leave the machine with comments switched off.")))
        { BaseAddress = new Uri("https://dev.azure.com/") };
        var client = Client(http);

        foreach (var write in new Func<Task>[]
        {
            () => client.CreateCommentThreadAsync("proj", "repo", 1, new NewCommentThread("Uwaga.", null, null), CancellationToken.None),
            () => client.ReplyToThreadAsync("proj", "repo", 1, 2, new NewComment("Odpowiedź."), CancellationToken.None),
            () => client.SetThreadStatusAsync("proj", "repo", 1, 2, "fixed", CancellationToken.None),
            () => client.UpdateCommentAsync("proj", "repo", 1, 2, 3, new EditComment("Nowa."), CancellationToken.None),
            () => client.DeleteCommentAsync("proj", "repo", 1, 2, 3, CancellationToken.None),
        })
        {
            var error = await Assert.ThrowsAsync<AzureDevOpsException>(write);
            Assert.Equal(503, error.StatusCode);
        }
    }

    [Fact]
    public async Task PostsANewThreadAnchoredOnTheRightHandSide()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            sent = request;
            body = request.Content!.ReadAsStringAsync().Result;
            return Json("""
                {"id":9,"status":"active","threadContext":{"filePath":"/src/invoices.cs","rightFileStart":{"line":42,"offset":1}},
                 "comments":[{"id":1,"author":{"displayName":"Ja"},"content":"Uwaga.","commentType":"text"}]}
                """);
        }))
        { BaseAddress = new Uri("https://dev.azure.com/") };

        var thread = await Client(http, allowComments: true).CreateCommentThreadAsync(
            "proj", "repo", 123, new NewCommentThread("  Uwaga.  ", "/src/invoices.cs", 42), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Contains("\"line\":42", body);
        Assert.Contains("\"offset\":1", body);
        Assert.DoesNotContain("leftFileStart", body);
        Assert.Contains("\"content\":\"Uwaga.\"", body);   // trimmed before it is sent
        Assert.Equal(9, thread.Id);
        Assert.Equal(42, thread.RightLine);
    }

    [Fact]
    public async Task PatchesAThreadStatusAsANumberAndReadsItBackAsAName()
    {
        string? body = null;
        HttpMethod? method = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            method = request.Method;
            body = request.Content!.ReadAsStringAsync().Result;
            return Json("""{"id":9,"status":"fixed","comments":[{"id":1,"commentType":"text","content":"Uwaga."}]}""");
        }))
        { BaseAddress = new Uri("https://dev.azure.com/") };

        var thread = await Client(http, allowComments: true)
            .SetThreadStatusAsync("proj", "repo", 123, 9, "fixed", CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, method);
        Assert.Contains("\"status\":2", body);
        Assert.Equal("fixed", thread.Status);
    }

    [Fact]
    public async Task RejectsAnUnknownStatusAndAnEmptyComment()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            throw new InvalidOperationException("Invalid input must not reach Azure DevOps.")))
        { BaseAddress = new Uri("https://dev.azure.com/") };
        var client = Client(http, allowComments: true);

        Assert.Equal(400, (await Assert.ThrowsAsync<AzureDevOpsException>(() => client
            .SetThreadStatusAsync("proj", "repo", 1, 2, "resolved", CancellationToken.None))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<AzureDevOpsException>(() => client
            .CreateCommentThreadAsync("proj", "repo", 1, new NewCommentThread("   ", null, null), CancellationToken.None))).StatusCode);
    }

    // A PAT without the threads scope is our configuration fault, not an Azure DevOps
    // outage, so a write must say so by name instead of returning a generic 502.
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, 503)]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    [InlineData(HttpStatusCode.Conflict, 409)]
    public async Task MapsWriteFailuresDifferentlyFromReadFailures(HttpStatusCode returned, int expected)
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(returned)))
        { BaseAddress = new Uri("https://dev.azure.com/") };

        var error = await Assert.ThrowsAsync<AzureDevOpsException>(() => Client(http, allowComments: true)
            .CreateCommentThreadAsync("proj", "repo", 1, new NewCommentThread("Uwaga.", null, null), CancellationToken.None));

        Assert.Equal(expected, error.StatusCode);
        if (expected == 503) Assert.Contains("PR threads (read & write)", error.Message);
    }

    // The regression guard for stage 4B: unlocking writes must not turn any read into one.
    [Fact]
    public async Task EveryReadPathStillSendsGet()
    {
        var methods = new List<HttpMethod>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            methods.Add(request.Method);
            Assert.Null(request.Content);
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/iterations") && !path.Contains("/changes")) return Iteration();
            if (path.Contains("/changes")) return Json("""{"changeEntries":[],"nextSkip":0}""");
            if (path.Contains("/threads")) return Json("""{"value":[]}""");
            return Json("""{"value":[]}""");
        }))
        { BaseAddress = new Uri("https://dev.azure.com/") };
        var client = Client(http);

        await client.GetProjectsAsync(CancellationToken.None);
        await client.GetRepositoriesAsync("proj", CancellationToken.None);
        await client.GetCommentThreadsAsync("proj", "repo", 123, CancellationToken.None);

        Assert.NotEmpty(methods);
        Assert.All(methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    private static AzureDevOpsClient Client(HttpClient http, bool allowComments = false)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureDevOps:Organization"] = "example",
            ["AzureDevOps:Pat"] = "test-pat",
            ["AzureDevOps:AllowComments"] = allowComments ? "true" : "false"
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
