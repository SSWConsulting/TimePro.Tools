using System.Net;
using System.Text;
using SSW.TimePro.Cli.Infrastructure.Config;

namespace SSW.TimePro.Cli.Features.Scrum;

/// <summary>
/// Formats a <see cref="ScrumModel"/> into the three flavors we need:
/// styled terminal output (Spectre markup + OSC 8 hyperlinks), HTML for
/// the rich-text clipboard, and plain text as a fallback.
/// </summary>
public class ScrumRenderer
{
    private readonly ScrumConfig _config;

    public ScrumRenderer(ScrumConfig config) => _config = config;

    // --- Public API -----------------------------------------------------

    /// <summary>
    /// Renders the full terminal view including the decorative email header.
    /// The email header is <b>not</b> part of what gets copied to the clipboard.
    /// </summary>
    public string RenderTerminal(ScrumModel m) =>
        RenderHeader(m) + "\n" + RenderBodyTerminal(m) + RenderFooterTerminal();

    /// <summary>
    /// Renders just the body (without the "To:/Cc:/Subject:" header) as
    /// Spectre markup with embedded OSC 8 hyperlinks. This is what `c` copies.
    /// </summary>
    public string RenderBodyTerminal(ScrumModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Hi team,");
        sb.AppendLine();

        if (m.IsInternal && m.Internal is not null)
        {
            sb.AppendLine(Dim("<Only for internal Daily Scrums>"));
            if (m.Internal.DaysUntilNextClientBooking is { } days)
                sb.AppendLine($"- I have {days} days until my next client booking.");
            if (!string.IsNullOrEmpty(m.Internal.TrelloUrl))
                sb.AppendLine($"- My Trello board is at {Osc8(m.Internal.TrelloUrl, m.Internal.TrelloUrl)}.");
            sb.AppendLine($"- I have joined my Daily Scrum meeting {(m.Internal.JoinedScrumMeeting ? "✅" : "❌")}");
            sb.AppendLine(Dim("</Only for internal Daily Scrums>"));
            sb.AppendLine();
        }

        sb.AppendLine("Yesterday I worked on:");
        if (m.Yesterday.Count == 0)
            sb.AppendLine("- " + Dim("(nothing recorded)"));
        else
            foreach (var item in m.Yesterday)
                sb.AppendLine(RenderItemTerminal(item));
        sb.AppendLine();

        sb.AppendLine("Today I'm working on:");
        if (m.Today.Count == 0)
            sb.AppendLine("- " + Dim("(nothing planned)"));
        else
            foreach (var item in m.Today)
                sb.AppendLine(RenderItemTerminal(item));

        sb.AppendLine();
        if (!string.IsNullOrEmpty(_config.FooterUrl))
            sb.AppendLine(Dim($"<This email was sent as per {_config.FooterUrl}>"));

        return sb.ToString();
    }

    /// <summary>
    /// Plain-text flavor used as the clipboard fallback when rich text fails.
    /// No OSC 8 hyperlinks, no ANSI — just Markdown-ish link syntax.
    /// </summary>
    public string RenderPlain(ScrumModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Hi team,");
        sb.AppendLine();

        if (m.IsInternal && m.Internal is not null)
        {
            sb.AppendLine("<Only for internal Daily Scrums>");
            if (m.Internal.DaysUntilNextClientBooking is { } days)
                sb.AppendLine($"- I have {days} days until my next client booking.");
            if (!string.IsNullOrEmpty(m.Internal.TrelloUrl))
                sb.AppendLine($"- My Trello board is at {m.Internal.TrelloUrl}.");
            sb.AppendLine($"- I have joined my Daily Scrum meeting {(m.Internal.JoinedScrumMeeting ? "✅" : "❌")}");
            sb.AppendLine("</Only for internal Daily Scrums>");
            sb.AppendLine();
        }

        sb.AppendLine("Yesterday I worked on:");
        if (m.Yesterday.Count == 0)
            sb.AppendLine("- (nothing recorded)");
        else
            foreach (var item in m.Yesterday)
                sb.AppendLine(RenderItemPlain(item));
        sb.AppendLine();

        sb.AppendLine("Today I'm working on:");
        if (m.Today.Count == 0)
            sb.AppendLine("- (nothing planned)");
        else
            foreach (var item in m.Today)
                sb.AppendLine(RenderItemPlain(item));

        sb.AppendLine();
        if (!string.IsNullOrEmpty(_config.FooterUrl))
            sb.AppendLine($"<This email was sent as per {_config.FooterUrl}>");

        return sb.ToString();
    }

    /// <summary>
    /// HTML flavor used for the rich-text clipboard. Styled inline so it
    /// survives pasting into Outlook / Apple Mail / Gmail.
    /// </summary>
    public string RenderHtml(ScrumModel m)
    {
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:-apple-system,Segoe UI,Helvetica,sans-serif;font-size:14px;line-height:1.4\">");
        sb.Append("<p>Hi team,</p>");

