using System.Net.Http.Json;
using System.Text.Json;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Infrastructure.ApiClient;

/// <summary>
/// Provides the current tenant configuration for the API client.
/// </summary>
public interface ITenantProvider
{
    TenantConfig? GetCurrentTenant();
}

/// <summary>
/// Abstraction over all TimePro REST API calls.
/// </summary>
public interface ITimeProApiClient
{
    Task<List<TimesheetItem>> GetTimesheetsAsync(string employeeId, DateOnly date, CancellationToken ct = default);
    Task<EmployeeIdResponse?> GetEmployeeIdAsync(CancellationToken ct = default);
    Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken ct = default);
    Task<EmployeeSettings?> GetEmployeeSettingsAsync(CancellationToken ct = default);
    Task<List<ClientSearchResult>> SearchClientsAsync(string employeeId, string searchText, CancellationToken ct = default);
    Task<List<ProjectForSelect>> GetProjectsForClientAsync(string employeeId, string clientId, CancellationToken ct = default);
    Task<ClientRateResponse?> GetClientRateAsync(string employeeId, string clientId, DateOnly date, CancellationToken ct = default);
    Task<List<AppointmentItem>> GetAppointmentsAsync(string employeeId, DateOnly start, DateOnly end, CancellationToken ct = default);
    Task<TimesheetResponse?> CreateTimesheetAsync(TimesheetRequest request, CancellationToken ct = default);
    Task<TimesheetResponse?> UpdateTimesheetAsync(TimesheetRequest request, CancellationToken ct = default);
    Task DeleteTimesheetAsync(int timesheetId, CancellationToken ct = default);
    Task<List<TimesheetLocation>> GetLocationsAsync(CancellationToken ct = default);
    Task<List<TimesheetCategory>> GetCategoriesAsync(CancellationToken ct = default);
    Task<List<TimesheetBillableType>> GetBillableTypesAsync(CancellationToken ct = default);
    Task<List<TimesheetItem>> RefreshSuggestedTimesheetsAsync(string employeeId, DateOnly date, CancellationToken ct = default);
    Task<TimesheetResponse?> AcceptSuggestedTimesheetAsync(int suggestedId, string? location, string? notes, decimal? newSellPrice, CancellationToken ct = default);
    Task DeleteSuggestedTimesheetAsync(int suggestedId, CancellationToken ct = default);
    Task<LeaveListResponse?> GetLeaveAsync(string filter, int pageNumber, int pageSize, CancellationToken ct = default);
    Task<List<LeaveTypeInfo>> GetLeaveTypesAsync(CancellationToken ct = default);
    Task CreateLeaveAsync(CreateLeaveRequest request, CancellationToken ct = default);
    Task UpdateLeaveAsync(UpdateLeaveRequest request, CancellationToken ct = default);
    Task CancelLeaveAsync(string leaveId, CancelLeaveRequest request, CancellationToken ct = default);
    Task<byte[]> ExportTimesheetsCsvAsync(DateOnly? startDate, DateOnly? endDate, CancellationToken ct = default);
    Task<List<BlogEntry>> GetBlogsAsync(bool includeFormerEmployees = false, CancellationToken ct = default);
    Task<List<ProjectSummaryItem>> GetProjectsSummaryAsync(string employeeId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default);
    Task<List<IterationItem>> GetIterationsAsync(string projectId, CancellationToken ct = default);
    Task<List<TimesheetSummaryEntry>> QueryTimesheetsAsync(TimesheetSummaryFilter filter, CancellationToken ct = default);

    // ─── Accounting (read-only) ───
    Task<PagedResponse<InvoiceSearchRow>?> ListInvoicesAsync(string? query, int skip, int limit, string field, string dir, bool onlyRecurring, CancellationToken ct = default);
    Task<InvoiceHeader?> GetInvoiceAsync(int invoiceId, CancellationToken ct = default);
    Task<List<InvoiceLine>> GetInvoiceProductsAsync(int invoiceId, CancellationToken ct = default);
    Task<List<InvoiceTimesheet>> GetInvoiceTimesheetsAsync(int invoiceId, string type, CancellationToken ct = default);
    Task<List<ReceiptRow>> GetInvoiceReceiptsAsync(int invoiceId, CancellationToken ct = default);
    Task<List<InvoiceHeader>> GetInvoicesByClientAsync(string clientId, CancellationToken ct = default);
    Task<List<InvoiceHeader>> GetUnpaidInvoicesByClientAsync(string clientId, CancellationToken ct = default);

    Task<PagedResponse<ReceiptRow>?> ListPaidReceiptsAsync(string? searchText, int skip, int limit, string field, string dir, CancellationToken ct = default);
    Task<ReceiptDetail?> GetReceiptDetailAsync(int receiptId, CancellationToken ct = default);
    Task<ClientOutstandingSummary?> GetClientOutstandingAsync(string clientId, CancellationToken ct = default);

    Task<List<CreditNoteRow>> GetCreditNotesByClientAsync(string clientId, CancellationToken ct = default);

    Task<List<ProductRow>> ListProductsAsync(bool isExpand, CancellationToken ct = default);
    Task<ProductRow?> GetProductAsync(string productId, CancellationToken ct = default);
    Task<List<ProductSkuRow>> ListAllSkusAsync(bool isPrepaid, CancellationToken ct = default);
    Task<List<ProductDiscountRow>> GetProductDiscountsForClientAsync(string clientId, CancellationToken ct = default);

    Task<ClientRateTable?> ListClientRatesAsync(string clientId, string? empId, bool showExpired, int? pageSize, int? skip, string? sortField, string? direction, bool selectAll, CancellationToken ct = default);
    Task<List<ClientOutstandingTimeRow>> GetClientsWithOutstandingTimeAsync(CancellationToken ct = default);
    Task<List<InvoiceTimesheet>> GetUnallocatedTimesheetsByClientAsync(string clientId, int? pageSize, int? skip, string? sortField, string? direction, CancellationToken ct = default);

    Task<PagedResponse<RecurringInvoiceRow>?> ListRecurringInvoicesAsync(string? query, string? clientId, bool showOutdated, int skip, int limit, string field, string dir, CancellationToken ct = default);
    Task<RecurringInvoiceDetail?> GetRecurringInvoiceAsync(int invoiceId, CancellationToken ct = default);

    Task<byte[]> GetPrepaidStatusReportPdfAsync(int invoiceId, int templateId, CancellationToken ct = default);
}

