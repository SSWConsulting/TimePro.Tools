using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Iterations;

[Description("List iterations/sprints for a project")]
public class ListCommand : AsyncCommand<ListCommand.Settings>
{
    private readonly ITimeProApiClient _api;

    public class Settings : CommandSettings
    {
        [CommandOption("--project <PROJECT_ID>")]
        [Description("Project ID")]
        public string ProjectId { get; set; } = string.Empty;

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public ListCommand(ITimeProApiClient api) => _api = api;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.ProjectId))
        {
            OutputHelper.WriteError("--project is required");
            return 1;
        }

        try
        {
            var iterations = await _api.GetIterationsAsync(settings.ProjectId, CancellationToken.None);

            OutputHelper.Render(iterations, settings.Json, list =>
            {
                if (list.Count == 0)
                {
                    OutputHelper.WriteInfo($"No iterations for project '{settings.ProjectId}'");
                    return;
                }

                var table = new Table().AddColumn("ID").AddColumn("Name");
                foreach (var it in list)
                    table.AddRow(
                        it.IterationId.ToString(),
                        Markup.Escape(it.IterationName ?? ""));
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
