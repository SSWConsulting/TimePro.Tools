using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Rates;

/// <summary>Where a recommended rate came from.</summary>
public enum RateSource
{
    Previous,
    EmployeeDefault,
    None
}

/// <summary>A recommended rate and where it was sourced from.</summary>
public record RateRecommendation(decimal Rate, decimal PrepaidRate, RateSource Source);

/// <summary>
/// One machine-actionable recovery step an agent can run to fix a missing rate, then retry.
/// <paramref name="Action"/> is a stable token (<c>create</c> / <c>extend</c>); <paramref name="Command"/>
/// is a ready-to-run <c>tp</c> invocation.
/// </summary>
public record RecoveryOption(string Action, string Description, string Command);

/// <summary>
/// Pure rate-resolution logic, mirroring the TimePro Angular rate dialog. Kept free of I/O so it
/// can be unit-tested in isolation (see <c>RateResolverTests</c>).
/// </summary>
public static class RateResolver
{
    /// <summary>
    /// Recommend a rate from the InitializeClientRate building blocks: prefer the latest
    /// client-specific rate (returned regardless of expiry), else the employee default rate.
    /// </summary>
    public static RateRecommendation Recommend(ClientRateInit init)
    {
        var prev = init.PreviousRate ?? 0m;
        var prevPrepaid = init.PreviousPrepaidRate ?? 0m;
        if (prev > 0 || prevPrepaid > 0)
            return new RateRecommendation(prev, prevPrepaid, RateSource.Previous);

        var def = init.DefaultRate ?? 0m;
        var defPrepaid = init.DefaultPrepaidRate ?? 0m;
        if (def > 0 || defPrepaid > 0)
            return new RateRecommendation(def, defPrepaid, RateSource.EmployeeDefault);

        return new RateRecommendation(0m, 0m, RateSource.None);
    }

    /// <summary>Sell price for a billable type: prepaid (BPP) uses the prepaid rate, everything else the regular rate.</summary>
    public static decimal SellPriceFor(string? billableId, decimal rate, decimal prepaidRate) =>
        string.Equals(billableId, "BPP", StringComparison.OrdinalIgnoreCase) ? prepaidRate : rate;

    /// <summary>New expiry when extending a lapsed rate forward — always measured from <paramref name="from"/>.</summary>
    public static DateOnly ExtendedExpiry(DateOnly from, int months = 6) => from.AddMonths(months);

    /// <summary>A rate is active on <paramref name="onDate"/> when it has no expiry or expires on/after that date.</summary>
    public static bool IsActive(DateTime? expiry, DateOnly onDate) =>
        expiry is null || DateOnly.FromDateTime(expiry.Value) >= onDate;

    /// <summary>
    /// Build the ready-to-run recovery commands for a client that has no active rate, so a
    /// non-interactive caller (an agent) can set a rate and retry. Always offers <c>create</c> (a
    /// new rate row, Angular-style); when a previous rate row exists (<paramref name="previousRateId"/>)
    /// it also offers <c>extend</c> — update that row's expiry forward 6 months in place.
    /// </summary>
    public static IReadOnlyList<RecoveryOption> BuildRecoveryOptions(
        string clientId, RateRecommendation rec, int? previousRateId, DateOnly today)
    {
        var options = new List<RecoveryOption>();
        var amounts = rec.Source == RateSource.None
            ? "--rate <amount> --prepaid <amount>"
            : $"--rate {rec.Rate:0.##} --prepaid {rec.PrepaidRate:0.##}";

        options.Add(new RecoveryOption(
            "create",
            rec.Source == RateSource.None
                ? "Create a new rate row with an explicit amount, then retry the timesheet."
                : $"Create a new rate row at the recommended amount ({rec.Source}), then retry the timesheet.",
            $"tp rate create --client {clientId} {amounts} --yes"));

        if (previousRateId is not null)
        {
            var expiry = ExtendedExpiry(today, 6);
            options.Add(new RecoveryOption(
                "extend",
                $"Extend the existing rate in place — push its expiry to {expiry:yyyy-MM-dd} — then retry the timesheet.",
                $"tp rate update --client {clientId} --id {previousRateId} --expiry {expiry:yyyy-MM-dd} --yes"));
        }

        return options;
    }
}
