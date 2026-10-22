using System.Globalization;
using System.Text.RegularExpressions;

namespace Modularr.Core.Indexers.Cardigann;

// Prowlarr DateTimeRoutines (Sergey Stoyan's fuzzy date parser): finds a date and/or a time anywhere in free text.
// Only the parts Cardigann uses are ported: the found local date/time, without the UTC offset bookkeeping.
public static partial class DateTimeRoutines
{
    public static bool TryParseDateOrTime(string text, bool ukDate, out DateTime result)
    {
        ArgumentNullException.ThrowIfNull(text);
        result = default;
        DateTime now = DateTime.Now;

        if (!TryParseDate(text, ukDate, now, out DateTime date, out int dateIndex, out int dateLength))
        {
            if (!TryParseTime(text, ukDate, -1, 0, out TimeSpan timeOnly))
            {
                return false;
            }

            result = DateTime.SpecifyKind(now.Date, DateTimeKind.Unspecified) + timeOnly;
            return true;
        }

        result = TryParseTime(text, ukDate, dateIndex, dateLength, out TimeSpan time) ? date + time : date;
        return true;
    }

    private static bool TryParseTime(string text, bool ukDate, int dateIndex, int dateLength, out TimeSpan time)
    {
        time = default;
        Match match;
        if (dateIndex > -1)
        {
            string after = text[(dateIndex + dateLength)..];
            match = TimeWithOffsetAfterDateRegex().Match(after);
            if (!match.Success)
            {
                match = (ukDate ? TimeAfterDateUkRegex() : TimeAfterDateUsRegex()).Match(after);
            }

            if (!match.Success)
            {
                match = (ukDate ? TimeBeforeDateUkRegex() : TimeBeforeDateUsRegex()).Match(text[..dateIndex]);
            }

            if (!match.Success)
            {
                match = (ukDate ? TimeBeforeDateUkRegex() : TimeBeforeDateUsRegex()).Match(text.Substring(dateIndex, dateLength));
            }
        }
        else
        {
            match = TimeWithOffsetRegex().Match(text);
            if (!match.Success)
            {
                match = (ukDate ? TimeAnywhereUkRegex() : TimeAnywhereUsRegex()).Match(text);
            }
        }

        if (!match.Success)
        {
            return false;
        }

        int hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        int minute = int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture);
        int second = match.Groups["second"].Success ? int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture) : 0;
        if (hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        string ampm = match.Groups["ampm"].Value;
        if (string.Equals(ampm, "PM", StringComparison.OrdinalIgnoreCase) && hour < 12)
        {
            hour += 12;
        }
        else if (string.Equals(ampm, "AM", StringComparison.OrdinalIgnoreCase) && hour == 12)
        {
            hour -= 12;
        }

        time = new TimeSpan(hour, minute, second);
        return true;
    }

    private static bool TryParseDate(string text, bool ukDate, DateTime now, out DateTime date, out int index, out int length)
    {
        date = default;
        index = -1;
        length = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // dd/mm/yy (US: mm/dd/yy)
        Match match = NumericDateRegex().Match(text);
        if (match.Success)
        {
            int first = Int(match, "day");
            int second = Int(match, "month");
            (int month, int day) = ukDate ? (second, first) : (first, second);
            return Found(match, ToDate(Int(match, "year"), month, day), out date, out index, out length);
        }

        // [yy]yy-mm-dd
        match = IsoDateRegex().Match(text);
        if (match.Success)
        {
            return Found(match, ToDate(Int(match, "year"), Int(match, "month"), Int(match, "day")), out date, out index, out length);
        }

        match = MonthDayYearRegex().Match(text);
        if (!match.Success)
        {
            match = DayMonthYearRegex().Match(text);
        }

        if (!match.Success)
        {
            match = YearMonthDayRegex().Match(text);
        }

        if (!match.Success)
        {
            match = MonthDayTimeZoneYearRegex().Match(text);
        }

        if (!match.Success)
        {
            match = MonthDayRegex().Match(text);
        }

        if (!match.Success)
        {
            return false;
        }

        // Upstream only knows these spellings (no "JUL").
        int monthNumber = match.Groups["month"].Value switch
        {
            "Jan" or "JAN" => 1,
            "Feb" or "FEB" => 2,
            "Mar" or "MAR" => 3,
            "Apr" or "APR" => 4,
            "May" or "MAY" => 5,
            "Jun" or "JUN" => 6,
            "Jul" => 7,
            "Aug" or "AUG" => 8,
            "Sep" or "SEP" => 9,
            "Oct" or "OCT" => 10,
            "Nov" or "NOV" => 11,
            "Dec" or "DEC" => 12,
            _ => -1,
        };

        int year = match.Groups["year"].Success && match.Groups["year"].Length > 0 ? Int(match, "year") : now.Year;
        return Found(match, ToDate(year, monthNumber, Int(match, "day")), out date, out index, out length);
    }

    private static bool Found(Match match, DateTime? parsed, out DateTime date, out int index, out int length)
    {
        date = parsed.GetValueOrDefault();
        index = match.Index;
        length = match.Length;
        return parsed is not null;
    }

    private static int Int(Match match, string group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    // Two-digit years: 31-99 -> 19xx, 00-30 -> 20xx; three-digit years are invalid.
    private static DateTime? ToDate(int year, int month, int day)
    {
        if (year is >= 100 and < 1000)
        {
            return null;
        }

        year = year switch
        {
            >= 100 => year,
            > 30 => year + 1900,
            _ => year + 2000,
        };

        return month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month) ? new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified) : null;
    }

    private const string Hms = @"(?'hour'\d{1,2})\s*:\s*(?'minute'\d{2})\s*(?::\s*(?'second'\d{2}))?(?:\s*(?'ampm'AM|am|PM|pm))?";
    private const string UsZone = @"(?:\s*(?'time_zone'UTC|GMT|CST|EST))?";
    private const string UkZone = @"(?:\s*(?'time_zone'UTC|GMT))?";
    private const string AfterDate = @"(?<=^\s*,?\s+|^\s*at\s*|^\s*[T\-]\s*)";
    private const string Offset = @"\s+(?'offset_sign'[\+\-])(?'offset_hh'\d{2}):?(?'offset_mm'\d{2})";
    private const string MonthName = "(?'month'Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[uarychilestmbro]*";

    [GeneratedRegex(AfterDate + @"(?'hour'\d{2})\s*:\s*(?'minute'\d{2})\s*:\s*(?'second'\d{2})" + Offset + @"(?=$|[^\d\w])")]
    private static partial Regex TimeWithOffsetAfterDateRegex();

    [GeneratedRegex(AfterDate + Hms + UsZone + @"(?=$|[^\d\w])")]
    private static partial Regex TimeAfterDateUsRegex();

    [GeneratedRegex(AfterDate + Hms + UkZone + @"(?=$|[^\d\w])")]
    private static partial Regex TimeAfterDateUkRegex();

    [GeneratedRegex(@"(?<=^|[^\d])" + Hms + UsZone + @"(?=$|[\s,]+)")]
    private static partial Regex TimeBeforeDateUsRegex();

    [GeneratedRegex(@"(?<=^|[^\d])" + Hms + UkZone + @"(?=$|[\s,]+)")]
    private static partial Regex TimeBeforeDateUkRegex();

    [GeneratedRegex(@"(?<=^|\s+|\s*T\s*)(?'hour'\d{2})\s*:\s*(?'minute'\d{2})\s*:\s*(?'second'\d{2})\s+(?'offset_sign'[\+\-])(?'offset_hh'\d{2}):?(?'offset_mm'\d{2})?(?=$|[^\d\w])")]
    private static partial Regex TimeWithOffsetRegex();

    [GeneratedRegex(@"(?<=^|\s+|\s*T\s*)" + Hms + UsZone + @"(?=$|[^\d\w])")]
    private static partial Regex TimeAnywhereUsRegex();

    [GeneratedRegex(@"(?<=^|\s+|\s*T\s*)" + Hms + UkZone + @"(?=$|[^\d\w])")]
    private static partial Regex TimeAnywhereUkRegex();

    [GeneratedRegex(@"(?<=^|[^\d])(?'day'\d{1,2})\s*(?'separator'[\\/\.])+\s*(?'month'\d{1,2})\s*\'separator'+\s*(?'year'\d{2}|\d{4})(?=$|[^\d])", RegexOptions.IgnoreCase)]
    private static partial Regex NumericDateRegex();

    [GeneratedRegex(@"(?<=^|[^\d])(?'year'\d{2}|\d{4})\s*(?'separator'[\-])\s*(?'month'\d{1,2})\s*\'separator'+\s*(?'day'\d{1,2})(?=$|[^\d])", RegexOptions.IgnoreCase)]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"(?:^|[^\d\w])" + MonthName + @"\s+(?'day'\d{1,2})(?:-?st|-?th|-?rd|-?nd)?\s*,?\s*(?'year'\d{4})(?=$|[^\d\w])", RegexOptions.IgnoreCase)]
    private static partial Regex MonthDayYearRegex();

    [GeneratedRegex(@"(?:^|[^\d\w:])(?'day'\d{1,2})(?:-?st\s+|-?th\s+|-?rd\s+|-?nd\s+|-|\s+)" + MonthName + @"(?:\s*,?\s*|-)'?(?'year'\d{2}|\d{4})(?=$|[^\d\w])", RegexOptions.IgnoreCase)]
    private static partial Regex DayMonthYearRegex();

    [GeneratedRegex(@"(?:^|[^\d\w])(?'year'\d{4})\s+" + MonthName + @"\s+(?'day'\d{1,2})(?:-?st|-?th|-?rd|-?nd)?(?=$|[^\d\w])", RegexOptions.IgnoreCase)]
    private static partial Regex YearMonthDayRegex();

    [GeneratedRegex(@"(?:^|[^\d\w])" + MonthName + @"\s+(?'day'\d{1,2})\s+\d{2}\:\d{2}\:\d{2}\s+(?:MDT|UTC)\s+(?'year'\d{4})(?=$|[^\d\w])", RegexOptions.IgnoreCase)]
    private static partial Regex MonthDayTimeZoneYearRegex();

    [GeneratedRegex(@"(?:^|[^\d\w])" + MonthName + @"\s+(?'day'\d{1,2})(?:-?st|-?th|-?rd|-?nd)?(?:\s*,?\s*(?'year'\d{4}))?(?=$|[^\d\w])", RegexOptions.IgnoreCase)]
    private static partial Regex MonthDayRegex();
}