        if (m.IsInternal && m.Internal is not null)
        {
            sb.Append("<p><em>&lt;Only for internal Daily Scrums&gt;</em></p>");
            sb.Append("<ul>");
            if (m.Internal.DaysUntilNextClientBooking is { } days)
                sb.Append($"<li>I have {days} days until my next client booking.</li>");
            if (!string.IsNullOrEmpty(m.Internal.TrelloUrl))
                sb.Append($"<li>My Trello board is at <a href=\"{WebUtility.HtmlEncode(m.Internal.TrelloUrl)}\">{WebUtility.HtmlEncode(m.Internal.TrelloUrl)}</a>.</li>");
            sb.Append($"<li>I have joined my Daily Scrum meeting {(m.Internal.JoinedScrumMeeting ? "✅" : "❌")}</li>");
            sb.Append("</ul>");
            sb.Append("<p><em>&lt;/Only for internal Daily Scrums&gt;</em></p>");
        }

        sb.Append("<p>Yesterday I worked on:</p>");
        sb.Append("<ul>");
        if (m.Yesterday.Count == 0)
            sb.Append("<li><em>(nothing recorded)</em></li>");
        else
            foreach (var item in m.Yesterday)
                sb.Append(RenderItemHtml(item));
        sb.Append("</ul>");

        sb.Append("<p>Today I'm working on:</p>");
        sb.Append("<ul>");
        if (m.Today.Count == 0)
            sb.Append("<li><em>(nothing planned)</em></li>");
        else
            foreach (var item in m.Today)
                sb.Append(RenderItemHtml(item));
        sb.Append("</ul>");

        if (!string.IsNullOrEmpty(_config.FooterUrl))
            sb.Append($"<p><em>&lt;This email was sent as per <a href=\"{WebUtility.HtmlEncode(_config.FooterUrl)}\">{WebUtility.HtmlEncode(_config.FooterUrl)}</a>&gt;</em></p>");

        sb.Append("</div>");
        return sb.ToString();
    }

    // --- Item rendering -------------------------------------------------

    private static string RenderItemTerminal(ScrumItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(item.Status)) parts.Add(item.Status);
        if (!string.IsNullOrEmpty(item.Kind)) parts.Add(item.Kind);
        var prefix = string.Join(" – ", parts);
        if (!string.IsNullOrEmpty(item.Url) && !string.IsNullOrEmpty(item.Reference))
            return $"- {prefix} – {item.Title} {Osc8(item.Url, item.Reference)}";
        return $"- {prefix} – {item.Title}";
    }

    private static string RenderItemPlain(ScrumItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(item.Status)) parts.Add(item.Status);
        if (!string.IsNullOrEmpty(item.Kind)) parts.Add(item.Kind);
        var prefix = string.Join(" – ", parts);
        if (!string.IsNullOrEmpty(item.Url) && !string.IsNullOrEmpty(item.Reference))
            return $"- {prefix} – {item.Title} [{item.Reference}]({item.Url})";
        return $"- {prefix} – {item.Title}";
    }

    private static string RenderItemHtml(ScrumItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(item.Status)) parts.Add(WebUtility.HtmlEncode(item.Status));
        if (!string.IsNullOrEmpty(item.Kind)) parts.Add(WebUtility.HtmlEncode(item.Kind));
        var prefix = string.Join(" – ", parts);
        var title = WebUtility.HtmlEncode(item.Title);
        var body = string.IsNullOrEmpty(prefix) ? title : $"{prefix} – {title}";
        if (!string.IsNullOrEmpty(item.Url) && !string.IsNullOrEmpty(item.Reference))
            body += $" <a href=\"{WebUtility.HtmlEncode(item.Url)}\">{WebUtility.HtmlEncode(item.Reference)}</a>";
        return $"<li>{body}</li>";
    }

    // --- Header / footer ------------------------------------------------

    private string RenderHeader(ScrumModel m)
    {
        var to = m.IsInternal
            ? _config.BenchMastersEmail
            : $"{{ {m.PrimaryClientName?.Trim() ?? "CLIENT"} }}";
        var sb = new StringBuilder();
        sb.AppendLine(Dim("┌─ (decorative, not copied) ─────────────────────────────┐"));
        sb.AppendLine($"{Dim("│")} {Dim("To:")}      {to}");
        sb.AppendLine($"{Dim("│")} {Dim("Cc:")}      {_config.DailyScrumCc}");
        sb.AppendLine($"{Dim("│")} {Dim("Subject:")} JK - Daily Scrum");
        sb.AppendLine(Dim("└────────────────────────────────────────────────────────┘"));
        return sb.ToString();
    }

    private static string RenderFooterTerminal() => string.Empty;

    // --- ANSI helpers --------------------------------------------------
    // Raw ANSI so we don't collide with Spectre.Console markup parser,
    // which chokes on the "]" inside OSC 8 hyperlink escapes.

    private static string Dim(string s) => $"\x1b[2m{s}\x1b[0m";

    private static string Osc8(string url, string text) =>
        $"\x1b]8;;{url}\x1b\\{text}\x1b]8;;\x1b\\";
}
