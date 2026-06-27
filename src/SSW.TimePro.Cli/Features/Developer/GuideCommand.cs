using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Developer;

[Description("Show developer diagnostic guide questions and command choices")]
public class GuideCommand : Command<GuideCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--use-case <TEXT>")]
        [Description("Optional short user goal, e.g. 'suggested timesheets missing in staging'")]
        public string? UseCase { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var guide = DeveloperGuide.For(settings.UseCase);

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
            AnsiConsole.MarkupLine("[bold]Useful skills[/]");
            foreach (var skill in g.RecommendedSkills)
                AnsiConsole.MarkupLine($"- {Markup.Escape(skill)}");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Telemetry follow-up[/]");
            foreach (var query in g.TelemetryFollowUp)
                AnsiConsole.MarkupLine($"- {Markup.Escape(query)}");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(Markup.Escape(g.Note));
        });

        return 0;
    }
}

public sealed record DeveloperGuide(
    string? UseCase,
    IReadOnlyList<string> AskUser,
    IReadOnlyList<string> RecommendedCommands,
    IReadOnlyList<string> RecommendedSkills,
    IReadOnlyList<string> TelemetryFollowUp,
    string Note)
{
    public static DeveloperGuide For(string? useCase = null) =>
        new(
            UseCase: useCase,
            AskUser:
            [
                "What is the exact symptom: suggested timesheets, CRM bookings, saved timesheets, invoices, credit notes, rates, prepaid drawdown, tax, or external sync?",
                "Which tenant/environment should be used, and should the command be process-local with --tenant/--env?",
                "What is the smallest anchor: EmpID, date, client ID, invoice ID, receipt ID, credit note ID, or external reference?",
                "Is this local/staging, or production read-only diagnostics?",
                "What successful behavior should be observed after a fix?"
            ],
            RecommendedCommands:
            [
                "tp tenant info --tenant <name> --env <env> --json",
                "tp user get <empId> --tenant <name> --env <env> --json",
                "tp ts get <date> --tenant <name> --env <env> --emp-id <empId> --json",
                "tp ts suggest <date> --tenant <name> --env <env> --json",
                "tp bk list --date <date> --tenant <name> --env <env> --json",
                "tp invoice get <invoiceId> --tenant <name> --env <env> --json",
                "tp invoice timesheets <invoiceId> --tenant <name> --env <env> --json",
                "tp rate list --client <clientId> --tenant <name> --env <env> --show-expired --json"
            ],
            RecommendedSkills:
            [
                "timepro-dev-diagnostics",
                "timepro-dev-timesheet-diagnostics",
                "timepro-dev-finance-diagnostics",
                "timepro-env-compare",
                "timepro-accounting-tax-mismatch",
                "timepro-accounting-invoice-diagnostics",
                "timepro-accounting-client-diagnostics"
            ],
            TelemetryFollowUp:
            [
                "Use App Insights only after CLI evidence identifies the relevant EmpID/date/client/invoice/reference.",
                "Start with requests/exceptions/traces filtered by the concrete ID and recent time window.",
                "Ask before any non-read-only production action, including resyncs, writes, or repair jobs."
            ],
            Note: "The guide is intentionally lightweight. Keep repro and fix verification in specialized skills and primitive CLI evidence so guide updates do not require code-heavy diagnostics.");
}
