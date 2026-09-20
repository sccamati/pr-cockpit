using System.Net;

namespace PRCockpit.Api;

/// <summary>
/// The tool has no authentication and is not getting any (D-01): the Azure DevOps token
/// lives in the server's configuration, so anything that reaches the port can comment as
/// its owner. Binding outside the loopback interface is therefore refused rather than
/// warned about.
/// </summary>
public static class LocalOnly
{
    public const string OverrideKey = "Security:AllowRemoteAccess";

    /// <summary>
    /// Returns the addresses that would expose the tool to the network, empty when every
    /// configured address is a loopback one. An unparseable or wildcard host counts as
    /// remote: "*", "+" and "0.0.0.0" all mean every interface.
    /// </summary>
    public static IReadOnlyList<string> RemoteAddresses(IConfiguration configuration)
    {
        var urls = (configuration["urls"] ?? configuration["ASPNETCORE_URLS"] ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(configuration.GetSection("Kestrel:Endpoints").GetChildren()
                .Select(endpoint => endpoint["Url"] ?? "")
                .Where(url => url.Length > 0));
        return urls.Where(url => !IsLoopback(url)).ToList();
    }

    private static bool IsLoopback(string url)
    {
        // Uri rejects the wildcard hosts Kestrel accepts, and they are exactly the ones
        // that must not pass, so a failed parse is an answer rather than a problem.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.Trim('[', ']');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    public static string Refusal(IEnumerable<string> addresses) =>
        $"""
        PR Cockpit nasłuchuje wyłącznie lokalnie i nie wystartuje na: {string.Join(", ", addresses)}.
        Narzędzie nie ma uwierzytelniania, a token Azure DevOps leży w konfiguracji serwera —
        kto dosięgnie portu, ten komentuje w Twoim imieniu. Ustaw adres na localhost albo,
        świadomie, {OverrideKey}=true.
        """;

    public const string Warning =
        "OSTRZEŻENIE: PR Cockpit nasłuchuje poza pętlą zwrotną. Nie ma uwierzytelniania — " +
        "komentarze zapisze każdy, kto dosięgnie tego portu.";
}
