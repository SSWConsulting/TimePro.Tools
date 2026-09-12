using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SSW.TimePro.Cli.Infrastructure.Telemetry;

public sealed record CommandLogOptions
{
    public long MaxBytes { get; init; } = 2 * 1024 * 1024;
    public int MaxFiles { get; init; } = 5;
    public int RetentionDays { get; init; } = 7;
}

/// <summary>
/// Appends one JSON line per invocation to a rolling local log. Every failure here is swallowed:
/// a log that cannot be written must never be the reason a <c>tp</c> command fails.
/// </summary>
public sealed class CommandLogWriter
{
    public const string FileName = "commands.jsonl";

    // No BOM: the file is read line by line as JSON, and a BOM makes the first line unparseable.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions LineOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _directory;
    private readonly CommandLogOptions _options;

    public CommandLogWriter(string directory, CommandLogOptions? options = null)
    {
        _directory = directory;
        _options = options ?? new CommandLogOptions();
    }

    public string CurrentFile => Path.Combine(_directory, FileName);

    public string ArchiveFile(int index) => Path.Combine(_directory, $"commands.{index}.jsonl");

    public void Write(CommandLogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            RestrictToOwner(_directory, directory: true);

            var line = JsonSerializer.Serialize(entry, LineOptions) + "\n";

            var current = CurrentFile;
            if (File.Exists(current) && new FileInfo(current).Length + line.Length > _options.MaxBytes)
                Rotate();

            File.AppendAllText(current, line, Utf8NoBom);
            RestrictToOwner(current, directory: false);
        }
        catch
        {
            // Diagnostics are best-effort; never fail the command because of them.
        }
    }

    private void Rotate()
    {
        var oldest = ArchiveFile(_options.MaxFiles - 1);
        if (File.Exists(oldest))
            File.Delete(oldest);

        for (var i = _options.MaxFiles - 2; i >= 1; i--)
        {
            var source = ArchiveFile(i);
            if (File.Exists(source))
                File.Move(source, ArchiveFile(i + 1), overwrite: true);
        }

        File.Move(CurrentFile, ArchiveFile(1), overwrite: true);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays);
        for (var i = 1; i <= _options.MaxFiles - 1; i++)
            PruneOlderThan(ArchiveFile(i), cutoff);
    }

    private static void PruneOlderThan(string path, DateTimeOffset cutoff)
    {
        if (!File.Exists(path))
            return;

        var kept = File.ReadLines(path).Where(line => !IsOlderThan(line, cutoff)).ToList();
        if (kept.Count == 0)
        {
            File.Delete(path);
            return;
        }

        File.WriteAllLines(path, kept, Utf8NoBom);
        RestrictToOwner(path, directory: false);
    }

    private static bool IsOlderThan(string line, DateTimeOffset cutoff)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty("ts", out var ts)
                   && ts.TryGetDateTimeOffset(out var timestamp)
                   && timestamp < cutoff;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void RestrictToOwner(string path, bool directory)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(
            path,
            directory
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
