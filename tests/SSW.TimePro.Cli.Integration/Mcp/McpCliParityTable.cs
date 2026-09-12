using WireMock.Server;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>Expected HTTP traffic for a write case: same method, path, payload and count on both sides.</summary>
public sealed record ExpectedRequest(string Method, string Path, int Count = 1)
{
    /// <summary>Fragments the request body must contain, checked verbatim.</summary>
    public string[] BodyContains { get; init; } = [];
}

/// <summary>
/// One row per MCP tool: its CLI counterpart, whether the two are expected to agree today, and
/// the differences that are permitted while they do not.
/// </summary>
public sealed record ParityRow(string ToolMethod, string? CliCommandPath)
{
    /// <summary>Full argv run through the in-process command app; null means "not executed yet".</summary>
    public string[]? CliArgs { get; init; }

    public Func<McpToolHost, CancellationToken, Task<string>>? InvokeTool { get; init; }

    /// <summary>True once CLI and MCP return the same document for this case.</summary>
    public bool ExpectParity { get; init; }

    /// <summary>
    /// JSON paths allowed to differ. Anything else differing fails: no generic normalisation.
    /// </summary>
    public string[] PermittedDifferences { get; init; } = [];

    public Action<WireMockServer>? Arrange { get; init; }

    public ExpectedRequest[] ExpectedRequests { get; init; } = [];

    /// <summary>Requests that must not happen at all — the write-safety half of a parity case.</summary>
    public ExpectedRequest[] ForbiddenRequests { get; init; } = [];

    /// <summary>Why this row is not at parity yet. Required whenever <see cref="ExpectParity"/> is false.</summary>
    public string? Note { get; init; }

    public bool TokenizeCurrentWeek { get; init; }

    public bool IsExecutable => CliArgs is not null && InvokeTool is not null;

    public override string ToString() => ToolMethod;
}

/// <summary>
/// The CLI/MCP pairing for all 47 tools. Rows flip <c>ExpectParity</c> to true as the unification
/// slices land; the five tools with no CLI command at all are the shrink-only allowlist.
/// </summary>
public static class McpCliParityTable
{
    private const string Date = NorthwindApi.AnyDate;

    /// <summary>
    /// Tools with no CLI counterpart today. This list may only shrink: adding to it means shipping
    /// behaviour that exists only behind MCP, which AGENTS.md forbids.
    /// </summary>
    public static IReadOnlySet<string> ToolsWithoutCliMirror { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "GetInvoicesByClient",
        "GetUnpaidInvoicesByClient",
        "ListCategories",
        "ListBillableTypes",
        "ListAllSkus",
    };

