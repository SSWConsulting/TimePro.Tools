using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.DependencyInjection;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console.Cli;
using Xunit;

using LeaveCreateCommand = SSW.TimePro.Cli.Features.Leave.CreateCommand;

namespace SSW.TimePro.Cli.Tests.Features.Leave;

public class CreateCommandTests
{
    [Fact]
    public async Task Create_WhenNoteMissing_ReturnsErrorWithoutCallingApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "create",
            "--start", "2026-03-30",
            "--end", "2026-03-30",
            "--type", "1",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.CreateLeaveAsync));
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.GetLeaveTypesAsync));
    }

    [Fact]
    public async Task Create_WhenNoteWhitespace_ReturnsErrorWithoutCallingApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "create",
            "--start", "2026-03-30",
            "--end", "2026-03-30",
            "--type", "1",
            "--note", "   ",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.CreateLeaveAsync));
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.GetLeaveTypesAsync));
    }

    [Fact]
    public async Task Create_WhenStartDateIsWeekend_ReturnsErrorWithoutCallingApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetEmployeeSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmployeeSettings { TimezoneId = "UTC" });
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "create",
            "--start", "2026-03-28",
            "--end", "2026-03-30",
            "--type", "1",
            "--note", "Annual leave",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.CreateLeaveAsync));
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.GetLeaveTypesAsync));
    }

    [Fact]
    public async Task Create_WhenProfileTimezoneAvailable_SendsDateOffsetsFromProfileTimezone()
    {
        var api = Substitute.For<ITimeProApiClient>();
        CreateLeaveRequest? request = null;
        api.CreateLeaveAsync(Arg.Do<CreateLeaveRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var (timeZoneId, timeZone) = FindTimeZone("Australia/Brisbane", "E. Australia Standard Time", "UTC");
        api.GetEmployeeSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmployeeSettings { TimezoneId = timeZoneId });
        var app = CreateApp(api);
        var expectedOffset = FormatOffset(timeZone.GetUtcOffset(new DateTime(2026, 3, 30, 0, 0, 0)));

        var exitCode = await app.RunAsync([
            "create",
            "--start", "2026-03-30",
            "--end", "2026-03-30",
            "--type", "1",
            "--note", "Annual leave",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        request.Should().NotBeNull();
        request!.StartDate.Should().Be($"2026-03-30T00:00:00.0000000{expectedOffset}");
        request.EndDate.Should().Be($"2026-03-30T23:59:00.0000000{expectedOffset}");
        request.Note.Should().Be("Annual leave");
    }

    [Fact]
    public async Task Create_WhenProfileTimezoneMissing_UsesMachineTimezone()
    {
        var api = Substitute.For<ITimeProApiClient>();
        CreateLeaveRequest? request = null;
        api.GetEmployeeSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmployeeSettings { TimezoneId = null });
        api.CreateLeaveAsync(Arg.Do<CreateLeaveRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var app = CreateApp(api);
        var expectedOffset = FormatOffset(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 3, 30, 0, 0, 0)));

        var exitCode = await app.RunAsync([
            "create",
            "--start", "2026-03-30",
            "--end", "2026-03-30",
            "--type", "1",
            "--note", "Annual leave",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        request.Should().NotBeNull();
        request!.StartDate.Should().Be($"2026-03-30T00:00:00.0000000{expectedOffset}");
        request.EndDate.Should().Be($"2026-03-30T23:59:00.0000000{expectedOffset}");
    }

    [Fact]
    public async Task Create_WhenDateTimeHasNoOffset_UsesProfileTimezone()
    {
        var api = Substitute.For<ITimeProApiClient>();
        CreateLeaveRequest? request = null;
        api.CreateLeaveAsync(Arg.Do<CreateLeaveRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var (timeZoneId, timeZone) = FindTimeZone("Pacific/Auckland", "New Zealand Standard Time", "UTC");
        api.GetEmployeeSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmployeeSettings { TimezoneId = timeZoneId });
        var app = CreateApp(api);
        var expectedOffset = FormatOffset(timeZone.GetUtcOffset(new DateTime(2026, 3, 30, 9, 30, 0)));

        var exitCode = await app.RunAsync([
            "create",
            "--start", "2026-03-30T09:30:00",
            "--end", "2026-03-30T17:30:00",
            "--type", "1",
            "--note", "Annual leave",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        request.Should().NotBeNull();
        request!.StartDate.Should().Be($"2026-03-30T09:30:00.0000000{expectedOffset}");
        request.EndDate.Should().Be($"2026-03-30T17:30:00.0000000{expectedOffset}");
    }

    [Fact]
    public async Task Create_WhenTimezoneOverrideProvided_UsesOverrideBeforeProfileTimezone()
    {
        var api = Substitute.For<ITimeProApiClient>();
        CreateLeaveRequest? request = null;
        api.CreateLeaveAsync(Arg.Do<CreateLeaveRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var (timeZoneId, timeZone) = FindTimeZone("Pacific/Auckland", "New Zealand Standard Time", "UTC");
        var app = CreateApp(api);
        var expectedOffset = FormatOffset(timeZone.GetUtcOffset(new DateTime(2026, 3, 30, 0, 0, 0)));

        var exitCode = await app.RunAsync([
            "create",
            "--start", "2026-03-30",
            "--end", "2026-03-30",
            "--type", "1",
            "--note", "Annual leave",
            "--timezone", timeZoneId,
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        request.Should().NotBeNull();
        request!.StartDate.Should().Be($"2026-03-30T00:00:00.0000000{expectedOffset}");
        request.EndDate.Should().Be($"2026-03-30T23:59:00.0000000{expectedOffset}");
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.GetEmployeeSettingsAsync));
    }

    [Fact]
    public async Task Create_WhenTimezoneOverrideUnknown_ReturnsErrorWithoutCallingApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "create",
            "--start", "2026-03-30",
            "--end", "2026-03-30",
            "--type", "1",
            "--note", "Annual leave",
            "--timezone", "Mars/Olympus_Mons",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.GetEmployeeSettingsAsync));
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.CreateLeaveAsync));
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.GetLeaveTypesAsync));
    }

    [Fact]
    public async Task Create_WhenProfileTimezoneUnknown_ReturnsErrorWithoutCallingApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetEmployeeSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmployeeSettings { TimezoneId = "Mars/Olympus_Mons" });
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "create",
            "--start", "2026-03-30",
            "--end", "2026-03-30",
            "--type", "1",
            "--note", "Annual leave",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.CreateLeaveAsync));
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.GetLeaveTypesAsync));
    }

    private static CommandApp CreateApp(ITimeProApiClient api)
    {
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.GetCurrentTenant().Returns(new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://timepro.example",
            ApiKey = "test-api-key",
            EmployeeId = "TST"
        });

        var services = new ServiceCollection();
        services.AddSingleton(api);
        services.AddSingleton(tenantProvider);

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(config =>
        {
            config.AddCommand<LeaveCreateCommand>("create");
        });
        return app;
    }

    private static (string id, TimeZoneInfo timeZone) FindTimeZone(params string[] ids)
    {
        foreach (var id in ids)
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var timeZone))
                return (id, timeZone);
        }

        throw new InvalidOperationException("No test timezone was available.");
    }

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        offset = offset.Duration();
        return $"{sign}{offset.Hours:00}:{offset.Minutes:00}";
    }
}

internal static class SubstituteApiAssertions
{
    public static void ShouldNotHaveReceived(this ITimeProApiClient api, string methodName)
    {
        api.ReceivedCalls()
            .Should()
            .NotContain(call => call.GetMethodInfo().Name == methodName);
    }
}
