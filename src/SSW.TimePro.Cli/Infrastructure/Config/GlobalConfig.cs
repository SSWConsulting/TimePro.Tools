using System.Text.Json.Serialization;

namespace SSW.TimePro.Cli.Infrastructure.Config;

/// <summary>
/// Global CLI configuration stored at ~/.config/timepro-cli/config.json.
/// </summary>
public class GlobalConfig
{
    [JsonPropertyName("activeTenant")]
    public string? ActiveTenant { get; set; }

    [JsonPropertyName("wfhDays")]
    public List<string> WfhDays { get; set; } = [];

    [JsonPropertyName("defaultLocation")]
    public string DefaultLocation { get; set; } = "Office";

    /// <summary>
    /// Optional feature packs that affect generated skills and MCP tool registration.
    /// </summary>
    [JsonPropertyName("features")]
    public Dictionary<string, FeatureConfig> Features { get; set; } = [];

    /// <summary>
    /// Generated skill versions installed by <c>tp skills create</c>.
    /// Used by <c>tp info</c> to flag locally stale skills.
    /// </summary>
    [JsonPropertyName("skills")]
    public Dictionary<string, SkillInstallConfig> Skills { get; set; } = [];

    /// <summary>
    /// Tracks the installed CLI version so release notes can show what changed
    /// since the user's previous install.
    /// </summary>
    [JsonPropertyName("version")]
    public InstalledVersionConfig Version { get; set; } = new();

    /// <summary>
    /// Settings used by <c>tp scrum</c> when generating daily scrum emails.
    /// </summary>
    [JsonPropertyName("scrum")]
    public ScrumConfig Scrum { get; set; } = new();

    /// <summary>
    /// Settings used by <c>tp accounting guide</c> and <c>tp dev guide</c>.
    /// </summary>
    [JsonPropertyName("guides")]
    public GuideConfig Guides { get; set; } = new();
}

public class FeatureConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Latest feature content version used by this local config.
    /// Used by future skill/MCP auto-upgrade checks.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }
}

public class SkillInstallConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("ignoredVersion")]
    public int? IgnoredVersion { get; set; }

    [JsonPropertyName("installedAt")]
    public DateTimeOffset? InstalledAt { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("global")]
    public bool Global { get; set; }
}

public class InstalledVersionConfig
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("previousVersion")]
    public string? PreviousVersion { get; set; }

    [JsonPropertyName("installedAt")]
    public DateTimeOffset? InstalledAt { get; set; }

    [JsonPropertyName("lastUpdateCheckedAt")]
    public DateTimeOffset? LastUpdateCheckedAt { get; set; }

    [JsonPropertyName("lastUpdateCheckedVersion")]
    public string? LastUpdateCheckedVersion { get; set; }
}

public class GuideConfig
{
    public const string DefaultRepositoryUrl = "https://github.com/SSWConsulting/TimePro.Tools";
    public const string DefaultBranch = "main";

    /// <summary>
    /// How long GitHub-downloaded diagnostic guides stay fresh in the local
    /// cache. Set to 0 to refresh on every guide command.
    /// </summary>
    [JsonPropertyName("cacheMinutes")]
    public int CacheMinutes { get; set; } = 5;

    /// <summary>
    /// GitHub repository that hosts the <c>guides/</c> folder.
    /// </summary>
    [JsonPropertyName("repositoryUrl")]
    public string RepositoryUrl { get; set; } = DefaultRepositoryUrl;

    /// <summary>
    /// Branch or ref used when downloading guide content.
    /// </summary>
    [JsonPropertyName("branch")]
    public string Branch { get; set; } = DefaultBranch;
}

/// <summary>
/// Configuration for the <c>tp scrum</c> daily scrum generator.
/// </summary>
public class ScrumConfig
{
    /// <summary>Optional Trello board URL shown in the internal scrum block.</summary>
    [JsonPropertyName("trelloUrl")]
    public string? TrelloUrl { get; set; }

    /// <summary>Email address shown in the decorative "To:" header for internal scrums.</summary>
    [JsonPropertyName("benchMastersEmail")]
    public string BenchMastersEmail { get; set; } = "SSWBenchMasters@ssw.com.au";

    /// <summary>Email address shown in the decorative "Cc:" header.</summary>
    [JsonPropertyName("dailyScrumCc")]
    public string DailyScrumCc { get; set; } = "SSWDailyScrum@ssw.com.au";

    /// <summary>Footer link referenced in the SSW daily scrum rule.</summary>
    [JsonPropertyName("footerUrl")]
    public string FooterUrl { get; set; } = "https://my.sugarlearning.com/SSW/items/8291";

    /// <summary>
    /// Optional local time-of-day boundary between "yesterday" and "today" for
    /// scrum classification. Work completed before this cutoff (back to the
    /// previous work day) is "yesterday"; work completed after it (this morning)
    /// shows as done under "today". When unset (null) the boundary is midnight
    /// (standard behaviour). Set e.g. "09:00:00" so a PR merged at 09:30 before a
    /// 10:00 stand-up still appears.
    /// </summary>
    [JsonPropertyName("cutoffTime")]
    public TimeOnly? CutoffTime { get; set; }

    /// <summary>
    /// How many days back an in-progress (open) item may have last been touched
    /// and still count as "yesterday". Keeps ongoing work visible across gaps and
    /// sprint boundaries where the literal previous work day is empty. Default 14.
    /// </summary>
    [JsonPropertyName("yesterdayLookbackDays")]
    public int YesterdayLookbackDays { get; set; } = 14;
}
