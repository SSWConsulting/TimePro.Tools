using SSW.TimePro.Cli.Features.Mcp.Tools;
using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// The single case catalog behind the MCP result goldens. One populated case per tool, plus the
/// generated empty and API-error variants declared on each case.
/// </summary>
public static class McpToolCatalog
{
    private const string Date = NorthwindApi.AnyDate;
    private const string Client = NorthwindApi.ClientId;
    private const string Project = NorthwindApi.ProjectId;
    private const string Emp = NorthwindApi.EmpId;

    /// <summary>The fixed IANA zone every leave case pins, so dry-run payloads never drift.</summary>
    public const string TimeZone = "Australia/Brisbane";

    public static IReadOnlyList<McpToolCase> Populated { get; } = BuildPopulated();

    public static IReadOnlyList<McpToolCase> All { get; } = Expand(Populated);

    public static IEnumerable<string> ToolNames => Populated.Select(c => c.ToolName);

    private static IReadOnlyList<McpToolCase> BuildPopulated() =>
    [
        // ───────────────────────── Timesheets ─────────────────────────

        new("GetTimesheets", "populated", (h, ct) => h.Timesheets.GetTimesheets(Date, ct: ct))
        {
            PrimaryRoute = "/api/Timesheets/GetTimesheetListViewModel",
            EmptyBody = "[]"
        },

        // The note and start time match the day's existing row so the empty-body read-back
        // actually finds the entry it claims to have created.
        new("CreateTimesheet", "populated", (h, ct) => h.Timesheets.CreateTimesheet(
            Client, Project, Date, description: "Product search", iterationId: 3402, ct: ct))
        {
            PrimaryRoute = "/api/Timesheets/SaveTimesheet",
            PrimaryMethod = "POST"
        },

        new("ListIterations", "populated", (h, ct) => h.Timesheets.ListIterations(Project, ct))
        {
            PrimaryRoute = "/api/ProjectIteration/GetIterationsForAddTimesheet",
            EmptyBody = "[]"
        },

        new("UpdateTimesheet", "populated", (h, ct) => h.Timesheets.UpdateTimesheet(
            NorthwindApi.TimesheetId, description: "Order history", date: Date, ct: ct))
        {
            PrimaryRoute = "/api/Timesheets/SaveTimesheet",
            PrimaryMethod = "POST"
        },

        new("DeleteTimesheet", "populated", (h, ct) => h.Timesheets.DeleteTimesheet(
            NorthwindApi.TimesheetId, date: Date, ct: ct))
        {
            PrimaryRoute = $"/api/Timesheets/DeleteTimesheet/{NorthwindApi.TimesheetId}",
            PrimaryMethod = "DELETE"
        },

        new("CheckWeek", "populated", (h, ct) => h.Timesheets.CheckWeek(0, ct: ct))
        {
            PrimaryRoute = "/api/Timesheets/GetTimesheetListViewModel",
            TokenizeCurrentWeek = true
        },

        new("GetSuggestedTimesheets", "populated", (h, ct) => h.Timesheets.GetSuggestedTimesheets(Date, ct))
        {
            PrimaryRoute = "/api/Timesheets/GetTimesheetListViewModel",
            EmptyBody = "[]"
        },

        new("AcceptSuggestedTimesheet", "populated", (h, ct) => h.Timesheets.AcceptSuggestedTimesheet(
            NorthwindApi.SuggestedTimesheetId, iteration: "Order history", date: Date, ct: ct))
        {
            PrimaryRoute = "/api/Timesheets/AcceptSuggestedTimesheet",
            PrimaryMethod = "POST"
        },

        // ───────────────────────── Lookups ─────────────────────────

        new("SearchClients", "populated", (h, ct) => h.Lookups.SearchClients("Northwind", ct))
        {
            PrimaryRoute = "/api/Timesheets/GetClientListForAddTimesheet",
            EmptyBody = "[]"
        },

        new("GetProjectsForClient", "populated", (h, ct) => h.Lookups.GetProjectsForClient(Client, ct))
        {
            PrimaryRoute = "/api/Projects/GetSelectListUsageDataProject",
            EmptyBody = "[]"
        },

        new("GetClientRate", "populated", (h, ct) => h.Lookups.GetClientRate(Client, Date, ct))
        {
            PrimaryRoute = "/api/Timesheets/GetClientRate",
            EmptyBody = "null"
        },

        new("GetCrmBookings", "populated", (h, ct) => h.Lookups.GetCrmBookings(Date, "2026-03-20", ct))
        {
            PrimaryRoute = "/Crm/Appointments",
            EmptyBody = "[]"
        },

        new("GetLocationAndMapping", "populated",
            (h, _) => Task.FromResult(h.Lookups.GetLocationAndMapping("~/code/traders-app")))
        {
            HasApiErrorCase = false
        },

        // ───────────────────────── Leave ─────────────────────────

        new("GetLeaveEntries", "populated", (h, ct) => h.Leave.GetLeaveEntries("UPCOMING", 10, Emp, ct: ct))
        {
            PrimaryRoute = "/api/leave/",
            EmptyBody = """{"leaves":{"pageNumber":1,"pageSize":10,"totalItems":0,"totalPages":0,"items":[]},"cancelledCount":0}"""
        },

        new("GetLeaveEntries", "past", (h, ct) => h.Leave.GetLeaveEntries("PAST", 10, Emp, ct: ct))
        {
            HasApiErrorCase = false
        },

        new("GetLeaveEntries", "all", (h, ct) => h.Leave.GetLeaveEntries("ALL", 10, Emp, ct: ct))
        {
            HasApiErrorCase = false,
            Arrange = NorthwindApi.StubDistinctPastLeave
        },

        new("GetLeaveEntries", "invalidFilter",
            (h, ct) => h.Leave.GetLeaveEntries("YESTERDAY", 10, Emp, ct: ct))
        {
            HasApiErrorCase = false
        },

        new("GetLeaveBalance", "populated", (h, ct) => h.Leave.GetLeaveBalance(Emp, ct: ct))
        {
            PrimaryRoute = $"/api/leave/stats/{Emp}",
            EmptyBody = "null"
        },

        new("CreateLeave", "dryRun", (h, ct) => h.Leave.CreateLeave(
            start: "2026-04-01",
            end: "2026-04-01",
            type: "Annual Leave",
            note: "Family day",
            approvedBy: NorthwindApi.EmpEmail,
            cc: NorthwindApi.EmpEmail,
            timeZoneId: TimeZone,
            dryRun: true,
            ct: ct))
        {
            PrimaryRoute = "/api/leave/types",
            HasApiErrorCase = true
        },

        // No PrimaryRoute: the generated empty/apiError variants would collide with the dryRun
        // case's goldens, which already cover a failing create.
        new("CreateLeave", "created", (h, ct) => h.Leave.CreateLeave(
            start: "2026-04-01",
            end: "2026-04-01",
            type: "Annual Leave",
            note: "Family day",
            approvedBy: NorthwindApi.EmpEmail,
            cc: NorthwindApi.EmpEmail,
            timeZoneId: TimeZone,
            ct: ct)),

        new("UpdateLeave", "dryRun", (h, ct) => h.Leave.UpdateLeave(
            id: NorthwindApi.LeaveId,
            note: "Family day (updated)",
            timeZoneId: TimeZone,
            dryRun: true,
            ct: ct))
        {
            PrimaryRoute = "/api/leave/",
            HasApiErrorCase = true
        },

        new("GetLeaveBalanceStatus", "populated", (h, ct) => h.Leave.GetLeaveBalanceStatus(ct))
        {
            PrimaryRoute = "/api/leave/balances/status",
            EmptyBody = "null"
        },

        // ───────────────────────── Accounting ─────────────────────────

        new("ImportLeaveBalances", "populated",
            (h, ct) => h.Accounting.ImportLeaveBalances(LeaveBalancesCsv.Path, ct))
        {
            PrimaryRoute = "/api/leave/balances/import",
            PrimaryMethod = "POST"
        },

        new("ListInvoices", "populated", (h, ct) => h.Accounting.ListInvoices(ct: ct))
        {
            PrimaryRoute = "/api/ClientInvoice/rangepaged",
            EmptyBody = """{"total":0,"data":[]}"""
        },

        new("GetInvoice", "populated", (h, ct) => h.Accounting.GetInvoice(NorthwindApi.InvoiceId, ct))
        {
            PrimaryRoute = $"/api/v2/ClientInvoice/{NorthwindApi.InvoiceId}",
            EmptyBody = "null"
        },

        new("GetInvoiceLines", "populated", (h, ct) => h.Accounting.GetInvoiceLines(NorthwindApi.InvoiceId, ct))
        {
            PrimaryRoute = $"/api/v2/ClientInvoice/{NorthwindApi.InvoiceId}/products",
            EmptyBody = "[]"
        },

        new("GetInvoiceTimesheets", "populated",
            (h, ct) => h.Accounting.GetInvoiceTimesheets(NorthwindApi.InvoiceId, "allocated", ct))
        {
            PrimaryRoute = "/api/v2/Timesheets/WithNames/Allocated",
            EmptyBody = "[]"
        },

        new("GetInvoiceReceipts", "populated",
            (h, ct) => h.Accounting.GetInvoiceReceipts(NorthwindApi.InvoiceId, ct))
        {
            PrimaryRoute = $"/api/v2/ClientInvoice/{NorthwindApi.InvoiceId}/receipts",
            EmptyBody = "[]"
        },

        new("GetInvoicesByClient", "populated", (h, ct) => h.Accounting.GetInvoicesByClient(Client, ct))
        {
            PrimaryRoute = $"/api/ClientInvoice/ClientID/{Client}",
            EmptyBody = "[]"
        },

        new("GetUnpaidInvoicesByClient", "populated",
            (h, ct) => h.Accounting.GetUnpaidInvoicesByClient(Client, ct))
        {
            PrimaryRoute = $"/api/ClientInvoice/UnpaidByClientID/{Client}",
            EmptyBody = "[]"
        },

        new("ListPaidReceipts", "populated", (h, ct) => h.Accounting.ListPaidReceipts(ct: ct))
        {
            PrimaryRoute = "/api/receipting/PaidReceiptsPaged",
            EmptyBody = """{"total":0,"data":[]}"""
        },

        new("GetReceiptDetail", "populated", (h, ct) => h.Accounting.GetReceiptDetail(NorthwindApi.ReceiptId, ct))
        {
            PrimaryRoute = $"/api/Receipting/details/{NorthwindApi.ReceiptId}",
            EmptyBody = "null"
        },

        new("GetClientOutstanding", "populated", (h, ct) => h.Accounting.GetClientOutstanding(Client, ct))
        {
            PrimaryRoute = $"/api/Receipting/ClientOutstanding/{Client}",
            EmptyBody = "null"
        },

        new("ListCreditNotes", "populated", (h, ct) => h.Accounting.ListCreditNotes(Client, ct))
        {
            PrimaryRoute = $"/api/creditnote/by-client/{Client}",
            EmptyBody = "[]"
        },

        new("ListProducts", "populated", (h, ct) => h.Accounting.ListProducts(isExpand: true, ct: ct))
        {
            PrimaryRoute = "/api/Product",
            EmptyBody = "[]"
        },

        new("GetProduct", "populated", (h, ct) => h.Accounting.GetProduct("PREPAID", ct))
        {
            PrimaryRoute = "/api/Product/PREPAID",
            EmptyBody = "null"
        },

        new("ListAllSkus", "populated", (h, ct) => h.Accounting.ListAllSkus(isPrepaid: true, ct: ct))
        {
            PrimaryRoute = "/api/Product/All",
            EmptyBody = "[]"
        },

        new("GetProductDiscountsForClient", "populated",
            (h, ct) => h.Accounting.GetProductDiscountsForClient(Client, ct))
        {
            PrimaryRoute = $"/api/Product/GetDiscountsForClient/{Client}",
            EmptyBody = "[]"
        },

        new("ListClientRates", "populated", (h, ct) => h.Accounting.ListClientRates(Client, ct: ct))
        {
            PrimaryRoute = "/api/clients/GetClientRates",
            EmptyBody = """{"rates":[],"total":0}"""
        },

        new("GetClientsWithOutstandingTime", "populated",
            (h, ct) => h.Accounting.GetClientsWithOutstandingTime(ct))
        {
            PrimaryRoute = "/api/clients/OutstandingTime",
            EmptyBody = "[]"
        },

        new("GetUnbilledTimesheetsForClient", "populated",
            (h, ct) => h.Accounting.GetUnbilledTimesheetsForClient(Client, ct: ct))
        {
            PrimaryRoute = "/api/v2/Timesheets/WithNames/Unallocated",
            EmptyBody = "[]"
        },

        new("ListRecurringInvoices", "populated", (h, ct) => h.Accounting.ListRecurringInvoices(ct: ct))
        {
            PrimaryRoute = "/api/recurring/invoices/",
            EmptyBody = """{"total":0,"data":[]}"""
        },

        new("GetRecurringInvoice", "populated",
            (h, ct) => h.Accounting.GetRecurringInvoice(NorthwindApi.RecurringInvoiceId, ct))
        {
            PrimaryRoute = $"/api/recurring/invoices/{NorthwindApi.RecurringInvoiceId}",
            EmptyBody = "null"
        },

        new("QueryTimesheets", "populated",
            (h, ct) => h.Accounting.QueryTimesheets("2026-03-01", "2026-03-31", empIds: [Emp], ct: ct))
        {
            PrimaryRoute = "/api/timesheetSummary/GetTableSummarydata",
            PrimaryMethod = "POST",
            EmptyBody = "[]"
        },

        new("GetCurrentUser", "populated", (h, ct) => h.Accounting.GetCurrentUser(ct))
        {
            PrimaryRoute = "/api/v2/users/me",
            EmptyBody = "null"
        },

        new("ListCategories", "populated", (h, ct) => h.Accounting.ListCategories(ct))
        {
            PrimaryRoute = "/api/Timesheets/GetTimesheetCategories",
            EmptyBody = "[]"
        },

        new("ListBillableTypes", "populated", (h, ct) => h.Accounting.ListBillableTypes(ct))
        {
            PrimaryRoute = "/api/Timesheets/GetTimesheetBillableType",
            EmptyBody = "[]"
        },

        new("ListLocations", "populated", (h, ct) => h.Accounting.ListLocations(ct))
        {
            PrimaryRoute = "/api/Timesheets/GetTimesheetLocation",
            EmptyBody = "[]"
        },

        new("GetProjectsSummary", "populated",
            (h, ct) => h.Accounting.GetProjectsSummary("2026-03-01", "2026-03-31", Emp, ct: ct))
        {
            PrimaryRoute = "/api/Timesheets/GetProjectsSummary",
            EmptyBody = "[]"
        },

        new("GetPrepaidStatus", "populated", (h, ct) => h.Accounting.GetPrepaidStatus(NorthwindApi.InvoiceId, ct))
        {
            PrimaryRoute = $"/api/v2/ClientInvoice/{NorthwindApi.InvoiceId}",
            EmptyBody = "null"
        },

        new("GetPrepaidStatusPdf", "populated",
            (h, ct) => h.Accounting.GetPrepaidStatusPdf(NorthwindApi.InvoiceId, 0, ct))
        {
            PrimaryRoute = "/Reporting/GetPrepaidStatusReport"
        },
    ];

