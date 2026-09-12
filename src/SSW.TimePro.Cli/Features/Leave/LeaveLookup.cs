using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Leave;

/// <summary>
/// Finds a single leave entry by ID. There is no get-by-id endpoint, so both list filters are paged.
/// </summary>
public sealed class LeaveLookup
{
    private const int PageSize = 100;
    private readonly ITimeProApiClient _api;

    public LeaveLookup(ITimeProApiClient api) => _api = api;

    public async Task<LeaveEntry?> FindAsync(string leaveId, string? employeeId, CancellationToken ct = default)
    {
        foreach (var filter in new[] { LeaveListService.Upcoming, LeaveListService.Past })
        {
            var pageNumber = 1;
            while (true)
            {
                var response = await _api.GetLeaveAsync(filter, pageNumber, PageSize, employeeId, ct);
                var page = response?.Leaves;
                var match = page?.Items.FirstOrDefault(entry =>
                    entry.Id.Equals(leaveId, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    return match;

                if (page is null || pageNumber >= page.TotalPages)
                    break;

                pageNumber++;
            }
        }

        return null;
    }

    /// <summary>
    /// Locates a freshly created leave request, which the create endpoint answers with an empty
    /// body. Returns every entry matching the submitted dates, type and note so an ambiguous
    /// result stays ambiguous to the caller.
    /// </summary>
    public async Task<IReadOnlyList<LeaveEntry>> FindCreatedAsync(
        CreateLeaveRequest request,
        CancellationToken ct = default)
    {
        try
        {
            foreach (var filter in new[] { LeaveListService.Upcoming, LeaveListService.Past })
            {
                var matches = await MatchesAsync(filter, request, ct);
                if (matches.Count > 0)
                    return matches;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A completed write is reported as success, full stop: any read-back failure that
            // surfaced as an error would invite a duplicate submission.
        }

        return [];
    }

    private async Task<List<LeaveEntry>> MatchesAsync(
        string filter,
        CreateLeaveRequest request,
        CancellationToken ct)
    {
        var matches = new List<LeaveEntry>();
        var pageNumber = 1;

        while (true)
        {
            var response = await _api.GetLeaveAsync(filter, pageNumber, PageSize, request.RequestedEmpId, ct);
            var page = response?.Leaves;
            if (page is not null)
                matches.AddRange(page.Items.Where(entry => Matches(entry, request)));

            if (page is null || pageNumber >= page.TotalPages)
                return matches;

            pageNumber++;
        }
    }

    private static bool Matches(LeaveEntry entry, CreateLeaveRequest request) =>
        entry.AllDay == request.AllDay
        && entry.LeaveType?.Id == request.LeaveTypeId
        && string.Equals(entry.Note?.Trim() ?? "", request.Note?.Trim() ?? "", StringComparison.Ordinal)
        && (request.AllDay
            ? SameDay(entry.StartDateLocal ?? entry.StartDate, request.StartDate)
              && SameDay(entry.EndDateLocal ?? entry.EndDate, request.EndDate)
            // Two partial-day requests on one day differ only by their slot, so comparing the
            // dates alone would return the wrong entry's id.
            : SameSlot(entry.StartDateLocal, entry.StartDate, request.StartDate)
              && SameSlot(entry.EndDateLocal, entry.EndDate, request.EndDate));

    /// <summary>
    /// All-day requests are compared on the calendar date only, never as instants: the server
    /// normalises their times and may render the day in its own offset.
    /// </summary>
    private static bool SameDay(string? left, string? right) =>
        DatePart(left) is { } day && day == DatePart(right);

    private static string? DatePart(string? value) =>
        value is null ? null : value.Split('T')[0];

    /// <summary>
    /// One end of a partial-day range, to the minute. The offset-free value is preferred;
    /// otherwise the two are compared as instants, because the server may echo the same moment in
    /// its own zone.
    /// </summary>
    private static bool SameSlot(string? entryLocal, string? entryOffset, string requestValue)
    {
        if (ParseOffset(requestValue) is not { } requested)
            return false;

        if (entryLocal is not null)
            return ToMinute(entryLocal) is { } local && local == Minute(requested);

        return ParseOffset(entryOffset) is { } entry
            && Minute(entry.ToOffset(requested.Offset)) == Minute(requested);
    }

    private static string? ToMinute(string value) =>
        DatePart(value) is { } day && TimePart(value) is { } time ? $"{day}T{time}" : null;

    private static DateTimeOffset? ParseOffset(string? value) =>
        DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string Minute(DateTimeOffset value) =>
        value.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    private static string? TimePart(string? value)
    {
        if (value?.Split('T') is not [_, var time])
            return null;

        var parts = time.Split(':');
        return parts.Length >= 2 ? $"{parts[0]}:{parts[1]}" : null;
    }
}
