using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Users;

[Description("List users and match names or emails to EmpIDs")]
public class ListCommand : AsyncCommand<ListCommand.Settings>
{
    private readonly ITimeProApiClient _api;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[QUERY]")]
        [Description("Text to match against EmpID, name, or email")]
        public string? Query { get; set; }

        [CommandOption("--emp-id <EMP_ID>")]
        [Description("Filter by EmpID")]
        public string? EmpId { get; set; }

        [CommandOption("--employee-id <EMPLOYEE_ID>")]
        [Description("Alias for --emp-id")]
        public string? EmployeeId { get; set; }

        [CommandOption("--name <TEXT>")]
        [Description("Filter by name")]
        public string? Name { get; set; }

        [CommandOption("--email <TEXT>")]
        [Description("Filter by email")]
        public string? Email { get; set; }

        [CommandOption("--all")]
        [Description("Include former employees")]
        public bool All { get; set; }

        [CommandOption("--staff")]
        [Description("Only active staff expected to log timesheets (excludes admin, service, work experience, contractor and retired accounts)")]
        public bool Staff { get; set; }

        [CommandOption("--limit <N>")]
        [Description("Maximum rows to show; 0 means no limit (default: 50)")]
        public int Limit { get; set; } = 50;

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public ListCommand(ITimeProApiClient api)
    {
        _api = api;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Limit < 0)
        {
            OutputHelper.WriteError("--limit must be 0 or greater.");
            return 1;
        }

        if (settings.Staff && settings.All)
        {
            OutputHelper.WriteError("--staff lists active employees only and cannot be combined with --all.");
            return 1;
        }

        try
        {
            var users = settings.Staff
                ? await StaffDirectory.ListAsync(_api, cancellationToken)
                : await _api.ListUsersAsync(settings.All, cancellationToken);
            var filtered = ApplyFilters(users, settings).ToList();
            var visible = settings.Limit == 0
                ? filtered
                : filtered.Take(settings.Limit).ToList();

            OutputHelper.Render(visible, settings.Json, list =>
            {
                if (list.Count == 0)
                {
                    OutputHelper.WriteInfo("No users found");
                    return;
                }

                var table = new Table()
                    .AddColumn("EmpID")
                    .AddColumn("Name")
                    .AddColumn("Email");

                foreach (var user in list)
                {
                    table.AddRow(
                        Markup.Escape(user.EmpId ?? ""),
                        Markup.Escape(user.Name ?? ""),
                        Markup.Escape(user.Email ?? ""));
                }

                AnsiConsole.Write(table);

                if (settings.Limit > 0 && filtered.Count > settings.Limit)
                    AnsiConsole.MarkupLine($"[dim]Showing {settings.Limit} of {filtered.Count}. Use --limit 0 to see all.[/]");
            });

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteApiError(ex, settings.Json);
            return 1;
        }
    }

    private static IEnumerable<EmployeeSummary> ApplyFilters(IEnumerable<EmployeeSummary> users, Settings settings)
    {
        var empId = FirstNonEmpty(settings.EmpId, settings.EmployeeId);

        foreach (var user in users)
        {
            if (!MatchesAnyField(user, settings.Query))
                continue;
            if (!Contains(user.EmpId, empId))
                continue;
            if (!Contains(user.Name, settings.Name))
                continue;
            if (!Contains(user.Email, settings.Email))
                continue;

            yield return user;
        }
    }

    private static bool MatchesAnyField(EmployeeSummary user, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return Contains(user.EmpId, query)
               || Contains(user.Name, query)
               || Contains(user.Email, query);
    }

    private static bool Contains(string? value, string? query)
    {
        return string.IsNullOrWhiteSpace(query)
               || (!string.IsNullOrWhiteSpace(value)
                   && value.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }
}
