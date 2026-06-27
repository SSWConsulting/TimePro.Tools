using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Guides;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Accounting;

[Description("Show accounting diagnostic interview questions and command choices")]
public class GuideCommand : AsyncCommand<GuideCommand.Settings>
{
    private const string Domain = "accounting";
    private readonly IConfigService _config;

    public GuideCommand(IConfigService config)
    {
        _config = config;
    }

    public class Settings : CommandSettings
    {
        [CommandOption("--use-case <TEXT>")]
        [Description("Optional short user goal, e.g. 'reconcile March receipts to Xero'")]
        public string? UseCase { get; set; }

        [CommandOption("--refresh|--force-refresh")]
        [Description("Force refresh the GitHub guide cache before reading")]
        public bool Refresh { get; set; }

        [CommandOption("--json")]
        [Description("Output as JSON")]
        public bool Json { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var guideConfig = _config.LoadGlobalConfig().Guides ?? new GuideConfig();
        var cacheMinutes = Math.Max(0, guideConfig.CacheMinutes);
        var guides = await GuideCatalog.LoadFromGitHubCacheAsync(
            Domain,
            new GuideCatalogOptions(
                _config.ConfigDirectory,
                TimeSpan.FromMinutes(cacheMinutes),
                settings.Refresh,
                guideConfig.RepositoryUrl,
                guideConfig.Branch,
                null),
            cancellationToken);
        var guide = AccountingGuide.For(settings.UseCase, guides);

        OutputHelper.Render(guide, settings.Json, g =>
        {
            AnsiConsole.MarkupLine("[bold]Ask first[/]");
            foreach (var question in g.AskUser)
                AnsiConsole.MarkupLine($"- {Markup.Escape(question)}");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Matching guides[/]");
            if (g.MatchingGuides.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]- No matching guide topics found yet[/]");
            }
            else
            {
                foreach (var guide in g.MatchingGuides)
                    AnsiConsole.MarkupLine($"- [cyan]{Markup.Escape(guide.Title)}[/] ({Markup.Escape(guide.MatchType)}): {Markup.Escape(guide.Description)}");
            }

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
    IReadOnlyList<RankedGuide> MatchingGuides,
    string Note)
{
    private const string Domain = "accounting";

    private static readonly IReadOnlyList<string> CommonCommands =
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
    ];

    private static readonly IReadOnlyList<string> CommonSkills =
    [
        "timepro-accounting-cli"
    ];

    private static readonly IReadOnlyList<string> CommonMcpTools =
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
    ];

    public static AccountingGuide For(
        string? useCase = null,
        IReadOnlyList<GuideDocument>? guides = null)
    {
        var matchingGuides = GuideRanking.Rank(useCase, guides ?? GuideCatalog.Load(Domain));
        var hasFilteredMatches = !string.IsNullOrWhiteSpace(useCase) && matchingGuides.Count > 0;

        return new(
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
            RecommendedCommands: hasFilteredMatches
                ? Unique(matchingGuides.SelectMany(guide => guide.Commands))
                : CommonCommands,
            RecommendedMcpTools: hasFilteredMatches
                ? SpecificOrFallback(matchingGuides.SelectMany(guide => guide.McpTools), CommonMcpTools)
                : CommonMcpTools,
            RecommendedSkills: hasFilteredMatches
                ? SpecificOrFallback(matchingGuides.SelectMany(guide => guide.Skills), CommonSkills)
                : CommonSkills,
            MatchingGuides: matchingGuides,
            Note: "The guide is intentionally lightweight and read-only. Specific recipes live in guides/accounting so local guide updates do not require app code changes.");
    }

    private static IReadOnlyList<string> Unique(IEnumerable<string> values) =>
        values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static IReadOnlyList<string> SpecificOrFallback(IEnumerable<string> values, IReadOnlyList<string> fallback)
    {
        var specific = Unique(values);
        return specific.Count == 0 ? fallback : specific;
    }
}
