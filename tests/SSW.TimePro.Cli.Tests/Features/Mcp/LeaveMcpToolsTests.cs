using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using SSW.TimePro.Cli.Features.Leave;
using SSW.TimePro.Cli.Features.Mcp.Tools;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Mcp;

public class LeaveMcpToolsTests
{
    [Fact]
    public void GetLeaveBalanceStatus_IsMarkedReadOnly()
    {
        var attribute = typeof(LeaveMcpTools)
            .GetMethod(nameof(LeaveMcpTools.GetLeaveBalanceStatus))!
            .GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
            .Cast<McpServerToolAttribute>()
            .Single();

        attribute.ReadOnly.Should().BeTrue();
        attribute.Destructive.Should().BeFalse();
    }

    [Fact]
    public async Task GetLeaveEntries_WhenFilterIsUnknown_ReturnsTheValidValuesWithoutCallingTheApi()
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
        var tools = CreateTools(api, config);

        var json = await tools.GetLeaveEntries(filter: "NOPE", ct: TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetString().Should().Contain("UPCOMING, PAST, ALL");
        await api.DidNotReceive().GetLeaveAsync(
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

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
        var tools = CreateTools(api, config);

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
        var tools = CreateTools(api, config);

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
        var tools = CreateTools(api, config);

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

        var tools = CreateTools(api, config);

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

    [Fact]
    public async Task CreateLeave_WhenDryRun_ReturnsPayloadWithoutCallingApi()
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
            .Returns(new EmployeeSettings { TimezoneId = "UTC" });
        var tools = CreateTools(api, config);

        var json = await tools.CreateLeave(
            start: "2026-03-30",
            end: "2026-03-30",
            type: "1",
            note: "Annual leave",
            dryRun: true,
            ct: TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("dryRun").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("request").GetProperty("note").GetString().Should().Be("Annual leave");
        api.ReceivedCalls()
            .Should()
            .NotContain(call => call.GetMethodInfo().Name == nameof(ITimeProApiClient.CreateLeaveAsync));
    }

    [Fact]
    public async Task UpdateLeave_WhenChangingNote_PreservesExistingFields()
    {
        const string leaveId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        var api = Substitute.For<ITimeProApiClient>();
        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://timepro.example",
            ApiKey = "test-api-key",
            EmployeeId = "TST"
        });
        api.GetLeaveAsync("UPCOMING", 1, 100, "TST", Arg.Any<CancellationToken>())
            .Returns(new LeaveListResponse
            {
                Leaves = new PaginatedList<LeaveEntry>
                {
                    PageNumber = 1,
                    PageSize = 100,
                    TotalItems = 1,
                    TotalPages = 1,
                    Items =
                    [
                        new LeaveEntry
                        {
                            Id = leaveId,
                            RequestedEmpId = "TST",
                            StartDate = "2026-03-30T00:00:00+10:00",
                            EndDate = "2026-03-30T23:59:00+10:00",
                            UserStartTime = "07:30",
                            UserEndTime = "16:00",
                            Note = "Original plans",
                            ApprovedBy = "approver@northwind.example",
                            OptionalEmp = ["notify@northwind.example"],
                            LeaveType = new LeaveTypeInfo { Id = 1, Name = "Annual Leave", IsActive = true },
                            AllDay = true
                        }
                    ]
                }
            });
        api.GetEmployeeSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmployeeSettings { StartTime = "09:00", EndTime = "18:00", TimezoneId = "UTC" });
        UpdateLeaveRequest? request = null;
        api.UpdateLeaveAsync(Arg.Do<UpdateLeaveRequest>(value => request = value), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var tools = CreateTools(api, config);

        var json = await tools.UpdateLeave(
            id: leaveId,
            note: "Updated plans",
            ct: TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        request.Should().NotBeNull();
        request!.Note.Should().Be("Updated plans");
        request.OptionalEmp.Should().Equal("notify@northwind.example");
        request.ApprovedBy.Should().Be("approver@northwind.example");
        request.LeaveTypeId.Should().Be(1);
        request.UserStartTime.Should().Be("07:30:00");
        request.UserEndTime.Should().Be("16:00:00");

        api.ClearReceivedCalls();
        var dryRunJson = await tools.UpdateLeave(
            id: leaveId,
            note: "Another update",
            dryRun: true,
            ct: TestContext.Current.CancellationToken);

        using var dryRunDoc = JsonDocument.Parse(dryRunJson);
        dryRunDoc.RootElement.GetProperty("dryRun").GetBoolean().Should().BeTrue();
        dryRunDoc.RootElement.GetProperty("request").GetProperty("note").GetString().Should().Be("Another update");
        api.ReceivedCalls()
            .Should()
            .NotContain(call => call.GetMethodInfo().Name == nameof(ITimeProApiClient.UpdateLeaveAsync));
    }

    [Fact]
    public async Task GetLeaveBalanceStatus_WhenNothingImported_ReportsNotImported()
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
        api.GetLeaveBalanceStatusAsync(Arg.Any<CancellationToken>()).Returns(new LeaveBalanceStatus
        {
            AsAtDate = null,
            LastImportedAt = null,
            EmployeeCount = 0,
            IsStale = false
        });
        var tools = CreateTools(api, config);

        var json = await tools.GetLeaveBalanceStatus(TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("imported").GetBoolean().Should().BeFalse();
    }

    private static LeaveMcpTools CreateTools(ITimeProApiClient api, IConfigService config) =>
        new(api,
            config,
            new LeaveCreateService(api, new LeaveLookup(api)),
            new LeaveUpdateService(api, new LeaveLookup(api)),
            new LeaveListService(api));

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
