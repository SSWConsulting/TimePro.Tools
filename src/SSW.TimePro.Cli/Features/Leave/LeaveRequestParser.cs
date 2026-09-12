using System.Globalization;
using SSW.TimePro.Cli.Shared.Models;

namespace SSW.TimePro.Cli.Features.Leave;

internal static class LeaveRequestParser
{
    public static readonly TimeOnly DefaultStartTime = new(9, 0);
    public static readonly TimeOnly DefaultEndTime = new(18, 0);

    private static readonly TimeOnly EndOfDay = new(23, 59, 0);
    private static readonly string[] WorkdayTimeFormats = ["H:mm:ss", "H:mm"];

    /// <summary>Partial-day windows pass their workday times; null means all-day (00:00-23:59).</summary>
    public static bool TryParseDateRange(
        string start,
        string end,
        TimeZoneInfo timeZone,
        TimeOnly? partialDayStart,
        TimeOnly? partialDayEnd,
        out DateTimeOffset startDate,
        out DateTimeOffset endDate,
        out string? error)
    {
        startDate = default;
        endDate = default;
        error = null;

        if (!TryParseBoundary(start, timeZone, partialDayStart ?? TimeOnly.MinValue, partialDayStart.HasValue, out startDate))
        {
            error = $"Invalid start date: '{start}'. Use yyyy-MM-dd format.";
            return false;
        }

        if (!TryParseBoundary(end, timeZone, partialDayEnd ?? EndOfDay, partialDayEnd.HasValue, out endDate))
        {
            error = $"Invalid end date: '{end}'. Use yyyy-MM-dd format.";
            return false;
        }

        return true;
    }

    public static bool TryParseWorkdayTime(string? value, TimeOnly fallback, string label, out TimeOnly time, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            time = fallback;
            error = null;
            return true;
        }

        if (TimeOnly.TryParseExact(value.Trim(), WorkdayTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        {
            error = null;
            return true;
        }

        time = fallback;
        error = $"Invalid workday {label} time: '{value}'. Use HH:mm or HH:mm:ss.";
        return false;
    }

    /// <summary>Mirrors the server rule so dry-run rejects what the real call would reject.</summary>
    public static bool TryValidatePartialDayTimes(TimeOnly startTime, TimeOnly endTime, out string? error)
    {
        if (!IsOnHourOrHalfHour(startTime))
        {
            error = "Start time must be on the hour or half-hour.";
            return false;
        }

        if (!IsOnHourOrHalfHour(endTime))
        {
            error = "End time must be on the hour or half-hour.";
            return false;
        }

        error = null;
        return true;
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
        bool forceTimeOfDay,
        out DateTimeOffset result)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            result = ToDateTimeOffset(dateOnly, timeOfDay, timeZone);
            return true;
        }

        if (!TryParseDateTime(value, timeZone, timeOfDay, out result))
            return false;

        if (forceTimeOfDay)
            result = new DateTimeOffset(result.Date.Add(timeOfDay.ToTimeSpan()), result.Offset);

        return true;
    }

    private static bool IsOnHourOrHalfHour(TimeOnly value) =>
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
