using System.Globalization;
using System.Net;
using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Features.Scrum;

internal enum ScrumTemplateFormat
{
    Markdown,
    Html
}

internal class ScrumTemplateRenderer
{
    private readonly string _configDirectory;

    internal const string DefaultMarkdownTemplate = """
Hi team,

{{#internal}}<Only for internal Daily Scrums>
{{#daysUntilNextClientBooking}}- I have {{daysUntilNextClientBooking}} days until my next client booking.
{{/daysUntilNextClientBooking}}{{#trelloUrl}}- My Trello board is at {{trelloUrl}}.
{{/trelloUrl}}- I have joined my Daily Scrum meeting {{joinedScrumMeeting}}
</Only for internal Daily Scrums>

{{/internal}}Yesterday I worked on:
{{#yesterday}}{{yesterday}}
{{/yesterday}}{{^yesterday}}- (nothing recorded)
{{/yesterday}}
Today I'm working on:
{{#today}}{{today}}
{{/today}}{{^today}}- (nothing planned)
{{/today}}
{{#blockers}}Blocked:
{{blockers}}
{{/blockers}}{{#footerUrl}}<This email was sent as per {{footerUrl}}>
{{/footerUrl}}
""";

    internal const string DefaultHtmlTemplate = """
<div style="font-family:-apple-system,Segoe UI,Helvetica,sans-serif;font-size:14px;line-height:1.4">
<p>Hi team,</p>
{{#internal}}<p><em>&lt;Only for internal Daily Scrums&gt;</em></p>
<ul>
{{#daysUntilNextClientBooking}}<li>I have {{daysUntilNextClientBooking}} days until my next client booking.</li>{{/daysUntilNextClientBooking}}
{{#trelloUrl}}<li>My Trello board is at <a href="{{trelloUrl}}">{{trelloUrl}}</a>.</li>{{/trelloUrl}}
<li>I have joined my Daily Scrum meeting {{joinedScrumMeeting}}</li>
</ul>
<p><em>&lt;/Only for internal Daily Scrums&gt;</em></p>
{{/internal}}<p>Yesterday I worked on:</p>
<ul>
{{#yesterday}}{{yesterday}}{{/yesterday}}{{^yesterday}}<li><em>(nothing recorded)</em></li>{{/yesterday}}
</ul>
<p>Today I'm working on:</p>
<ul>
{{#today}}{{today}}{{/today}}{{^today}}<li><em>(nothing planned)</em></li>{{/today}}
</ul>
{{#blockers}}<p>Blocked:</p>
<ul>
{{blockers}}
</ul>
{{/blockers}}{{#footerUrl}}<p><em>&lt;This email was sent as per <a href="{{footerUrl}}">{{footerUrl}}</a>&gt;</em></p>
{{/footerUrl}}</div>
""";

    public ScrumTemplateRenderer(string configDirectory)
    {
        _configDirectory = configDirectory;
    }

    public string Render(
        ScrumTemplateFormat format,
        string? tenantId,
        string? clientId,
        ScrumModel model,
        ScrumConfig config)
    {
        var template = ResolveRawTemplate(format, tenantId, clientId);
        var values = BuildValues(format, model, config);
        return ScrumTemplateEngine.Render(template, values);
    }

    public string ResolveRawTemplate(ScrumTemplateFormat format, string? tenantId, string? clientId)
    {
        foreach (var path in CandidatePaths(format, tenantId, clientId))
        {
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        return BuiltInTemplate(format);
    }

    internal IEnumerable<string> CandidatePaths(ScrumTemplateFormat format, string? tenantId, string? clientId)
    {
        var ext = Extension(format);
        var tenantPart = SafeScopePart(tenantId);
        var clientPart = SafeScopePart(clientId);

        if (tenantPart is not null && clientPart is not null)
            yield return Path.Combine(_configDirectory, $"daily-scrum.tenant.{tenantPart}.client.{clientPart}.{ext}");
        if (tenantPart is not null)
            yield return Path.Combine(_configDirectory, $"daily-scrum.tenant.{tenantPart}.{ext}");
        if (clientPart is not null)
            yield return Path.Combine(_configDirectory, $"daily-scrum.{clientPart}.{ext}");

        yield return Path.Combine(_configDirectory, $"daily-scrum.{ext}");
    }

    private static Dictionary<string, object?> BuildValues(
        ScrumTemplateFormat format,
        ScrumModel model,
        ScrumConfig config)
    {
        var markdown = format == ScrumTemplateFormat.Markdown;
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["yesterday"] = RenderItems(model.Yesterday, format),
            ["today"] = RenderItems(model.Today, format),
            ["blockers"] = RenderItems(model.Blockers, format),
            ["date"] = model.TodayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["yesterdayDate"] = model.YesterdayDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            ["client"] = FormatText(model.PrimaryClientName, markdown),
            ["footerUrl"] = FormatText(config.FooterUrl, markdown),
            ["internal"] = model.IsInternal && model.Internal is not null,
            ["daysUntilNextClientBooking"] = model.Internal?.DaysUntilNextClientBooking,
            ["trelloUrl"] = FormatText(model.Internal?.TrelloUrl, markdown),
            ["joinedScrumMeeting"] = model.Internal is null
                ? string.Empty
                : model.Internal.JoinedScrumMeeting ? "✅" : "❌"
        };
    }

    private static string RenderItems(IReadOnlyCollection<ScrumItem> items, ScrumTemplateFormat format) =>
        format == ScrumTemplateFormat.Markdown
            ? string.Join(Environment.NewLine, items.Select(item => ScrumRenderer.RenderItemText(item, markdown: true)))
            : string.Concat(items.Select(ScrumRenderer.RenderItemHtml));

    private static string FormatText(string? value, bool markdown) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : markdown ? value : WebUtility.HtmlEncode(value);

    private static string BuiltInTemplate(ScrumTemplateFormat format) =>
        format == ScrumTemplateFormat.Markdown ? DefaultMarkdownTemplate : DefaultHtmlTemplate;

    private static string Extension(ScrumTemplateFormat format) =>
        format == ScrumTemplateFormat.Markdown ? "md" : "html";

    private static string? SafeScopePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains(Path.DirectorySeparatorChar) ||
            trimmed.Contains(Path.AltDirectorySeparatorChar) ||
            trimmed.Contains("..", StringComparison.Ordinal))
            return null;

        return trimmed;
    }
}
