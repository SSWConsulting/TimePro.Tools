using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Leave;

public sealed class LeaveFilterValidationException(string message) : Exception(message);

/// <summary>
/// Reads leave lists. The API's filter enum only has UPCOMING and PAST, so ALL is merged client-side.
/// </summary>
public sealed class LeaveListService
{
    public const string Upcoming = "UPCOMING";
    public const string Past = "PAST";
    public const string All = "ALL";

    public static readonly string[] ValidFilters = [Upcoming, Past, All];

    private readonly ITimeProApiClient _api;

    public LeaveListService(ITimeProApiClient api) => _api = api;

    public static bool TryNormalizeFilter(string? filter, out string normalized, out string? error)
    {
        normalized = (filter ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0)
            normalized = Upcoming;

        if (ValidFilters.Contains(normalized))
        {
            error = null;
            return true;
        }

        error = $"Unknown leave filter '{filter}'. Valid values: {string.Join(", ", ValidFilters)}.";
        return false;
    }

    public async Task<LeaveListResponse> ListAsync(
        string? filter,
        int limit,
        string? employeeId,
        CancellationToken ct = default)
    {
        if (!TryNormalizeFilter(filter, out var normalized, out var error))
            throw new LeaveFilterValidationException(error!);

        if (normalized != All)
            return await _api.GetLeaveAsync(normalized, 1, limit, employeeId, ct) ?? new LeaveListResponse();

        var upcoming = _api.GetLeaveAsync(Upcoming, 1, limit, employeeId, ct);
        var past = _api.GetLeaveAsync(Past, 1, limit, employeeId, ct);
        await Task.WhenAll(upcoming, past);
        return Merge(upcoming.Result, past.Result, limit);
    }

    public static LeaveListResponse Merge(LeaveListResponse? upcoming, LeaveListResponse? past, int limit)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<LeaveEntry>();

        foreach (var entry in Items(upcoming).Concat(Items(past)))
        {
            if (items.Count >= limit)
                break;
            if (seen.Add(entry.Id))
                items.Add(entry);
        }

        var totalItems = (upcoming?.Leaves?.TotalItems ?? 0) + (past?.Leaves?.TotalItems ?? 0);

        return new LeaveListResponse
        {
            Leaves = new PaginatedList<LeaveEntry>
            {
                PageNumber = 1,
                PageSize = limit,
                TotalItems = totalItems,
                TotalPages = limit > 0 ? (int)Math.Ceiling(totalItems / (double)limit) : 0,
                Items = items
            },
            // The server counts cancelled entries before applying the filter, so both halves report
            // the same number and adding them would double it.
            CancelledCount = Math.Max(upcoming?.CancelledCount ?? 0, past?.CancelledCount ?? 0)
        };
    }

    private static IEnumerable<LeaveEntry> Items(LeaveListResponse? response) =>
        response?.Leaves?.Items ?? [];
}
