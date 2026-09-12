using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Telemetry;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace SSW.TimePro.Cli.Integration.Features;

/// <summary>
/// Drives the real client for every call that puts an identifier in the path, and asserts the
/// recorded route never carries the value. Employee, client and product ids are ordinary strings,
/// so these are the cases no value-shape rule could have caught.
/// </summary>
public class RouteRedactionTests : TestBase
{
    private const string EmpId = "BOB";
    private const string ClientId = "NWIND";
    private const string ProjectId = "1I776Q";
    private const string LeaveId = "6f3e1f2e-0b6f-4f0a-9f4e-2f5a1c8d9b77";
    private const int InvoiceId = 48219;

    public static TheoryData<string, string> ParameterisedRoutes() => new()
    {
        { nameof(ITimeProApiClient.GetUserAsync), "/api/employees/{empId}" },
        { nameof(ITimeProApiClient.GetLeaveStatsAsync), "/api/leave/stats/{employeeId}" },
        { nameof(ITimeProApiClient.GetClientTaxRateAsync), "/api/v2/clients/{clientId}/taxrates" },
        { nameof(ITimeProApiClient.GetInvoicesByClientAsync), "/api/ClientInvoice/ClientID/{clientId}" },
        { nameof(ITimeProApiClient.GetClientInvoiceTableByClientAsync), "/api/ClientInvoice/GetByClientId/{clientId}" },
        { nameof(ITimeProApiClient.GetUnpaidInvoicesByClientAsync), "/api/ClientInvoice/UnpaidByClientID/{clientId}" },
        { nameof(ITimeProApiClient.GetClientOutstandingAsync), "/api/Receipting/ClientOutstanding/{clientId}" },
        { nameof(ITimeProApiClient.GetCreditNotesByClientAsync), "/api/creditnote/by-client/{clientId}" },
        { nameof(ITimeProApiClient.GetProductAsync), "/api/Product/{productId}" },
        { nameof(ITimeProApiClient.GetProductDiscountsForClientAsync), "/api/Product/GetDiscountsForClient/{clientId}" },
        { nameof(ITimeProApiClient.GetReceiptDetailAsync), "/api/Receipting/details/{receiptId}" },
        { nameof(ITimeProApiClient.GetInvoiceAsync), "/api/v2/ClientInvoice/{invoiceId}" },
        { nameof(ITimeProApiClient.GetInvoiceProductsAsync), "/api/v2/ClientInvoice/{invoiceId}/products" },
        { nameof(ITimeProApiClient.GetInvoiceReceiptsAsync), "/api/v2/ClientInvoice/{invoiceId}/receipts" },
        { nameof(ITimeProApiClient.GetRecurringInvoiceAsync), "/api/recurring/invoices/{invoiceId}" },
        { nameof(ITimeProApiClient.DeleteTimesheetAsync), "/api/Timesheets/DeleteTimesheet/{timesheetId}" },
        { nameof(ITimeProApiClient.DeleteSuggestedTimesheetAsync), "/api/Timesheets/DeleteSuggestedTimesheet/{suggestedId}" },
        { nameof(ITimeProApiClient.CancelLeaveAsync), "/api/leave/{leaveId}/cancel" },
        { nameof(ITimeProApiClient.GetInvoiceTimesheetsAsync), "/api/v2/Timesheets/WithNames/WriteOff" }
    };

    [Theory]
    [MemberData(nameof(ParameterisedRoutes))]
    public async Task TheRecordedRoute_IsTheTemplateAndNeverTheValue(string method, string expectedRoute)
    {
        StubEverything();
        ClientContext.BeginCli("ts get");

        await CallIgnoringBodyShapeAsync(method);

        // A call may make more than one request (GetUserAsync enriches from a second endpoint),
        // so every route it recorded is checked, not just the first.
        var routes = ClientContext.Current!.Requests.Select(r => r.Route).ToList();
        routes.Should().Contain(expectedRoute);

        foreach (var route in routes)
        foreach (var value in new[] { EmpId, ClientId, ProjectId, LeaveId, InvoiceId.ToString() })
            route.Should().NotContain(value, $"{method} must not log the value it was given");
    }

