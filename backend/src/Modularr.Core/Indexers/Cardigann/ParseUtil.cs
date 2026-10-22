using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace Modularr.Core.Indexers.Cardigann;

// Prowlarr ParseUtil: lenient number and size parsing for scraped values ("1,023.4 MB", "1000 grabs", "2.222").
public static partial class ParseUtil
{
    public static double CoerceDouble(string? value) => double.Parse(NormalizeNumber(value, isInt: false), NumberStyles.Any, CultureInfo.InvariantCulture);

    public static float CoerceFloat(string? value) => float.Parse(NormalizeNumber(value, isInt: false), NumberStyles.Any, CultureInfo.InvariantCulture);

    public static int CoerceInt(string? value) => int.Parse(NormalizeNumber(value, isInt: true), NumberStyles.Any, CultureInfo.InvariantCulture);

    public static long CoerceLong(string? value) => long.Parse(NormalizeNumber(value, isInt: true), NumberStyles.Any, CultureInfo.InvariantCulture);

    // First run of digits in the string ("asdf123asdf456" -> 123).
    public static long? GetLongFromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Match digits = FirstDigitsRegex().Match(value);
        return CoerceLong(digits.Value);
    }

    public static string? GetFullImdbId(string? value)
    {
        Match match = value is null ? Match.Empty : ImdbIdRegex().Match(value);
        int id = match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return id == 0 ? null : $"tt{id:D7}";
    }

    public static string? GetArgumentFromQueryString(string? url, string? argument)
    {
        if (url is null || argument is null)
        {
            return null;
        }

        string query = url.Split('?', 2)[1].Split('#', 2)[0];
        return QueryHelpers.ParseQuery(query).TryGetValue(argument, out Microsoft.Extensions.Primitives.StringValues values) ? values.FirstOrDefault() : null;
    }

    // Binary units (1 KB = 1024 B); a bare number is bytes.
    public static long GetBytes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string unit = new string([.. value.Where(char.IsLetter)]).Replace("i", "", StringComparison.Ordinal).ToLowerInvariant();
        double number = CoerceDouble(value);
        // Multiplied step by step like upstream, so float rounding matches.
        double bytes = unit switch
        {
            _ when unit.Contains("kb", StringComparison.Ordinal) => number * 1024d,
            _ when unit.Contains("mb", StringComparison.Ordinal) => number * 1024d * 1024d,
            _ when unit.Contains("gb", StringComparison.Ordinal) => number * 1024d * 1024d * 1024d,
            _ when unit.Contains("tb", StringComparison.Ordinal) => number * 1024d * 1024d * 1024d * 1024d,
            _ => number,
        };

        return (long)bytes;
    }

    // Keeps digits and separators. Ints treat '.' as a thousands separator; decimals keep only the last '.' or ',' as the point.
    private static string NormalizeNumber(string? value, bool isInt)
    {
        string number = new string([.. (value ?? "").Where(c => char.IsDigit(c) || c is '.' or ',')]).Trim();

        if (isInt)
        {
            return number.Contains(',', StringComparison.Ordinal) && number.Contains('.', StringComparison.Ordinal)
                ? number
                : number.Length == 0 ? "0" : number.Replace('.', ',');
        }

        number = number.Length == 0 ? "0" : number.Replace(',', '.');
        int lastPoint = number.LastIndexOf('.');
        return number.Count(c => c == '.') > 1 ? number[..lastPoint].Replace(".", "", StringComparison.Ordinal) + number[lastPoint..] : number;
    }

    [GeneratedRegex("[0-9]+")]
    private static partial Regex FirstDigitsRegex();

    [GeneratedRegex(@"^(?:tt)?(\d{1,8})$")]
    private static partial Regex ImdbIdRegex();
}
