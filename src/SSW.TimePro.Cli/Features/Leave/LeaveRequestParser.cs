using System.Globalization;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Leave;

internal static class LeaveRequestParser
{
    public const string DefaultStartTime = "09:00:00";
    public const string DefaultEndTime = "18:00:00";

    public static bool TryParseDateRange(
        string start,
        string end,
        TimeZoneInfo timeZone,
        out DateTimeOffset startDate,
        out DateTimeOffset endDate,
        out string? error)
    {
        startDate = default;
        endDate = default;
        error = null;

        if (!TryParseStartDate(start, timeZone, out startDate))
        {
            error = $"Invalid start date: '{start}'. Use yyyy-MM-dd format.";
            return false;
        }

        if (!TryParseEndDate(end, timeZone, out endDate))
        {
            error = $"Invalid end date: '{end}'. Use yyyy-MM-dd format.";
            return false;
        }

        return true;
    }

    public static string NormalizeTime(string? time, string defaultValue)
    {
        var normalized = string.IsNullOrWhiteSpace(time) ? defaultValue : time.Trim();
        return normalized.Length == 5 ? normalized + ":00" : normalized;
    }

    public static List<string> ParseOptionalEmployees(string? optionalEmp) =>
        string.IsNullOrWhiteSpace(optionalEmp)
            ? []
            : optionalEmp.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static bool TryResolveRequestTimeZone(
        string? overrideTimeZoneId,
        EmployeeSettings? settings,
        out TimeZoneInfo timeZone,
        out string? error)
    {
        if (!string.IsNullOrWhiteSpace(overrideTimeZoneId))
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(overrideTimeZoneId.Trim(), out timeZone!))
            {
                error = null;
                return true;
            }

            error = $"Timezone override has an unknown timezone: '{overrideTimeZoneId}'. Use a valid IANA or Windows timezone ID.";
            timeZone = TimeZoneInfo.Local;
            return false;
        }

        if (settings is not null && !string.IsNullOrWhiteSpace(settings.TimezoneId))
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(settings.TimezoneId.Trim(), out timeZone!))
            {
                error = null;
                return true;
            }

            error = $"TimePro user profile has an unknown timezone: '{settings.TimezoneId}'. Update the profile timezone or run on a machine with that timezone available.";
            timeZone = TimeZoneInfo.Local;
            return false;
        }

        timeZone = TimeZoneInfo.Local;
        error = null;
        return true;
    }

    private static bool TryParseStartDate(string value, TimeZoneInfo timeZone, out DateTimeOffset result)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            result = ToDateTimeOffset(dateOnly, TimeOnly.MinValue, timeZone);
            return true;
        }

        return TryParseDateTime(value, timeZone, useEndOfDayForDateOnly: false, out result);
    }

    private static bool TryParseEndDate(string value, TimeZoneInfo timeZone, out DateTimeOffset result)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            result = ToDateTimeOffset(dateOnly, new TimeOnly(23, 59, 0), timeZone);
            return true;
        }

        return TryParseDateTime(value, timeZone, useEndOfDayForDateOnly: true, out result);
    }

    private static DateTimeOffset ToDateTimeOffset(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var dateTime = date.ToDateTime(time);
        return new DateTimeOffset(dateTime, timeZone.GetUtcOffset(dateTime));
    }

    private static bool TryParseDateTime(
        string value,
        TimeZoneInfo timeZone,
        bool useEndOfDayForDateOnly,
        out DateTimeOffset result)
    {
        var trimmed = value.Trim();

        if (HasExplicitOffset(trimmed))
            return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

        if (!DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
        {
            result = default;
            return false;
        }

        if (useEndOfDayForDateOnly && dateTime.TimeOfDay == TimeSpan.Zero)
            dateTime = dateTime.Date.AddHours(23).AddMinutes(59);

        dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
        result = new DateTimeOffset(dateTime, timeZone.GetUtcOffset(dateTime));
        return true;
    }

    private static bool HasExplicitOffset(string value)
    {
        if (value.EndsWith('Z') || value.EndsWith('z'))
            return true;

        var timeSeparator = Math.Max(value.LastIndexOf('T'), value.LastIndexOf(' '));
        if (timeSeparator < 0)
            return false;

        for (var i = value.Length - 1; i > timeSeparator; i--)
        {
            if (value[i] is '+' or '-')
                return true;
        }

        return false;
    }
}
