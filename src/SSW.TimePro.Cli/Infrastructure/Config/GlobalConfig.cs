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
    /// Settings used by <c>tp scrum</c> when generating daily scrum emails.
    /// </summary>
    [JsonPropertyName("scrum")]
    public ScrumConfig Scrum { get; set; } = new();
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
}
