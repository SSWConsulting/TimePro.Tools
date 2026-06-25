using System.Globalization;
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

    /// <summary>A rate is active on <paramref name="onDate"/> when it has no expiry or expires on/after that date.</summary>
    public static bool IsActive(DateTime? expiry, DateOnly onDate) =>
        expiry is null || DateOnly.FromDateTime(expiry.Value) >= onDate;

    /// <summary>
    /// Build the ready-to-run recovery command for a client that has no active rate, so a
    /// non-interactive caller (an agent) can set a rate and retry. Mirrors the Angular timesheet
    /// dialog, which only ever <c>create</c>s a rate (amount = recommended, else explicit); changing
    /// an existing rate's expiry is a separate, explicit <c>tp rate update</c>.
    /// </summary>
    public static IReadOnlyList<RecoveryOption> BuildRecoveryOptions(string clientId, RateRecommendation rec)
    {
        // Invariant culture: these strings are CLI commands — a locale decimal comma would not
        // round-trip through arg parsing on machines with a different separator.
        var amounts = rec.Source == RateSource.None
            ? "--rate <amount> --prepaid <amount>"
            : $"--rate {rec.Rate.ToString("0.##", CultureInfo.InvariantCulture)} --prepaid {rec.PrepaidRate.ToString("0.##", CultureInfo.InvariantCulture)}";

        return new[]
        {
            new RecoveryOption(
                "create",
                rec.Source == RateSource.None
                    ? "Create a rate with an explicit amount, then retry."
                    : $"Create a rate at the recommended amount ({rec.Source}), then retry.",
                $"tp rate create --client {clientId} {amounts} --yes")
        };
    }
}
