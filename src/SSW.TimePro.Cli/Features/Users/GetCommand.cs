using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Users;

[Description("Show user details by EmpID")]
public class GetCommand : AsyncCommand<GetCommand.Settings>
{
    private readonly ITimeProApiClient _api;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<EMP_ID>")]
        [Description("EmpID to retrieve")]
        public string EmpId { get; set; } = string.Empty;

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public GetCommand(ITimeProApiClient api)
    {
        _api = api;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.EmpId))
        {
            OutputHelper.WriteError("EmpID is required.");
            return 1;
        }

        try
        {
            var user = await _api.GetUserAsync(settings.EmpId.Trim(), cancellationToken);
            if (user is null)
            {
                OutputHelper.WriteError("User not found.");
                return 1;
            }

            OutputHelper.Render(user, settings.Json, u =>
            {
                var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
                table.AddRow("[bold]EmpID[/]", Markup.Escape(u.EmpId ?? "unknown"));
                table.AddRow("[bold]Name[/]", Markup.Escape(u.Name ?? "unknown"));
                table.AddRow("[bold]Email[/]", Markup.Escape(u.Email ?? "unknown"));
                table.AddRow("[bold]Position[/]", Markup.Escape(u.Position ?? "unknown"));
                table.AddRow("[bold]Category[/]", Markup.Escape(u.CategoryId ?? "unknown"));
                table.AddRow("[bold]Site[/]", Markup.Escape(u.SiteId ?? "unknown"));
                table.AddRow("[bold]Timezone[/]", Markup.Escape(u.TimezoneId ?? "unknown"));
                table.AddRow("[bold]Enabled[/]", FormatBool(u.IsEnabled));
                table.AddRow("[bold]Former employee[/]", FormatBool(u.DateEnd is not null));
                if (u.DateEnd is not null)
                    table.AddRow("[bold]End date[/]", Markup.Escape($"{u.DateEnd:yyyy-MM-dd}"));
                table.AddRow("[bold]Work hours[/]", Markup.Escape(FormatWorkHours(u.StartTime, u.EndTime)));
                table.AddRow("[bold]Lunch[/]", Markup.Escape(FormatWorkHours(u.LunchBreakStart, u.LunchBreakEnd)));
                table.AddRow("[bold]Time-less minutes[/]", Markup.Escape(u.TimeLessMinutes?.ToString() ?? "unknown"));
                AnsiConsole.Write(table);
            });

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteApiError(ex, settings.Json);
            return 1;
        }
    }

    private static string FormatBool(bool? value)
    {
        return value switch
        {
            true => "yes",
            false => "no",
            null => "unknown"
        };
    }

    private static string FormatWorkHours(string? start, string? end)
    {
        if (string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end))
            return "unknown";

        return $"{start ?? "?"} - {end ?? "?"}";
    }
}
