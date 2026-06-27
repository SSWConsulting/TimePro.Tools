using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Accounting;

[Description("Show accounting diagnostic interview questions and command choices")]
public class GuideCommand : Command<GuideCommand.Settings>
{
    private readonly IAccountingDiagnosticsService _diagnostics;

    public class Settings : CommandSettings
    {
        [CommandOption("--use-case <TEXT>")]
        [Description("Optional short user goal, e.g. 'reconcile March receipts to Xero'")]
        public string? UseCase { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    public GuideCommand(IAccountingDiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var guide = _diagnostics.GetUseCaseGuide(settings.UseCase);

        OutputHelper.Render(guide, settings.Json, g =>
        {
            AnsiConsole.MarkupLine("[bold]Ask first[/]");
            foreach (var question in g.AskUser)
                AnsiConsole.MarkupLine($"- {Markup.Escape(question)}");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Useful CLI commands[/]");
            foreach (var command in g.RecommendedCommands)
                AnsiConsole.MarkupLine($"- [cyan]{Markup.Escape(command)}[/]");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(Markup.Escape(g.Note));
        });

        return 0;
    }
}
