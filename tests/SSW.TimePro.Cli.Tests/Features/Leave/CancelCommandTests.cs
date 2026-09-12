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

using LeaveCancelCommand = SSW.TimePro.Cli.Features.Leave.CancelCommand;

namespace SSW.TimePro.Cli.Tests.Features.Leave;

public class CancelCommandTests
{
    private const string LeaveId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

    [Fact]
    public async Task Cancel_WhenLeaveIdIsNotGuid_ReturnsErrorWithoutCallingApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "cancel",
            "not-a-guid",
            "--reason", "Plans changed",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.CancelLeaveAsync));
    }

    [Fact]
    public async Task Cancel_WhenReasonMissing_ReturnsErrorWithoutCallingApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "cancel",
            LeaveId,
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.CancelLeaveAsync));
    }

    [Theory]
    [InlineData(LeaveStatusRules.Declined)]
    [InlineData(LeaveStatusRules.Cancelled)]
    [InlineData(LeaveStatusRules.PendingCancellation)]
    public async Task Cancel_WhenLeaveIsInATerminalStatus_DoesNotCallCancelApi(int status)
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureLeave(api, status);
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "cancel",
            LeaveId,
            "--reason", "Plans changed",
            "--yes",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.CancelLeaveAsync));
    }

    [Theory]
    [InlineData("--wait")]
    [InlineData("--wait", "45")]
    public async Task Cancel_WhenWaitIsUsedUnderStrictParsing_BindsInsteadOfFailingToParse(params string[] waitArgs)
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureLeave(api, LeaveStatusRules.Cancelled);
        var app = CreateApp(api);

        var exitCode = await app.RunAsync(
            ["cancel", LeaveId, "--reason", "Plans changed", "--yes", "--json", .. waitArgs],
            TestContext.Current.CancellationToken);

        // The guard answers first, so reaching exit 1 (not Spectre's -1) proves the option bound.
        exitCode.Should().Be(1);
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.CancelLeaveAsync));
    }

    [Fact]
    public async Task Cancel_WhenApproved_CallsCancelApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        ConfigureLeave(api, 2);
        var app = CreateApp(api);

        var exitCode = await app.RunAsync([
            "cancel",
            LeaveId,
            "--reason", "Plans changed",
            "--yes",
            "--json"
        ], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        await api.Received(1).CancelLeaveAsync(
            LeaveId,
            Arg.Is<CancelLeaveRequest>(request => request.CancellationReason == "Plans changed"),
            Arg.Any<CancellationToken>());
    }

    private static void ConfigureLeave(ITimeProApiClient api, int status)
    {
        api.GetLeaveAsync(
                LeaveListService.Upcoming,
                1,
                100,
                "TST",
                Arg.Any<CancellationToken>())
            .Returns(Page(status));
    }

    private static LeaveListResponse Page(int status) => new()
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
                    Note = "Original plans",
                    LeaveType = new LeaveTypeInfo { Id = 1, Name = "Annual Leave", IsActive = true },
                    AllDay = true,
                    LeaveStatus = status
                }
            ]
        }
    };

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
        services.AddSingleton<LeaveLookup>();

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(configurator =>
        {
            configurator.UseStrictParsing();
            configurator.AddCommand<LeaveCancelCommand>("cancel");
        });
        return app;
    }
}
