using System.Text.Json;
using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Telemetry;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure.Telemetry;

public class CommandLogTests
{
    [Fact]
    public void Record_DoesNotThrow_WhenLoadingConfigFails()
    {
        // A command that succeeded must not end with an exception raised by its own diagnostics.
        var act = () => CommandLog.Record(
            new ClientInvocation("cli", "ts get"),
            () => throw new JsonException("config.json is not valid JSON"),
            () => throw new InvalidOperationException("no tenant"),
            durationMs: 10,
            exitCode: 0);

        act.Should().NotThrow();
    }

    [Fact]
    public void Record_DoesNotThrow_WhenTheTenantCannotBeLoaded()
    {
        var act = () => CommandLog.Record(
            new ClientInvocation("cli", "ts get"),
            () => new GlobalConfig { Telemetry = new TelemetryConfig { LocalLog = false } },
            () => throw new InvalidOperationException("no tenant"),
            durationMs: 10,
            exitCode: 0);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("""{"telemetry":null}""")]
    [InlineData("""{}""")]
    public void ATelemetrySectionThatIsAbsentOrNull_LeavesTheLogEnabled(string json)
    {
        var config = JsonSerializer.Deserialize<GlobalConfig>(json);

        // Record() treats anything other than an explicit false as enabled.
        (config?.Telemetry is { LocalLog: false }).Should().BeFalse();
    }

    [Fact]
    public void AnExplicitFalse_DisablesTheLog()
    {
        var config = JsonSerializer.Deserialize<GlobalConfig>("""{"telemetry":{"localLog":false}}""");

        (config?.Telemetry is { LocalLog: false }).Should().BeTrue();
    }

    [Fact]
    public void Build_ReportsTheApiHostWithoutPathOrQuery()
    {
        var entry = CommandLog.Build(
            new ClientInvocation("cli", "ts get"),
            "0.3.1+abc1234",
            new TenantConfig
            {
                ConfigName = "northwind-staging",
                ApiUrl = "https://api.northwind.example/base?token=secret"
            },
            durationMs: 5,
            exitCode: 0);

        entry.ApiHost.Should().Be("api.northwind.example");
        entry.Version.Should().Be("0.3.1");
    }

    [Fact]
    public void Build_LeavesTheHostNull_WhenTheApiUrlIsMalformed()
    {
        var entry = CommandLog.Build(
            new ClientInvocation("cli", "ts get"),
            "0.3.1",
            new TenantConfig { ConfigName = "northwind-staging", ApiUrl = "not a url" },
            durationMs: 5,
            exitCode: 0);

        entry.ApiHost.Should().BeNull();
    }
}
