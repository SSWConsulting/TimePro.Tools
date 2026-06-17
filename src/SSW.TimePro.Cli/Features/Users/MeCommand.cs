using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Users;

[Description("Show current user info")]
public class MeCommand : AsyncCommand<MeCommand.Settings>
{
    private readonly ITimeProApiClient _api;

    public class Settings : CommandSettings
    {
        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public MeCommand(ITimeProApiClient api)
    {
        _api = api;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _api.GetCurrentUserAsync(CancellationToken.None);
            if (user is null)
            {
                OutputHelper.WriteError("Could not retrieve user info.");
                return 1;
            }

            OutputHelper.Render(user, settings.Json, u =>
            {
                var table = new Table().NoBorder().HideHeaders().AddColumn("Key").AddColumn("Value");
                table.AddRow("[bold]Employee ID[/]", Markup.Escape(u.EmployeeId ?? "unknown"));
                table.AddRow("[bold]Name[/]", Markup.Escape($"{u.FirstName} {u.LastName}".Trim()));
                table.AddRow("[bold]Email[/]", Markup.Escape(u.Email ?? "unknown"));
                AnsiConsole.Write(table);
            });

            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }
}
