using PRCockpit.Application.Ports;
using PRCockpit.Application.PullRequests;
using PRCockpit.Domain.Analysis;
using PRCockpit.Domain.PullRequests;
using PRCockpit.Domain.Review;

namespace PRCockpit.Application.Analysis;

/// <summary>
/// The third AI path: a free-text question about one file, with follow-ups that work. The
/// history is assembled here from rows we wrote, never from the request, so the client
/// cannot hand the model a previous answer it never gave. Turns recorded against other
/// content stay on screen but are not sent back, so an answer never rests on code that is
/// no longer in the file.
/// </summary>
public sealed class FileQuestionService(
    IAzureDevOpsClient client,
    PullRequestContextService contexts,
    IAiSummaryAnalyzer analyzer,
    IFileQuestionStore store)
{
    // Three turns is a follow-up, not a transcript: enough for "a to drugie?" to resolve,
    // while the prompt stays the file plus a few lines.
    private const int HistoryTurns = 3;

    public async Task<FileQuestionTurn> AskAsync(
        string project, string repositoryId, int pullRequestId,
        string path, string? question, string? selection, CancellationToken ct)
    {
        // Checked before any I/O: a blank or oversized question must not cost an Azure
        // DevOps round trip, let alone a model run.
        var asked = (question ?? "").Trim();
        if (asked.Length == 0)
            throw new ChecklistException("Ask a question first.", 400);
        if (asked.Length > FileQuestionRequest.MaxQuestionLength)
            throw new ChecklistException(
                $"The question may be at most {FileQuestionRequest.MaxQuestionLength} characters.", 400);
        // ponytail: the snippet is taken on trust — we do not check that it occurs in the
        // file. The whole file is already in the context, so a snippet is a pointer rather
        // than new information. Ceiling: a user can "select" text that is not in the file
        // and get an answer about it. Harmless for one local reviewer, wrong for a shared tool.
        var snippet = selection?.Trim();
        if (snippet is { Length: > FileQuestionRequest.MaxSelectionLength })
            throw new ChecklistException(
                $"The selected snippet may be at most {FileQuestionRequest.MaxSelectionLength} characters.", 400);
        if (snippet is { Length: 0 }) snippet = null;

        var details = await client.GetPullRequestAsync(project, repositoryId, pullRequestId, ct);
        // The same allowlist the diff and the explanation use: a path outside this pull
        // request's file list never reaches Azure DevOps or the model.
        var changed = details.ChangedFiles.FirstOrDefault(file => file.Path == path);
        if (changed is null)
            throw new AzureDevOpsException("File is no longer in this pull request.", 404);

        var history = await store.GetAsync(project, repositoryId, pullRequestId, path, ct);
        var context = await contexts.BuildAsync(project, repositoryId, details, ct, path);
        var turn = await SummaryRunner.RunAskAsync(context,
            new FileQuestion(asked, snippet, changed.ObjectId,
                Current(history, changed.ObjectId, details.HeadCommitSha)),
            analyzer, ct);
        return await store.SaveAsync(project, repositoryId, pullRequestId, turn, ct);
    }

    /// <summary>
    /// The last few turns that are still about the file as it is now. A turn recorded
    /// against other content answered a question about code that has since changed, so it
    /// stays in the thread the reviewer can read but is not fed back to the model — an
    /// answer built on it would describe lines that are no longer there.
    /// <see cref="ContentFreshness"/> is the same rule the reviewed marker and the saved
    /// explanation use, so "changed under us" means one thing in this codebase.
    /// </summary>
    public static IReadOnlyList<FileQuestionTurn> Current(
        IReadOnlyList<FileQuestionTurn> history, string? blobId, string? headCommitSha) =>
        [.. history
            .Where(turn => ContentFreshness.IsCurrent(turn.BlobId, blobId, turn.HeadCommitSha, headCommitSha))
            .TakeLast(HistoryTurns)];
}
