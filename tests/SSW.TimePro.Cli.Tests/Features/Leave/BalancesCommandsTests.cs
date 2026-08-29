using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SSW.TimePro.Cli.Features.Leave;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.DependencyInjection;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console.Cli;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Leave;

public class BalancesCommandsTests : IDisposable
{
    private const string ValidCsv = "Employee,Leave Type,Units\nJane Doe,Annual Leave,76.00\n";

    private readonly string _tempDir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "tp-balance-cmd-tests", Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public async Task Import_WithValidCsvAndJson_CallsApiWithoutPrompting()
    {
        var api = Substitute.For<ITimeProApiClient>();
        string? sent = null;
        api.ImportLeaveBalancesAsync(Arg.Do<string>(v => sent = v), Arg.Any<CancellationToken>())
            .Returns(new ImportLeaveBalancesResult { AsAtDate = new DateOnly(2026, 8, 1), Created = 3, Updated = 4 });
        var app = CreateApp(api);
        var path = WriteFile("balances.csv", ValidCsv);

        // --json implies non-interactive, so no confirmation should be required.
        var exitCode = await app.RunAsync(["import", path, "--json"], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        sent.Should().Be(ValidCsv);
    }

    [Fact]
    public async Task Import_WhenFileMissing_FailsBeforeCallingApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var app = CreateApp(api);

        var exitCode = await app.RunAsync(
            ["import", Path.Combine(_tempDir, "nope.csv"), "--json"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.ImportLeaveBalancesAsync));
    }

    [Fact]
    public async Task Import_WhenNotLoggedIn_FailsBeforeReadingFile()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var app = CreateApp(api, loggedIn: false);
        var path = WriteFile("balances.csv", ValidCsv);

        var exitCode = await app.RunAsync(["import", path, "--json"], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.ImportLeaveBalancesAsync));
    }

    [Fact]
    public async Task Import_WhenApiRejectsCsv_ReturnsFailureExitCode()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.ImportLeaveBalancesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ImportLeaveBalancesResult?>>(_ => throw new ApiException(
                422, "Unprocessable Entity", "\"Missing required column 'Units'.\""));
        var app = CreateApp(api);
        var path = WriteFile("balances.csv", ValidCsv);

        var exitCode = await app.RunAsync(["import", path, "--json"], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Status_WhenNothingImported_SucceedsWithoutError()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetLeaveBalanceStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new LeaveBalanceStatus
            {
                AsAtDate = null,
                LastImportedAt = null,
                EmployeeCount = 0,
                IsStale = false
            });
        var app = CreateApp(api);

        var exitCode = await app.RunAsync(["status", "--json"], TestContext.Current.CancellationToken);

        // "Never imported" is a valid state to report, not a failure.
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Status_WhenBalancesStored_Succeeds()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetLeaveBalanceStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new LeaveBalanceStatus
            {
                AsAtDate = new DateOnly(2026, 8, 1),
                LastImportedAt = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero),
                EmployeeCount = 42,
                IsStale = true
            });
        var app = CreateApp(api);

        var exitCode = await app.RunAsync(["status", "--json"], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        await api.Received(1).GetLeaveBalanceStatusAsync(Arg.Any<CancellationToken>());
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static CommandApp CreateApp(ITimeProApiClient api, bool loggedIn = true)
    {
        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(loggedIn
            ? new TenantConfig
            {
                TenantId = "test",
                ApiUrl = "https://timepro.example",
                ApiKey = "test-api-key",
                EmployeeId = "TST"
            }
            : null);

        var services = new ServiceCollection();
        services.AddSingleton(api);
        services.AddSingleton(config);
        services.AddSingleton<LeaveBalanceImportService>();

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(configurator =>
        {
            configurator.AddCommand<BalancesImportCommand>("import");
            configurator.AddCommand<BalancesStatusCommand>("status");
        });
        return app;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }
}
