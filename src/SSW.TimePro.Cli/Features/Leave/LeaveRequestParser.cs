using System.Globalization;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Leave;

internal static class LeaveRequestParser
{
    public const string DefaultStartTime = "09:00:00";
    public const string DefaultEndTime = "18:00:00";

    private static readonly TimeOnly EndOfDay = new(23, 59, 0);

    public static bool TryParseDateRange(
        string start,
        string end,
        TimeZoneInfo timeZone,
        bool allDay,
        string userStartTime,
        string userEndTime,
        out DateTimeOffset startDate,
        out DateTimeOffset endDate,
        out string? error)
    {
        startDate = default;
        endDate = default;
        error = null;

        var startTimeOfDay = allDay ? TimeOnly.MinValue : ParseTimeOfDay(userStartTime, DefaultStartTime);
        var endTimeOfDay = allDay ? EndOfDay : ParseTimeOfDay(userEndTime, DefaultEndTime);

        if (!TryParseBoundary(start, timeZone, startTimeOfDay, out startDate))
        {
            error = $"Invalid start date: '{start}'. Use yyyy-MM-dd format.";
            return false;
        }

        if (!TryParseBoundary(end, timeZone, endTimeOfDay, out endDate))
        {
            error = $"Invalid end date: '{end}'. Use yyyy-MM-dd format.";
            return false;
        }

        return true;
    }

    /// <summary>Mirrors the server rule so dry-run rejects what the real call would reject.</summary>
    public static bool TryValidatePartialDayTimes(DateTimeOffset startDate, DateTimeOffset endDate, out string? error)
    {
        if (!IsOnHourOrHalfHour(startDate))
        {
            error = "Start time must be on the hour or half-hour.";
            return false;
        }

        if (!IsOnHourOrHalfHour(endDate))
        {
            error = "End time must be on the hour or half-hour.";
            return false;
        }

        error = null;
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

    private static bool TryParseBoundary(
        string value,
        TimeZoneInfo timeZone,
        TimeOnly timeOfDay,
        out DateTimeOffset result)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            result = ToDateTimeOffset(dateOnly, timeOfDay, timeZone);
            return true;
        }

        return TryParseDateTime(value, timeZone, timeOfDay, out result);
    }

    private static TimeOnly ParseTimeOfDay(string? value, string fallback)
    {
        var normalized = NormalizeTime(value, fallback);
        return TimeOnly.TryParseExact(normalized, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : TimeOnly.Parse(fallback, CultureInfo.InvariantCulture);
    }

    private static bool IsOnHourOrHalfHour(DateTimeOffset value) =>
        value.Minute is 0 or 30 && value.Second == 0 && value.Millisecond == 0;

    private static DateTimeOffset ToDateTimeOffset(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var dateTime = date.ToDateTime(time);
        return new DateTimeOffset(dateTime, timeZone.GetUtcOffset(dateTime));
    }

    private static bool TryParseDateTime(
        string value,
        TimeZoneInfo timeZone,
        TimeOnly timeOfDay,
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

        if (dateTime.TimeOfDay == TimeSpan.Zero)
            dateTime = dateTime.Date.Add(timeOfDay.ToTimeSpan());

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