    private static IReadOnlyList<McpToolCase> Expand(IReadOnlyList<McpToolCase> populated)
    {
        var cases = new List<McpToolCase>(populated);

        foreach (var populatedCase in populated)
        {
            if (populatedCase.PrimaryRoute is null)
                continue;

            if (populatedCase.EmptyBody is not null)
            {
                var body = populatedCase.EmptyBody;
                var route = populatedCase.PrimaryRoute;
                var method = populatedCase.PrimaryMethod;
                cases.Add(populatedCase with
                {
                    CaseName = "empty",
                    Arrange = server => NorthwindApi.RawJson(
                        server, route, method, body, NorthwindApi.OverridePriority)
                });
            }

            if (populatedCase.HasApiErrorCase)
            {
                var route = populatedCase.PrimaryRoute;
                var method = populatedCase.PrimaryMethod;
                cases.Add(populatedCase with
                {
                    CaseName = "apiError",
                    Arrange = server => NorthwindApi.Status(
                        server, route, method, 500,
                        """{"title":"Server error","detail":"Northwind API is unavailable"}""",
                        NorthwindApi.OverridePriority)
                });
            }
        }

        return cases;
    }

    public static TenantConfig Tenant(string apiUrl) => new()
    {
        TenantId = "northwind",
        ApiUrl = apiUrl,
        ApiKey = "test-api-key",
        EmployeeId = Emp,
        EmployeeName = NorthwindApi.EmpName,
        ConfigName = "northwind-test"
    };

    public static TestConfigService Config(string apiUrl) => new(Tenant(apiUrl))
    {
        RepoMappings =
        [
            new()
            {
                PathPattern = "~/code/traders-app",
                ClientId = Client,
                ProjectId = Project,
                ProjectName = NorthwindApi.ClientName,
                CategoryId = "WEBDEV"
            }
        ]
    };
}

/// <summary>A fixed Xero-style CSV on disk, so the import tool has a real path to read.</summary>
public static class LeaveBalancesCsv
{
    public static string Path { get; } = Create();

    private static string Create()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tp-mcp-harness-csv");
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "LeaveBalances.csv");
        File.WriteAllText(path,
            """
            Employee,Leave Type,Balance (Hours)
            Bob Northwind,Annual Leave,76.0
            Bobby Northwind,Annual Leave,12.0
            """);
        return path;
    }
}