    public static IReadOnlyList<ParityRow> Rows { get; } =
    [
        // ───────── Mirrored pairs over a shared service ─────────

        new("UpdateTimesheet", "ts update")
        {
            CliArgs = ["ts", "update", "4242", "--description", "Order history", "--date", Date, "--yes", "--json"],
            InvokeTool = (h, ct) => h.Timesheets.UpdateTimesheet(
                NorthwindApi.TimesheetId, description: "Order history", date: Date, ct: ct),
            ExpectParity = false,
            PermittedDifferences =
            [
                "$.iterationApplied", "$.message", "$.warning",
                "$.timesheet.inputSource", "$.timesheet.invoiceId",
                "$.timesheet.invoiceType", "$.timesheet.iterationId"
            ],
            Note = "CLI omits nulls; MCP writes them. TimesheetUpdateService already shared.",
            ExpectedRequests =
            [
                new("POST", "/api/Timesheets/SaveTimesheet")
                {
                    BodyContains = ["\"timeID\":4242", "\"Notes\":\"Order history\"", "\"sellPrice\":175"]
                }
            ]
        },

        new("AcceptSuggestedTimesheet", "ts accept")
        {
            CliArgs = ["ts", "accept", "4243", "--iteration", "Order history", "--date", Date, "--yes", "--json"],
            InvokeTool = (h, ct) => h.Timesheets.AcceptSuggestedTimesheet(
                NorthwindApi.SuggestedTimesheetId, iteration: "Order history", date: Date, ct: ct),
            ExpectParity = false,
            PermittedDifferences =
            [
                "$.message", "$.warning",
                "$.timesheet.inputSource", "$.timesheet.invoiceId",
                "$.timesheet.invoiceType", "$.timesheet.iterationId"
            ],
            Note = "CLI omits nulls; MCP writes them. TimesheetAcceptService already shared.",
            ExpectedRequests =
            [
                new("POST", "/api/Timesheets/AcceptSuggestedTimesheet"),
                new("POST", "/api/Timesheets/SaveTimesheet")
            ]
        },

        new("DeleteTimesheet", "ts delete")
        {
            CliArgs = ["ts", "delete", "4242", "--date", Date, "--yes", "--json"],
            InvokeTool = (h, ct) => h.Timesheets.DeleteTimesheet(NorthwindApi.TimesheetId, date: Date, ct: ct),
            ExpectParity = true,
            ExpectedRequests = [new("DELETE", $"/api/Timesheets/DeleteTimesheet/{NorthwindApi.TimesheetId}")]
        },

        new("DeleteTimesheet", "ts delete")
        {
            CliArgs = ["ts", "delete", "4243", "--date", Date, "--yes", "--json"],
            InvokeTool = (h, ct) => h.Timesheets.DeleteTimesheet(NorthwindApi.SuggestedTimesheetId, date: Date, ct: ct),
            ExpectParity = false,
            PermittedDifferences = ["$.error"],
            Note = "Both refuse the suggestion and write nothing, but the CLI renders the shared "
                 + "message through the --json error envelope while MCP returns a bare error string.",
            ForbiddenRequests =
            [
                new("DELETE", "/api/Timesheets/DeleteTimesheet/4243"),
                new("DELETE", "/api/Timesheets/DeleteTimesheet/4242")
            ]
        },

        new("CheckWeek", "ts check")
        {
            CliArgs = ["ts", "check", "--week", "0", "--json"],
            InvokeTool = (h, ct) => h.Timesheets.CheckWeek(0, ct: ct),
            ExpectParity = false,
            PermittedDifferences = ["$.tenant", "$.leaveType", "$.days[0].leaveType", "$.days[1].leaveType",
                                    "$.days[2].leaveType", "$.days[3].leaveType", "$.days[4].leaveType"],
            Note = "WeekCoverageService is shared; the envelopes still differ in null handling.",
            TokenizeCurrentWeek = true
        },

        new("CreateLeave", "leave create")
        {
            CliArgs = ["leave", "create", "--start", "2026-04-01", "--end", "2026-04-01",
                       "--type", "Annual Leave", "--note", "Family day",
                       "--approved-by", NorthwindApi.EmpEmail, "--cc", NorthwindApi.EmpEmail,
                       "--timezone", McpToolCatalog.TimeZone, "--dry-run", "--json"],
            InvokeTool = (h, ct) => h.Leave.CreateLeave(
                start: "2026-04-01", end: "2026-04-01", type: "Annual Leave", note: "Family day",
                approvedBy: NorthwindApi.EmpEmail, cc: NorthwindApi.EmpEmail,
                timeZoneId: McpToolCatalog.TimeZone, dryRun: true, ct: ct),
            ExpectParity = false,
            PermittedDifferences = ["$.request.timeLessOverride"],
            Note = "LeaveCreateService is shared; the CLI omits the null TimeLessOverride.",
            ForbiddenRequests = [new("POST", "/api/leave/")]
        },

        new("UpdateLeave", "leave update")
        {
            CliArgs = ["leave", "update", NorthwindApi.LeaveId, "--note", "Family day (updated)",
                       "--timezone", McpToolCatalog.TimeZone, "--dry-run", "--json"],
            InvokeTool = (h, ct) => h.Leave.UpdateLeave(
                id: NorthwindApi.LeaveId, note: "Family day (updated)",
                timeZoneId: McpToolCatalog.TimeZone, dryRun: true, ct: ct),
            ExpectParity = false,
            PermittedDifferences = ["$.request.timeLessOverride"],
            Note = "LeaveUpdateService is shared; the CLI omits the null TimeLessOverride.",
            ForbiddenRequests = [new("PUT", "/api/leave/")]
        },

        new("ImportLeaveBalances", "leave balances import")
        {
            CliArgs = ["leave", "balances", "import", LeaveBalancesCsv.Path, "--yes", "--json"],
            InvokeTool = (h, ct) => h.Accounting.ImportLeaveBalances(LeaveBalancesCsv.Path, ct),
            ExpectParity = false,
            PermittedDifferences = ["$.success"],
            Note = "LeaveBalanceImportService is shared; only the MCP success flag differs.",
            ExpectedRequests = [new("POST", "/api/leave/balances/import") { BodyContains = ["Bob Northwind"] }]
        },

        new("GetLeaveBalanceStatus", "leave balances status")
        {
            CliArgs = ["leave", "balances", "status", "--json"],
            InvokeTool = (h, ct) => h.Leave.GetLeaveBalanceStatus(ct),
            ExpectParity = true
        },

        new("GetProjectsForClient", "project list")
        {
            CliArgs = ["project", "list", "--client", NorthwindApi.ClientId, "--json"],
            InvokeTool = (h, ct) => h.Lookups.GetProjectsForClient(NorthwindApi.ClientId, ct),
            ExpectParity = true,
            Note = "ProjectLookup is shared, including the dropdown placeholder filter."
        },

        new("GetClientRate", "rate get")
        {
            CliArgs = ["rate", "get", "--client", NorthwindApi.ClientId, "--date", Date, "--json"],
            InvokeTool = (h, ct) => h.Lookups.GetClientRate(NorthwindApi.ClientId, Date, ct),
            ExpectParity = false,
            PermittedDifferences = ["$.found", "$.date"],
            Note = "RateLookup is shared; MCP still returns the raw rate and a bare null on a miss. "
                 + "Giving it the CLI's RateLookupResult shape is a 0.4.0 contract change."
        },

        // ───────── Parity of output only: the two implementations are still separate ─────────

        new("ListIterations", "iteration list")
        {
            CliArgs = ["iteration", "list", "--project", NorthwindApi.ProjectId, "--json"],
            InvokeTool = (h, ct) => h.Timesheets.ListIterations(NorthwindApi.ProjectId, ct),
            ExpectParity = true,
            Note = "Separate projections over one API call; equal today, nothing enforces it but this row."
        },

        new("SearchClients", "client search")
        {
            CliArgs = ["client", "search", "Northwind", "--json"],
            InvokeTool = (h, ct) => h.Lookups.SearchClients("Northwind", ct),
            ExpectParity = true,
            Note = "Separate projections; the CLI --limit has no MCP argument, so sharing it is a schema change."
        },

        new("GetCrmBookings", "booking list")
        {
            CliArgs = ["booking", "list", "--date", Date, "--json"],
            InvokeTool = (h, ct) => h.Lookups.GetCrmBookings(Date, Date, ct),
            ExpectParity = true,
            Note = "Separate projections over one API call; the CLI derives the same range from --date."
        },

        // ───────── Not unified yet: declared so a later slice flips the flag ─────────

        new("GetTimesheets", "ts get") { Note = "MCP reshapes the row; CLI returns the API shape." },
        new("CreateTimesheet", "ts create") { Note = "No shared create orchestration; MCP sends no sell price." },
        new("GetSuggestedTimesheets", "ts suggest") { Note = "Separate projections." },
        new("GetLocationAndMapping", "location info") { Note = "MCP merges location defaults and repo mapping." },
        new("GetLeaveEntries", "leave list") { Note = "MCP returns the items array, CLI the envelope." },
        new("GetLeaveBalance", "leave balance") { Note = "Separate projections." },
        new("ListInvoices", "invoice list") { Note = "Separate projections." },
        new("GetInvoice", "invoice get") { Note = "Separate projections." },
        new("GetInvoiceLines", "invoice lines") { Note = "Separate projections." },
        new("GetInvoiceTimesheets", "invoice timesheets") { Note = "Separate projections." },
        new("GetInvoiceReceipts", "invoice receipts") { Note = "Separate projections." },
        new("ListPaidReceipts", "receipt list") { Note = "Separate projections." },
        new("GetReceiptDetail", "receipt get") { Note = "Separate projections." },
        new("GetClientOutstanding", "receipt outstanding") { Note = "Separate projections." },
        new("ListCreditNotes", "creditnote list") { Note = "Separate projections." },
        new("ListProducts", "product list") { Note = "Separate projections." },
        new("GetProduct", "product get") { Note = "Separate projections." },
        new("GetProductDiscountsForClient", "product discounts") { Note = "Separate projections." },
        new("ListClientRates", "rate list") { Note = "Separate projections." },
        new("GetClientsWithOutstandingTime", "client outstanding") { Note = "Separate projections." },
        new("GetUnbilledTimesheetsForClient", "unbilled list") { Note = "Separate projections." },
        new("ListRecurringInvoices", "recurring list") { Note = "Separate projections." },
        new("GetRecurringInvoice", "recurring get") { Note = "Separate projections." },
        new("QueryTimesheets", "query") { Note = "Separate projections." },
        new("GetCurrentUser", "user me") { Note = "Separate projections." },
        new("ListLocations", "location info") { Note = "Separate projections." },
        new("GetProjectsSummary", "summary") { Note = "Separate projections." },
        new("GetPrepaidStatus", "prepaid summary") { Note = "Separate projections." },
        new("GetPrepaidStatusPdf", "prepaid status") { Note = "CLI writes a file; MCP returns base64." },

        // TODO #38: give these a CLI command and drop them from the allowlist

        new("GetInvoicesByClient", null) { Note = "No CLI command lists a client's full invoice history." },
        new("GetUnpaidInvoicesByClient", null) { Note = "No CLI command lists a client's unpaid invoices." },
        new("ListCategories", null) { Note = "No CLI command lists timesheet category codes." },
        new("ListBillableTypes", null) { Note = "No CLI command lists billable-type codes." },
        new("ListAllSkus", null) { Note = "No CLI command lists SKUs independently of products." },
    ];

    public static IEnumerable<ParityRow> Executable => Rows.Where(r => r.IsExecutable);
}
