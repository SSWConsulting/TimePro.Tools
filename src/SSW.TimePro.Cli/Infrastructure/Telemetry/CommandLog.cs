using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Infrastructure.Telemetry;

/// <summary>
/// Builds and persists the local command log line for a finished invocation.
/// </summary>
public static class CommandLog
{
    public static CommandLogEntry Build(
        ClientInvocation invocation,
        string version,
        TenantConfig? tenant,
        long durationMs,
        int? exitCode,
        string? failure = null) =>
        new()
        {
            Timestamp = DateTimeOffset.UtcNow,
            Version = ClientHeaders.ShortVersion(version),
            Surface = invocation.Surface,
            Command = invocation.Command,
            Tenant = tenant?.ConfigName ?? tenant?.TenantId,
            ApiHost = HostOf(tenant?.ApiUrl),
            DurationMs = durationMs,
            ExitCode = exitCode,
            Failure = failure,
            Requests = invocation.Requests
        };

    public static void Record(
        ClientInvocation? invocation,
        GlobalConfig? config,
        TenantConfig? tenant,
        long durationMs,
        int? exitCode,
        string? failure = null)
    {
        if (invocation is null || config?.Telemetry.LocalLog is not true)
            return;

        new CommandLogWriter(ConfigPaths.LogsDir)
            .Write(Build(invocation, BuildInfo.Version, tenant, durationMs, exitCode, failure));
    }

    private static string? HostOf(string? apiUrl) =>
        Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri) ? uri.Host : null;
}
