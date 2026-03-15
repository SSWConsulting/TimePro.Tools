using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Bookings;

[Description("List CRM bookings/appointments")]
public class ListCommand : AsyncCommand<ListCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--date <DATE>")]
        [Description("Date (yyyy-MM-dd). Defaults to today")]
        public string? Date { get; set; }

        [CommandOption("--week [OFFSET]")]
        [Description("Show week. Optional offset: 0=this week, -1=last week")]
        [DefaultValue(null)]
        public FlagValue<int>? Week { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public ListCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        try
        {
            DateOnly start, end;

            if (settings.Week is not null && settings.Week.IsSet)
            {
                var offset = settings.Week.Value;
                var today = DateOnly.FromDateTime(DateTime.Today);
                var monday = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday + (offset * 7));
                if (today.DayOfWeek == DayOfWeek.Sunday)
                    monday = monday.AddDays(-7);
                start = monday;
                end = monday.AddDays(4);
            }
            else if (settings.Date is not null)
            {
                start = DateOnly.ParseExact(settings.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                end = start;
            }
            else
            {
                start = DateOnly.FromDateTime(DateTime.Today);
                end = start;
            }

            var appointments = await _api.GetAppointmentsAsync(
                tenant.EmployeeId, start, end.AddDays(1), CancellationToken.None);

            OutputHelper.Render(appointments, settings.Json, list =>
            {
                if (list.Count == 0)
                {
                    OutputHelper.WriteInfo("No CRM bookings found");
                    return;
                }

                var table = new Table()
                    .AddColumn("Title")
                    .AddColumn("Start")
                    .AddColumn("End")
                    .AddColumn("Client")
                    .AddColumn("Project")
                    .AddColumn("All Day");

                foreach (var a in list.OrderBy(a => a.Start))
                {
                    table.AddRow(
                        Markup.Escape(a.Title ?? ""),
                        Markup.Escape(a.Start ?? ""),
                        Markup.Escape(a.End ?? ""),
                        Markup.Escape(a.ClientId ?? "-"),
                        Markup.Escape(a.ProjectId ?? "-"),
                        a.AllDay ? "Yes" : "No");
                }

                AnsiConsole.Write(table);
            });

            return 0;
        }
        catch (FormatException)
        {
            OutputHelper.WriteError("Invalid date format. Use yyyy-MM-dd.");
            return 1;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }
}
