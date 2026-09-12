using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console;

namespace SSW.TimePro.Cli.Features.Rates;

/// <summary>
/// Shared reporting for rate-resolving flows (timesheet create/update) when a client has no active
/// rate. Emits a machine-actionable recovery recipe on the --json path and ready-to-run commands on
/// the human path, so an agent — or a person — can set a rate and retry.
/// </summary>
public static class RateGuard
{
    public static string NoActiveRateMessage(string clientId) =>
        $"No active rate for client '{clientId}' (expired or not set). " +
        "Set a rate using the recovery command below, then retry.";

    /// <summary>The recovery recipe, shared by the CLI error envelope and the MCP error payload.</summary>
    public static object BuildRecovery(string clientId, RateRecommendation rec) => new
    {
        reason = "no_active_rate",
        clientId,
        recommended = rec.Source == RateSource.None
            ? null
            : (object)new { rate = rec.Rate, prepaidRate = rec.PrepaidRate, source = rec.Source.ToString() },
        steps = RateResolver.BuildRecoveryOptions(clientId, rec)
    };

    public static void ReportNoActiveRate(string clientId, RateRecommendation rec, bool json)
    {
        var msg = NoActiveRateMessage(clientId);

        if (json)
        {
            OutputHelper.WriteJsonError(msg, code: null, detail: null, recovery: BuildRecovery(clientId, rec));
        }
        else
        {
            OutputHelper.WriteError(msg);
            foreach (var s in RateResolver.BuildRecoveryOptions(clientId, rec))
                OutputHelper.WriteInfo($"  [{s.Action}] {s.Command}");
        }
    }
}
