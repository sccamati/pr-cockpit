using PRCockpit.Application.Analysis;
using PRCockpit.Application.Ports;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;
using PRCockpit.Infrastructure.Analysis;

namespace PRCockpit.Api.Tests;

public sealed class CSharpUsagesTests
{
    private static SourceSnapshot Snapshot(params (string Path, string Text)[] files) =>
        new(new string('c', 40), files.ToDictionary(file => file.Path, file => file.Text), 0);

    private static CodeDeclaration Declaration(CodeUsagesResponse response, string name) =>
        Assert.Single(response.Declarations, item => item.Name == name);

    [Fact]
    public async Task ReportsANewMethodNobodyCallsAsUnused()
    {
        var snapshot = Snapshot(("/src/Invoices.cs", """
            namespace Billing;
            public class Invoices
            {
                public void Send() { }
                public void Draft() { }
            }
            """), ("/src/Caller.cs", """
            namespace Billing;
            public class Caller { public void Run(Invoices invoices) => invoices.Send(); }
            """));

        var response = await CSharpUsages.FindAsync(snapshot, "/src/Invoices.cs", CancellationToken.None);

        Assert.Equal("semantic", response.Mode);
        Assert.Empty(Declaration(response, "Draft").Usages);
        var send = Assert.Single(Declaration(response, "Send").Usages);
        Assert.Equal("/src/Caller.cs", send.Path);
        Assert.Equal(2, send.Line);
        Assert.Single(Declaration(response, "Invoices").Usages);
    }

    [Fact]
    public async Task CountsACallThroughTheInterfaceForTheImplementation()
    {
        var snapshot = Snapshot(("/src/IMailer.cs", "namespace App; public interface IMailer { void Send(); }"),
            ("/src/Mailer.cs", "namespace App; public sealed class Mailer : IMailer { public void Send() { } }"),
            ("/src/Use.cs", "namespace App; public class Use { public void Run(IMailer mailer) { mailer.Send(); mailer.Send(); } }"));

        var response = await CSharpUsages.FindAsync(snapshot, "/src/Mailer.cs", CancellationToken.None);

        var usages = Declaration(response, "Send").Usages;
        Assert.Equal(2, usages.Count(usage => usage.Path == "/src/Use.cs"));
    }

    [Fact]
    public async Task KeepsOverloadsApart()
    {
        var snapshot = Snapshot(("/src/Format.cs", """
            namespace App;
            public static class Format
            {
                public static string Money(int value) => "";
                public static string Money(int value, string currency) => "";
            }
            """), ("/src/Use.cs", "namespace App; public class Use { public string Run() => Format.Money(1); }"));

        var response = await CSharpUsages.FindAsync(snapshot, "/src/Format.cs", CancellationToken.None);

        var overloads = response.Declarations.Where(item => item.Name == "Money").ToArray();
        Assert.Equal(2, overloads.Length);
        Assert.Single(overloads[0].Usages);
        Assert.Empty(overloads[1].Usages);
    }

    [Fact]
    public async Task ResolvesCallsThatRelyOnImplicitUsings()
    {
        // Neither file has a using directive: SDK projects get System.Threading.Tasks from a
        // generated file that is never in the repository.
        var snapshot = Snapshot(("/src/Job.cs", "namespace App; public class Job { public Task RunAsync() => Task.CompletedTask; }"),
            ("/src/Use.cs", "namespace App; public class Use { public Task Go(Job job) => job.RunAsync(); }"));

        var response = await CSharpUsages.FindAsync(snapshot, "/src/Job.cs", CancellationToken.None);

        Assert.Single(Declaration(response, "RunAsync").Usages);
    }

    [Fact]
    public async Task AFileOutsideTheSnapshotHasNoDeclarations()
    {
        var response = await CSharpUsages.FindAsync(Snapshot(("/a.cs", "class A { }")), "/b.cs", CancellationToken.None);

        Assert.Empty(response.Declarations);
    }
}

public sealed class NameUsagesTests
{
    private static SourceSnapshot Snapshot(params (string Path, string Text)[] files) =>
        new(new string('c', 40), files.ToDictionary(file => file.Path, file => file.Text), 0);

    private static CodeDeclaration Declaration(CodeUsagesResponse response, string name) =>
        Assert.Single(response.Declarations, item => item.Name == name);

