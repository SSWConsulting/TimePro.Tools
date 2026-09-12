using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Projects;

/// <summary>
/// The project read behind both <c>project list</c> and the MCP GetProjectsForClient tool, so the
/// dropdown placeholder row can only be filtered in one place.
/// </summary>
public static class ProjectLookup
{
    /// <summary>
    /// The API prepends a dropdown placeholder row with no project ID; it is not a project, and an
    /// agent that picks it writes a timesheet with a blank project.
    /// </summary>
    public static async Task<List<ProjectForSelect>> SelectableAsync(
        ITimeProApiClient api,
        string empId,
        string clientId,
        CancellationToken ct = default)
    {
        var projects = await api.GetProjectsForClientAsync(empId, clientId, ct);
        return projects.Where(p => !string.IsNullOrWhiteSpace(p.Value)).ToList();
    }
}
