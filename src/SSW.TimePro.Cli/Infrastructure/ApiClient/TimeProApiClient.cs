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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
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

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    private async Task<T?> PostAsync<T>(string relativeUrl, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        ConfigureRequest(request);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
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
