using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Features;

/// <summary>
/// WireMock-backed integration tests for the read-only accounting endpoints added
/// in the <c>feat/accountant-readonly</c> branch. One happy-path test per API method;
/// field names use the server's PascalCase shape (case-insensitive deserialize).
/// Receipt and recurring-invoice endpoints live in <see cref="ReceiptRecurringApiTests"/>.
/// </summary>
public class AccountingApiTests : TestBase
{
    // ────── Invoices ──────

    [Fact]
    public async Task ListInvoices_ReturnsPagedResponse()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/ClientInvoice/rangepaged").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "total": 2,
                  "data": [
                    {"InvoiceID": 142, "DateCreated": "2026-03-12T00:00:00", "ClientID": "NWIND", "CoName": "Northwind Traders", "InvoiceType": "Sale", "SellTotal": 1100, "PaidAmt": 0, "ExternalSyncStatus": 0},
                    {"InvoiceID": 143, "DateCreated": "2026-03-11T00:00:00", "ClientID": "NWIND", "CoName": "Northwind Traders", "InvoiceType": "Sale", "SellTotal": 550,  "PaidAmt": 550, "ExternalSyncStatus": 1}
                  ]
                }
                """)
        );

        var page = await ApiClient.ListInvoicesAsync(null, 0, 50, "DateCreated", "desc", false, CancellationToken.None);

        page.Should().NotBeNull();
        page!.Total.Should().Be(2);
        page.Data.Should().HaveCount(2);
        page.Data[0].InvoiceId.Should().Be(142);
        page.Data[0].SellTotal.Should().Be(1100);
    }

    [Fact]
    public async Task GetInvoice_ReturnsHeaderFields()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/v2/ClientInvoice/142").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "InvoiceID": 142, "ClientID": "NWIND", "InvoiceType": "Sale",
                  "SubTotal": 1000, "SellTotal": 1100, "SalesTaxAmt": 100, "SalesTaxPct": 10,
                  "PaidAmt": 0, "OSAmt": 1100, "IsLocked": false, "IsCreditNote": false
                }
                """)
        );

        var inv = await ApiClient.GetInvoiceAsync(142, CancellationToken.None);

        inv.Should().NotBeNull();
        inv!.InvoiceId.Should().Be(142);
        inv.SellTotal.Should().Be(1100);
        inv.SalesTaxPct.Should().Be(10);
    }

    [Fact]
    public async Task GetInvoiceProducts_ReturnsLineItems()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/v2/ClientInvoice/142/products").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                [
                  {"InvoiceProdID": 1, "InvoiceID": 142, "SkuID": "TIME", "ProdName": "T&M",  "Qty": 10, "SellAmt": 100, "SellTotal": 1000},
                  {"InvoiceProdID": 2, "InvoiceID": 142, "SkuID": "PP",   "ProdName": "Prepaid Block", "Qty": 1, "SellAmt": 100, "SellTotal": 100}
                ]
                """)
        );

        var rows = await ApiClient.GetInvoiceProductsAsync(142, CancellationToken.None);

        rows.Should().HaveCount(2);
        rows[0].SkuId.Should().Be("TIME");
        rows[0].SellTotal.Should().Be(1000);
    }

    [Fact]
    public async Task GetInvoiceTimesheets_Allocated_UsesCorrectPath()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/v2/Timesheets/WithNames/Allocated")
                .WithParam("invoiceID", "142").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""[{"TimeId":1,"EmpId":"TST","EmpName":"Test User","ClientId":"NWIND","TotalTime":8,"BillableAmount":800}]""")
        );

        var rows = await ApiClient.GetInvoiceTimesheetsAsync(142, "allocated", CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].BillableAmount.Should().Be(800);
    }

    [Fact]
    public async Task GetInvoiceTimesheets_WriteOff_HitsWriteOffPath()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/v2/Timesheets/WithNames/WriteOff")
                .WithParam("invoiceID", "142").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("[]")
        );

        var rows = await ApiClient.GetInvoiceTimesheetsAsync(142, "writeoff", CancellationToken.None);

        rows.Should().BeEmpty();
        var requestMessage = WireMock.LogEntries.First().RequestMessage;
        requestMessage.Should().NotBeNull();
        requestMessage!.AbsolutePath.Should().EndWith("/WriteOff");
    }

    // ────── Receipts ──────

    [Fact]
    public async Task GetClientOutstanding_ReturnsAgedDebtorView()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/Receipting/ClientOutstanding/NWIND").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "ClientId":"NWIND","CoName":"Northwind Traders","OutstandingInvoices":[
                    {"InvoiceId":142,"DateInvoiced":"2026-02-01T00:00:00","Total":1100,"PaidAmt":0,"OsAmt":1100,"DaysOverdue":42}
                  ]
                }
                """)
        );

        var d = await ApiClient.GetClientOutstandingAsync("NWIND", CancellationToken.None);

        d.Should().NotBeNull();
        d!.OutstandingInvoices.Should().HaveCount(1);
        d.OutstandingInvoices![0].DaysOverdue.Should().Be(42);
    }

    // ────── Credit notes ──────

    [Fact]
    public async Task GetCreditNotesByClient_ReturnsRows()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/creditnote/by-client/NWIND").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""[{"Id":101,"Amount":-220,"Note":"refund","CreditNoteDate":"2026-03-01T00:00:00","TaxRate":10,"IsLocked":true,"SyncStatus":1}]""")
        );

        var rows = await ApiClient.GetCreditNotesByClientAsync("NWIND", CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].Amount.Should().Be(-220);
        rows[0].IsLocked.Should().BeTrue();
    }

    // ────── Products ──────

    [Fact]
    public async Task ListProducts_ReturnsProducts()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/Product").WithParam("isExpand", "false").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""[{"ProductId":"TRAIN","ProductName":"Training","Head":"Courses","AllowDiscount":true,"DisplayOnWeb":true,"isTraining":true}]""")
        );

        var rows = await ApiClient.ListProductsAsync(false, CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].ProductId.Should().Be("TRAIN");
        rows[0].IsTraining.Should().BeTrue();
    }

    [Fact]
    public async Task ListAllSkus_Prepaid_FiltersCorrectly()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/Product/All").WithParam("IsPrepaid", "true").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""[{"SkuId":"PP-20","SkuName":"20h prepaid block","ProductId":"PP","SellAmt":3000,"IsPrepaid":true}]""")
        );

        var rows = await ApiClient.ListAllSkusAsync(true, CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].IsPrepaid.Should().BeTrue();
    }

    // ────── Rates ──────

    [Fact]
    public async Task ListClientRates_ReturnsPagedTable()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/clients/GetClientRates").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"rates":[{"ClientRateId":1,"EmpId":"TST","EmployeeName":"Test","ClientId":"NWIND","Rate":200,"ExpiryDate":"2027-01-01T00:00:00"}],"total":1}""")
        );

        var d = await ApiClient.ListClientRatesAsync("NWIND", null, showExpired: false,
            pageSize: 100, skip: 0, sortField: "ExpiryDate", direction: "desc", selectAll: false,
            CancellationToken.None);

        d.Should().NotBeNull();
        d!.Total.Should().Be(1);
        d.Rates[0].Rate.Should().Be(200);
    }

    // ────── Outstanding / unbilled ──────

    [Fact]
    public async Task GetClientsWithOutstandingTime_ReturnsRows()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/clients/OutstandingTime").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""[{"ClientId":"NWIND","CoName":"Northwind","Billable":800,"Os":400,"EarliestUnAllocatedTimesheetDate":"2026-02-15T00:00:00"}]""")
        );

        var rows = await ApiClient.GetClientsWithOutstandingTimeAsync(CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].Billable.Should().Be(800);
    }

    [Fact]
    public async Task GetUnallocatedTimesheets_ForClient_ReturnsRows()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/v2/Timesheets/WithNames/Unallocated").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""[{"TimeId":42,"EmpId":"TST","ClientId":"NWIND","ProjectId":"1I776Q","TotalTime":4,"BillableAmount":400}]""")
        );

        var rows = await ApiClient.GetUnallocatedTimesheetsByClientAsync("NWIND", 100, 20, "DateCreated", "desc", CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].BillableAmount.Should().Be(400);
        var request = WireMock.LogEntries.Single().RequestMessage!;
        var url = request.Url!.ToString();
        url.Should().Contain("clientId=NWIND");
        url.Should().NotContain("pageSize=");
        url.Should().NotContain("skip=");
        url.Should().NotContain("sortField=");
        url.Should().NotContain("direction=");
    }

    // ────── Prepaid PDF ──────

    [Fact]
    public async Task GetPrepaidStatusPdf_ReturnsBytes()
    {
        var fakePdf = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // "%PDF"
        WireMock.Given(
            Request.Create().WithPath("/Reporting/GetPrepaidStatusReport")
                .WithParam("invoiceId", "142").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/pdf").WithBody(fakePdf)
        );

        var bytes = await ApiClient.GetPrepaidStatusReportPdfAsync(142, 0, CancellationToken.None);

        bytes.Should().StartWith(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }

    [Fact]
    public async Task GetClientInvoiceTableByClient_ReturnsRemainingPrepaidCredit()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/ClientInvoice/GetByClientId/NWIND")
                .WithParam("sortField", "invoiceid")
                .WithParam("direction", "desc")
                .WithParam("outstandingOnly", "false")
                .WithParam("withCreditNotes", "false")
                .UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "Invoices": [
                    {"InvoiceID": 142, "ClientID": "NWIND", "InvoiceType": "Prepaid", "RemainingPrepaidCredit": 500}
                  ],
                  "Total": 1
                }
                """)
        );

        var table = await ApiClient.GetClientInvoiceTableByClientAsync("NWIND", CancellationToken.None);

        table.Should().NotBeNull();
        table!.Total.Should().Be(1);
        table.Invoices[0].InvoiceId.Should().Be(142);
        table.Invoices[0].RemainingPrepaidCredit.Should().Be(500);
    }

    [Fact]
    public async Task GetPrepaidStatusSummary_ComposesStructuredAmounts()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/v2/ClientInvoice/142").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "InvoiceID": 142, "ClientID": "NWIND", "CurrencyID": "AUD", "InvoiceType": "Prepaid",
                  "SubTotal": 1000, "SellTotal": 1100, "SalesTaxAmt": 100, "SalesTaxPct": 0.1
                }
                """)
        );

        WireMock.Given(
            Request.Create().WithPath("/api/v2/Timesheets/WithNames/Allocated")
                .WithParam("invoiceID", "142")
                .UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                [
                  {"TimeID": 1, "BillableID": "BPP", "SellTotal": 300, "SalesTaxAmt": 30},
                  {"TimeID": 2, "BillableID": "B", "SellTotal": 400, "SalesTaxAmt": 40}
                ]
                """)
        );

        WireMock.Given(
            Request.Create().WithPath("/api/ClientInvoice/GetByClientId/NWIND")
                .WithParam("sortField", "invoiceid")
                .WithParam("direction", "desc")
                .WithParam("outstandingOnly", "false")
                .WithParam("withCreditNotes", "false")
                .UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "Invoices": [
                    {"InvoiceID": 142, "ClientID": "NWIND", "InvoiceType": "Prepaid", "RemainingPrepaidCredit": 500}
                  ],
                  "Total": 1
                }
                """)
        );

        WireMock.Given(
            Request.Create().WithPath("/api/creditnote/by-client/NWIND").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                [
                  {"Id": 7, "Amount": 220, "TaxRate": 0.1, "IsCreditingInvoice": true, "AssociatedInvoiceId": 142},
                  {"Id": 8, "Amount": 110, "TaxRate": 0.1, "IsCreditingInvoice": false, "AssociatedInvoiceId": 142},
                  {"Id": 9, "Amount": 110, "TaxRate": 0.1, "IsCreditingInvoice": true, "AssociatedInvoiceId": 108}
                ]
                """)
        );

        var summary = await ApiClient.GetPrepaidStatusSummaryAsync(142, CancellationToken.None);

        summary.Should().NotBeNull();
        summary!.Original.ExGst.Should().Be(1000);
        summary.Original.Gst.Should().Be(100);
        summary.Original.IncGst.Should().Be(1100);
        summary.DrawnDown.ExGst.Should().Be(300);
        summary.DrawnDown.Gst.Should().Be(30);
        summary.DrawnDown.IncGst.Should().Be(330);
        summary.Credited.ExGst.Should().Be(200);
        summary.Credited.Gst.Should().Be(20);
        summary.Credited.IncGst.Should().Be(220);
        summary.Remaining.ExGst.Should().Be(500);
        summary.Remaining.Gst.Should().Be(50);
        summary.Remaining.IncGst.Should().Be(550);
        summary.DrawdownTimesheetCount.Should().Be(1);
        summary.CreditingCreditNoteCount.Should().Be(1);
        summary.ReconciliationDeltaExGst.Should().Be(0);
        summary.ReconciliationDeltaIncGst.Should().Be(0);
    }
}
