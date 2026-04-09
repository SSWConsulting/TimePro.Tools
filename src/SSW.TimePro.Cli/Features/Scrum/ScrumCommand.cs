using System.ComponentModel;
using System.Globalization;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Scrum;

/// <summary>
/// <c>tp scrum</c> — generates an SSW-format daily scrum email from
/// timesheets, bookings, repo mappings and GitHub activity. Renders to
/// stdout by default; optional interactive mode lets you press a key to
/// copy the body (rich-text on macOS, plain elsewhere).
/// </summary>
[Description("Generate a daily scrum email from timesheets + GitHub activity")]
public class ScrumCommand : AsyncCommand<ScrumCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--date <DATE>")]
        [Description("Reference date for 'today' (yyyy-MM-dd). Defaults to today")]
        public string? Date { get; set; }

        [CommandOption("--project <PROJECT>")]
        [Description("Only include this project ID in the scrum")]
        public string? ProjectId { get; set; }

        [CommandOption("--internal")]
        [Description("Force internal daily scrum format (even if client bookings exist)")]
        public bool ForceInternal { get; set; }

        [CommandOption("--external")]
        [Description("Force client-facing format (skip the internal block)")]
        public bool ForceExternal { get; set; }

        [CommandOption("-i|--interactive")]
        [Description("Show an interactive prompt ([c] copy body, [C] with internal, [q] quit)")]
        public bool Interactive { get; set; }

        [CommandOption("--copy")]
        [Description("Render and copy the body to the clipboard, then exit")]
        public bool CopyAndExit { get; set; }

        [CommandOption("--html")]
        [Description("Emit the HTML body to stdout instead of the styled terminal view")]
        public bool Html { get; set; }

        [CommandOption("--json")]
        [Description("Emit the structured scrum model as JSON")]
        public bool Json { get; set; }

        [CommandOption("--set-trello-url <URL>")]
        [Description("Persist a Trello board URL for the internal scrum block and exit")]
        public string? SetTrelloUrl { get; set; }
    }

    public ScrumCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // One-off config shortcut
        if (!string.IsNullOrEmpty(settings.SetTrelloUrl))
        {
            var g = _config.LoadGlobalConfig();
            g.Scrum.TrelloUrl = settings.SetTrelloUrl;
            _config.SaveGlobalConfig(g);
            OutputHelper.WriteSuccess($"Scrum Trello URL set to {settings.SetTrelloUrl}");
            return 0;
        }

        var tenant = _config.LoadActiveTenantConfig();
        if (tenant?.EmployeeId is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        DateOnly today;
        try
        {
            today = settings.Date is not null
                ? DateOnly.ParseExact(settings.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : DateOnly.FromDateTime(DateTime.Today);
        }
        catch (FormatException)
        {
            OutputHelper.WriteError("Invalid --date. Use yyyy-MM-dd.");
            return 1;
        }

        bool? forceInternal = settings.ForceInternal ? true : settings.ForceExternal ? false : null;

        var gatherer = new ScrumDataGatherer(_api, _config, new GhCli());
        ScrumModel model;
        try
        {
            model = await gatherer.BuildAsync(tenant.EmployeeId, today, settings.ProjectId, forceInternal, CancellationToken.None);
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }

        var globalConfig = _config.LoadGlobalConfig();
        var renderer = new ScrumRenderer(globalConfig.Scrum);

        // --- Output modes --------------------------------------------------
        if (settings.Json)
        {
            OutputHelper.WriteJson(model);
            return 0;
        }
        if (settings.Html)
        {
            Console.WriteLine(renderer.RenderHtml(model));
            return 0;
        }

        // Write raw ANSI directly to stdout — the renderer emits OSC 8
        // hyperlinks which would collide with Spectre's markup parser.
        Console.Out.Write(renderer.RenderTerminal(model));

        // --- Copy shortcut -------------------------------------------------
        if (settings.CopyAndExit)
        {
            CopyBody(renderer, model, full: model.IsInternal);
            return 0;
        }

        // --- Interactive loop ---------------------------------------------
        if (settings.Interactive)
        {
            return RunInteractive(renderer, model);
        }

        // Non-interactive help hint
        AnsiConsole.MarkupLine("[dim]Tip: run with [/][bold]-i[/][dim] for interactive copy, or [/][bold]--copy[/][dim] to copy and exit.[/]");
        return 0;
    }

    private int RunInteractive(ScrumRenderer renderer, ScrumModel model)
    {
        AnsiConsole.MarkupLine("[dim]──────────────────────────────────────────────────────────[/]");
        AnsiConsole.MarkupLine("[dim][[c]] copy body   [[C]] copy with internal block   [[q]] quit[/]");

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Q)
                return 0;
            if (key.KeyChar == 'c')
            {
                CopyBody(renderer, model, full: false);
                continue;
            }
            if (key.KeyChar == 'C')
            {
                CopyBody(renderer, model, full: true);
                continue;
            }
        }
    }

    private void CopyBody(ScrumRenderer renderer, ScrumModel model, bool full)
    {
        // Re-render with forced internal if the user pressed 'C'.
        var modelToCopy = model;
        if (full && !model.IsInternal)
        {
            modelToCopy = new ScrumModel
            {
                TodayDate = model.TodayDate,
                YesterdayDate = model.YesterdayDate,
                IsInternal = true,
                PrimaryClientName = model.PrimaryClientName,
                Yesterday = model.Yesterday,
                Today = model.Today,
                Internal = model.Internal ?? new InternalBlock { JoinedScrumMeeting = true }
            };
        }

        var html = renderer.RenderHtml(modelToCopy);
        var plain = renderer.RenderPlain(modelToCopy);
        var clip = new ClipboardService();
        var result = clip.Copy(html, plain);

        var message = result switch
        {
            ClipboardService.Result.RichTextCopied => $"[green]✓[/] Copied {(full ? "full-fat" : "clean")} scrum as rich text",
            ClipboardService.Result.PlainTextCopied => $"[yellow]✓[/] Copied {(full ? "full-fat" : "clean")} scrum as plain text (rich text unavailable)",
            _ => "[red]✗[/] Failed to copy"
        };
        AnsiConsole.MarkupLine(message);
    }
}
