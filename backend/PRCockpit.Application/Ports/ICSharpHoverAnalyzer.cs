using PRCockpit.Domain.Analysis;

namespace PRCockpit.Application.Ports;

/// <summary>Type information for the two sides of a C# diff, and nothing else.</summary>
public interface ICSharpHoverAnalyzer
{
    CSharpHoverResponse Build(CSharpHoverRequest request);
}
