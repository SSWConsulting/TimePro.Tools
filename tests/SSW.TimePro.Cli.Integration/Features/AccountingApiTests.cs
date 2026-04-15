using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Features;

/// <summary>
/// WireMock-backed integration tests for the read-only accounting endpoints added
/// in the <c>feat/accountant-readonly</c> branch. One happy-path test per API method;
/// field names use the server's PascalCase shape (case-insensitive deserialize).
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
                    {"InvoiceID": 19145, "DateCreated": "2026-03-12T00:00:00", "ClientID": "ACME", "CoName": "Acme Pty Ltd", "InvoiceType": "Sale", "SellTotal": 1100, "PaidAmt": 0, "ExternalSyncStatus": 0},
                    {"InvoiceID": 19144, "DateCreated": "2026-03-11T00:00:00", "ClientID": "BETA", "CoName": "Beta Co",       "InvoiceType": "Sale", "SellTotal": 550,  "PaidAmt": 550, "ExternalSyncStatus": 1}
                  ]
                }
                """)
        );

        var page = await ApiClient.ListInvoicesAsync(null, 0, 50, "DateCreated", "desc", false, CancellationToken.None);

        page.Should().NotBeNull();
        page!.Total.Should().Be(2);
        page.Data.Should().HaveCount(2);
        page.Data[0].InvoiceId.Should().Be(19145);
        page.Data[0].SellTotal.Should().Be(1100);
    }

    [Fact]
    public async Task GetInvoice_ReturnsHeaderFields()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/v2/ClientInvoice/19145").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "InvoiceID": 19145, "ClientID": "ACME", "InvoiceType": "Sale",
                  "SubTotal": 1000, "SellTotal": 1100, "SalesTaxAmt": 100, "SalesTaxPct": 10,
                  "PaidAmt": 0, "OSAmt": 1100, "IsLocked": false, "IsCreditNote": false
                }
                """)
        );

        var inv = await ApiClient.GetInvoiceAsync(19145, CancellationToken.None);

        inv.Should().NotBeNull();
        inv!.InvoiceId.Should().Be(19145);
        inv.SellTotal.Should().Be(1100);
        inv.SalesTaxPct.Should().Be(10);
    }

    [Fact]
    public async Task GetInvoiceProducts_ReturnsLineItems()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/v2/ClientInvoice/19145/products").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                [
                  {"InvoiceProdID": 1, "InvoiceID": 19145, "SkuID": "TIME", "ProdName": "T&M",  "Qty": 10, "SellAmt": 100, "SellTotal": 1000},
                  {"InvoiceProdID": 2, "InvoiceID": 19145, "SkuID": "PP",   "ProdName": "Prepaid Block", "Qty": 1, "SellAmt": 100, "SellTotal": 100}
                ]
                """)
        );

        var rows = await ApiClient.GetInvoiceProductsAsync(19145, CancellationToken.None);

        rows.Should().HaveCount(2);
        rows[0].SkuId.Should().Be("TIME");
        rows[0].SellTotal.Should().Be(1000);
    }

    [Fact]
    public async Task GetInvoiceTimesheets_Allocated_UsesCorrectPath()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/v2/Timesheets/WithNames/Allocated")
                .WithParam("invoiceID", "19145").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""[{"TimeId":1,"EmpId":"TST","EmpName":"Test User","ClientId":"ACME","TotalTime":8,"BillableAmount":800}]""")
        );

        var rows = await ApiClient.GetInvoiceTimesheetsAsync(19145, "allocated", CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].BillableAmount.Should().Be(800);
    }

    [Fact]
    public async Task GetInvoiceTimesheets_WriteOff_HitsWriteOffPath()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/v2/Timesheets/WithNames/WriteOff")
                .WithParam("invoiceID", "19145").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("[]")
        );

        var rows = await ApiClient.GetInvoiceTimesheetsAsync(19145, "writeoff", CancellationToken.None);

        rows.Should().BeEmpty();
        WireMock.LogEntries.First().RequestMessage.AbsolutePath.Should().EndWith("/WriteOff");
    }

    [Fact]
    public async Task GetInvoiceReceipts_ReturnsReceiptsWithSignConvention()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/v2/ClientInvoice/19145/receipts").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                [{"SaleReceiptID":501,"InvoiceID":19145,"PaymentDate":"2026-03-20T00:00:00","Paid":-1100,"PaidTotal":-1100,"SaleReceiptStatus":"Paid","IsCreditingPrepaid":false,
                  "SaleReceiptType":{"Id":"CASH","TypeName":"Cash","TypeSign":"-"}}]
                """)
        );

        var rows = await ApiClient.GetInvoiceReceiptsAsync(19145, CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].PaidTotal.Should().Be(-1100);
        rows[0].SaleReceiptType!.TypeName.Should().Be("Cash");
    }

    // ────── Receipts ──────

    [Fact]
    public async Task ListPaidReceipts_ReturnsPagedResponse()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/receipting/PaidReceiptsPaged").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                {"total": 1, "data": [
                  {"SaleReceiptID": 501, "InvoiceID": 19145, "Paid": -1100, "PaidTotal": -1100, "CoName": "Acme", "PaymentDate": "2026-03-20T00:00:00"}
                ]}
                """)
        );

        var page = await ApiClient.ListPaidReceiptsAsync(null, 0, 100, "PaymentDate", "desc", CancellationToken.None);

        page.Should().NotBeNull();
        page!.Data.Should().HaveCount(1);
        page.Data[0].PaidTotal.Should().Be(-1100);
    }

    [Fact]
    public async Task GetClientOutstanding_ReturnsAgedDebtorView()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/Receipting/ClientOutstanding/ACME").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "ClientId":"ACME","CoName":"Acme Pty Ltd","OutstandingInvoices":[
                    {"InvoiceId":19145,"DateInvoiced":"2026-02-01T00:00:00","Total":1100,"PaidAmt":0,"OsAmt":1100,"DaysOverdue":42}
                  ]
                }
                """)
        );

        var d = await ApiClient.GetClientOutstandingAsync("ACME", CancellationToken.None);

        d.Should().NotBeNull();
        d!.OutstandingInvoices.Should().HaveCount(1);
        d.OutstandingInvoices![0].DaysOverdue.Should().Be(42);
    }

    // ────── Credit notes ──────

    [Fact]
    public async Task GetCreditNotesByClient_ReturnsRows()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/creditnote/by-client/ACME").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""[{"Id":101,"Amount":-220,"Note":"refund","CreditNoteDate":"2026-03-01T00:00:00","TaxRate":10,"IsLocked":true,"SyncStatus":1}]""")
        );

        var rows = await ApiClient.GetCreditNotesByClientAsync("ACME", CancellationToken.None);

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
                .WithBody("""{"rates":[{"ClientRateId":1,"EmpId":"TST","EmployeeName":"Test","ClientId":"ACME","Rate":200,"ExpiryDate":"2027-01-01T00:00:00"}],"total":1}""")
        );

        var d = await ApiClient.ListClientRatesAsync("ACME", null, showExpired: false,
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
                .WithBody("""[{"ClientId":"ACME","CoName":"Acme","Billable":800,"Os":400,"EarliestUnAllocatedTimesheetDate":"2026-02-15T00:00:00"}]""")
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
                .WithBody("""[{"TimeId":42,"EmpId":"TST","ClientId":"ACME","ProjectId":"P1","TotalTime":4,"BillableAmount":400}]""")
        );

        var rows = await ApiClient.GetUnallocatedTimesheetsByClientAsync("ACME", null, null, null, null, CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].BillableAmount.Should().Be(400);
    }

    // ────── Recurring ──────

    [Fact]
    public async Task ListRecurringInvoices_ReturnsPage()
    {
        WireMock.Given(
            Request.Create().WithPath("/api/recurring/invoices/").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"total":1,"data":[{"id":7,"clientId":"ACME","clientName":"Acme","sellTotal":500,"countOfInv":12,"unit":"Month","isActive":true,"createdOn":"2025-01-01T00:00:00"}]}""")
        );

        var page = await ApiClient.ListRecurringInvoicesAsync(null, null, false, 0, 50, "LastInvEndDate", "desc", CancellationToken.None);

        page.Should().NotBeNull();
        page!.Data[0].Id.Should().Be(7);
        page.Data[0].IsActive.Should().BeTrue();
    }

    // ────── Prepaid PDF ──────

    [Fact]
    public async Task GetPrepaidStatusPdf_ReturnsBytes()
    {
        var fakePdf = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // "%PDF"
        WireMock.Given(
            Request.Create().WithPath("/Reporting/GetPrepaidStatusReport")
                .WithParam("invoiceId", "19145").UsingGet()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/pdf").WithBody(fakePdf)
        );

        var bytes = await ApiClient.GetPrepaidStatusReportPdfAsync(19145, 0, CancellationToken.None);

        bytes.Should().StartWith(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }
}
