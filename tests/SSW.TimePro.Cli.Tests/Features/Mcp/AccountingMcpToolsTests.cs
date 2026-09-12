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

public class AccountingMcpToolsTests
{
    [Fact]
    public void BalanceTools_AreSeparatedBetweenDefaultAndAccountingSurfaces()
    {
        typeof(LeaveMcpTools).GetMethod(nameof(LeaveMcpTools.GetLeaveBalanceStatus)).Should().NotBeNull();
        typeof(LeaveMcpTools).GetMethod(nameof(AccountingMcpTools.ImportLeaveBalances)).Should().BeNull();
        typeof(AccountingMcpTools).GetMethod(nameof(AccountingMcpTools.ImportLeaveBalances)).Should().NotBeNull();
    }

    [Fact]
    public void ImportLeaveBalances_IsMarkedDestructiveAndIdempotent()
    {
        var attribute = typeof(AccountingMcpTools)
            .GetMethod(nameof(AccountingMcpTools.ImportLeaveBalances))!
            .GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
            .Cast<McpServerToolAttribute>()
            .Single();

        attribute.Destructive.Should().BeTrue();
        attribute.Idempotent.Should().BeTrue();
        attribute.ReadOnly.Should().BeFalse();
    }

    [Fact]
    public async Task ImportLeaveBalances_WithCsvPath_SendsFileContentsAndReportsSkippedRows()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var config = CreateConfig();
        const string csv = "Employee,Leave Type,Units\nJane Doe,Annual Leave,76.00\n";
        string? sent = null;
        api.ImportLeaveBalancesAsync(Arg.Do<string>(value => sent = value), Arg.Any<CancellationToken>())
            .Returns(new ImportLeaveBalancesResult
            {
                AsAtDate = new DateOnly(2026, 8, 1),
                Created = 1,
                Updated = 2,
                UnmatchedEmployees = ["Nobody Here"],
                Warnings = ["Jane Doe has an unusually large balance"]
            });

        var path = Path.Combine(Path.GetTempPath(), $"tp-mcp-balances-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, csv, TestContext.Current.CancellationToken);

        try
        {
            var tools = CreateTools(api, config);
            var json = await tools.ImportLeaveBalances(path, TestContext.Current.CancellationToken);

            sent.Should().Be(csv);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("created").GetInt32().Should().Be(1);
            doc.RootElement.GetProperty("updated").GetInt32().Should().Be(2);
            doc.RootElement.GetProperty("unmatchedEmployees").EnumerateArray()
                .Select(e => e.GetString()).Should().Equal("Nobody Here");
            doc.RootElement.GetProperty("warnings").GetArrayLength().Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportLeaveBalances_WhenFileMissing_ReturnsErrorWithoutCallingApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var tools = CreateTools(api, CreateConfig());

        var json = await tools.ImportLeaveBalances(
            Path.Combine(Path.GetTempPath(), $"tp-missing-{Guid.NewGuid():N}.csv"),
            TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetString().Should().StartWith("File not found");
        await api.DidNotReceive().ImportLeaveBalancesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportLeaveBalances_WhenNotLoggedIn_ReturnsErrorWithoutCallingApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns((TenantConfig?)null);
        var tools = CreateTools(api, config);

        var json = await tools.ImportLeaveBalances("balances.csv", TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetString().Should().Contain("Not logged in");
        await api.DidNotReceive().ImportLeaveBalancesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportLeaveBalances_WhenSubmissionOutcomeIsUncertain_ReturnsSafeRetryGuidance()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.ImportLeaveBalancesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ImportLeaveBalancesResult?>>(_ => throw new HttpRequestException("Connection dropped"));
        var tools = CreateTools(api, CreateConfig());
        var path = Path.Combine(Path.GetTempPath(), $"tp-mcp-balances-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, "Employee,Leave Type,Units\nJane Doe,Annual Leave,76.00\n",
            TestContext.Current.CancellationToken);

        try
        {
            var json = await tools.ImportLeaveBalances(path, TestContext.Current.CancellationToken);

            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("mayHaveBeenApplied").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("error").GetString()
                .Should().Contain("tp leave balances status").And.Contain("before retrying");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ListRecurringInvoices_RendersUnitAsEnumName()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.ListRecurringInvoicesAsync(null, null, false, 0, 50, "LastInvEndDate", "desc", Arg.Any<CancellationToken>())
            .Returns(new PagedResponse<RecurringInvoiceRow>
            {
                Total = 1,
                Data = [new RecurringInvoiceRow { Id = 7, ClientId = "NWIND", Unit = RecurrenceUnit.Month }]
            });

        var tools = CreateTools(api, CreateConfig());
        var json = await tools.ListRecurringInvoices(ct: TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("data")[0].GetProperty("unit").GetString().Should().Be("month");
    }

    private static IConfigService CreateConfig()
    {
        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(new TenantConfig
        {
            TenantId = "test",
            ApiUrl = "https://timepro.example",
            ApiKey = "test-api-key",
            EmployeeId = "TST"
        });
        return config;
    }

    private static AccountingMcpTools CreateTools(ITimeProApiClient api, IConfigService config) =>
        new(api, config, new LeaveBalanceImportService(api));
}
