using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Guides;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Developer;

[Description("Show developer diagnostic guide questions and command choices")]
public class GuideCommand : AsyncCommand<GuideCommand.Settings>
{
    private const string Domain = "dev";
    private readonly IConfigService _config;

    public GuideCommand(IConfigService config)
    {
        _config = config;
    }

    public class Settings : CommandSettings
    {
        [CommandOption("--use-case <TEXT>")]
        [Description("Optional short user goal, e.g. 'suggested timesheets missing in staging'")]
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
        var guide = DeveloperGuide.For(settings.UseCase, guides);

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
    IReadOnlyList<RankedGuide> MatchingGuides,
    IReadOnlyList<string> TelemetryFollowUp,
    string Note)
{
    private const string Domain = "dev";

    private static readonly IReadOnlyList<string> CommonCommands =
    [
        "tp tenant info --tenant <name> --env <env> --json",
        "tp user get <empId> --tenant <name> --env <env> --json",
        "tp ts get <date> --tenant <name> --env <env> --emp-id <empId> --json",
        "tp ts suggest <date> --tenant <name> --env <env> --json",
        "tp bk list --date <date> --tenant <name> --env <env> --json",
        "tp invoice get <invoiceId> --tenant <name> --env <env> --json",
        "tp invoice timesheets <invoiceId> --tenant <name> --env <env> --json",
        "tp rate list --client <clientId> --tenant <name> --env <env> --show-expired --json"
    ];

    private static readonly IReadOnlyList<string> CommonSkills =
    [
        "timepro-dev-diagnostics",
        "timepro-dev-timesheet-diagnostics",
        "timepro-dev-finance-diagnostics",
        "timepro-env-compare"
    ];

    public static DeveloperGuide For(
        string? useCase = null,
        IReadOnlyList<GuideDocument>? guides = null)
    {
        var matchingGuides = GuideRanking.Rank(useCase, guides ?? GuideCatalog.Load(Domain));
        var hasFilteredMatches = !string.IsNullOrWhiteSpace(useCase) && matchingGuides.Count > 0;

        return new(
            UseCase: useCase,
            AskUser:
            [
                "What is the exact symptom: suggested timesheets, CRM bookings, saved timesheets, invoices, credit notes, rates, prepaid drawdown, tax, or external sync?",
                "Which tenant/environment should be used, and should the command be process-local with --tenant/--env?",
                "What is the smallest anchor: EmpID, date, client ID, invoice ID, receipt ID, credit note ID, or external reference?",
                "Is this local/staging, or production read-only diagnostics?",
                "What successful behavior should be observed after a fix?"
            ],
            RecommendedCommands: hasFilteredMatches
                ? Unique(matchingGuides.SelectMany(guide => guide.Commands))
                : CommonCommands,
            RecommendedSkills: hasFilteredMatches
                ? SpecificOrFallback(matchingGuides.SelectMany(guide => guide.Skills), CommonSkills)
                : CommonSkills,
            MatchingGuides: matchingGuides,
            TelemetryFollowUp:
            [
                "Use App Insights only after CLI evidence identifies the relevant EmpID/date/client/invoice/reference.",
                "Start with requests/exceptions/traces filtered by the concrete ID and recent time window.",
                "Ask before any non-read-only production action, including resyncs, writes, or repair jobs."
            ],
            Note: "The guide is intentionally lightweight. Specific recipes live in guides/dev so local guide updates do not require app code changes.");
    }

    private static IReadOnlyList<string> Unique(IEnumerable<string> values) =>
        values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static IReadOnlyList<string> SpecificOrFallback(IEnumerable<string> values, IReadOnlyList<string> fallback)
    {
        var specific = Unique(values);
        return specific.Count == 0 ? fallback : specific;
    }
}
