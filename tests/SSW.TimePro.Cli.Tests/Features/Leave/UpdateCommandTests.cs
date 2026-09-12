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

using LeaveUpdateCommand = SSW.TimePro.Cli.Features.Leave.UpdateCommand;

namespace SSW.TimePro.Cli.Tests.Features.Leave;

public class UpdateCommandTests
{
    private const string LeaveId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

    [Fact]
    public async Task Update_WhenChangingNote_PreservesUnspecifiedFields()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureExistingLeave(api);
        UpdateLeaveRequest? request = null;
        api.UpdateLeaveAsync(Arg.Do<UpdateLeaveRequest>(value => request = value), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            LeaveId,
            "--note", "Updated plans",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        request.Should().NotBeNull();
        request!.Id.Should().Be(LeaveId);
        request.RequestedEmpId.Should().Be("TST");
        request.Note.Should().Be("Updated plans");
        request.LeaveTypeId.Should().Be(1);
        request.AllDay.Should().BeTrue();
        request.OptionalEmp.Should().Equal("notify@northwind.example");
        request.ApprovedBy.Should().Be("approver@northwind.example");
        request.TimeLessOverride.Should().Be(1.5m);
        request.UserStartTime.Should().Be("07:30:00");
        request.UserEndTime.Should().Be("16:00:00");
    }

    [Fact]
    public async Task Update_WhenNoChangesSpecified_DoesNotCallUpdateApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureExistingLeave(api);
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            LeaveId,
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.UpdateLeaveAsync));
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.GetEmployeeSettingsAsync));
    }

    [Fact]
    public async Task Update_WhenDryRun_PreparesPayloadWithoutCallingUpdateApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureExistingLeave(api);
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            LeaveId,
            "--note", "Updated plans",
            "--dry-run",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.UpdateLeaveAsync));
        await api.Received(1).GetEmployeeSettingsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_WhenProfileTimezoneUnknown_ReturnsErrorWithoutCallingUpdateApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureExistingLeave(api);
        api.GetEmployeeSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmployeeSettings { TimezoneId = "Mars/Olympus_Mons" });
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            LeaveId,
            "--note", "Updated plans",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.UpdateLeaveAsync));
    }

    [Fact]
    public async Task Update_WhenChangingDatesTypeAndDayMode_UsesProfileTimezoneAndClearsRequestedFields()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureExistingLeave(api);
        api.GetEmployeeSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmployeeSettings
            {
                StartTime = "08:30",
                EndTime = "17:00",
                TimezoneId = "Australia/Brisbane"
            });
        api.GetLeaveTypesAsync(Arg.Any<CancellationToken>())
            .Returns([new LeaveTypeInfo { Id = 2, Name = "Personal Leave", IsActive = true }]);
        UpdateLeaveRequest? request = null;
        api.UpdateLeaveAsync(Arg.Do<UpdateLeaveRequest>(value => request = value), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            LeaveId,
            "--start", "2026-04-01",
            "--end", "2026-04-01",
            "--type", "Personal Leave",
            "--half-day",
            "--clear-approved-by",
            "--clear-cc",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        request.Should().NotBeNull();
        request!.StartDate.Should().Be("2026-04-01T07:30:00.0000000+10:00");
        request.EndDate.Should().Be("2026-04-01T16:00:00.0000000+10:00");
        request.LeaveTypeId.Should().Be(2);
        request.AllDay.Should().BeFalse();
        request.ApprovedBy.Should().BeNull();
        request.OptionalEmp.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_WhenLeaveIdIsNotGuid_DoesNotCallApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            "not-a-guid",
            "--note", "Updated plans",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.GetLeaveAsync));
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.UpdateLeaveAsync));
    }

    [Fact]
    public async Task Update_WhenHalfDayAndFullDayAreBothSet_DoesNotCallApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            LeaveId,
            "--half-day",
            "--full-day",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.GetLeaveAsync));
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.UpdateLeaveAsync));
    }

    [Fact]
    public async Task Update_WhenSwitchingToFullDay_UsesWholeDayRange()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureExistingLeave(api, allDay: false);
        UpdateLeaveRequest? request = null;
        api.UpdateLeaveAsync(Arg.Do<UpdateLeaveRequest>(value => request = value), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            LeaveId,
            "--full-day",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        request.Should().NotBeNull();
        request!.AllDay.Should().BeTrue();
        request.StartDate.Should().Be("2026-03-30T00:00:00.0000000+00:00");
        request.EndDate.Should().Be("2026-03-30T23:59:00.0000000+00:00");
    }

    [Fact]
    public async Task Update_WhenSwitchingToHalfDay_UsesWorkdayTimesInsteadOfEndOfDay()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureExistingLeave(api);
        UpdateLeaveRequest? request = null;
        api.UpdateLeaveAsync(Arg.Do<UpdateLeaveRequest>(value => request = value), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            LeaveId,
            "--half-day",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        request.Should().NotBeNull();
        request!.AllDay.Should().BeFalse();
        request.StartDate.Should().Be("2026-03-30T07:30:00.0000000+00:00");
        request.EndDate.Should().Be("2026-03-30T16:00:00.0000000+00:00");
    }

    [Fact]
    public async Task Update_WhenPartialDayEndTimeIsNotOnTheHourOrHalfHour_DoesNotCallUpdateApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureExistingLeave(api);
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            LeaveId,
            "--half-day",
            "--end-time", "16:20",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.UpdateLeaveAsync));
    }

    [Fact]
    public async Task Update_WhenDryRunPartialDayEndTimeIsInvalid_FailsLikeTheRealCall()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureExistingLeave(api);
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "update",
            LeaveId,
            "--half-day",
            "--end-time", "16:20",
            "--dry-run",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.UpdateLeaveAsync));
    }

    private static void ConfigureExistingLeave(ITimeProApiClient api, bool allDay = true)
    {
        api.GetLeaveAsync(
                "UPCOMING",
                1,
                100,
                "TST",
                Arg.Any<CancellationToken>())
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
                            Id = LeaveId,
                            RequestedEmpId = "TST",
                            StartDate = "2026-03-30T00:00:00+10:00",
                            EndDate = "2026-03-30T23:59:00+10:00",
                            UserStartTime = "07:30",
                            UserEndTime = "16:00",
                            Note = "Original plans",
                            ApprovedBy = "approver@northwind.example",
                            OptionalEmp = ["notify@northwind.example"],
                            LeaveType = new LeaveTypeInfo { Id = 1, Name = "Annual Leave", IsActive = true },
                            AllDay = allDay,
                            TimeLessOverride = 1.5m
                        }
                    ]
                }
            });
        api.GetEmployeeSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new EmployeeSettings
            {
                StartTime = "08:30",
                EndTime = "17:00",
                TimezoneId = "UTC"
            });
    }

    private static CommandApp CreateApp(ITimeProApiClient api)
    {
        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://timepro.example",
            ApiKey = "test-api-key",
            EmployeeId = "TST"
        });

        var services = new ServiceCollection();
        services.AddSingleton(api);
        services.AddSingleton(config);
        services.AddSingleton<LeaveUpdateService>();

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(configurator =>
        {
            configurator.AddCommand<LeaveUpdateCommand>("update");
        });
        return app;
    }
}
