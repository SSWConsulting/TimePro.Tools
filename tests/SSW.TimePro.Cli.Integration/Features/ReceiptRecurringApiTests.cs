using FluentAssertions;
using SSW.TimePro.Cli.Shared.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Features;

/// <summary>
/// Receipt and recurring-invoice endpoints, against payloads captured from a live server and
/// sanitised to Northwind. Each endpoint is also checked with <see cref="JsonShape"/> so a
/// property the DTO cannot bind fails here rather than showing up as a zero in the output.
/// </summary>
public class ReceiptRecurringApiTests : TestBase
{
    // ────── Recurring ──────

    private const string RecurringListJson = """
    {"total":2,"data":[
      {"id":7,"clientId":"NWIND","clientName":"Northwind Traders","sellTotal":3227.8180,"countOfInv":4,"unit":1,"note":null,"noteInternal":null,"lastInvEndDate":"2026-09-30T22:00:00","isActive":true,"createdOn":"2026-06-09T11:10:46.607565"},
      {"id":8,"clientId":"NWIND","clientName":"Northwind Traders","sellTotal":790.6800,"countOfInv":87,"unit":0,"note":"Annual support","noteInternal":"Renews automatically","lastInvEndDate":"2026-09-26T22:00:00","isActive":false,"createdOn":"2026-06-03T07:48:41.7919998"}
    ]}
    """;

    private const string RecurringDetailJson = """
    {
      "id": 7, "clientId": "NWIND", "sellAmt": 2934.38, "sellTaxAmt": 293.438, "sellTotal": 3227.818,
      "countOfInv": 4, "firstInvPeriod": 1, "subsequentInvPeriod": 1, "lastInvPeriod": 0,
      "dateStart": "2026-05-31T22:00:00Z", "dateEnd": null,
      "lastInvEndDate": "2026-09-30T22:00:00",
      "nextInvoicePeriodStart": "2026-09-30T22:00:01", "nextInvoicePeriodEnd": "2026-10-30T22:00:00",
      "canGenerateNow": true, "cannotGenerateReason": null,
      "unit": 1, "note": null, "noteInternal": null,
      "modifiedBy": "bob@northwind.example", "createdBy": "bob@northwind.example",
      "createdOn": "2026-06-09T11:10:46.607565", "modifiedOn": "2026-08-30T00:20:18.3787796",
      "products": [
        {
          "id": 9, "recurringInvoiceID": 7, "prodID": "EK6H8R", "prodName": "Checkout API support",
          "prodCatgoryID": "KR4022", "prodCategoryName": "Support",
          "sellAmt": 2934.38, "sellTotal": 2934.38, "salesTaxPct": 0.1, "salesTaxAmt": 293.438,
          "costTotal": null, "noteInternal": "", "note": "Monthly retainer", "qty": 1,
          "modifiedBy": "bob@northwind.example", "createdBy": "bob@northwind.example",
          "createdOn": "2026-06-09T11:10:46.6077165", "modifiedOn": "2026-06-09T11:10:46.6078024"
        }
      ]
    }
    """;

    [Fact]
    public async Task ListRecurringInvoices_BindsNumericUnitToEnum()
    {
        WireMock.Given(Request.Create().WithPath("/api/recurring/invoices/").UsingGet())
            .RespondWith(Json(RecurringListJson));

        var page = await ApiClient.ListRecurringInvoicesAsync(null, null, false, 0, 50, "LastInvEndDate", "desc", CancellationToken.None);

        page.Should().NotBeNull();
        page!.Total.Should().Be(2);
        page.Data[0].Unit.Should().Be(RecurrenceUnit.Month);
        page.Data[0].SellTotal.Should().Be(3227.8180m);
        page.Data[1].Unit.Should().Be(RecurrenceUnit.Year);
        page.Data[1].IsActive.Should().BeFalse();
    }

    [Fact]
    public void RecurringInvoiceList_BindsEveryProperty() =>
        JsonShape.AssertFullyMapped<PagedResponse<RecurringInvoiceRow>>(RecurringListJson);

