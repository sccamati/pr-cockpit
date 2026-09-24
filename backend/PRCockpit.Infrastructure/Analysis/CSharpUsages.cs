using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using PRCockpit.Domain.Analysis;

namespace PRCockpit.Infrastructure.Analysis;

/// <summary>
/// Find All References over every C# file of the repository, the way the IDE does it:
/// SymbolFinder cascades to the interface member a method implements and to the member it
/// overrides, so a service called through its interface is not reported unused.
///
/// ponytail: the whole repository is one project with no package references — only the
/// framework assemblies this process runs on. Ceiling: a type that clashes across two
/// projects, or a member reached only through a NuGet type, can resolve to the wrong symbol
/// or to none; an unresolved declaration simply shows what was found.
/// </summary>
public static class CSharpUsages
{
    // SDK-style projects get these from a generated file under obj/, which is never in the
    // repository; without them half the calls would not bind. The Web SDK's set is included
    // because an unknown namespace here costs nothing but a diagnostic nobody reads.
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using System.Net.Http.Json;
        global using Microsoft.AspNetCore.Builder;
        global using Microsoft.AspNetCore.Hosting;
        global using Microsoft.AspNetCore.Http;
        global using Microsoft.AspNetCore.Routing;
        global using Microsoft.Extensions.Configuration;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Hosting;
        global using Microsoft.Extensions.Logging;
        """;

    // Tied to the snapshot object, so the parsed solution lives exactly as long as the
    // cache keeps the snapshot and is never rebuilt for the next file of the same commit.
    private static readonly ConditionalWeakTable<SourceSnapshot, Solution> Solutions = new();

    public static async Task<CodeUsagesResponse> FindAsync(SourceSnapshot snapshot, string path, CancellationToken ct)
    {
        var solution = Solutions.GetValue(snapshot, Build);
        var document = solution.Projects.Single().Documents.FirstOrDefault(item => item.FilePath == path);
        if (document is null) return new CodeUsagesResponse("semantic", [], snapshot.SkippedFiles);

        var root = await document.GetSyntaxRootAsync(ct);
        var model = await document.GetSemanticModelAsync(ct);
        if (root is null || model is null) return new CodeUsagesResponse("semantic", [], snapshot.SkippedFiles);

        var declared = root.DescendantNodes()
            .Select(node => (Identifier: Identifier(node), Symbol: model.GetDeclaredSymbol(node, ct)))
            .Where(item => item.Identifier is not null && item.Symbol is not null)
            .ToArray();
        var declarations = await Task.WhenAll(declared.Select(async item =>
        {
            var references = await SymbolFinder.FindReferencesAsync(item.Symbol!, solution, ct);
            var usages = references
                .SelectMany(reference => reference.Locations)
                .Where(location => !location.IsImplicit && location.Location.IsInSource &&
                    location.Document.FilePath is not null)
                .Select(location => Located(location.Document.FilePath!, location.Location))
                .Distinct()
                .OrderBy(location => location.Path, StringComparer.Ordinal)
                .ThenBy(location => location.Line).ThenBy(location => location.StartColumn)
                .ToArray();
            var span = item.Identifier!.Value.GetLocation().GetLineSpan();
            return new CodeDeclaration(
                span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
                span.EndLinePosition.Character + 1, item.Identifier.Value.ValueText, usages);
        }));
        return new CodeUsagesResponse("semantic", declarations, snapshot.SkippedFiles);
    }

    private static CodeLocation Located(string path, Location location)
    {
        var span = location.GetLineSpan();
        return new CodeLocation(path, span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1, span.EndLinePosition.Character + 1);
    }

    // What the IDE puts a reference count over: types and members, not fields or locals.
    private static SyntaxToken? Identifier(SyntaxNode node) => node switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier,
        DelegateDeclarationSyntax @delegate => @delegate.Identifier,
        MethodDeclarationSyntax method => method.Identifier,
        ConstructorDeclarationSyntax constructor => constructor.Identifier,
        PropertyDeclarationSyntax property => property.Identifier,
        EventDeclarationSyntax @event => @event.Identifier,
        _ => null
    };

    private static Solution Build(SourceSnapshot snapshot)
    {
        var projectId = ProjectId.CreateNewId();
        var documents = snapshot.Files
            .Where(file => SourceFiles.IsCSharp(file.Key))
            .Select(file => DocumentInfo.Create(DocumentId.CreateNewId(projectId), file.Key,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(file.Value), VersionStamp.Default)),
                filePath: file.Key))
            // No file path: a location in it is never reported as a usage.
            .Append(DocumentInfo.Create(DocumentId.CreateNewId(projectId), "ImplicitUsings.g.cs",
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(ImplicitUsings), VersionStamp.Default))));
        var project = ProjectInfo.Create(projectId, VersionStamp.Default, "PRCockpit.Usages", "PRCockpit.Usages",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            documents: documents,
            metadataReferences: CSharpHovers.References.Value);
        // ponytail: the workspace is not disposed — it holds nothing but memory, and it goes
        // with the solution when the snapshot leaves the cache.
        return new AdhocWorkspace().CurrentSolution.AddProject(project);
    }
}
