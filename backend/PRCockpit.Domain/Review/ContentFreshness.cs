namespace PRCockpit.Domain.Review;

/// <summary>
/// One rule for "has this file changed since we last looked at it", used by the reviewed
/// marker and by the saved AI explanation of a file (US-P2). A blob id changes if and only
/// if the file's content changed, so it answers the question exactly; the head commit is
/// the fallback for the case where Azure DevOps gives no blob id, and there a single
/// unrelated commit is enough to invalidate the entry.
/// <para>
/// The same rule runs in the browser for the reviewed marker (reviewState in frontend/src/useReviewProgress.ts), on
/// data it already holds. Keep the two in step.
/// </para>
/// </summary>
public static class ContentFreshness
{
    /// <summary>
    /// With nothing to compare against the saved entry stands: nagging without cause is
    /// worse than a slightly optimistic one.
    /// </summary>
    public static bool IsCurrent(string? savedBlobId, string? currentBlobId,
        string? savedHeadSha, string? currentHeadSha)
    {
        if (Both(savedBlobId, currentBlobId)) return Same(savedBlobId!, currentBlobId!);
        if (Both(savedHeadSha, currentHeadSha)) return Same(savedHeadSha!, currentHeadSha!);
        return true;
    }

    private static bool Both(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right);

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
