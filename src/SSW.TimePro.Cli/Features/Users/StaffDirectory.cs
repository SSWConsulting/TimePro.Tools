using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Users;

/// <summary>
/// The active employees expected to log timesheets — the roster a "who is missing timesheets"
/// check runs against. Shared by <c>tp user list --staff</c> and the <c>ListStaff</c> MCP tool.
/// </summary>
public static class StaffDirectory
{
    /// <summary>
    /// Categories that never log timesheets: office/admin (<c>O</c>, <c>OA-E</c>), external
    /// contractors (<c>EXCON</c>) and work experience (<c>WE</c>). Uncategorised accounts are
    /// excluded too; they are service, bot and admin logins.
    /// </summary>
    private static readonly HashSet<string> ExcludedCategories =
        new(["O", "OA-E", "EXCON", "WE"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Retired accounts are renamed with a "zz" prefix rather than given an end date.</summary>
    private const string RetiredPrefix = "zz";

    private const int MaxConcurrentDetailReads = 8;

    /// <summary>
    /// Lists active staff. The dropdown carries no category, so each remaining employee's detail
    /// is read (bounded concurrency); "zz" accounts are dropped first to skip their reads.
    /// </summary>
    public static async Task<List<EmployeeSummary>> ListAsync(ITimeProApiClient api, CancellationToken ct)
    {
        var candidates = (await api.ListUsersAsync(includeFormerEmployees: false, ct))
            .Where(u => !IsRetired(u.Name))
            .ToList();

        var categories = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentDetailReads, CancellationToken = ct },
            async (user, token) =>
            {
                var detail = await api.GetUserAsync(user.EmpId!, token);
                lock (categories)
                    categories[user.EmpId!] = detail?.CategoryId;
            });

        return candidates
            .Where(u => IsStaffCategory(categories.GetValueOrDefault(u.EmpId!)))
            .ToList();
    }

    private static bool IsRetired(string? name) =>
        name?.TrimStart().StartsWith(RetiredPrefix, StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsStaffCategory(string? categoryId) =>
        !string.IsNullOrWhiteSpace(categoryId) && !ExcludedCategories.Contains(categoryId.Trim());
}
