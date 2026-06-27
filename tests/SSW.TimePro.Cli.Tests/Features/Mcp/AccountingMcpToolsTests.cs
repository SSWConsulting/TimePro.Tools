using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using SSW.TimePro.Cli.Features.Accounting;
using SSW.TimePro.Cli.Features.Mcp.Tools;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Mcp;

public class AccountingMcpToolsTests
{
    [Fact]
    public async Task FindTimesheetTaxMismatches_FindsNonZeroZeroTaxTimesheetOnNonZeroTaxInvoice()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var config = Substitute.For<IConfigService>();
        config.LoadActiveTenantConfig().Returns(new TenantConfig { TenantId = "northwind", EmployeeId = "ALEX" });

        api.ListInvoicesAsync(null, 0, 10, "DateCreated", "desc", false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PagedResponse<InvoiceSearchRow>?>(new PagedResponse<InvoiceSearchRow>
            {
                Total = 2,
                Data =
                [
                    new() { InvoiceId = 142, ClientId = "NWIND", DateCreated = new DateTime(2026, 3, 12), SellTotal = 1100 },
                    new() { InvoiceId = 143, ClientId = "NWIND", DateCreated = new DateTime(2026, 3, 13), SellTotal = 500 },
                ],
            }));

        api.GetInvoiceAsync(142, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<InvoiceHeader?>(new InvoiceHeader
            {
                InvoiceId = 142,
                ClientId = "NWIND",
                SalesTaxPct = 0.1,
                SalesTaxAmt = 100,
                DateCreated = new DateTime(2026, 3, 12),
            }));
        api.GetInvoiceAsync(143, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<InvoiceHeader?>(new InvoiceHeader
            {
                InvoiceId = 143,
                ClientId = "NWIND",
                SalesTaxPct = 0,
                SalesTaxAmt = 0,
                DateCreated = new DateTime(2026, 3, 13),
            }));

        api.GetInvoiceTimesheetsAsync(142, "allocated", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<InvoiceTimesheet>
            {
                new() { TimeId = 1, BillableAmount = 800, SalesTaxPct = 0, EmpId = "ALEX", ProjectId = "1I776Q" },
                new() { TimeId = 2, BillableAmount = 0, SalesTaxPct = 0, EmpId = "ALEX", ProjectId = "1I776Q" },
                new() { TimeId = 3, BillableAmount = 200, SalesTaxPct = 0.1, EmpId = "ALEX", ProjectId = "1I776Q" },
            }));

        var tools = new AccountingMcpTools(api, config, new AccountingDiagnosticsService(api));

        var json = await tools.FindTimesheetTaxMismatches(limit: 10, ct: TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("scannedInvoiceCount").GetInt32().Should().Be(2);
        root.GetProperty("invoicesWithNonZeroTax").GetInt32().Should().Be(1);
        root.GetProperty("mismatchCount").GetInt32().Should().Be(1);
        root.GetProperty("rows")[0].GetProperty("timeId").GetInt32().Should().Be(1);
        root.GetProperty("rows")[0].GetProperty("invoiceId").GetInt32().Should().Be(142);

        await api.DidNotReceive().GetInvoiceTimesheetsAsync(143, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
