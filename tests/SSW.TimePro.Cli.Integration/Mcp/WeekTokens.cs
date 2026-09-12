namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// Replaces the current work week's five dates with tokens. <c>WeekCoverageService</c> derives its
/// window from the machine clock and has no injectable clock, so this — and only this — is the
/// declared normalisation the harness applies.
/// </summary>
public static class WeekTokens
{
    public static string Apply(string json)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monday = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        if (today.DayOfWeek == DayOfWeek.Sunday)
            monday = monday.AddDays(-7);

        for (var i = 0; i < 5; i++)
            json = json.Replace(monday.AddDays(i).ToString("yyyy-MM-dd"), $"{{{{weekday{i}}}}}");

        return json;
    }
}
