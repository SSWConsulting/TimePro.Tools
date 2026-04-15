using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Mcp.Tools;

/// <summary>
/// MCP tools for accountant-focused read-only operations. Also exposes cross-domain read
/// tools useful to accountants (timesheet queries, product lists, rate tables, etc.) that
/// aren't already on <see cref="LookupMcpTools"/> or <see cref="TimesheetMcpTools"/>.
///
/// Designed for composition with a Xero MCP server: an agent can call ListInvoices /
/// ListPaidReceipts here and cross-check amounts against Xero tool responses.
/// </summary>
[McpServerToolType]
public class AccountingMcpTools
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AccountingMcpTools(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    private bool NotAuthed(out string error)
    {
        if (_config.LoadActiveTenantConfig() is null)
        {
            error = """{"error":"Not logged in. Run 'tp login --tenant <id>' first."}""";
            return true;
        }
        error = string.Empty;
        return false;
    }

    // ─── Invoices ───────────────────────────────────────────────────────────

    [McpServerTool]
    [Description("List invoices (paged). Filter by query text, sort by DateCreated/DateInvoiced/SellTotal/ClientID. Set onlyRecurring=true for recurring-generated invoices.")]
    public async Task<string> ListInvoices(
        [Description("Free-text search (client name, invoice id)")] string? query = null,
        [Description("Rows to skip (default 0)")] int skip = 0,
        [Description("Page size (default 50)")] int limit = 50,
        [Description("Sort field (default DateCreated)")] string field = "DateCreated",
        [Description("Sort direction: asc or desc (default desc)")] string dir = "desc",
        [Description("Filter to recurring-generated invoices only")] bool onlyRecurring = false,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var page = await _api.ListInvoicesAsync(query, skip, limit, field, dir, onlyRecurring, ct);
        return JsonSerializer.Serialize(page, JsonOpts);
    }

    [McpServerTool]
    [Description("Get a single invoice header: totals, paid amount, outstanding, GST, margin, external sync status.")]
    public async Task<string> GetInvoice(
        [Description("Invoice ID")] int invoiceId,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var inv = await _api.GetInvoiceAsync(invoiceId, ct);
        return JsonSerializer.Serialize(inv, JsonOpts);
    }

    [McpServerTool]
    [Description("List line items (products) on an invoice.")]
    public async Task<string> GetInvoiceLines(
        [Description("Invoice ID")] int invoiceId,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var lines = await _api.GetInvoiceProductsAsync(invoiceId, ct);
        return JsonSerializer.Serialize(lines, JsonOpts);
    }

    [McpServerTool]
    [Description("List timesheets billed on an invoice. Set type='writeoff' for written-off timesheets instead of allocated.")]
    public async Task<string> GetInvoiceTimesheets(
        [Description("Invoice ID")] int invoiceId,
        [Description("Either 'allocated' (default) or 'writeoff'")] string type = "allocated",
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.GetInvoiceTimesheetsAsync(invoiceId, type, ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    [McpServerTool]
    [Description("List receipts (payments) against a specific invoice.")]
    public async Task<string> GetInvoiceReceipts(
        [Description("Invoice ID")] int invoiceId,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.GetInvoiceReceiptsAsync(invoiceId, ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    [McpServerTool]
    [Description("List all invoices for a client (full history, unpaged).")]
    public async Task<string> GetInvoicesByClient(
        [Description("Client ID")] string clientId,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.GetInvoicesByClientAsync(clientId, ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    [McpServerTool]
    [Description("List unpaid invoices for a client.")]
    public async Task<string> GetUnpaidInvoicesByClient(
        [Description("Client ID")] string clientId,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.GetUnpaidInvoicesByClientAsync(clientId, ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    // ─── Receipts ───────────────────────────────────────────────────────────

    [McpServerTool]
    [Description("List paid receipts (paged). Sort by PaymentDate descending by default. PaidTotal is NEGATIVE for incoming payments; take abs() for reported sales.")]
    public async Task<string> ListPaidReceipts(
        [Description("Search text (client/reference)")] string? searchText = null,
        [Description("Rows to skip")] int skip = 0,
        [Description("Page size (500 is safe)")] int limit = 100,
        [Description("Sort field (default PaymentDate)")] string field = "PaymentDate",
        [Description("Sort direction")] string dir = "desc",
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var page = await _api.ListPaidReceiptsAsync(searchText, skip, limit, field, dir, ct);
        return JsonSerializer.Serialize(page, JsonOpts);
    }

    [McpServerTool]
    [Description("Get a receipt with its invoice allocations (how the payment is split across invoices).")]
    public async Task<string> GetReceiptDetail(
        [Description("Receipt ID")] int receiptId,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var d = await _api.GetReceiptDetailAsync(receiptId, ct);
        return JsonSerializer.Serialize(d, JsonOpts);
    }

    [McpServerTool]
    [Description("Get the aged-debtor view for a client: outstanding invoices with days overdue.")]
    public async Task<string> GetClientOutstanding(
        [Description("Client ID")] string clientId,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var d = await _api.GetClientOutstandingAsync(clientId, ct);
        return JsonSerializer.Serialize(d, JsonOpts);
    }

    // ─── Credit notes ───────────────────────────────────────────────────────

    [McpServerTool]
    [Description("List credit notes for a client.")]
    public async Task<string> ListCreditNotes(
        [Description("Client ID")] string clientId,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.GetCreditNotesByClientAsync(clientId, ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    // ─── Products / SKUs ────────────────────────────────────────────────────

    [McpServerTool]
    [Description("List products. With isExpand=true, each product includes its SKUs inline.")]
    public async Task<string> ListProducts(
        [Description("Expand SKUs")] bool isExpand = false,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.ListProductsAsync(isExpand, ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    [McpServerTool]
    [Description("Get a single product by ID.")]
    public async Task<string> GetProduct(
        [Description("Product ID")] string productId,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var p = await _api.GetProductAsync(productId, ct);
        return JsonSerializer.Serialize(p, JsonOpts);
    }

    [McpServerTool]
    [Description("List all SKUs. Set isPrepaid=true to list only prepaid SKUs (useful for prepaid drawdown analysis).")]
    public async Task<string> ListAllSkus(
        [Description("Filter to prepaid SKUs only")] bool isPrepaid = false,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.ListAllSkusAsync(isPrepaid, ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    [McpServerTool]
    [Description("Show product discounts configured for a client.")]
    public async Task<string> GetProductDiscountsForClient(
        [Description("Client ID")] string clientId,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.GetProductDiscountsForClientAsync(clientId, ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    // ─── Rates / outstanding / unbilled ─────────────────────────────────────

    [McpServerTool]
    [Description("List all configured client rates (all employees by default). For just the current employee's rate for a client, prefer GetClientRate.")]
    public async Task<string> ListClientRates(
        [Description("Client ID")] string clientId,
        [Description("Filter to one employee ID")] string? empId = null,
        [Description("Include expired rates")] bool showExpired = false,
        [Description("Page size (default 100)")] int? pageSize = 100,
        [Description("Rows to skip")] int? skip = 0,
        [Description("Sort field (default ExpiryDate)")] string? sortField = "ExpiryDate",
        [Description("Sort direction (asc/desc)")] string? direction = "desc",
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var d = await _api.ListClientRatesAsync(clientId, empId, showExpired, pageSize, skip, sortField, direction, selectAll: false, ct);
        return JsonSerializer.Serialize(d, JsonOpts);
    }

    [McpServerTool]
    [Description("List clients that have outstanding (unbilled) time, with OS balance and earliest unallocated timesheet date.")]
    public async Task<string> GetClientsWithOutstandingTime(CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.GetClientsWithOutstandingTimeAsync(ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    [McpServerTool]
    [Description("List unbilled (unallocated) timesheets for a client — revenue still in the pipeline.")]
    public async Task<string> GetUnbilledTimesheetsForClient(
        [Description("Client ID")] string clientId,
        [Description("Page size")] int? pageSize = null,
        [Description("Rows to skip")] int? skip = null,
        [Description("Sort field (e.g. DateCreated)")] string? sortField = null,
        [Description("Sort direction")] string? direction = null,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.GetUnallocatedTimesheetsByClientAsync(clientId, pageSize, skip, sortField, direction, ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    // ─── Recurring ──────────────────────────────────────────────────────────

    [McpServerTool]
    [Description("List recurring invoice templates.")]
    public async Task<string> ListRecurringInvoices(
        [Description("Search text")] string? query = null,
        [Description("Filter to one client")] string? clientId = null,
        [Description("Include outdated/stopped templates")] bool showOutdated = false,
        [Description("Rows to skip")] int skip = 0,
        [Description("Page size")] int limit = 50,
        [Description("Sort field (default LastInvEndDate)")] string field = "LastInvEndDate",
        [Description("Sort direction")] string dir = "desc",
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var page = await _api.ListRecurringInvoicesAsync(query, clientId, showOutdated, skip, limit, field, dir, ct);
        return JsonSerializer.Serialize(page, JsonOpts);
    }

    [McpServerTool]
    [Description("Get a recurring invoice template (includes product lines).")]
    public async Task<string> GetRecurringInvoice(
        [Description("Recurring invoice ID")] int recurringId,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var d = await _api.GetRecurringInvoiceAsync(recurringId, ct);
        return JsonSerializer.Serialize(d, JsonOpts);
    }

    // ─── Cross-domain reads (useful alongside accounting queries) ───────────

    [McpServerTool]
    [Description("Query timesheets across employees, clients, projects and a date range. Returns detailed rows including hours and sell price — use for audits, billable-hour reconciliation, month-to-month comparisons.")]
    public async Task<string> QueryTimesheets(
        [Description("Start date (yyyy-MM-dd)")] string startDate,
        [Description("End date (yyyy-MM-dd)")] string endDate,
        [Description("Filter to specific employee IDs (omit for all)")] string[]? employeeIds = null,
        [Description("Filter to specific client IDs")] string[]? clientIds = null,
        [Description("Filter to specific project IDs")] string[]? projectIds = null,
        [Description("Filter to specific category IDs")] string[]? categoryIds = null,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;

        var filter = new TimesheetSummaryFilter
        {
            StartDate = startDate,
            EndDate = endDate,
            EmployeeIds = employeeIds?.ToList() ?? [],
            ClientIds = clientIds?.ToList() ?? [],
            ProjectIds = projectIds?.ToList() ?? [],
            CategoryIds = categoryIds?.ToList() ?? [],
        };
        var rows = await _api.QueryTimesheetsAsync(filter, ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    [McpServerTool]
    [Description("Get the current user (identity, tenant, display name) — handy for showing who an MCP session is authenticated as.")]
    public async Task<string> GetCurrentUser(CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var u = await _api.GetCurrentUserAsync(ct);
        return JsonSerializer.Serialize(u, JsonOpts);
    }

    [McpServerTool]
    [Description("List timesheet category codes (for labelling timesheets when creating or filtering queries).")]
    public async Task<string> ListCategories(CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.GetCategoriesAsync(ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    [McpServerTool]
    [Description("List timesheet billable-type codes (B = billable, BPP = prepaid, W = write-off, etc.).")]
    public async Task<string> ListBillableTypes(CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.GetBillableTypesAsync(ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    [McpServerTool]
    [Description("List timesheet location codes (SSW, Home, Client, Travel, Other).")]
    public async Task<string> ListLocations(CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var rows = await _api.GetLocationsAsync(ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    [McpServerTool]
    [Description("Project-hours summary for the current employee over a period (billable vs non-billable across projects).")]
    public async Task<string> GetProjectsSummary(
        [Description("Start date (yyyy-MM-dd)")] string startDate,
        [Description("End date (yyyy-MM-dd)")] string endDate,
        CancellationToken ct = default)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
            return """{"error":"Not logged in. Run 'tp login --tenant <id>' first."}""";

        var start = DateOnly.ParseExact(startDate, "yyyy-MM-dd");
        var end = DateOnly.ParseExact(endDate, "yyyy-MM-dd");
        var rows = await _api.GetProjectsSummaryAsync(tenant.EmployeeId, start, end, ct);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    // ─── Prepaid (PDF → base64 in JSON wrapper) ─────────────────────────────

    [McpServerTool]
    [Description("Download the prepaid drawdown status report PDF for an invoice. Returns base64-encoded bytes inside a JSON envelope — decode on the client side to save as .pdf. The row-level drawdown details aren't otherwise exposed via JSON.")]
    public async Task<string> GetPrepaidStatusPdf(
        [Description("Prepaid invoice ID")] int invoiceId,
        [Description("Report template ID (0 = tenant default)")] int templateId = 0,
        CancellationToken ct = default)
    {
        if (NotAuthed(out var err)) return err;
        var bytes = await _api.GetPrepaidStatusReportPdfAsync(invoiceId, templateId, ct);
        var payload = new
        {
            contentType = "application/pdf",
            invoiceId,
            templateId,
            sizeBytes = bytes.Length,
            contentBase64 = Convert.ToBase64String(bytes)
        };
        return JsonSerializer.Serialize(payload, JsonOpts);
    }
}
