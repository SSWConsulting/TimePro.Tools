namespace SSW.TimePro.Cli.Infrastructure.Config;

public static class ApiUrlNormalizer
{
    private const int DefaultLocalHttpsPort = 7107;
    private const string LocalTimeProDomain = ".local-sswtimepro.com";

    public static string Normalize(string apiUrl, out string? note)
    {
        note = null;

        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri))
            return apiUrl;

        if (!IsLocalTimeProHost(uri.Host))
            return apiUrl;

        var port = uri.IsDefaultPort || uri.Port == 443
            ? DefaultLocalHttpsPort
            : uri.Port;

        var normalized = new UriBuilder(uri)
        {
            Host = "localhost",
            Port = port,
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri.ToString().TrimEnd('/');

        note = $"Using {normalized} for local TimePro because {uri.Host} defaults to HTTPS port 443 and the local dev certificate is issued for localhost.";
        return normalized;
    }

    private static bool IsLocalTimeProHost(string host) =>
        host.EndsWith(LocalTimeProDomain, StringComparison.OrdinalIgnoreCase);
}
