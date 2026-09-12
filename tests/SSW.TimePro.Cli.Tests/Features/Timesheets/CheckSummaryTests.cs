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

public class CheckSummaryTests
{
    // The mocked week returns one suggested entry per checked weekday (Mon–Fri).
    private const int ExpectedPendingSuggestions = 5;

    [Fact]
    public void Summarize_WithNoIssuesAndNoSuggestions_IsAllClear()
    {
        var summary = CheckEvaluator.Summarize(0, 0, 0, pendingSuggestions: 0, allCovered: true, strict: false);

        summary.Severity.Should().Be("success");
        summary.Message.Should().Be("All clear — every day covered");
        summary.Failed.Should().BeFalse();
        summary.StrictFailure.Should().BeFalse();
    }

    [Fact]
    public void Summarize_WithPendingSuggestions_StatesThemAlongsideCoverage()
    {
        var summary = CheckEvaluator.Summarize(0, 0, 3, pendingSuggestions: 3, allCovered: true, strict: false);

        summary.Severity.Should().Be("warning");
        summary.Message.Should().Be("All days covered; 3 suggestions still need accepting");
        summary.Failed.Should().BeFalse();
    }

    [Fact]
    public void Summarize_WithOnePendingSuggestion_UsesSingularWording()
    {
        var summary = CheckEvaluator.Summarize(0, 0, 1, pendingSuggestions: 1, allCovered: true, strict: false);

        summary.Message.Should().Be("All days covered; 1 suggestion still needs accepting");
    }

    [Fact]
    public void Summarize_WithPendingSuggestionsAndStrict_Fails()
    {
        var summary = CheckEvaluator.Summarize(0, 0, 3, pendingSuggestions: 3, allCovered: true, strict: true);

        summary.Severity.Should().Be("error");
        summary.Message.Should().Be("All days covered; 3 suggestions still need accepting");
        summary.Failed.Should().BeTrue();
        summary.StrictFailure.Should().BeTrue();
    }

    [Fact]
    public void Summarize_WithErrors_KeepsTheCountsLineAndStillFails()
    {
        var summary = CheckEvaluator.Summarize(1, 2, 4, pendingSuggestions: 2, allCovered: false, strict: false);

        summary.Severity.Should().Be("error");
        summary.Message.Should().Be("1 error(s), 2 warning(s), 4 info(s); 2 suggestions still need accepting");
        summary.Failed.Should().BeTrue();
    }

    [Fact]
    public async Task Check_Json_CarriesPendingSuggestions()
    {
        var (exitCode, stdout) = await RunAsync(["check", "--week", "--json"]);

        exitCode.Should().Be(0);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        root.GetProperty("allCovered").GetBoolean().Should().BeTrue();
        root.GetProperty("pendingSuggestions").GetInt32().Should().Be(ExpectedPendingSuggestions);
    }

    [Fact]
    public async Task Check_Strict_ExitsNonZeroForPendingSuggestions()
    {
        var (plain, _) = await RunAsync(["check", "--week"]);
        var (strict, _) = await RunAsync(["check", "--week", "--strict"]);

        plain.Should().Be(0);
        strict.Should().Be(1);
    }

    private static async Task<(int ExitCode, string Stdout)> RunAsync(string[] args)
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetTimesheetsAsync("BOB", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([
                new TimesheetItem { TimeId = 1, TotalTime = 8m, Notes = "Product search", HasNotes = true, IsSuggested = false },
                new TimesheetItem { TimeId = 2, TotalTime = 2m, Notes = "Checkout API", HasNotes = true, IsSuggested = true }
            ]);
        api.GetLeaveAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((LeaveListResponse?)null);

        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://timepro.example",
            ApiKey = "test-api-key",
            EmployeeId = "BOB"
        });

        var services = new ServiceCollection();
        services.AddSingleton(api);
        services.AddSingleton(config);

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(c => c.AddCommand<CheckCommand>("check"));

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
