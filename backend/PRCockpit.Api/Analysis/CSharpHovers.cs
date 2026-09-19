using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PRCockpit.Api.AzureDevOps;

namespace PRCockpit.Api.Analysis;

public sealed record CSharpHoverRequest(string OriginalText, string ModifiedText);

public sealed record CSharpHoverEntry(
    int StartLine, int StartColumn, int EndLine, int EndColumn, string Signature);

public sealed record CSharpHoverResponse(
    IReadOnlyList<CSharpHoverEntry> Original, IReadOnlyList<CSharpHoverEntry> Modified);

public static class CSharpHovers
{
    private const int MaxFileBytes = 256 * 1024;
    private static readonly Lazy<MetadataReference[]> References = new(() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray());

    public static CSharpHoverResponse Build(CSharpHoverRequest request)
    {
        if (request.OriginalText is null || request.ModifiedText is null ||
            Encoding.UTF8.GetByteCount(request.OriginalText) > MaxFileBytes ||
            Encoding.UTF8.GetByteCount(request.ModifiedText) > MaxFileBytes ||
            TextLineLimit.Exceeded(request.OriginalText, request.ModifiedText))
            throw new AzureDevOpsException("C# source exceeds the diff limits.", 400);

        return new CSharpHoverResponse(Analyze(request.OriginalText), Analyze(request.ModifiedText));
    }

    private static IReadOnlyList<CSharpHoverEntry> Analyze(string source)
    {
        if (source.Length == 0) return [];

        var tree = CSharpSyntaxTree.ParseText(source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create("PRCockpit.Hover", [tree], References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var result = new List<CSharpHoverEntry>();

        foreach (var token in root.DescendantTokens())
        {
            if (!token.IsKind(SyntaxKind.IdentifierToken) || token.Parent is null) continue;

            ISymbol? symbol = token.Parent is IdentifierNameSyntax identifier
                ? model.GetSymbolInfo(identifier).Symbol
                : model.GetDeclaredSymbol(token.Parent);
            var signature = Signature(symbol);
            if (signature is null) continue;

            var span = tree.GetLineSpan(token.Span);
            result.Add(new CSharpHoverEntry(
                span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
                span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1,
                signature));
        }

        return result;
    }

    private static string? Signature(ISymbol? symbol) => symbol switch
    {
        ILocalSymbol local when Known(local.Type) => $"{TypeName(local.Type)} {local.Name}",
        IParameterSymbol parameter when Known(parameter.Type) =>
            $"{TypeName(parameter.Type)} {parameter.Name}",
        IFieldSymbol field when Known(field.Type) =>
            $"{TypeName(field.Type)} {field.ContainingType.Name}.{field.Name}",
        IPropertySymbol property when Known(property.Type) =>
            $"{TypeName(property.Type)} {property.ContainingType.Name}.{property.Name}",
        IMethodSymbol method when Known(method.ReturnType) &&
            method.Parameters.All(parameter => Known(parameter.Type)) =>
            $"{TypeName(method.ReturnType)} {method.ContainingType?.Name}.{method.Name}" +
            $"({string.Join(", ", method.Parameters.Select(p => $"{TypeName(p.Type)} {p.Name}"))})",
        INamedTypeSymbol type when Known(type) => type.SpecialType == SpecialType.None
            ? $"{type.TypeKind.ToString().ToLowerInvariant()} {TypeName(type)}"
            : TypeName(type),
        _ => null
    };

    private static string TypeName(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static bool Known(ITypeSymbol type) => type switch
    {
        IErrorTypeSymbol => false,
        IArrayTypeSymbol array => Known(array.ElementType),
        IPointerTypeSymbol pointer => Known(pointer.PointedAtType),
        INamedTypeSymbol named => named.TypeArguments.All(Known),
        _ => true
    };
}
