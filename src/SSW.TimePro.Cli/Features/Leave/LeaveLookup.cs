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
}
