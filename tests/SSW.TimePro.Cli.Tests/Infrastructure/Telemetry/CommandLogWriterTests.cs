using System.Text;
using System.Text.Json;
using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Telemetry;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure.Telemetry;

public class CommandLogWriterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"tp-log-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Write_AppendsOneJsonLinePerInvocation()
    {
        var writer = new CommandLogWriter(_directory);

        writer.Write(Entry("ts get"));
        writer.Write(Entry("ts check"));

        var lines = File.ReadAllLines(writer.CurrentFile);
        lines.Should().HaveCount(2);
        lines.Select(Command).Should().Equal("ts get", "ts check");
    }

    [Fact]
    public void Write_RecordsTheDiagnosticFieldsAndNothingIdentifying()
    {
        var invocation = new ClientInvocation("cli", "ts update");
        invocation.Record(new RequestRecord("req-1", "POST", "/api/Timesheets/SaveTimesheet", 400));

        var tenant = new TenantConfig
        {
            ConfigName = "northwind-staging",
            TenantId = "northwind",
            ApiUrl = "https://api.northwind.example/base?token=secret",
            ApiKey = "super-secret-key",
            EmployeeId = "BOB"
        };

        var writer = new CommandLogWriter(_directory);
        writer.Write(CommandLog.Build(invocation, "0.3.1+abc1234", tenant, durationMs: 412, exitCode: 1));

        var line = File.ReadAllLines(writer.CurrentFile).Single();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        root.GetProperty("version").GetString().Should().Be("0.3.1");
        root.GetProperty("surface").GetString().Should().Be("cli");
        root.GetProperty("command").GetString().Should().Be("ts update");
        root.GetProperty("tenant").GetString().Should().Be("northwind-staging");
        root.GetProperty("apiHost").GetString().Should().Be("api.northwind.example");
        root.GetProperty("durationMs").GetInt64().Should().Be(412);
        root.GetProperty("exitCode").GetInt32().Should().Be(1);
        root.GetProperty("ts").TryGetDateTimeOffset(out _).Should().BeTrue();

        var request = root.GetProperty("requests").EnumerateArray().Single();
        request.GetProperty("requestId").GetString().Should().Be("req-1");
        request.GetProperty("method").GetString().Should().Be("POST");
        request.GetProperty("route").GetString().Should().Be("/api/Timesheets/SaveTimesheet");
        request.GetProperty("status").GetInt32().Should().Be(400);

        line.Should().NotContain("super-secret-key");
        line.Should().NotContain("BOB");
        line.Should().NotContain("token=secret");
    }

    [Fact]
    public void Write_StartsTheFileWithoutAByteOrderMark()
    {
        var writer = new CommandLogWriter(_directory);

        writer.Write(Entry("ts get"));

        File.ReadAllBytes(writer.CurrentFile).Take(3)
            .Should().NotEqual([0xEF, 0xBB, 0xBF], "a BOM makes the first JSONL line unparseable");
        JsonDocument.Parse(File.ReadAllText(writer.CurrentFile, new UTF8Encoding(false)).Split('\n')[0]);
    }

    [Fact]
    public void Write_RotatesAtTheSizeLimitAndKeepsOnlyTheConfiguredNumberOfFiles()
    {
        var writer = new CommandLogWriter(_directory, new CommandLogOptions { MaxBytes = 400, MaxFiles = 3 });

        for (var i = 0; i < 40; i++)
            writer.Write(Entry($"ts get {i}"));

        File.Exists(writer.ArchiveFile(1)).Should().BeTrue();
        File.Exists(writer.ArchiveFile(2)).Should().BeTrue();
        File.Exists(writer.ArchiveFile(3)).Should().BeFalse("MaxFiles = 3 means the current file plus two archives");
        new FileInfo(writer.CurrentFile).Length.Should().BeLessThanOrEqualTo(400);
    }

    [Fact]
    public void Rotate_DropsEntriesOlderThanTheRetentionWindow()
    {
        var writer = new CommandLogWriter(
            _directory,
            new CommandLogOptions { MaxBytes = 400, MaxFiles = 3, RetentionDays = 7 });

        writer.Write(Entry("stale", DateTimeOffset.UtcNow.AddDays(-8)));
        for (var i = 0; i < 10; i++)
            writer.Write(Entry($"fresh {i}"));

        var archived = Enumerable.Range(1, 2)
            .Select(writer.ArchiveFile)
            .Where(File.Exists)
            .SelectMany(File.ReadAllLines)
            .Select(Command)
            .ToList();

        archived.Should().NotContain("stale");
        archived.Should().NotBeEmpty();
    }

    [Fact]
    public void Write_KeepsEveryLineIntact_WhenWritersOverlapAtTheRotationBoundary()
    {
        // Overlapping MCP tool calls and separate tp processes share one file.
        var options = new CommandLogOptions { MaxBytes = 600, MaxFiles = 4 };
        const int writers = 8;
        const int perWriter = 25;

        Parallel.For(0, writers, w =>
        {
            var writer = new CommandLogWriter(_directory, options);
            for (var i = 0; i < perWriter; i++)
                writer.Write(Entry($"ts get {w}-{i}"));
        });

        var reader = new CommandLogWriter(_directory, options);
        var files = new[] { reader.CurrentFile }
            .Concat(Enumerable.Range(1, options.MaxFiles - 1).Select(reader.ArchiveFile))
            .Where(File.Exists);

        var lines = files.SelectMany(File.ReadAllLines).Where(l => l.Length > 0).ToList();

        lines.Should().NotBeEmpty();
        foreach (var line in lines)
            JsonDocument.Parse(line).RootElement.TryGetProperty("command", out _)
                .Should().BeTrue("an interleaved write must not tear a line");
    }

    [Fact]
    public void Write_RestrictsTheLogToTheOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes only.");
            return;
        }

        var writer = new CommandLogWriter(_directory);
        writer.Write(Entry("ts get"));

        File.GetUnixFileMode(writer.CurrentFile)
            .Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void Write_SwallowsFailures_SoTheCommandStillSucceeds()
    {
        var blocked = Path.Combine(_directory, "blocked");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(blocked, "this path is a file, not a directory");

        var act = () => new CommandLogWriter(blocked).Write(Entry("ts get"));

        act.Should().NotThrow();
    }

    private static string Command(string line) =>
        JsonDocument.Parse(line).RootElement.GetProperty("command").GetString()!;

    private static CommandLogEntry Entry(string command, DateTimeOffset? timestamp = null) => new()
    {
        Timestamp = timestamp ?? DateTimeOffset.UtcNow,
        Version = "0.3.1",
        Surface = "cli",
        Command = command,
        Tenant = "northwind-staging",
        ApiHost = "api.northwind.example",
        DurationMs = 10,
        ExitCode = 0
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }
}
