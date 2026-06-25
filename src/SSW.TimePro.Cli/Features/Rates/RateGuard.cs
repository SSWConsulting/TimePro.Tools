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
    public static void ReportNoActiveRate(string clientId, RateRecommendation rec, bool json)
    {
        var steps = RateResolver.BuildRecoveryOptions(clientId, rec);
        var msg = $"No active rate for client '{clientId}' (expired or not set). " +
                  "Set a rate using the recovery command below, then retry.";

        if (json)
        {
            var recovery = new
            {
                reason = "no_active_rate",
                clientId,
                recommended = rec.Source == RateSource.None
                    ? null
                    : (object)new { rate = rec.Rate, prepaidRate = rec.PrepaidRate, source = rec.Source.ToString() },
                steps
            };
            OutputHelper.WriteJsonError(msg, code: null, detail: null, recovery: recovery);
        }
        else
        {
            OutputHelper.WriteError(msg);
            foreach (var s in steps)
                OutputHelper.WriteInfo($"  [{s.Action}] {s.Command}");
        }
    }
}