/// <summary>
/// Typed HttpClient for the TimePro API. Registered via DI with
/// <c>services.AddHttpClient&lt;ITimeProApiClient, TimeProApiClient&gt;()</c>.
/// Auth headers are set per-request from the current <see cref="ITenantProvider"/>.
/// </summary>
public class TimeProApiClient : ITimeProApiClient
{
    private readonly HttpClient _http;
    private readonly ITenantProvider _tenantProvider;

    /// <summary>
    /// Options for deserializing API responses (case-insensitive).
    /// </summary>
    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Options for serializing request bodies. No naming policy so that
    /// [JsonPropertyName] attributes are respected (e.g. "empID", "clientID").
    /// </summary>
    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public TimeProApiClient(HttpClient http, ITenantProvider tenantProvider)
    {
        _http = http;
        _tenantProvider = tenantProvider;
    }

    // ───────────────────────── Timesheets ─────────────────────────

    public async Task<List<TimesheetItem>> GetTimesheetsAsync(
        string employeeId, DateOnly date, CancellationToken ct = default)
    {
        var url = $"/api/Timesheets/GetTimesheetListViewModel?employeeID={Uri.EscapeDataString(employeeId)}&date={date:yyyy-MM-dd}";
        return await GetAsync<List<TimesheetItem>>(url, ct) ?? [];
    }

