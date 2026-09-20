using System.Reflection;
using PRCockpit.Application.Ports;
using PRCockpit.Domain.PullRequests;
using PRCockpit.Infrastructure.Persistence;

namespace PRCockpit.Api.Tests;

/// <summary>
/// Layers are only real if something fails when they are crossed. Folder names do not
/// enforce anything; these do, by reading what each assembly actually compiled against.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(PrContext).Assembly;
    private static readonly Assembly Application = typeof(IAzureDevOpsClient).Assembly;
    private static readonly Assembly Infrastructure = typeof(PrCockpitContext).Assembly;

    private static IEnumerable<string> ReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? "");

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]   // persistence
    [InlineData("Microsoft.Data.SqlClient")]        // a database driver
    [InlineData("Microsoft.CodeAnalysis")]          // Roslyn
    [InlineData("Microsoft.AspNetCore")]            // the web host
    [InlineData("Microsoft.Extensions.Http")]       // outbound HTTP
    public void DomainKnowsNothingAboutTheOutsideWorld(string forbidden)
    {
        Assert.DoesNotContain(ReferencesOf(Domain),
            name => name.StartsWith(forbidden, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.Data.SqlClient")]
    [InlineData("Microsoft.CodeAnalysis")]
    [InlineData("Microsoft.AspNetCore")]
    public void ApplicationTalksToPortsRatherThanTechnology(string forbidden)
    {
        Assert.DoesNotContain(ReferencesOf(Application),
            name => name.StartsWith(forbidden, StringComparison.Ordinal));
    }

    [Fact]
    public void ApplicationDoesNotDependOnInfrastructure()
    {
        Assert.DoesNotContain("PRCockpit.Infrastructure", ReferencesOf(Application));
    }

    [Fact]
    public void DomainDoesNotDependOnApplication()
    {
        Assert.DoesNotContain("PRCockpit.Application", ReferencesOf(Domain));
    }

    [Fact]
    public void InfrastructureImplementsTheApplicationPorts()
    {
        // The direction that is allowed: adapters point inwards at the ports.
        Assert.Contains("PRCockpit.Application", ReferencesOf(Infrastructure));
    }

    [Fact]
    public void EveryPortHasAnImplementationInInfrastructure()
    {
        var ports = Application.GetTypes()
            .Where(type => type.IsInterface && type.Namespace == "PRCockpit.Application.Ports")
            .ToArray();
        Assert.NotEmpty(ports);

        var adapters = Infrastructure.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false }).ToArray();
        foreach (var port in ports)
        {
            Assert.True(adapters.Any(port.IsAssignableFrom),
                $"No adapter in Infrastructure implements {port.Name}.");
        }
    }
}
