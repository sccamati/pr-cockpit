using PRCockpit.Api.Analysis;
using PRCockpit.Api.AzureDevOps;

namespace PRCockpit.Api.Tests;

public sealed class CSharpHoversTests
{
    [Fact]
    public void ResolvesLocalTypesInBothVersions()
    {
        const string original = "class Sample { void Run() { var value = 1; System.Console.WriteLine(value); } }";
        const string modified = "class Sample { void Run() { var value = \"text\"; System.Console.WriteLine(value); } }";

        var hovers = CSharpHovers.Build(new CSharpHoverRequest(original, modified));

        Assert.Equal(2, hovers.Original.Count(item => item.Signature == "int value"));
        Assert.Equal(2, hovers.Modified.Count(item => item.Signature == "string value"));
        Assert.Contains(hovers.Modified, item => item.Signature.StartsWith("void Console.WriteLine("));
        Assert.Contains(hovers.Modified, item => item.Signature == "string");
        Assert.All(hovers.Modified, item => Assert.True(item.StartColumn < item.EndColumn));
    }

    [Fact]
    public void SkipsTypesThatCannotBeResolvedFromTheFile()
    {
        const string source = "class Sample { MissingType unknown; int count; }";

        var hovers = CSharpHovers.Build(new CSharpHoverRequest("", source));

        Assert.DoesNotContain(hovers.Modified, item => item.Signature.Contains("MissingType"));
        Assert.Contains(hovers.Modified, item => item.Signature == "int Sample.count");
    }

    [Fact]
    public void ClassifiesDeclarationsCallsAndExternalTypeSyntax()
    {
        const string source = """
            class Customer
            {
                public string Name { get; set; }
                public void Save(int amount) { }
                public void Run(Customer customer)
                {
                    customer.Save(1);
                    var item = new ExternalType();
                    item.Value = 1;
                }
            }
            """;

        var tokens = CSharpHovers.Build(new CSharpHoverRequest("", source)).ModifiedTokens;

        Assert.Contains(tokens, token => token.Line == 1 && token.Kind == "class");
        Assert.Contains(tokens, token => token.Line == 3 && token.Kind == "property");
        Assert.Contains(tokens, token => token.Line == 4 && token.Kind == "method");
        Assert.Contains(tokens, token => token.Line == 5 && token.Kind == "parameter");
        Assert.Contains(tokens, token => token.Line == 7 && token.Kind == "method");
        Assert.Contains(tokens, token => token.Line == 8 && token.Kind == "class");
        Assert.Contains(tokens, token => token.Line == 9 && token.Kind == "property");
        Assert.DoesNotContain(tokens, token => token.Line == 8 && token.StartColumn == 5);
    }

    [Fact]
    public void RejectsSourcesLargerThanTheDiffLimit()
    {
        var source = new string('x', 256 * 1024 + 1);

        var error = Assert.Throws<AzureDevOpsException>(() =>
            CSharpHovers.Build(new CSharpHoverRequest("", source)));

        Assert.Equal(400, error.StatusCode);
    }
}
