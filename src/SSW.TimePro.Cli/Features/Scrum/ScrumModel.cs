namespace SSW.TimePro.Cli.Features.Scrum;

/// <summary>
/// Structured daily scrum data, rendered once and then formatted for
/// terminal / HTML / plain text.
/// </summary>
public class ScrumModel
{
    public DateOnly TodayDate { get; set; }
    public DateOnly? YesterdayDate { get; set; }

    /// <summary>
    /// True when today has no non-SSW client bookings and no leave — the
    /// internal daily scrum block is included.
    /// </summary>
    public bool IsInternal { get; set; }

    /// <summary>
    /// Headline project shown in the "To:" decorative header. Pulled from
    /// today's timesheets; falls back to "SSW" when internal.
    /// </summary>
    public string? PrimaryClientName { get; set; }

    /// <summary>
    /// Primary client id for resolving scoped daily scrum templates.
    /// </summary>
    public string? PrimaryClientId { get; set; }

    public List<ScrumItem> Yesterday { get; set; } = [];
    public List<ScrumItem> Today { get; set; } = [];

    /// <summary>
    /// Items that are actively blocked. Populated only when <c>--smart</c> is used.
    /// Blockers also appear in <see cref="Today"/> for completeness.
    /// </summary>
    public List<ScrumItem> Blockers { get; set; } = [];

    /// <summary>
    /// Raw timesheet notes for yesterday, keyed by project name. Not
    /// included in the rendered scrum bullets by default (too noisy) —
    /// exposed in the JSON output so agents / skills can use them as
    /// context when enhancing the scrum.
    /// </summary>
    public List<string> YesterdayNotes { get; set; } = [];

    /// <summary>
    /// Raw timesheet notes for today. Same rationale as <see cref="YesterdayNotes"/>.
    /// </summary>
    public List<string> TodayNotes { get; set; } = [];

    public InternalBlock? Internal { get; set; }
}

/// <summary>
/// A single bullet in the scrum — an emoji/status, label and optional link.
/// </summary>
public class ScrumItem
{
    public string Status { get; set; } = string.Empty;   // "✅ Done", "❌ Blocked", ""
    public string Kind { get; set; } = "PBI";             // PBI, Email, Note, Removed
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Reference { get; set; }                // e.g. "#4732"
}

/// <summary>
/// Extra fields shown only when today is internal-SSW work.
/// </summary>
public class InternalBlock
{
    public int? DaysUntilNextClientBooking { get; set; }
    public string? TrelloUrl { get; set; }
    public bool JoinedScrumMeeting { get; set; } = true;
    public int? InboxCount { get; set; }                  // always null for MVP
}
