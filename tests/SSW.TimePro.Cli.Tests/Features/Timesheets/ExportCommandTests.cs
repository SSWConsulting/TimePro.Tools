using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.DependencyInjection;
using Spectre.Console.Cli;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Timesheets;

public class ExportCommandTests : IDisposable
{
    private const string Csv =
        "Date,EmpID,ProjectID,Description,Hours\n" +
        "2026-09-01,BOB,1I776Q,\"Product search, phase 2\",8\n" +
        "2026-09-01,ANN,1I776Q,Checkout API,8\n" +
        "2026-09-02,BOB,1I776Q,Order history,4\n";

    private readonly string _output = Path.Combine(Path.GetTempPath(), $"tp-export-{Guid.NewGuid():N}.csv");

    [Fact]
    public async Task Export_ByDefault_KeepsOnlyTheLoggedInEmployee()
    {
        var exitCode = await RunAsync(["export", "--output", _output]);

        exitCode.Should().Be(0);
        DataRows().Should().HaveCount(2).And.OnlyContain(l => l.Contains("BOB"));
    }

    [Fact]
    public async Task Export_WithEmpId_KeepsThatEmployee()
    {
        var exitCode = await RunAsync(["export", "--emp-id", "ANN", "--output", _output]);

        exitCode.Should().Be(0);
        DataRows().Should().HaveCount(1).And.OnlyContain(l => l.Contains("ANN"));
    }

    [Fact]
    public async Task Export_WithAll_WritesTheRawExport()
    {
        var exitCode = await RunAsync(["export", "--all", "--output", _output]);

        exitCode.Should().Be(0);
        File.ReadAllText(_output).Should().Be(Csv);
    }

    [Fact]
    public async Task Export_WithAllAndEmpId_Fails()
    {
        var exitCode = await RunAsync(["export", "--all", "--emp-id", "BOB", "--output", _output]);

        exitCode.Should().NotBe(0);
        File.Exists(_output).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Export_WithNoUsableConfiguredEmpId_FailsBeforeFetchingOrWriting(string? configuredEmpId)
    {
        var (exitCode, api) = await RunWithApiAsync(["export", "--output", _output], configuredEmpId);

        exitCode.Should().Be(1);
        File.Exists(_output).Should().BeFalse();
        await api.DidNotReceiveWithAnyArgs().ExportTimesheetsCsvAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Export_WithAll_IgnoresABlankConfiguredEmpId()
    {
        var (exitCode, _) = await RunWithApiAsync(["export", "--all", "--output", _output], configuredEmpId: "  ");

        exitCode.Should().Be(0);
        File.ReadAllText(_output).Should().Be(Csv);
    }

    [Fact]
    public async Task Export_Filtered_KeepsTheExportsByteOrderMark()
    {
        var payload = Bom.Concat(Encoding.UTF8.GetBytes(Csv)).ToArray();

        var (exitCode, _) = await RunWithApiAsync(["export", "--output", _output], "BOB", payload);

        exitCode.Should().Be(0);
        var written = File.ReadAllBytes(_output);
        written.Take(3).Should().Equal(Bom);
        Encoding.UTF8.GetString(written, 3, written.Length - 3).Should().NotContain("ANN");
    }

    [Fact]
    public async Task Export_WithAll_WritesThePayloadByteForByte()
    {
        // 0x80 is not valid UTF-8 on its own: a decode/encode round trip would mangle it.
        var payload = Bom.Concat(Encoding.UTF8.GetBytes(Csv)).Append((byte)0x80).ToArray();

        var (exitCode, _) = await RunWithApiAsync(["export", "--all", "--output", _output], "BOB", payload);

        exitCode.Should().Be(0);
        File.ReadAllBytes(_output).Should().Equal(payload);
    }

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private string[] DataRows() =>
        File.ReadAllText(_output).Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();

    private static async Task<int> RunAsync(string[] args) => (await RunWithApiAsync(args, "BOB")).ExitCode;

    private static async Task<(int ExitCode, ITimeProApiClient Api)> RunWithApiAsync(
        string[] args, string? configuredEmpId, byte[]? payload = null)
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.ExportTimesheetsCsvAsync(Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(payload ?? Encoding.UTF8.GetBytes(Csv));

        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://timepro.example",
            ApiKey = "test-api-key",
            EmployeeId = configuredEmpId
        });

        var services = new ServiceCollection();
        services.AddSingleton(api);
        services.AddSingleton(config);

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(c =>
        {
            c.PropagateExceptions();
            c.AddCommand<ExportCommand>("export");
        });

        try
        {
            return (await app.RunAsync(args, TestContext.Current.CancellationToken), api);
        }
        catch (CommandRuntimeException)
        {
            return (1, api);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_output))
            File.Delete(_output);
    }
}
