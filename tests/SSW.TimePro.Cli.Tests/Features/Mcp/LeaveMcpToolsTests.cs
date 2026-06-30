using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using SSW.TimePro.Cli.Features.Mcp.Tools;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Mcp;

public class LeaveMcpToolsTests
{
    [Fact]
    public async Task CreateLeave_WhenProfileTimezoneAvailable_SendsDateOffsetsFromProfileTimezone()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://timepro.example",
            ApiKey = "test-api-key",
            EmployeeId = "TST"
        });

        CreateLeaveRequest? request = null;
        api.CreateLeaveAsync(Arg.Do<CreateLeaveRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (timeZoneId, timeZone) = FindTimeZone("Australia/Brisbane", "E. Australia Standard Time", "UTC");
        api.GetEmployeeSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmployeeSettings { TimezoneId = timeZoneId });
        var expectedOffset = FormatOffset(timeZone.GetUtcOffset(new DateTime(2026, 3, 30, 0, 0, 0)));
        var tools = new LeaveMcpTools(api, config);

        var json = await tools.CreateLeave(
            start: "2026-03-30",
            end: "2026-03-30",
            type: "1",
            note: "Annual leave",
            ct: TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        request.Should().NotBeNull();
        request!.RequestedEmpId.Should().Be("TST");
        request.StartDate.Should().Be($"2026-03-30T00:00:00.0000000{expectedOffset}");
        request.EndDate.Should().Be($"2026-03-30T23:59:00.0000000{expectedOffset}");
        request.Note.Should().Be("Annual leave");
    }

    [Fact]
    public async Task CreateLeave_WhenProfileTimezoneMissing_UsesMachineTimezone()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://timepro.example",
            ApiKey = "test-api-key",
            EmployeeId = "TST"
        });

        api.GetEmployeeSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmployeeSettings { TimezoneId = null });
        CreateLeaveRequest? request = null;
        api.CreateLeaveAsync(Arg.Do<CreateLeaveRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var expectedOffset = FormatOffset(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 3, 30, 0, 0, 0)));
        var tools = new LeaveMcpTools(api, config);

        var json = await tools.CreateLeave(
            start: "2026-03-30",
            end: "2026-03-30",
            type: "1",
            note: "Annual leave",
            ct: TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        request.Should().NotBeNull();
        request!.StartDate.Should().Be($"2026-03-30T00:00:00.0000000{expectedOffset}");
        request.EndDate.Should().Be($"2026-03-30T23:59:00.0000000{expectedOffset}");
    }

    [Fact]
    public async Task CreateLeave_WhenTimezoneOverrideProvided_UsesOverrideBeforeProfileTimezone()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://timepro.example",
            ApiKey = "test-api-key",
            EmployeeId = "TST"
        });

        CreateLeaveRequest? request = null;
        api.CreateLeaveAsync(Arg.Do<CreateLeaveRequest>(r => request = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (timeZoneId, timeZone) = FindTimeZone("Pacific/Auckland", "New Zealand Standard Time", "UTC");
        var expectedOffset = FormatOffset(timeZone.GetUtcOffset(new DateTime(2026, 3, 30, 0, 0, 0)));
        var tools = new LeaveMcpTools(api, config);

        var json = await tools.CreateLeave(
            start: "2026-03-30",
            end: "2026-03-30",
            type: "1",
            note: "Annual leave",
            timeZoneId: timeZoneId,
            ct: TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        request.Should().NotBeNull();
        request!.StartDate.Should().Be($"2026-03-30T00:00:00.0000000{expectedOffset}");
        request.EndDate.Should().Be($"2026-03-30T23:59:00.0000000{expectedOffset}");
        api.ReceivedCalls()
            .Should()
            .NotContain(call => call.GetMethodInfo().Name == nameof(ITimeProApiClient.GetEmployeeSettingsAsync));
    }

    [Fact]
    public async Task CreateLeave_WhenNoteMissing_ReturnsErrorWithoutCallingApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://timepro.example",
            ApiKey = "test-api-key",
            EmployeeId = "TST"
        });

        var tools = new LeaveMcpTools(api, config);

        var json = await tools.CreateLeave(
            start: "2026-03-30",
            end: "2026-03-30",
            type: "1",
            note: " ",
            ct: TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetString().Should().Contain("note is required");
        api.ReceivedCalls()
            .Should()
            .NotContain(call => call.GetMethodInfo().Name == nameof(ITimeProApiClient.CreateLeaveAsync));
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