    [Fact]
    public void CountsAScriptSetupFunctionThatOnlyTheTemplateCalls()
    {
        var snapshot = Snapshot(("/src/Panel.vue", """
            <script setup lang="ts">
            function save() {}
            function draft() {}
            </script>

            <template>
              <!-- draft() is not wired yet -->
              <button @click="save">Zapisz</button>
            </template>
            """));

        var response = NameUsages.Find(snapshot, "/src/Panel.vue");

        Assert.Equal("name", response.Mode);
        var save = Assert.Single(Declaration(response, "save").Usages);
        Assert.Equal(8, save.Line);
        Assert.Empty(Declaration(response, "draft").Usages);
    }

    [Fact]
    public void LooksForANonExportedSymbolInItsOwnFileOnly()
    {
        var snapshot = Snapshot(("/src/a.ts", "function load() {}\nload()\n"),
            ("/src/b.ts", "function load() {}\nload()\nload()\n"));

        var response = NameUsages.Find(snapshot, "/src/a.ts");

        Assert.Equal(["/src/a.ts"], Declaration(response, "load").Usages.Select(usage => usage.Path));
    }

    [Fact]
    public void LooksForAnExportedSymbolInTheFilesThatImportItsModule()
    {
        var snapshot = Snapshot(("/src/format.ts", "export function fileName(path: string) { return path }\n"),
            ("/src/App.vue", "<script setup lang=\"ts\">\nimport { fileName } from './format'\n</script>\n<template>{{ fileName(x) }}</template>\n"),
            ("/src/other.ts", "import { something } from './elsewhere'\nconst fileName = 1\n"));

        var response = NameUsages.Find(snapshot, "/src/format.ts");

        var usages = Declaration(response, "fileName").Usages;
        Assert.Equal(2, usages.Count);
        Assert.All(usages, usage => Assert.Equal("/src/App.vue", usage.Path));
    }

    [Fact]
    public void IgnoresCommentsButKeepsStrings()
    {
        var snapshot = Snapshot(("/src/a.ts", """
            export const url = 'http://example.test' // reset() here is a comment
            function reset() {}
            /* reset() */
            const handlers = { click: 'reset' }
            """));

        var reset = Declaration(NameUsages.Find(snapshot, "/src/a.ts"), "reset");

        Assert.Single(reset.Usages);
        Assert.Equal(4, reset.Usages[0].Line);
    }

    [Fact]
    public void SkipsLocalValuesButKeepsLocalFunctions()
    {
        var snapshot = Snapshot(("/src/useThing.ts", """
            export function useThing() {
              const loading = ref(false)
              const load = async () => { loading.value = true }
              function reset() {}
              return { load, reset }
            }
            """));

        var names = NameUsages.Find(snapshot, "/src/useThing.ts").Declarations.Select(item => item.Name);

        Assert.Equal(["useThing", "load", "reset"], names);
    }

    [Fact]
    public void DoesNotReadKeywordsGluedToNamesAsDeclarations()
    {
        var snapshot = Snapshot(("/src/a.ts", "class A {\n  constructor() {}\n}\ntypeof x\n"));

        Assert.Equal(["A"], NameUsages.Find(snapshot, "/src/a.ts").Declarations.Select(item => item.Name));
    }
}