    [Fact]
    public async Task GetRecurringInvoice_BindsScheduleAndProducts()
    {
        WireMock.Given(Request.Create().WithPath("/api/recurring/invoices/7").UsingGet())
            .RespondWith(Json(RecurringDetailJson));

        var d = await ApiClient.GetRecurringInvoiceAsync(7, CancellationToken.None);

        d.Should().NotBeNull();
        d!.Id.Should().Be(7);
        d.Unit.Should().Be(RecurrenceUnit.Month);
        d.SellAmt.Should().Be(2934.38m);
        d.SubsequentInvPeriod.Should().Be(1);
        d.CanGenerateNow.Should().BeTrue();
        d.NextInvoicePeriodEnd.Should().Be(new DateTime(2026, 10, 30, 22, 0, 0));
        d.Products.Should().HaveCount(1);
        d.Products![0].ProdId.Should().Be("EK6H8R");
        d.Products[0].ProdName.Should().Be("Checkout API support");
        d.Products[0].Qty.Should().Be(1);
        d.Products[0].SellTotal.Should().Be(2934.38m);
    }

    [Fact]
    public void RecurringInvoiceDetail_BindsEveryProperty() =>
        JsonShape.AssertFullyMapped<RecurringInvoiceDetail>(RecurringDetailJson);

    // ────── Receipts ──────

    private const string PaidReceiptsPagedJson = """
    {"total":1,"data":[
      {
        "Bank": null, "Batch": "m160725", "Branch": null, "CategoryID": null,
        "ClientId": "NWIND", "PaymentDate": "2026-03-20T00:00:00",
        "DateCreated": "2026-03-20T11:38:39.753", "DateUpdated": null, "Drawer": null,
        "EmpUpdated": "Bob Northwind/BOB/NWIND", "ExportID": null, "Month": null, "Note": "",
        "SaleReceiptID": 501,
        "SaleReceiptType": {
          "Id": "DD", "TypeName": "Direct Deposit", "TypeSign": "-", "Count": 0,
          "DateUpdated": "2026-01-18T13:24:47.12", "EmpUpdated": "Bob Northwind/BOB/NWIND",
          "Note": null, "DateCreated": "2026-01-18T13:24:47.12", "IsHardCoded": false
        },
        "Unallocated": 0.0, "PaidTotal": -100.0, "CoName": "Northwind Traders",
        "ClientFirstName": null, "ClientSurname": "Northwind",
        "InvoiceIDs": [142], "SaleReceiptPaids": [],
        "ExternalSyncType": "Xero", "ExternalSyncId": "7ca82ece-9d0c-4a64-9fa1-49450b792d29",
        "CreditNoteId": null
      }
    ]}
    """;

    private const string ReceiptDetailJson = """
    {
      "Receipt": {
        "SaleReceiptID": "501", "ReceiptType": "DD", "ClientID": "NWIND", "CategoryID": null,
        "CoName": "Northwind Traders", "PaymentDate": "2026-03-20T00:00:00",
        "ClientFirstName": null, "ClientSurname": "Northwind", "IsNew": false,
        "BatchNo": "m160725", "ReceiptTotal": -100.0, "Note": "",
        "OutstandingInvoices": [],
        "SaleReceiptPaids": [
          {
            "InvoiceID": 142, "SaleReceiptID": 501, "InvoiceDate": "2026-03-16T00:00:00",
            "DateCreated": "2026-03-20T11:38:39.753", "PaymentDate": "2026-03-20T00:00:00",
            "Total": 2019.99, "AlreadyPaidAmt": 0, "PaidAmt": -100.0, "Balance": 1919.99,
            "IsAllocated": true, "ClientID": "NWIND", "CoName": "Northwind Traders",
            "SaleReceiptStatus": "Part Payment", "Note": ""
          }
        ],
        "ExternalSyncType": "Xero", "ExternalSyncId": "7ca82ece-9d0c-4a64-9fa1-49450b792d29"
      },
      "ContactPerson": "Bob Northwind",
      "PaymentMethods": [{"ID": "DD", "Name": "Direct Deposit", "Sign": "-"}],
      "TotalPaid": -100.0,
      "InvoicesAmount": 0
    }
    """;

    private const string InvoiceReceiptsJson = """
    [
      {
        "invoiceID": 142, "paid": -100.0, "coName": "Northwind Traders",
        "dateCreated": "2026-03-20T11:38:39.753", "paymentDate": "2026-03-20T00:00:00",
        "dateUpdated": null, "empUpdated": "Bob Northwind/BOB/NWIND", "note": "",
        "rowguid": "ec6fa1c3-e5a7-448b-89f7-210a5b198aa4", "saleReceiptID": 501,
        "saleReceiptStatus": "Part Payment", "sswTimeStamp": "AAAAAAABHPM=",
        "creditNoteId": 16, "isCreditingPrepaid": false
      }
    ]
    """;

