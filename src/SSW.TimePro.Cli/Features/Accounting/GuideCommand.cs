using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Accounting;

[Description("Show accounting diagnostic interview questions and command choices")]
public class GuideCommand : Command<GuideCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--use-case <TEXT>")]
        [Description("Optional short user goal, e.g. 'reconcile March receipts to Xero'")]
        public string? UseCase { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var guide = AccountingGuide.For(settings.UseCase);

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
            AnsiConsole.MarkupLine(Markup.Escape(g.Note));
        });

        return 0;
    }
}

public sealed record AccountingGuide(
    string? UseCase,
    IReadOnlyList<string> AskUser,
    IReadOnlyList<string> RecommendedCommands,
    IReadOnlyList<string> RecommendedMcpTools,
    IReadOnlyList<string> RecommendedSkills,
    string Note)
{
    public static AccountingGuide For(string? useCase = null) =>
        new(
            UseCase: useCase,
            AskUser:
            [
                "What are you trying to verify: invoices, receipts, aged debtors, prepaid drawdown, unbilled work, credit notes, tax anomalies, or external parity?",
                "What external evidence should TimePro be compared to: Excel, CSV, Xero MCP, bank-feed MCP, or another source?",
                "Which date field should drive the comparison: invoice date, date created, payment date, or service period?",
                "Should GST be included, excluded, or reported in both ex-GST and inc-GST forms?",
                "Should credit notes and write-offs be netted into the total or reported separately?",
                "What tolerance should be used for amount mismatches?"
            ],
            RecommendedCommands:
            [
                "tp invoice get <invoiceId> --json",
                "tp invoice lines <invoiceId> --json",
                "tp invoice timesheets <invoiceId> --json",
                "tp invoice receipts <invoiceId> --json",
                "tp receipt list --search <clientOrReference> --json",
                "tp creditnote list --client <clientId> --json",
                "tp rate list --client <clientId> --show-expired --json",
                "tp prepaid summary <invoiceId> --json",
                "tp query --from <yyyy-MM-dd> --to <yyyy-MM-dd> --json"
            ],
            RecommendedMcpTools:
            [
                "ListInvoices",
                "GetInvoice",
                "GetInvoiceLines",
                "GetInvoiceTimesheets",
                "GetInvoiceReceipts",
                "ListPaidReceipts",
                "ListCreditNotes",
                "ListClientRates",
                "GetPrepaidStatus",
                "QueryTimesheets"
            ],
            RecommendedSkills:
            [
                "timepro-accounting-cli",
                "timepro-accounting-tax-mismatch",
                "timepro-accounting-invoice-diagnostics",
                "timepro-accounting-client-diagnostics"
            ],
            Note: "The guide is intentionally lightweight and read-only. Use primitive CLI/MCP reads for data, then let the specialized accounting skills compose the evidence pack so local skill updates do not require code changes.");
}
