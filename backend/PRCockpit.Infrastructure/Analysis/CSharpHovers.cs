using PRCockpit.Domain.Analysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PRCockpit.Domain.PullRequests;
using PRCockpit.Application.Ports;

namespace PRCockpit.Infrastructure.Analysis;

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

        var original = Analyze(request.OriginalText);
        var modified = Analyze(request.ModifiedText);
        return new CSharpHoverResponse(
            original.Hovers, modified.Hovers, original.Tokens, modified.Tokens);
    }

    private sealed record DocumentAnalysis(
        IReadOnlyList<CSharpHoverEntry> Hovers, IReadOnlyList<CSharpSemanticToken> Tokens);

    private static DocumentAnalysis Analyze(string source)
    {
        if (source.Length == 0) return new DocumentAnalysis([], []);

        var tree = CSharpSyntaxTree.ParseText(source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create("PRCockpit.Hover", [tree], References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var hovers = new List<CSharpHoverEntry>();
        var tokens = new List<CSharpSemanticToken>();

        foreach (var token in root.DescendantTokens())
        {
            if (!token.IsKind(SyntaxKind.IdentifierToken) || token.Parent is null) continue;

            ISymbol? symbol = token.Parent is IdentifierNameSyntax identifier
                ? model.GetSymbolInfo(identifier).Symbol
                : model.GetDeclaredSymbol(token.Parent);
            var signature = Signature(symbol);
            var span = tree.GetLineSpan(token.Span);
            if (signature is not null)
                hovers.Add(new CSharpHoverEntry(
                    span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
                    span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1,
                    signature));

            // Keep keywords such as "var" in the syntax tokenizer's existing color.
            if (token.Text == token.ValueText &&
                (SyntaxFacts.GetKeywordKind(token.ValueText) != SyntaxKind.None ||
                 SyntaxFacts.GetContextualKeywordKind(token.ValueText) != SyntaxKind.None))
                continue;

            var kind = TokenKind(symbol) ?? SyntaxTokenKind(token.Parent);
            if (kind is not null && span.StartLinePosition.Line == span.EndLinePosition.Line)
                tokens.Add(new CSharpSemanticToken(
                    span.StartLinePosition.Line + 1,
                    span.StartLinePosition.Character + 1,
                    span.EndLinePosition.Character + 1,
                    kind));
        }

        return new DocumentAnalysis(hovers, tokens);
    }

    private static string? TokenKind(ISymbol? symbol) => symbol switch
    {
        INamespaceSymbol => "namespace",
        INamedTypeSymbol { TypeKind: not TypeKind.Error } type => type.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Interface => "interface",
            TypeKind.Struct => "struct",
            TypeKind.Enum => "enum",
            TypeKind.Delegate => "delegate",
            _ => null
        },
        ITypeParameterSymbol => "typeParameter",
        IMethodSymbol { MethodKind: MethodKind.Constructor } => "class",
        IMethodSymbol => "method",
        IPropertySymbol => "property",
        IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => "enumMember",
        IFieldSymbol => "field",
        IEventSymbol => "event",
        ILocalSymbol => "variable",
        IParameterSymbol => "parameter",
        _ => null
    };

    private static string? SyntaxTokenKind(SyntaxNode node) => node switch
    {
        ClassDeclarationSyntax or RecordDeclarationSyntax => "class",
        InterfaceDeclarationSyntax => "interface",
        StructDeclarationSyntax => "struct",
        EnumDeclarationSyntax => "enum",
        MethodDeclarationSyntax or ConstructorDeclarationSyntax => "method",
        PropertyDeclarationSyntax => "property",
        VariableDeclaratorSyntax => "variable",
        ParameterSyntax => "parameter",
        IdentifierNameSyntax name => SimpleNameKind(name),
        GenericNameSyntax name => SimpleNameKind(name),
        _ => null
    };

    private static string? SimpleNameKind(SimpleNameSyntax name)
    {
        if (name.Parent is UsingDirectiveSyntax) return "namespace";
        if (name.Parent is ObjectCreationExpressionSyntax or BaseTypeSyntax or VariableDeclarationSyntax)
            return "class";
        if (name.Parent is InvocationExpressionSyntax) return "method";
        if (name.Parent is MemberAccessExpressionSyntax member && member.Name == name)
            return member.Parent is InvocationExpressionSyntax ? "method" : "property";
        return null;
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

/// <summary>Adapter so the application depends on a port, not on Roslyn.</summary>
public sealed class CSharpHoverAnalyzer : ICSharpHoverAnalyzer
{
    public CSharpHoverResponse Build(CSharpHoverRequest request) => CSharpHovers.Build(request);
}