    [Fact]
    public async Task ARouteWithNoIdentifier_IsRecordedAsItsLiteralPath()
    {
        StubEverything();
        ClientContext.BeginCli("ts get");

        await Ignoring(() => ApiClient.GetLeaveTypesAsync(TestContext.Current.CancellationToken));

        ClientContext.Current!.Requests.Should().ContainSingle()
            .Which.Route.Should().Be("/api/leave/types");
    }

    /// <summary>
    /// The route is recorded when the response arrives, before the body is read, so a stub body
    /// that does not fit the endpoint's DTO is irrelevant to what is being asserted here.
    /// </summary>
    private Task CallIgnoringBodyShapeAsync(string method) => Ignoring(() => CallAsync(method));

    private static async Task Ignoring(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (System.Text.Json.JsonException)
        {
        }
    }

    private Task CallAsync(string method)
    {
        var ct = TestContext.Current.CancellationToken;

        return method switch
        {
            nameof(ITimeProApiClient.GetUserAsync) => ApiClient.GetUserAsync(EmpId, ct),
            nameof(ITimeProApiClient.GetLeaveStatsAsync) => ApiClient.GetLeaveStatsAsync(EmpId, ct),
            nameof(ITimeProApiClient.GetClientTaxRateAsync) => ApiClient.GetClientTaxRateAsync(ClientId, ct),
            nameof(ITimeProApiClient.GetInvoicesByClientAsync) => ApiClient.GetInvoicesByClientAsync(ClientId, ct),
            nameof(ITimeProApiClient.GetClientInvoiceTableByClientAsync) => ApiClient.GetClientInvoiceTableByClientAsync(ClientId, ct),
            nameof(ITimeProApiClient.GetUnpaidInvoicesByClientAsync) => ApiClient.GetUnpaidInvoicesByClientAsync(ClientId, ct),
            nameof(ITimeProApiClient.GetClientOutstandingAsync) => ApiClient.GetClientOutstandingAsync(ClientId, ct),
            nameof(ITimeProApiClient.GetCreditNotesByClientAsync) => ApiClient.GetCreditNotesByClientAsync(ClientId, ct),
            nameof(ITimeProApiClient.GetProductAsync) => ApiClient.GetProductAsync(ProjectId, ct),
            nameof(ITimeProApiClient.GetProductDiscountsForClientAsync) => ApiClient.GetProductDiscountsForClientAsync(ClientId, ct),
            nameof(ITimeProApiClient.GetReceiptDetailAsync) => ApiClient.GetReceiptDetailAsync(InvoiceId, ct),
            nameof(ITimeProApiClient.GetInvoiceAsync) => ApiClient.GetInvoiceAsync(InvoiceId, ct),
            nameof(ITimeProApiClient.GetInvoiceProductsAsync) => ApiClient.GetInvoiceProductsAsync(InvoiceId, ct),
            nameof(ITimeProApiClient.GetInvoiceReceiptsAsync) => ApiClient.GetInvoiceReceiptsAsync(InvoiceId, ct),
            nameof(ITimeProApiClient.GetRecurringInvoiceAsync) => ApiClient.GetRecurringInvoiceAsync(InvoiceId, ct),
            nameof(ITimeProApiClient.DeleteTimesheetAsync) => ApiClient.DeleteTimesheetAsync(InvoiceId, ct),
            nameof(ITimeProApiClient.DeleteSuggestedTimesheetAsync) => ApiClient.DeleteSuggestedTimesheetAsync(InvoiceId, ct),
            nameof(ITimeProApiClient.CancelLeaveAsync) => ApiClient.CancelLeaveAsync(
                LeaveId, new SSW.TimePro.Cli.Shared.Models.CancelLeaveRequest(), ct),
            nameof(ITimeProApiClient.GetInvoiceTimesheetsAsync) => ApiClient.GetInvoiceTimesheetsAsync(InvoiceId, "writeoff", ct),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Add the call for this route.")
        };
    }

    private void StubEverything() =>
        WireMock
            .Given(Request.Create().WithPath("/*").UsingAnyMethod())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));
}