    public async Task<TimesheetResponse?> CreateTimesheetAsync(
        TimesheetRequest request, CancellationToken ct = default)
    {
        return await PostAsync<TimesheetResponse>(
            "/api/Timesheets/SaveTimesheet?isEdit=false&isSuggested=false", request, ct);
    }

    public async Task<TimesheetResponse?> UpdateTimesheetAsync(
        TimesheetRequest request, CancellationToken ct = default)
    {
        return await PostAsync<TimesheetResponse>(
            "/api/Timesheets/SaveTimesheet?isEdit=true&isSuggested=false", request, ct);
    }

    public async Task DeleteTimesheetAsync(int timesheetId, CancellationToken ct = default)
    {
        await DeleteAsync($"/api/Timesheets/DeleteTimesheet/{timesheetId}", ct);
    }

    // ───────────────────────── Employees / Users ─────────────────────────

    public async Task<EmployeeIdResponse?> GetEmployeeIdAsync(CancellationToken ct = default)
    {
        return await GetAsync<EmployeeIdResponse>("/api/Employees/GetEmployeeID", ct);
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        return await GetAsync<CurrentUserResponse>("/api/v2/users/me", ct);
    }

    public async Task<EmployeeSettings?> GetEmployeeSettingsAsync(CancellationToken ct = default)
    {
        return await GetAsync<EmployeeSettings>("/api/employees/getSettingsDetails", ct);
    }

    // ───────────────────────── Clients / Projects ─────────────────────────

    public async Task<List<ClientSearchResult>> SearchClientsAsync(
        string employeeId, string searchText, CancellationToken ct = default)
    {
        var url = $"/api/Timesheets/GetClientListForAddTimesheet?empID={Uri.EscapeDataString(employeeId)}&searchText={Uri.EscapeDataString(searchText)}";
        return await GetAsync<List<ClientSearchResult>>(url, ct) ?? [];
    }

    public async Task<List<ProjectForSelect>> GetProjectsForClientAsync(
        string employeeId, string clientId, CancellationToken ct = default)
    {
        var url = $"/api/Projects/GetSelectListUsageDataProject?clientId={Uri.EscapeDataString(clientId)}";
        return await GetAsync<List<ProjectForSelect>>(url, ct) ?? [];
    }

    public async Task<ClientRateResponse?> GetClientRateAsync(
        string employeeId, string clientId, DateOnly date, CancellationToken ct = default)
    {
        var url = $"/api/Timesheets/GetClientRate?empID={Uri.EscapeDataString(employeeId)}&clientID={Uri.EscapeDataString(clientId)}&timesheetDateCreated={date:yyyy-MM-dd}";
        return await GetAsync<ClientRateResponse>(url, ct);
    }

    // ───────────────────────── Appointments ─────────────────────────

    public async Task<List<AppointmentItem>> GetAppointmentsAsync(
        string employeeId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var startEpoch = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var endEpoch = new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var url = $"/Crm/Appointments?employeeID={Uri.EscapeDataString(employeeId)}&start={startEpoch}&end={endEpoch}";
        return await GetAsync<List<AppointmentItem>>(url, ct) ?? [];
    }

    // ───────────────────────── Lookups ─────────────────────────

