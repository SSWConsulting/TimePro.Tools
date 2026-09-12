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

    /// <summary>
    /// Records a finished invocation. Takes the config accessors rather than loaded config because
    /// reading config can itself throw, and no diagnostic step may fail an otherwise good command.
    /// </summary>
    public static void Record(
        ClientInvocation? invocation,
        Func<GlobalConfig?> loadConfig,
        Func<TenantConfig?> loadTenant,
        long durationMs,
        int? exitCode,
        string? failure = null)
    {
        try
        {
            if (invocation is null)
                return;

            // Absent or malformed telemetry config means the default, which is on.
            if (loadConfig()?.Telemetry is { LocalLog: false })
                return;

            new CommandLogWriter(ConfigPaths.LogsDir)
                .Write(Build(invocation, BuildInfo.Version, loadTenant(), durationMs, exitCode, failure));
        }
        catch
        {
            // Diagnostics are best-effort; never fail the command because of them.
        }
    }

    private static string? HostOf(string? apiUrl) =>
        Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri) ? uri.Host : null;
}
