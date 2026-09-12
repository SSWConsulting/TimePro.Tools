using System.Text.Json.Serialization;

namespace SSW.TimePro.Cli.Infrastructure.Telemetry;

/// <summary>
/// One line of <c>~/.config/timepro-cli/logs/commands.jsonl</c>. Deliberately carries no arguments,
/// bodies, keys, notes or employee ids - only what is needed to match a local failure to a server request.
/// </summary>
public sealed record CommandLogEntry
{
    [JsonPropertyName("ts")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("surface")]
    public required string Surface { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("tenant")]
    public string? Tenant { get; init; }

    [JsonPropertyName("apiHost")]
    public string? ApiHost { get; init; }

    [JsonPropertyName("durationMs")]
    public required long DurationMs { get; init; }

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; init; }

    /// <summary>Set instead of an exit code when the invocation ended in a known failure category.</summary>
    [JsonPropertyName("failure")]
    public string? Failure { get; init; }

    [JsonPropertyName("requests")]
    public IReadOnlyList<RequestRecord> Requests { get; init; } = [];
}
