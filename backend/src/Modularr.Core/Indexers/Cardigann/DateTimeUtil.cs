using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Modularr.Core.Indexers.Cardigann;

// Prowlarr DateTimeUtil: turns the many date shapes trackers print into a DateTime (local time when no zone is given).
public static partial class DateTimeUtil
{
    // Cardigann passes dates between filters and fields in this format.
    public const string Rfc1123ZPattern = "ddd, dd MMM yyyy HH':'mm':'ss z";

    public static string Format(DateTime date) => date.ToString(Rfc1123ZPattern, CultureInfo.InvariantCulture);

    public static DateTime UnixTimestampToDateTime(long unixTime) => DateTime.UnixEpoch.AddSeconds(unixTime).ToLocalTime();

    // "2 hours 1 day ago", "now".
    /// <exception cref="FormatException">An unknown unit.</exception>
    public static DateTime FromTimeAgo(string text)
    {
        text = Bounded(text);
        text = text.ToLowerInvariant();
        if (text.Contains("now", StringComparison.Ordinal))
        {
            return DateTime.Now;
        }

        text = text.Replace(",", "", StringComparison.Ordinal).Replace("ago", "", StringComparison.Ordinal).Replace("and", "", StringComparison.Ordinal);

        TimeSpan ago = TimeSpan.Zero;
        foreach (Match match in TimeAgoPartRegex().Matches(text))
        {
            float value = ParseUtil.CoerceFloat(match.Groups[1].Value);
            string unit = match.Groups[2].Value;
            ago += unit switch
            {
                _ when unit.Contains("sec", StringComparison.Ordinal) || unit == "s" => TimeSpan.FromSeconds(value),
                _ when unit.Contains("min", StringComparison.Ordinal) || unit == "m" => TimeSpan.FromMinutes(value),
                _ when unit.Contains("hour", StringComparison.Ordinal) || unit.Contains("hr", StringComparison.Ordinal) || unit == "h" => TimeSpan.FromHours(value),
                _ when unit.Contains("day", StringComparison.Ordinal) || unit == "d" => TimeSpan.FromDays(value),
                _ when unit.Contains("week", StringComparison.Ordinal) || unit.Contains("wk", StringComparison.Ordinal) || unit == "w" => TimeSpan.FromDays(value * 7),
                _ when unit.Contains("month", StringComparison.Ordinal) || unit == "mo" => TimeSpan.FromDays(value * 30),
                _ when unit.Contains("year", StringComparison.Ordinal) || unit == "y" => TimeSpan.FromDays(value * 365),
                _ => throw new FormatException($"TimeAgo parsing failed, unknown unit: {unit}"),
            };
        }

        return DateTime.Now - ago;
    }

    /// <exception cref="FormatException">No date or time found.</exception>
    public static DateTime FromFuzzyTime(string text, string? format = null) =>
        DateTimeRoutines.TryParseDateOrTime(Bounded(text), ukDate: format == "UK", out DateTime date) ? date : throw new FormatException($"FromFuzzyTime parsing failed for string {text}");

    // Scraped date text is remote data: a real date is a few dozen characters, a longer value only feeds backtracking regexes.
    private const int MaxDateLength = 256;

    private static string Bounded(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= MaxDateLength ? text : throw new FormatException($"Date text longer than {MaxDateLength} characters");
    }

    /// <exception cref="FormatException">The text is not a recognizable date.</exception>
    public static DateTime FromUnknown(string text, string? format = null)
    {
        Bounded(text);
        try
        {
            text = text.Trim();

            if (DateTime.TryParseExact(text, Rfc1123ZPattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                return parsed;
            }

            if (text.Length > 0 && text.All(char.IsAsciiDigit) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long unixTime))
            {
                return UnixTimestampToDateTime(unixTime);
            }

            if (text.Contains("now", StringComparison.OrdinalIgnoreCase))
            {
                return DateTime.UtcNow;
            }

            if (TimeAgoRegex().IsMatch(text))
            {
                return FromTimeAgo(text);
            }

            // Today / Yesterday / Tomorrow [at] [time]
            foreach ((Regex regex, int days) in RelativeDays)
            {
                Match relative = regex.Match(text);
                if (relative.Success)
                {
                    return UtcToday() + ParseTimeOfDay(text.Replace(relative.Value, "", StringComparison.Ordinal)) + TimeSpan.FromDays(days);
                }
            }

            // "Saturday at 14:22": the latest such weekday, today included.
            Match weekday = DayOfWeekRegex().Match(text);
            if (weekday.Success)
            {
                DateTime date = UtcToday() + ParseTimeOfDay(text.Replace(weekday.Value, "", StringComparison.Ordinal));
                DayOfWeek dayOfWeek = Enum.Parse<DayOfWeek>(weekday.Groups[1].Value, ignoreCase: true);
                while (date.DayOfWeek != dayOfWeek)
                {
                    date = date.AddDays(-1);
                }

                return date;
            }

            Match missingYear = MissingYearRegex().Match(text);
            if (missingYear.Success)
            {
                string date = missingYear.Groups[1].Value;
                text = text.Replace(date, $"{DateTime.Now.Year}-{date}", StringComparison.Ordinal);
            }

            // "1 Jan 10:30"
            missingYear = MissingYearBeforeTimeRegex().Match(text);
            if (missingYear.Success)
            {
                text = $"{missingYear.Groups[1].Value} {DateTime.Now.Year} {missingYear.Groups[2].Value}";
            }

            return FromFuzzyTime(text, format);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new FormatException($"DateTime parsing failed for \"{text}\": {ex.Message}", ex);
        }
    }

