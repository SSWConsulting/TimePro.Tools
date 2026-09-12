using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.DependencyInjection;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console.Cli;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Timesheets;

public class LessOptionTests
{
    private const string UnitMessage = "--less expects whole minutes, e.g. --less 90";
    private const string RangeMessage = "--less must be zero or greater";

    [Theory]
    [InlineData("1.5", false, null, UnitMessage)]
    [InlineData("abc", false, null, UnitMessage)]
    [InlineData("", false, null, UnitMessage)]
    [InlineData("   ", false, null, UnitMessage)]
    [InlineData("-1", false, null, RangeMessage)]
    [InlineData("  90  ", true, 90, null)]
    [InlineData("0", true, 0, null)]
    [InlineData(null, true, null, null)]
    public void TryParse_AcceptsWholeMinutesAndRejectsEverythingElse(
        string? raw,
        bool expectedResult,
        int? expectedMinutes,
        string? expectedErrorPrefix)
    {
        var result = LessOption.TryParse(raw, out var minutes, out var error);

        result.Should().Be(expectedResult);
        minutes.Should().Be(expectedMinutes);
        if (expectedErrorPrefix is null)
            error.Should().BeNull();
        else
            error.Should().StartWith(expectedErrorPrefix);
    }

    [Theory]
    [InlineData("create", "1.5", UnitMessage)]
    [InlineData("create", "abc", UnitMessage)]
    [InlineData("create", "", UnitMessage)]
    [InlineData("create", "-1", RangeMessage)]
    [InlineData("update", "1.5", UnitMessage)]
    [InlineData("update", "abc", UnitMessage)]
    [InlineData("update", "", UnitMessage)]
    [InlineData("update", "-1", RangeMessage)]
    public async Task Command_WhenLessIsInvalid_WritesEnvelopeAndSkipsTheApi(
        string command,
        string less,
        string expectedMessagePrefix)
    {
        var api = Substitute.For<ITimeProApiClient>();
        string[] args = command == "create"
            ? ["create", "--client", "NWIND", "--project", "NW0002", "--less", less, "--yes", "--json"]
            : ["update", "42", "--less", less, "--json"];

        var (exitCode, stdout) = await RunAsync(api, args);

        exitCode.Should().Be(1);
        using var doc = JsonDocument.Parse(stdout);
        doc.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().StartWith(expectedMessagePrefix);

        await api.DidNotReceive().CreateTimesheetAsync(Arg.Any<TimesheetRequest>(), Arg.Any<CancellationToken>());
        await api.DidNotReceive().UpdateTimesheetAsync(Arg.Any<TimesheetRequest>(), Arg.Any<CancellationToken>());
    }

    private static async Task<(int ExitCode, string Stdout)> RunAsync(ITimeProApiClient api, string[] args)
    {
        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://timepro.example",
            ApiKey = "test-api-key",
            EmployeeId = "TST"
        });
        config.LoadGlobalConfig().Returns(new GlobalConfig());

        var services = new ServiceCollection();
        services.AddSingleton(api);
        services.AddSingleton(config);
        services.AddSingleton<TimesheetCreateService>();
        services.AddSingleton<TimesheetUpdateService>();

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(configurator =>
        {
            configurator.AddCommand<CreateCommand>("create");
            configurator.AddCommand<UpdateCommand>("update");
        });

        var original = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            var exitCode = await app.RunAsync(args, TestContext.Current.CancellationToken);
            return (exitCode, writer.ToString().Trim());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