public sealed class CodeUsageServiceTests
{
    [Fact]
    public async Task RefusesAPathOutsideThePullRequest()
    {
        var client = new SnapshotClient();
        var service = new CodeUsageService(client, new MemorySourceSnapshotCache(), new CodeUsageFinder());

        var error = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            service.FindAsync("p", "r", 1, "/src/Other.cs", CancellationToken.None));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(0, client.SnapshotCalls);
    }

    [Fact]
    public async Task RefusesAFileThePullRequestDeletes()
    {
        var client = new SnapshotClient();
        var service = new CodeUsageService(client, new MemorySourceSnapshotCache(), new CodeUsageFinder());

        var error = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            service.FindAsync("p", "r", 1, "/src/Gone.cs", CancellationToken.None));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(0, client.SnapshotCalls);
    }

    [Fact]
    public async Task RefusesAFileTypeWithoutUsagesBeforeAnyRequest()
    {
        var client = new SnapshotClient();
        var service = new CodeUsageService(client, new MemorySourceSnapshotCache(), new CodeUsageFinder());

        var error = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            service.FindAsync("p", "r", 1, "/README.md", CancellationToken.None));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, client.DetailsCalls);
    }

    [Fact]
    public async Task FetchesTheRepositoryOncePerHeadCommit()
    {
        var client = new SnapshotClient();
        var service = new CodeUsageService(client, new MemorySourceSnapshotCache(), new CodeUsageFinder());

        await service.FindAsync("p", "r", 1, "/src/Job.cs", CancellationToken.None);
        await service.FindAsync("p", "r", 1, "/src/Job.cs", CancellationToken.None);
        var source = await service.GetSourceAsync("p", "r", 1, "/src/Use.cs", CancellationToken.None);

        Assert.Equal(1, client.SnapshotCalls);
        Assert.Contains("job.Run()", source.Text);
    }

    [Fact]
    public async Task ServesSourceOnlyFromTheSnapshot()
    {
        var service = new CodeUsageService(new SnapshotClient(), new MemorySourceSnapshotCache(), new CodeUsageFinder());

        var error = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            service.GetSourceAsync("p", "r", 1, "/../secrets.json", CancellationToken.None));

        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task DoesNotKeepAFailedFetch()
    {
        var client = new SnapshotClient { FailFirst = true };
        var service = new CodeUsageService(client, new MemorySourceSnapshotCache(), new CodeUsageFinder());

        await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            service.FindAsync("p", "r", 1, "/src/Job.cs", CancellationToken.None));
        var response = await service.FindAsync("p", "r", 1, "/src/Job.cs", CancellationToken.None);

        Assert.Equal(2, client.SnapshotCalls);
        Assert.NotEmpty(response.Declarations);
    }

    // Wide port, narrow fake, as in FileQuestionServiceTests.
    private sealed class SnapshotClient : IAzureDevOpsClient
    {
        public int DetailsCalls { get; private set; }
        public int SnapshotCalls { get; private set; }
        public bool FailFirst { get; init; }

        public Task<PullRequestDetails> GetPullRequestAsync(
            string project, string repositoryId, int pullRequestId, CancellationToken ct)
        {
            DetailsCalls++;
            return Task.FromResult(new PullRequestDetails(1, "Change", null, "Author", "Repo", "feature", "main",
                "active", DateTimeOffset.UtcNow, [], 1,
                [new ChangedFile("/src/Job.cs", "edit", null, "blob-1"), new ChangedFile("/src/Gone.cs", "delete", null)],
                0, [], [], new string('a', 40), new string('b', 40)));
        }

        public Task<SourceSnapshot> GetSourceSnapshotAsync(
            string project, string repositoryId, string commitSha, CancellationToken ct)
        {
            SnapshotCalls++;
            if (FailFirst && SnapshotCalls == 1) throw new AzureDevOpsException("Azure DevOps request failed.", 502);
            return Task.FromResult(new SourceSnapshot(commitSha, new Dictionary<string, string>
            {
                ["/src/Job.cs"] = "namespace App; public class Job { public void Run() { } }",
                ["/src/Use.cs"] = "namespace App; public class Use { public void Go(Job job) => job.Run(); }",
            }, 0));
        }

        public Task<FileDiff> GetFileDiffAsync(string project, string repositoryId, PullRequestDetails details, string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Repository>> GetRepositoriesAsync(string project, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsAsync(string project, string repositoryId, CancellationToken ct) => throw new NotSupportedException();
        public Task<FileDiff> GetFileDiffAsync(string project, string repositoryId, int pullRequestId, string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PrCommentThread>> GetCommentThreadsAsync(string project, string repositoryId, int pullRequestId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrCommentThread> CreateCommentThreadAsync(string project, string repositoryId, int pullRequestId, NewCommentThread thread, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrCommentThread> ReplyToThreadAsync(string project, string repositoryId, int pullRequestId, int threadId, NewComment comment, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrCommentThread> UpdateCommentAsync(string project, string repositoryId, int pullRequestId, int threadId, int commentId, EditComment comment, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrCommentThread> DeleteCommentAsync(string project, string repositoryId, int pullRequestId, int threadId, int commentId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrCommentThread> SetThreadStatusAsync(string project, string repositoryId, int pullRequestId, int threadId, string? status, CancellationToken ct) => throw new NotSupportedException();
        public Task<FileDiff> GetFileDiffSinceIterationAsync(string project, string repositoryId, int pullRequestId, string path, int iterationId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetChangedPathsSinceIterationAsync(string project, string repositoryId, int pullRequestId, int iterationId, CancellationToken ct) => throw new NotSupportedException();
    }
}