    // Converts a Go layout ("2006-01-02 15:04:05 -07:00") to a .NET one; a .NET layout is tried as is first.
    /// <exception cref="FormatException">The date does not match the layout.</exception>
    public static DateTime ParseDateTimeGoLang(string date, string layout)
    {
        ArgumentNullException.ThrowIfNull(date);
        ArgumentNullException.ThrowIfNull(layout);
        date = date.Trim();

        if (layout.AsSpan().ContainsAny(StandardLayoutLetters)
            && DateTime.TryParseExact(date, layout, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            return parsed;
        }

        string format = layout
            .Replace("2006", "yyyy", StringComparison.Ordinal)
            .Replace("06", "yy", StringComparison.Ordinal)
            .Replace("January", "MMMM", StringComparison.Ordinal)
            .Replace("Jan", "MMM", StringComparison.Ordinal)
            .Replace("01", "MM", StringComparison.Ordinal)
            .Replace("Monday", "dddd", StringComparison.Ordinal)
            .Replace("Mon", "ddd", StringComparison.Ordinal)
            .Replace("02", "dd", StringComparison.Ordinal)
            .Replace("2", "d", StringComparison.Ordinal)
            .Replace("05", "ss", StringComparison.Ordinal)
            .Replace("15", "HH", StringComparison.Ordinal)
            .Replace("03", "hh", StringComparison.Ordinal)
            .Replace("3", "h", StringComparison.Ordinal)
            .Replace("04", "mm", StringComparison.Ordinal)
            .Replace("4", "m", StringComparison.Ordinal)
            .Replace("5", "s", StringComparison.Ordinal)
            .Replace("1", "M", StringComparison.Ordinal)
            .Replace(".0000", "ffff", StringComparison.Ordinal)
            .Replace(".000", "fff", StringComparison.Ordinal)
            .Replace(".00", "ff", StringComparison.Ordinal)
            .Replace(".0", "f", StringComparison.Ordinal)
            .Replace(".9999", "FFFF", StringComparison.Ordinal)
            .Replace(".999", "FFF", StringComparison.Ordinal)
            .Replace(".99", "FF", StringComparison.Ordinal)
            .Replace(".9", "F", StringComparison.Ordinal)
            .Replace("PM", "tt", StringComparison.Ordinal)
            .Replace("pm", "tt", StringComparison.Ordinal)
            .Replace("Z07:00", "'Z'zzz", StringComparison.Ordinal)
            .Replace("Z07", "'Z'zz", StringComparison.Ordinal)
            .Replace("-07:00", "zzz", StringComparison.Ordinal)
            .Replace("-07", "zz", StringComparison.Ordinal);

        return DateTime.TryParseExact(date, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
            ? parsed
            : throw new FormatException($"Error while parsing DateTime \"{date}\", using layout \"{layout}\" ({format})");
    }

    // A layout with these letters may already be a .NET one.
    private static readonly SearchValues<char> StandardLayoutLetters = SearchValues.Create("yYhHdD");

    private static readonly (Regex Regex, int Days)[] RelativeDays = [(TodayRegex(), 0), (YesterdayRegex(), -1), (TomorrowRegex(), 1)];

    private static DateTime UtcToday() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Unspecified);

    private static TimeSpan ParseTimeOfDay(string time) =>
        string.IsNullOrWhiteSpace(time) ? TimeSpan.Zero : DateTime.Parse(time, CultureInfo.InvariantCulture).TimeOfDay;

    [GeneratedRegex(@"\s*?([\d\.]+)\s*?([^\d\s\.]+)\s*?")]
    private static partial Regex TimeAgoPartRegex();

    [GeneratedRegex(@"(?i)\bago")]
    private static partial Regex TimeAgoRegex();

    [GeneratedRegex(@"(?i)\btoday(?:[\s,]+(?:at){0,1}\s*|[\s,]*|$)")]
    private static partial Regex TodayRegex();

    [GeneratedRegex(@"(?i)\btomorrow(?:[\s,]+(?:at){0,1}\s*|[\s,]*|$)")]
    private static partial Regex TomorrowRegex();

    [GeneratedRegex(@"(?i)\byesterday(?:[\s,]+(?:at){0,1}\s*|[\s,]*|$)")]
    private static partial Regex YesterdayRegex();

    [GeneratedRegex(@"(?i)\b(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\s+at\s+")]
    private static partial Regex DayOfWeekRegex();

    [GeneratedRegex(@"^(\d{1,2}-\d{1,2})(\s|$)")]
    private static partial Regex MissingYearRegex();

    [GeneratedRegex(@"^(\d{1,2}\s+\w{3})\s+(\d{1,2}\:\d{1,2}.*)$")]
    private static partial Regex MissingYearBeforeTimeRegex();
}
