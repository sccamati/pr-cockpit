using Microsoft.Extensions.Configuration;
using PRCockpit.Api;

namespace PRCockpit.Api.Tests;

/// <summary>
/// The tool has no authentication (D-01), so the listen address is the only thing keeping
/// the token holder's identity to one machine. US-P0.
/// </summary>
public sealed class LocalOnlyTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    [Theory]
    [InlineData("http://localhost:5164")]
    [InlineData("http://127.0.0.1:5164")]
    [InlineData("https://[::1]:5164")]
    [InlineData("http://localhost:5164;https://localhost:7164")]
    public void LoopbackAddressesStart(string urls)
    {
        Assert.Empty(LocalOnly.RemoteAddresses(Config(("urls", urls))));
    }

    [Theory]
    [InlineData("http://0.0.0.0:5164")]
    [InlineData("http://*:5164")]
    [InlineData("http://+:5164")]
    [InlineData("http://192.168.1.10:5164")]
    [InlineData("http://my-laptop:5164")]          // a name, not a number, and not localhost
    public void AnythingReachableFromTheNetworkIsRefused(string url)
    {
        Assert.Equal([url], LocalOnly.RemoteAddresses(Config(("urls", url))));
    }

    [Fact]
    public void OneRemoteAddressAmongLocalOnesIsEnoughToRefuse()
    {
        var remote = LocalOnly.RemoteAddresses(Config(("urls", "http://localhost:5164;http://0.0.0.0:80")));
        Assert.Equal(["http://0.0.0.0:80"], remote);
    }

    [Fact]
    public void KestrelEndpointsAreReadToo()
    {
        var remote = LocalOnly.RemoteAddresses(Config(("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:5164")));
        Assert.Equal(["http://0.0.0.0:5164"], remote);
    }

    [Fact]
    public void NoConfiguredAddressMeansTheFrameworkDefault()
    {
        Assert.Empty(LocalOnly.RemoteAddresses(Config()));
    }
}