    [Fact]
    public async Task ListPaidReceipts_BindsReceiptLevelTotalAndInvoiceIds()
    {
        WireMock.Given(Request.Create().WithPath("/api/receipting/PaidReceiptsPaged").UsingGet())
            .RespondWith(Json(PaidReceiptsPagedJson));

        var page = await ApiClient.ListPaidReceiptsAsync(null, 0, 100, "PaymentDate", "desc", CancellationToken.None);

        page.Should().NotBeNull();
        page!.Data.Should().HaveCount(1);
        var r = page.Data[0];
        r.SaleReceiptId.Should().Be(501);
        r.PaidTotal.Should().Be(-100.0m);
        r.InvoiceIds.Should().Equal(142);
        r.Batch.Should().Be("m160725");
        r.CoName.Should().Be("Northwind Traders");
        r.SaleReceiptType!.TypeName.Should().Be("Direct Deposit");
        r.SaleReceiptType.IsHardCoded.Should().BeFalse();
        r.ExternalSyncType.Should().Be("Xero");
    }

    [Fact]
    public void PaidReceiptsPaged_BindsEveryProperty() =>
        JsonShape.AssertFullyMapped<PagedResponse<PaidReceiptRow>>(PaidReceiptsPagedJson);

    [Fact]
    public async Task GetReceiptDetail_UnwrapsEnvelopeAndBindsAllocations()
    {
        WireMock.Given(Request.Create().WithPath("/api/Receipting/details/501").UsingGet())
            .RespondWith(Json(ReceiptDetailJson));

        var v = await ApiClient.GetReceiptDetailAsync(501, CancellationToken.None);

        v.Should().NotBeNull();
        v!.TotalPaid.Should().Be(-100.0m);
        v.ContactPerson.Should().Be("Bob Northwind");
        v.PaymentMethods.Should().ContainSingle().Which.Name.Should().Be("Direct Deposit");

        var d = v.Receipt!;
        d.SaleReceiptId.Should().Be(501);
        d.ClientId.Should().Be("NWIND");
        d.ReceiptTotal.Should().Be(-100.0m);
        d.BatchNo.Should().Be("m160725");
        d.SaleReceiptPaids.Should().HaveCount(1);
        d.SaleReceiptPaids[0].InvoiceId.Should().Be(142);
        d.SaleReceiptPaids[0].PaidAmt.Should().Be(-100.0m);
        d.SaleReceiptPaids[0].Total.Should().Be(2019.99m);
        d.SaleReceiptPaids[0].Balance.Should().Be(1919.99m);
    }

    [Fact]
    public void ReceiptDetail_BindsEveryProperty() =>
        JsonShape.AssertFullyMapped<ReceiptDetailResponse>(ReceiptDetailJson);

    [Fact]
    public async Task GetInvoiceReceipts_BindsPaymentRows()
    {
        WireMock.Given(Request.Create().WithPath("/api/v2/ClientInvoice/142/receipts").UsingGet())
            .RespondWith(Json(InvoiceReceiptsJson));

        var rows = await ApiClient.GetInvoiceReceiptsAsync(142, CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].SaleReceiptId.Should().Be(501);
        rows[0].InvoiceId.Should().Be(142);
        rows[0].Paid.Should().Be(-100.0m);
        rows[0].SaleReceiptStatus.Should().Be("Part Payment");
        rows[0].CreditNoteId.Should().Be(16);
        rows[0].IsCreditingPrepaid.Should().BeFalse();
    }

    [Fact]
    public void InvoiceReceipts_BindsEveryPropertyExceptRowMetadata() =>
        JsonShape.AssertFullyMapped<List<ReceiptRow>>(InvoiceReceiptsJson, "$[].rowguid", "$[].sswTimeStamp");

    [Fact]
    public void JsonShape_FailsOnAnUnmappedProperty()
    {
        var act = () => JsonShape.AssertFullyMapped<PagedResponse<RecurringInvoiceRow>>(
            """{"total":1,"data":[{"id":7,"cadence":"Monthly"}]}""");

        act.Should().Throw<Xunit.Sdk.XunitException>().WithMessage("*$.data[].cadence*");
    }

    private static IResponseBuilder Json(string body) =>
        Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(body);
}
