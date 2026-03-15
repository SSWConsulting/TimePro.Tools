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
}
