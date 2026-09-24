using PRCockpit.Application.Ports;
using PRCockpit.Domain.Analysis;

namespace PRCockpit.Infrastructure.Analysis;

/// <summary>Semantic for C#, by name for the rest — the mode travels in the response.</summary>
public sealed class CodeUsageFinder : ICodeUsageFinder
{
    public Task<CodeUsagesResponse> FindAsync(SourceSnapshot snapshot, string path, CancellationToken ct) =>
        SourceFiles.IsCSharp(path)
            ? CSharpUsages.FindAsync(snapshot, path, ct)
            : Task.FromResult(NameUsages.Find(snapshot, path));
}
