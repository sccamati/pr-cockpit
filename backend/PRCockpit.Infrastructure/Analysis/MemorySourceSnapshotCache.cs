using Microsoft.Extensions.Caching.Memory;
using PRCockpit.Application.Ports;
using PRCockpit.Domain.Analysis;

namespace PRCockpit.Infrastructure.Analysis;

/// <summary>
/// The one cache of Azure DevOps content in the app, and a safe one: the key carries the
/// commit SHA, and what a commit contains never changes. It only spares fetching the whole
/// repository again for every file of the same pull request.
///
/// ponytail: two snapshots, ten idle minutes, in process memory. Ceiling: switching between
/// three pull requests refetches, and a restart starts cold.
/// </summary>
public sealed class MemorySourceSnapshotCache : ISourceSnapshotCache, IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 2 });
    private readonly Lock gate = new();

    public Task<SourceSnapshot> GetOrAddAsync(
        string key, Func<CancellationToken, Task<SourceSnapshot>> load, CancellationToken ct)
    {
        Lazy<Task<SourceSnapshot>> entry;
        // Locked so two files opened at once share one download instead of racing two.
        lock (gate)
        {
            entry = cache.GetOrCreate(key, item =>
            {
                item.Size = 1;
                item.SlidingExpiration = TimeSpan.FromMinutes(10);
                return new Lazy<Task<SourceSnapshot>>(() => LoadAsync(key, load));
            })!;
        }
        // The caller may leave (the reader opened another file) without abandoning a fetch
        // the next file of the same commit will want.
        return entry.Value.WaitAsync(ct);
    }

    private async Task<SourceSnapshot> LoadAsync(string key, Func<CancellationToken, Task<SourceSnapshot>> load)
    {
        try
        {
            return await load(CancellationToken.None);
        }
        catch
        {
            // A failure is not an answer worth keeping for ten minutes.
            cache.Remove(key);
            throw;
        }
    }

    public void Dispose() => cache.Dispose();
}
