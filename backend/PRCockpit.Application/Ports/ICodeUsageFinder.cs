using PRCockpit.Domain.Analysis;

namespace PRCockpit.Application.Ports;

/// <summary>Where the declarations of one file are used, anywhere in the snapshot.</summary>
public interface ICodeUsageFinder
{
    Task<CodeUsagesResponse> FindAsync(SourceSnapshot snapshot, string path, CancellationToken ct);
}

/// <summary>
/// Snapshots by commit. Content under a commit SHA cannot change, so holding one for a few
/// minutes cannot serve stale code — it only spares refetching the repository per file.
/// </summary>
public interface ISourceSnapshotCache
{
    Task<SourceSnapshot> GetOrAddAsync(
        string key, Func<CancellationToken, Task<SourceSnapshot>> load, CancellationToken ct);
}