    public async Task<List<TimesheetLocation>> GetLocationsAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<TimesheetLocation>>("/api/Timesheets/GetTimesheetLocation", ct) ?? [];
    }

    public async Task<List<TimesheetCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<TimesheetCategory>>("/api/Timesheets/GetTimesheetCategories", ct) ?? [];
    }

    public async Task<List<TimesheetBillableType>> GetBillableTypesAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<TimesheetBillableType>>("/api/Timesheets/GetTimesheetBillableType", ct) ?? [];
    }

    // ───────────────────────── Suggested Timesheets ─────────────────────────

    public async Task<List<TimesheetItem>> RefreshSuggestedTimesheetsAsync(
        string employeeId, DateOnly date, CancellationToken ct = default)
    {
        var url = $"/api/Timesheets/RefreshSuggestedTimesheets?employeeID={Uri.EscapeDataString(employeeId)}&timesheetDate={date:yyyy-MM-dd}T00:00:00";
        return await GetAsync<List<TimesheetItem>>(url, ct) ?? [];
    }

    public async Task<TimesheetResponse?> AcceptSuggestedTimesheetAsync(
        int suggestedId, string? location, string? notes, decimal? newSellPrice, CancellationToken ct = default)
    {
        var url = $"/api/Timesheets/AcceptSuggestedTimesheet?id={suggestedId}&newSellPrice={newSellPrice}";
        var body = new { location, notes };
        return await PostAsync<TimesheetResponse>(url, body, ct);
    }

    public async Task DeleteSuggestedTimesheetAsync(int suggestedId, CancellationToken ct = default)
    {
        await DeleteAsync($"/api/Timesheets/DeleteSuggestedTimesheet/{suggestedId}", ct);
    }

    // ───────────────────────── Leave ─────────────────────────

    public async Task<LeaveListResponse?> GetLeaveAsync(
        string filter, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var url = $"/api/leave/?pageNumber={pageNumber}&pageSize={pageSize}&leaveFilter={Uri.EscapeDataString(filter)}";
        return await GetAsync<LeaveListResponse>(url, ct);
    }

    public async Task<List<LeaveTypeInfo>> GetLeaveTypesAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<LeaveTypeInfo>>("/api/leave/types", ct) ?? [];
    }

    public async Task CreateLeaveAsync(CreateLeaveRequest request, CancellationToken ct = default)
    {
        await PostAsync<object>("/api/leave/", request, ct);
    }

    public async Task UpdateLeaveAsync(UpdateLeaveRequest request, CancellationToken ct = default)
    {
        await PutAsync("/api/leave/", request, ct);
    }

    public async Task CancelLeaveAsync(string leaveId, CancelLeaveRequest request, CancellationToken ct = default)
    {
        await PutAsync($"/api/leave/{Uri.EscapeDataString(leaveId)}/cancel", request, ct);
    }

    // ───────────────────────── Export ─────────────────────────

    public async Task<byte[]> ExportTimesheetsCsvAsync(
        DateOnly? startDate, DateOnly? endDate, CancellationToken ct = default)
    {
        var url = "/Export/ExportTimesheetsToCSV";
        var query = new List<string>();
        if (startDate.HasValue) query.Add($"startDate={startDate.Value:yyyy-MM-dd}");
        if (endDate.HasValue) query.Add($"endDate={endDate.Value:yyyy-MM-dd}");
        if (query.Count > 0) url += "?" + string.Join("&", query);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ConfigureRequest(request);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    // ───────────────────────── Blogs ─────────────────────────

    public async Task<List<BlogEntry>> GetBlogsAsync(
        bool includeFormerEmployees = false, CancellationToken ct = default)
    {
        var url = $"/api/Timesheets/PopulateBlogs?includePreviousEmployeeBlogs={includeFormerEmployees.ToString().ToLower()}";
        return await GetAsync<List<BlogEntry>>(url, ct) ?? [];
    }

    // ───────────────────────── Summary ─────────────────────────

    public async Task<List<ProjectSummaryItem>> GetProjectsSummaryAsync(
        string employeeId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
    {
        var url = $"/api/Timesheets/GetProjectsSummary?employeeID={Uri.EscapeDataString(employeeId)}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}&currentDate={endDate:yyyy-MM-dd}";
        return await GetAsync<List<ProjectSummaryItem>>(url, ct) ?? [];
    }

    // ───────────────────────── Iterations ─────────────────────────

    public async Task<List<IterationItem>> GetIterationsAsync(
        string projectId, CancellationToken ct = default)
    {
        var url = $"/api/ProjectIteration/GetIterationsForAddTimesheet?projectId={Uri.EscapeDataString(projectId)}";
        return await GetAsync<List<IterationItem>>(url, ct) ?? [];
    }

    // ───────────────────────── Query / Reporting ─────────────────────────

    public async Task<List<TimesheetSummaryEntry>> QueryTimesheetsAsync(
        TimesheetSummaryFilter filter, CancellationToken ct = default)
    {
        return await PostAsync<List<TimesheetSummaryEntry>>(
            "/api/timesheetSummary/GetTableSummarydata", filter, ct) ?? [];
    }

    // ───────────────────────── Accounting: Invoices ─────────────────────────

    public async Task<PagedResponse<InvoiceSearchRow>?> ListInvoicesAsync(
        string? query, int skip, int limit, string field, string dir, bool onlyRecurring, CancellationToken ct = default)
    {
        var url = $"/api/ClientInvoice/rangepaged?query={Uri.EscapeDataString(query ?? string.Empty)}&skip={skip}&limit={limit}&field={Uri.EscapeDataString(field)}&dir={Uri.EscapeDataString(dir)}&onlyRecurring={onlyRecurring.ToString().ToLower()}";
        return await GetAsync<PagedResponse<InvoiceSearchRow>>(url, ct);
    }

    public async Task<InvoiceHeader?> GetInvoiceAsync(int invoiceId, CancellationToken ct = default)
    {
        return await GetAsync<InvoiceHeader>($"/api/v2/ClientInvoice/{invoiceId}", ct);
    }

    public async Task<List<InvoiceLine>> GetInvoiceProductsAsync(int invoiceId, CancellationToken ct = default)
    {
        return await GetAsync<List<InvoiceLine>>($"/api/v2/ClientInvoice/{invoiceId}/products", ct) ?? [];
    }

    public async Task<List<InvoiceTimesheet>> GetInvoiceTimesheetsAsync(int invoiceId, string type, CancellationToken ct = default)
    {
        var endpoint = type.Equals("writeoff", StringComparison.OrdinalIgnoreCase)
            ? "WriteOff"
            : "Allocated";
        var url = $"/api/v2/Timesheets/WithNames/{endpoint}?invoiceID={invoiceId}";
        return await GetAsync<List<InvoiceTimesheet>>(url, ct) ?? [];
    }

    public async Task<List<ReceiptRow>> GetInvoiceReceiptsAsync(int invoiceId, CancellationToken ct = default)
    {
        return await GetAsync<List<ReceiptRow>>($"/api/v2/ClientInvoice/{invoiceId}/receipts", ct) ?? [];
    }

    public async Task<List<InvoiceHeader>> GetInvoicesByClientAsync(string clientId, CancellationToken ct = default)
    {
        var url = $"/api/ClientInvoice/ClientID/{Uri.EscapeDataString(clientId)}";
        return await GetAsync<List<InvoiceHeader>>(url, ct) ?? [];
    }

    public async Task<List<InvoiceHeader>> GetUnpaidInvoicesByClientAsync(string clientId, CancellationToken ct = default)
    {
        var url = $"/api/ClientInvoice/UnpaidByClientID/{Uri.EscapeDataString(clientId)}";
        return await GetAsync<List<InvoiceHeader>>(url, ct) ?? [];
    }

    // ───────────────────────── Accounting: Receipts ─────────────────────────

    public async Task<PagedResponse<ReceiptRow>?> ListPaidReceiptsAsync(
        string? searchText, int skip, int limit, string field, string dir, CancellationToken ct = default)
    {
        var url = $"/api/receipting/PaidReceiptsPaged?searchText={Uri.EscapeDataString(searchText ?? string.Empty)}&skip={skip}&limit={limit}&field={Uri.EscapeDataString(field)}&dir={Uri.EscapeDataString(dir)}";
        return await GetAsync<PagedResponse<ReceiptRow>>(url, ct);
    }

    public async Task<ReceiptDetail?> GetReceiptDetailAsync(int receiptId, CancellationToken ct = default)
    {
        return await GetAsync<ReceiptDetail>($"/api/Receipting/details/{receiptId}", ct);
    }

    public async Task<ClientOutstandingSummary?> GetClientOutstandingAsync(string clientId, CancellationToken ct = default)
    {
        return await GetAsync<ClientOutstandingSummary>(
            $"/api/Receipting/ClientOutstanding/{Uri.EscapeDataString(clientId)}", ct);
    }

    // ───────────────────────── Accounting: Credit Notes ─────────────────────────

    public async Task<List<CreditNoteRow>> GetCreditNotesByClientAsync(string clientId, CancellationToken ct = default)
    {
        return await GetAsync<List<CreditNoteRow>>(
            $"/api/creditnote/by-client/{Uri.EscapeDataString(clientId)}", ct) ?? [];
    }

    // ───────────────────────── Accounting: Products / SKUs ─────────────────────────

    public async Task<List<ProductRow>> ListProductsAsync(bool isExpand, CancellationToken ct = default)
    {
        return await GetAsync<List<ProductRow>>(
            $"/api/Product?isExpand={isExpand.ToString().ToLower()}", ct) ?? [];
    }

    public async Task<ProductRow?> GetProductAsync(string productId, CancellationToken ct = default)
    {
        return await GetAsync<ProductRow>($"/api/Product/{Uri.EscapeDataString(productId)}", ct);
    }

    public async Task<List<ProductSkuRow>> ListAllSkusAsync(bool isPrepaid, CancellationToken ct = default)
    {
        return await GetAsync<List<ProductSkuRow>>(
            $"/api/Product/All?IsPrepaid={isPrepaid.ToString().ToLower()}", ct) ?? [];
    }

    public async Task<List<ProductDiscountRow>> GetProductDiscountsForClientAsync(string clientId, CancellationToken ct = default)
    {
        return await GetAsync<List<ProductDiscountRow>>(
            $"/api/Product/GetDiscountsForClient/{Uri.EscapeDataString(clientId)}", ct) ?? [];
    }

    // ───────────────────────── Accounting: Rates / Outstanding / Unbilled ─────────────────────────

    public async Task<ClientRateTable?> ListClientRatesAsync(
        string clientId, string? empId, bool showExpired,
        int? pageSize, int? skip, string? sortField, string? direction, bool selectAll,
        CancellationToken ct = default)
    {
        var qs = new List<string>
        {
            $"clientId={Uri.EscapeDataString(clientId)}",
            $"showExpiredRates={showExpired.ToString().ToLower()}",
            $"selectAll={selectAll.ToString().ToLower()}"
        };
        if (!string.IsNullOrEmpty(empId)) qs.Add($"empId={Uri.EscapeDataString(empId)}");
        if (pageSize.HasValue) qs.Add($"pageSize={pageSize.Value}");
        if (skip.HasValue) qs.Add($"skip={skip.Value}");
        if (!string.IsNullOrEmpty(sortField)) qs.Add($"sortField={Uri.EscapeDataString(sortField)}");
        if (!string.IsNullOrEmpty(direction)) qs.Add($"direction={Uri.EscapeDataString(direction)}");

        return await GetAsync<ClientRateTable>($"/api/clients/GetClientRates?{string.Join("&", qs)}", ct);
    }

    public async Task<List<ClientOutstandingTimeRow>> GetClientsWithOutstandingTimeAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<ClientOutstandingTimeRow>>("/api/clients/OutstandingTime", ct) ?? [];
    }

    public async Task<List<InvoiceTimesheet>> GetUnallocatedTimesheetsByClientAsync(
        string clientId, int? pageSize, int? skip, string? sortField, string? direction, CancellationToken ct = default)
    {
        // Prefer the v2 named endpoint (returns shaped data the same as Allocated).
        var qs = new List<string> { $"clientId={Uri.EscapeDataString(clientId)}" };
        if (pageSize.HasValue) qs.Add($"pageSize={pageSize.Value}");
        if (skip.HasValue) qs.Add($"skip={skip.Value}");
        if (!string.IsNullOrEmpty(sortField)) qs.Add($"sortField={Uri.EscapeDataString(sortField)}");
        if (!string.IsNullOrEmpty(direction)) qs.Add($"direction={Uri.EscapeDataString(direction)}");

        return await GetAsync<List<InvoiceTimesheet>>(
            $"/api/v2/Timesheets/WithNames/Unallocated?{string.Join("&", qs)}", ct) ?? [];
    }

    // ───────────────────────── Accounting: Recurring ─────────────────────────

    public async Task<PagedResponse<RecurringInvoiceRow>?> ListRecurringInvoicesAsync(
        string? query, string? clientId, bool showOutdated, int skip, int limit, string field, string dir,
        CancellationToken ct = default)
    {
        var qs = new List<string>
        {
            $"query={Uri.EscapeDataString(query ?? string.Empty)}",
            $"showOutdated={showOutdated.ToString().ToLower()}",
            $"skip={skip}",
            $"pageSize={limit}",
            $"field={Uri.EscapeDataString(field)}",
            $"dir={Uri.EscapeDataString(dir)}"
        };
        if (!string.IsNullOrEmpty(clientId)) qs.Add($"clientId={Uri.EscapeDataString(clientId)}");

        // /api/recurring/invoices/ returns the list shape { Total, Data[] }.
        return await GetAsync<PagedResponse<RecurringInvoiceRow>>(
            $"/api/recurring/invoices/?{string.Join("&", qs)}", ct);
    }

    public async Task<RecurringInvoiceDetail?> GetRecurringInvoiceAsync(int invoiceId, CancellationToken ct = default)
    {
        return await GetAsync<RecurringInvoiceDetail>($"/api/recurring/invoices/{invoiceId}", ct);
    }

    // ───────────────────────── Accounting: Prepaid ─────────────────────────

    public async Task<byte[]> GetPrepaidStatusReportPdfAsync(int invoiceId, int templateId, CancellationToken ct = default)
    {
        return await GetBytesAsync(
            $"/Reporting/GetPrepaidStatusReport?invoiceId={invoiceId}&templateId={templateId}", ct);
    }

    // ───────────────────────── HTTP Helpers ─────────────────────────

    private void ConfigureRequest(HttpRequestMessage request)
    {
        var tenant = _tenantProvider.GetCurrentTenant()
            ?? throw new InvalidOperationException(
                "No active tenant configured. Run 'tp login --tenant <id>' first.");

        request.RequestUri = new Uri(new Uri(tenant.ApiUrl.TrimEnd('/')), request.RequestUri!.ToString());

        request.Headers.TryAddWithoutValidation("x-timepro-tenant-id", tenant.TenantId);
        request.Headers.TryAddWithoutValidation("x-timepro-api-key", tenant.ApiKey);
        request.Headers.TryAddWithoutValidation("x-timepro-api-name", tenant.AppName);
    }

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        ConfigureRequest(request);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<T>(ReadJsonOptions, ct);
    }

    private async Task<byte[]> GetBytesAsync(string relativeUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        ConfigureRequest(request);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<T?> PostAsync<T>(string relativeUrl, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
        {
            Content = JsonContent.Create(body, options: WriteJsonOptions)
        };
        ConfigureRequest(request);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        // Some endpoints return empty body on success (e.g. SaveTimesheet)
        var content = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content))
            return default;

        return System.Text.Json.JsonSerializer.Deserialize<T>(content, ReadJsonOptions);
    }

    private async Task PutAsync(string relativeUrl, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, relativeUrl)
        {
            Content = JsonContent.Create(body, options: WriteJsonOptions)
        };
        ConfigureRequest(request);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task DeleteAsync(string relativeUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, relativeUrl);
        ConfigureRequest(request);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new ApiException(
            (int)response.StatusCode,
            $"TimePro API returned {(int)response.StatusCode} {response.ReasonPhrase}",
            body);
    }
}
